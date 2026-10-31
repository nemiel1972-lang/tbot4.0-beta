using Tbot.Common.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using TBot.Common.Logging;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using System.ComponentModel.Design;
using TBot.Model;
using System.Threading;
using System.Globalization;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;
using System.Numerics;
using System.IO;
using Newtonsoft.Json;

namespace Tbot.Workers {
	internal static class TargetRejectionMemo {
		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _until = new();
		private static readonly TimeSpan Duration = TimeSpan.FromMinutes(30);

		private static bool Relevant(Missions mission) =>
			mission == Missions.Spy || mission == Missions.Attack || mission == Missions.Transport || mission == Missions.Deploy;

		private static string Key(ITBotMain inst, Coordinate c) => $"{inst.InstanceAlias}|{c.Galaxy}:{c.System}:{c.Position}:{c.Type}";

		public static void Add(ITBotMain inst, Coordinate c, Missions mission) {
			if (c != null && Relevant(mission))
				_until[Key(inst, c)] = DateTime.UtcNow + Duration;
		}

		public static bool IsBlocked(ITBotMain inst, Coordinate c, Missions mission) {
			if (c == null || !Relevant(mission))
				return false;
			var key = Key(inst, c);
			if (!_until.TryGetValue(key, out var until))
				return false;
			if (until > DateTime.UtcNow)
				return true;
			_until.TryRemove(key, out _);
			return false;
		}
	}

	public class FleetScheduler : IFleetScheduler {
		private readonly SemaphoreSlim _discoveryDispatchSemaphore = new(1, 1);
		private readonly object _fleetLock = new();
		private ITBotMain _tbotInstance = null;
		private ITBotOgamedBridge _tbotOgameBridge;
		private readonly IOgameService _ogameService;
		private readonly ICalculationService _calcService;
		

		private readonly Dictionary<string, Timer> timers = new();
		private readonly object _timersLock = new();

		private bool TryGetTimer(string key, out Timer timer) {
			lock (_timersLock) {
				return timers.TryGetValue(key, out timer);
			}
		}

		private readonly object _recallDepartureLock = new();
		private readonly Dictionary<int, DateTime> _recallDeparture = new();
		private void AddTimer(string key, Timer timer) {
			lock (_timersLock) {
				if (!timers.ContainsKey(key)) timers.Add(key, timer);
			}
		}

		private void DisposeAndRemoveTimer(string key) {
			lock (_timersLock) {
				if (timers.TryGetValue(key, out var t)) {
					try { t.Dispose(); } catch { }
					timers.Remove(key);
				}
			}
		}

		public FleetScheduler(
			ICalculationService helpService,
			IOgameService ogameService) {
			_calcService = helpService;
			_ogameService = ogameService;
		}

		public void SetTBotInstance(ITBotMain tbotInstance) {
			_tbotInstance = tbotInstance;
		}

		public void SetTBotOgameBridge(ITBotOgamedBridge tbotOgameBridge) {
			_tbotOgameBridge = tbotOgameBridge;
		}

		public async Task SpyCrash(Celestial fromCelestial, Coordinate target = null) {
			decimal speed = Speeds.HundredPercent;
			fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Ships);
			fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Resources);
			var payload = fromCelestial.Resources;
			Random random = new Random();

			if (fromCelestial.Ships.EspionageProbe == 0 || payload.Deuterium < 1) {
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				await _tbotInstance.SendTelegramMessage($"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				return;
			}
                         List<Coordinate> spycrash = new();

                           if (target == null) {
                                int playerid = _tbotInstance.UserData.userInfo.PlayerID;

                               for (int sys = fromCelestial.Coordinate.System - 2; sys <= fromCelestial.Coordinate.System + 2; sys++) {
                              int clampedSys = GeneralHelper.ClampSystem(sys);

                                  GalaxyInfo galaxyInfo = await _ogameService.GetGalaxyInfo(fromCelestial.Coordinate.Galaxy, clampedSys);
                              if (galaxyInfo?.Planets == null) continue;

                               foreach (var planet in galaxyInfo.Planets) {
                                   try {
                                           if (planet != null
                                         && !planet.Administrator
                                         && !planet.Inactive
                                         && !planet.StrongPlayer
                                         && !planet.Newbie
                                         && !planet.Banned
                                         && !planet.Vacation
                                         && planet.Player != null
                                         && planet.Player.ID != playerid) {

                    spycrash.Add(new Coordinate(
                        planet.Coordinate.Galaxy,
                        planet.Coordinate.System,
                        planet.Coordinate.Position,
                        Celestials.Planet));
                }
            } catch (NullReferenceException) {
                continue;
            }
        }
    }

                               if (spycrash.Count == 0) {
                                  await _tbotInstance.SendTelegramMessage("No planet to spycrash on could be found over system -2 -> +2");
                                   return;
                          }

                                         target = spycrash[random.Next(spycrash.Count)];
                              }
	              		var attackingShips = new Ships().Add(Buildables.EspionageProbe, 1);

                         int fleetId = await SendFleet(fromCelestial, attackingShips, target, Missions.Attack, speed);

                   if (fleetId != (int)SendFleetCode.GenericError &&
                              fleetId != (int)SendFleetCode.AfterSleepTime &&
                              fleetId != (int)SendFleetCode.NotEnoughSlots)
                 {
                  _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
                                $"EspionageProbe sent to crash on {target}");

                         await _tbotInstance.SendTelegramMessage(
                                     $"EspionageProbe sent to crash on {target}");
                                     }

