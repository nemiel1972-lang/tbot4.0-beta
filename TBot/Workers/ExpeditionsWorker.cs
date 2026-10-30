using Tbot.Common.IO;
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

namespace Tbot.Workers {
	public class ExpeditionsWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private int _nextOriginIndex;
		private readonly ExpeditionDiagnostics.RepeatLimiter _repeat = new();
		private const string RepeatNoSend = "nosend";
		private const string RepeatOriginConfig = "origincfg";

		private ExpeditionDurationTracker _durationTracker;
		private readonly object _durationTrackerLock = new();

		public ExpeditionDurationTracker GetDurationTracker() {
			lock (_durationTrackerLock) {
				if (_durationTracker != null)
					return _durationTracker;
				string path = null;
				try {
					string playerTag = _tbotInstance?.UserData?.userInfo?.PlayerName ?? "unknown";
					string universeTag = _tbotInstance?.UserData?.serverData?.Name ?? "unknown";
					string dataDir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
					System.IO.Directory.CreateDirectory(dataDir);
					path = System.IO.Path.Combine(dataDir, $"expedition_durations_{playerTag}-{universeTag}.json");
				} catch (Exception e) {
					DoLog(LogLevel.Debug, $"Expedition duration file path could not be determined, measurements stay in memory only: {e.Message}");
				}
				_durationTracker = new ExpeditionDurationTracker(path, msg => DoLog(LogLevel.Debug, msg));
				return _durationTracker;
			}
		}

		public ExpeditionsWorker(
			ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) : base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		public override bool IsWorkerEnabledBySettings() {
			try { return (bool)_tbotInstance.InstanceSettings.Expeditions.Active; }
			catch (Exception) { return false; }
		}

		public override string GetWorkerName() => "Expeditions";
		public override Feature GetFeature() => Feature.Expeditions;
		public override LogSender GetLogSender() => LogSender.Expeditions;

		private int CountActiveExpeditionsFromOrigin(Celestial origin) {
			if (origin?.Coordinate == null || _tbotInstance.UserData.fleets == null)
				return 0;

			return _tbotInstance.UserData.fleets.Count(f =>
				f.Mission == Missions.Expedition &&
				f.Origin != null &&
				f.Origin.Galaxy == origin.Coordinate.Galaxy &&
				f.Origin.System == origin.Coordinate.System &&
				f.Origin.Position == origin.Coordinate.Position &&
				f.Origin.Type == origin.Coordinate.Type);
		}

		private ExpoFindsStore _expoStore;
		private async System.Threading.Tasks.Task RefreshExpoFinds() {
			try {
				if (!ReadExpeditionReportsEnabled())
					return;
				_expoStore ??= new ExpoFindsStore(ExpoFindsStore.PathFor(_tbotInstance.InstanceAlias));
				if (!_expoStore.Due(DateTime.UtcNow, TimeSpan.FromMinutes(20)))
					return;
				var messages = await _ogameService.GetExpeditionMessages();
				double ShipValue(string name) {
					if (!Enum.TryParse<Buildables>(name, true, out var b))
						return 0;
					var price = _calculationService.CalcPrice(b, 1);
					return price.Metal / Resources.MetalPerDeuterium + price.Crystal / Resources.CrystalPerDeuterium + price.Deuterium;
				}
				bool baseline = (_expoStore.Current?.Reports ?? 0) == 0;
				var fresh = new List<ExpoFindsStore.Entry>();
				int added = _expoStore.Add(messages, ShipValue, DateTime.UtcNow, fresh);
				if (added > 0)
					DoLog(LogLevel.Information, $"Expedition reports: {added} new report(s) saved, {_expoStore.Current?.Reports ?? 0} in total.");
				if (!baseline)
					await NotifyExpoFinds(fresh);
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Expedition reports could not be read: {e.Message}");
			}
		}

