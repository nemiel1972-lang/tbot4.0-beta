using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using static Tbot.Workers.DirectiveLogic;

namespace Tbot.Workers {
	internal class DirectivesConfig {
		public bool Active = false;
		public int IntervalMinutes = 10;
		public bool CollectRewards = true;
		public bool Fulfil = false;
		public bool Fleets = false;
		public bool DryRun = false;
		public int LastChapter = 7;

		public static DirectivesConfig Read(ITBotMain inst) {
			var c = new DirectivesConfig();
			try {
				var s = inst?.InstanceSettings;
				if (s == null || !SettingsService.IsSettingSet(s, "Brain") || !SettingsService.IsSettingSet(s.Brain, "Directives"))
					return c;
				var x = s.Brain.Directives;
				if (SettingsService.IsSettingSet(x, "Active")) c.Active = (bool) x.Active;
				if (SettingsService.IsSettingSet(x, "IntervalMinutes")) c.IntervalMinutes = Math.Max(1, (int) x.IntervalMinutes);
				if (SettingsService.IsSettingSet(x, "CollectRewards")) c.CollectRewards = (bool) x.CollectRewards;
				if (SettingsService.IsSettingSet(x, "Fulfil")) c.Fulfil = (bool) x.Fulfil;
				if (SettingsService.IsSettingSet(x, "Fleets")) c.Fleets = (bool) x.Fleets;
				if (SettingsService.IsSettingSet(x, "DryRun")) c.DryRun = (bool) x.DryRun;
				if (SettingsService.IsSettingSet(x, "LastChapter")) c.LastChapter = Math.Clamp((int) x.LastChapter, 1, 12);
			} catch (Exception) {
			}
			return c;
		}
	}

	internal class DirectivesWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly ICalculationService _calculationService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly Dictionary<string, DateTime> _gesperrt = new();
		private DateTime _letzteExpedition = DateTime.MinValue;
		private DateTime _letzteSpionage = DateTime.MinValue;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private readonly Dictionary<string, DateTime> _shipOrders = new();

		public DirectivesWorker(ITBotMain parentInstance, IOgameService ogameService, IFleetScheduler fleetScheduler, ICalculationService calculationService, ITBotOgamedBridge tbotOgameBridge) : base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(10);

		private string _letzteListe = "";
		private readonly Dictionary<int, DateTime> _kapitelVersuch = new();
		private readonly HashSet<string> _voraussetzungGemeldet = new();

		private static string Kuerzen(string s) => string.IsNullOrEmpty(s) ? "(empty)" : (s.Length > 300 ? s.Substring(0, 300) + "…" : s);

		private void Log(LogLevel level, string text) {
			DoLog(level, text);
			ExtraLog.Write(_tbotInstance, "Directives", $"[{level}] {text}");
		}