                               return;
                            }
		private readonly object _pendingRecallsLock = new();

		private class PendingRecallEntry {
			public int FleetId { get; set; }
			public DateTime RecallAtUtc { get; set; }
			public string OriginCoordinate { get; set; }
		}

		private string GetPendingRecallsFilePath() {
			string playerTag = _tbotInstance?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universeTag = _tbotInstance?.UserData?.serverData?.Name ?? "unknown";
			string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataDir);
			return Path.Combine(dataDir, $"pending_recalls_{playerTag}-{universeTag}.json");
		}

		private List<PendingRecallEntry> LoadPendingRecalls() {
			lock (_pendingRecallsLock) {
				try {
					string path = GetPendingRecallsFilePath();
					if (!File.Exists(path)) return new();
					return JsonConvert.DeserializeObject<List<PendingRecallEntry>>(File.ReadAllText(path)) ?? new();
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.FleetScheduler, $"Unable to load pending recalls: {e.Message}");
					return new();
				}
			}
		}

		private void SavePendingRecalls(List<PendingRecallEntry> entries) {
			lock (_pendingRecallsLock) {
				try {
					AtomicFile.WriteAllText(GetPendingRecallsFilePath(), JsonConvert.SerializeObject(entries));
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.FleetScheduler, $"Unable to save pending recalls: {e.Message}");
				}
			}
		}

		private void AddPendingRecall(int fleetId, DateTime recallAtUtc, string originCoordinate) {
			var entries = LoadPendingRecalls();
			entries.RemoveAll(e => e.FleetId == fleetId);
			entries.Add(new PendingRecallEntry { FleetId = fleetId, RecallAtUtc = recallAtUtc, OriginCoordinate = originCoordinate });
			SavePendingRecalls(entries);
		}

		private void RemovePendingRecall(int fleetId) {
			var entries = LoadPendingRecalls();
			if (entries.RemoveAll(e => e.FleetId == fleetId) > 0)
				SavePendingRecalls(entries);
		}

		public async Task ReconcilePendingRecalls() {
			var entries = LoadPendingRecalls();
			if (entries.Count == 0) return;

			_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Reconciling {entries.Count} pending recall(s) from before restart...");
			_tbotInstance.UserData.fleets = await UpdateFleets();

			foreach (var entry in entries) {
				Fleet fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == entry.FleetId);
				if (fleet == null) {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Pending recall for fleet {entry.FleetId} dropped: fleet no longer exists (already returned/cancelled).");
					RemovePendingRecall(entry.FleetId);
					continue;
				}

				DateTime now = await _tbotOgameBridge.GetDateTime();
				double remainingMs = (entry.RecallAtUtc - now).TotalMilliseconds;

				if (remainingMs <= 0) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Pending recall for fleet {entry.FleetId} (origin {entry.OriginCoordinate}) is overdue - recalling now.");
					await CancelFleet(fleet);
				} else {
					AddTimer($"RecallTimer-{entry.FleetId}", new Timer(RetireFleet, fleet, (long) remainingMs, Timeout.Infinite));
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Pending recall for fleet {entry.FleetId} (origin {entry.OriginCoordinate}) re-armed, due at {entry.RecallAtUtc}.");
				}
			}
		}

		public async Task<List<string>> DryRunFleetSave(Celestial celestial, long minDuration) {
			var res = new List<string>();
			try {
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
				ShadowFleetWorker.ExcludeFromFleetSave(_tbotInstance.InstanceSettings, celestial);
				var movable = celestial.Ships.GetMovableShips();
				if (movable.IsEmpty()) {
					res.Add($"warn|No fleet to save on {celestial}: the fleetsave would be skipped (resources alone stay where they are).");
					return res;
				}
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				long deut = celestial.Resources.Deuterium;
				if (deut == 0) {
					res.Add("bad|No deuterium on the celestial: the fleetsave would be skipped (no fuel).");
					return res;
				}
				res.Add($"info|Fleet to save: {movable.GetFleetPoints():N0} points, deuterium {deut:N0}, metal {celestial.Resources.Metal:N0}, crystal {celestial.Resources.Crystal:N0}.");
				var departure = await _tbotOgameBridge.GetDateTime();
				if (!Missions.TryParse(_tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DefaultMission, out Missions first))
					first = Missions.Harvest;
				var chain = new List<Missions> { first, Missions.Deploy };
				if (celestial.Ships.ColonyShip > 0)
					chain.Add(Missions.Colonize);
				if (celestial.Ships.EspionageProbe > 0)
					chain.Add(Missions.Spy);
				chain.Add(Missions.Transport);
				foreach (var mission in chain.Distinct()) {
					var hyp = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departure, minDuration, mission, deut);
					var usable = hyp.Where(h => h.Fuel <= deut).OrderBy(h => h.Fuel).ToList();
					if (usable.Count == 0) {
						res.Add($"info|{mission}: no suitable destination ({hyp.Count} candidates, none usable with the fuel and flight time).");
						continue;
					}
					var f = usable[0];
					res.Add($"ok|{mission}: first choice would be {f.Destination}, flight {TimeSpan.FromSeconds(f.Duration):hh\\:mm\\:ss}, speed {f.Speed * 10}%, fuel {f.Fuel:N0} ({usable.Count} usable candidates).");
					res.Add($"ok|Cargo: everything (metal, crystal, food, deuterium minus fuel). Recall after the last attack wave. Nothing is sent in this test.");
					return res;
				}
				res.Add("bad|No destination found for any mission: the fleet would NOT be saved.");
			} catch (Exception e) {
				res.Add($"bad|Fleetsave dry run failed: {e.Message}");
			}
			return res;
		}

		public async Task AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool WaitFleetsReturn = false, Missions TelegramMission = Missions.None, bool fromTelegram = false, bool saveall = false) {
			DateTime departureTime = await _tbotOgameBridge.GetDateTime();
			_tbotInstance.SleepDuration = minDuration;

			if (WaitFleetsReturn) {

				_tbotInstance.UserData.fleets = await UpdateFleets();
				long interval;
				try {
					interval = (_tbotInstance.UserData.fleets
						.Where(f => f.Mission != Missions.Discovery)
						.OrderBy(f => f.BackIn)
						.Last()
						.BackIn ?? 0
					) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				} catch {
					interval = 0;
				}

				if (interval > 0 && (!TryGetTimer("GhostSleepTimer", out Timer value))) {
					List<Feature> features = new List<Feature> {
						Feature.Colonize,
						Feature.BrainAutoRepatriate,
						Feature.BrainAutoMine,
						Feature.BrainAutoResearch,
						Feature.BrainAutoMine,
						Feature.BrainLifeformAutoMine,
						Feature.BrainLifeformAutoResearch,
						Feature.AutoFarm,
#if FASTFARM
						Feature.FastFarm,
#endif
						Feature.Harvest,
						Feature.Expeditions,
						Feature.AutoDiscovery
					};
					foreach(var feat in features) {
						await _tbotInstance.StopFeature(feat);
					}
					

					interval += RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime TimeToGhost = departureTime.AddMilliseconds(interval);
					_tbotInstance.NextWakeUpTime = TimeToGhost.AddMilliseconds(minDuration * 1000);

					if (saveall)
						AddTimer("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturnAll, null, interval, Timeout.Infinite));
					else
						AddTimer("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturn, null, interval, Timeout.Infinite));

					_tbotInstance.log(LogLevel.Information, LogSender.SleepMode, $"Fleets active, Next check at {TimeToGhost.ToString()}");
					await _tbotInstance.SendTelegramMessage($"Waiting for fleets return, delaying ghosting at {TimeToGhost.ToString()}");

					return;
				} else if (interval == 0 && (!TryGetTimer("GhostSleepTimer", out Timer value2))) {

					_tbotInstance.log(LogLevel.Information, LogSender.SleepMode, $"No fleets active, Ghosting now.");
					_tbotInstance.NextWakeUpTime = departureTime.AddMilliseconds(minDuration * 1000);
					if (saveall)
						GhostandSleepAfterFleetsReturnAll(null);
					else
						GhostandSleepAfterFleetsReturn(null);

					return;
				} else if (TryGetTimer("GhostSleepTimer", out Timer value3)) {
					await _tbotInstance.SendTelegramMessage($"GhostSleep already planned, try /cancelghostsleep");
					return;
				}
			}

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			if (!isSleepTimeFleetSave)
				ShadowFleetWorker.ExcludeFromFleetSave(_tbotInstance.InstanceSettings, celestial);
			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fleet to save!");
				if (fromTelegram)
					await _tbotInstance.SendTelegramMessage($"{celestial.ToString()}: there is no fleet!");
				return;
			}

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			Celestial destination = new() { ID = 0 };

			if (celestial.Resources.Deuterium == 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fuel!");
				if (fromTelegram)
					await _tbotInstance.SendTelegramMessage($"{celestial.ToString()}: there is no fuel!");
				return;
			}

			long maxDeuterium = celestial.Resources.Deuterium;

			if (isSleepTimeFleetSave) {
				if (DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp)) {
					if (departureTime >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					minDuration = (long) wakeUp.Subtract(departureTime).TotalSeconds;
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Could not plan fleetsave from {celestial.ToString()}: unable to parse comeback time");
					return;
				}
			}

			long deutVorher = celestial.Resources.Deuterium;
			var payload = celestial.Resources;
			if (isSleepTimeFleetSave) {
				if ((long) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeutToLeave > 0)
					payload.Deuterium -= (long) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeutToLeave;
				if (payload.Deuterium < 0)
					payload.Deuterium = 0;
			}

			Resources PayloadFuer(FleetHypotesis f) {
				if (isSleepTimeFleetSave) {
					Resources ps = new();
					ps.Metal = payload.Metal;
					ps.Crystal = payload.Crystal;
					ps.Food = payload.Food;
					ps.Deuterium = payload.Deuterium;
					return ps;
				}
				Resources p = new();
				p.Metal = celestial.Resources.Metal;
				p.Crystal = celestial.Resources.Crystal;
				p.Food = celestial.Resources.Food;
				p.Deuterium = Math.Max(0, deutVorher - (long) f.Fuel);
				return p;
			}

			FleetHypotesis possibleFleet = new();
			int fleetId = (int) SendFleetCode.GenericError;
			bool AlreadySent = false;

			Missions mission;
			if (!Missions.TryParse(_tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DefaultMission, out mission)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Error: Could not parse 'DefaultMission' from settings, value set to Harvest.");
				mission = Missions.Harvest;
			}

			if (TelegramMission != Missions.None)
				mission = TelegramMission;

			List<FleetHypotesis> fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
			if (fleetHypotesis.Count() > 0) {
				foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
					if (CheckFuel(fleet, celestial)) {
						fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, PayloadFuer(fleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);

						if (fleetId != (int)SendFleetCode.GenericError &&
    fleetId != (int)SendFleetCode.AfterSleepTime &&
    fleetId != (int)SendFleetCode.NotEnoughSlots) {
    possibleFleet = fleet;
    AlreadySent = true;
    DateTime arrivalTime = departureTime.AddSeconds(fleet.Duration);
    _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
        $"{mission} fleet sent from {fleet.Origin.Coordinate} to {fleet.Destination} " +
        $"| Departure: {departureTime:yyyy-MM-dd HH:mm:ss} | Arrival: {arrivalTime:yyyy-MM-dd HH:mm:ss} " +
        $"| Duration: {TimeSpan.FromSeconds(fleet.Duration):hh\\:mm\\:ss} | Speed: {fleet.Speed * 10}%");
    if (fromTelegram) {
        await _tbotInstance.SendTelegramMessage(
            $"{mission} fleet sent to {fleet.Destination}\nDeparture: {departureTime:yyyy-MM-dd HH:mm:ss}\nArrival: {arrivalTime:yyyy-MM-dd HH:mm:ss}");
    }
    break;
}
					}
				}
			}

			if (fromTelegram && !AlreadySent && mission == Missions.Harvest && fleetHypotesis.Count() == 0) {
				await _tbotInstance.SendTelegramMessage($"No debris field found for {mission}, try to /spycrash.");
				return;
			} else if (fromTelegram && !AlreadySent && fleetHypotesis.Count() >= 0) {
				await _tbotInstance.SendTelegramMessage($"Available fuel: {celestial.Resources.Deuterium}\nNo destination found for {mission}, try to reduce ghost time.");
				return;
			}

			if (!AlreadySent) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible, checking next mission..");
				if (mission == Missions.Harvest) { mission = Missions.Deploy; } else { mission = Missions.Harvest; };
				mission = Missions.Deploy;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, PayloadFuer(fleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);

							if (fleetId != (int)SendFleetCode.GenericError &&
    fleetId != (int)SendFleetCode.AfterSleepTime &&
    fleetId != (int)SendFleetCode.NotEnoughSlots)
{
    possibleFleet = fleet;
    AlreadySent = true;
    break;
}
						}
					}
				}
			}
			if (!AlreadySent && !isSleepTimeFleetSave && celestial.Ships.ColonyShip > 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Colonize destination...");
				mission = Missions.Colonize;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, PayloadFuer(fleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);

							if (fleetId != (int)SendFleetCode.GenericError &&
    fleetId != (int)SendFleetCode.AfterSleepTime &&
    fleetId != (int)SendFleetCode.NotEnoughSlots)
{
    possibleFleet = fleet;
    AlreadySent = true;
    break;
}

						}
					}
				}
			}

			if (!AlreadySent && !isSleepTimeFleetSave && celestial.Ships.EspionageProbe > 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Spy destination...");
				mission = Missions.Spy;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, PayloadFuer(fleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);

							if (fleetId != (int)SendFleetCode.GenericError &&
    fleetId != (int)SendFleetCode.AfterSleepTime &&
    fleetId != (int)SendFleetCode.NotEnoughSlots)
{
    possibleFleet = fleet;
    AlreadySent = true;
    break;
}

						}
					}
				}
			}

			if (!AlreadySent && isSleepTimeFleetSave) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no deploy to an own moon possible - fallbacks to foreign targets (Colonize/Spy/Transport) deliberately skipped during sleep fleetsave.");
			} else if (!AlreadySent) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Transport destination...");
				mission = Missions.Transport;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, PayloadFuer(fleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);

							if (fleetId != (int)SendFleetCode.GenericError &&
    fleetId != (int)SendFleetCode.AfterSleepTime &&
    fleetId != (int)SendFleetCode.NotEnoughSlots)
{
    possibleFleet = fleet;
    AlreadySent = true;
    break;
}

						}
					}
				}
			}

			bool hasMoon = _tbotInstance.UserData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
			bool isHomeCelestial = false;
			try {
				isHomeCelestial = celestial.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy
					&& celestial.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System
					&& celestial.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position;
			} catch { }
			if (!AlreadySent && isHomeCelestial) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()}: no deploy target found - switch to the unprotected home moon deliberately skipped, fleet stays at home (turtle).");
				await _tbotInstance.SendTelegramMessage($"Fleetsave from {celestial.ToString()}: no deploy target found (deuterium?) - fleet stays at home.");
				return;
			}

			if (!AlreadySent && hasMoon && !TryGetTimer("GhostSleepTimer", out Timer val)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible (missing fuel?), checking for switch if has Moon");
				try {
					Coordinate switchDest = new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position,
						celestial.Coordinate.Type == Celestials.Planet ? Celestials.Moon : Celestials.Planet);
					Ships switchShips = celestial.Ships.GetMovableShips();
					FleetPrediction switchPrediction = _calcService.CalcFleetPrediction(celestial.Coordinate, switchDest, switchShips, Missions.Deploy, Speeds.TenPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
					FleetHypotesis switchFleet = new() {
						Origin = celestial,
						Destination = switchDest,
						Ships = switchShips,
						Mission = Missions.Deploy,
						Speed = Speeds.TenPercent,
						Fuel = switchPrediction.Fuel,
						Duration = switchPrediction.Time
					};
					if (CheckFuel(switchFleet, celestial)) {
						int switchId = await SendFleet(celestial, switchShips, switchDest, Missions.Deploy, Speeds.TenPercent, PayloadFuer(switchFleet), _tbotInstance.UserData.userInfo.Class, true, fromTelegram, allowSpeedReduction: true, shrinkDeuteriumPayload: true, fleetsave: true);
						if (switchId != (int) SendFleetCode.GenericError && switchId != (int) SendFleetCode.AfterSleepTime && switchId != (int) SendFleetCode.NotEnoughSlots) {
							fleetId = switchId;
							possibleFleet = switchFleet;
							AlreadySent = true;
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Switch-Fallback: {celestial.ToString()} -> {switchDest} sent at 10%, duration {TimeSpan.FromSeconds(switchPrediction.Time):hh\\:mm\\:ss}, recall is being scheduled.");
						}
					}
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Switch fallback failed: {e.Message}");
				}
			}

			if (!AlreadySent) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.Coordinate.ToString()} no suitable destination found, you gonna get hit!");
				await _tbotInstance.SendTelegramMessage($"Fleetsave from {celestial.Coordinate.ToString()} No destination found!, you gonna get hit!");
				return;
			}

			if (AlreadySent)
{
    if (fleetId > 0)
    {
        Fleet fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
        if (fleet == null) {
            try {
                _tbotInstance.UserData.fleets = await UpdateFleets();
                fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
            } catch { }
            if (fleet == null) {
                _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} not in the fleet list - recall is scheduled anyway with a fallback object.");
                fleet = new Fleet { ID = fleetId, Origin = celestial.Coordinate };
            }
        }
        DateTime time = await _tbotOgameBridge.GetDateTime();
        lock (_recallDepartureLock) { _recallDeparture[fleetId] = time; }
        var interval = ((minDuration / 2) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);

        if (interval <= 0)
            interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

        if (fleet.ArriveIn > 0) {
            long maxIntervalMs = (long) (fleet.ArriveIn - 90) * 1000;
            if (maxIntervalMs < 30000) maxIntervalMs = 30000;
            if (interval > maxIntervalMs) {
                _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Recall time ({interval / 1000}s) was after the actual landing (in {fleet.ArriveIn}s) - capped to {maxIntervalMs / 1000}s.");
                interval = maxIntervalMs;
            }
        }

        DateTime newTime = time.AddMilliseconds(interval);
        AddTimer($"RecallTimer-{fleetId}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
        AddPendingRecall(fleetId, newTime, fleet.Origin?.ToString() ?? celestial.ToString());

        _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
            $"The fleet will be recalled at {newTime}");
    }
}

	else
{
    if (fleetId > 0)
    {
        Fleet fleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId);
        DateTime returntime = (DateTime)fleet.BackTime;

        _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
            $"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination}, arrive at {possibleFleet.Duration} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");

        if (fromTelegram)
            await _tbotInstance.SendTelegramMessage(
                $"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination}, arrive at {possibleFleet.Duration}, returned at {returntime} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
    }
}
		}
		public async Task<bool> SendDiscovery(Celestial origin, Coordinate destination) {
			await _discoveryDispatchSemaphore.WaitAsync();
			try {
				if (origin?.Coordinate == null || destination == null) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Discovery not sent: origin or destination is invalid.");
					return false;
				}

				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				int slotsToLeaveFree = (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree;
				if (_tbotInstance.UserData.slots == null || _tbotInstance.UserData.slots.Free <= slotsToLeaveFree) {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
						$"Discovery not sent from {origin.Coordinate}: only {_tbotInstance.UserData.slots?.Free ?? 0} fleet slot(s) available, {slotsToLeaveFree} reserved.");
					return false;
				}

				bool sent = await _ogameService.SendDiscovery(origin, destination);
				if (!sent) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
						$"Discovery rejected from {origin.Coordinate} to {destination}.");
					return false;
				}

				try {
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				} catch (Exception refreshException) {
					_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler,
						$"Discovery was accepted, but slot refresh failed: {refreshException.Message}");
				}

				return true;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
					$"Unable to send discovery fleet from {origin.Coordinate} to {destination}: {e.Message}");
				return false;
			} finally {
				_discoveryDispatchSemaphore.Release();
			}
		}

		public async Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false, bool ignoreLedger = false, long deutToLeave = 0, bool allowSpeedReduction = false, bool shrinkDeuteriumPayload = false, bool fleetsave = false) {
			_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending fleet from {origin.Coordinate.ToString()} to {destination.ToString()}. Mission: {mission.ToString()}. Speed: {(speed * 10).ToString()}% Ships: {ships.ToString()}{(string.IsNullOrEmpty(LedgerTBot.CurrentOwner) ? "" : " [worker: " + LedgerTBot.CurrentOwner + "]")}");

			if (playerClass == CharacterClass.NoClass)
				playerClass = _tbotInstance.UserData.userInfo.Class;

			if (_tbotInstance.IsBotPaused && !fleetsave && !ignoreLedger && BotPause.BlocksMission(mission)) {
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet not sent: the bot is paused ({mission}).");
				ExtraLog.Write(_tbotInstance, "BotPause", () => $"SendFleet blocked: {mission} from {origin.Coordinate} to {destination} (ships {ships}).");
				return (int) SendFleetCode.GenericError;
			}
			if (!ships.HasMovableFleet()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: there are no ships to send");
				return (int) SendFleetCode.GenericError;
			}
			if (mission == Missions.Expedition && ships.IsOnlyProbes()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: cannot send an expedition with no ships");
				return (int) SendFleetCode.GenericError;
			}
			if (origin.Coordinate.IsSame(destination)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: origin and destination are the same");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.Galaxy <= 0 || destination.Galaxy > _tbotInstance.UserData.serverData.Galaxies || destination.Position <= 0 || destination.Position > 17) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.System <= 0 || destination.System > _tbotInstance.UserData.serverData.Systems) {
				if (_tbotInstance.UserData.serverData.DonutGalaxy) {
					if (destination.System <= 0) {
						destination.System += _tbotInstance.UserData.serverData.Systems;
					} else if (destination.System > _tbotInstance.UserData.serverData.Systems) {
						destination.System -= _tbotInstance.UserData.serverData.Systems;
					}
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
					return (int) SendFleetCode.GenericError;
				}
			}

			if (TargetRejectionMemo.IsBlocked(_tbotInstance, destination, mission)) {
				ExtraLog.Write(_tbotInstance, "FleetTargets", () => $"SendFleet skipped: {destination} was refused by the game a short time ago ({mission}).");
				return (int) SendFleetCode.TargetInvalid;
			}
if (!_calcService.GetValidSpeedsForClass(playerClass).Any(s => s == speed)) {
    _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
        "Unable to send fleet: speed not available for your class");
    return (int)SendFleetCode.GenericError;
}

origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);

FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(
    origin.Coordinate,
    destination,
    ships,
    mission,
    speed,
    _tbotInstance.UserData.researches,
    _tbotInstance.UserData.serverData,
    origin.LFBonuses,
    _tbotInstance.UserData.userInfo.Class,
    _tbotInstance.UserData.allianceClass
);

var flightTime = mission switch
{
    Missions.Deploy => fleetPrediction.Time,
    Missions.Expedition => (long)Math.Round((double)(2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),
    _ => (long)Math.Round((double)(2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity)
};

origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);

long deuteriumInPayload = payload?.Deuterium ?? 0;
_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler,
    $"SendFleet diagnostics: origin={origin.Coordinate}, destination={destination}, mission={mission}, speed={speed * 10}%, ships={ships}, stockDeut={origin.Resources.Deuterium:N0}, payloadDeut={deuteriumInPayload:N0}, reserveDeut={deutToLeave:N0}, predictedFuel={fleetPrediction.Fuel:N0}, flightTime={flightTime}s.");

long fuelCapacity = _calcService.CalcFleetFuelCapacity(
    ships,
    _tbotInstance.UserData.serverData,
    _tbotInstance.UserData.researches.HyperspaceTechnology,
    _tbotInstance.UserData.userInfo.Class,
    _tbotInstance.UserData.serverData.ProbeCargo
);

bool notEnoughDeuterium = origin.Resources.Deuterium - deuteriumInPayload - deutToLeave < fleetPrediction.Fuel;
bool notEnoughFuelCapacity = fuelCapacity != 0 && fuelCapacity < fleetPrediction.Fuel;
if (notEnoughDeuterium || notEnoughFuelCapacity) {
    _tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler,
        $"SendFleet fuel gate: notEnoughDeuterium={notEnoughDeuterium}, notEnoughFuelCapacity={notEnoughFuelCapacity}, availableAfterCargoAndReserve={origin.Resources.Deuterium - deuteriumInPayload - deutToLeave:N0}, fuel={fleetPrediction.Fuel:N0}, fuelCapacity={fuelCapacity:N0}, allowSpeedReduction={allowSpeedReduction}, shrinkDeuteriumPayload={shrinkDeuteriumPayload}.");
    bool speedReduced = false;
    if (allowSpeedReduction) {
        var lowerSpeeds = _calcService.GetValidSpeedsForClass(playerClass).Where(s => s < speed).OrderByDescending(s => s).ToList();
        foreach (var lowerSpeed in lowerSpeeds) {
            var lowerPrediction = _calcService.CalcFleetPrediction(
                origin.Coordinate, destination, ships, mission, lowerSpeed,
                _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData,
                origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
            if (origin.Resources.Deuterium - deuteriumInPayload - deutToLeave >= lowerPrediction.Fuel && (fuelCapacity == 0 || fuelCapacity >= lowerPrediction.Fuel)) {
                _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
                    $"Insufficient fuel at {speed * 10}% speed ({fleetPrediction.Fuel} fuel needed, {origin.Resources.Deuterium} available, tank capacity {fuelCapacity}). Reducing to {lowerSpeed * 10}% speed ({lowerPrediction.Fuel} fuel needed).");
                fleetPrediction = lowerPrediction;
                flightTime = mission switch {
                    Missions.Deploy => fleetPrediction.Time,
                    Missions.Expedition => (long)Math.Round((double)(2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),
                    _ => (long)Math.Round((double)(2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity)
                };
                speed = lowerSpeed;
                speedReduced = true;
                break;
            }
        }
    }
    if (!speedReduced) {
        if (notEnoughFuelCapacity) {
            _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
                "Unable to send fleet: ships don't have enough fuel capacity!");
            return (int)SendFleetCode.GenericError;
        }
        long availableForDeuteriumCargo = Math.Max(0, origin.Resources.Deuterium - deutToLeave - fleetPrediction.Fuel);
        bool fuelFitsWithoutCargo = origin.Resources.Deuterium - deutToLeave >= fleetPrediction.Fuel;
        if (shrinkDeuteriumPayload && payload != null && fuelFitsWithoutCargo && deuteriumInPayload > availableForDeuteriumCargo) {
            _tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler,
                $"Not enough deuterium to ship {deuteriumInPayload} and still cover {fleetPrediction.Fuel} fuel plus the {deutToLeave} reserve - shipping {availableForDeuteriumCargo} deuterium instead.");
            payload.Deuterium = availableForDeuteriumCargo;
            deuteriumInPayload = availableForDeuteriumCargo;
        } else {
            _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
                $"Unable to send fleet: not enough deuterium! (stock {origin.Resources.Deuterium}, payload {deuteriumInPayload}, reserve {deutToLeave}, fuel {fleetPrediction.Fuel})");
            return (int)SendFleetCode.GenericError;
        }
    }
}

bool ignoreSleepForExpeditions =
    mission == Missions.Expedition &&
    (bool)_tbotInstance.InstanceSettings.Expeditions.IgnoreSleep;

bool sleepWaveActive = mission == Missions.Expedition && _tbotInstance.SleepWaveActive;

