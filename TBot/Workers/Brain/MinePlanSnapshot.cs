using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tbot.Common.IO;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public static class MinePlanSnapshot {
		private const int LookaheadSteps = 10;
		private const int PerMineSteps = 5;
		private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(2);
		private static readonly Dictionary<string, DateTime> LastWrite = new();
		private static readonly object Gate = new();
		private static readonly object PlanGate = new();
		private static readonly Dictionary<string, (DateTime At, List<PlanStep> Steps)> LastPlans = new();

		private static string LfbDiagText = "";

		private static readonly Buildables[] Mines = { Buildables.MetalMine, Buildables.CrystalMine, Buildables.DeuteriumSynthesizer };

		public static void TryWrite(ITBotMain inst, ICalculationService calc, IOgameService ogame, ICollection<Celestial> excluded) {
			try {
				string alias = inst.InstanceAlias;
				lock (Gate) {
					if (LastWrite.TryGetValue(alias, out var last) && DateTime.UtcNow - last < MinInterval)
						return;
					LastWrite[alias] = DateTime.UtcNow;
				}
				var planets = inst.UserData.celestials.OfType<Planet>().ToList();
				if (planets.Count == 0 || inst.UserData.researches == null || inst.UserData.serverData == null)
					return;

				StartItemRefresh(inst, ogame, planets);

				var plans = new List<PlanetPlan>();
				foreach (var p in planets) {
					var plan = BuildPlanet(inst, calc, p, excluded != null && excluded.Any(e => e.ID == p.ID));
					if (plan != null) {
						AttachItems(plan, p.ID);
						plans.Add(plan);
					}
				}
				if (plans.Count == 0)
					return;

				var candidates = plans.Where(pl => !pl.Excluded)
					.SelectMany(pl => pl.Next.Where(n => n.RoiDays > 0).Select(n => new BestStep {
						Planet = pl.Name, Coord = pl.Coord, Mine = n.Mine, Level = n.Level, RoiDays = n.RoiDays,
						CostDeut = n.CostDeut, GainPerDay = n.GainPerDay, EnergyShort = n.EnergyShort
					})).ToList();
				ResearchPlanCfg researchCfg = ReadResearchPlanCfg(inst);
				var research = BuildResearch(calc, planets, inst.UserData.researches, inst.UserData.serverData.Speed, inst.UserData.userInfo.Class,
					inst.UserData.staff.Geologist, inst.UserData.staff.IsFull, researchCfg);
				candidates.AddRange(research.Where(r => r.GainPerDay > 0).Select(r => new BestStep {
					Planet = "Account", Coord = "", Mine = r.Name, Level = r.Level, RoiDays = r.RoiDays,
					CostDeut = r.CostDeut, GainPerDay = r.GainPerDay, IsResearch = true
				}));

				RankConfig rank = ReadRankConfig(inst);
				var best = MinePlanRanking.Rank(candidates, c => c.CostDeut, c => c.GainPerDay, c => c.IsResearch, rank, 12);
				Tbot.Workers.ExtraLog.Write(inst, "MinePlanRanking", () => $"mode={rank.Mode}, horizon={rank.HorizonDays}d, maxPayback={rank.MaxPaybackDays}d, onlySensible={rank.OnlySensible}, research={rank.IncludeResearch}: {candidates.Count} candidates, top: " + string.Join(" | ", best.Take(5).Select(b => $"{b.Planet} {b.Mine} {b.Level} cost={b.CostDeut:N0} gain/d={b.GainPerDay:N0} payback={b.RoiDays:F1}d")));

				var buildPlan = BuildGlobalPlan(calc, planets, plans.Where(pl => pl.Excluded).Select(pl => pl.Name).ToHashSet(), inst.UserData.researches,
					inst.UserData.serverData.Speed, inst.UserData.userInfo.Class, inst.UserData.staff.Geologist, inst.UserData.staff.IsFull, rank,
					new LfEffectsStore(LfEffectsStore.PathFor(alias)), MineCapsOf(inst, planets), LfCapOf(inst), LfPrioOf(inst), LfbCapOf(inst), researchCfg);

				string diag = "";
				try {
					int lfPlanets = planets.Count(pl => pl.LFBuildings != null && (int) pl.LFtype >= 1 && (int) pl.LFtype <= 4);
					int lfRows = new LfEffectsStore(LfEffectsStore.PathFor(alias)).CurrentRows().Count;
					diag = $"LF data loaded for {lfPlanets} of {planets.Count} planets, {lfRows} effect-table rows, payback limit {rank.MaxPaybackDays} days, research included: {(rank.IncludeResearch ? "yes" : "no")}.";
					var lfsDiag = new LfEffectsStore(LfEffectsStore.PathFor(alias));
					var capDiag = LfbCapOf(inst);
					int started = 0, withTable = 0, belowCap = 0, noTargets = 0;
					foreach (var pl in planets) {
						int rasse = (int) pl.LFtype;
						if (rasse < 1 || rasse > 4 || pl.LFBuildings == null) continue;
						foreach (var b in LfbBoost.Keys.Where(x => (int) x / 1000 == 10 + rasse)) {
							int cur; try { cur = pl.GetLevel(b); } catch { continue; }
							if (cur < 1) continue;
							started++;
							if (lfsDiag.TableValue((int) b, cur + 1, LfbBoost[b][0].Item2) != null) withTable++;
							int cap = capDiag(pl, b);
							if (cap == 0) noTargets++; else if (cur + 1 <= cap) belowCap++;
						}
					}
					diag = $"LF buildings with resource bonus already started: {started}, with level table: {withTable}, below target: {belowCap}, no target values: {noTargets}. First-step calculation: {(string.IsNullOrEmpty(LfbDiagText) ? "none" : LfbDiagText)}. " + diag;
				} catch { }
				lock (PlanGate) {
					LastPlans[alias] = (DateTime.UtcNow, buildPlan);
				}

				var file = new PlanFile {
					UpdatedUtc = DateTime.UtcNow.ToString("o"),
					BudgetDeutPerDay = ReadBudget(inst),
					Expo = ReadExpoSummary(alias),
					BuildPlan = buildPlan,
					Speed = inst.UserData.serverData.Speed,
					Planets = plans,
					Best = best,
					Candidates = candidates,
					Research = research,
					Live = ReadLive(inst, buildPlan),
					Ranking = new RankInfo { Mode = rank.Mode.ToString(), HorizonDays = rank.HorizonDays, MaxPaybackDays = rank.MaxPaybackDays, OnlySensible = rank.OnlySensible, IncludeResearch = rank.IncludeResearch }
				};
				string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dataDir);
				string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				AtomicFile.WriteAllText(Path.Combine(dataDir, $"minenplan_{safe}.json"), JsonConvert.SerializeObject(file, Formatting.Indented));
			} catch {
			}
		}

		private static Tbot.Services.ExpoFindsStore.Summary ReadExpoSummary(string alias) {
			try {
				string path = Tbot.Services.ExpoFindsStore.PathFor(alias);
				if (!File.Exists(path))
					return null;
				var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
				return o["Summary"]?.ToObject<Tbot.Services.ExpoFindsStore.Summary>();
			} catch {
				return null;
			}
		}

		public static bool ShouldHold(ITBotMain inst, string planetName, Buildables buildable, out string reason) {
			reason = "";
			try {
				if (buildable != Buildables.MetalMine && buildable != Buildables.CrystalMine && buildable != Buildables.DeuteriumSynthesizer)
					return false;
				var g = inst.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking"))
					return false;
				var r = g.Experimental.MinePlanRanking;
				if (!SettingsService.IsSettingSet(r, "UsePlanForAutoMine") || !(bool) r.UsePlanForAutoMine)
					return false;
				int lookahead = SettingsService.IsSettingSet(r, "PlanLookahead") ? Math.Max(1, (int) r.PlanLookahead) : 12;
				List<PlanStep> steps;
				DateTime at;
				lock (PlanGate) {
					if (!LastPlans.TryGetValue(inst.InstanceAlias, out var e))
						return false;
					(at, steps) = e;
				}
				if (steps == null || steps.Count == 0 || DateTime.UtcNow - at > TimeSpan.FromHours(6))
					return false;
				var first = steps.Where(x => !x.IsResearch).Take(lookahead).ToList();
				if (first.Any(s => !s.IsResearch && s.Planet == planetName))
					return false;
				var top = first.FirstOrDefault(s => !s.IsResearch);
				reason = top == null ? "no planet step in the plan" : $"the plan builds first: {top.Planet} {top.What} {top.Level}";
				return true;
			} catch {
				return false;
			}
		}

		public static bool TryPlanPick(ITBotMain inst, string planetName, out Buildables mine, out int level, out float roiDays) {
			mine = Buildables.Null; level = 0; roiDays = 0;
			try {
				var g = inst.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking"))
					return false;
				var r = g.Experimental.MinePlanRanking;
				if (!SettingsService.IsSettingSet(r, "UsePlanForAutoMine") || !(bool) r.UsePlanForAutoMine)
					return false;
				int lookahead = SettingsService.IsSettingSet(r, "PlanLookahead") ? Math.Max(1, (int) r.PlanLookahead) : 12;
				List<PlanStep> steps;
				DateTime at;
				lock (PlanGate) {
					if (!LastPlans.TryGetValue(inst.InstanceAlias, out var e))
						return false;
					(at, steps) = e;
				}
				if (steps == null || steps.Count == 0 || DateTime.UtcNow - at > TimeSpan.FromHours(6))
					return false;
				var step = steps.Where(x => !x.IsResearch).Take(lookahead).FirstOrDefault(x => x.Planet == planetName);
				if (step == null)
					return false;
				mine = step.What.StartsWith("Metal") ? Buildables.MetalMine : step.What.StartsWith("Crystal") ? Buildables.CrystalMine : step.What.StartsWith("Deuterium") ? Buildables.DeuteriumSynthesizer : Buildables.Null;
				level = step.Level;
				roiDays = step.RoiDays;
				return mine != Buildables.Null;
			} catch {
				return false;
			}
		}

		public static bool TryPlanPickLf(ITBotMain inst, string planetName, out LFBuildables building, out int level) {
			building = LFBuildables.None; level = 0;
			try {
				var g = inst.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking"))
					return false;
				var r = g.Experimental.MinePlanRanking;
				if (!SettingsService.IsSettingSet(r, "UsePlanForAutoMine") || !(bool) r.UsePlanForAutoMine)
					return false;
				int lookahead = SettingsService.IsSettingSet(r, "PlanLookahead") ? Math.Max(1, (int) r.PlanLookahead) : 12;
				List<PlanStep> steps;
				DateTime at;
				lock (PlanGate) {
					if (!LastPlans.TryGetValue(inst.InstanceAlias, out var e))
						return false;
					(at, steps) = e;
				}
				if (steps == null || steps.Count == 0 || DateTime.UtcNow - at > TimeSpan.FromHours(6))
					return false;
				var step = steps.Where(x => x.IsLfBuilding).Take(lookahead).FirstOrDefault(x => x.Planet == planetName);
				if (step == null)
					return false;
				string name = step.What.Replace("LF building:", "").Trim();
				int sp = name.IndexOf(' ');
				if (sp > 0) name = name.Substring(0, sp);
				if (!Enum.TryParse(name, out LFBuildables b) || b == LFBuildables.None)
					return false;
				building = b;
				level = step.Level;
				return true;
			} catch {
				return false;
			}
		}

		private static LiveInfo ReadLive(ITBotMain inst, List<PlanStep> plan) {
			var li = new LiveInfo { Active = false, Lookahead = 12, Planets = new List<string>(), Next = new List<PlanStep>() };
			try {
				var g = inst.InstanceSettings;
				if (SettingsService.IsSettingSet(g, "Experimental") && SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking")) {
					var r = g.Experimental.MinePlanRanking;
					li.Active = SettingsService.IsSettingSet(r, "UsePlanForAutoMine") && (bool) r.UsePlanForAutoMine;
					if (SettingsService.IsSettingSet(r, "PlanLookahead"))
						li.Lookahead = Math.Max(1, (int) r.PlanLookahead);
				}
				if (plan != null) {
					li.Next = plan.Where(x => !x.IsResearch).Take(li.Lookahead).ToList();
					li.Planets = li.Next.Where(s => !s.IsResearch).Select(s => s.Planet).Distinct().ToList();
				}
			} catch {
			}
			return li;
		}

		private static long ReadBudget(ITBotMain inst) {
			try {
				var g = inst.InstanceSettings;
				if (SettingsService.IsSettingSet(g, "Experimental") && SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking")
					&& SettingsService.IsSettingSet(g.Experimental.MinePlanRanking, "DailyBudgetDeut"))
					return Math.Max(0L, (long) g.Experimental.MinePlanRanking.DailyBudgetDeut);
			} catch {
			}
			return 0;
		}

		private static List<PlanStep> BuildGlobalPlan(ICalculationService calc, List<Planet> planets, HashSet<string> excludedNames, Researches r, int speed,
			CharacterClass cls, bool geo, bool staff, RankConfig rank, LfEffectsStore lfStore = null, Dictionary<int, int[]> caps = null, Func<string, int> lfCap = null, List<int> lfPrio = null, Func<Planet, LFBuildables, int> lfbCap = null, ResearchPlanCfg rc = null) {
			var result = new List<PlanStep>();
			try {
				var ps = planets.Where(p => p.Buildings != null && p.Coordinate != null && p.Temperature != null && !excludedNames.Contains(p.ToString())).ToList();
				if (ps.Count == 0)
					return result;
				int n = ps.Count;
				var lv = new int[n][];
				var en = new long[n];
				var extras = new float[n][];
				for (int i = 0; i < n; i++) {
					lv[i] = new[] { ps[i].Buildings.MetalMine, ps[i].Buildings.CrystalMine, ps[i].Buildings.DeuteriumSynthesizer };
					en[i] = ps[i].Resources?.Energy ?? 0;
					extras[i] = new float[3];
					for (int k = 0; k < 3; k++)
						extras[i][k] = ExtraBonus(calc, Mines[k], ps[i], r, speed, cls, geo, staff, out _);
				}
				var lfRows = new List<LfEffectsStore.Row>[n];
				var lfLevel = new Dictionary<(int, int), int>();
				for (int i = 0; i < n; i++) {
					lfRows[i] = new List<LfEffectsStore.Row>();
					if (lfStore == null || !rank.IncludeResearch) continue;
					foreach (var row in lfStore.CurrentRows())
						if (row.Category == "resources" && row.CelestialId == ps[i].ID && Enum.IsDefined(typeof(LFTechno), row.TechId) && MineOfTarget(row.Target) >= 0) {
							lfRows[i].Add(row);
							lfLevel[(i, row.TechId)] = row.Level;
						}
				}
				var lfAll = new List<LfEffectsStore.Row>[n];
				for (int i = 0; i < n; i++) {
					lfAll[i] = new List<LfEffectsStore.Row>();
					if (lfStore == null || lfPrio == null || lfPrio.Count == 0) continue;
					lfAll[i] = lfStore.CurrentRows().Where(x => x.CelestialId == ps[i].ID && Enum.IsDefined(typeof(LFTechno), x.TechId)).GroupBy(x => x.TechId).Select(g => g.First()).ToList();
				}
				var lfbLevel = new Dictionary<(int, LFBuildables), int>();
				var lfbOpts = new List<LFBuildables>[n];
				for (int i = 0; i < n; i++) {
					lfbOpts[i] = new List<LFBuildables>();
					if (lfStore == null || lfbCap == null) continue;
					int rasse = (int) ps[i].LFtype;
					if (rasse < 1 || rasse > 4 || ps[i].LFBuildings == null) continue;
					foreach (var b in LfbBoost.Keys.Where(x => (int) x / 1000 == 10 + rasse)) {
						int cur = 0;
						try { cur = ps[i].GetLevel(b); } catch { continue; }
						if (cur < 1) continue;
						lfbLevel[(i, b)] = cur;
						lfbOpts[i].Add(b);
					}
				}
				var lfbDg = new List<string>();
				bool cheap = rank.Mode == RankMode.Cheapest;
				Func<float, bool> roiOk = roi => roi > 0 && ((cheap && !rank.OnlySensible) || roi <= rank.MaxPaybackDays);
				Func<long, float, PlanStep, bool> better = (cst, roi, cur) => cur == null || (cheap ? (cst < cur.CostDeut || (cst == cur.CostDeut && roi < cur.RoiDays)) : roi < cur.RoiDays);
				int plasma = r.PlasmaTechnology;
				int astro = r.Astrophysics;
				Func<(long cost, float gain, float roi)> plasmaNext = () => {
					float g = 0;
					for (int i = 0; i < n; i++)
						for (int k = 0; k < 3; k++)
							if (lv[i][k] > 0)
								g += ToDayDeut(Mines[k], HourProdPlasma(calc, Mines[k], lv[i][k], ps[i], plasma + 1, speed, cls, geo, staff) - HourProdPlasma(calc, Mines[k], lv[i][k], ps[i], plasma, speed, cls, geo, staff));
					long c = PlasmaCost(calc, planets, plasma + 1);
					return (c, g, MinePlanRanking.PaybackDays(c, g));
				};
				try {
					for (int no = 1; no <= MaxPlanSteps; no++) {
						_plasmaSim = plasma;
						if (rank.IncludeResearch && rc != null) {
							PlanStep forced = null;
							if (ForcedResearch(rc, plasma, astro) == 1) {
								var pn = plasmaNext();
								forced = new PlanStep { Planet = "Account", Coord = "", What = "Plasma technology", Level = plasma + 1, CostDeut = pn.cost, GainPerDay = pn.gain, RoiDays = pn.roi, IsResearch = true };
								plasma++;
							} else if (ForcedResearch(rc, plasma, astro) == 2) {
								forced = new PlanStep { Planet = "Account", Coord = "", What = "Astrophysics", Level = astro + 1, CostDeut = CostDeut(calc.CalcPrice(Buildables.Astrophysics, astro + 1)), GainPerDay = 0, RoiDays = 0, IsResearch = true };
								astro++;
							}
							if (forced != null) {
								if (forced.CostDeut >= PlanCostLimit) break;
								forced.No = no;
								result.Add(forced);
								continue;
							}
						}
						PlanStep best = null;
						int bi = -1, bk = -1;
						StepInfo bs = null;
						for (int i = 0; i < n; i++) {
							for (int k = 0; k < 3; k++) {
								if (caps != null && caps.TryGetValue(ps[i].ID, out var cp) && lv[i][k] + 1 > cp[k])
									continue;
								var st = Step(calc, ps[i], Mines[k], lv[i][k] + 1, en[i], r, speed, cls, geo, staff, extras[i][k]);
								if (!roiOk(st.RoiDays))
									continue;
								if (cheap ? better(st.CostDeut, st.RoiDays, best) : (best == null || st.RoiDays < best.RoiDays || (st.RoiDays == best.RoiDays && st.CostDeut < best.CostDeut))) {
									best = new PlanStep { Planet = ps[i].ToString(), Coord = ps[i].Coordinate.ToString(), What = MineName(Mines[k]) + " mine", Level = st.Level, CostDeut = st.CostDeut, GainPerDay = st.GainPerDay, RoiDays = st.RoiDays, EnergyShort = st.EnergyShort };
									bi = i; bk = k; bs = st;
								}
							}
						}
						if (rank.IncludeResearch && PlasmaAllowed(rc, plasma)) {
							var pnx = plasmaNext();
							long cost = pnx.cost; float gain = pnx.gain, roi = pnx.roi;
							if (no == 1) lfbDg.Add($"Plasma technology L{plasma + 1}: cost {cost:N0}, gain/day {gain:N0}, payback {roi:F0} d");
							if (roiOk(roi) && better(cost, roi, best)) {
								best = new PlanStep { Planet = "Account", Coord = "", What = "Plasma technology", Level = plasma + 1, CostDeut = cost, GainPerDay = gain, RoiDays = roi, IsResearch = true };
								bi = -1; bs = null;
							}
						}
						int lfi = -1, lft = 0;
						for (int i = 0; i < n && rank.IncludeResearch; i++) {
							HashSet<string> erlaubt = null;
							if (lfPrio != null && lfPrio.Count > 0 && lfCap != null) {
								foreach (int slot in lfPrio) {
									if (!Tbot.Workers.Brain.LifeformsAutoResearchWorker.PrioSlotTechNames.TryGetValue(slot, out var slotNamen))
										continue;
									bool offen = lfAll[i].Any(x => {
										string nm = ((LFTechno) x.TechId).ToString();
										int lvNow = lfLevel.TryGetValue((i, x.TechId), out int l0) ? l0 : x.Level;
										return slotNamen.Contains(nm) && lvNow < lfCap(nm);
									});
									if (offen) { erlaubt = new HashSet<string>(slotNamen); break; }
								}
							}
							foreach (var row in lfRows[i]) {
								int k = MineOfTarget(row.Target);
								int cur = lfLevel[(i, row.TechId)];
								string techName = ((LFTechno) row.TechId).ToString();
								if (lfCap != null && cur + 1 > lfCap(techName))
									continue;
								if (erlaubt != null && !erlaubt.Contains(techName))
									continue;
								if (lv[i][k] <= 0) continue;
								double delta = lfStore.NextLevelDelta(row.TechId, "resources", row.Target, cur) / 100.0;
								if (delta <= 0) continue;
								float lfGain = ToDayDeut(Mines[k], (float) delta * HourProd(calc, Mines[k], lv[i][k], ps[i], r, speed, cls, geo, staff, false));
								long lfCost = calc.CalcPrice((LFTechno) row.TechId, cur + 1).ConvertedDeuterium;
								float lfRoi = MinePlanRanking.PaybackDays(lfCost, lfGain);
								if (roiOk(lfRoi) && better(lfCost, lfRoi, best)) {
									best = new PlanStep { Planet = ps[i].ToString(), Coord = ps[i].Coordinate.ToString(), What = "LF: " + ((LFTechno) row.TechId) + " (" + MineName(Mines[k]) + ")", Level = cur + 1, CostDeut = lfCost, GainPerDay = lfGain, RoiDays = lfRoi, IsResearch = true };
									bi = -1; bs = null; lfi = i; lft = row.TechId;
								}
							}
						}
						int lfbI = -1, lfbDeltaE = 0;
						LFBuildables lfbB = LFBuildables.None;
						for (int i = 0; i < n; i++) {
							foreach (var b in lfbOpts[i]) {
								int cur = lfbLevel[(i, b)], nxt = cur + 1;
								if (nxt > lfbCap(ps[i], b))
									continue;
								float lfbGain = 0;
								bool vorhanden = true;
								foreach (var (k, col) in LfbBoost[b]) {
									double? vn = lfStore.TableValue((int) b, nxt, col), vc = cur <= 0 ? 0 : lfStore.TableValue((int) b, cur, col);
									if (vn == null || vc == null) { vorhanden = false; break; }
									if (lv[i][k] <= 0) continue;
									lfbGain += ToDayDeut(Mines[k], (float) ((vn.Value - vc.Value) / 100.0) * HourProd(calc, Mines[k], lv[i][k], ps[i], r, speed, cls, geo, staff, false));
								}
								if (no == 1) {
									if (!vorhanden) lfbDg.Add($"{ps[i].Name} {b} L{nxt}: level table incomplete");
									else if (lfbGain <= 0) lfbDg.Add($"{ps[i].Name} {b} L{nxt}: no gain (mine level 0 or no bonus step)");
								}
								if (!vorhanden || lfbGain <= 0) continue;
								long lfbCost = calc.CalcPrice(b, nxt).ConvertedDeuterium;
								float lfbRoi = MinePlanRanking.PaybackDays(lfbCost, lfbGain);
								if (no == 1) lfbDg.Add($"{ps[i].Name} {b} L{nxt}: cost {lfbCost:N0}, gain/day {lfbGain:N0}, payback {lfbRoi:F0} d");
								if (roiOk(lfbRoi) && better(lfbCost, lfbRoi, best)) {
									double? en1 = lfStore.TableValue((int) b, nxt, "energy_consumption"), en0 = cur <= 0 ? 0 : lfStore.TableValue((int) b, cur, "energy_consumption");
									int dE = (en1.HasValue && en0.HasValue) ? (int) Math.Round(en1.Value - en0.Value) : 0;
									long nachher = en[i] + dE;
									best = new PlanStep { Planet = ps[i].ToString(), Coord = ps[i].Coordinate.ToString(), What = "LF building: " + b + " (" + string.Join("/", LfbBoost[b].Select(x => MineName(Mines[x.Item1]))) + ")", Level = nxt, CostDeut = lfbCost, GainPerDay = lfbGain, RoiDays = lfbRoi, IsResearch = true, IsLfBuilding = true, EnergyShort = nachher < 0 ? -nachher : 0 };
									bi = -1; bs = null; lfi = -1; lfbI = i; lfbB = b; lfbDeltaE = dE;
								}
							}
						}
						if (best == null || best.CostDeut >= PlanCostLimit)
							break;
						best.No = no;
						if (lfbI >= 0) {
							lfbLevel[(lfbI, lfbB)]++;
							en[lfbI] += lfbDeltaE;
							result.Add(best);
							continue;
						}
						if (lfi >= 0)
							lfLevel[(lfi, lft)]++;
						else if (best.IsResearch)
							plasma++;
						else {
							lv[bi][bk]++;
							en[bi] = bs.EnergyAfter;
						}
						result.Add(best);
					}
				} finally {
					_plasmaSim = 0;
					LfbDiagText = string.Join(" | ", lfbDg.Take(10));
				}
			} catch {
			}
			return result;
		}

		private static Dictionary<int, int[]> MineCapsOf(ITBotMain inst, List<Planet> planets) {
			var result = new Dictionary<int, int[]>();
			try {
				dynamic am = inst.InstanceSettings.Brain.AutoMine;
				foreach (var cel in planets) {
					try {
						bool home = cel.Coordinate.Galaxy == (int) am.HomeWorld.Galaxy && cel.Coordinate.System == (int) am.HomeWorld.System
							&& cel.Coordinate.Position == (int) am.HomeWorld.Position && cel.Coordinate.Type == Enum.Parse<Celestials>((string) am.HomeWorld.Type);
						int[] c = { (int) am.MaxMetalMine, (int) am.MaxCrystalMine, (int) am.MaxDeuteriumSynthetizer };
						if (!home && Tbot.Helpers.AutoMineOverrideHelper.IsColonyConfigured(am)) {
							c[0] = (int) am.Colony.MaxMetalMine; c[1] = (int) am.Colony.MaxCrystalMine; c[2] = (int) am.Colony.MaxDeuteriumSynthetizer;
						}
						if (!home) {
							dynamic ov = Tbot.Helpers.AutoMineOverrideHelper.FindOverride(cel, am);
							if (ov != null) {
								c[0] = Tbot.Helpers.AutoMineOverrideHelper.Resolve(ov, am, "MaxMetalMine", c[0]);
								c[1] = Tbot.Helpers.AutoMineOverrideHelper.Resolve(ov, am, "MaxCrystalMine", c[1]);
								c[2] = Tbot.Helpers.AutoMineOverrideHelper.Resolve(ov, am, "MaxDeuteriumSynthetizer", c[2]);
							}
						}
						result[cel.ID] = c;
					} catch {  }
				}
			} catch { }
			return result;
		}

		private static readonly Dictionary<LFBuildables, (int, string)[]> LfbBoost = new() {
			{ LFBuildables.HighEnergySmelting, new[] { (0, "metal_boost") } },
			{ LFBuildables.FusionPoweredProduction, new[] { (1, "crystal_boost"), (2, "deuterium_boost") } },
			{ LFBuildables.MagmaForge, new[] { (0, "metal_boost") } },
			{ LFBuildables.CrystalRefinery, new[] { (1, "crystal_boost") } },
			{ LFBuildables.DeuteriumSynthesiser, new[] { (2, "deuterium_boost") } },
			{ LFBuildables.HighPerformanceSynthesiser, new[] { (2, "deuterium_boost") } }
		};

		private static Func<Planet, LFBuildables, int> LfbCapOf(ITBotMain inst) {
			var cache = new Dictionary<int, LFBuildings>();
			return (planet, b) => {
				try {
					if (!cache.TryGetValue(planet.ID, out var max)) {
						max = new LFBuildings();
						object lfSettings = (object) inst.InstanceSettings.Brain.LifeformAutoMine;
						if (!Tbot.Helpers.LifeformTargets.TryApplyMaxLevels(lfSettings, planet, max, (l, m) => { }))
							max = null;
						cache[planet.ID] = max;
					}
					if (max == null)
						return 0;
					var prop = max.GetType().GetProperty(b.ToString());
					return prop == null ? 0 : (int) prop.GetValue(max);
				} catch {
					return 0;
				}
			};
		}

		private static Func<string, int> LfCapOf(ITBotMain inst) {
			try {
				dynamic lf = inst.InstanceSettings.Brain.LifeformAutoResearch;
				int maxLevel = SettingsService.IsSettingSet(lf, "MaxResearchLevel") ? (int) lf.MaxResearchLevel : 1;
				var perName = new Dictionary<string, int>();
				foreach (var kv in Tbot.Workers.Brain.LifeformsAutoResearchWorker.PrioSlotTechNames) {
					string key = "MaxTechs" + kv.Key;
					int v = SettingsService.IsSettingSet(lf, key) ? (int) lf[key] : maxLevel;
					foreach (string nm in kv.Value)
						perName[nm] = v;
				}
				return nm => perName.TryGetValue(nm, out int c) ? c : maxLevel;
			} catch {
				return null;
			}
		}

		private static List<int> LfPrioOf(ITBotMain inst) {
			var res = new List<int>();
			try {
				dynamic lf = inst.InstanceSettings.Brain.LifeformAutoResearch;
				if (SettingsService.IsSettingSet(lf, "PriorityTechs"))
					foreach (var e in lf.PriorityTechs) {
						int s = (int) e;
						if (!res.Contains(s)) res.Add(s);
					}
			} catch {
			}
			return res;
		}

		private static int MineOfTarget(string target) => target == "metal" ? 0 : target == "crystal" ? 1 : target == "deuterium" ? 2 : -1;

		internal const int MaxPlanSteps = 50;

		internal const long PlanCostLimit = 1_000_000_000_000_000L;

		internal sealed class ResearchPlanCfg {
			public int MaxPlasma = int.MaxValue, MaxAstro = 0;
			public bool PrioPlasma, PrioAstro;
		}

		internal static bool PlasmaAllowed(ResearchPlanCfg rc, int plasma) => rc == null || plasma + 1 <= rc.MaxPlasma;

		internal static int ForcedResearch(ResearchPlanCfg rc, int plasma, int astro) {
			if (rc == null) return 0;
			if (rc.PrioPlasma && plasma + 1 <= rc.MaxPlasma) return 1;
			if (rc.PrioAstro && astro + 1 <= rc.MaxAstro) return 2;
			return 0;
		}

		private static ResearchPlanCfg ReadResearchPlanCfg(ITBotMain inst) {
			var c = new ResearchPlanCfg();
			try {
				dynamic ar = inst.InstanceSettings.Brain.AutoResearch;
				if (SettingsService.IsSettingSet(ar, "MaxPlasmaTechnology")) c.MaxPlasma = (int) ar.MaxPlasmaTechnology;
				if (SettingsService.IsSettingSet(ar, "MaxAstrophysics")) c.MaxAstro = (int) ar.MaxAstrophysics;
				if (SettingsService.IsSettingSet(ar, "PrioritizePlasmaTechnology")) c.PrioPlasma = (bool) ar.PrioritizePlasmaTechnology;
				if (SettingsService.IsSettingSet(ar, "PrioritizeAstrophysics")) c.PrioAstro = (bool) ar.PrioritizeAstrophysics;
			} catch {
			}
			return c;
		}

		private static RankConfig ReadRankConfig(ITBotMain inst) {
			RankConfig c = new();
			try {
				var g = inst.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "MinePlanRanking"))
					return c;
				var r = g.Experimental.MinePlanRanking;
				if (SettingsService.IsSettingSet(r, "Mode"))
					c.Mode = MinePlanRanking.ParseMode((string) r.Mode);
				if (SettingsService.IsSettingSet(r, "HorizonDays"))
					c.HorizonDays = Math.Max(1, (int) r.HorizonDays);
				if (SettingsService.IsSettingSet(r, "MaxPaybackDays"))
					c.MaxPaybackDays = Math.Max(1, (int) r.MaxPaybackDays);
				if (SettingsService.IsSettingSet(r, "OnlySensible"))
					c.OnlySensible = (bool) r.OnlySensible;
				if (SettingsService.IsSettingSet(r, "IncludeResearch"))
					c.IncludeResearch = (bool) r.IncludeResearch;
			} catch {
			}
			return c;
		}

		private static float HourProdPlasma(ICalculationService calc, Buildables mine, int level, Planet p, int plasma, int speed, CharacterClass cls, bool geo, bool staff) {
			var lf = p.LFBonuses?.Production;
			switch (mine) {
				case Buildables.MetalMine:
					return calc.CalcMetalProduction(level, p.Coordinate.Position, speed, 1, plasma, lf?.Metal ?? 0, cls, geo, staff);
				case Buildables.CrystalMine:
					return calc.CalcCrystalProduction(level, p.Coordinate.Position, speed, 1, plasma, lf?.Crystal ?? 0, cls, geo, staff);
				default:
					return calc.CalcDeuteriumProduction(level, p.Temperature.Average, speed, 1, plasma, lf?.Deuterium ?? 0, cls, geo, staff);
			}
		}

		private static long PlasmaCost(ICalculationService calc, List<Planet> planets, int level) {
			long best = CostDeut(calc.CalcPrice(Buildables.PlasmaTechnology, level));
			foreach (var p in planets) {
				if (p.LFBonuses == null)
					continue;
				long c = CostDeut(calc.CalcPrice(Buildables.PlasmaTechnology, level, p.LFBonuses));
				if (c > 0 && c < best)
					best = c;
			}
			return best;
		}

		private static long CostDeut(Resources price) =>
			(long) Math.Round(price.Metal / MetalPerDeut + price.Crystal / CrystalPerDeut + price.Deuterium);

		private static List<ResearchStep> BuildResearch(ICalculationService calc, List<Planet> planets, Researches r, int speed, CharacterClass cls, bool geo, bool staff, ResearchPlanCfg rc = null) {
			var list = new List<ResearchStep>();
			try {
				int plasma = r.PlasmaTechnology;
				float plasmaGain = 0;
				foreach (var p in planets) {
					if (p.Buildings == null || p.Coordinate == null || p.Temperature == null)
						continue;
					foreach (var mine in Mines) {
						int lvl = Level(p, mine);
						if (lvl <= 0)
							continue;
						plasmaGain += ToDayDeut(mine, HourProdPlasma(calc, mine, lvl, p, plasma + 1, speed, cls, geo, staff) - HourProdPlasma(calc, mine, lvl, p, plasma, speed, cls, geo, staff));
					}
				}
				long plasmaCost = PlasmaCost(calc, planets, plasma + 1);
				if (PlasmaAllowed(rc, plasma))
				list.Add(new ResearchStep {
					Name = "Plasma technology", Level = plasma + 1, CostDeut = plasmaCost, GainPerDay = plasmaGain,
					RoiDays = MinePlanRanking.PaybackDays(plasmaCost, plasmaGain),
					Note = "extra output of all mines on all planets"
				});

				int et = r.EnergyTechnology;
				double extraEnergy = 0, savedCapital = 0;
				int fusionPlanets = 0;
				foreach (var p in planets) {
					if (p.Buildings == null || p.Temperature == null || p.Buildings.FusionReactor <= 0)
						continue;
					int fl = p.Buildings.FusionReactor;
					double delta = 30.0 * fl * (Math.Pow(1.05 + 0.01 * (et + 1), fl) - Math.Pow(1.05 + 0.01 * et, fl));
					int satOut = calc.GetSolarSatelliteOutput(p);
					if (satOut <= 0)
						continue;
					double pricePerEnergy = CostDeut(calc.CalcPrice(Buildables.SolarSatellite, 1)) / (double) satOut;
					extraEnergy += delta;
					savedCapital += delta * pricePerEnergy;
					fusionPlanets++;
				}
				long etCost = CostDeut(calc.CalcPrice(Buildables.EnergyTechnology, et + 1));
				list.Add(new ResearchStep {
					Name = "Energy technology", Level = et + 1, CostDeut = etCost, GainPerDay = 0, RoiDays = 0,
					ExtraEnergy = (long) Math.Round(extraEnergy), SavedCapitalDeut = (long) Math.Round(savedCapital),
					Note = fusionPlanets == 0 ? "no fusion reactor - no effect"
						: $"+{Math.Round(extraEnergy):0} energy on {fusionPlanets} fusion planet(s); saves about {Math.Round(savedCapital):0} D-eq of satellites once (not ranked)"
				});
			} catch {
			}
			return list;
		}

		private static readonly TimeSpan ItemMaxAge = TimeSpan.FromMinutes(15);
		private static readonly Dictionary<long, (DateTime At, List<ActiveItem> Items)> ItemCache = new();
		private static int _itemRefreshRunning;

		private static void AttachItems(PlanetPlan plan, long celestialId) {
			(DateTime At, List<ActiveItem> Items) entry;
			lock (ItemCache) {
				if (!ItemCache.TryGetValue(celestialId, out entry))
					return;
			}
			plan.ItemsFetchedUtc = entry.At.ToString("o");
			long age = (long) (DateTime.UtcNow - entry.At).TotalSeconds;
			foreach (var it in entry.Items) {
				long left = it.TimeRemaining - age;
				if (left <= 0)
					continue;
				plan.Items.Add(new ItemInfo { Name = it.Name, Ref = it.Ref, RemainingSec = left, TotalSec = it.TotalDuration });
			}
		}

		private static void StartItemRefresh(ITBotMain inst, IOgameService ogame, List<Planet> planets) {
			if (ogame == null)
				return;
			List<Planet> due;
			lock (ItemCache) {
				due = planets.Where(p => !ItemCache.TryGetValue(p.ID, out var e) || DateTime.UtcNow - e.At > ItemMaxAge).ToList();
			}
			if (due.Count == 0 || Interlocked.CompareExchange(ref _itemRefreshRunning, 1, 0) != 0)
				return;
			_ = Task.Run(async () => {
				try {
					foreach (var p in due) {
						try {
							var items = await ogame.GetActiveItems(p) ?? new List<ActiveItem>();
							string before;
							lock (ItemCache) {
								before = ItemCache.TryGetValue(p.ID, out var old) ? Signature(old.Items) : null;
								ItemCache[p.ID] = (DateTime.UtcNow, items);
							}
							if (Signature(items) != before && (before != null || items.Count > 0)) {
								string text = items.Count == 0 ? "none" : string.Join("; ", items.Select(i => $"{i.Name} [{i.Ref}] {i.TimeRemaining / 3600}h left"));
								inst.log(LogLevel.Information, LogSender.Brain, $"[MinePlan] Active items on {p}: {text}");
							}
						} catch (Exception e) {
							inst.log(LogLevel.Debug, LogSender.Brain, $"[MinePlan] Could not read active items of {p}: {e.Message}");
						}
						await Task.Delay(3000);
					}
				} catch {
				} finally {
					Interlocked.Exchange(ref _itemRefreshRunning, 0);
				}
			});
		}

		private static string Signature(List<ActiveItem> items) => string.Join("|", items.Select(i => i.Ref + ":" + i.ID).OrderBy(x => x));

		private static float HourProd(ICalculationService calc, Buildables mine, int level, Planet p, Researches r, int speed, CharacterClass cls, bool geo, bool staff, bool bonus) {
			int plasma = bonus ? PlasmaNow(r) : 0;
			var lf = bonus ? p.LFBonuses?.Production : null;
			var c = bonus ? cls : CharacterClass.NoClass;
			bool g = bonus && geo, st = bonus && staff;
			switch (mine) {
				case Buildables.MetalMine:
					return calc.CalcMetalProduction(level, p.Coordinate.Position, speed, 1, plasma, lf?.Metal ?? 0, c, g, st);
				case Buildables.CrystalMine:
					return calc.CalcCrystalProduction(level, p.Coordinate.Position, speed, 1, plasma, lf?.Crystal ?? 0, c, g, st);
				default:
					return calc.CalcDeuteriumProduction(level, p.Temperature.Average, speed, 1, plasma, lf?.Deuterium ?? 0, c, g, st);
			}
		}

		private const float MetalPerDeut = (float) Resources.MetalPerDeuterium;
		private const float CrystalPerDeut = (float) Resources.CrystalPerDeuterium;

		private static float ToDayDeut(Buildables mine, float hour) =>
			mine == Buildables.MetalMine ? hour / MetalPerDeut * 24 : mine == Buildables.CrystalMine ? hour / CrystalPerDeut * 24 : hour * 24;

		private static float DayProd(ICalculationService calc, Buildables mine, int level, Planet p, Researches r, int speed, CharacterClass cls, bool geo, bool staff, float extra) {
			float hour = HourProd(calc, mine, level, p, r, speed, cls, geo, staff, true);
			if (extra != 0)
				hour += extra * HourProd(calc, mine, level, p, r, speed, cls, geo, staff, false);
			return ToDayDeut(mine, hour);
		}

		private static float ExtraBonus(ICalculationService calc, Buildables mine, Planet p, Researches r, int speed, CharacterClass cls, bool geo, bool staff, out string note) {
			note = "";
			try {
				var rp = p.ResourcesProduction;
				if (rp == null || rp.Energy == null || rp.Energy.Available < 0) { note = rp?.Energy != null ? "energy deficit" : "no data"; return 0; }
				long actual = mine == Buildables.MetalMine ? rp.Metal?.CurrentProduction ?? 0
					: mine == Buildables.CrystalMine ? rp.Crystal?.CurrentProduction ?? 0
					: rp.Deuterium?.CurrentProduction ?? 0;
				int level = Level(p, mine);
				if (actual <= 0 || level <= 0) { note = "no data"; return 0; }
				float known = HourProd(calc, mine, level, p, r, speed, cls, geo, staff, true);
				float basis = HourProd(calc, mine, level, p, r, speed, cls, geo, staff, false);
				if (basis <= 0) { note = "no data"; return 0; }
				float basic = (mine == Buildables.MetalMine ? 30 : mine == Buildables.CrystalMine ? 15 : 0) * speed;
				float extra = (actual - basic - known) / basis;
				if (extra < -0.05f || extra > 1.5f) { note = "implausible"; return 0; }
				if (Math.Abs(extra) < 0.005f)
					extra = 0;
				return extra;
			} catch {
				note = "error";
				return 0;
			}
		}

		[ThreadStatic] private static int _plasmaSim;
		private static int PlasmaNow(Researches r) => _plasmaSim > 0 ? _plasmaSim : r.PlasmaTechnology;

		private static int Level(Planet p, Buildables mine) =>
			mine == Buildables.MetalMine ? p.Buildings.MetalMine : mine == Buildables.CrystalMine ? p.Buildings.CrystalMine : p.Buildings.DeuteriumSynthesizer;

		private static string MineName(Buildables mine) =>
			mine == Buildables.MetalMine ? "Metal" : mine == Buildables.CrystalMine ? "Crystal" : "Deuterium";

		private static StepInfo Step(ICalculationService calc, Planet p, Buildables mine, int toLevel, long energyLeft, Researches r, int speed, CharacterClass cls, bool geo, bool staff, float extra) {
			var price = calc.CalcPrice(mine, toLevel, p.LFBonuses);
			long cost = (long) Math.Round(price.Metal / MetalPerDeut + price.Crystal / CrystalPerDeut + price.Deuterium);
			float gain = DayProd(calc, mine, toLevel, p, r, speed, cls, geo, staff, extra) - DayProd(calc, mine, toLevel - 1, p, r, speed, cls, geo, staff, extra);
			float gainBase = DayProd(calc, mine, toLevel, p, r, speed, cls, geo, staff, 0) - DayProd(calc, mine, toLevel - 1, p, r, speed, cls, geo, staff, 0);
			long energyNeed = calc.GetRequiredEnergyDelta(mine, toLevel);
			long after = energyLeft - energyNeed;
			return new StepInfo {
				Mine = MineName(mine), Level = toLevel,
				Metal = price.Metal, Crystal = price.Crystal, Deuterium = price.Deuterium,
				CostDeut = cost, GainPerDay = gain,
				RoiDays = gain > 0 && cost > 0 ? cost / gain : 0,
				RoiDaysBase = gainBase > 0 && cost > 0 ? cost / gainBase : 0,
				EnergyNeed = energyNeed, EnergyAfter = after, EnergyShort = after < 0 ? -after : 0
			};
		}

		private static PlanetPlan BuildPlanet(ITBotMain inst, ICalculationService calc, Planet p, bool excluded) {
			if (p.Buildings == null || p.Coordinate == null || p.Temperature == null)
				return null;
			var r = inst.UserData.researches;
			int speed = inst.UserData.serverData.Speed;
			var cls = inst.UserData.userInfo.Class;
			bool geo = inst.UserData.staff.Geologist, staff = inst.UserData.staff.IsFull;
			long energy = p.Resources?.Energy ?? 0;

			var plan = new PlanetPlan {
				Name = p.ToString(), Coord = p.Coordinate.ToString(), Excluded = excluded,
				Metal = p.Buildings.MetalMine, Crystal = p.Buildings.CrystalMine, Deuterium = p.Buildings.DeuteriumSynthesizer,
				Energy = energy
			};

			var extras = new float[3];
			for (int k = 0; k < 3; k++) {
				extras[k] = ExtraBonus(calc, Mines[k], p, r, speed, cls, geo, staff, out var note);
				plan.ExtraPercent[k] = (float) Math.Round(extras[k] * 100, 1);
				plan.ExtraNote[k] = note;
			}

			int[] cap = null;
			try {
				MineCapsOf(inst, new List<Planet> { p }).TryGetValue(p.ID, out cap);
			} catch { }

			for (int k = 0; k < 3; k++)
			{
				var ns = Step(calc, p, Mines[k], Level(p, Mines[k]) + 1, energy, r, speed, cls, geo, staff, extras[k]);
				if (cap != null && ns.Level > cap[k]) {
					ns.Capped = true;
					ns.RoiDays = 0;
					ns.RoiDaysBase = 0;
				}
				plan.Next.Add(ns);
			}

			int[] lv = { p.Buildings.MetalMine, p.Buildings.CrystalMine, p.Buildings.DeuteriumSynthesizer };
			long e = energy, cumCost = 0;
			float cumGain = 0;
			for (int i = 0; i < LookaheadSteps; i++) {
				StepInfo bestStep = null;
				int bestIdx = -1;
				for (int k = 0; k < 3; k++) {
					if (cap != null && lv[k] + 1 > cap[k])
						continue;
					var s = Step(calc, p, Mines[k], lv[k] + 1, e, r, speed, cls, geo, staff, extras[k]);
					if (s.RoiDays <= 0)
						continue;
					if (bestStep == null || s.RoiDays < bestStep.RoiDays) { bestStep = s; bestIdx = k; }
				}
				if (bestStep == null)
					break;
				lv[bestIdx]++;
				e = bestStep.EnergyAfter;
				cumCost += bestStep.CostDeut;
				cumGain += bestStep.GainPerDay;
				bestStep.CumCostDeut = cumCost;
				bestStep.CumGainPerDay = cumGain;
				bestStep.CumRoiDays = cumGain > 0 ? cumCost / cumGain : 0;
				plan.Sequence.Add(bestStep);
			}

			for (int mi = 0; mi < 3; mi++) {
				var mine = Mines[mi];
				var alt = new AltPlan { Mine = MineName(mine) };
				int l = Level(p, mine);
				long ae = energy, ac = 0;
				float ag = 0;
				for (int i = 0; i < PerMineSteps; i++) {
					if (cap != null && l + 1 > cap[mi])
						break;
					var s = Step(calc, p, mine, l + 1, ae, r, speed, cls, geo, staff, extras[mi]);
					l++;
					ae = s.EnergyAfter;
					ac += s.CostDeut;
					ag += s.GainPerDay;
					s.CumCostDeut = ac;
					s.CumGainPerDay = ag;
					s.CumRoiDays = ag > 0 ? ac / ag : 0;
					alt.Steps.Add(s);
				}
				plan.Alternatives.Add(alt);
			}
			return plan;
		}

		public sealed class PlanFile {
			public string UpdatedUtc;
			public string Diag;
			public long BudgetDeutPerDay;
			public Tbot.Services.ExpoFindsStore.Summary Expo;
			public List<PlanStep> BuildPlan;
			public int Speed;
			public List<PlanetPlan> Planets;
			public List<BestStep> Best;
			public List<BestStep> Candidates;
			public List<ResearchStep> Research;
			public RankInfo Ranking;
			public LiveInfo Live;
		}
		public sealed class LiveInfo {
			public bool Active;
			public int Lookahead;
			public List<string> Planets;
			public List<PlanStep> Next;
		}
		public sealed class PlanStep {
			public int No;
			public string Planet, Coord, What;
			public int Level;
			public long CostDeut, EnergyShort;
			public float GainPerDay, RoiDays;
			public bool IsResearch;
			public bool IsLfBuilding;
		}
		public sealed class RankInfo {
			public string Mode;
			public int HorizonDays, MaxPaybackDays;
			public bool OnlySensible, IncludeResearch;
		}
		public sealed class ResearchStep {
			public string Name, Note;
			public int Level;
			public long CostDeut;
			public float GainPerDay, RoiDays;
			public long ExtraEnergy, SavedCapitalDeut;
		}
		public sealed class PlanetPlan {
			public string Name, Coord;
			public bool Excluded;
			public int Metal, Crystal, Deuterium;
			public long Energy;
			public float[] ExtraPercent = new float[3];
			public string[] ExtraNote = new string[3];
			public string ItemsFetchedUtc;
			public List<ItemInfo> Items = new();
			public List<StepInfo> Next = new();
			public List<StepInfo> Sequence = new();
			public List<AltPlan> Alternatives = new();
		}
		public sealed class ItemInfo {
			public string Name, Ref;
			public long RemainingSec, TotalSec;
		}
		public sealed class AltPlan {
			public string Mine;
			public List<StepInfo> Steps = new();
		}
		public sealed class StepInfo {
			public string Mine;
			public int Level;
			public long Metal, Crystal, Deuterium, CostDeut;
			public float GainPerDay, RoiDays, RoiDaysBase;
			public long EnergyNeed, EnergyAfter, EnergyShort;
			public long CumCostDeut;
			public float CumGainPerDay, CumRoiDays;
			public bool Capped;
		}
		public sealed class BestStep {
			public string Planet, Coord, Mine;
			public int Level;
			public float RoiDays, GainPerDay;
			public long CostDeut, EnergyShort;
			public bool IsResearch;
		}
	}
}