		private bool ReadExpeditionReportsEnabled() {
			try {
				var g = _tbotInstance?.InstanceSettings;
				if (g == null || !SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "ReportReading"))
					return true;
				var r = g.Experimental.ReportReading;
				return !SettingsService.IsSettingSet(r, "Expeditions") || (bool) r.Expeditions;
			} catch (Exception) {
				return true;
			}
		}

		private readonly List<ExpoFindsStore.Entry> _expoPending = new();
		private DateTime _expoPendingSince = DateTime.MinValue;
		private async System.Threading.Tasks.Task NotifyExpoFinds(List<ExpoFindsStore.Entry> fresh) {
			try {
				var exp = _tbotInstance.InstanceSettings.Expeditions;
				if (!SettingsService.IsSettingSet(exp, "TelegramMessenger") || !SettingsService.IsSettingSet(exp.TelegramMessenger, "Active") || !(bool) exp.TelegramMessenger.Active) {
					_expoPending.Clear();
					return;
				}
				bool onlyFinds = !SettingsService.IsSettingSet(exp.TelegramMessenger, "OnlyFinds") || (bool) exp.TelegramMessenger.OnlyFinds;
				int window = SettingsService.IsSettingSet(exp.TelegramMessenger, "WindowMinutes") ? Math.Max(0, (int) exp.TelegramMessenger.WindowMinutes) : 0;
				var now = DateTime.UtcNow;
				var neu = fresh.Where(e => e.Utc >= now.AddHours(-26)).ToList();
				if (neu.Count > 0 && _expoPending.Count == 0)
					_expoPendingSince = now;
				_expoPending.AddRange(neu);
				if (_expoPending.Count == 0 || (window > 0 && (now - _expoPendingSince).TotalMinutes < window))
					return;
				string text = ExpoFindsStore.FormatTelegram(_expoPending, onlyFinds);
				_expoPending.Clear();
				if (text != null)
					await _tbotInstance.SendTelegramMessage(text);
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Expedition message could not be sent: {e.Message}");
			}
		}

		private LfEffectsStore _lfEffects;
		private async System.Threading.Tasks.Task RefreshLfEffects(long celestialId) {
			try {
				_lfEffects ??= new LfEffectsStore(LfEffectsStore.PathFor(_tbotInstance.InstanceAlias));
				if (!_lfEffects.Due(DateTime.UtcNow, TimeSpan.FromHours(6)))
					return;
				string html = await _ogameService.GetLfBonusesPageHtml(celestialId);
				var rows = LfEffectsStore.Parse(html);
				int changed = _lfEffects.Add(rows, DateTime.UtcNow);
				if (changed > 0)
					DoLog(LogLevel.Information, $"Lifeform effects: {changed} new value(s) learned ({rows.Count} entries read).");
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Lifeform effects could not be read: {e.Message}");
			}
		}

		private DateTime _lfTablesLast = DateTime.MinValue;
		private async System.Threading.Tasks.Task RefreshLfTables(IEnumerable<long> celestialIds) {
			if (DateTime.UtcNow - _lfTablesLast < TimeSpan.FromHours(24))
				return;
			_lfTablesLast = DateTime.UtcNow;
			try {
				_lfEffects ??= new LfEffectsStore(LfEffectsStore.PathFor(_tbotInstance.InstanceAlias));
				var done = new HashSet<int>();
				int changed = 0;
				foreach (long cel in celestialIds.Distinct().Take(3)) {
					var ids = new List<int>();
					foreach (string comp in new[] { "lfbuildings", "lfresearch" }) {
						string page = await _ogameService.GetLfPageHtml(cel, comp);
						foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(page ?? "", @"data-technology=""(\d{4,6})"""))
							ids.Add(int.Parse(m.Groups[1].Value));
					}
					if (ids.Count == 0) {
						var celObj = (_tbotInstance.UserData.celestials ?? new List<Celestial>()).FirstOrDefault(c => c.ID == cel);
						int rasse = celObj != null ? (int) celObj.LFtype : 0;
						bool alle = rasse < 1 || rasse > 4;
						ids.AddRange(Enum.GetValues<LFBuildables>().Select(x => (int) x).Where(x => x >= 11000 && (alle || x / 1000 == 10 + rasse)));
						ids.AddRange(Enum.GetValues<LFTechno>().Select(x => (int) x).Where(x => x >= 11000 && (alle || x / 1000 == 10 + rasse)));
					}
					foreach (int id in ids.Distinct()) {
						if (!done.Add(id)) continue;
						if (_lfEffects.HasFreshTable(id, TimeSpan.FromDays(30), DateTime.UtcNow)) continue;
						string html = await _ogameService.GetLfTechInfoHtml(cel, id);
						var nm = System.Text.RegularExpressions.Regex.Match(html ?? "", @"data-title=""Technik - ([^""]*)""");
						changed += _lfEffects.AddTable(id, nm.Success ? System.Net.WebUtility.HtmlDecode(nm.Groups[1].Value) : "", LfEffectsStore.ParseTechInfo(html), DateTime.UtcNow);
						await System.Threading.Tasks.Task.Delay(1500);
					}
				}
				if (changed > 0)
					DoLog(LogLevel.Information, $"Lifeform tables: {changed} level value(s) of {done.Count} building/research entries saved.");
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Lifeform tables could not be read: {e.Message}");
			}
		}

		private void WriteLfSnapshot(LFBonuses lf) {
			try {
				if (lf == null)
					return;
				var ex = lf.Expeditions ?? new LFBonusesExpeditions();
				float Cargo(Buildables b) => lf.LfShipBonusesInt.TryGetValue((int) b, out var v) ? v.CargoCapacity : 0;
				float Fuel(Buildables b) => lf.LfShipBonusesInt.TryGetValue((int) b, out var v) ? v.FuelConsumption : 0;
				long gtNeeded = 0; int gtCapacity = 0;
				try {
					int hyper = _tbotInstance.UserData.researches?.HyperspaceTechnology ?? 0;
					var cls = _tbotInstance.UserData.userInfo.Class;
					gtNeeded = _calculationService.CalcIdealExpeditionShips(Buildables.LargeCargo, hyper, lf.LfResourceBonuses?.ResourcesExpedition ?? 0, lf.LfShipBonusesInt, _tbotInstance.UserData.serverData, cls).LargeCargo;
					gtCapacity = _calculationService.CalcShipCapacity(Buildables.LargeCargo, hyper, _tbotInstance.UserData.serverData, Cargo(Buildables.LargeCargo), cls);
				} catch (Exception) {
				}
				if (gtNeeded > 0)
					_tbotInstance.UserData.ExpoLargeCargoNeeded = gtNeeded;
				var snapshot = new {
					updatedUtc = DateTime.UtcNow,
					expeditionBonuses = lf.ExpeditionBonuses,
					expoFinds = _expoStore?.Current,
					suggestion = new { largeCargoPerExpedition = gtNeeded, capacityPerLargeCargo = gtCapacity },
					expeditions = new { ex.Ships, Resources = Math.Max(ex.Resources, lf.LfResourceBonuses?.ResourcesExpedition ?? 0), ex.Speed, ex.DarkMatter, ex.FleetLoss, ex.Slots, ex.LessEnemies },
					cargo = new { LargeCargo = Cargo(Buildables.LargeCargo), SmallCargo = Cargo(Buildables.SmallCargo), Pathfinder = Cargo(Buildables.Pathfinder) },
					fuel = new { LargeCargo = Fuel(Buildables.LargeCargo), Pathfinder = Fuel(Buildables.Pathfinder) },
					topScore = (float) _tbotInstance.UserData.serverData.TopScore,
					ecoSpeed = (int) _tbotInstance.UserData.serverData.Speed,
					playerClass = _tbotInstance.UserData.userInfo.Class.ToString(),
					discovererClass = lf.CharacterClassesBonuses?.Characterclasses3 ?? 0,
					fleetSlots = lf.FleetSlots,
					explorations = lf.Explorations
				};
				string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
				System.IO.Directory.CreateDirectory(dir);
				string file = System.IO.Path.Combine(dir, $"lf_expedition_{System.IO.Path.GetFileName(_tbotInstance.InstanceAlias)}.json");
				AtomicFile.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(snapshot));
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"LF snapshot could not be written: {e.Message}");
			}
		}

		private int MaxExpeditionsForOrigin(Celestial origin, int globalMax) {
			try {
				foreach (var configured in _tbotInstance.InstanceSettings.Expeditions.Origin) {
					var type = Enum.Parse<Celestials>(configured.Type.ToString(), true);
					if (origin.Coordinate.Galaxy == (int)configured.Galaxy &&
						origin.Coordinate.System == (int)configured.System &&
						origin.Coordinate.Position == (int)configured.Position &&
						origin.Coordinate.Type == type) {
						if (SettingsService.IsSettingSet(configured, "MaxExpeditions")) {
							int perOrigin = Convert.ToInt32(configured.MaxExpeditions);
							if (perOrigin > 0)
								return perOrigin;
						}
						return globalMax;
					}
				}
			} catch (Exception e) {
				bool warn = _repeat.ShouldLog(RepeatOriginConfig, e.Message, DateTime.UtcNow, TimeSpan.FromHours(6));
				DoLog(warn ? LogLevel.Warning : LogLevel.Debug, $"Expeditions.Origin MaxExpeditions could not be read, using the global value: {e.Message}");
			}
			return globalMax;
		}

		protected override void OnWorkerStarting() {
			var configured = _tbotInstance.InstanceSettings.Expeditions.Origin;
			if (configured == null || configured.Length == 0) {
				DoLog(LogLevel.Information, "Expedition origins in the loaded settings: none set (automatic choice).");
				return;
			}
			var parts = new List<string>();
			foreach (var o in configured) {
				string text = $"{o.Galaxy}:{o.System}:{o.Position} {(o.Type == null ? "Planet" : o.Type.ToString())}";
				try {
					if (SettingsService.IsSettingSet(o, "MaxExpeditions") && (int) o.MaxExpeditions > 0)
						text += $" (max {(int) o.MaxExpeditions})";
				} catch (Exception) { }
				parts.Add(text);
			}
			DoLog(LogLevel.Information, $"Expedition origins in the loaded settings ({parts.Count}): {string.Join(", ", parts)}");
		}

		private List<Celestial> ResolveExpeditionOrigins() {
			var available = (_tbotInstance.UserData.celestials ?? new List<Celestial>())
				.Where(c => c?.Coordinate != null)
				.ToList();
			var origins = new List<Celestial>();

			if (_tbotInstance.InstanceSettings.Expeditions.Origin.Length > 0) {
				foreach (var configured in _tbotInstance.InstanceSettings.Expeditions.Origin) {
					try {
						var type = Enum.Parse<Celestials>(configured.Type.ToString(), true);
						var match = available.FirstOrDefault(c =>
							c.Coordinate.Galaxy == (int)configured.Galaxy &&
							c.Coordinate.System == (int)configured.System &&
							c.Coordinate.Position == (int)configured.Position &&
							c.Coordinate.Type == type);

						if (match == null) {
							DoLog(LogLevel.Warning,
								$"Expedition origin not found: {configured.Galaxy}:{configured.System}:{configured.Position} ({type})");
							continue;
						}

						if (!origins.Any(o => o.Coordinate.IsSame(match.Coordinate)))
							origins.Add(match);
					} catch (Exception e) {
						DoLog(LogLevel.Warning, $"Unable to parse expedition origin: {e.Message}");
					}
				}
			} else {
				var best = available
					.OrderBy(c => c.Coordinate.Type == Celestials.Moon)
					.ThenByDescending(c => c.Ships == null ? 0 : _calculationService.CalcFleetCapacity(
						c.Ships,
						_tbotInstance.UserData.serverData,
						_tbotInstance.UserData.researches.HyperspaceTechnology,
						null,
						_tbotInstance.UserData.userInfo.Class,
						_tbotInstance.UserData.serverData.ProbeCargo))
					.FirstOrDefault();
				if (best != null)
					origins.Add(best);
			}

			return (bool)_tbotInstance.InstanceSettings.Expeditions.RandomizeOrder
				? origins.Shuffle().ToList()
				: origins;
		}

		private Ships BuildManualExpeditionFleet() {
			return new Ships(
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.LightFighter,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.HeavyFighter,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Cruiser,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Battleship,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Battlecruiser,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Bomber,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Destroyer,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Deathstar,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.SmallCargo,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.LargeCargo,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.ColonyShip,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Recycler,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.EspionageProbe,
				0,
				0,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Reaper,
				(long)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Pathfinder);
		}

		private Ships BuildSmartExpeditionFleet(Ships availableShips, Buildables primaryShip, LFBonuses lfBonuses, int pendingFromOrigin) {
			var researches = _tbotInstance.UserData.researches;
			var serverData = _tbotInstance.UserData.serverData;
			var playerClass = _tbotInstance.UserData.userInfo.Class;
			var exp = _tbotInstance.InstanceSettings.Expeditions;

			Buildables mixShip = Buildables.SmallCargo;
			if (SettingsService.IsSettingSet(exp, "SmartMixShip")) {
				string mixText = (string) exp.SmartMixShip;
				if (!Enum.TryParse<Buildables>(mixText, true, out mixShip))
					mixShip = Buildables.SmallCargo;
			}

			Func<Buildables, int> capacityOf = ship => {
				float cargoBonus = lfBonuses != null && lfBonuses.LfShipBonusesInt != null && lfBonuses.LfShipBonusesInt.TryGetValue((int) ship, out var shipBonus) ? shipBonus.CargoCapacity : 0;
				return _calculationService.CalcShipCapacity(ship, researches.HyperspaceTechnology, serverData, cargoBonus, playerClass, serverData.ProbeCargo);
			};
			float expeditionBonus = lfBonuses?.LfResourceBonuses != null ? lfBonuses.LfResourceBonuses.ResourcesExpedition : 0;
			var ideal = _calculationService.CalcIdealExpeditionShips(primaryShip, researches.HyperspaceTechnology, expeditionBonus, lfBonuses?.LfShipBonusesInt, serverData, playerClass, serverData.ProbeCargo);
			long fuelCargo = SettingsService.IsSettingSet(exp, "FuelToCarry") ? Convert.ToInt64(exp.FuelToCarry) : 0;

			var fleet = SmartExpeditionFleet.Build(new SmartExpeditionInput {
				Available = availableShips,
				Primary = primaryShip,
				Mix = mixShip,
				Pending = pendingFromOrigin,
				IdealPrimary = ideal.GetAmount(primaryShip),
				ExtraCapacity = fuelCargo,
				CapacityPrimary = capacityOf(primaryShip),
				CapacityMix = mixShip == Buildables.Null ? 0 : capacityOf(mixShip)
			});
			DoLog(LogLevel.Debug, $"SmartShips: {pendingFromOrigin} expedition(s) still planned from this origin, fleet {fleet}.");
			return fleet;
		}

		private Ships BuildExpeditionFleet(Celestial origin, LFBonuses lfBonuses, int pendingFromOrigin = 1) {
			if ((bool)_tbotInstance.InstanceSettings.Expeditions.ManualShips.Active)
				return BuildManualExpeditionFleet();

			Buildables primaryShip = Buildables.LargeCargo;
			if (!Enum.TryParse<Buildables>(_tbotInstance.InstanceSettings.Expeditions.PrimaryShip.ToString(), true, out primaryShip)) {
				DoLog(LogLevel.Warning, "Unable to parse PrimaryShip. Using LargeCargo.");
				primaryShip = Buildables.LargeCargo;
			}
			if (primaryShip == Buildables.Null)
				return new Ships();

			var availableShips = origin.Ships?.GetMovableShips() ?? new Ships();
			if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "PrimaryToKeep") &&
				(int)_tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep > 0) {
				availableShips.SetAmount(primaryShip, Math.Max(0,
					availableShips.GetAmount(primaryShip) - (long)_tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep));
			}

			bool smartShips = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "SmartShips") && (bool) _tbotInstance.InstanceSettings.Expeditions.SmartShips;
			Ships autoFleet = smartShips ? BuildSmartExpeditionFleet(availableShips, primaryShip, lfBonuses, pendingFromOrigin) : _calculationService.CalcFullExpeditionShips(
				availableShips,
				primaryShip,
				1,
				_tbotInstance.UserData.serverData,
				_tbotInstance.UserData.researches,
				lfBonuses,
				_tbotInstance.UserData.userInfo.Class,
				_tbotInstance.UserData.serverData.ProbeCargo);

			if (!smartShips) try {
				long fuelCargo = 0;
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "FuelToCarry"))
					fuelCargo = Convert.ToInt64(_tbotInstance.InstanceSettings.Expeditions.FuelToCarry);
				if (fuelCargo > 0 && autoFleet.GetAmount(primaryShip) > 0) {
					float cargoBonus = lfBonuses != null && lfBonuses.LfShipBonusesInt != null && lfBonuses.LfShipBonusesInt.TryGetValue((int) primaryShip, out var shipBonus) ? shipBonus.CargoCapacity : 0;
					int capacity = _calculationService.CalcShipCapacity(primaryShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
					if (capacity > 0) {
						long extra = (fuelCargo + capacity - 1) / capacity;
						long add = Math.Min(extra, Math.Max(0, availableShips.GetAmount(primaryShip) - autoFleet.GetAmount(primaryShip)));
						if (add > 0) {
							autoFleet.Add(primaryShip, add);
							DoLog(LogLevel.Debug, $"FuelToCarry {fuelCargo:N0} needs cargo space: {add:N0} extra {primaryShip} added on {origin.Coordinate} (capacity {capacity:N0} each).");
						}
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Expeditions.FuelToCarry could not be added to the fleet size: {e.Message}");
			}

			int minPrimary = 0;
			try {
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "MinPrimaryToSend"))
					minPrimary = Convert.ToInt32(_tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend);
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Expeditions.MinPrimaryToSend could not be read, ignored: {e.Message}");
			}
			if (minPrimary > 0 && autoFleet.GetAmount(primaryShip) < minPrimary) {
				DoLog(LogLevel.Information, $"Only {autoFleet.GetAmount(primaryShip):N0} {primaryShip} available on {origin.Coordinate} after PrimaryToKeep, MinPrimaryToSend is {minPrimary:N0} - no expedition from this origin.");
				return new Ships();
			}
			return autoFleet;
		}

		private async Task<bool> ExpeditionBedarfReicht(Celestial origin, Ships fleet, Coordinate destination, decimal speed, int pending, LFBonuses lf) {
			try {
				var exp = _tbotInstance.InstanceSettings.Expeditions;
				if (!SettingsService.IsSettingSet(exp, "StopBelowNeed") || !(bool) exp.StopBelowNeed)
					return true;
				long fuelToCarry = SettingsService.IsSettingSet(exp, "FuelToCarry") ? Convert.ToInt64(exp.FuelToCarry) : 0;
				long flug = _calculationService.CalcFleetPrediction(origin.Coordinate, destination, fleet, Missions.Expedition, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, lf, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Fuel;
				long bedarf = (flug + Math.Max(0, fuelToCarry)) * Math.Max(1, pending);
				var aktuell = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
				long vorhanden = (long) (aktuell?.Resources?.Deuterium ?? 0);
				if (vorhanden >= bedarf)
					return true;
				DoLog(LogLevel.Warning, $"Expedition from {origin.Coordinate} stopped: deuterium {vorhanden:N0} is below the need {bedarf:N0} (flight fuel {flug:N0} + cargo {fuelToCarry:N0} per expedition x {Math.Max(1, pending)}); waiting for the resupply from the base.");
				string schluessel = ExpeditionDiagnostics.DeutKey(_tbotInstance.InstanceAlias, _tbotInstance.UserData?.serverData?.Name, origin.Coordinate.ToString() + "|need");
				lock (_deutMeldung) {
					if (_deutMeldung.TryGetValue(schluessel, out DateTime zuletzt) && (DateTime.UtcNow - zuletzt).TotalHours < 6)
						return false;
					_deutMeldung[schluessel] = DateTime.UtcNow;
				}
				await _tbotInstance.SendTelegramMessage($"Expeditions from {origin.Coordinate} paused: deuterium {vorhanden:N0} is below the need {bedarf:N0}. They continue as soon as the base has resupplied.");
				return false;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Deuterium need check for {origin.Coordinate} failed: {e.Message}");
				return true;
			}
		}

		private static readonly Dictionary<string, DateTime> _deutMeldung = new();
		private async Task<bool> ExpeditionDeuteriumReicht(Celestial origin) {
			try {
				if (origin.Coordinate.Type != Celestials.Moon)
					return true;
				var exp = _tbotInstance.InstanceSettings.Expeditions;
				if (!SettingsService.IsSettingSet(exp, "MinDeuteriumOnMoon"))
					return true;
				long min = (long) exp.MinDeuteriumOnMoon;
				if (min <= 0)
					return true;
				var aktuell = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
				long vorhanden = (long) (aktuell?.Resources?.Deuterium ?? 0);
				if (vorhanden >= min)
					return true;
								DoLog(LogLevel.Information, $"Expedition moon {origin.Coordinate}: deuterium {vorhanden:N0} is below the target {min:N0}; expeditions continue, the repatriate round resupplies deuterium from the base.");
				string schluessel = ExpeditionDiagnostics.DeutKey(_tbotInstance.InstanceAlias, _tbotInstance.UserData?.serverData?.Name, origin.Coordinate.ToString());
				lock (_deutMeldung) {
					if (_deutMeldung.TryGetValue(schluessel, out DateTime zuletzt) && (DateTime.UtcNow - zuletzt).TotalHours < 6)
						return true;
					_deutMeldung[schluessel] = DateTime.UtcNow;
				}
				await _tbotInstance.SendTelegramMessage($"Expedition moon {origin.Coordinate}: deuterium {vorhanden:N0} is below the target {min:N0}. Expeditions keep running, the base resupplies the moon in the next repatriate round.");
				return true;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Deuterium check for {origin.Coordinate} failed: {e.Message}");
				return true;
			}
		}

		private async Task<Ships> RefreshExpeditionShips(Celestial origin) {
			try {
				origin.Ships = await _ogameService.GetShips(origin);
				return origin.Ships;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Unable to refresh expedition ships on {origin.Coordinate}: {e.Message}");
				return null;
			}
		}

		private Coordinate BuildExpeditionDestination(Celestial origin, int expeditionCount, List<int> usedSystems, Random random) {
			if (!(bool)_tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
				return new Coordinate {
					Galaxy = origin.Coordinate.Galaxy,
					System = origin.Coordinate.System,
					Position = 16,
					Type = Celestials.DeepSpace
				};
			}

			int range = Math.Max(1, (int)_tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Range);
			while (expeditionCount > range * 2)
				range++;

			for (int attempt = 0; attempt < 100; attempt++) {
				int system = GeneralHelper.WrapSystem(random.Next(
					origin.Coordinate.System - range,
					origin.Coordinate.System + range + 1));
				if (usedSystems.Contains(system))
					continue;

				usedSystems.Add(system);
				return new Coordinate {
					Galaxy = origin.Coordinate.Galaxy,
					System = system,
					Position = 16,
					Type = Celestials.DeepSpace
				};
			}

			return new Coordinate {
				Galaxy = origin.Coordinate.Galaxy,
				System = origin.Coordinate.System,
				Position = 16,
				Type = Celestials.DeepSpace
			};
		}

		private volatile int _sleepWaveSpeedStep;

		public void SetSleepWaveSpeed(int speedStep) {
			_sleepWaveSpeedStep = Math.Max(0, Math.Min(10, speedStep));
		}

		private volatile string _sleepWaveReturnAt;
		private volatile string _sleepWaveWakeUp;
		private bool _sleepWaveTargetWarned;

		public void SetSleepWaveReturn(string returnAt, string wakeUp) {
			_sleepWaveReturnAt = string.IsNullOrWhiteSpace(returnAt) ? null : returnAt.Trim();
			_sleepWaveWakeUp = wakeUp;
			_sleepWaveTargetWarned = false;
		}

		private DateTime? ResolveSleepWaveTarget(DateTime now) {
			string spec = _sleepWaveReturnAt;
			if (string.IsNullOrWhiteSpace(spec))
				return null;
			DateTime baseTime;
			int offsetMinutes = 0;
			var m = Regex.Match(spec, @"^wake\s*-?\s*up\s*(?:([+-])\s*(\d+))?$", RegexOptions.IgnoreCase);
			if (m.Success) {
				if (!DateTime.TryParse(_sleepWaveWakeUp, out baseTime))
					return null;
				if (m.Groups[2].Success)
					offsetMinutes = int.Parse(m.Groups[2].Value) * (m.Groups[1].Value == "-" ? -1 : 1);
			} else if (!DateTime.TryParse(spec, out baseTime)) {
				if (!_sleepWaveTargetWarned) {
					DoLog(LogLevel.Warning, $"Sleep wave: ReturnAt '{spec}' not understood (use \"WakeUp\", \"WakeUp+15\", \"WakeUp-10\" or \"HH:mm\") - falling back to SpeedPercent.");
					_sleepWaveTargetWarned = true;
				}
				return null;
			}
			while (baseTime <= now)
				baseTime = baseTime.AddDays(1);
			return baseTime.AddMinutes(offsetMinutes);
		}

		private async Task<(Coordinate destination, decimal speed)?> PlanSleepWaveExpedition(Celestial origin, Ships fleet, int expeditionCount, List<int> usedSystems, LFBonuses lfBonuses) {
			try {
				if (_sleepWaveReturnAt == null)
					return null;
				DateTime now = await _tbotOgameBridge.GetDateTime();
				DateTime? targetOrNull = ResolveSleepWaveTarget(now);
				if (targetOrNull == null)
					return null;
				DateTime target = targetOrNull.Value;

				var systems = new List<int>();
				if ((bool) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
					int range = Math.Max(1, (int) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Range);
					while (expeditionCount > range * 2)
						range++;
					for (int s = origin.Coordinate.System - range; s <= origin.Coordinate.System + range; s++) {
						int sys = GeneralHelper.WrapSystem(s);
						if (!usedSystems.Contains(sys) && !systems.Contains(sys))
							systems.Add(sys);
					}
				}
				if (systems.Count == 0)
					systems.Add(origin.Coordinate.System);

				var speeds = _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Where(s => s > 0).ToList();
				if (speeds.Count == 0)
					return null;

				Coordinate bestDest = null;
				decimal bestSpeed = 0;
				double bestDiff = double.MaxValue;
				DateTime bestReturn = default;
				foreach (int sys in systems) {
					var dest = new Coordinate { Galaxy = origin.Coordinate.Galaxy, System = sys, Position = 16, Type = Celestials.DeepSpace };
					foreach (decimal speed in speeds) {
						var prediction = _calculationService.CalcFleetPrediction(origin.Coordinate, dest, fleet, Missions.Expedition, speed,
							_tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, lfBonuses,
							_tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						DateTime ret = now.AddSeconds(2 * prediction.Time + 3600);
						double diff = Math.Abs((ret - target).TotalSeconds);
						if (diff < bestDiff || (diff == bestDiff && speed > bestSpeed)) {
							bestDiff = diff; bestDest = dest; bestSpeed = speed; bestReturn = ret;
						}
					}
				}
				if (bestDest == null)
					return null;

				if ((bool) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active)
					usedSystems.Add(bestDest.System);
				double offMin = (bestReturn - target).TotalMinutes;
				DoLog(bestDiff > 600 ? LogLevel.Warning : LogLevel.Information,
					$"Sleep wave: target return {target:dd.MM. HH:mm}, chosen system {bestDest.System} at {bestSpeed * 10}% -> return about {bestReturn:dd.MM. HH:mm} ({offMin:+0.#;-0.#;0} min from target){(bestDiff > 600 ? " - window not reachable exactly, closest match used" : "")}.");
				return (bestDest, bestSpeed);
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Sleep wave: return-time planning failed ({e.Message}) - falling back to SpeedPercent.");
				return null;
			}
		}

		private decimal CurrentExpeditionSpeed() {
			int step = _sleepWaveSpeedStep;
			return step > 0 ? step : Speeds.HundredPercent;
		}

		public async Task<bool> RunSleepWaveAsync(int speedStep, TimeSpan lockTimeout, string returnAt = null, string wakeUp = null) {
			var sem = GetSemaphore();
			if (!await sem.WaitAsync(lockTimeout)) {
				DoLog(LogLevel.Warning, "Sleep wave: expeditions worker is busy, wave skipped.");
				return false;
			}
			bool? prevAll = null, prevMajority = null;
			try {
				try { prevAll = (bool) _tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions; } catch { }
				try { prevMajority = (bool) _tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions; } catch { }
				if (prevAll.HasValue)
					_tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions = false;
				if (prevMajority.HasValue)
					_tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions = false;
				SetSleepWaveSpeed(speedStep);
				SetSleepWaveReturn(returnAt, wakeUp);
				_tbotInstance.SleepWaveActive = true;
				DoLog(LogLevel.Information, string.IsNullOrWhiteSpace(returnAt)
					? $"Sleep wave: sending expeditions at {speedStep * 10}% speed before going to sleep."
					: $"Sleep wave: sending expeditions timed to return at '{returnAt}' (fallback {speedStep * 10}% speed) before going to sleep.");
				await Execute();
				return true;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Sleep wave failed: {e.Message}");
				return false;
			} finally {
				_tbotInstance.SleepWaveActive = false;
				SetSleepWaveSpeed(0);
				SetSleepWaveReturn(null, null);
				if (prevAll.HasValue)
					_tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions = prevAll.Value;
				if (prevMajority.HasValue)
					_tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions = prevMajority.Value;
				sem.Release();
			}
		}

		protected override TimeSpan ExecuteTimeout {
			get {
				try {
					int maxPerOrigin = (int?) _tbotInstance.InstanceSettings.Expeditions.MaxExpeditionsPerOrigin ?? 13;
					int maxWaitNextFleet = (int) _tbotInstance.InstanceSettings.Expeditions.MaxWaitNextFleet;
					int originCount = 1;
					try {
						originCount = Math.Max(1, ((IEnumerable<object>) _tbotInstance.InstanceSettings.Expeditions.Origin).Count());
					} catch (Exception) { }

					double perFleetSeconds = maxWaitNextFleet + 5;
					double estimatedSeconds = maxPerOrigin * originCount * perFleetSeconds * 3;
					var computed = TimeSpan.FromSeconds(estimatedSeconds);

					var floor = TimeSpan.FromMinutes(3);
					var ceiling = TimeSpan.FromMinutes(30);
					if (computed < floor) return floor;
					if (computed > ceiling) return ceiling;
					return computed;
				} catch (Exception) {
					return TimeSpan.FromMinutes(15);
				}
			}
		}

		private void LogNoSend(string reason, long intervalMs) {
			try {
				string text = "Expeditions: nothing sent - " + (reason ?? "reason not determined.");
				bool info = _repeat.ShouldLog(RepeatNoSend, text, DateTime.UtcNow, TimeSpan.FromMinutes(30));
				DoLog(info ? LogLevel.Information : LogLevel.Debug, $"{text} Next check at {DateTime.Now.AddMilliseconds(intervalMs)}.");
			} catch (Exception) {
			}
		}

		private sealed class ExpCycle {
			public long Id;
			public string Tag;
			public System.Diagnostics.Stopwatch Uhr = System.Diagnostics.Stopwatch.StartNew();
			public TimeSpan Timeout;
			public int Versuche;
			public int Gesendet;
			public readonly HashSet<string> Gemeldet = new();
		}
		private long _zyklusZaehler;
		private long _neuesterZyklus;
		private int _aktiveZyklen;
		private long _versuchZaehler;

		private bool PruefeVeraltet(ExpCycle c, string ort, bool immerMelden = false) {
			try {
				long neuester = System.Threading.Interlocked.Read(ref _neuesterZyklus);
				bool veraltet = neuester != c.Id;
				bool ueberZeit = c.Uhr.Elapsed > c.Timeout;
				if ((veraltet || ueberZeit) && (immerMelden || c.Gemeldet.Add(ort))) {
					if (veraltet)
						DoLog(LogLevel.Warning, $"[{c.Tag}] STALE cycle still active at '{ort}': newer cycle E{neuester} has already started (this one is {c.Uhr.Elapsed.TotalSeconds:F0}s old, ExecuteTimeout {c.Timeout.TotalSeconds:F0}s, sent {c.Gesendet}). Two expedition cycles are running in parallel.");
					else
						DoLog(LogLevel.Warning, $"[{c.Tag}] cycle is past ExecuteTimeout at '{ort}' ({c.Uhr.Elapsed.TotalSeconds:F0}s > {c.Timeout.TotalSeconds:F0}s): WorkerBase has abandoned it, the worker lock stays held until it ends (or 10 min without heartbeat).");
				}
				return veraltet;
			} catch (Exception) {
				return false;
			}
		}

		protected override async Task Execute() {
			var c = new ExpCycle();
			try {
				c.Id = System.Threading.Interlocked.Increment(ref _zyklusZaehler);
				c.Tag = $"E{c.Id}";
				c.Timeout = ExecuteTimeout;
				int aktiv = System.Threading.Interlocked.Increment(ref _aktiveZyklen);
				long vorher = System.Threading.Interlocked.Exchange(ref _neuesterZyklus, c.Id);
				if (aktiv > 1)
					DoLog(LogLevel.Warning, $"[{c.Tag}] cycle start while {aktiv - 1} older cycle(s) are still running (previous newest: E{vorher}) - overlapping expedition cycles. Thread {Environment.CurrentManagedThreadId}.");
				else
					DoLog(LogLevel.Debug, $"[{c.Tag}] cycle start (ExecuteTimeout {c.Timeout.TotalSeconds:F0}s, thread {Environment.CurrentManagedThreadId}).");
			} catch (Exception) {
			}
			string ergebnis = "completed";
			try {
				await ExecuteCore(c);
			} catch (Exception e) {
				ergebnis = "exception: " + e.Message;
				throw;
			} finally {
				try {
					int uebrig = System.Threading.Interlocked.Decrement(ref _aktiveZyklen);
					long neuester = System.Threading.Interlocked.Read(ref _neuesterZyklus);
					string zusatz = $"{c.Uhr.Elapsed.TotalSeconds:F0}s, attempts {c.Versuche}, sent {c.Gesendet}, result {ergebnis}, other cycles still active {uebrig}";
					if (neuester != c.Id)
						DoLog(LogLevel.Warning, $"[{c.Tag}] STALE cycle finished after newer cycle E{neuester} had started ({zusatz}, ExecuteTimeout {c.Timeout.TotalSeconds:F0}s). Its end handling may still have changed the worker period / ended the worker.");
					else if (c.Uhr.Elapsed > c.Timeout)
						DoLog(LogLevel.Warning, $"[{c.Tag}] cycle finished only after ExecuteTimeout ({zusatz}, ExecuteTimeout {c.Timeout.TotalSeconds:F0}s); WorkerBase had abandoned it, no newer cycle had started.");
					else
						DoLog(LogLevel.Debug, $"[{c.Tag}] cycle end ({zusatz}).");
				} catch (Exception) {
				}
			}
		}

		private async Task ExecuteCore(ExpCycle cyc) {
			bool stop = false;
			bool delay = false;
			string noSendReason = null;

			try {
				if (!(bool)_tbotInstance.InstanceSettings.Expeditions.Active)
					return;

				_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
				if (_tbotInstance.UserData.researches.Astrophysics == 0) {
					DoLog(LogLevel.Information, "Skipping: Astrophysics not yet researched!");
					ChangeWorkerPeriod(RandomizeHelper.CalcRandomInterval(IntervalType.AboutHalfAnHour));
					return;
				}

				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				_tbotInstance.UserData.serverData = await _ogameService.GetServerData();
				GetDurationTracker().Observe(_tbotInstance.UserData.fleets, DateTime.UtcNow);

				var rankSlotsPriority = new List<RankSlotsPriority> {
					new RankSlotsPriority(Feature.BrainAutoMine,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.Brain,
						(bool)_tbotInstance.InstanceSettings.Brain.Active && (bool)_tbotInstance.InstanceSettings.Brain.Transports.Active &&
						((bool)_tbotInstance.InstanceSettings.Brain.AutoMine.Active ||
						 (bool)_tbotInstance.InstanceSettings.Brain.AutoResearch.Active ||
						 (bool)_tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active ||
						 (bool)_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active),
						(int)_tbotInstance.InstanceSettings.Brain.Transports.MaxSlots,
						(int)_tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Transport)),
					new RankSlotsPriority(Feature.Expeditions,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.Expeditions,
						true,
						(int)_tbotInstance.UserData.slots.ExpTotal,
						(int)_tbotInstance.UserData.slots.ExpInUse),
					new RankSlotsPriority(Feature.AutoFarm,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoFarm,
						(bool)_tbotInstance.InstanceSettings.AutoFarm.Active,
						(int)_tbotInstance.InstanceSettings.AutoFarm.MaxSlots,
						(int)_tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Attack)),
					new RankSlotsPriority(Feature.Colonize,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.Colonize,
						(bool)_tbotInstance.InstanceSettings.AutoColonize.Active,
						(bool)_tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active ? (int)_tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MaxSlots : 1,
						(int)_tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Colonize)),
					new RankSlotsPriority(Feature.AutoDiscovery,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoDiscovery,
						(bool)_tbotInstance.InstanceSettings.AutoDiscovery.Active,
						(int)_tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots,
						(int)_tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Discovery)),
					new RankSlotsPriority(Feature.Harvest,
						(int)_tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoHarvest,
						(bool)_tbotInstance.InstanceSettings.AutoHarvest.Active,
						(int)_tbotInstance.InstanceSettings.AutoHarvest.MaxSlots,
						(int)_tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Harvest))
				};

				int maxSlots = Math.Max(0,
					_tbotInstance.UserData.slots.Total - (int)_tbotInstance.InstanceSettings.General.SlotsToLeaveFree);
				int expeditionsToSend = Math.Min(
					Math.Min(_tbotInstance.UserData.slots.ExpFree, _tbotInstance.UserData.slots.Free),
					maxSlots);

				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "WaitForAllExpeditions") &&
					(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions &&
					_tbotInstance.UserData.slots.ExpInUse > 0)
					expeditionsToSend = 0;

				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "WaitForMajorityOfExpeditions") &&
					(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions &&
					(double)expeditionsToSend < Math.Round((double)_tbotInstance.UserData.slots.ExpTotal / 2D, 0, MidpointRounding.ToZero) + 1D)
					expeditionsToSend = 0;

				int? prioritySlotsResult = null;
				if (expeditionsToSend > 0) {
					int prioritySlots = _calculationService.CalcSlotsPriority(
						Feature.Expeditions,
						rankSlotsPriority,
						_tbotInstance.UserData.slots,
						_tbotInstance.UserData.fleets,
						(int)_tbotInstance.InstanceSettings.General.SlotsToLeaveFree);
					prioritySlotsResult = prioritySlots;
					expeditionsToSend = Math.Min(expeditionsToSend, Math.Max(0, prioritySlots));
				}

				if (expeditionsToSend <= 0) {
					var exp = _tbotInstance.InstanceSettings.Expeditions;
					bool waitAll = SettingsService.IsSettingSet(exp, "WaitForAllExpeditions") && (bool) exp.WaitForAllExpeditions;
					bool waitMajority = SettingsService.IsSettingSet(exp, "WaitForMajorityOfExpeditions") && (bool) exp.WaitForMajorityOfExpeditions;
					noSendReason = ExpeditionDiagnostics.ExplainNoSend(
						(int) _tbotInstance.UserData.slots.ExpTotal, (int) _tbotInstance.UserData.slots.ExpInUse,
						_tbotInstance.UserData.slots.Free, _tbotInstance.UserData.slots.Total,
						(int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree, waitAll, waitMajority, prioritySlotsResult)
						?? "no expedition may be sent (reason not determined).";
					delay = true;
					return;
				}

				if (!_tbotInstance.GhostSleepAllowsNewExpedition(GetDurationTracker().GetMaxDurationSeconds())) {
					DoLog(LogLevel.Information, "GhostSleep drain: no new expeditions before sleeping - they would not be back in time.");
					noSendReason = "GhostSleep drain: new expeditions would not be back before bedtime.";
					delay = true;
					return;
				}

				var origins = ResolveExpeditionOrigins();
				if (origins.Count == 0) {
					DoLog(LogLevel.Warning, "No valid expedition origin is configured.");
					noSendReason = "no valid expedition origin is configured.";
					delay = true;
					return;
				}

				var lfBonuses = await _ogameService.GetLFBonuses();
				await RefreshExpoFinds();
				await RefreshLfEffects(origins[0].ID);
				await RefreshLfTables(origins.Select(o => (long) o.ID));
				WriteLfSnapshot(lfBonuses);
				int maxPerOrigin = (int?)_tbotInstance.InstanceSettings.Expeditions.MaxExpeditionsPerOrigin ?? 1;
				var defaultMax = new int[origins.Count];
				for (var i = 0; i < origins.Count; i++) {
					defaultMax[i] = maxPerOrigin;
					if (origins.Count > 1 && _tbotInstance.UserData.slots.ExpTotal > 0) {
						int total = (int) _tbotInstance.UserData.slots.ExpTotal;
						defaultMax[i] = Math.Min(maxPerOrigin, total / origins.Count + (i < total % origins.Count ? 1 : 0));
					}
				}
				var capacities = origins
					.Select((origin, i) => Math.Max(0, MaxExpeditionsForOrigin(origin, defaultMax[i]) - CountActiveExpeditionsFromOrigin(origin)))
					.ToArray();
				var sentByOrigin = new int[origins.Count];
				var unavailableOrigins = new bool[origins.Count];
				var retryableUnavailableOrigins = new bool[origins.Count];
				var retriedUnavailableOrigins = false;
				var remaining = expeditionsToSend;
				var planningStart = _nextOriginIndex % origins.Count;
				var random = new Random();
				var usedSystems = new List<int>();

			while (remaining > 0) {
				var residualCapacities = capacities
					.Select((capacity, index) => unavailableOrigins[index]
						? 0
						: Math.Max(0, capacity - sentByOrigin[index]))
					.ToArray();
				var plan = ExpeditionOriginPlanner.BuildRoundRobinPlan(residualCapacities, remaining, planningStart);
				if (plan.Sum() == 0) {
					if (ExpeditionOriginPlanner.ShouldRetryUnavailableOrigins(
						remaining, retryableUnavailableOrigins, retriedUnavailableOrigins)) {
						retriedUnavailableOrigins = true;
						for (var index = 0; index < unavailableOrigins.Length; index++)
							unavailableOrigins[index] = unavailableOrigins[index] && !retryableUnavailableOrigins[index];
						DoLog(LogLevel.Information,
							$"Expedition origin capacity is still short by {remaining}; refreshing previously unavailable origins once.");
						continue;
					}
					break;
				}

				var progressInPass = false;
				for (var offset = 0; offset < origins.Count && remaining > 0; offset++) {
					var originIndex = (planningStart + offset) % origins.Count;
					var count = plan[originIndex];
					if (count <= 0)
						continue;

					var origin = origins[originIndex];
					for (var attempt = 0; attempt < count && remaining > 0; attempt++) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						PruefeVeraltet(cyc, "after UpdateFleets (loop)");
						if (CountActiveExpeditionsFromOrigin(origin) >= MaxExpeditionsForOrigin(origin, defaultMax[originIndex]))
							break;

						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
						PruefeVeraltet(cyc, "after UpdateSlots (loop)");
						if (_tbotInstance.UserData.slots.ExpFree <= 0 ||
							_tbotInstance.UserData.slots.Free <= (int)_tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
							noSendReason = $"slots ran out while sending (expedition slots free {_tbotInstance.UserData.slots.ExpFree}, fleet slots free {_tbotInstance.UserData.slots.Free}/{_tbotInstance.UserData.slots.Total}, SlotsToLeaveFree {(int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree}).";
							delay = true;
							return;
						}

						var refreshedShips = await RefreshExpeditionShips(origin);
						PruefeVeraltet(cyc, "after RefreshExpeditionShips");
						if (refreshedShips == null) {
							unavailableOrigins[originIndex] = true;
							retryableUnavailableOrigins[originIndex] = true;
							break;
						}

						int pendingFromOrigin = Math.Max(1, new[] {
							count - attempt,
							remaining,
							_tbotInstance.UserData.slots.ExpFree,
							_tbotInstance.UserData.slots.Free - (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree,
							MaxExpeditionsForOrigin(origin, defaultMax[originIndex]) - CountActiveExpeditionsFromOrigin(origin)
						}.Min());
						var fleet = BuildExpeditionFleet(origin, lfBonuses, pendingFromOrigin);
						if (fleet == null || fleet.IsEmpty() || !refreshedShips.HasAtLeast(fleet, 1)) {
							DoLog(LogLevel.Information,
								$"No usable expedition fleet on {origin.Coordinate}; excluding this origin until the bounded retry.");
							unavailableOrigins[originIndex] = true;
							retryableUnavailableOrigins[originIndex] = true;
							break;
						}

						if (!await ExpeditionDeuteriumReicht(origin)) {
							unavailableOrigins[originIndex] = true;
							break;
						}

						var wavePlan = await PlanSleepWaveExpedition(origin, fleet, remaining, usedSystems, lfBonuses);
						var destination = wavePlan?.destination ?? BuildExpeditionDestination(origin, remaining, usedSystems, random);
						decimal sendSpeed = wavePlan?.speed ?? CurrentExpeditionSpeed();
						if (!await ExpeditionBedarfReicht(origin, fleet, destination, sendSpeed, pendingFromOrigin, lfBonuses)) {
							unavailableOrigins[originIndex] = true;
							break;
						}
						var payload = new Resources();
						if ((long)_tbotInstance.InstanceSettings.Expeditions.FuelToCarry > 0)
							payload.Deuterium = (long)_tbotInstance.InstanceSettings.Expeditions.FuelToCarry;

						DoLog(LogLevel.Information, $"Sending expedition from {origin.Coordinate} to {destination}");
						string versuchId = $"A{System.Threading.Interlocked.Increment(ref _versuchZaehler)}";
						cyc.Versuche++;
						bool veraltetVorSenden = PruefeVeraltet(cyc, "before SendFleet", true);
						DoLog(veraltetVorSenden ? LogLevel.Warning : LogLevel.Debug,
							$"[{cyc.Tag}/{versuchId}] {(veraltetVorSenden ? "STALE cycle is about to SEND" : "about to send")}: sender {origin.Coordinate} ({origin.Coordinate.Type}), destination {destination}, speed {sendSpeed}, ships {fleet}, cycle age {cyc.Uhr.Elapsed.TotalSeconds:F0}s, attempt {cyc.Versuche} of this cycle, sent so far {cyc.Gesendet}, active cycles {System.Threading.Volatile.Read(ref _aktiveZyklen)}.");
						int fleetId;
						try {
							fleetId = await _fleetScheduler.SendFleet(
								origin,
								fleet,
								destination,
								Missions.Expedition,
								sendSpeed,
								payload,
								shrinkDeuteriumPayload: true);
						} catch (Exception sendEx) {
							DoLog(LogLevel.Warning, $"[{cyc.Tag}/{versuchId}] send attempt from {origin.Coordinate} FAILED with exception after {cyc.Uhr.Elapsed.TotalSeconds:F0}s: {sendEx.Message}");
							throw;
						}
						bool veraltetNachSenden = PruefeVeraltet(cyc, "after SendFleet", true);
						DoLog(veraltetNachSenden || fleetId <= (int)SendFleetCode.GenericError ? LogLevel.Warning : LogLevel.Debug,
							$"[{cyc.Tag}/{versuchId}] send attempt from {origin.Coordinate} finished: result {(fleetId > (int)SendFleetCode.GenericError ? "fleet id " + fleetId : "code " + fleetId)}{(veraltetNachSenden ? " (cycle was STALE)" : "")}, cycle age {cyc.Uhr.Elapsed.TotalSeconds:F0}s.");

						if (fleetId == (int)SendFleetCode.AfterSleepTime) {
							stop = true;
							return;
						}
						if (fleetId == (int)SendFleetCode.NotEnoughSlots) {
							noSendReason = "SendFleet refused the fleet: no free slot (taken by another worker in the meantime, or reserved for the fleetsave).";
							delay = true;
							return;
						}
						if (fleetId <= (int)SendFleetCode.GenericError) {
							DoLog(LogLevel.Warning, $"Expedition was not sent from {origin.Coordinate}; excluding this origin for the rest of the cycle.");
							unavailableOrigins[originIndex] = true;
							break;
						}

						cyc.Gesendet++;
						GetDurationTracker().NoteSent(fleetId, origin.Coordinate, DateTime.UtcNow);
						_repeat.Reset(RepeatNoSend);
						sentByOrigin[originIndex]++;
						remaining--;
						progressInPass = true;
						planningStart = (originIndex + 1) % origins.Count;

						int minWait = (int)_tbotInstance.InstanceSettings.Expeditions.MinWaitNextFleet;
						int maxWait = (int)_tbotInstance.InstanceSettings.Expeditions.MaxWaitNextFleet;
						if (maxWait < minWait)
							(minWait, maxWait) = (maxWait, minWait);
						await Task.Delay((int)RandomizeHelper.CalcRandomIntervalSecToMs(minWait, maxWait), _ct);
						PruefeVeraltet(cyc, "after pause between fleets");
					}
				}

				if (!progressInPass) {
					if (ExpeditionOriginPlanner.ShouldRetryUnavailableOrigins(
						remaining, retryableUnavailableOrigins, retriedUnavailableOrigins)) {
						retriedUnavailableOrigins = true;
						for (var index = 0; index < unavailableOrigins.Length; index++)
							unavailableOrigins[index] = unavailableOrigins[index] && !retryableUnavailableOrigins[index];
						DoLog(LogLevel.Information,
							$"No expedition was dispatched in the pass; refreshing previously unavailable origins once.");
						continue;
					}
					break;
				}
			}

			_nextOriginIndex = planningStart;

				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				GetDurationTracker().Observe(_tbotInstance.UserData.fleets, DateTime.UtcNow);
				var expeditionFleets = _tbotInstance.UserData.fleets
					.Where(f => f.Mission == Missions.Expedition)
					.OrderBy(f => f.BackIn)
					.ToList();
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

				long interval;
				long? nextReturn = ExpeditionDiagnostics.NextReturnSeconds(expeditionFleets);
				if (expeditionFleets.Count == 0 ||
					(_tbotInstance.UserData.slots.ExpFree > 0 &&
					 !(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions &&
					 !(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions)) {
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
				} else if (nextReturn.HasValue) {
					interval = 1000 * nextReturn.Value +
						RandomizeHelper.CalcRandomIntervalSecToMs(
							(int)_tbotInstance.InstanceSettings.Expeditions.MinWaitNextRound,
							(int)_tbotInstance.InstanceSettings.Expeditions.MaxWaitNextRound);
				} else {
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					DoLog(LogLevel.Debug, "No expedition has a valid return time (BackIn missing) - using the default check interval.");
				}

				ChangeWorkerPeriod(interval);
				DoLog(LogLevel.Information, $"Next expedition check at {DateTime.Now.AddMilliseconds(interval)}");
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"HandleExpeditions exception: {e.Message}");
				DoLog(LogLevel.Debug, e.StackTrace);
				ChangeWorkerPeriod(RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo));
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop)
						await EndExecution();

					if (delay) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						long? nextBack = ExpeditionDiagnostics.NextReturnSeconds(_tbotInstance.UserData.fleets);
						if (nextBack.HasValue) {
							interval = nextBack.Value * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} else {
							interval = RandomizeHelper.CalcRandomInterval(
								(int)_tbotInstance.InstanceSettings.Expeditions.MinWaitNextRound,
								(int)_tbotInstance.InstanceSettings.Expeditions.MaxWaitNextRound);
						}
						ChangeWorkerPeriod(interval);
						LogNoSend(noSendReason, interval);
					}

					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}