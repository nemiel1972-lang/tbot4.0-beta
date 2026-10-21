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

		private void WriteLfSnapshot(LFBonuses lf) {
			try {
				if (lf == null)
					return;
				var ex = lf.Expeditions ?? new LFBonusesExpeditions();
				float Cargo(Buildables b) => lf.LfShipBonusesInt.TryGetValue((int) b, out var v) ? v.CargoCapacity : 0;
				float Fuel(Buildables b) => lf.LfShipBonusesInt.TryGetValue((int) b, out var v) ? v.FuelConsumption : 0;
				int gtNeeded = 0, gtCapacity = 0;
				try {
					int hyper = _tbotInstance.UserData.researches?.HyperspaceTechnology ?? 0;
					var cls = _tbotInstance.UserData.userInfo.Class;
					gtNeeded = _calculationService.CalcIdealExpeditionShips(Buildables.LargeCargo, hyper, lf.LfResourceBonuses?.ResourcesExpedition ?? 0, lf.LfShipBonusesInt, _tbotInstance.UserData.serverData, cls).LargeCargo;
					gtCapacity = _calculationService.CalcShipCapacity(Buildables.LargeCargo, hyper, _tbotInstance.UserData.serverData, Cargo(Buildables.LargeCargo), cls);
				} catch (Exception) {
				}
				var snapshot = new {
					updatedUtc = DateTime.UtcNow,
					expeditionBonuses = lf.ExpeditionBonuses,
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
				DoLog(LogLevel.Warning, $"Expeditions.Origin MaxExpeditions could not be read, using the global value: {e.Message}");
			}
			return globalMax;
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

		private Ships BuildExpeditionFleet(Celestial origin, LFBonuses lfBonuses) {
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

			var autoFleet = _calculationService.CalcFullExpeditionShips(
				availableShips,
				primaryShip,
				1,
				_tbotInstance.UserData.serverData,
				_tbotInstance.UserData.researches,
				lfBonuses,
				_tbotInstance.UserData.userInfo.Class,
				_tbotInstance.UserData.serverData.ProbeCargo);

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
				DoLog(LogLevel.Warning, $"Expedition from {origin.Coordinate} skipped: deuterium {vorhanden:N0} is below the minimum {min:N0} on this moon.");
				string schluessel = origin.Coordinate.ToString();
				lock (_deutMeldung) {
					if (_deutMeldung.TryGetValue(schluessel, out DateTime zuletzt) && (DateTime.UtcNow - zuletzt).TotalHours < 6)
						return false;
					_deutMeldung[schluessel] = DateTime.UtcNow;
				}
				await _tbotInstance.SendTelegramMessage($"Expeditions from moon {origin.Coordinate} paused: deuterium {vorhanden:N0} is below the minimum {min:N0}.");
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Deuterium check for {origin.Coordinate} failed: {e.Message}");
				return true;
			}
			return false;
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

		protected override async Task Execute() {
			bool stop = false;
			bool delay = false;

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

				if (expeditionsToSend > 0) {
					int prioritySlots = _calculationService.CalcSlotsPriority(
						Feature.Expeditions,
						rankSlotsPriority,
						_tbotInstance.UserData.slots,
						_tbotInstance.UserData.fleets,
						(int)_tbotInstance.InstanceSettings.General.SlotsToLeaveFree);
					expeditionsToSend = Math.Min(expeditionsToSend, Math.Max(0, prioritySlots));
				}

				if (expeditionsToSend <= 0) {
					delay = true;
					return;
				}

				if (!_tbotInstance.GhostSleepAllowsNewExpedition(GetDurationTracker().GetMaxDurationSeconds())) {
					DoLog(LogLevel.Information, "GhostSleep drain: no new expeditions before sleeping - they would not be back in time.");
					delay = true;
					return;
				}

				var origins = ResolveExpeditionOrigins();
				if (origins.Count == 0) {
					DoLog(LogLevel.Warning, "No valid expedition origin is configured.");
					delay = true;
					return;
				}

				var lfBonuses = await _ogameService.GetLFBonuses();
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
						if (CountActiveExpeditionsFromOrigin(origin) >= MaxExpeditionsForOrigin(origin, defaultMax[originIndex]))
							break;

						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
						if (_tbotInstance.UserData.slots.ExpFree <= 0 ||
							_tbotInstance.UserData.slots.Free <= (int)_tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
							delay = true;
							return;
						}

						var refreshedShips = await RefreshExpeditionShips(origin);
						if (refreshedShips == null) {
							unavailableOrigins[originIndex] = true;
							retryableUnavailableOrigins[originIndex] = true;
							break;
						}

						var fleet = BuildExpeditionFleet(origin, lfBonuses);
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
						var payload = new Resources();
						if ((long)_tbotInstance.InstanceSettings.Expeditions.FuelToCarry > 0)
							payload.Deuterium = (long)_tbotInstance.InstanceSettings.Expeditions.FuelToCarry;

						DoLog(LogLevel.Information, $"Sending expedition from {origin.Coordinate} to {destination}");
						var fleetId = await _fleetScheduler.SendFleet(
							origin,
							fleet,
							destination,
							Missions.Expedition,
							sendSpeed,
							payload);

						if (fleetId == (int)SendFleetCode.AfterSleepTime) {
							stop = true;
							return;
						}
						if (fleetId == (int)SendFleetCode.NotEnoughSlots) {
							delay = true;
							return;
						}
						if (fleetId <= (int)SendFleetCode.GenericError) {
							DoLog(LogLevel.Warning, $"Expedition was not sent from {origin.Coordinate}; excluding this origin for the rest of the cycle.");
							unavailableOrigins[originIndex] = true;
							break;
						}

						GetDurationTracker().NoteSent(fleetId, origin.Coordinate, DateTime.UtcNow);
						sentByOrigin[originIndex]++;
						remaining--;
						progressInPass = true;
						planningStart = (originIndex + 1) % origins.Count;

						int minWait = (int)_tbotInstance.InstanceSettings.Expeditions.MinWaitNextFleet;
						int maxWait = (int)_tbotInstance.InstanceSettings.Expeditions.MaxWaitNextFleet;
						if (maxWait < minWait)
							(minWait, maxWait) = (maxWait, minWait);
						await Task.Delay((int)RandomizeHelper.CalcRandomIntervalSecToMs(minWait, maxWait), _ct);
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
				if (expeditionFleets.Count == 0 ||
					(_tbotInstance.UserData.slots.ExpFree > 0 &&
					 !(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions &&
					 !(bool)_tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions)) {
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
				} else {
					interval = (int)((1000 * expeditionFleets.First().BackIn) +
						RandomizeHelper.CalcRandomIntervalSecToMs(
							(int)_tbotInstance.InstanceSettings.Expeditions.MinWaitNextRound,
							(int)_tbotInstance.InstanceSettings.Expeditions.MaxWaitNextRound));
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
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 +
								RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval(
								(int)_tbotInstance.InstanceSettings.Expeditions.CheckIntervalMin,
								(int)_tbotInstance.InstanceSettings.Expeditions.CheckIntervalMax);
						}
						ChangeWorkerPeriod(interval);
					}

					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}