		private async Task<List<DirectiveChapterInfo>> ReadAll(DirectivesConfig cfg) {
			var list = new List<DirectiveChapterInfo>();
			for (int id = 4001; id <= 4000 + cfg.LastChapter; id++) {
				string html = await _ogameService.GetDirectiveChapterHtml(id);
				var ch = ParseChapter(id, html);
				if (ch.Tasks.Count == 0)
					continue;
				list.Add(ch);
				await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(1, 3));
			}
			return list;
		}

		protected override async Task Execute() {
			var cfg = DirectivesConfig.Read(_tbotInstance);
			long nextMs = cfg.IntervalMinutes * 60 * 1000L + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
			try {
				if (!cfg.Active || _tbotInstance.UserData.isSleeping || _tbotInstance.IsBotPaused) {
					ChangeWorkerPeriod(nextMs);
					return;
				}
				var chapters = await ReadAll(cfg);
				Log(LogLevel.Information, $"Directives: {chapters.Count} chapter(s) read, {chapters.Sum(c => c.Tasks.Count(t => t.State != "collected"))} open task(s).");

				if (cfg.CollectRewards) {
					bool any = false;
					foreach (var ch in chapters) {
						foreach (var t in ch.Tasks.Where(t => t.CanCollect)) {
							try {
								var antwort = await _ogameService.CollectDirectiveTask(t.TaskId);
								Log(LogLevel.Information, $"Directives: reward of task {t.TaskId} (chapter {ch.ChapterId}) collected.");
								if (!Regex.IsMatch(antwort ?? "", "\"success\"\\s*:\\s*true"))
									Log(LogLevel.Warning, $"Directives: task {t.TaskId} - unexpected server answer: {Kuerzen(antwort)}");
								any = true;
							} catch (Exception e) {
								Log(LogLevel.Warning, $"Directives: collecting task {t.TaskId} failed: {e.Message}");
							}
							await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(2, 4));
						}
					}
					if (any)
						chapters = await ReadAll(cfg);
					foreach (var ch in chapters) {
						bool alleAbgeholt = ch.Tasks.Count > 0 && ch.Tasks.All(t => t.State == "collected");
						if (alleAbgeholt && string.IsNullOrWhiteSpace(ch.ChapterButtonClass) && !ch.CanClaimChapter && (!_kapitelVersuch.TryGetValue(ch.ChapterId, out var wann) || DateTime.UtcNow - wann > TimeSpan.FromHours(6))) {
							_kapitelVersuch[ch.ChapterId] = DateTime.UtcNow;
							try {
								var antwort = await _ogameService.ClaimDirectiveChapter(ch.ChapterId);
								Log(LogLevel.Information, $"Directives: chapter {ch.ChapterId} complete - claim request sent (no button found). Answer: {Kuerzen(antwort)}");
							} catch (Exception e) {
								Log(LogLevel.Debug, $"Directives: chapter {ch.ChapterId} fallback claim failed: {e.Message}");
							}
							await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(2, 4));
						}
					}
					foreach (var ch in chapters.Where(c => c.CanClaimChapter)) {
						try {
							var antwort = await _ogameService.ClaimDirectiveChapter(ch.ChapterId);
							Log(LogLevel.Information, $"Directives: chapter {ch.ChapterId} reward collected.");
							if (!Regex.IsMatch(antwort ?? "", "\"success\"\\s*:\\s*true"))
								Log(LogLevel.Warning, $"Directives: chapter {ch.ChapterId} - unexpected server answer: {Kuerzen(antwort)}");
						} catch (Exception e) {
							Log(LogLevel.Warning, $"Directives: collecting chapter {ch.ChapterId} failed: {e.Message}");
						}
						await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(2, 4));
					}
				}

				var open = chapters.SelectMany(c => c.Tasks.Where(t => t.State != "collected" && !t.CanCollect)
					.SelectMany(t => t.Actions.Select((a, i) => (Task: t, Index: i, Action: a)))).Where(x => !x.Action.Done).ToList();

				var zeilen = new List<string>();
				foreach (var x in open.Where(x => x.Action.Kind == GoalKind.Manual))
					zeilen.Add($"Directives: manual task {x.Task.TaskId}: {x.Action.Text}");
				foreach (var x in open.Where(x => x.Action.Kind == GoalKind.Fleet || x.Action.Kind == GoalKind.Unknown))
					zeilen.Add($"Directives: not automated yet ({x.Action.FleetTag}) task {x.Task.TaskId}: {x.Action.Text}");
				string liste = string.Join("\n", zeilen);
				if (liste != _letzteListe) {
					_letzteListe = liste;
					foreach (var z in zeilen) Log(LogLevel.Information, z);
				}

				if (cfg.Fulfil) {
					var todo = open.Where(x => x.Action.Kind == GoalKind.BuildingLevel || x.Action.Kind == GoalKind.ResearchLevel
						|| x.Action.Kind == GoalKind.ShipCount || x.Action.Kind == GoalKind.DefenceCount || x.Action.Kind == GoalKind.LifeformBuilding || x.Action.Kind == GoalKind.LifeformResearch).ToList();
					if (todo.Count > 0) {
						_tbotInstance.UserData.researches = await _ogameService.GetResearches();
						_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();
					}
					bool buildingStarted = false, researchStarted = false, lfStarted = false, lfResearchStarted = false;
					Planet heimat = null;
					if (todo.Count > 0 && Planets().Count > 0) {
						heimat = Planets()[0];
						heimat = await _tbotOgameBridge.UpdatePlanet(heimat, UpdateTypes.Buildings) as Planet;
						heimat = await _tbotOgameBridge.UpdatePlanet(heimat, UpdateTypes.Facilities) as Planet;
					}
					foreach (var x in todo) {
						try {
							var a = x.Action;
							if (heimat != null && a.Kind != GoalKind.LifeformBuilding && a.Kind != GoalKind.LifeformResearch) {
								Func<Buildables, int> stufe = b => DirectivePrereq.IsResearch(b) ? _tbotInstance.UserData.researches.GetLevel(b) : heimat.GetLevel(b);
								bool braucht = a.Kind == GoalKind.ShipCount || a.Kind == GoalKind.DefenceCount || stufe(a.Item) == 0;
								var fehlt = braucht ? DirectivePrereq.FirstMissing(a.Item, stufe) : null;
								if (fehlt != null) {
									if (_voraussetzungGemeldet.Add($"{a.Item}>{fehlt.Value.Item}{fehlt.Value.Level}"))
										Log(LogLevel.Information, $"Directives: {a.Item} needs {fehlt.Value.Item} level {fehlt.Value.Level} first - building that instead.");
									a = new DirectiveAction {
										Kind = DirectivePrereq.IsResearch(fehlt.Value.Item) ? GoalKind.ResearchLevel : GoalKind.BuildingLevel,
										Item = fehlt.Value.Item, Value = fehlt.Value.Level, Text = $"prerequisite of {a.Item}"
									};
								}
							}
							if (a.Kind == GoalKind.ResearchLevel) {
								if (researchStarted) continue;
								researchStarted = await DoResearch(a, cfg);
							} else if (a.Kind == GoalKind.LifeformResearch) {
								if (lfResearchStarted) continue;
								lfResearchStarted = await DoLifeformResearch(a, cfg);
							} else if (a.Kind == GoalKind.LifeformBuilding) {
								if (lfStarted) continue;
								lfStarted = await DoLifeformBuilding(a, cfg);
							} else if (a.Kind == GoalKind.BuildingLevel) {
								if (buildingStarted) continue;
								buildingStarted = await DoBuilding(a, cfg);
							} else {
								await DoUnits(x.Task.TaskId, x.Index, a, cfg);
							}
						} catch (Exception e) {
							Log(LogLevel.Warning, $"Directives: task {x.Task.TaskId} '{x.Action.Text}' failed: {e.Message}");
						}
					}
				}

				if (cfg.Fleets) {
					var gemacht = new HashSet<string>();
					foreach (var x in open.Where(x => x.Action.Kind == GoalKind.Fleet)) {
						if (!gemacht.Add(x.Action.FleetTag)) continue;
						try {
							if (x.Action.FleetTag == "colonize") await Kolonisieren(cfg);
							else if (x.Action.FleetTag == "expedition") await Expedition(cfg);
							else if (x.Action.FleetTag == "spy16") await Spionage16(cfg);
							else if (x.Action.FleetTag == "recall") await Rueckruf(cfg);
							else if (x.Action.FleetTag == "lithium") await LithiumStarten(cfg);
						} catch (Exception e) {
							Log(LogLevel.Warning, $"Directives: fleet task {x.Task.TaskId} '{x.Action.Text}' failed: {e.Message}");
						}
					}
				}
				if (cfg.Fleets && _lithiumGestartet && !open.Any(x => x.Action.FleetTag == "lithium"))
					await LithiumStoppen(cfg);
			} catch (Exception e) {
				Log(LogLevel.Warning, $"Directives: cycle failed: {e.Message}");
				nextMs = 3 * 60 * 1000;
			}
			ChangeWorkerPeriod(nextMs);
		}

		private List<Planet> Planets() => _tbotInstance.UserData.celestials.OfType<Planet>().OrderBy(c => c.ID).ToList();

		private async Task<Planet> Pick(DirectiveAction a, Buildables item) {
			var planets = Planets();
			if (planets.Count == 0) return null;
			if (a.PlanetIndex > 0) return a.PlanetIndex <= planets.Count ? planets[a.PlanetIndex - 1] : null;
			if (!a.OnColony) return planets[0];
			var cols = planets.Skip(1).ToList();
			if (cols.Count == 0) return null;
			Planet best = null; int bestLevel = -1;
			foreach (var c in cols) {
				var u = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Buildings) as Planet;
				u = await _tbotOgameBridge.UpdatePlanet(u, UpdateTypes.Facilities) as Planet;
				int lvl = u.GetLevel(item);
				if (lvl < a.Value && lvl > bestLevel) { best = u; bestLevel = lvl; }
			}
			return best;
		}

		private async Task<bool> DoBuilding(DirectiveAction a, DirectivesConfig cfg) {
			var p = await Pick(a, a.Item);
			if (p == null) { Log(LogLevel.Information, $"Directives: no suitable planet for {a.Item} {a.Value}."); return false; }
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Buildings) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Facilities) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Constructions) as Planet;
			int level = p.GetLevel(a.Item);
			if (level >= a.Value) return false;
			if (p.Constructions.BuildingID != 0) { Log(LogLevel.Debug, $"Directives: construction already running on {p}."); return true; }
			int next = level + 1;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Resources) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBonuses) as Planet;
			var cost = _calculationService.CalcPrice(a.Item, next, p.LFBonuses);
			if (!p.Resources.IsEnoughFor(cost)) {
				Log(LogLevel.Information, $"Directives: not enough resources for {a.Item} {next} on {p} (need {cost.TransportableResources}, have {p.Resources.TransportableResources}).");
				return false;
			}
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would build {a.Item} {next} on {p} (target {a.Value})."); return true; }
			await _ogameService.BuildConstruction(p, a.Item);
			Log(LogLevel.Information, $"Directives: building {a.Item} {next} on {p} (task target {a.Value}).");
			return true;
		}

		private async Task<bool> DoLifeformBuilding(DirectiveAction a, DirectivesConfig cfg) {
			var p = await Pick(a, Buildables.MetalMine);
			if (p == null) { Log(LogLevel.Information, $"Directives: no suitable planet for lifeform building {a.LFItem} {a.Value}."); return false; }
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBuildings) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Buildings) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Constructions) as Planet;
			if (p.LFtype == LFTypes.None) { Log(LogLevel.Debug, $"Directives: {p} has no lifeform, skipping {a.LFItem}."); return false; }
			var ziel = a.LFItem;
			int zielStufe = a.Value;
			if (p.GetLevel(ziel) >= zielStufe) return false;
			for (int tiefe = 0; tiefe < 6; tiefe++) {
				var fehlt = _calculationService.GetLFRequirements(ziel).FirstOrDefault(r => p.GetLevel(r.Key) < r.Value);
				if (fehlt.Key == LFBuildables.None) break;
				if (_voraussetzungGemeldet.Add($"LF:{ziel}>{fehlt.Key}{fehlt.Value}"))
					Log(LogLevel.Information, $"Directives: {ziel} needs {fehlt.Key} level {fehlt.Value} first - building that instead.");
				ziel = fehlt.Key; zielStufe = fehlt.Value;
			}
			if (p.Constructions.LFBuildingID != 0 || p.Constructions.BuildingID == (int) Buildables.RoboticsFactory || p.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
				Log(LogLevel.Debug, $"Directives: a lifeform/robotics/nanite construction is already running on {p}.");
				return true;
			}
			int level = p.GetLevel(ziel);
			int next = level + 1;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Resources) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBonuses) as Planet;
			var cost = _calculationService.CalcPrice(ziel, next, _calculationService.CalcLFBuildingsResourcesCostBonus(p), 0, _calculationService.CalcLFBuildingsPopulationCostBonus(p));
			if (!p.Resources.IsBuildable(cost)) {
				Log(LogLevel.Information, $"Directives: not enough resources for {ziel} {next} on {p} (need {cost.TransportableResources}, have {p.Resources.TransportableResources}).");
				return false;
			}
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would build lifeform building {ziel} {next} on {p} (target {zielStufe})."); return true; }
			await _ogameService.BuildCancelable(p, ziel);
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Constructions) as Planet;
			if (p.Constructions.LFBuildingID == (int) ziel)
				Log(LogLevel.Information, $"Directives: building lifeform building {ziel} {next} on {p} (task target {a.Value}).");
			else
				Log(LogLevel.Warning, $"Directives: order of {ziel} {next} on {p} was not accepted by the game.");
			return true;
		}

		private async Task<bool> DoLifeformResearch(DirectiveAction a, DirectivesConfig cfg) {
			var p = await Pick(a, Buildables.MetalMine);
			if (p == null) { Log(LogLevel.Information, $"Directives: no suitable planet for lifeform research {a.LFTech} {a.Value}."); return false; }
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBuildings) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFTechs) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Constructions) as Planet;
			if (p.LFtype == LFTypes.None) { Log(LogLevel.Debug, $"Directives: {p} has no lifeform, skipping {a.LFTech}."); return false; }
			if ((int) a.LFTech / 1000 - 10 != (int) p.LFtype) { Log(LogLevel.Debug, $"Directives: {a.LFTech} belongs to another lifeform than {p}, skipped."); return false; }
			if (p.LFTechs.GetLevel(a.LFTech) >= a.Value) return false;
			if (p.Constructions.LFResearchID != 0) { Log(LogLevel.Debug, $"Directives: a lifeform research is already running on {p}."); return true; }
			int next = _calculationService.GetNextLevel(p, a.LFTech);
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Resources) as Planet;
			var cost = _calculationService.CalcPrice(a.LFTech, next);
			if (!p.Resources.IsEnoughFor(cost)) {
				Log(LogLevel.Information, $"Directives: not enough resources for {a.LFTech} {next} on {p} (need {cost.TransportableResources}, have {p.Resources.TransportableResources}).");
				return false;
			}
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would research lifeform tech {a.LFTech} {next} on {p} (target {a.Value})."); return true; }
			try {
				await _ogameService.BuildCancelable(p, a.LFTech);
			} catch (Exception e) {
				Log(LogLevel.Warning, $"Directives: lifeform research {a.LFTech} {next} on {p} refused: {e.Message}");
				return false;
			}
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Constructions) as Planet;
			if (p.Constructions.LFResearchID == (int) a.LFTech)
				Log(LogLevel.Information, $"Directives: researching lifeform tech {a.LFTech} {next} on {p} (task target {a.Value}).");
			else
				Log(LogLevel.Warning, $"Directives: order of lifeform research {a.LFTech} {next} on {p} was not accepted by the game.");
			return true;
		}

		private async Task<bool> DoResearch(DirectiveAction a, DirectivesConfig cfg) {
			var planets = Planets();
			if (planets.Count == 0) return false;
			int level = _tbotInstance.UserData.researches.GetLevel(a.Item);
			if (level >= a.Value) return false;
			var p = planets[0];
			foreach (var c in planets) {
				var u = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Constructions) as Planet;
				if (u.Constructions.ResearchID != 0) { Log(LogLevel.Debug, "Directives: a research is already running."); return true; }
			}
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Resources) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBonuses) as Planet;
			int next = level + 1;
			var cost = _calculationService.CalcPrice(a.Item, next, p.LFBonuses);
			if (!p.Resources.IsEnoughFor(cost)) {
				Log(LogLevel.Information, $"Directives: not enough resources for {a.Item} {next} on {p} (need {cost.TransportableResources}, have {p.Resources.TransportableResources}).");
				return false;
			}
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would research {a.Item} {next} (target {a.Value})."); return true; }
			await _ogameService.BuildCancelable(p, a.Item);
			Log(LogLevel.Information, $"Directives: researching {a.Item} {next} on {p} (task target {a.Value}).");
			return true;
		}

		private async Task DoUnits(long taskId, int index, DirectiveAction a, DirectivesConfig cfg) {
			string key = $"{taskId}:{index}";
			if (_shipOrders.TryGetValue(key, out var last) && DateTime.UtcNow - last < TimeSpan.FromHours(2))
				return;
			var planets = Planets();
			if (planets.Count == 0) return;
			var p = planets[0];
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Productions) as Planet;
			if (p.HasProduction()) { Log(LogLevel.Debug, $"Directives: production running on {p}."); return; }
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Resources) as Planet;
			p = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBonuses) as Planet;
			var cost = _calculationService.CalcPrice(a.Item, a.Value, p.LFBonuses);
			if (!p.Resources.IsEnoughFor(cost)) {
				Log(LogLevel.Information, $"Directives: not enough resources for {a.Value} x {a.Item} on {p} (need {cost.TransportableResources}, have {p.Resources.TransportableResources}).");
				return;
			}
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would build {a.Value} x {a.Item} on {p}."); return; }
			if (a.Kind == GoalKind.DefenceCount)
				await _ogameService.BuildDefences(p, a.Item, a.Value);
			else
				await _ogameService.BuildShips(p, a.Item, a.Value);
			_shipOrders[key] = DateTime.UtcNow;
			Log(LogLevel.Information, $"Directives: ordered {a.Value} x {a.Item} on {p}.");
		}

		private async Task Kolonisieren(DirectivesConfig cfg) {
			_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
			_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();
			int maxPlanets = _calculationService.CalcMaxPlanets(_tbotInstance.UserData.researches);
			int aktuell = _tbotInstance.UserData.celestials.Count(c => c.Coordinate.Type == Celestials.Planet);
			if (aktuell >= maxPlanets) { Log(LogLevel.Information, "Directives: colonise - no free planet slot."); return; }
			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Colonize && !f.ReturnFlight)) return;
			var home = Planets().FirstOrDefault();
			if (home == null) return;
			home = await _tbotOgameBridge.UpdatePlanet(home, UpdateTypes.Ships) as Planet;
			if (home.Ships.ColonyShip < 1) { Log(LogLevel.Debug, "Directives: colonise - no colony ship at home yet."); return; }
			int astro = _tbotInstance.UserData.researches.Astrophysics;
			var kandidaten = new List<Coordinate>();
			for (int d = 0; d <= 12; d++) {
				foreach (int sys in new[] { home.Coordinate.System + d, home.Coordinate.System - d }.Distinct()) {
					int s = GeneralHelper.WrapSystem(sys);
					for (int pos = 4; pos <= 12; pos++) {
						if (!_calculationService.IsAstrophysicsPositionValid(pos, astro)) continue;
						kandidaten.Add(new Coordinate(home.Coordinate.Galaxy, s, pos, Celestials.Planet));
					}
				}
			}
			int geprueft = 0;
			foreach (var t in kandidaten) {
				if (geprueft >= 15) break;
				if (_tbotInstance.UserData.celestials.Any(c => c.HasCoords(t))) continue;
				string key = t.ToString();
				if (_gesperrt.TryGetValue(key, out var bis) && bis > DateTime.UtcNow) continue;
				if (GalaxyCacheHub.KnownOccupied(_tbotInstance, t, GalaxyCacheConfig.Read(_tbotInstance).UseInColonize)) continue;
				geprueft++;
				var galaxy = await GalaxyCacheHub.GetOrLiveAsync(_tbotInstance, _ogameService, t.Galaxy, t.System, GalaxyCacheConfig.Read(_tbotInstance).UseInColonize);
				if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) continue;
				if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would colonise {t}."); return; }
				var ships = new Ships { ColonyShip = 1 };
				int id = await _fleetScheduler.SendFleet(home, ships, t, Missions.Colonize, Speeds.HundredPercent);
				if (id == (int) SendFleetCode.TargetInvalid) { _gesperrt[key] = DateTime.UtcNow.AddHours(6); continue; }
				if (id <= (int) SendFleetCode.GenericError) { Log(LogLevel.Information, $"Directives: colonise {t} not sent (code {id})."); return; }
				Log(LogLevel.Information, $"Directives: colony ship sent to {t}.");
				return;
			}
			Log(LogLevel.Information, "Directives: colonise - no free target found this cycle.");
		}

		private async Task Expedition(DirectivesConfig cfg) {
			if (DateTime.UtcNow - _letzteExpedition < TimeSpan.FromHours(1)) return;
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
			if (_tbotInstance.UserData.slots.ExpFree < 1 || _tbotInstance.UserData.slots.Free < 1) { Log(LogLevel.Debug, "Directives: expedition - no free slot."); return; }
			var home = Planets().FirstOrDefault();
			if (home == null) return;
			home = await _tbotOgameBridge.UpdatePlanet(home, UpdateTypes.Ships) as Planet;
			long n = Math.Min(home.Ships.SmallCargo, 5);
			if (n < 1) { Log(LogLevel.Debug, "Directives: expedition - no small cargo at home."); return; }
			var dest = new Coordinate { Galaxy = home.Coordinate.Galaxy, System = home.Coordinate.System, Position = 16, Type = Celestials.DeepSpace };
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would send {n} small cargo on expedition to {dest}."); return; }
			int id = await _fleetScheduler.SendFleet(home, new Ships { SmallCargo = n }, dest, Missions.Expedition, Speeds.HundredPercent);
			if (id <= (int) SendFleetCode.GenericError) { Log(LogLevel.Information, $"Directives: expedition not sent (code {id})."); return; }
			_letzteExpedition = DateTime.UtcNow;
			Log(LogLevel.Information, $"Directives: expedition sent with {n} small cargo to {dest}.");
		}

		private async Task Spionage16(DirectivesConfig cfg) {
			if (DateTime.UtcNow - _letzteSpionage < TimeSpan.FromHours(1)) return;
			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Spy && f.Destination.Position == 16 && !f.ReturnFlight)) return;
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
			if (_tbotInstance.UserData.slots.Free < 1) { Log(LogLevel.Debug, "Directives: spy 16 - no free slot."); return; }
			var home = Planets().FirstOrDefault();
			if (home == null) return;
			home = await _tbotOgameBridge.UpdatePlanet(home, UpdateTypes.Ships) as Planet;
			if (home.Ships.SmallCargo < 1 || home.Ships.EspionageProbe < 1) { Log(LogLevel.Debug, "Directives: spy 16 - need 1 small cargo and 1 espionage probe at home."); return; }
			home = await _tbotOgameBridge.UpdatePlanet(home, UpdateTypes.Resources) as Planet;
			var dest = new Coordinate(home.Coordinate.Galaxy, home.Coordinate.System, 16, Celestials.Planet);
			var payload = new Resources { Metal = Math.Min(500, home.Resources.Metal) };
			if (cfg.DryRun) { Log(LogLevel.Information, $"Directives (dry run): would send spy mission with cargo to {dest}."); return; }
			int id = await _fleetScheduler.SendFleet(home, new Ships { SmallCargo = 1, EspionageProbe = 1 }, dest, Missions.Spy, Speeds.HundredPercent, payload);
			if (id <= (int) SendFleetCode.GenericError) { Log(LogLevel.Information, $"Directives: spy 16 not sent (code {id})."); return; }
			_letzteSpionage = DateTime.UtcNow;
			Log(LogLevel.Information, $"Directives: spy mission with cargo sent to {dest}.");
		}

		private bool _lithiumGestartet;

		private async Task LithiumStarten(DirectivesConfig cfg) {
			var laufend = LithiumClient.ParseActiveConversions(await LithiumClient.FetchProduction(_ogameService));
			if (laufend.Count > 0) { Log(LogLevel.Debug, "Directives: lithium conversion already running."); return; }
			if (cfg.DryRun) { Log(LogLevel.Information, "Directives (dry run): would start a lithium conversion (metal, 100%)."); return; }
			var r = LithiumClient.Check(await LithiumClient.Start(_ogameService, "metal", 100));
			if (r.Ok) {
				_lithiumGestartet = true;
				Log(LogLevel.Information, "Directives: lithium conversion started (metal, 100%).");
			} else {
				Log(LogLevel.Warning, $"Directives: starting the lithium conversion failed: {r.Message}");
			}
		}

		private async Task LithiumStoppen(DirectivesConfig cfg) {
			var ids = LithiumClient.ParseActiveConversions(await LithiumClient.FetchProduction(_ogameService));
			if (ids.Count == 0) { _lithiumGestartet = false; return; }
			if (cfg.DryRun) { Log(LogLevel.Information, "Directives (dry run): would stop the lithium conversion."); return; }
			foreach (var id in ids) {
				var r = LithiumClient.Check(await LithiumClient.Stop(_ogameService, id));
				Log(r.Ok ? LogLevel.Information : LogLevel.Warning, r.Ok ? $"Directives: lithium conversion {id} stopped." : $"Directives: stopping lithium conversion {id} failed: {r.Message}");
				await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(2, 4));
			}
			_lithiumGestartet = false;
		}

		private async Task Rueckruf(DirectivesConfig cfg) {
			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			var f = _tbotInstance.UserData.fleets.FirstOrDefault(x => x.Mission == Missions.Spy && x.Destination.Position == 16 && !x.ReturnFlight);
			if (f == null) return;
			if (cfg.DryRun) { Log(LogLevel.Information, "Directives (dry run): would recall the spy fleet."); return; }
			await _fleetScheduler.CancelFleet(f);
			Log(LogLevel.Information, "Directives: spy fleet recalled.");
		}

		public override bool IsWorkerEnabledBySettings() => DirectivesConfig.Read(_tbotInstance).Active;

		public override string GetWorkerName() => "Directives";

		public override Feature GetFeature() => Feature.Directives;

		public override LogSender GetLogSender() => LogSender.Directives;
	}
}