if (
    (bool)_tbotInstance.InstanceSettings.SleepMode.Active &&
    DateTime.TryParse((string)_tbotInstance.InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep) &&
    DateTime.TryParse((string)_tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp) &&
    !force &&
    !sleepWaveActive
)
{
    DateTime time = await _tbotOgameBridge.GetDateTime();

    if (GeneralHelper.ShouldSleep(time, goToSleep, wakeUp)) {
        _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
            "Unable to send fleet: bed time has passed");
        return (int)SendFleetCode.AfterSleepTime;
    }

    if (goToSleep >= wakeUp) wakeUp = wakeUp.AddDays(1);
    if (goToSleep < time) goToSleep = goToSleep.AddDays(1);
    if (wakeUp < time) wakeUp = wakeUp.AddDays(1);

    DateTime returnTime = time.AddSeconds(flightTime);

    if (returnTime >= goToSleep && returnTime <= wakeUp && !ignoreSleepForExpeditions) {
        _tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler,
            "Unable to send fleet: it would come back during sleep time");
        return (int)SendFleetCode.AfterSleepTime;
    }
}
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
			int slotsToLeaveFree = (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree;
			SlotDecision slotDecision = SlotReserve.Decide(_tbotInstance.UserData.slots.Free, slotsToLeaveFree, ReadFleetsaveSlots(), force, fleetsave);
			ExtraLog.Write(_tbotInstance, "FleetsaveSlot", () => $"{mission} from {origin.Coordinate}: free={_tbotInstance.UserData.slots.Free}, total={_tbotInstance.UserData.slots.Total}, toLeaveFree={slotsToLeaveFree}, reservedForFleetsave={ReadFleetsaveSlots()}, force={force}, fleetsave={fleetsave} -> {slotDecision}.");
			if (slotDecision != SlotDecision.Allowed) {
				int count = System.Threading.Interlocked.Increment(ref _slotRefusals);
				string why = slotDecision == SlotDecision.ReservedForFleetsave ? "last slot is reserved for the fleetsave" : "no slots available";
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Unable to send fleet, {why}");
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"SLOTCOLLISION #{count}: {mission} from {origin.Coordinate} refused ({why}; free {_tbotInstance.UserData.slots.Free}, total {_tbotInstance.UserData.slots.Total}).");
				return (int) SendFleetCode.NotEnoughSlots;
			} else {
				if (payload == null)
					payload = new();
				try {
					if (payload.Metal < 0)
						payload.Metal = 0;
					if (payload.Crystal < 0)
						payload.Crystal = 0;
					if (payload.Deuterium < 0)
						payload.Deuterium = 0;
					if (payload.Food < 0)
						payload.Food = 0;
					if (!ignoreLedger && !fleetsave && !LedgerTBot.CheckOrigin(_tbotInstance, origin, payload, (long) fleetPrediction.Fuel))
						return (int) SendFleetCode.NotEnoughRessources;
					Fleet newFleet = await _ogameService.SendFleet(origin, ships, destination, mission, speed, payload);
					if (newFleet == null) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Unable to send fleet from {origin.Coordinate.ToString()}: no ships to send.");
						return (int) SendFleetCode.GenericError;
					}
					_tbotInstance.UserData.fleets = _tbotInstance.UserData.fleets.Append(newFleet).ToList();
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Fleet sent successfully!");
					FleetCounter.Count(_tbotInstance.InstanceAlias, TBot.Ogame.Infrastructure.RequestStats.Current.Value, mission);
					if (mission == Missions.Transport)
						LedgerTBot.OnTransportSent(_tbotInstance, destination, newFleet.ID, payload, fleetPrediction.Time);
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					return newFleet.ID;
				} catch (Exception e) {
					var spielFehler = System.Text.RegularExpressions.Regex.Match(e.Message, @"Message:\s*(.*?\(140\d{3}\))");
					if (spielFehler.Success) {
						string grund = spielFehler.Groups[1].Value.Trim();
						bool zielFehler = e.Message.Contains("(140035)") || e.Message.Contains("(140010)") || e.Message.Contains("(140016)") || e.Message.Contains("(140006)");
						if (zielFehler)
							TargetRejectionMemo.Add(_tbotInstance, destination, mission);
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet cannot be sent: {grund} ({origin.Coordinate} -> {destination}, {mission})");
						return zielFehler ? (int) SendFleetCode.TargetInvalid : (int) SendFleetCode.GenericError;
					}
					_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to send fleet: an exception has occurred: {e.Message}");
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
					return (int) SendFleetCode.GenericError;
				}
			}
		}

		private static int _slotRefusals;

		private int ReadFleetsaveSlots() {
			try {
				var g = _tbotInstance.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "FleetsaveSlot"))
					return new FleetsaveSlotConfig().Slots;
				var f = g.Experimental.FleetsaveSlot;
				if (SettingsService.IsSettingSet(f, "Active") && !(bool) f.Active)
					return 0;
				return SettingsService.IsSettingSet(f, "Slots") ? Math.Max(0, (int) f.Slots) : new FleetsaveSlotConfig().Slots;
			} catch (Exception) {
				return new FleetsaveSlotConfig().Slots;
			}
		}

		public async Task CancelFleet(Fleet fleet) {
			try {
				bool isUnderAttack = await _ogameService.IsUnderAttack();
				if (isUnderAttack) {
					var attacks = await _ogameService.GetAttacks();
					var attacksOnOrigin = attacks
						.Where(a => a.Destination.IsSame(fleet.Origin))
						.OrderByDescending(a => a.ArriveIn)
						.ToList();

					if (attacksOnOrigin.Any()) {
						var lastAttack = attacksOnOrigin.First();
						var currentTime = await _tbotOgameBridge.GetDateTime();
						var currentFleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleet.ID);
						if (currentFleet != null && currentFleet.ArriveIn > 0) {
							DateTime departure;
							bool hasDeparture;
							lock (_recallDepartureLock) { hasDeparture = _recallDeparture.TryGetValue(fleet.ID, out departure); }
							if (!hasDeparture) {
								_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"[RECALL-CHECK] Departure time of fleet {fleet.ID} unknown (restart?) - recall without attack check.");
								goto doRecall;
							}
							long estimatedReturnSeconds = (long) Math.Max(0, (currentTime - departure).TotalSeconds);

							_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"[RECALL-CHECK] Fleet {fleet.ID} would return in ~{estimatedReturnSeconds}s if recalled now");
							_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"[RECALL-CHECK] Last attack on {fleet.Origin} arrives in {lastAttack.ArriveIn}s");
							if (estimatedReturnSeconds < lastAttack.ArriveIn) {
								long waitSeconds = (lastAttack.ArriveIn - estimatedReturnSeconds) / 2 + 30;
								if (waitSeconds >= currentFleet.ArriveIn - 15) {
									_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"[RECALL-SKIPPED] Fleet {fleet.ID} lands in {currentFleet.ArriveIn}s, return would be before the attack ({estimatedReturnSeconds}s < {lastAttack.ArriveIn}s) - fleet stays at the target (safe).");
									RemovePendingRecall(fleet.ID);
									DisposeAndRemoveTimer($"RecallTimer-{fleet.ID.ToString()}");
									return;
								}
								_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"[RECALL-BLOCKED] Fleet {fleet.ID} would return in {estimatedReturnSeconds}s but attack arrives in {lastAttack.ArriveIn}s - DELAYING RECALL!");
								var newRecallDelay = waitSeconds * 1000;
								if (newRecallDelay < 30000) newRecallDelay = 30000;
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"[RECALL-RESCHEDULED] New recall in {newRecallDelay/1000}s");
								DisposeAndRemoveTimer($"RecallTimer-{fleet.ID.ToString()}");
								AddTimer($"RecallTimer-{fleet.ID.ToString()}", new Timer(RetireFleet, fleet, (long) newRecallDelay, Timeout.Infinite));
								AddPendingRecall(fleet.ID, currentTime.AddMilliseconds(newRecallDelay), fleet.Origin?.ToString());
								if ((bool) _tbotInstance.InstanceSettings.Defender.TelegramMessenger.Active) {
									await _tbotInstance.SendTelegramMessage($"Recall delayed! Attack still incoming on {fleet.Origin}. New recall in {newRecallDelay/1000}s");
								}
								return;
							} else {
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"[RECALL-OK] Fleet {fleet.ID} return time ({estimatedReturnSeconds}s) is AFTER last attack ({lastAttack.ArriveIn}s) - proceeding with recall");
							}
						}
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"[RECALL-CHECK] Error checking attacks before recall: {e.Message}");
			}

			doRecall:
			try {
				await Task.Delay((int) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Recall Fleet with ID: {fleet.ID}");
				await _ogameService.CancelFleet(fleet);
				await Task.Delay((int) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				var updatedFleets = await UpdateFleets();
				lock (_fleetLock) {
					_tbotInstance.UserData.fleets = updatedFleets;
				}
				Fleet recalledFleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleet.ID) ?? new() { ID = (int) SendFleetCode.GenericError };
				if (recalledFleet.ID == (int) SendFleetCode.GenericError) {
					_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, "Unable to recall fleet: an unknon error has occurred, already recalled ?.");
				} else {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					if ((bool) _tbotInstance.InstanceSettings.Defender.TelegramMessenger.Active) {
						await _tbotInstance.SendTelegramMessage($"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					}
					return;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to recall fleet: an exception has occurred: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return;
			} finally {
				DisposeAndRemoveTimer($"RecallTimer-{fleet.ID.ToString()}");
				RemovePendingRecall(fleet.ID);
}
		}
		public async Task<List<Fleet>> UpdateFleets() {
			try {
				return await _ogameService.GetFleets();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var letzte = _tbotInstance.UserData.fleets;
				return letzte != null ? new List<Fleet>(letzte) : new();
			}
		}

		public async Task<bool> DebrisSave(Celestial celestial, Coordinate debris, bool random, long durationSeconds, decimal speed) {
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			var ships = celestial.Ships.GetMovableShips();
			if (ships.IsEmpty()) {
				await _tbotInstance.SendTelegramMessage($"DebrisSave: no ships on {celestial.Coordinate}.");
				return false;
			}
			if (celestial.Resources.Deuterium == 0) {
				await _tbotInstance.SendTelegramMessage($"DebrisSave: no deuterium on {celestial.Coordinate}.");
				return false;
			}
			if (random) {
				debris = await FindNearestDebris(celestial);
				if (debris == null) {
					await _tbotInstance.SendTelegramMessage($"DebrisSave: no debris field with resources within 10 systems of {celestial.Coordinate}.");
					return false;
				}
			}
			debris.Type = Celestials.Debris;
			int fleetId = await SendFleet(celestial, ships, debris, Missions.Harvest, speed, celestial.Resources, _tbotInstance.UserData.userInfo.Class, true, true, fleetsave: true);
			if (fleetId <= 0) {
				await _tbotInstance.SendTelegramMessage($"DebrisSave to {debris} failed (code {fleetId}). Does a debris field exist there, and is a Recycler included?");
				return false;
			}
			Fleet fleet = null;
			try {
				_tbotInstance.UserData.fleets = await UpdateFleets();
				fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
			} catch { }
			fleet ??= new Fleet { ID = fleetId, Origin = celestial.Coordinate };

			long interval = (durationSeconds / 2) * 1000;
			if (interval <= 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (fleet.ArriveIn > 0) {
				long maxIntervalMs = (long) (fleet.ArriveIn - 90) * 1000;
				if (maxIntervalMs < 30000) maxIntervalMs = 30000;
				if (interval > maxIntervalMs) interval = maxIntervalMs;
			}
			DateTime time = await _tbotOgameBridge.GetDateTime();
			lock (_recallDepartureLock) { _recallDeparture[fleetId] = time; }
			DateTime recallAt = time.AddMilliseconds(interval);
			AddTimer($"RecallTimer-{fleetId}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
			AddPendingRecall(fleetId, recallAt, fleet.Origin?.ToString() ?? celestial.ToString());
			_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"[DebrisSave] Fleet {fleetId} -> {debris}, recall at {recallAt}");
			await _tbotInstance.SendTelegramMessage($"DebrisSave: fleet {fleetId} is flying to {debris}, recall at {recallAt}.");
			return true;
		}

		private async Task<Coordinate> FindNearestDebris(Celestial celestial) {
			for (int d = 0; d <= 10; d++) {
				foreach (int sys in d == 0 ? new[] { celestial.Coordinate.System } : new[] { celestial.Coordinate.System - d, celestial.Coordinate.System + d }) {
					if (sys < 1) continue;
					var galaxyInfo = await _ogameService.GetGalaxyInfo(celestial.Coordinate.Galaxy, GeneralHelper.ClampSystem(sys));
					var hit = galaxyInfo.Planets
						.Where(pl => pl != null && pl.Debris != null && pl.Debris.Resources.TotalResources > 0)
						.OrderBy(pl => Math.Abs(pl.Coordinate.Position - celestial.Coordinate.Position))
						.FirstOrDefault();
					if (hit != null)
						return new Coordinate(hit.Coordinate.Galaxy, hit.Coordinate.System, hit.Coordinate.Position, Celestials.Debris);
				}
			}
			return null;
		}

		public async Task<ShadowFlightResult> ShadowFlight(Celestial celestial, Ships ships, int deutFlights, long totalSeconds, decimal speed) {
			var r = new ShadowFlightResult();
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			Coordinate debris = await FindNearestDebris(celestial);
			if (debris == null) { r.Problem = "no debris field with resources within 10 systems"; return r; }
			r.Debris = debris;
			var prediction = _calcService.CalcFleetPrediction(celestial.Coordinate, debris, ships, Missions.Harvest, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
			long capacity = _calcService.CalcFleetCapacity(ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
			var plan = ShadowFleetLogic.PlanDeuterium(celestial.Resources.Deuterium, prediction.Fuel, deutFlights, capacity);
			r.Fuel = prediction.Fuel;
			r.Level = plan.Level;
			r.FlightsPossible = plan.FlightsPossible;
			r.Payload = plan.Payload;
			if (plan.Level == DeutLevel.None) { r.Problem = "not enough deuterium for one more flight"; return r; }
			debris.Type = Celestials.Debris;
			var payload = new Resources(0, 0, plan.Payload);
			int fleetId = await SendFleet(celestial, ships, debris, Missions.Harvest, speed, payload, _tbotInstance.UserData.userInfo.Class, true, true, shrinkDeuteriumPayload: true, fleetsave: true);
			if (fleetId <= 0) { r.Problem = $"send failed (code {fleetId})"; return r; }
			Fleet fleet = null;
			try {
				_tbotInstance.UserData.fleets = await UpdateFleets();
				fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
			} catch { }
			fleet ??= new Fleet { ID = fleetId, Origin = celestial.Coordinate };
			long interval = (totalSeconds / 2) * 1000;
			if (interval <= 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (fleet.ArriveIn > 0) {
				long maxIntervalMs = (long) (fleet.ArriveIn - 90) * 1000;
				if (maxIntervalMs < 30000) maxIntervalMs = 30000;
				if (interval > maxIntervalMs) interval = maxIntervalMs;
			}
			DateTime time = await _tbotOgameBridge.GetDateTime();
			lock (_recallDepartureLock) { _recallDeparture[fleetId] = time; }
			r.RecallAt = time.AddMilliseconds(interval);
			r.RoundTripSeconds = interval * 2 / 1000;
			AddTimer($"RecallTimer-{fleetId}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
			AddPendingRecall(fleetId, r.RecallAt, fleet.Origin?.ToString() ?? celestial.ToString());
			r.FleetId = fleetId;
			return r;
		}

		public void CancelPendingRecall(int fleetId) {
			DisposeAndRemoveTimer($"RecallTimer-{fleetId}");
			RemovePendingRecall(fleetId);
		}

		public async void RetireFleet(object fleet) { try {
			try {
				await CancelFleet((Fleet)fleet);
			} catch (Exception ex) {
				_tbotInstance?.log(LogLevel.Error, LogSender.FleetScheduler, $"RetireFleet callback failed: {ex.Message}");
			}
		} catch (Exception asyncVoidEx) { _tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unhandled error in RetireFleet: {asyncVoidEx}"); }
		}

		private async void GhostandSleepAfterFleetsReturnAll(object state) { try {
			try {
				DisposeAndRemoveTimer("GhostSleepTimer");

				var celestialsToFleetsave = await _tbotOgameBridge.UpdateCelestials();
				var moons = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
				var targets = moons.Count > 0 ? moons : celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

				foreach (Celestial celestial in targets)
					await AutoFleetSave(celestial, false, _tbotInstance.SleepDuration, false, _tbotInstance.TelegramUserData.Mission, true);

				await _tbotInstance.SleepNow(_tbotInstance.NextWakeUpTime);
			} catch (Exception ex) {
				_tbotInstance?.log(LogLevel.Error, LogSender.SleepMode, $"GhostSleep(all) timer callback failed: {ex.Message}");
			}
		} catch (Exception asyncVoidEx) { _tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unhandled error in GhostandSleepAfterFleetsReturnAll: {asyncVoidEx}"); }
		}

		private async void GhostandSleepAfterFleetsReturn(object state) { try {
			try {
				DisposeAndRemoveTimer("GhostSleepTimer");

				await AutoFleetSave(_tbotInstance.TelegramUserData.CurrentCelestialToSave, false, _tbotInstance.SleepDuration, false, _tbotInstance.TelegramUserData.Mission, true);
				await _tbotInstance.SleepNow(_tbotInstance.NextWakeUpTime);
			} catch (Exception ex) {
				_tbotInstance?.log(LogLevel.Error, LogSender.SleepMode, $"GhostSleep timer callback failed: {ex.Message}");
			}
		} catch (Exception asyncVoidEx) { _tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unhandled error in GhostandSleepAfterFleetsReturn: {asyncVoidEx}"); }
		}


		private static bool IstTransportTyp(Buildables typ) {
			return typ == Buildables.SmallCargo || typ == Buildables.LargeCargo;
		}

		private long LiesExpoSchiffeJeExpedition(string typName) {
			try {
				var expo = _tbotInstance.InstanceSettings.Expeditions;
				if (!SettingsService.IsSettingSet(expo, "ManualShips"))
					return 0;
				var manuell = expo.ManualShips;
				if (!SettingsService.IsSettingSet(manuell, "Active") || !(bool) manuell.Active || !SettingsService.IsSettingSet(manuell, "Ships"))
					return 0;
				var schiffe = manuell.Ships;
				if (!SettingsService.IsSettingSet(schiffe, typName))
					return 0;
				return Math.Max(0L, (long) (typName == "SmallCargo" ? schiffe.SmallCargo : schiffe.LargeCargo));
			} catch (Exception) {
				return 0;
			}
		}

		private bool IstExpeditionsHerkunft(Celestial origin) {
			try {
				var expo = _tbotInstance.InstanceSettings.Expeditions;
				if (!SettingsService.IsSettingSet(expo, "Origin"))
					return true;
				bool irgendeiner = false;
				foreach (var o in expo.Origin) {
					irgendeiner = true;
					if (origin.Coordinate.Galaxy == (int) o.Galaxy && origin.Coordinate.System == (int) o.System && origin.Coordinate.Position == (int) o.Position
						&& origin.Coordinate.Type == Enum.Parse<Celestials>((string) o.Type, true))
						return true;
				}
				return !irgendeiner;
			} catch (Exception) {
				return true;
			}
		}

		private long LiesTransportReserve(Celestial origin, Buildables typ) {
			try {
				var transports = _tbotInstance.InstanceSettings.Brain.Transports;
				if (!SettingsService.IsSettingSet(transports, "KeepOnOrigin"))
					return 0;
				var keep = transports.KeepOnOrigin;
				string name = typ == Buildables.SmallCargo ? "SmallCargo" : (typ == Buildables.LargeCargo ? "LargeCargo" : "");
				if (name == "")
					return 0;
				long fest = 0;
				if (SettingsService.IsSettingSet(keep, name))
					fest = Math.Max(0L, (long) (typ == Buildables.SmallCargo ? keep.SmallCargo : keep.LargeCargo));
				long jeSlot = 0;
				string slotName = name + "PerFreeExpoSlot";
				if (SettingsService.IsSettingSet(keep, "UseExpeditionManualShips") && (bool) keep.UseExpeditionManualShips)
					jeSlot = LiesExpoSchiffeJeExpedition(name);
				else if (SettingsService.IsSettingSet(keep, slotName))
					jeSlot = Math.Max(0L, (long) (typ == Buildables.SmallCargo ? keep.SmallCargoPerFreeExpoSlot : keep.LargeCargoPerFreeExpoSlot));
				long dynamisch = 0;
				if (jeSlot > 0 && IstExpeditionsHerkunft(origin)) {
					bool expoAktiv = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings, "Expeditions") || !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "Active") || (bool) _tbotInstance.InstanceSettings.Expeditions.Active;
					if (expoAktiv) {
						long freieSlots = Math.Max(0L, (long) _tbotInstance.UserData.slots.ExpTotal - (long) _tbotInstance.UserData.slots.ExpInUse);
						dynamisch = jeSlot * freieSlots;
					}
				}
				return fest + dynamisch;
			} catch (Exception) {
			}
			return 0;
		}

		private long VerfuegbareTransportSchiffe(Celestial origin, Buildables typ) {
			long vorhanden = origin.Ships.GetAmount(typ);
			if (!IstTransportTyp(typ))
				return vorhanden;
			return Math.Max(0L, vorhanden - LiesTransportReserve(origin, typ));
		}

		private List<Buildables> LiesTransportKette(Buildables konfiguriert) {
			List<Buildables> kette = new() { konfiguriert };
			try {
				var transports = _tbotInstance.InstanceSettings.Brain.Transports;
				if (SettingsService.IsSettingSet(transports, "CargoFallback")) {
					foreach (var eintrag in transports.CargoFallback) {
						if (Enum.TryParse<Buildables>((string) eintrag, true, out Buildables typ) && IstTransportTyp(typ) && !kette.Contains(typ))
							kette.Add(typ);
					}
				}
			} catch (Exception) {
			}
			return kette;
		}

		private bool LiesMischflotte() {
			try {
				var transports = _tbotInstance.InstanceSettings.Brain.Transports;
				return SettingsService.IsSettingSet(transports, "MixFleet") && (bool) transports.MixFleet;
			} catch (Exception) {
				return false;
			}
		}

		private long KapazitaetJeSchiff(Celestial origin, Buildables typ) {
			try {
				Ships eins = new();
				eins.Add(typ, 1);
				Resources gross = new Resources(1000000000000L, 0, 0);
				Resources passt = _calcService.CalcMaxTransportableResources(eins, gross, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
				return passt.Metal + passt.Crystal + passt.Deuterium;
			} catch (Exception) {
				return 0;
			}
		}

		internal static List<KeyValuePair<Buildables, long>> WaehleTransportKette(IList<Buildables> kette, long gesamtLadung, Func<Buildables, long> verfuegbar, Func<Buildables, long> kapazitaetJeSchiff, bool mischen) {
			if (gesamtLadung <= 0)
				return null;
			foreach (Buildables typ in kette) {
				long kap = kapazitaetJeSchiff(typ);
				if (kap <= 0)
					continue;
				long noetig = (gesamtLadung + kap - 1) / kap;
				if (noetig <= verfuegbar(typ))
					return new List<KeyValuePair<Buildables, long>> { new KeyValuePair<Buildables, long>(typ, noetig) };
			}
			if (!mischen)
				return null;
			List<KeyValuePair<Buildables, long>> mix = new();
			long rest = gesamtLadung;
			foreach (Buildables typ in kette) {
				long kap = kapazitaetJeSchiff(typ);
				long verf = verfuegbar(typ);
				if (kap <= 0 || verf <= 0)
					continue;
				long noetig = (rest + kap - 1) / kap;
				if (noetig <= verf) {
					mix.Add(new KeyValuePair<Buildables, long>(typ, noetig));
					rest = 0;
					break;
				}
				mix.Add(new KeyValuePair<Buildables, long>(typ, verf));
				rest -= verf * kap;
			}
			return (rest <= 0 && mix.Count > 1) ? mix : null;
		}

		private Buildables WaehleTransportSchiff(Celestial origin, Resources payload, Buildables konfiguriert, out Ships mischflotte) {
			mischflotte = null;
			try {
				if (!IstTransportTyp(konfiguriert))
					return konfiguriert;
				List<Buildables> kette = LiesTransportKette(konfiguriert);
				if (kette.Count < 2)
					return konfiguriert;
				long gesamt = payload.Metal + payload.Crystal + payload.Deuterium;
				var wahl = WaehleTransportKette(kette, gesamt, t => VerfuegbareTransportSchiffe(origin, t), t => KapazitaetJeSchiff(origin, t), LiesMischflotte());
				if (wahl == null) {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Transport chain {origin.ToString()}: no ship combination carries {gesamt:N0} (available after reserve: {string.Join(", ", kette.Select(t => t.ToString() + " " + VerfuegbareTransportSchiffe(origin, t).ToString("N0")))}).");
					return konfiguriert;
				}
				if (wahl.Count == 1) {
					if (wahl[0].Key != konfiguriert)
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Transport chain {origin.ToString()}: {konfiguriert} is not enough (available {VerfuegbareTransportSchiffe(origin, konfiguriert):N0}) - using {wahl[0].Key} ({wahl[0].Value:N0}).");
					return wahl[0].Key;
				}
				Ships mix = new();
				foreach (var eintrag in wahl)
					mix.Add(eintrag.Key, eintrag.Value);
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Transport chain {origin.ToString()}: mixed fleet {string.Join(" + ", wahl.Select(e => e.Value.ToString("N0") + " " + e.Key.ToString()))} (speed of the slowest ship).");
				mischflotte = mix;
				return wahl[wahl.Count - 1].Key;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Transport chain failed ({e.Message}) - using the configured type.");
				mischflotte = null;
				return konfiguriert;
			}
		}

		private bool KannVollstaendigLiefern(Celestial herkunft, Ships schiffe, Coordinate ziel, Resources menge, Resources zuruecklassen) {
			try {
				long mengeMetall = menge.Metal;
				long mengeKristall = menge.Crystal;
				long mengeDeut = menge.Deuterium;
				var vorhersage = _calcService.CalcFleetPrediction(herkunft.Coordinate, ziel, schiffe, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, herkunft.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
				long sprit = (long) vorhersage.Fuel;
				bool okMetall = herkunft.Resources.Metal >= mengeMetall + zuruecklassen.Metal;
				bool okKristall = herkunft.Resources.Crystal >= mengeKristall + zuruecklassen.Crystal;
				bool okDeut = herkunft.Resources.Deuterium >= mengeDeut + sprit + zuruecklassen.Deuterium;
				Resources passt = _calcService.CalcMaxTransportableResources(schiffe, new Resources(mengeMetall, mengeKristall, mengeDeut), _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, herkunft.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
				bool okLaderaum = passt.Metal >= mengeMetall && passt.Crystal >= mengeKristall && passt.Deuterium >= mengeDeut;
				if (!(okMetall && okKristall && okDeut && okLaderaum)) {
					_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Partial delivery check {herkunft.ToString()}: metal {okMetall}, crystal {okKristall}, deuterium incl. fuel ({sprit}) {okDeut}, cargo space {okLaderaum}");
					return false;
				}
				return true;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Partial delivery check failed ({e.Message}) - nothing is sent.");
				return false;
			}
		}
		
		private bool CheckFuel(FleetHypotesis fleetHypotesis, Celestial celestial) {
			if (celestial.Resources.Deuterium < fleetHypotesis.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: not enough fuel!");
				return false;
			}
			if (_calcService.CalcFleetFuelCapacity(fleetHypotesis.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) < fleetHypotesis.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: ships don't have enough fuel capacity!");
				return false;
			}
			return true;
		}

		private async Task<List<FleetHypotesis>> GetFleetSaveDestination(List<Celestial> source, Celestial origin, DateTime departureDate, long minFlightTime, Missions mission, long maxFuel) {
			var validSpeeds = _tbotInstance.UserData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
			List<FleetHypotesis> possibleFleets = new();
			List<Coordinate> possibleDestinations = new();
			GalaxyInfo galaxyInfo = new();
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
			int sys = 0;

			int playerid = _tbotInstance.UserData.userInfo.PlayerID;

			switch (mission) {
				case Missions.Transport:
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						if (possibleDestinations.Count() == 10) {
							break;
						}
						sys = GeneralHelper.ClampSystem(sys);

                                     var gi = await _ogameService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
                                      if (gi?.Planets == null) continue;

                                      foreach (var planet in gi.Planets) {
                                    if (planet != null && !planet.Vacation && !planet.Administrator && !planet.Banned && !planet.StrongPlayer) {
                             possibleDestinations.Add(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet));
                                          }
                            }
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								FleetHypotesis fleetHypotesis = new() {
									Origin = origin,
									Destination = possibleDestination,
									Ships = origin.Ships.GetMovableShips(),
									Mission = mission,
									Speed = currentSpeed,
									Duration = fleetPrediction.Time,
									Fuel = fleetPrediction.Fuel
								};
								if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
									possibleFleets.Add(fleetHypotesis);
									break;
								}
							}
						}
					}
					break;

				case Missions.Spy:
					if (origin.Ships.EspionageProbe == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No espionageprobe available, skipping to next mission...");
						break;
					}
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						sys = GeneralHelper.ClampSystem(sys);
						Coordinate destination = new(origin.Coordinate.Galaxy, sys, 16, Celestials.Planet);
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = destination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;

				case Missions.Colonize:
					if (origin.Ships.ColonyShip == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No colony ship available, skipping to next mission...");
						break;
					}
					galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate);

					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						int pos = 0;
						sys = GeneralHelper.ClampSystem(sys);
						galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
						foreach (var planet in galaxyInfo.Planets) {
					pos++;
					if (pos >= 1 && pos <= 15 && planet == null) {
						possibleDestinations.Add(new(origin.Coordinate.Galaxy, sys, pos));
					}
				}
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								FleetHypotesis fleetHypotesis = new() {
									Origin = origin,
									Destination = possibleDestination,
									Ships = origin.Ships.GetMovableShips(),
									Mission = mission,
									Speed = currentSpeed,
									Duration = fleetPrediction.Time,
									Fuel = fleetPrediction.Fuel
								};
								if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
									possibleFleets.Add(fleetHypotesis);
									break;
								}
							}
						}
					}
					break;

				case Missions.Harvest:
					if (origin.Ships.Recycler == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No recycler available, skipping to next mission...");
						break;
					}
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						sys = GeneralHelper.ClampSystem(sys);
						galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
						foreach (var planet in galaxyInfo.Planets) {
							if (planet != null && planet.Debris != null && planet.Debris.Resources.TotalResources > 0) {
								possibleDestinations.Add(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris));
							}
						}
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								FleetHypotesis fleetHypotesis = new() {
									Origin = origin,
									Destination = possibleDestination,
									Ships = origin.Ships.GetMovableShips(),
									Mission = mission,
									Speed = currentSpeed,
									Duration = fleetPrediction.Time,
									Fuel = fleetPrediction.Fuel
								};
								if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
									possibleFleets.Add(fleetHypotesis);
									break;
								}
							}
						}
					}
					break;

				case Missions.Deploy:
					possibleDestinations = _tbotInstance.UserData.celestials
						.Where(planet => planet.ID != origin.ID)
						.Where(planet => (planet.Coordinate.Type == Celestials.Moon))
						.Select(planet => planet.Coordinate)
						.ToList();

					if (possibleDestinations.Count == 0) {
						possibleDestinations = _tbotInstance.UserData.celestials
							.Where(planet => planet.ID != origin.ID)
							.Select(planet => planet.Coordinate)
							.ToList();
					}

					decimal[] deploySpeeds = origin.Coordinate.Type == Celestials.Moon
						? new[] { Speeds.TenPercent }
						: new[] { Speeds.TenPercent * 3, Speeds.TenPercent * 2, Speeds.TenPercent };
					deploySpeeds = deploySpeeds.Where(s => s == Speeds.TenPercent || validSpeeds.Contains(s)).ToArray();

					bool isHomePlanet = false;
					try {
						isHomePlanet = origin.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy
							&& origin.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System
							&& origin.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position;
					} catch {
					}

					List<List<Coordinate>> destinationSets = new();
					try {
						if (isHomePlanet)
							throw new InvalidOperationException("Home planet: DeployTarget is skipped");
						int dtGalaxy = (int) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeployTarget.Galaxy;
						int dtSystem = (int) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeployTarget.System;
						int dtPosition = (int) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeployTarget.Position;
						var targetMoon = _tbotInstance.UserData.celestials.FirstOrDefault(c =>
							c.ID != origin.ID
							&& c.Coordinate.Type == Celestials.Moon
							&& c.Coordinate.Galaxy == dtGalaxy
							&& c.Coordinate.System == dtSystem
							&& c.Coordinate.Position == dtPosition);
						if (targetMoon != null)
							destinationSets.Add(new List<Coordinate> { targetMoon.Coordinate });
					} catch {
					}
					destinationSets.Add(possibleDestinations);

					foreach (var destinationSet in destinationSets) {
					foreach (decimal currentSpeed in deploySpeeds) {
						foreach (var possibleDestination in destinationSet) {
							FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = possibleDestination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime / 2 + 300 && fleetHypotesis.Fuel <= maxFuel) {
								possibleFleets.Add(fleetHypotesis);
							}
						}
						if (possibleFleets.Count > 0)
							break;
					}
					if (possibleFleets.Count > 0)
						break;
					if (destinationSet != possibleDestinations)
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {origin.ToString()}: DeployTarget not usable (flight time/fuel), searching among all own moons...");
					}
					break;

				default:
					break;
			}

			if (possibleFleets.Count() > 0) {
				return possibleFleets;

			} else {
				return new List<FleetHypotesis>();
			}
		}

		private int LiesMaxGebuendelteBauten(out long maxSekunden) {
			maxSekunden = 0;
			try {
				var t = _tbotInstance.InstanceSettings.Brain.Transports;
				if (SettingsService.IsSettingSet(t, "Lookahead")) {
					var la = t.Lookahead;
					if (SettingsService.IsSettingSet(la, "Active") && (bool) la.Active) {
						string worker = LedgerTBot.CurrentOwner ?? "";
						if (worker != "" && !SettingsService.GetSetting<bool>(la, worker, true))
							return 0;
						if (SettingsService.IsSettingSet(la, "MaxHours"))
							maxSekunden = (long) Math.Max(0, Math.Round((double) la.MaxHours * 3600));
						return SettingsService.IsSettingSet(la, "MaxItems") ? Math.Max(0, (int) la.MaxItems) : 10;
					}
				}
				if (SettingsService.IsSettingSet(t, "MaxBundledBuildings"))
					return Math.Max(0, (int) t.MaxBundledBuildings);
			} catch (Exception) {
			}
			return 0;
		}

		private static string TransportReason(Celestial dest, string what, Resources cost) {
			string owner = LedgerTBot.CurrentOwner;
			string text = " - requested by " + (string.IsNullOrEmpty(owner) ? "unknown worker" : owner);
			if (!string.IsNullOrEmpty(what))
				text += " for " + what;
			if (dest != null)
				text += " on " + dest.ToString();
			if (cost != null)
				text += $" (full cost {cost.TransportableResources})";
			return text;
		}

		private static string LevelText(Func<int> currentLevel) {
			try { return " level " + (currentLevel() + 1); } catch { return ""; }
		}

		public async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				string forWhat = TransportReason(destination, buildable != Buildables.Null ? buildable.ToString() + LevelText(() => destination.GetLevel(buildable)) : "", resources);
				if (origin.ID == destination.ID) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = LedgerTBot.MissingForTransport(_tbotInstance, resources, destination, out bool ledgerApplied);
					if (ledgerApplied && missingResources.Metal + missingResources.Crystal + missingResources.Deuterium <= 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "[Ledger] Demand for " + destination.ToString() + " already covered by stock/own deliveries - no transport needed.");
						return 0;
					}
					Resources resToLeave = new(0, 0, 0);
					resToLeave.Deuterium = TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, origin.Coordinate.Type);

					origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}
						
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						float cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						long flightTime = flightPrediction.Time;
						var availableShips = VerfuegbareTransportSchiffe(origin, preferredShip);
						if (buildable != Buildables.Null) {
							int level = _calcService.GetNextLevel(destination, buildable);
							long buildTime = _calcService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								int gebuendelt = 0;
								int maxGebuendelt = LiesMaxGebuendelteBauten(out long maxZusatzSekunden);
								long zusatzSekunden = 0;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips && gebuendelt < maxGebuendelt && (maxZusatzSekunden <= 0 || zusatzSekunden < maxZusatzSekunden)) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += _calcService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += _calcService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.Resources.Energy -= _calcService.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= _calcService.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = _calcService.GetNextBuildingToBuild(tempCelestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = _calcService.GetNextLunarFacilityToBuild(tempCelestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
									}
									if ((nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite)) {
										var nextLevel = _calcService.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(_calcService.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = _calcService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (newIdealShips <= VerfuegbareTransportSchiffe(origin, preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												{ long zeit = _calcService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial.Facilities); buildTime += zeit; zusatzSekunden += zeit; }
												_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
													gebuendelt++;
												level = nextLevel;
												buildable = nextBuildable;
											} else {
												break;
											}
										} else {
											break;
										}
									} else {
										break;
									}
								}
							}
						}

						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.Transports, "RoundResources") && (bool) _tbotInstance.InstanceSettings.Brain.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						Ships mischflotte = null;
						Buildables gewaehlterTyp = WaehleTransportSchiff(origin, missingResources, preferredShip, out mischflotte);
						if (gewaehlterTyp != preferredShip || mischflotte != null) {
							preferredShip = gewaehlterTyp;
							cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
							Ships zeitSchiffe = mischflotte;
							if (zeitSchiffe == null) {
								zeitSchiffe = new();
								zeitSchiffe.Add(preferredShip, 1);
							}
							flightTime = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, zeitSchiffe, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
						}

						bool doMultipleTransports = (mischflotte == null && idealShips > VerfuegbareTransportSchiffe(origin, preferredShip) && (bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) &&
							(origin.Coordinate.Galaxy == destination.Coordinate.Galaxy &&
							origin.Coordinate.System == destination.Coordinate.System &&
							origin.Coordinate.Position == destination.Coordinate.Position) ?
								true:
								false;

						if (mischflotte != null || idealShips <= VerfuegbareTransportSchiffe(origin, preferredShip)) {
							if (mischflotte != null) {
									ships = mischflotte;
								} else if (doMultipleTransports) {
								ships.Add(preferredShip, VerfuegbareTransportSchiffe(origin, preferredShip));
							} else {
								ships.Add(preferredShip, idealShips);
							}
							
							Coordinate pruefZiel = (differentDestination != null && differentDestination.ID != 0) ? differentDestination.Coordinate : destination.Coordinate;
							if (!KannVollstaendigLiefern(origin, ships, pruefZiel, missingResources, resToLeave)) {
								_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping transport: partial delivery blocked - {origin.ToString()} cannot deliver {missingResources.TransportableResources} completely (resources/fuel/cargo space).");
								return (int) SendFleetCode.NotEnoughRessources;
							}

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

								float metProdInASecond = destination.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
								float cryProdInASecond = destination.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
								float deutProdInASecond = destination.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
								var metProdInFlightTime = metProdInASecond * flightTime;
								var cryProdInFlightTime = cryProdInASecond * flightTime;
								var deutProdInFlightTime = deutProdInASecond * flightTime;

								if (
									(metProdInASecond == 0 && missingResources.Metal > 0) ||
									(cryProdInFlightTime == 0 && missingResources.Crystal > 0) ||
									(deutProdInFlightTime == 0 && missingResources.Deuterium > 0) ||
									missingResources.Metal >= metProdInFlightTime ||
									missingResources.Crystal >= cryProdInFlightTime ||
									missingResources.Deuterium >= deutProdInFlightTime ||
									resources.Metal > destination.ResourcesProduction.Metal.StorageCapacity ||
									resources.Crystal > destination.ResourcesProduction.Crystal.StorageCapacity ||
									resources.Deuterium > destination.ResourcesProduction.Deuterium.StorageCapacity
								) {
									if (differentDestination != null && differentDestination.ID != 0)
										destination = differentDestination;
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}{forWhat}");
									return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: it is quicker to wait for production.");
									return (int) SendFleetCode.QuickerToWaitForProduction;
								}
							} else {
								if (differentDestination != null && differentDestination.ID != 0)
									destination = differentDestination;
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}{forWhat}");
								return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping transport: not enough resources in {origin.ToString()}. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return (int) SendFleetCode.NotEnoughRessources;
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"HandleMinerTransport Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		public async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Celestial differentDestination, Resources resources, LFBuildables buildable = LFBuildables.None, LFBuildings maxLFBuildings = null, bool preventIfMoreExpensiveThanNextMine = false) {
			try {
				string forWhat = TransportReason(destination, buildable != LFBuildables.None ? buildable.ToString() + LevelText(() => destination.GetLevel(buildable)) : "", resources);
				if (origin.ID == destination.ID) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = LedgerTBot.MissingForTransport(_tbotInstance, resources, destination, out bool ledgerApplied);
					if (ledgerApplied && missingResources.Metal + missingResources.Crystal + missingResources.Deuterium <= 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "[Ledger] Demand for " + destination.ToString() + " already covered by stock/own deliveries - no transport needed.");
						return 0;
					}
					Resources resToLeave = new(0, 0, 0);
					resToLeave.Deuterium = TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, origin.Coordinate.Type);
					
					origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
						destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Facilities);
						destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Constructions);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBuildings);
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						float cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) + 1;
						Ships ships = new();
						Ships tempShips = new();
						if (maxLFBuildings == null)
							maxLFBuildings = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						long flightTime = flightPrediction.Time;
						idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						var availableShips = VerfuegbareTransportSchiffe(origin, preferredShip);
						if (buildable != LFBuildables.None) {
							destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.LFBuildings);
							destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.LFTechs);
							destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.LFBonuses);
							int level = _calcService.GetNextLevel(destination, buildable);
							long buildTime = _calcService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination);
							var tempCelestial = destination;
								int gebuendelt = 0;
								int maxGebuendelt = LiesMaxGebuendelteBauten(out long maxZusatzSekunden);
								long zusatzSekunden = 0;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips && gebuendelt < maxGebuendelt && (maxZusatzSekunden <= 0 || zusatzSekunden < maxZusatzSekunden)) {
								tempCelestial.SetLevel(buildable, level);
									
								tempCelestial.ResourcesProduction.Population.LivingSpace = _calcService.CalcLivingSpace(tempCelestial as Planet);
								tempCelestial.ResourcesProduction.Population.Satisfied = _calcService.CalcSatisfied(tempCelestial as Planet);

								bool preventTechBuilding = false;
								if (tempCelestial.Constructions.LFResearchCountdown > 0) {
									preventTechBuilding = true;
								}

								var nextBuildable = LFBuildables.None;									
								nextBuildable = _calcService.GetNextLFBuildingToBuild(tempCelestial as Planet, maxLFBuildings, preventIfMoreExpensiveThanNextMine, preventTechBuilding);
								if (nextBuildable != LFBuildables.None) {
									var nextLevel = _calcService.GetNextLevel(tempCelestial, nextBuildable);
									float costReduction = _calcService.CalcLFBuildingsResourcesCostBonus(tempCelestial);
									float popReduction = _calcService.CalcLFBuildingsPopulationCostBonus(tempCelestial);
									var newMissingRes = missingResources.Sum(_calcService.CalcPrice(nextBuildable, nextLevel, costReduction, 0, popReduction));

									if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
										var newIdealShips = _calcService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
										if (newIdealShips <= VerfuegbareTransportSchiffe(origin, preferredShip)) {
											idealShips = newIdealShips;
												missingResources = newMissingRes;
											{ long zeit = _calcService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial); buildTime += zeit; zusatzSekunden += zeit; }
											_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
													gebuendelt++;
											level = nextLevel;
											buildable = nextBuildable;
										} else {
											break;
										}
									} else {
										break;
									}
								} else {
									break;
								}
							}
						}

						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.Transports, "RoundResources") && (bool) _tbotInstance.InstanceSettings.Brain.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						Ships mischflotte = null;
						Buildables gewaehlterTyp = WaehleTransportSchiff(origin, missingResources, preferredShip, out mischflotte);
						if (gewaehlterTyp != preferredShip || mischflotte != null) {
							preferredShip = gewaehlterTyp;
							cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
							Ships zeitSchiffe = mischflotte;
							if (zeitSchiffe == null) {
								zeitSchiffe = new();
								zeitSchiffe.Add(preferredShip, 1);
							}
							flightTime = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, zeitSchiffe, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
						}

						bool doMultipleTransports = (mischflotte == null && idealShips > VerfuegbareTransportSchiffe(origin, preferredShip) && (bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) &&
							(origin.Coordinate.Galaxy == destination.Coordinate.Galaxy &&
							origin.Coordinate.System == destination.Coordinate.System &&
							origin.Coordinate.Position == destination.Coordinate.Position) ?
								true:
								false;
						
						if (mischflotte != null || idealShips <= VerfuegbareTransportSchiffe(origin, preferredShip)) {
							if (mischflotte != null) {
									ships = mischflotte;
								} else if (doMultipleTransports) {
								ships.Add(preferredShip, VerfuegbareTransportSchiffe(origin, preferredShip));
							} else {
								ships.Add(preferredShip, idealShips);
							}

							Coordinate pruefZiel = (differentDestination != null && differentDestination.ID != 0) ? differentDestination.Coordinate : destination.Coordinate;
							if (!KannVollstaendigLiefern(origin, ships, pruefZiel, missingResources, resToLeave)) {
								_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping transport: partial delivery blocked - {origin.ToString()} cannot deliver {missingResources.TransportableResources} completely (resources/fuel/cargo space).");
								return (int) SendFleetCode.NotEnoughRessources;
							}

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

								float metProdInASecond = destination.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
								float cryProdInASecond = destination.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
								float deutProdInASecond = destination.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
								var metProdInFlightTime = metProdInASecond * flightTime;
								var cryProdInFlightTime = cryProdInASecond * flightTime;
								var deutProdInFlightTime = deutProdInASecond * flightTime;

								if (
									(metProdInASecond == 0 && missingResources.Metal > 0) ||
									(cryProdInFlightTime == 0 && missingResources.Crystal > 0) ||
									(deutProdInFlightTime == 0 && missingResources.Deuterium > 0) ||
									missingResources.Metal >= metProdInFlightTime ||
									missingResources.Crystal >= cryProdInFlightTime ||
									missingResources.Deuterium >= deutProdInFlightTime ||
									resources.Metal > destination.ResourcesProduction.Metal.StorageCapacity ||
									resources.Crystal > destination.ResourcesProduction.Crystal.StorageCapacity ||
									resources.Deuterium > destination.ResourcesProduction.Deuterium.StorageCapacity
								) {
									if (differentDestination != null && differentDestination.ID != 0)
										destination = differentDestination;
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}{forWhat}");
									return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: it is quicker to wait for production.");
									return (int) SendFleetCode.QuickerToWaitForProduction;
								}
							} else {
								if (differentDestination != null && differentDestination.ID != 0)
									destination = differentDestination;
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}{forWhat}");
								return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping transport: not enough resources in {origin.ToString()}. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return (int) SendFleetCode.NotEnoughRessources;
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"HandleMinerTransport Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		public async Task Collect(bool noLimit = false, string typFilter = null, bool skipIncoming = false) {
			await CollectImpl(true, noLimit, typFilter, skipIncoming);
		}

		public string PreviewCollect(bool noLimit = false, string typFilter = null, bool skipIncoming = false) {
			try {
				var rp = _tbotInstance.InstanceSettings.Brain.AutoRepatriate;
				if (rp.Target.Length == 0)
					return "No repatriate target is set (Brain.AutoRepatriate.Target).";
				List<Celestial> ziele = _calcService.ParseCelestialsList(rp.Target, _tbotInstance.UserData.celestials);
				List<Celestial> ausgenommen = _calcService.ParseCelestialsList(rp.Exclude, _tbotInstance.UserData.celestials);
				bool ohneMonde = (bool) rp.ExcludeMoons;
				var kommen = new List<string>();
				var uebersprungen = new List<string>();
				foreach (Celestial c in _tbotInstance.UserData.celestials.ToList()) {
					string name = c.ToString();
					if (ziele.Any(z => z.Coordinate.IsSame(c.Coordinate))) { uebersprungen.Add(name + " (target)"); continue; }
					if (ausgenommen.Has(c)) { uebersprungen.Add(name + " (excluded)"); continue; }
					if (typFilter != null && ((typFilter == "moon") != (c.Coordinate.Type == Celestials.Moon))) { uebersprungen.Add(name + " (" + typFilter + " only)"); continue; }
					if (c.Coordinate.Type == Celestials.Moon && ohneMonde) { uebersprungen.Add(name + " (ExcludeMoons)"); continue; }
					if (skipIncoming && _calcService.IsThereTransportTowardsCelestial(c, _tbotInstance.UserData.fleets)) { uebersprungen.Add(name + " (transport incoming)"); continue; }
					kommen.Add(name);
				}
				string text = $"Target: {string.Join(", ", ziele.Select(z => z.ToString()))}\n" +
					$"Would be checked ({kommen.Count}): {(kommen.Count > 0 ? string.Join(", ", kommen) : "none")}\n" +
					$"Skipped ({uebersprungen.Count}): {(uebersprungen.Count > 0 ? string.Join(", ", uebersprungen) : "none")}\n" +
					(noLimit ? "Minimum resources are ignored." : "Minimum resources and the ledger still apply when running.");
				return text;
			} catch (Exception e) {
				return "Preview not possible: " + e.Message;
			}
		}

		public async Task CollectDeut(long MinAmount = 0) {
			_tbotInstance.UserData.fleets = await UpdateFleets();
			long TotalDeut = 0;
			Coordinate destinationCoordinate;

			Celestial cel = _tbotInstance.UserData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Galaxy)
					.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.System)
					.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

			if (cel.ID == 0) {
				await _tbotInstance.SendTelegramMessage("Error! Could not parse auto repatriate Celestial from JSON InstanceSettings. Need <code>/editsettings</code>");
				return;
			} else {
				destinationCoordinate = cel.Coordinate;
			}

			foreach (Celestial celestial in _tbotInstance.UserData.celestials.ToList()) {
				if (celestial.Coordinate.IsSame(destinationCoordinate)) {
					continue;
				}
				if (celestial is Moon) {
					continue;
				}

				var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				_tbotInstance.UserData.fleets = await UpdateFleets();

				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);

				Buildables preferredShip = Buildables.LargeCargo;
				if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
					preferredShip = Buildables.LargeCargo;
				}
				Resources payload = tempCelestial.Resources;
				payload.Metal = 0;
				payload.Crystal = 0;
				payload.Food = 0;

				if ((long) tempCelestial.Resources.Deuterium < (long) MinAmount || payload.IsEmpty()) {
					continue;
				}

				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
				float cargoBonus = tempCelestial.LFBonuses.GetShipCargoBonus(preferredShip);
				long idealShips = _calcService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

				Ships ships = new();
				if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
					if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
						ships.Add(preferredShip, idealShips);
					} else {
						ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
					}
					payload = _calcService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

					if ((long) payload.TotalResources >= (long) MinAmount) {
						var fleetId = await SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
						if (fleetId == (int) SendFleetCode.AfterSleepTime) {
							continue;
						}
						if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
							continue;
						}

						TotalDeut += payload.Deuterium;
					}
				} else {
					continue;
				}
			}

			if (TotalDeut > 0) {
				await _tbotInstance.SendTelegramMessage($"{TotalDeut} Deuterium sent.");
			} else {
				await _tbotInstance.SendTelegramMessage("No resources sent");
			}
		}

		private Buildables LiesRepatriateCargo() {
			Buildables typ = Buildables.SmallCargo;
			try {
				if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out typ))
					typ = Buildables.SmallCargo;
			} catch (Exception) {
				typ = Buildables.SmallCargo;
			}
			return typ;
		}

		private Ships WaehleHinflugSchiffe(Celestial origin, Resources ladung, Buildables konfiguriert) {
			try {
				if (!IstTransportTyp(konfiguriert))
					return null;
				Buildables typ = WaehleTransportSchiff(origin, ladung, konfiguriert, out Ships mischflotte);
				if (mischflotte != null)
					return mischflotte;
				float cargoBonus = origin.LFBonuses.GetShipCargoBonus(typ);
				long anzahl = _calcService.CalcShipNumberForPayload(ladung, typ, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
				if (anzahl <= 0 || anzahl > VerfuegbareTransportSchiffe(origin, typ))
					return null;
				Ships schiffe = new();
				schiffe.Add(typ, anzahl);
				return schiffe;
			} catch (Exception) {
				return null;
			}
		}

		private Buildables LiesBrainCargo() {
			Buildables typ = Buildables.SmallCargo;
			try {
				if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out typ))
					typ = Buildables.SmallCargo;
			} catch (Exception) {
				typ = Buildables.SmallCargo;
			}
			return typ;
		}

		private bool LiesBrainSchalter(string name) {
			try {
				var transports = _tbotInstance.InstanceSettings.Brain.Transports;
				if (!SettingsService.IsSettingSet(transports, name))
					return false;
				return name == "UseTransportChain" ? (bool) transports.UseTransportChain : (bool) transports.CompactSlotPlan;
			} catch (Exception) {
				return false;
			}
		}

		public int BrainMaxSendungen(int freieSlots) {
			return LiesBrainSchalter("CompactSlotPlan") ? freieSlots : int.MaxValue;
		}

		public Ships WaehleBrainSchiffe(Celestial origin, Resources payload) {
			try {
				Buildables konfiguriert = LiesBrainCargo();
				if (LiesBrainSchalter("UseTransportChain") && IstTransportTyp(konfiguriert))
					return WaehleHinflugSchiffe(origin, payload, konfiguriert);
				long anzahl = _calcService.CalcShipNumberForPayload(payload, konfiguriert, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, origin.LFBonuses.GetShipCargoBonus(konfiguriert), _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
				if (anzahl <= 0 || anzahl > origin.Ships.GetAmount(konfiguriert))
					return null;
				Ships schiffe = new();
				schiffe.Add(konfiguriert, anzahl);
				return schiffe;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Brain transport ship selection failed ({e.Message}).");
				return null;
			}
		}

		public Ships BrainQuellSchiffe(Celestial origin) {
			Ships ergebnis = new();
			try {
				Buildables konfiguriert = LiesBrainCargo();
				if (!LiesBrainSchalter("UseTransportChain") || !IstTransportTyp(konfiguriert)) {
					ergebnis.Add(konfiguriert, origin.Ships.GetAmount(konfiguriert));
					return ergebnis;
				}
				List<Buildables> kette = LiesTransportKette(konfiguriert);
				if (LiesMischflotte()) {
					foreach (Buildables typ in kette) {
						long verf = VerfuegbareTransportSchiffe(origin, typ);
						if (verf > 0)
							ergebnis.Add(typ, verf);
					}
					return ergebnis;
				}
				Buildables bester = konfiguriert;
				long besteKapazitaet = -1;
				foreach (Buildables typ in kette) {
					long kapazitaet = VerfuegbareTransportSchiffe(origin, typ) * KapazitaetJeSchiff(origin, typ);
					if (kapazitaet > besteKapazitaet) {
						besteKapazitaet = kapazitaet;
						bester = typ;
					}
				}
				long anzahl = VerfuegbareTransportSchiffe(origin, bester);
				if (anzahl > 0)
					ergebnis.Add(bester, anzahl);
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Brain transport source ships failed ({e.Message}).");
			}
			return ergebnis;
		}

		private ReverseHaken ErzeugeReverseHaken() {
			return new ReverseHaken {
				Kette = LiesTransportKette,
				HinflugSchiffe = WaehleHinflugSchiffe,
				Senden = (herkunft, schiffe, ziel, mission, tempo, nutzlast, erzwingen) => SendFleet(herkunft, schiffe, ziel, mission, tempo, nutzlast, CharacterClass.NoClass, erzwingen)
			};
		}

		public async Task<RepatriateCode> CollectImpl(bool fromTelegram, bool noLimit = false, string typFilter = null, bool skipIncoming = false) {
			try {
				_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, "Repatriating resources...");

				if (fromTelegram) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Telegram collect initated..");
				}

				_tbotInstance.UserData.fleets = await UpdateFleets();
				if (ReverseRepatriate.Aktiv(_tbotInstance))
					await ReverseRepatriate.VerarbeiteOffeneAsync(
						_tbotInstance, _calcService, _tbotOgameBridge,
						ErzeugeReverseHaken(), LiesRepatriateCargo());

				List<Celestial> tempCelestials = new();
				if (_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Length > 0) {
					tempCelestials = _calcService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target, _tbotInstance.UserData.celestials);
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, "Skipping autorepatriate: no custom destination");
					if (fromTelegram)
						await _tbotInstance.SendTelegramMessage("Collect aborted: no target configured. Set <code>Brain.AutoRepatriate.Target</code> in the settings (or via /editsettings).");
					return RepatriateCode.Failure;
				}
				try {
					var zielGefiltert = new List<Celestial>();
					foreach (var zielEintrag in _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target) {
						Celestials zielTyp = Enum.Parse<Celestials>((string) zielEintrag.Type, true);
						int zielG = (int) zielEintrag.Galaxy;
						int zielS = (int) zielEintrag.System;
						int zielP = (int) zielEintrag.Position;
						zielGefiltert.AddRange(tempCelestials.Where(c => c.Coordinate.Galaxy == zielG && c.Coordinate.System == zielS && c.Coordinate.Position == zielP && c.Coordinate.Type == zielTyp));
					}
					if (zielGefiltert.Count > 0)
						tempCelestials = zielGefiltert;
				} catch (Exception) {
				}
				_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate: target = {string.Join(", ", tempCelestials.Select(c => c.ToString()))}");

				ExtraLog.Write(_tbotInstance, "Repatriate", () => $"Repatriate diagnostics: fromTelegram={fromTelegram}, noLimit={noLimit}, typFilter={typFilter ?? "(none)"}, skipIncoming={skipIncoming}, reverseActive={ReverseRepatriate.Aktiv(_tbotInstance)}, targetsConfigured={_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target?.Length ?? 0}, resolvedTargets={tempCelestials.Count}, minResources={(long)_tbotInstance.InstanceSettings.Brain.AutoRepatriate.MinimumResources:N0}.");

				if (tempCelestials.Count() > 0) {
					long TotalMet = 0;
					long TotalCri = 0;
					long TotalDeut = 0;
					DateTime rpStart = DateTime.UtcNow;
					int rpGesendet = 0;
					int rpFehlgeschlagen = 0;
					bool samePosition = (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.TargetAssociateMoon;
					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					List<Celestial> celestialsToExclude = _calcService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialList = _tbotInstance.UserData.celestials.ToList();
					
					celestialList = (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.RandomOrder ? celestialList.Shuffle().ToList() : celestialList.ToList();
					
					foreach (Celestial celestial in celestialList) {
						List<Celestial> closestCelestials = tempCelestials
							.OrderBy(c => _calcService.CalcDistance(c.Coordinate, celestial.Coordinate, _tbotInstance.UserData.serverData)).ToList();
						Coordinate destinationCoordinate = new();
						if (samePosition) {
							if (celestial.Coordinate.Type == Celestials.Planet) {
								if (!_calcService.IsThereMoonHere(_tbotInstance.UserData.celestials, celestial)) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {celestial.ToString()}: There is no moon.");
									continue;
								}
								destinationCoordinate = new(
									(int) celestial.Coordinate.Galaxy,
									(int) celestial.Coordinate.System,
									(int) celestial.Coordinate.Position,
									Celestials.Moon
								);
							} else {
								destinationCoordinate = closestCelestials.First().Coordinate;
							}
						} else {
							destinationCoordinate = closestCelestials.First().Coordinate;
						}
						if (celestialsToExclude.Has(celestial)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						if (celestial.Coordinate.IsSame(destinationCoordinate)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {celestial.ToString()}: celestial is the target.");
							continue;
						}
						if (typFilter != null && ((typFilter == "moon") != (celestial.Coordinate.Type == Celestials.Moon))) {
							continue;
						}

						if (celestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ExcludeMoons) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {celestial.ToString()}: celestial is a moon.");
							continue;
						}

						if (ReverseRepatriate.Aktiv(_tbotInstance) && ReverseRepatriate.HatOffene(_tbotInstance, celestial)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {celestial.ToString()}: reverse pickup already running.");
							continue;
						}

						var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);

						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.SkipIfIncomingTransport && _calcService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets) && (!fromTelegram)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}
						if (fromTelegram && skipIncoming && _calcService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {tempCelestial.ToString()}: there is a transport incoming (skip).");
							continue;
						}

						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);

						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}
						Resources payload = tempCelestial.Resources;

						long applicableDeutToLeave = 0;
						if ((long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave > 0) {
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.OnlyOnMoons) {
								if (tempCelestial.Coordinate.Type == Celestials.Moon) {
									applicableDeutToLeave = (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave;
									payload = payload.Difference(new(0, 0, applicableDeutToLeave));
								}
							} else {
								applicableDeutToLeave = (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave;
								payload = payload.Difference(new(0, 0, applicableDeutToLeave));
							}
						}

						try {
							Resources freierAnteil = LedgerTBot.FreeForExternal(_tbotInstance, tempCelestial, out string gehaltenText);
							if (freierAnteil != null) {
								payload = new Resources(
									Math.Min(payload.Metal, freierAnteil.Metal),
									Math.Min(payload.Crystal, freierAnteil.Crystal),
									Math.Min(payload.Deuterium, freierAnteil.Deuterium));
								payload.Food = 0;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate-Ledger {tempCelestial.ToString()}: {gehaltenText}, abtransportierbar {payload.TotalResources:N0}.");
							}
						} catch (Exception ledgerEx) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Repatriate ledger: free share could not be determined ({ledgerEx.Message}) - live stock applies.");
						}

						ExtraLog.Write(_tbotInstance, "Repatriate", () => $"Repatriate candidate {tempCelestial}: stock M={tempCelestial.Resources.Metal:N0}/C={tempCelestial.Resources.Crystal:N0}/D={tempCelestial.Resources.Deuterium:N0}; payload after LeaveDeut/Ledger M={payload.Metal:N0}/C={payload.Crystal:N0}/D={payload.Deuterium:N0}; total={payload.TotalResources:N0}; min={(long)_tbotInstance.InstanceSettings.Brain.AutoRepatriate.MinimumResources:N0}; cargoShip={preferredShip}; availableCargoShips={tempCelestial.Ships.GetAmount(preferredShip):N0}.");
						if ((!noLimit && payload.TotalResources < (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.MinimumResources) || payload.IsEmpty()) {
							long bestandGesamt = tempCelestial.Resources.Metal + tempCelestial.Resources.Crystal + tempCelestial.Resources.Deuterium;
							string grund = bestandGesamt <= 0 ? "celestial is empty" : (payload.TotalResources < bestandGesamt ? $"stock {bestandGesamt:N0}, of that {bestandGesamt - payload.TotalResources:N0} reserved (ledger) or left behind (LeaveDeut)" : $"stock {bestandGesamt:N0}");
							_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Skipping {tempCelestial.ToString()}: nothing to send - free {payload.TotalResources:N0} < minimum {(long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.MinimumResources:N0} ({grund})");
							continue;
						}

						if (ReverseRepatriate.Aktiv(_tbotInstance)) {
							ReverseStatus reverseStatus = await ReverseRepatriate.StarteAbholungAsync(_tbotInstance, _calcService, _tbotOgameBridge, ErzeugeReverseHaken(), tempCelestial, destinationCoordinate, payload, preferredShip);
							if (reverseStatus == ReverseStatus.Gesendet) {
								rpGesendet++;
								newCelestials.Remove(celestial);
								newCelestials.Add(tempCelestial);
								continue;
							}
								if (reverseStatus == ReverseStatus.Wartet) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate: reverse pickup for {tempCelestial} is waiting; delaying cycle.");
									return RepatriateCode.Delay;
								}
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoRepatriate, $"Repatriate: reverse pickup for {tempCelestial} returned {reverseStatus}; trying classic transport path.");
						}

						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
						float cargoBonus = tempCelestial.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

						_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate diagnosis {tempCelestial.ToString()}: payload {payload.TotalResources:N0}, needed {idealShips:N0} {preferredShip}, available {tempCelestial.Ships.GetAmount(preferredShip):N0}");

						Ships ships = new();
						if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
							if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
								ships.Add(preferredShip, idealShips);
							} else {
								ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
							}
							payload = _calcService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

							if (payload.TotalResources > 0) {
								try {
									int maxTransportSlots = 0;
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain, "Transports") && SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.Transports, "MaxSlots"))
										maxTransportSlots = (int) _tbotInstance.InstanceSettings.Brain.Transports.MaxSlots;
									int reserveSlots = 0;
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoRepatriate, "ReserveTransportSlots"))
										reserveSlots = (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ReserveTransportSlots;
									if (maxTransportSlots > 0) {
										int laufendeTransporte = _tbotInstance.UserData.fleets.ToList().Count(f => f.Mission == Missions.Transport);
										if (laufendeTransporte >= maxTransportSlots - reserveSlots) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate: transport limit reached ({laufendeTransporte}/{maxTransportSlots}, reserved {reserveSlots}) while sending {tempCelestial.ToString()} - cycle is delayed.");
											return RepatriateCode.Delay;
										}
									}
								} catch (Exception slotEx) {
									_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Repatriate: transport limit could not be checked ({slotEx.Message}) - only the slot check in SendFleet applies.");
								}
								ExtraLog.Write(_tbotInstance, "Repatriate", () => $"Repatriate send attempt: origin={tempCelestial}, destination={destinationCoordinate}, mission={Missions.Transport}, ships={ships}, cargo M={payload.Metal:N0}/C={payload.Crystal:N0}/D={payload.Deuterium:N0}, deutToLeave={applicableDeutToLeave:N0}.");
								long deutToLeaveEff = applicableDeutToLeave;
								try {
									if (applicableDeutToLeave > 0) {
										long spritBedarf = _calcService.CalcFleetPrediction(tempCelestial, destinationCoordinate, ships, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Fuel;
										long moeglich = Math.Max(0L, tempCelestial.Resources.Deuterium - spritBedarf);
										if (moeglich < applicableDeutToLeave) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate: deuterium reserve {applicableDeutToLeave:N0} on {tempCelestial.ToString()} cannot be kept in full (stock {tempCelestial.Resources.Deuterium:N0}, fuel {spritBedarf:N0}) - keeping {moeglich:N0}.");
											deutToLeaveEff = moeglich;
										}
									}
								} catch (Exception reserveEx) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoRepatriate, $"Repatriate: reserve check failed ({reserveEx.Message}), using the configured reserve.");
								}
								var fleetId = await SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload, deutToLeave: deutToLeaveEff, shrinkDeuteriumPayload: true);
								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									return RepatriateCode.Stop;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Repatriate: no free slots while sending {tempCelestial.ToString()} after {(DateTime.UtcNow - rpStart).TotalSeconds:F0} s ({rpGesendet} fleet(s) sent in this cycle) - cycle is delayed, remaining celestials are handled in the next run.");
									return RepatriateCode.Delay;
								}
								ExtraLog.Write(_tbotInstance, "Repatriate", () => $"Repatriate SendFleet result: origin={tempCelestial}, destination={destinationCoordinate}, resultCode={fleetId}.");
								if (fleetId > 0) {
									TotalMet += payload.Metal;
									TotalCri += payload.Crystal;
									TotalDeut += payload.Deuterium;
									rpGesendet++;
								} else {
									rpFehlgeschlagen++;
									_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Repatriate: sending {tempCelestial.ToString()} to {destinationCoordinate.ToString()} failed (code {fleetId}), deuterium there: {tempCelestial.Resources.Deuterium:N0} - the reason is in the preceding 'Unable to send fleet' line.");
								}
							}
						} else {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()} on this celestial (to send: {payload.TotalResources:N0}; Reverse is {(ReverseRepatriate.Aktiv(_tbotInstance) ? "on but could not start" : "off")})");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;
					if (fromTelegram) {
						int rpGesamt = celestialList.Count;
						int rpOhneVersand = Math.Max(0, rpGesamt - rpGesendet - rpFehlgeschlagen);
						string zusammenfassung = $"\n{rpGesamt} celestials, {rpGesendet} fleet(s) sent, {rpOhneVersand} without shipment" + (rpFehlgeschlagen > 0 ? $", {rpFehlgeschlagen} failed" : "") + $" ({(DateTime.UtcNow - rpStart).TotalSeconds:F0} s)";
						if ((TotalMet > 0) || (TotalCri > 0) || (TotalDeut > 0)) {
							await _tbotInstance.SendTelegramMessage($"Resources sent!:\n{TotalMet:N0} Metal\n{TotalCri:N0} Crystal\n{TotalDeut:N0} Deuterium" + zusammenfassung);
						} else {
							await _tbotInstance.SendTelegramMessage("No resources sent" + zusammenfassung);
						}
					}
					_tbotInstance.log(LogLevel.Information, LogSender.AutoRepatriate, $"Repatriate: cycle finished after {(DateTime.UtcNow - rpStart).TotalSeconds:F0} s, {rpGesendet} fleet(s) sent.");
					return RepatriateCode.Success;
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, "Skipping autorepatriate: unable to parse custom destination");
					if (fromTelegram)
						await _tbotInstance.SendTelegramMessage("Collect aborted: the configured target (Brain.AutoRepatriate.Target) matches none of your celestials.");
					return RepatriateCode.Failure;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Unable to complete repatriate: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoRepatriate, $"Stacktrace: {e.StackTrace}");
				if (fromTelegram)
					await _tbotInstance.SendTelegramMessage($"Collect failed: {e.Message}");
				return RepatriateCode.Failure;
			}
		}
	

}
}