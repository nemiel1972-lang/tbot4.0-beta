using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tbot.Common.IO;
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

				var best = plans.Where(pl => !pl.Excluded)
					.SelectMany(pl => pl.Next.Where(n => n.RoiDays > 0).Select(n => new BestStep {
						Planet = pl.Name, Coord = pl.Coord, Mine = n.Mine, Level = n.Level, RoiDays = n.RoiDays,
						CostDeut = n.CostDeut, GainPerDay = n.GainPerDay, EnergyShort = n.EnergyShort
					}))
					.OrderBy(s => s.RoiDays).Take(12).ToList();

				var file = new PlanFile {
					UpdatedUtc = DateTime.UtcNow.ToString("o"),
					Speed = inst.UserData.serverData.Speed,
					Planets = plans,
					Best = best
				};
				string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dataDir);
				string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				AtomicFile.WriteAllText(Path.Combine(dataDir, $"minenplan_{safe}.json"), JsonConvert.SerializeObject(file, Formatting.Indented));
			} catch {
			}
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
			int plasma = bonus ? r.PlasmaTechnology : 0;
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

		private static int Level(Planet p, Buildables mine) =>
			mine == Buildables.MetalMine ? p.Buildings.MetalMine : mine == Buildables.CrystalMine ? p.Buildings.CrystalMine : p.Buildings.DeuteriumSynthesizer;

		private static string MineName(Buildables mine) =>
			mine == Buildables.MetalMine ? "Metal" : mine == Buildables.CrystalMine ? "Crystal" : "Deuterium";

		private static StepInfo Step(ICalculationService calc, Planet p, Buildables mine, int toLevel, long energyLeft, Researches r, int speed, CharacterClass cls, bool geo, bool staff, float extra) {
			var price = calc.CalcPrice(mine, toLevel);
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

			for (int k = 0; k < 3; k++)
				plan.Next.Add(Step(calc, p, Mines[k], Level(p, Mines[k]) + 1, energy, r, speed, cls, geo, staff, extras[k]));

			int[] lv = { p.Buildings.MetalMine, p.Buildings.CrystalMine, p.Buildings.DeuteriumSynthesizer };
			long e = energy, cumCost = 0;
			float cumGain = 0;
			for (int i = 0; i < LookaheadSteps; i++) {
				StepInfo bestStep = null;
				int bestIdx = -1;
				for (int k = 0; k < 3; k++) {
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
			public int Speed;
			public List<PlanetPlan> Planets;
			public List<BestStep> Best;
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
		}
		public sealed class BestStep {
			public string Planet, Coord, Mine;
			public int Level;
			public float RoiDays, GainPerDay;
			public long CostDeut, EnergyShort;
		}
	}
}
