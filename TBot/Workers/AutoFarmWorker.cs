using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using System.Threading;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Xml.Linq;
using System.Globalization;

namespace Tbot.Workers {
	public class AutoFarmWorker : WorkerBase {
		// PATCH 87, 88, 74, 83, 96: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 1
		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(60);

		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private readonly AutoFarmBlacklist _blacklist;
		private readonly AutoFarmSuccessfulTargets _successfulTargets;
		private readonly AutoFarmStateStore _stateStore;
		private bool _stateLoaded;
		// PATCH 73, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 2
		private readonly Dictionary<long, int> _nonActionableReportAttempts = new();

		// PATCH 96: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 3
		private bool _defendedHalfToggle;

		// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 4
		private long? _sequentialModeNextArrivalSeconds;
		// PATCH 102: Sende-Zeitpunkt der Sonde (UTC) - siehe Kommentar bei der Reschedule-Stelle
		// ganz am Ende von Execute() (dort wird die seit dem Versand bereits verstrichene Zeit von
		// der Flugzeit abgezogen, statt den vollen Wert ab "jetzt" nochmal draufzuschlagen).
		private DateTime? _sequentialModeProbeSentAtUtc;

		// PATCH 101: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 5
		// PATCH 107: nur noch Startwert vor dem Laden aus der DB (siehe Konstruktor) - der
		// eigentliche persistente Wert liegt jetzt in AutoFarmStateStore.
		private volatile bool _mainScanCompletedSinceStart = false;

		// PATCH 82: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 6
		private readonly ConcurrentQueue<Coordinate> _activeScanDiscoveries = new();
		private Task _activeScanLoopTask;
		private readonly object _activeScanStartLock = new object();

		// PATCH 110: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 130
		// Zeitpunkt (UTC) des letzten Abarbeitens der ActiveScan-Entdeckungs-Queue ("Block 11") -
		// bewusst reiner In-Memory-Zustand (kein Persistieren noetig): schlimmstenfalls wird nach
		// einem Neustart einmal sofort abgearbeitet, statt auf AutoFarm.Block11MaxAgeHours zu warten -
		// das ist sicher (kein doppeltes Abarbeiten moeglich, nur potenziell frueher als sonst).
		private DateTime _lastActiveScanDrainUtc = DateTime.MinValue;

		// PATCH 126: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 146 - zeitversetzter Recycler-Versand.
		// Verzoegerte Recycler-Starts werden in einer kleinen JSON-Datei neben der AutoFarm-DB gespeichert
		// (gleiches Muster wie die Fleetsave-Recalls in FleetScheduler), damit ein Neustart sie nicht
		// verliert. Jeder Auftrag bekommt einen eigenen One-Shot-Timer, der nie per Change() umgestellt
		// wird (bekanntes Timer.Change()-Problem), sondern bei Bedarf neu erzeugt.
		private class PendingRecyclerEntry {
			public string Id { get; set; }
			public int OriginGalaxy { get; set; }
			public int OriginSystem { get; set; }
			public int OriginPosition { get; set; }
			public int OriginType { get; set; }
			public int TargetGalaxy { get; set; }
			public int TargetSystem { get; set; }
			public int TargetPosition { get; set; }
			public long Recyclers { get; set; }
			public decimal Speed { get; set; }
			public DateTime SendAtUtc { get; set; }
			public DateTime GiveUpAtUtc { get; set; }
			// PATCH 128: Fehlversuche NACH dem Kampf (begrenzt)
			public int PostBattleAttempts { get; set; }
			public int Attempts { get; set; }
		}
		private readonly object _pendingRecyclersLock = new();
		private readonly ConcurrentDictionary<string, Timer> _recyclerTimers = new();
		private bool _pendingRecyclersReconciled;

		// PATCH 127: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 147 - Flugzeit-Kalibrierung.
		// Die berechneten Flugzeiten (CalcFleetPrediction) weichen in der Praxis ab (Recycler kamen
		// 2-3 min zu frueh). Nach jedem Versand wird die ECHTE Restflugzeit aus der Flottenliste gelesen
		// und als Korrekturfaktor (gleitender Mittelwert) pro Flottenart gemerkt und gespeichert.
		private class FlightCalibration {
			public double RecyclerFactor { get; set; } = 1.0;
			public int RecyclerSamples { get; set; }
			public double AttackFactor { get; set; } = 1.0;
			public int AttackSamples { get; set; }
		}
		private readonly object _flightCalibrationLock = new();
		private FlightCalibration _flightCalibration;

		private string GetFlightCalibrationFilePath() {
			string playerTag = _tbotInstance?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universeTag = _tbotInstance?.UserData?.serverData?.Name ?? "unknown";
			string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataDir);
			return Path.Combine(dataDir, $"flight_calibration_{playerTag}-{universeTag}.json");
		}

		private FlightCalibration GetFlightCalibration() {
			lock (_flightCalibrationLock) {
				if (_flightCalibration != null) return _flightCalibration;
				try {
					string path = GetFlightCalibrationFilePath();
					_flightCalibration = File.Exists(path)
						? (JsonConvert.DeserializeObject<FlightCalibration>(File.ReadAllText(path)) ?? new FlightCalibration())
						: new FlightCalibration();
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.AutoFarm, $"Flight time calibration could not be loaded: {e.Message}");
					_flightCalibration = new FlightCalibration();
				}
				return _flightCalibration;
			}
		}

		// isRecycler: true = Recycler (Abbau), false = Hauptflotte (Angriff)
		private void RecordFlightSample(bool isRecycler, long predictedSec, long actualSec) {
			if (predictedSec <= 0 || actualSec <= 0) return;
			double ratio = Math.Clamp((double) actualSec / predictedSec, 0.3, 3.0);
			lock (_flightCalibrationLock) {
				var cal = GetFlightCalibration();
				double oldFactor = isRecycler ? cal.RecyclerFactor : cal.AttackFactor;
				int samples = isRecycler ? cal.RecyclerSamples : cal.AttackSamples;
				// erste Messung direkt uebernehmen, danach gleitend (30% neue Messung)
				double newFactor = samples == 0 ? ratio : oldFactor * 0.7 + ratio * 0.3;
				if (isRecycler) { cal.RecyclerFactor = newFactor; cal.RecyclerSamples = samples + 1; }
				else { cal.AttackFactor = newFactor; cal.AttackSamples = samples + 1; }
				try {
					File.WriteAllText(GetFlightCalibrationFilePath(), JsonConvert.SerializeObject(cal));
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.AutoFarm, $"Flight time calibration could not be saved: {e.Message}");
				}
				_tbotInstance?.log(LogLevel.Debug, LogSender.AutoFarm, $"Flugzeit-Kalibrierung {(isRecycler ? "Recycler" : "Hauptflotte")}: calculated {predictedSec}s, actual {actualSec}s (factor {ratio:F3}) -> correction {oldFactor:F3} -> {newFactor:F3} ({samples + 1} measurements).");
			}
		}

		// PATCH 141: see AutoFarmWorker.PATCHNOTES.md, section 159 - log spam reduction.
		// Status messages that would otherwise repeat every cycle are written once per key and then only again
		// after AutoFarm.RepeatedLogIntervalMinutes (default 360; 0 = log every time as before).
		private readonly Dictionary<string, DateTime> _throttledLogUtc = new();

		private void LogThrottled(string key, LogLevel level, string message) {
			int intervalMinutes = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "RepeatedLogIntervalMinutes", 360);
			var nowUtc = DateTime.UtcNow;
			if (intervalMinutes > 0) {
				lock (_throttledLogUtc) {
					if (_throttledLogUtc.TryGetValue(key, out var lastUtc) && (nowUtc - lastUtc).TotalMinutes < intervalMinutes)
						return;
					_throttledLogUtc[key] = nowUtc;
					// drop stale keys so the dictionary does not grow forever
					if (_throttledLogUtc.Count > 2000) {
						foreach (var stale in _throttledLogUtc.Where(kv => (nowUtc - kv.Value).TotalMinutes >= intervalMinutes).Select(kv => kv.Key).ToList())
							_throttledLogUtc.Remove(stale);
					}
				}
			}
			_tbotInstance.log(level, LogSender.AutoFarm, message);
		}

		// PATCH 140: see AutoFarmWorker.PATCHNOTES.md, section 158 - loss tracking "sent vs. returned".
		// Result per attack: returned (return flight seen, exact losses), destroyed (vanished at arrival, no
		// return flight, bot was active) or unknown (bot inactive during the relevant window - NOT counted as a loss).
		private static readonly Buildables[] LossShipTypes = {
			Buildables.LightFighter, Buildables.HeavyFighter, Buildables.Cruiser, Buildables.Battleship,
			Buildables.Battlecruiser, Buildables.Bomber, Buildables.Destroyer, Buildables.Deathstar,
			Buildables.SmallCargo, Buildables.LargeCargo, Buildables.ColonyShip, Buildables.Recycler,
			Buildables.EspionageProbe, Buildables.Reaper, Buildables.Pathfinder,
		};
		private DateTime _lastLossSummaryLocalDate = DateTime.MinValue;

		private static Dictionary<string, long> ShipsToLossDict(Ships ships) {
			var dict = new Dictionary<string, long>();
			if (ships == null)
				return dict;
			foreach (var type in LossShipTypes) {
				long amount = ships.GetAmount(type);
				if (amount > 0)
					dict[type.ToString()] = amount;
			}
			return dict;
		}

		private void RecordFleetForLossTracking(int fleetId, Celestial origin, Coordinate target, Ships ships, decimal speed, Resources plannedLoot) {
			try {
				long outboundSec = (long) _calculationService.CalcFleetPrediction(origin.Coordinate, target, ships, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
				var nowUtc = DateTime.UtcNow;
				if (!_stateStore.RecordFleetDispatch(fleetId, target, origin.Coordinate, nowUtc, nowUtc.AddSeconds(outboundSec), nowUtc.AddSeconds(2 * outboundSec), ShipsToLossDict(ships), plannedLoot))
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Loss tracking: fleet {fleetId} to {target} could not be stored.");
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Loss tracking: fleet {fleetId} not recorded ({e.Message}).");
			}
		}

		private Resources LossValueOf(Dictionary<string, long> lost) {
			long m = 0, c = 0, d = 0;
			foreach (var kv in lost) {
				if (kv.Value <= 0 || !Enum.TryParse<Buildables>(kv.Key, out var type))
					continue;
				var price = _calculationService.CalcPrice(type, 1);
				m += price.Metal * kv.Value;
				c += price.Crystal * kv.Value;
				d += price.Deuterium * kv.Value;
			}
			return new Resources(m, c, d);
		}

		private static Dictionary<string, long> LostShips(AutoFarmFleetLossRecord rec) {
			var lost = new Dictionary<string, long>();
			if (rec.Status == "destroyed") {
				foreach (var kv in rec.Sent) lost[kv.Key] = kv.Value;
			} else if (rec.Status == "returned" && rec.Returned != null) {
				foreach (var kv in rec.Sent) {
					long diff = kv.Value - rec.Returned.GetValueOrDefault(kv.Key);
					if (diff > 0) lost[kv.Key] = diff;
				}
			}
			return lost;
		}

		private static string FormatLostShips(Dictionary<string, long> lost) {
			var parts = lost.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value:N0}x{kv.Key}").ToList();
			return parts.Count == 0 ? "none" : string.Join(", ", parts);
		}

		private void TrackFleetLosses() {
			try {
				var fleets = _tbotInstance.UserData.fleets;
				if (fleets == null)
					return;
				var nowUtc = DateTime.UtcNow;
				var pending = _stateStore.LoadFleetLosses("pending");
				var pendingIds = new HashSet<int>(pending.Select(p => p.FleetId));
				var usedFallback = new HashSet<int>();

				foreach (var rec in pending) {
					var fleet = fleets.FirstOrDefault(f => f.ID == rec.FleetId);
					if (fleet == null) {
						// Fallback in case the return flight carries a different ID: same route, attack mission, returning
						fleet = fleets.FirstOrDefault(f => f.ReturnFlight && f.Mission == Missions.Attack
							&& !pendingIds.Contains(f.ID) && !usedFallback.Contains(f.ID)
							&& f.Origin != null && f.Destination != null
							&& ((f.Destination.IsSame(rec.Target) && f.Origin.IsSame(rec.Origin))
								|| (f.Origin.IsSame(rec.Target) && f.Destination.IsSame(rec.Origin))));
						if (fleet != null)
							usedFallback.Add(fleet.ID);
					}

					if (fleet != null) {
						if (fleet.ReturnFlight) {
							var returned = ShipsToLossDict(fleet.Ships);
							if (returned.Count == 0) {
								// Ships not readable - better "unknown" than a false total loss
								_stateStore.ResolveFleetLoss(rec.FleetId, "unknown", null, new Resources(), nowUtc);
								continue;
							}
							var resolved = new AutoFarmFleetLossRecord { Sent = rec.Sent, Returned = returned, Status = "returned" };
							var lost = LostShips(resolved);
							var value = LossValueOf(lost);
							_stateStore.ResolveFleetLoss(rec.FleetId, "returned", returned, value, nowUtc);
							if (lost.Count > 0)
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attack losses {rec.Target}: {FormatLostShips(lost)} (value {value.TotalResources:N0}).");
							else
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Attack {rec.Target}: fleet returning without losses.");
						} else {
							long arriveIn = Convert.ToInt64((object) fleet.ArriveIn ?? 0);
							DateTime? arrival = arriveIn > 0 ? nowUtc.AddSeconds(arriveIn) : null;
							DateTime? ret = arrival.HasValue ? arrival.Value + (arrival.Value - rec.DispatchedAtUtc) : null;
							_stateStore.UpdateFleetLossObservation(rec.FleetId, 0, arrival, ret);
						}
						continue;
					}

					// Fleet not (or no longer) in the list - decide only on the second miss (guards against empty lists)
					int miss = rec.MissCount + 1;
					if (miss < 2) {
						_stateStore.UpdateFleetLossObservation(rec.FleetId, miss, null, null);
						continue;
					}
					double outboundSec = Math.Max(0, (rec.ExpectedArrivalUtc - rec.DispatchedAtUtc).TotalSeconds);
					double marginSec = Math.Max(120, outboundSec * 0.15);
					if (nowUtc >= rec.ExpectedArrivalUtc.AddSeconds(-marginSec) && nowUtc < rec.ExpectedReturnUtc.AddSeconds(-marginSec)) {
						// vanished after arrival while the return flight should still be visible -> destroyed
						var destroyed = new AutoFarmFleetLossRecord { Sent = rec.Sent, Status = "destroyed" };
						var lost = LostShips(destroyed);
						var value = LossValueOf(lost);
						_stateStore.ResolveFleetLoss(rec.FleetId, "destroyed", null, value, nowUtc);
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Attack fleet to {rec.Target} completely destroyed: {FormatLostShips(lost)} (value {value.TotalResources:N0}).");
					} else {
						_stateStore.ResolveFleetLoss(rec.FleetId, "unknown", null, new Resources(), nowUtc);
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Loss tracking: return of the fleet to {rec.Target} not observed (bot inactive?) - marked 'unknown', not counted as a loss.");
					}
				}

				// once per calendar day: summary
				var today = DateTime.Now.Date;
				if (_lastLossSummaryLocalDate != today) {
					_lastLossSummaryLocalDate = today;
					LogLossSummary(nowUtc);
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Loss tracking failed: {e.Message}");
			}
		}

		private void LogLossSummary(DateTime nowUtc) {
			foreach (var (label, days) in new[] { ("24h", 1), ("7 days", 7), ("30 days", 30) }) {
				var records = _stateStore.LoadFleetLosses(null, nowUtc.AddDays(-days));
				if (records.Count == 0) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"AutoFarm losses {label}: no tracked attacks.");
					continue;
				}
				var total = new Dictionary<string, long>();
				long lossValue = 0, loot = 0;
				int counted = 0, destroyedCount = 0;
				foreach (var rec in records.Where(r => r.Status == "returned" || r.Status == "destroyed")) {
					counted++;
					if (rec.Status == "destroyed") destroyedCount++;
					foreach (var kv in LostShips(rec))
						total[kv.Key] = total.GetValueOrDefault(kv.Key) + kv.Value;
					lossValue += rec.LossValue.TotalResources;
					loot += rec.PlannedLoot.TotalResources;
				}
				int unknown = records.Count(r => r.Status == "unknown");
				int open = records.Count(r => r.Status == "pending");
				string ratio = loot > 0 ? $"{lossValue * 100.0 / loot:F0}% of planned loot {loot:N0}" : "no loot";
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
					$"AutoFarm losses {label}: {counted} attacks evaluated ({destroyedCount} destroyed), lost {FormatLostShips(total)} = {lossValue:N0} ({ratio}) | unknown {unknown}, pending {open}.");
			}
		}		// Echte Restflugzeit (Sekunden) einer gerade gesendeten Flotte aus der Flottenliste
		private async Task<long?> GetActualArriveInAsync(int fleetId) {
			try {
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				var fleet = _tbotInstance.UserData.fleets?.FirstOrDefault(f => f.ID == fleetId);
				if (fleet == null) return null;
				long arriveIn = Convert.ToInt64((object) fleet.ArriveIn ?? 0);
				return arriveIn > 0 ? arriveIn : null;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Actual flight time for fleet {fleetId} not readable: {e.Message}");
				return null;
			}
		}

		private long PredictRecyclerSeconds(Celestial origin, Ships recyclers, Coordinate debris, decimal speed) {
			try {
				return (long) _calculationService.CalcFleetPrediction(origin.Coordinate, debris, recyclers, Missions.Harvest, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
			} catch {
				return 0;
			}
		}

		private string GetPendingRecyclersFilePath() {
			string playerTag = _tbotInstance?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universeTag = _tbotInstance?.UserData?.serverData?.Name ?? "unknown";
			string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataDir);
			return Path.Combine(dataDir, $"pending_recyclers_{playerTag}-{universeTag}.json");
		}

		private List<PendingRecyclerEntry> LoadPendingRecyclers() {
			lock (_pendingRecyclersLock) {
				try {
					string path = GetPendingRecyclersFilePath();
					if (!File.Exists(path)) return new();
					return JsonConvert.DeserializeObject<List<PendingRecyclerEntry>>(File.ReadAllText(path)) ?? new();
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recyclers could not be loaded: {e.Message}");
					return new();
				}
			}
		}

		private void SavePendingRecyclers(List<PendingRecyclerEntry> entries) {
			lock (_pendingRecyclersLock) {
				try {
					File.WriteAllText(GetPendingRecyclersFilePath(), JsonConvert.SerializeObject(entries));
				} catch (Exception e) {
					_tbotInstance?.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recyclers could not be saved: {e.Message}");
				}
			}
		}

		private void UpsertPendingRecycler(PendingRecyclerEntry entry) {
			lock (_pendingRecyclersLock) {
				var entries = LoadPendingRecyclers();
				entries.RemoveAll(e => e.Id == entry.Id);
				entries.Add(entry);
				SavePendingRecyclers(entries);
			}
		}

		private void RemovePendingRecycler(string id) {
			lock (_pendingRecyclersLock) {
				var entries = LoadPendingRecyclers();
				if (entries.RemoveAll(e => e.Id == id) > 0)
					SavePendingRecyclers(entries);
			}
		}

		private void ArmRecyclerTimer(PendingRecyclerEntry entry) {
			long dueMs = (long) Math.Max(0, (entry.SendAtUtc - DateTime.UtcNow).TotalMilliseconds);
			if (_recyclerTimers.TryRemove(entry.Id, out var oldTimer)) {
				oldTimer.Dispose();
			}
			string id = entry.Id;
			var timer = new Timer(_ => { _ = FirePendingRecyclerAsync(id); }, null, dueMs, Timeout.Infinite);
			_recyclerTimers[id] = timer;
		}

		// PATCH 128: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 148 - Wiederholungs-Taktik.
		// GiveUpAtUtc liegt bei allen Aufrufern genau 30 min hinter der Angriffsankunft.
		// Schlaegt der Versand VOR dem Kampf fehl (typisch: Truemmerfeld existiert noch nicht), bringt
		// alle 30s erneut versuchen nichts - naechster Versuch erst 20s nach Kampfende. Danach alle 90s,
		// hoechstens RecyclerMaxRetriesAfterBattle Mal (Default 10).
		// PATCH 129: aus JSON zurueckgelesene decimals behalten ihre Skalierung ("10.0") - ogamed lehnt
		// das mit "invalid speed" ab. Vor jedem Versand auf die minimale Darstellung normalisieren.
		private static decimal NormalizeSpeed(decimal speed) {
			return decimal.Parse(speed.ToString("0.#####", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
		}

		// PATCH 133: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 151 - gleiche Pruefung wie in
		// FleetScheduler.SendFleet ("bed time has passed" / "would come back during sleep time"), aber
		// VOR Trigger-Sonde und Recycler, damit nicht erst Sonde + Recycler rausgehen und die
		// Hauptflotte dann abgelehnt wird.
		private async Task<bool> WouldConflictWithSleepAsync(long roundTripSec) {
			try {
				if (!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings, "SleepMode")
					|| !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.SleepMode, "Active")
					|| !(bool) _tbotInstance.InstanceSettings.SleepMode.Active) {
					return false;
				}
				if (!DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep)
					|| !DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp)) {
					return false;
				}
				DateTime time = await _tbotOgameBridge.GetDateTime();
				if (GeneralHelper.ShouldSleep(time, goToSleep, wakeUp)) {
					return true;
				}
				if (goToSleep >= wakeUp) wakeUp = wakeUp.AddDays(1);
				if (goToSleep < time) goToSleep = goToSleep.AddDays(1);
				if (wakeUp < time) wakeUp = wakeUp.AddDays(1);
				DateTime returnTime = time.AddSeconds(roundTripSec);
				return returnTime >= goToSleep && returnTime <= wakeUp;
			} catch {
				return false;
			}
		}

		// PATCH 133: geplante Recycler-Auftraege fuer ein Truemmerfeld verwerfen und bereits fliegende
		// Recycler zu diesem Feld zurueckrufen (Hauptflotte wurde doch nicht gesendet).
		private async Task CancelRecyclersForDebrisAsync(Coordinate debris) {
			try {
				lock (_pendingRecyclersLock) {
					var entries = LoadPendingRecyclers();
					foreach (var e in entries.Where(e => e.TargetGalaxy == debris.Galaxy && e.TargetSystem == debris.System && e.TargetPosition == debris.Position).ToList()) {
						if (_recyclerTimers.TryRemove(e.Id, out var t)) t.Dispose();
						entries.Remove(e);
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Scheduled recycler for {debris} discarded (main fleet not sent).");
					}
					SavePendingRecyclers(entries);
				}
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				var flying = _tbotInstance.UserData.fleets?
					.Where(f => f.Mission == Missions.Harvest && !f.ReturnFlight && f.Destination != null
						&& f.Destination.Galaxy == debris.Galaxy && f.Destination.System == debris.System && f.Destination.Position == debris.Position)
					.ToList() ?? new List<Fleet>();
				foreach (var f in flying) {
					await _ogameService.CancelFleet(f);
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Pre-sent recycler (fleet {f.ID}) to {debris} recalled (main fleet not sent).");
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Recyclers for {debris} could not be cancelled: {e.Message}");
			}
		}

		private DateTime? NextRecyclerRetryUtc(PendingRecyclerEntry entry) {
			DateTime attackArrivalUtc = entry.GiveUpAtUtc.AddMinutes(-30);
			DateTime now = DateTime.UtcNow;
			if (now < attackArrivalUtc.AddSeconds(-30)) {
				return attackArrivalUtc.AddSeconds(20);
			}
			int maxAfterBattle = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "RecyclerMaxRetriesAfterBattle", 10);
			if (entry.PostBattleAttempts >= maxAfterBattle) {
				return null;
			}
			entry.PostBattleAttempts++;
			return now.AddSeconds(90);
		}

		private void SchedulePendingRecycler(Celestial origin, Ships recyclers, Coordinate debris, decimal speed, DateTime sendAtUtc, DateTime giveUpAtUtc, int attempts = 0, int postBattleAttempts = 0) {
			var entry = new PendingRecyclerEntry {
				Id = Guid.NewGuid().ToString("N"),
				OriginGalaxy = origin.Coordinate.Galaxy,
				OriginSystem = origin.Coordinate.System,
				OriginPosition = origin.Coordinate.Position,
				OriginType = (int) origin.Coordinate.Type,
				TargetGalaxy = debris.Galaxy,
				TargetSystem = debris.System,
				TargetPosition = debris.Position,
				Recyclers = recyclers.GetAmount(Buildables.Recycler),
				Speed = NormalizeSpeed(speed),
				SendAtUtc = sendAtUtc,
				GiveUpAtUtc = giveUpAtUtc,
				Attempts = attempts,
				PostBattleAttempts = postBattleAttempts,
			};
			UpsertPendingRecycler(entry);
			ArmRecyclerTimer(entry);
		}

		// Sofort senden; schlaegt das fehl (Truemmerfeld noch nicht da, kein Slot, ...), wird ein
		// persistierter Wiederholungsauftrag angelegt (Taktik siehe NextRecyclerRetryUtc, PATCH 128).
		private async Task<long?> SendRecyclerOrScheduleRetryAsync(Celestial origin, Ships recyclers, Coordinate debris, decimal speed, DateTime giveUpAtUtc) {
			int fleetId;
			try {
				fleetId = await _fleetScheduler.SendFleet(origin, recyclers, debris, Missions.Harvest, NormalizeSpeed(speed));
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Recycler dispatch to {debris} error ({e.Message}).");
				fleetId = (int) SendFleetCode.GenericError;
			}
			if (fleetId > (int) SendFleetCode.GenericError) {
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Recycler ({recyclers.GetAmount(Buildables.Recycler)}x) sent to {debris}, {speed * 10}% speed.");
				// PATCH 127: echte Flugzeit messen -> Kalibrierung; Rueckgabe = echte Restflugzeit
				long predictedRec = PredictRecyclerSeconds(origin, recyclers, debris, speed);
				long? actualRec = await GetActualArriveInAsync(fleetId);
				if (actualRec.HasValue) RecordFlightSample(true, predictedRec, actualRec.Value);
				return actualRec;
			}
			var probeEntry = new PendingRecyclerEntry { GiveUpAtUtc = giveUpAtUtc };
			DateTime? nextTry = NextRecyclerRetryUtc(probeEntry);
			if (!nextTry.HasValue) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Recycler dispatch to {debris} failed (code {fleetId}) - giving up.");
				return null;
			}
			_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Recycler dispatch to {debris} failed (code {fleetId}, see FleetScheduler warning for the reason) - next attempt {nextTry.Value.ToLocalTime():HH:mm:ss}.");
			SchedulePendingRecycler(origin, recyclers, debris, speed, nextTry.Value, giveUpAtUtc, 1, probeEntry.PostBattleAttempts);
			return null;
		}

		private async Task FirePendingRecyclerAsync(string id) {
			if (_recyclerTimers.TryRemove(id, out var firedTimer)) {
				firedTimer.Dispose();
			}
			var entry = LoadPendingRecyclers().FirstOrDefault(e => e.Id == id);
			if (entry == null) {
				return;
			}
			string debrisLabel = $"{entry.TargetGalaxy}:{entry.TargetSystem}:{entry.TargetPosition}";
			try {
				if (DateTime.UtcNow > entry.GiveUpAtUtc) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recycler for debris field {debrisLabel} abandoned (time window expired, {entry.Attempts} attempts).");
					RemovePendingRecycler(id);
					return;
				}
				var originCoordinate = new Coordinate(entry.OriginGalaxy, entry.OriginSystem, entry.OriginPosition, (Celestials) entry.OriginType);
				Celestial origin = _tbotInstance.UserData.celestials?.FirstOrDefault(c => c.HasCoords(originCoordinate));
				if (origin == null) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recycler for {debrisLabel}: origin celestial {originCoordinate} not found - discarded.");
					RemovePendingRecycler(id);
					return;
				}
				origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
				origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
				long toSend = Math.Min(entry.Recyclers, origin.Ships.GetAmount(Buildables.Recycler));
				if (toSend > 0) {
					var debris = new Coordinate(entry.TargetGalaxy, entry.TargetSystem, entry.TargetPosition, Celestials.Debris);
					int fleetId = await _fleetScheduler.SendFleet(origin, new Ships().Add(Buildables.Recycler, toSend), debris, Missions.Harvest, NormalizeSpeed(entry.Speed));
					if (fleetId > (int) SendFleetCode.GenericError) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Scheduled recycler ({toSend}x) sent to {debris}, {entry.Speed * 10}% speed{(entry.Attempts > 0 ? $" (attempt {entry.Attempts + 1})" : "")}.");
						RemovePendingRecycler(id);
						// PATCH 127: echte Flugzeit messen -> Kalibrierung
						var sentRecyclers = new Ships().Add(Buildables.Recycler, toSend);
						long predictedRec = PredictRecyclerSeconds(origin, sentRecyclers, debris, entry.Speed);
						long? actualRec = await GetActualArriveInAsync(fleetId);
						if (actualRec.HasValue) RecordFlightSample(true, predictedRec, actualRec.Value);
						return;
					}
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Scheduled recycler to {debris} failed (code {fleetId}).");
				} else {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Scheduled recycler for {debrisLabel}: no recyclers on {origin} .");
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recycler for {debrisLabel}: error ({e.Message}).");
			}
			entry.Attempts++;
			DateTime? nextRetry = NextRecyclerRetryUtc(entry);
			if (!nextRetry.HasValue || nextRetry.Value > entry.GiveUpAtUtc) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recycler for debris field {debrisLabel} abandoned ({entry.Attempts} attempts) - see the FleetScheduler warnings for the reason.");
				RemovePendingRecycler(id);
				return;
			}
			_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Scheduled recycler for {debrisLabel}: next attempt {nextRetry.Value.ToLocalTime():HH:mm:ss} (attempt {entry.Attempts + 1}).");
			entry.SendAtUtc = nextRetry.Value;
			UpsertPendingRecycler(entry);
			ArmRecyclerTimer(entry);
		}

		// Einmal beim ersten Execute() nach dem Start: gespeicherte Auftraege neu bewaffnen, ueberfaellige
		// sofort ausfuehren, abgelaufene verwerfen.
		private void ReconcilePendingRecyclers() {
			var entries = LoadPendingRecyclers();
			if (entries.Count == 0) {
				return;
			}
			_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{entries.Count} scheduled recycler start(s) from before the restart found - being restored.");
			foreach (var entry in entries) {
				if (DateTime.UtcNow > entry.GiveUpAtUtc) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Scheduled recycler for {entry.TargetGalaxy}:{entry.TargetSystem}:{entry.TargetPosition} discarded (time window expired during the restart).");
					RemovePendingRecycler(entry.Id);
					continue;
				}
				ArmRecyclerTimer(entry);
			}
		}

		public AutoFarmWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
			string blacklistPath = $"autofarm_blacklist_{_tbotInstance.InstanceAlias}.json";
			_blacklist = new AutoFarmBlacklist(blacklistPath);
			string successfulPath = $"autofarm_successful_{_tbotInstance.InstanceAlias}.json";
			_successfulTargets = new AutoFarmSuccessfulTargets(successfulPath);
			_stateStore = new AutoFarmStateStore($"autofarm_{_tbotInstance.InstanceAlias}.db");
			// PATCH 107: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 127
			// _mainScanCompletedSinceStart war bisher (PATCH 101) reiner In-Memory-Zustand - jeder
			// Neustart hat den Hintergrund-Scanner (ActiveScan) dadurch erneut fuer einen ganzen
			// Durchlauf pausiert, obwohl schon laengst mindestens einmal komplett durchgescannt wurde.
			// Jetzt beim Start aus der DB geladen (Default false bei einer frischen/neuen DB - dann
			// bleibt das bisherige, sichere Verhalten: erst nach einem vollen Durchlauf freischalten).
			_mainScanCompletedSinceStart = _stateStore.LoadMainScanCompleted();
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoFarm.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoFarm";
		}
		public override Feature GetFeature() {
			return Feature.AutoFarm;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoFarm;
		}

		// PATCH 82: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 7
		private void EnsureActiveScanLoopStarted() {
			bool enabled = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "ActiveScan") &&
				SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.ActiveScan, "Active") &&
				(bool) _tbotInstance.InstanceSettings.AutoFarm.ActiveScan.Active;
			if (!enabled)
				return;

			// PATCH 101: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 8
			if (!_mainScanCompletedSinceStart)
				return;

			lock (_activeScanStartLock) {
				if (_activeScanLoopTask != null && !_activeScanLoopTask.IsCompleted)
					return;

				_activeScanLoopTask = Task.Run(() => ActiveScanLoop(_ct), _ct);
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
					"[ActiveScan] Continuous active scanner started (independent of the normal AutoFarm cycle).");
			}
		}

		// PATCH 82: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 9
		private async Task ActiveScanLoop(CancellationToken ct) {
			var rnd = new Random();
			while (!ct.IsCancellationRequested) {
				try {
					// PATCH 105: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 125
					var scanRanges = GetEffectiveScanRanges();
					// PATCH 122: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 142 - dieselbe
					// Umstellung wie im Haupt-Scan-Zyklus: konfigurierte Reihenfolge statt
					// Galaxy/StartSystem-Sortierung, fuer konsistentes Verhalten zwischen
					// ActiveScan und dem regulaeren AutoFarm-Zyklus.
					var orderedRanges = scanRanges.ToList();

					foreach (var range in orderedRanges) {
						int galaxy = (int) range.Galaxy;
						int startSystem = (int) range.StartSystem;
						int endSystem = (int) range.EndSystem;

						for (int system = startSystem; system <= endSystem; system++) {
							if (ct.IsCancellationRequested)
								return;

							try {
								if (!ShouldExcludeSystem(galaxy, system)) {
									GalaxyInfo info = await _ogameService.GetGalaxyInfo(galaxy, system);
									var planets = info?.Planets?.Where(p => p != null).ToList() ?? new List<Planet>();
									var eligible = AutoFarmSystemPolicy.GetEligibleTargets(planets);

									foreach (var celestial in eligible) {
										bool alreadyKnown = _tbotInstance.UserData.farmTargets != null &&
											_tbotInstance.UserData.farmTargets.Any(t => t.Celestial.HasCoords(celestial.Coordinate));
										if (alreadyKnown)
											continue;
										if (ShouldExcludePlanet(celestial))
											continue;

										_activeScanDiscoveries.Enqueue(celestial.Coordinate);
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
											$"[ActiveScan] New inactive target discovered: {celestial.Coordinate} - will be handled first in the next AutoFarm cycle.");
									}
								}
							} catch (Exception ex) {
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
									$"[ActiveScan] Error at system {galaxy}:{system}: {ex.Message}");
							}

							int minSec = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.ActiveScan, "IntervalMinSec", 4);
							int maxSec = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.ActiveScan, "IntervalMaxSec", 7);
							if (maxSec < minSec) maxSec = minSec;
							int delayMs = rnd.Next(Math.Max(1, minSec) * 1000, maxSec * 1000 + 1);

							try {
								await Task.Delay(delayMs, ct);
							} catch (TaskCanceledException) {
								return;
							}
						}
					}

					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "[ActiveScan] Voller Scan-Durchlauf abgeschlossen.");
				} catch (Exception ex) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"[ActiveScan] Error during scan pass: {ex.Message}");
				}

				int pauseHours = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.ActiveScan, "PauseAfterFullScanHours", 2);
				try {
					await Task.Delay(TimeSpan.FromHours(Math.Max(0, pauseHours)), ct);
				} catch (TaskCanceledException) {
					return;
				}
			}
		}

		private void LoadPersistedTargets() {
			if (_stateLoaded)
				return;

			_tbotInstance.UserData.farmTargets ??= new List<FarmTarget>();
			foreach (var persisted in _stateStore.LoadTargets()) {
				if (!persisted.ConsumedReportId.HasValue &&
					persisted.State == FarmState.AttackSent &&
					persisted.Report?.ID > 0) {
					// Databases created before report consumption was persisted still
					// contain the report that was used for this completed dispatch.
					persisted.ConsumedReportId = persisted.Report.ID;
				}

				var current = _tbotInstance.UserData.farmTargets.FirstOrDefault(target =>
					target?.Celestial?.Coordinate?.IsSame(persisted.Celestial.Coordinate) == true);
				if (current == null) {
					_tbotInstance.UserData.farmTargets.Add(persisted);
					continue;
				}

				current.Celestial = persisted.Celestial;
				current.State = persisted.State;
				current.Report = persisted.Report;
				current.ConsumedReportId = persisted.ConsumedReportId;
			}

			var cursor = _stateStore.LoadScanCursor();
			if (cursor != null) {
				_tbotInstance.UserData.autoFarmLastRangeIndex = cursor.RangeIndex;
				_tbotInstance.UserData.autoFarmLastGalaxy = cursor.Galaxy;
				_tbotInstance.UserData.autoFarmLastSystem = cursor.System;
			}
			_stateLoaded = true;
		}

		private void PersistTargets() {
			_stateStore.ReplaceTargets(_tbotInstance.UserData.farmTargets, DateTime.UtcNow);
		}

		private void SaveScanCursor(int rangeIndex, int galaxy, int system) {
			_tbotInstance.UserData.autoFarmLastRangeIndex = rangeIndex;
			_tbotInstance.UserData.autoFarmLastGalaxy = galaxy;
			_tbotInstance.UserData.autoFarmLastSystem = system;
			_stateStore.SaveScanCursor(rangeIndex, galaxy, system, DateTime.UtcNow);
		}

		private TimeSpan GetSystemDataTtl() {
			int days = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm,
				"DaysToKeepOldSystemData", 7);
			return TimeSpan.FromDays(Math.Max(1, days));
		}

		private TimeSpan GetEmptySystemCooldown() {
			int days = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm,
				"EmptySystemCooldownDays", 30);
			return TimeSpan.FromDays(Math.Max(1, days));
		}

		private async Task PruneOldReports(List<Fleet> fleets) {
			var newTime = await _tbotOgameBridge.GetDateTime();
			// PATCH 117: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 137
			// HeldForManualReview-Ziele (initial zu stark verteidigt) werden von der altersbasierten
			// Report-Loeschung ausgenommen - der Bericht soll bestehen bleiben, bis der Nutzer ihn
			// selbst manuell prueft/entfernt, nicht automatisch nach AutoFarm.KeepReportFor verschwinden.
			var removeReports = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackSent || (!t.HeldForManualReview && t.Report != null && DateTime.Compare(t.Report.Date.AddMinutes((double) _tbotInstance.InstanceSettings.AutoFarm.KeepReportFor), newTime) < 0)).ToList();
			// PATCH 86, 68: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 10
			int attackGraceSeconds = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "AttackGraceSeconds", 60);
			foreach (var remove in removeReports) {
				if (remove.State == FarmState.AttackSent) {
					bool withinGracePeriod = remove.AttackSentAt.HasValue &&
						(newTime - remove.AttackSentAt.Value).TotalSeconds < attackGraceSeconds;
					bool stillInProgress = AutoFarmAttackPolicy.IsAttackInProgress(remove, fleets);

					if (withinGracePeriod || stillInProgress) {
						// PATCH 141: logged once per target and interval instead of every cycle
						LogThrottled($"attacksent:{remove.Celestial?.Coordinate}", LogLevel.Debug,
							withinGracePeriod && !stillInProgress
								? $"Keeping {remove} in AttackSent state - within {attackGraceSeconds}s grace period after dispatch (fleet list may not be updated yet)."
								: $"Keeping {remove} in AttackSent state while an attack fleet is still in progress.");
						continue;
					}
				}

				// PATCH 84: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 11
				bool reportExpiredByAge = remove.Report != null &&
					DateTime.Compare(remove.Report.Date.AddMinutes((double) _tbotInstance.InstanceSettings.AutoFarm.KeepReportFor), newTime) < 0;

				var updateReport = remove;
				updateReport.State = FarmState.ProbesPending;
				if (reportExpiredByAge || updateReport.Report == null) {
					updateReport.Report = null;
				} else {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
						$"Keeping last espionage report for {remove} visible (AutoFarm.KeepReportFor not yet elapsed) even though the attack fleet already landed.");
				}
				_tbotInstance.UserData.farmTargets.Remove(remove);
				_tbotInstance.UserData.farmTargets.Add(updateReport);
			}
		}

		private Dictionary<int, long> GetCachedCelestialProbes(List<Celestial> origins) {
			return (origins ?? new List<Celestial>())
				.Where(celestial => celestial != null)
				.GroupBy(celestial => celestial.ID)
				.ToDictionary(
					group => group.Key,
					group => group.First().Ships?.EspionageProbe ?? 0);
		}

		private List<Celestial> GetFarmOrigins() {
			List<Celestial> origins;
			if (_tbotInstance.InstanceSettings.AutoFarm.Origin.Length > 0)
				origins = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.AutoFarm.Origin, _tbotInstance.UserData.celestials);
			else
				origins = _tbotInstance.UserData.celestials;

			return (origins ?? new List<Celestial>())
				.Where(celestial => celestial?.Coordinate != null)
				.GroupBy(celestial => $"{celestial.Coordinate.Galaxy}:{celestial.Coordinate.System}:{celestial.Coordinate.Position}:{celestial.Coordinate.Type}")
				.Select(group => group.First())
				.ToList();
		}

		// PATCH 105: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 125
		// Liefert die tatsaechlich zu scannenden Ranges (Galaxy/StartSystem/EndSystem). Normalerweise
		// identisch mit der manuell konfigurierten Liste AutoFarm.ScanRange - ist aber zusaetzlich
		// die optionale Einstellung AutoFarm.SystemRange (int, Radius) gesetzt, wird ScanRange
		// komplett IGNORIERT und die Ranges stattdessen automatisch berechnet: fuer jede Galaxie, in
		// der ein AutoFarm-Origin liegt (AutoFarm.Origin, sonst alle Celestials), wird eine Range
		// [homeSystem - SystemRange, homeSystem + SystemRange] erzeugt (auf [1, ServerData.Systems]
		// geklemmt - bewusst OHNE Wraparound ueber die Galaxie-Ringgrenze, das waere nur relevant,
		// wenn die Heimatwelt sehr nah an System 1 oder Systems liegt UND der Radius entsprechend
		// gross ist; fuer den Normalfall reicht Klemmen). Grund: Nutzer-Feedback, dass eine manuell
		// gepflegte Start/End-Range ueberfluessig ist, seit die Scan-REIHENFOLGE (PATCH 104) ohnehin
		// vom Origin aus gezaehlt wird - ein einzelner Radius-Wert ist einfacher zu pflegen.
		private List<dynamic> GetEffectiveScanRanges() {
			bool useSystemRange = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "SystemRange") &&
				(int) _tbotInstance.InstanceSettings.AutoFarm.SystemRange > 0;

			if (!useSystemRange)
				return ((IEnumerable<dynamic>) _tbotInstance.InstanceSettings.AutoFarm.ScanRange).ToList();

			int radius = (int) _tbotInstance.InstanceSettings.AutoFarm.SystemRange;
			int totalSystems = (int) _tbotInstance.UserData.serverData.Systems;

			var homeGalaxies = GetFarmOrigins()
				.Select(origin => (int) origin.Coordinate.Galaxy)
				.Distinct()
				.ToList();

			var generatedRanges = new List<dynamic>();
			foreach (var galaxy in homeGalaxies) {
				int home = GetHomeSystemForGalaxy(galaxy);
				int rawStart = home - radius;
				int rawEnd = home + radius;

				// PATCH 112: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 132
				// PATCH 111 hat bei Ueber-/Unterlauf zwei getrennte Range-Segmente erzeugt (z.B.
				// [107,499] + [1,108]). Bug (Nutzer-Meldung: "block reihenfolge falschrum, weiteste
				// weg in block 5"): die beiden Segmente werden als GETRENNTE Ranges weiter unten per
				// `orderedRanges.OrderBy(Galaxy).ThenBy(StartSystem)` einsortiert - das sortiert nach
				// roher Systemnummer, NICHT nach Distanz zur Heimatwelt. Ergebnis: das Segment mit der
				// kleineren StartSystem-Zahl (hier [1,108], Distanzbereich ~143-249) wurde KOMPLETT VOR
				// dem Segment abgearbeitet, das die Heimatwelt selbst enthaelt ([107,499], Distanz
				// 0-250) - obwohl innerhalb jedes einzelnen Segments korrekt nach Distanz sortiert
				// wurde, war die REIHENFOLGE DER SEGMENTE UNTEREINANDER falsch, weil die beiden
				// Distanzbereiche sich ueberlappen (ein Segment kann nicht einfach "danach" drankommen,
				// wenn es naehere Systeme enthaelt als das Ende des anderen Segments).
				// Fix: bei Ueber-/Unterlauf wird jetzt EIN einziges Segment ueber die GESAMTE Galaxie
				// [1, totalSystems] erzeugt, statt zwei getrennte Segmente. AutoFarmScanPlanner.
				// EnumerateSystemsByDistance sortiert dieses eine Segment danach als GANZES korrekt
				// nach Distanz zur Heimatwelt (naechstes zuerst, echter Ring-Wraparound inklusive) -
				// es gibt dann keine zwei Teillisten mehr, deren Reihenfolge zueinander stimmen
				// muesste. Praktische Konsequenz: bei einem Radius, der ueberhaupt einen Wraparound
				// ausloest, wird effektiv die komplette Galaxie als Kandidatenmenge herangezogen (die
				// Sortierung nach Distanz sorgt aber weiterhin dafuer, dass die naechsten Systeme
				// zuerst drankommen - AutoFarm.ScanBlockSize/TargetsProbedBeforeAttack begrenzen die
				// tatsaechliche Arbeit pro Zyklus ohnehin unabhaengig von der Groesse der Kandidatenmenge).
				// Ohne bekannte Gesamt-Systemzahl (totalSystems <= 0) wird weiterhin nur geklemmt, da
				// dann kein sicherer Wraparound berechenbar ist.
				if (totalSystems > 0 && (rawStart < 1 || rawEnd > totalSystems)) {
					generatedRanges.Add(new { Galaxy = galaxy, StartSystem = 1, EndSystem = totalSystems });
				} else {
					int start = Math.Max(1, rawStart);
					int end = totalSystems > 0 ? Math.Min(totalSystems, rawEnd) : rawEnd;
					generatedRanges.Add(new { Galaxy = galaxy, StartSystem = start, EndSystem = end });
				}
			}

			if (!generatedRanges.Any()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
					"AutoFarm.SystemRange set, but no AutoFarm origin found - falling back to AutoFarm.ScanRange.");
				return ((IEnumerable<dynamic>) _tbotInstance.InstanceSettings.AutoFarm.ScanRange).ToList();
			}

			return generatedRanges;
		}

		// PATCH 104: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 124
		// Liefert das System, das als "0-Punkt" fuer die distanzbasierte Scan-Reihenfolge dient
		// (siehe AutoFarmScanPlanner.EnumerateSystemsByDistance) - also das System, von dem aus in
		// der angegebenen Galaxie tatsaechlich Flotten/Sonden verschickt werden (Heimatwelt oder
		// Mond, beide teilen sich dieselbe Systemnummer). Bevorzugt einen Origin (AutoFarm.Origin,
		// oder falls leer alle Celestials) in derselben Galaxie wie die zu scannende Range; gibt es
		// dort keinen, wird ersatzweise der erste bekannte Origin ueberhaupt verwendet (besser eine
		// grobe Naeherung als gar keine Sortierung).
		private int GetHomeSystemForGalaxy(int galaxy) {
			var farmOrigins = GetFarmOrigins();
			if (farmOrigins.Count == 0)
				return 0;

			var sameGalaxyOrigin = farmOrigins.FirstOrDefault(o => (int) o.Coordinate.Galaxy == galaxy);
			return (int) (sameGalaxyOrigin ?? farmOrigins.First()).Coordinate.System;
		}

		private bool ShouldExcludeSystem(int galaxy, int system) {
			bool excludeSystem = false;
			foreach (var exclude in _tbotInstance.InstanceSettings.AutoFarm.Exclude) {
				bool hasPosition = false;
				foreach (var value in exclude.Keys)
					if (value == "Position")
						hasPosition = true;
				if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && !hasPosition) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping system {system.ToString()}: system in exclude list.");
					excludeSystem = true;
					break;
				}
			}
			return excludeSystem;
		}

		private async Task<List<Celestial>> GetScannedTargetsFromGalaxy(int galaxy, int system) {
			DateTime nowUtc = DateTime.UtcNow;
			var cachedSnapshot = _stateStore.GetSystemSnapshot(galaxy, system);
			var cacheDecision = AutoFarmSystemCachePolicy.GetDecision(
				cachedSnapshot,
				nowUtc,
				GetSystemDataTtl(),
				GetEmptySystemCooldown());
			if (cacheDecision == AutoFarmSystemCacheDecision.Skip) {
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
					$"Skipping cached empty system {galaxy}:{system} until {cachedSnapshot.ObservedAtUtc.Add(GetEmptySystemCooldown()):u}.");
				return null;
			}

			if (cacheDecision == AutoFarmSystemCacheDecision.UseSnapshot) {
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
					$"Using cached galaxy data for {galaxy}:{system} ({(nowUtc - cachedSnapshot.ObservedAtUtc).TotalDays:F1} days old).");
				return AutoFarmSystemPolicy.GetEligibleTargets(cachedSnapshot.Planets);
			}

			GalaxyInfo galaxyInfo = null;
			int retryCount = 0;
			int maxRetries = 5;

			while (retryCount < maxRetries) {
				try {
					galaxyInfo = await _ogameService.GetGalaxyInfo(galaxy, system);
					break;
				} catch (Exception e) when (e.Message.Contains("system must be within") || e.Message.Contains("503") || e.Message.Contains("Service Unavailable")) {
					retryCount++;
					int waitSeconds = retryCount * 3;

					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Exception details: {e.Message}");
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Galaxy scan failed. Retry {retryCount}/{maxRetries} in {waitSeconds}s...");

					if (retryCount < maxRetries) {
						await Task.Delay(waitSeconds * 1000);

						try {
							_tbotInstance.UserData.serverData = await _ogameService.GetServerData();
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"ServerData after refresh: Systems={_tbotInstance.UserData.serverData.Systems}");
						} catch (Exception serverDataEx) {
							_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"Failed to refresh ServerData: {serverDataEx.Message}");
						}

						if (retryCount == 4 && _tbotInstance.UserData.serverData.Systems == 0) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"ServerData broken. Root cause: {e.Message}. Restarting ogamed...");

							try {
								_ogameService.KillOgamedExecutable();
								await Task.Delay(5000);
								_ogameService.RerunOgamed();
								await Task.Delay(10000);

								try {
									_tbotInstance.UserData.serverData = await _ogameService.GetServerData();
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"ServerData after restart: Systems={_tbotInstance.UserData.serverData.Systems}");
								} catch (Exception restartEx) {
									_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"Failed to get ServerData after ogamed restart: {restartEx.Message}");
								}
							} catch (Exception ogamedEx) {
								_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"Failed to restart ogamed: {ogamedEx.Message}");
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {ogamedEx.StackTrace}");
							}
						}
					} else {
						_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"Galaxy scan failed after {maxRetries} retries.");
						throw;
					}
				}
			}

			if (galaxyInfo?.Planets == null) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Galaxy scan returned no planet data for {galaxy}:{system}; this system will not be cached as empty.");
				return null;
			}

			var planets = galaxyInfo.Planets.Where(p => p != null).ToList();
			bool isEmptySystem = AutoFarmSystemPolicy.IsEmptySystem(planets);
			_stateStore.SaveSystemSnapshot(galaxy, system, nowUtc, isEmptySystem, planets);

			if (isEmptySystem)
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"System {galaxy}:{system} is empty for AutoFarm; applying the configured long cooldown.");

			return AutoFarmSystemPolicy.GetEligibleTargets(planets);
		}

		private bool IsTargetInMinimumRank(Celestial planet, List<Celestial> scannedTargets) {
			if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinimumPlayerRank") && _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank != 0) {
				int rank = 1;
				if (planet.Coordinate.Type == Celestials.Planet) {
					rank = (planet as Planet).Player.Rank;
				} else {
					if (scannedTargets.Any(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)))) {
						rank = (scannedTargets.Single(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet))) as Planet).Player.Rank;
					}
				}
				if ((int) _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank < rank) {

					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipping {planet.ToString()}: player has rank {rank} that is less than minimum configured {(int) _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank}.");
					return false;
				}
			}
			return true;
		}

		// --- AutoFarm.AllianceWhitelist: Mitglieder der eigenen (und optional weiterer) Allianzen werden nie
		// ausgespaeht oder angegriffen. Mitgliederliste aus der oeffentlichen OGame-API (alliances.xml, aktualisiert
		// sich dort nur 1x/Tag -> alle ~20 h neu laden). ShouldExcludePlanet ist der gemeinsame Filter fuer Scan,
		// Spionage und Angriff, daher greift der Schutz an allen drei Stellen.
		private static readonly HttpClient _allianceApiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		private HashSet<int> _allyPlayerIds = new();
		private readonly HashSet<string> _allyPlanetPositions = new();
		private DateTime _allyCacheTime = DateTime.MinValue;

		private async Task RefreshAllianceWhitelistAsync() {
			try {
				var af = _tbotInstance.InstanceSettings.AutoFarm;
				if (!SettingsService.IsSettingSet(af, "AllianceWhitelist")) {
					_allyPlayerIds = new();
					return;
				}
				dynamic wl = af.AllianceWhitelist;
				if (!SettingsService.IsSettingSet(wl, "Active") || !(bool) wl.Active) {
					_allyPlayerIds = new();
					return;
				}
				if (DateTime.UtcNow - _allyCacheTime >= TimeSpan.FromHours(20)) {
					string host = SettingsService.IsSettingSet(wl, "ServerHost") ? (string) wl.ServerHost : "";
					var ids = new List<int>();
					if (SettingsService.IsSettingSet(wl, "AllianceId") && Convert.ToInt32(wl.AllianceId) > 0)
						ids.Add(Convert.ToInt32(wl.AllianceId));
					if (SettingsService.IsSettingSet(wl, "AdditionalAllianceIds"))
						foreach (var extra in wl.AdditionalAllianceIds)
							if (Convert.ToInt32(extra) > 0)
								ids.Add(Convert.ToInt32(extra));
					if (string.IsNullOrWhiteSpace(host) || ids.Count == 0) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "AutoFarm.AllianceWhitelist.Active is true, but ServerHost or AllianceId is missing - whitelist not applied.");
						_allyCacheTime = DateTime.UtcNow.AddHours(-19);
						return;
					}
					try {
						string xml = await _allianceApiClient.GetStringAsync($"https://{host}/api/alliances.xml");
						XDocument doc = XDocument.Parse(xml);
						var members = new HashSet<int>();
						foreach (int allianceId in ids) {
							XElement alliance = doc.Descendants("alliance").FirstOrDefault(a => (int?) a.Attribute("id") == allianceId);
							if (alliance == null) {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"AutoFarm.AllianceWhitelist: alliance ID {allianceId} not found in the API response.");
								continue;
							}
							foreach (var p in alliance.Descendants("player")) {
								int? pid = (int?) p.Attribute("id");
								if (pid.HasValue)
									members.Add(pid.Value);
							}
						}
						_allyPlayerIds = members;
						_allyCacheTime = DateTime.UtcNow;
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"AutoFarm.AllianceWhitelist: {members.Count} member IDs loaded ({ids.Count} alliance(s)).");
					} catch (Exception e) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"AutoFarm.AllianceWhitelist: update failed ({e.Message}) - keeping the last known list, retry in 1 h.");
						_allyCacheTime = DateTime.UtcNow.AddHours(-19);
					}
				}
				// Bereits bekannte Ziele von Allianzmitgliedern entfernen (z. B. nach Beitritt), ausser Flotte/Sonden sind unterwegs
				if (_allyPlayerIds.Count > 0 && _tbotInstance.UserData.farmTargets != null) {
					int removed = _tbotInstance.UserData.farmTargets.RemoveAll(t =>
						t?.Celestial is Planet pl && pl.Player != null && _allyPlayerIds.Contains(pl.Player.ID)
						&& t.State != FarmState.ProbesSent && t.State != FarmState.AttackSent);
					if (removed > 0)
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"AutoFarm.AllianceWhitelist: removed {removed} alliance member target(s) from the farm list.");
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"AutoFarm.AllianceWhitelist failed: {e.Message}");
			}
		}

		private bool IsAllianceWhitelisted(Celestial planet) {
			if (_allyPlayerIds.Count == 0)
				return false;
			string pos = $"{planet.Coordinate.Galaxy}:{planet.Coordinate.System}:{planet.Coordinate.Position}";
			if (planet is Planet p && p.Player != null && _allyPlayerIds.Contains(p.Player.ID)) {
				_allyPlanetPositions.Add(pos);
				LogThrottled($"allywl:{pos}", LogLevel.Debug, $"Skipping {planet.ToString()}: alliance whitelist (player {p.Player.ID}).");
				return true;
			}
			// Monde tragen kein Player-Objekt: gleiche Koordinate wie ein bereits erkannter Allianz-Planet
			if (planet.Coordinate.Type == Celestials.Moon && _allyPlanetPositions.Contains(pos)) {
				LogThrottled($"allywl:{pos}:moon", LogLevel.Debug, $"Skipping {planet.ToString()}: moon of an alliance whitelist member.");
				return true;
			}
			return false;
		}

		private bool ShouldExcludePlanet(Celestial planet) {
			if (IsAllianceWhitelisted(planet))
				return true;
			bool excludePlanet = false;
			foreach (var exclude in _tbotInstance.InstanceSettings.AutoFarm.Exclude) {
				bool hasPosition = false;
				foreach (var value in exclude.Keys)
					if (value == "Position")
						hasPosition = true;
				if ((int) exclude.Galaxy == planet.Coordinate.Galaxy && (int) exclude.System == planet.Coordinate.System && hasPosition && (int) exclude.Position == planet.Coordinate.Position) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {planet.ToString()}: celestial in exclude list.");
					excludePlanet = true;
					break;
				}
			}
			return excludePlanet;
		}

		private void AddMoons(List<Celestial> scannedTargets) {
			foreach (var t in scannedTargets.ToList()) {
				var planet = t as Planet;
				if (planet == null)
					continue;
				if (planet.Moon != null) {
					Celestial tempCelestial = planet.Moon;
					tempCelestial.Coordinate = new Coordinate(
						t.Coordinate.Galaxy,
						t.Coordinate.System,
						t.Coordinate.Position,
						Celestials.Moon);
					scannedTargets.Add(tempCelestial);
				}
			}
		}

		// PATCH 106: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 126
		// CheckDuplicatesAndGetExisting() (direkt unter dieser Methode) raeumt Duplikate nur
		// OPPORTUNISTISCH auf - und zwar nur fuer genau das eine Celestial, das gerade per
		// GetFarmTarget() erneut gescannt wird. Ein Duplikat, das zur Laufzeit entsteht (genaue
		// Ursache noch nicht abschliessend verifiziert - Verdacht: verwaister Eintrag aus einer
		// alten Sondierung/Report-Verarbeitung, moeglicherweise im Zusammenhang mit dem separat
		// gefixten ABRT-Crash beim Prozess-Stop), bleibt deshalb bestehen, bis der normale Scan-
		// Cursor zufaellig wieder genau bei diesem System vorbeikommt - mit PATCH 104
		// (distanzbasierte Scan-Reihenfolge) kann das fuer weit entfernte Systeme sehr lange
		// dauern. Ergebnis (Nutzer-Meldung, Screenshot): zwei komplett unabhaengige FarmTarget-
		// Objekte fuer dieselbe Koordinate erreichen unabhaengig voneinander AttackPending -> zwei
		// separate Angriffsflotten auf dasselbe Ziel.
		// Fix: einmal pro Execute()-Zyklus wird JETZT die GESAMTE farmTargets-Liste auf Duplikate
		// (gleiche Koordinate, siehe HasCoords) durchsucht, nicht nur das gerade gescannte Ziel.
		// Bei einem Duplikat-Fund wird NICHT einfach das erste behalten, sondern gezielt der am
		// weitesten fortgeschrittene/aktuellste Eintrag (siehe FarmStateRank - AttackSent/
		// AttackPending/ProbesSent haben Vorrang vor einem frischen, noch unbearbeiteten Eintrag,
		// damit kein bereits laufender Angriffs-/Sondenversand-Fortschritt und keine
		// ConsumedReportId-Buchhaltung verloren geht), bei Gleichstand der mit dem neueren Report.
		private void DeduplicateFarmTargets() {
			var targets = _tbotInstance.UserData.farmTargets;
			if (targets == null || targets.Count < 2)
				return;

			var snapshot = targets.ToList();
			var deduped = new List<FarmTarget>();
			var handled = new HashSet<FarmTarget>();

			foreach (var t in snapshot) {
				if (handled.Contains(t))
					continue;

				if (t?.Celestial?.Coordinate == null) {
					deduped.Add(t);
					handled.Add(t);
					continue;
				}

				var group = snapshot.Where(x => x?.Celestial != null && x.Celestial.HasCoords(t.Celestial.Coordinate)).ToList();
				foreach (var g in group)
					handled.Add(g);

				if (group.Count == 1) {
					deduped.Add(t);
					continue;
				}

				var kept = group
					.OrderByDescending(x => FarmStateRank(x.State))
					.ThenByDescending(x => x.Report?.Date ?? DateTime.MinValue)
					.First();
				deduped.Add(kept);

				foreach (var dup in group) {
					if (dup == kept)
						continue;
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
						$"[Dedup] Duplicate farm target for {dup.Celestial.Coordinate} removed (state {dup.State}, report from {dup.Report?.Date.ToString() ?? "n/a"}) - kept: state {kept.State}, report from {kept.Report?.Date.ToString() ?? "n/a"}.");
				}
			}

			if (deduped.Count != targets.Count) {
				targets.Clear();
				targets.AddRange(deduped);
			}
		}

		private static int FarmStateRank(FarmState state) {
			switch (state) {
				case FarmState.AttackSent: return 6;
				case FarmState.AttackPending: return 5;
				case FarmState.ProbesSent: return 4;
				case FarmState.FailedProbesRequired: return 3;
				case FarmState.ProbesRequired: return 2;
				case FarmState.NotSuitable: return 1;
				case FarmState.ProbesPending: return 0;
				default: return -1; // Idle / unbekannt
			}
		}

		private FarmTarget CheckDuplicatesAndGetExisting(Celestial planet) {
			var exists = _tbotInstance.UserData.farmTargets.Where(t => t != null && t.Celestial.HasCoords(planet.Coordinate)).ToList();
			if (exists.Count() > 1) {
				var firstExisting = exists.First();
				_tbotInstance.UserData.farmTargets.RemoveAll(c => c.Celestial.HasCoords(planet.Coordinate));
				_tbotInstance.UserData.farmTargets.Add(firstExisting);
				return firstExisting;
			}
			else if (exists.Count() == 1) {
				return exists.First();
			}
			return null;
		}

		private FarmTarget GetFarmTarget(Celestial planet) {
		bool blacklistActive = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
			SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "Active") &&
			(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;

		if (blacklistActive && _blacklist.IsBlacklisted(planet.Coordinate)) {
			var blacklistedTarget = _blacklist.GetBlacklistedTarget(planet.Coordinate);
			_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Target {planet.ToString()} is blacklisted (Reason: {blacklistedTarget.Reason}). Skipping...");
			return null;
		}

			var target = CheckDuplicatesAndGetExisting(planet);

			if (target == null) {
				target = new(planet, FarmState.ProbesPending);
				_tbotInstance.UserData.farmTargets.Add(target);
			} else {
				target.Celestial = planet;

				if (target.State == FarmState.Idle)
					target.State = FarmState.ProbesPending;

				if (target.State == FarmState.NotSuitable && target.Report != null) {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Target {planet.ToString()} marked as Not Suitable. Skipping...");
					return null;
				}

				if (target.State == FarmState.ProbesSent || target.State == FarmState.AttackPending || target.State == FarmState.AttackSent) {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Target {planet.ToString()} marked as {target.State.ToString()}. Skipping...");
					return null;
				}
			}
			return target;
		}

		private int GetNeededProbes(FarmTarget target) {
			int neededProbes = (int) _tbotInstance.InstanceSettings.AutoFarm.NumProbes;
			if (target.State == FarmState.ProbesRequired)
				neededProbes *= 3;
			if (target.State == FarmState.FailedProbesRequired)
				neededProbes *= 9;
			return neededProbes;
		}

		// PATCH 65, 62: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 12
		private Buildables GetConfiguredCargoType() {
			bool useSpeedFleet = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "UseSpeedFleet", false);
			if (useSpeedFleet) {
				string speedFleetTypesSetting = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "SpeedFleetCargoTypes", "Pathfinder");
				foreach (string typeName in speedFleetTypesSetting.Split(',')) {
					if (Enum.TryParse<Buildables>(typeName.Trim(), true, out var speedFleetCargoType) && speedFleetCargoType != Buildables.Null) {
						return speedFleetCargoType;
					}
				}
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"AutoFarm.UseSpeedFleet is active, but AutoFarm.SpeedFleetCargoTypes (\"{speedFleetTypesSetting}\") contains no valid ship type - falling back to AutoFarm.CargoType.");
			}

			if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.AutoFarm.CargoType, true, out var cargoType))
				return Buildables.LargeCargo;

			return cargoType;
		}

		// PATCH 79, 62: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 13
		private Buildables GetPlainCargoType() {
			if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.AutoFarm.CargoType, true, out var cargoType))
				return Buildables.LargeCargo;
			return cargoType;
		}

		// PATCH 39, 58, 60, 61, 64, 79, 65, 62, 66, 67, 80, 82, 68, 86, 69, 70, 51, 71, 72, 73, 59, 74, 47, 56, 57: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 14
		private class EscortSearchProgress {
			public Dictionary<Buildables, long> Escort;
			public SimulatorResult LastResult;
			public DateTime CachedAtUtc;
		}
		private readonly Dictionary<string, EscortSearchProgress> _escortSearchCache = new();

		private string BuildEscortSearchCacheKey(FarmTarget target, Buildables cargoShip, long numCargo) {
			return $"{target.Celestial.Coordinate}|{target.Report?.ID}|{cargoShip}|{numCargo}";
		}

		private void PruneEscortSearchCache() {
			var cutoff = DateTime.UtcNow.AddHours(-12);
			var staleKeys = _escortSearchCache.Where(kv => kv.Value.CachedAtUtc < cutoff).Select(kv => kv.Key).ToList();
			foreach (var key in staleKeys) {
				_escortSearchCache.Remove(key);
			}
		}

		// PATCH 57: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 15
		private bool AutoFarmIsEscortPoolLow(Celestial origin) {
			string typesSetting = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortShipTypes", "LightFighter,HeavyFighter,Cruiser,Battleship,Bomber,Destroyer,Battlecruiser,Reaper,Pathfinder");
			// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 16
			string fallbackTypesSetting = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortFallbackShipTypes", "");
			long step = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortShipsStep", (long) 20);
			foreach (string typeName in typesSetting.Split(',').Concat(fallbackTypesSetting.Split(','))) {
				if (!Enum.TryParse<Buildables>(typeName.Trim(), true, out var candidateType)) {
					continue;
				}
				if (origin.Ships.GetAmount(candidateType) >= step) {
					return false; // at least one type still has a usable amount - pool is fine
				}
			}
			return true;
		}

		// PATCH 98: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 17
		private long EstimateFleetResourceValue(Ships fleet) {
			Buildables[] shipTypes = {
				Buildables.LightFighter, Buildables.HeavyFighter, Buildables.Cruiser, Buildables.Battleship,
				Buildables.Battlecruiser, Buildables.Bomber, Buildables.Destroyer, Buildables.Deathstar,
				Buildables.SmallCargo, Buildables.LargeCargo, Buildables.ColonyShip, Buildables.Recycler,
				Buildables.EspionageProbe, Buildables.Reaper, Buildables.Pathfinder,
			};
			long total = 0;
			foreach (Buildables shipType in shipTypes) {
				long amount = fleet.GetAmount(shipType);
				if (amount <= 0) {
					continue;
				}
				total += _calculationService.CalcPrice(shipType, 1).TotalResources * amount;
			}
			return total;
		}

		private async Task<(Dictionary<Buildables, long> Escort, SimulatorResult Result, bool UsedFallbackTier)?> AutoFarmDetermineEscort(FarmTarget target, Celestial origin, Buildables cargoShip, long numCargo) {
			string typesSetting = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortShipTypes", "Cruiser,LightFighter,HeavyFighter,Battleship,Bomber,Destroyer,Battlecruiser,Reaper,Pathfinder");
			List<Buildables> candidateTypes = typesSetting.Split(',')
				.Select(s => Enum.TryParse<Buildables>(s.Trim(), true, out var b) ? b : Buildables.Null)
				.Where(b => b != Buildables.Null)
				.ToList();
			if (candidateTypes.Count == 0) {
				candidateTypes.Add(Buildables.Bomber);
			}

			// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 18
			string fallbackTypesSetting = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortFallbackShipTypes", "");
			List<Buildables> fallbackTypes = fallbackTypesSetting.Split(',')
				.Select(s => Enum.TryParse<Buildables>(s.Trim(), true, out var b) ? b : Buildables.Null)
				.Where(b => b != Buildables.Null && !candidateTypes.Contains(b))
				.Distinct()
				.ToList();

			long step = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortShipsStep", (long) 20);
			long fallbackCap = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxEscortShips", (long) 400);
			int maxIterations = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortMaxIterations", 30);
			double minWinRate = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MinWinRate", 0.90);
			// PATCH 54: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 19
			double maxCargoLossPercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxCargoLossPercent", 5.0);
			int simulations = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "SimulationRuns", 500);
			// PATCH 58: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 20
			int intermediateSimulations = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortSearchSimulationRuns", 100);

			// PATCH 50: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 21
			Dictionary<Buildables, long> shipCaps = new();
			// PATCH 118: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 138
			// EscortShipCaps ist eine FIXE absolute Obergrenze pro Schiffstyp - bei einer grossen
			// Flotte (z.B. >120000 Cruiser verfuegbar) kann das dazu fuehren, dass fuer EIN einzelnes
			// Ziel praktisch die gesamte verfuegbare Kampfkraft dieses Typs in die Ceiling-
			// Berechnung/Suche einfliesst (siehe Abschnitt 136/PATCH 116 - genau das hat zum
			// Sprit-Overkill-Fall gefuehrt). MaxEscortFleetPercent begrenzt den effektiven Cap
			// zusaetzlich auf einen Prozentsatz der AKTUELL VERFUEGBAREN Menge dieses Typs auf dem
			// Ursprungscelestial - damit wird nie mehr als dieser Anteil der Flotte fuer EIN Ziel in
			// Erwaegung gezogen, unabhaengig davon wie hoch EscortShipCaps gesetzt ist. Default 0 =
			// deaktiviert (altes Verhalten, volle Abwaertskompatibilitaet). Wirkt IMMER zusaetzlich zu
			// EscortShipCaps (der kleinere der beiden Werte gewinnt), ersetzt es nicht.
			double maxEscortFleetPercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxEscortFleetPercent", 0.0);
			// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 22
			foreach (Buildables candidateType in candidateTypes.Concat(fallbackTypes)) {
				long typeCap = fallbackCap;
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "EscortShipCaps")) {
					typeCap = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.EscortShipCaps, candidateType.ToString(), fallbackCap);
				}
				if (maxEscortFleetPercent > 0) {
					long availableOfType = origin.Ships.GetAmount(candidateType);
					long percentCap = (long) Math.Floor(availableOfType * (maxEscortFleetPercent / 100.0));
					if (percentCap < typeCap) {
						typeCap = percentCap;
					}
				}
				shipCaps[candidateType] = typeCap;
			}

			// PATCH 52: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 23
			long maxDefensePoints = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxDefensePoints", (long) 0);
			Defences reportDefences = new(
				target.Report.RocketLauncher.GetValueOrDefault(),
				target.Report.LightLaser.GetValueOrDefault(),
				target.Report.HeavyLaser.GetValueOrDefault(),
				target.Report.GaussCannon.GetValueOrDefault(),
				target.Report.IonCannon.GetValueOrDefault(),
				target.Report.PlasmaTurret.GetValueOrDefault(),
				target.Report.SmallShieldDome.GetValueOrDefault(),
				target.Report.LargeShieldDome.GetValueOrDefault(),
				target.Report.AntiBallisticMissiles.GetValueOrDefault(),
				target.Report.InterplanetaryMissiles.GetValueOrDefault()
			);
			// PATCH 71: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 24
			Ships reportFleet = new(
				target.Report.LightFighter.GetValueOrDefault(),
				target.Report.HeavyFighter.GetValueOrDefault(),
				target.Report.Cruiser.GetValueOrDefault(),
				target.Report.Battleship.GetValueOrDefault(),
				target.Report.Battlecruiser.GetValueOrDefault(),
				target.Report.Bomber.GetValueOrDefault(),
				target.Report.Destroyer.GetValueOrDefault(),
				target.Report.Deathstar.GetValueOrDefault(),
				target.Report.SmallCargo.GetValueOrDefault(),
				target.Report.LargeCargo.GetValueOrDefault(),
				target.Report.ColonyShip.GetValueOrDefault(),
				target.Report.Recycler.GetValueOrDefault(),
				target.Report.EspionageProbe.GetValueOrDefault(),
				target.Report.SolarSatellite.GetValueOrDefault(),
				target.Report.Crawler.GetValueOrDefault(),
				target.Report.Reaper.GetValueOrDefault(),
				target.Report.Pathfinder.GetValueOrDefault()
			);
			long fleetPoints = reportFleet.GetFleetPoints();
			long defensePoints = reportDefences.GetDefencePoints() + fleetPoints;
			_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: defense points {reportDefences.GetDefencePoints():N0} + fleet points {fleetPoints:N0} = {defensePoints:N0} (for AutoFarm.MaxDefensePoints calibration).");
			if (maxDefensePoints > 0 && defensePoints > maxDefensePoints) {
				// PATCH 117: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 137
				// Von Anfang an ("initial") zu stark verteidigte Ziele werden NICHT geblacklistet
				// (das wuerde den Spionagebericht nach AutoFarm.Blacklist.ResetAfterHours automatisch
				// wieder freigeben und die Entscheidung dem Nutzer entziehen) und auch NICHT in eine
				// separate DB-Liste verschoben (der Nutzer will den Kampfrechner direkt vom normalen
				// Spionagebericht aus aufrufen koennen, eine eigene Liste waere dafuer nicht
				// kontrollierbar). Stattdessen wird das Ziel nur als HeldForManualReview markiert:
				// bleibt in der normalen Zielliste sichtbar, wird aber von PruneOldReports NICHT mehr
				// automatisch nach AutoFarm.KeepReportFor geloescht - der Nutzer prueft/entfernt es
				// manuell selbst (z.B. per Kampfrechner direkt am Bericht).
				if (!target.HeldForManualReview) {
					target.HeldForManualReview = true;
					_tbotInstance.UserData.farmTargets.Remove(target);
					_tbotInstance.UserData.farmTargets.Add(target);
					PersistTargets();
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: defense points ({defensePoints:N0}) exceed AutoFarm.MaxDefensePoints ({maxDefensePoints:N0}) - too strongly defended initially, marked as HeldForManualReview (no blacklist, espionage report is kept until you remove it manually).");
				} else {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: still HeldForManualReview (defense points {defensePoints:N0} > AutoFarm.MaxDefensePoints {maxDefensePoints:N0}) - skipping without simulation.");
				}
				return null;
			}
			// PATCH 117: Ziel ist nicht (mehr) ueber dem Schwellwert - falls es vorher HeldForManualReview
			// war (z.B. weil der Verteidiger abgezogen ist oder der Nutzer den Schwellwert erhoeht hat),
			// die Markierung wieder entfernen, damit es wieder ganz normal verarbeitet wird.
			if (target.HeldForManualReview) {
				target.HeldForManualReview = false;
				_tbotInstance.UserData.farmTargets.Remove(target);
				_tbotInstance.UserData.farmTargets.Add(target);
				PersistTargets();
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: no longer above AutoFarm.MaxDefensePoints ({defensePoints:N0} <= {maxDefensePoints:N0}) - HeldForManualReview mark removed, processed normally again.");
			}

			// PATCH 66, 51: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 25
			double minLootPerDefensePoint = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MinLootPerDefensePoint", 0.0);
			if (minLootPerDefensePoint > 0 && defensePoints > 0) {
				long targetLoot = target.Report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources;
				double requiredLoot = defensePoints * minLootPerDefensePoint;
				if (targetLoot < requiredLoot) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: loot ({targetLoot:N0}) is out of proportion to the defense ({defensePoints:N0} points, {requiredLoot:N0} would be needed) - skipping without simulation.");
					return null;
				}
			}

			// PATCH 67, 66: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 26
			long minimumResourcesDefended = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MinimumResourcesDefended", (long) 0);
			if (minimumResourcesDefended > 0) {
				long targetLootForFloor = target.Report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources;
				// PATCH 98, 80: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 27
				long estimatedFleetDebris = 0;
				if (fleetPoints > 0) {
					double fleetToDebrisPercentForFloor = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FleetToDebrisPercent", 30.0) / 100.0;
					estimatedFleetDebris = (long) (EstimateFleetResourceValue(reportFleet) * fleetToDebrisPercentForFloor);
				}
				long targetLootPlusDebrisForFloor = targetLootForFloor + estimatedFleetDebris;
				if (targetLootPlusDebrisForFloor < minimumResourcesDefended) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: loot ({targetLootForFloor:N0}) + estimated fleet debris ({estimatedFleetDebris:N0}) = {targetLootPlusDebrisForFloor:N0} below AutoFarm.MinimumResourcesDefended ({minimumResourcesDefended:N0}) - defended target is not worth the escort effort, skipping without simulation.");
					return null;
				}
			}

			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
			SimulatorDefender defender = AutoFarmBuildSimulatorDefender(target.Report);

			// PATCH 80: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 28
			double fleetToDebris = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FleetToDebrisPercent", 30.0) / 100.0;

			// PATCH 46, 58: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 29
			SimulatorFleet BuildAttackerFleet(Dictionary<Buildables, long> composition) {
				var fleet = new SimulatorFleet {
					Weapon = _tbotInstance.UserData.researches.WeaponsTechnology,
					Shield = _tbotInstance.UserData.researches.ShieldingTechnology,
					Armour = _tbotInstance.UserData.researches.ArmourTechnology,
				};
				typeof(SimulatorFleet).GetProperty(cargoShip.ToString())?.SetValue(fleet, numCargo);
				foreach (var kv in composition) {
					typeof(SimulatorFleet).GetProperty(kv.Key.ToString())?.SetValue(fleet, kv.Value);
				}
				return fleet;
			}

			// PATCH 61, 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 30
			Dictionary<Buildables, long> fixedBase = new();

			// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 31
			Dictionary<Buildables, long> WithFixedBase(Dictionary<Buildables, long> varying) {
				if (fixedBase.Count == 0) {
					return varying;
				}
				var merged = new Dictionary<Buildables, long>(fixedBase);
				foreach (var kv in varying) {
					merged[kv.Key] = merged.GetValueOrDefault(kv.Key, 0) + kv.Value;
				}
				return merged;
			}

			// PATCH 124: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 144 - Eskortensuche nach Kosten
			// statt nur nach Schiffsanzahl. Bisher galt als "Ja" nur Winrate + Cargo-Verlust, die
			// Eskortenverluste selbst spielten fuer die Suche keine Rolle - Ergebnis war z.B. eine
			// "minimale" Eskorte, die zwar zu 100% gewinnt, dabei aber 42 Mio. an eigenen Schiffen
			// verliert. Neu:
			//   - optionales Verlustlimit AutoFarm.MaxEscortLossPercent (Anteil des Werts der
			//     GESAMTEN Angriffsflotte, der laut Simulation verloren geht; 0 = aus)
			//   - Kostenfunktion Verluste + Sprit (Referenz 100% Geschwindigkeit, gleich wie beim
			//     bisherigen PreferLowerLossComposition-Vergleich), um unter allen getesteten
			//     "Ja"-Punkten den billigsten zu waehlen und das Feintuning danach zu steuern
			double maxEscortLossPercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxEscortLossPercent", 0.0);

			Ships ToShipsWithCargo(Dictionary<Buildables, long> fullComposition) {
				Ships ships = new Ships().Add(cargoShip, numCargo);
				foreach (var kv in fullComposition) {
					ships.Add(kv.Key, kv.Value);
				}
				return ships;
			}

			bool EscortLossOk(Dictionary<Buildables, long> fullComposition, SimulatorResult result) {
				if (maxEscortLossPercent <= 0 || result == null) {
					return true;
				}
				long fleetValue = EstimateFleetResourceValue(ToShipsWithCargo(fullComposition));
				if (fleetValue <= 0) {
					return true;
				}
				long losses = result.AttackerLosses?.Total() ?? 0;
				return losses * 100.0 / fleetValue <= maxEscortLossPercent;
			}

			// PATCH 139: see AutoFarmWorker.PATCHNOTES.md, section 157 - weight of ship losses versus fuel
			// when choosing the escort. 1.0 = unchanged (1 resource lost = 1 resource fuel). >1 = losses weigh more, a
			// larger escort with fewer losses is preferred. Affects ONLY the selection, the profitability
			// check still uses real losses.
			double escortLossWeight = Math.Max(0.0, SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortLossWeight", 1.0));

			long CompositionCost(Dictionary<Buildables, long> fullComposition, SimulatorResult result) {
				long losses = (long) ((result?.AttackerLosses?.Total() ?? 0) * escortLossWeight);
				long fuel = 0;
				try {
					fuel = _calculationService.CalcFleetPrediction(origin.Coordinate, target.Celestial.Coordinate, ToShipsWithCargo(fullComposition), Missions.Attack, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Fuel;
				} catch (Exception e) {
					// Sprit nicht berechenbar - dann nur nach Verlust vergleichen (wie PATCH 116 im Fehlerfall)
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: fuel estimate for the cost comparison failed ({e.Message}) - comparing by loss only.");
				}
				return losses + fuel;
			}

			string FormatComposition(Dictionary<Buildables, long> composition) {
				return string.Join(", ", composition.Select(kv => $"{kv.Value}x{kv.Key}"));
			}

			// PATCH 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 32
			async Task<(bool Accepted, SimulatorResult Result, bool CallFailed)> ConfirmCompositionAsync(Dictionary<Buildables, long> composition) {
				SimulatorResult confirmation;
				try {
					// PATCH (Heartbeat, 22.09.2026): Lebenszeichen vor jedem teuren, u.U. bis zu 60s
					// dauernden SimulateBattle-Aufruf - siehe WorkerBase.Heartbeat()/HangDetectionTimeout.
					Heartbeat();
					confirmation = await _ogameService.SimulateBattle(new SimulateBattleRequest {
						Attacker = BuildAttackerFleet(composition),
						Defender = defender,
						FleetToDebris = fleetToDebris,
						Simulations = simulations,
					});
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Full-precision confirmation simulate-battle call failed ({e.Message}) for {target.Celestial.Coordinate}.");
					return (false, null, true);
				}
				double confirmationCargoLossPercent = confirmation.CargoLossRate * 100;
				// PATCH 124: Verlustlimit (MaxEscortLossPercent) gilt auch fuer die Bestaetigung
				bool accepted = confirmation.AttackerWinRate >= minWinRate && confirmationCargoLossPercent <= maxCargoLossPercent
					&& EscortLossOk(composition, confirmation);
				return (accepted, confirmation, false);
			}

			// PATCH 49: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 33
			Dictionary<Buildables, long> ceilingComposition = new();
			SimulatorResult ceilingResult = null;
			{
				// Each type at its own cap (shipCaps) now - no more shared total to split, so
				// this can use every type at full strength simultaneously, same as the greedy
				// search's actual per-type ceiling.
				foreach (Buildables candidateType in candidateTypes) {
					long capped = Math.Min(origin.Ships.GetAmount(candidateType), shipCaps[candidateType]);
					if (capped > 0) {
						ceilingComposition[candidateType] = capped;
					}
				}
				// PATCH 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 34
				var ceilingAttacker = BuildAttackerFleet(ceilingComposition);
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: running ceiling check (every candidate type at its cap) before the detailed search...");
				try {
					// A rough pass/fail check doesn't need the full simulation count - fewer runs
					// here means less time spent before either bailing out or moving on to the
					// (also individually much smaller, per-type) detailed search below.
					int ceilingSimulations = Math.Min(simulations, intermediateSimulations);
					Heartbeat();
					ceilingResult = await _ogameService.SimulateBattle(new SimulateBattleRequest {
						Attacker = ceilingAttacker,
						Defender = defender,
						FleetToDebris = fleetToDebris,
						Simulations = ceilingSimulations,
					});
					double ceilingCargoLossPercent = ceilingResult.CargoLossRate * 100;
					if (ceilingResult.AttackerWinRate < minWinRate || ceilingCargoLossPercent > maxCargoLossPercent) {
						if (fallbackTypes.Count == 0) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: even every available escort type at once only reaches {ceilingResult.AttackerWinRate:P0} win rate / {ceilingCargoLossPercent:F1}% cargo loss - not beatable within the configured per-type caps and loss limits, skipping detailed search.");
							return null;
						}
						// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 35
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: Tier-1 ceiling ({ceilingResult.AttackerWinRate:P0} win rate / {ceilingCargoLossPercent:F1}% cargo loss) is not enough - checking Tier-1+Tier-2 ceiling ({string.Join(",", fallbackTypes)})...");
						Dictionary<Buildables, long> fallbackCeilingComposition = new();
						foreach (Buildables fallbackType in fallbackTypes) {
							long capped = Math.Min(origin.Ships.GetAmount(fallbackType), shipCaps[fallbackType]);
							if (capped > 0) {
								fallbackCeilingComposition[fallbackType] = capped;
							}
						}
						var combinedComposition = new Dictionary<Buildables, long>(ceilingComposition);
						foreach (var kv in fallbackCeilingComposition) {
							combinedComposition[kv.Key] = combinedComposition.GetValueOrDefault(kv.Key, 0) + kv.Value;
						}
						SimulatorResult combinedResult;
						try {
							Heartbeat();
							combinedResult = await _ogameService.SimulateBattle(new SimulateBattleRequest {
								Attacker = BuildAttackerFleet(combinedComposition),
								Defender = defender,
						FleetToDebris = fleetToDebris,
								Simulations = Math.Min(simulations, intermediateSimulations),
							});
						} catch (Exception e) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Combined ceiling simulate-battle call failed ({e.Message}) for {target.Celestial.Coordinate} - not beatable within the configured per-type caps and loss limits (Tier-2 fallback could not be checked), skipping detailed search.");
							return null;
						}
						double combinedCargoLossPercent = combinedResult.CargoLossRate * 100;
						if (combinedResult.AttackerWinRate < minWinRate || combinedCargoLossPercent > maxCargoLossPercent) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: selbst Tier 1 + Tier 2 zusammen am Cap erreichen nur {combinedResult.AttackerWinRate:P0} win rate / {combinedCargoLossPercent:F1}% cargo loss - not beatable within the configured per-type caps and loss limits, skipping detailed search.");
							return null;
						}
						// Tier 1 wird ab hier als fixedBase eingefroren (immer am Cap, nie wieder variiert),
						// candidateTypes wechselt auf die Tier-2-Typen, und ceilingComposition/ceilingResult
						// werden auf die reine Tier-2-Ceiling-Referenz bzw. das kombinierte Ergebnis
						// umgestellt - Bisektion und Feinabstimmung unten sehen dann nur noch Tier 2.
						fixedBase = ceilingComposition;
						candidateTypes = fallbackTypes;
						ceilingComposition = fallbackCeilingComposition;
						ceilingResult = combinedResult;
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: Tier-1+Tier-2 ceiling is enough ({combinedResult.AttackerWinRate:P0} win rate, {combinedCargoLossPercent:F1}% cargo loss) - Tier 1 fixed at {string.Join(", ", fixedBase.Select(kv => $"{kv.Value}x{kv.Key}"))}, now searching for the minimal Tier-2 addition.");
					}
					if (fixedBase.Count == 0) {
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: ceiling check passed ({ceilingResult.AttackerWinRate:P0} win rate, {ceilingCargoLossPercent:F1}% cargo loss at max caps) - proceeding to find the minimal composition.");
					}
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Ceiling simulate-battle call failed ({e.Message}) for {target.Celestial.Coordinate} - proceeding with the normal search anyway.");
					// PATCH 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 36
				}
			}

			// PATCH 72: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 37
			(Dictionary<Buildables, long> Escort, SimulatorResult Result, bool UsedFallbackTier) PreferLowerLossComposition(
				Dictionary<Buildables, long> minimalComposition, SimulatorResult minimalResult, bool usedFallbackTier) {
				if (ceilingResult == null) {
					return (minimalComposition, minimalResult, usedFallbackTier);
				}
				var fullCeilingComposition = WithFixedBase(ceilingComposition);
				// Ceiling und minimale Loesung koennen identisch sein (z.B. wenn die Ceiling selbst
				// schon die "minimale" ist) - dann gibt's nichts zu vergleichen/loggen.
				bool sameComposition = fullCeilingComposition.Count == minimalComposition.Count &&
					fullCeilingComposition.All(kv => minimalComposition.GetValueOrDefault(kv.Key, 0) == kv.Value);
				if (sameComposition) {
					return (minimalComposition, minimalResult, usedFallbackTier);
				}
				long minimalLosses = minimalResult?.AttackerLosses?.Total() ?? long.MaxValue;
				long ceilingLosses = ceilingResult.AttackerLosses?.Total() ?? long.MaxValue;
				if (ceilingLosses < minimalLosses) {
					// PATCH 116: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 136
					// Vorher wurde hier NUR der geschaetzte Schiffsverlust verglichen - die volle
					// Eskorte am Cap gewann automatisch, sobald sie weniger Verlust hatte, egal wie
					// viel MEHR Sprit dafuer draufgeht (bei EscortShipCaps im 5- oder 6-stelligen
					// Bereich kann das die Flottengroesse verhundertfachen). Das hat in der Praxis
					// dazu gefuehrt, dass an sich profitable Angriffe durch eine unnoetig riesige
					// Eskorte einen Sprit-Overkill bekommen und danach bei der
					// Wirtschaftlichkeitspruefung (predictedCost >= predictedLoot) als "not
					// profitable" geblacklistet wurden - obwohl die minimierte Eskorte den Angriff
					// profitabel gemacht haette. Fix: den Sprit-MEHRBEDARF der vollen Eskorte
					// gegenueber der minimierten grob abschaetzen (gleiche Referenzgeschwindigkeit
					// fuer beide, damit der Vergleich fair ist) und die volle Eskorte nur noch nehmen,
					// wenn dieser Mehrbedarf die eingesparten Schiffsverluste nicht uebersteigt -
					// also ein echter Kosten-Nutzen-Vergleich statt reiner Verlust-Vergleich.
					long lossSavings = minimalLosses - ceilingLosses;
					long extraFuel = 0;
					bool fuelEstimateFailed = false;
					try {
						Ships minimalShips = new Ships().Add(cargoShip, numCargo);
						foreach (var kv in minimalComposition) minimalShips.Add(kv.Key, kv.Value);
						Ships fullShips = new Ships().Add(cargoShip, numCargo);
						foreach (var kv in fullCeilingComposition) fullShips.Add(kv.Key, kv.Value);
						FleetPrediction minimalFuelPrediction = _calculationService.CalcFleetPrediction(origin.Coordinate, target.Celestial.Coordinate, minimalShips, Missions.Attack, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						FleetPrediction fullFuelPrediction = _calculationService.CalcFleetPrediction(origin.Coordinate, target.Celestial.Coordinate, fullShips, Missions.Attack, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						extraFuel = fullFuelPrediction.Fuel - minimalFuelPrediction.Fuel;
					} catch (Exception e) {
						fuelEstimateFailed = true;
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: fuel estimate for PreferLowerLossComposition failed ({e.Message}) - to be safe, comparing by ship loss only (old behavior).");
					}
					// PATCH 139: loss savings weighted by EscortLossWeight
					if (fuelEstimateFailed || extraFuel <= (long) (lossSavings * escortLossWeight)) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: full escort at cap ({string.Join(", ", fullCeilingComposition.Select(kv => $"{kv.Value}x{kv.Key}"))}) has clearly less risk with an estimated {ceilingLosses:N0} loss than the minimized escort ({string.Join(", ", minimalComposition.Select(kv => $"{kv.Value}x{kv.Key}"))}, estimated {minimalLosses:N0}); the extra fuel ({extraFuel:N0}) does not exceed the savings ({lossSavings:N0}) - taking the full escort.");
						return (fullCeilingComposition, ceilingResult, fixedBase.Count > 0);
					}
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: full escort at cap would have less ship loss ({ceilingLosses:N0} instead of {minimalLosses:N0}), but the extra fuel for it ({extraFuel:N0}) exceeds the savings ({lossSavings:N0}) - staying with the minimized escort ({string.Join(", ", minimalComposition.Select(kv => $"{kv.Value}x{kv.Key}"))}).");
					return (minimalComposition, minimalResult, usedFallbackTier);
				}
				return (minimalComposition, minimalResult, usedFallbackTier);
			}

			// PATCH 61, 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 38
			Dictionary<Buildables, long> escort = new(fixedBase);
			SimulatorResult lastResult = null;
			long totalEscort = 0;

			// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 39
			string escortCacheKey = BuildEscortSearchCacheKey(target, cargoShip, numCargo);
			// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 40
			if (fixedBase.Count == 0 && _escortSearchCache.TryGetValue(escortCacheKey, out var cachedProgress)) {
				escort = new Dictionary<Buildables, long>(cachedProgress.Escort);
				lastResult = cachedProgress.LastResult;
				totalEscort = escort.Values.Sum();
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: resuming escort search from cached progress ({string.Join(", ", escort.Select(kv => $"{kv.Value}x{kv.Key}"))}, {totalEscort} ships total) instead of starting from zero.");
			} else if (ceilingResult != null) {
				// PATCH 60: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 41
				int bisectionIterations = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortBisectionIterations", 12);
				double low = 0.0, high = 1.0;
				Dictionary<Buildables, long> bestComposition = ceilingComposition;
				SimulatorResult bestResult = ceilingResult;
				Dictionary<Buildables, long> lastTestedComposition = ceilingComposition;
				// PATCH 124: alle getesteten Ja-Punkte (variabler Anteil, Ergebnis, Kosten) - die
				// Ceiling selbst ist auch ein Kandidat, sofern sie das Verlustlimit einhaelt
				var okPoints = new List<(Dictionary<Buildables, long> Varying, SimulatorResult Result, long Cost)>();
				if (EscortLossOk(WithFixedBase(ceilingComposition), ceilingResult)) {
					okPoints.Add((ceilingComposition, ceilingResult, CompositionCost(WithFixedBase(ceilingComposition), ceilingResult)));
				}
				for (int i = 0; i < bisectionIterations; i++) {
					double mid = (low + high) / 2.0;
					var midComposition = new Dictionary<Buildables, long>();
					foreach (var kv in ceilingComposition) {
						long scaled = (long) (Math.Floor(kv.Value * mid / step) * step);
						if (scaled > 0) {
							midComposition[kv.Key] = scaled;
						}
					}
					// Die Rundung auf EscortShipsStep-Vielfache erschoepft den Suchraum
					// irgendwann - wenn zwei Runden dieselbe Komposition ergeben, ist fuer
					// diese Aufloesung nichts mehr zu gewinnen, weitere Runden wuerden nur
					// denselben Sim-Aufruf wiederholen.
					if (midComposition.Count > 0 && midComposition.OrderBy(kv => kv.Key).SequenceEqual(lastTestedComposition.OrderBy(kv => kv.Key))) {
						break;
					}
					lastTestedComposition = midComposition;
					if (midComposition.Count == 0) {
						// Auf "quasi nichts" herunterskaliert (jeder Typ unter einem Step) -
						// kein Sim-Aufruf noetig, das reicht so gut wie sicher nicht.
						low = mid;
						continue;
					}
					SimulatorResult midResult;
					try {
						Heartbeat();
						midResult = await _ogameService.SimulateBattle(new SimulateBattleRequest {
							// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 42
							Attacker = BuildAttackerFleet(WithFixedBase(midComposition)),
							Defender = defender,
						FleetToDebris = fleetToDebris,
							Simulations = intermediateSimulations,
						});
					} catch (Exception e) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Bisection simulate-battle call failed ({e.Message}) for {target.Celestial.Coordinate} - stopping bisection, continuing with the best point found so far.");
						break;
					}
					double midCargoLossPercent = midResult.CargoLossRate * 100;
					// PATCH 124: "Ja" beruecksichtigt jetzt zusaetzlich das Verlustlimit
					// (MaxEscortLossPercent) - groessere Flotte = weniger Verlust-%, der Ja-Bereich
					// bleibt also monoton und die Bisektion funktioniert weiterhin.
					bool midOk = midResult.AttackerWinRate >= minWinRate && midCargoLossPercent <= maxCargoLossPercent
						&& EscortLossOk(WithFixedBase(midComposition), midResult);
					// PATCH 75: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 43
					if (midOk) {
						high = mid;
						bestComposition = midComposition;
						bestResult = midResult;
						// PATCH 124: jeden Ja-Punkt mit seinen Kosten merken
						long midCost = CompositionCost(WithFixedBase(midComposition), midResult);
						okPoints.Add((midComposition, midResult, midCost));
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: bisection yes at {FormatComposition(WithFixedBase(midComposition))} - loss {(midResult.AttackerLosses?.Total() ?? 0):N0}, cost (loss+fuel@100%) {midCost:N0}.");
					} else {
						low = mid;
					}
				}

				// PATCH 124: statt des kleinsten Ja-Punkts den BILLIGSTEN Ja-Punkt (Verlust + Sprit)
				// nehmen. Kosten sind U-foermig ueber die Flottengroesse (klein = hohe Verluste,
				// gross = viel Sprit) - der kleinste Punkt ist daher oft nicht der beste.
				if (okPoints.Count > 0) {
					var cheapest = okPoints.OrderBy(p => p.Cost).ThenBy(p => p.Varying.Values.Sum()).First();
					bestComposition = cheapest.Varying;
					bestResult = cheapest.Result;
					long bestCost = cheapest.Cost;
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: billigster Ja-Punkt aus {okPoints.Count} Kandidaten: {FormatComposition(WithFixedBase(bestComposition))} (Kosten {bestCost:N0}).");

					// PATCH 124: Feintuning NACH UNTEN ("minus 1 Schiffsklasse"):
					//   Stufe 1: je Schiffstyp probieren, ihn komplett wegzulassen
					//   Stufe 2: je Schiffstyp reihum Schiffe abziehen (Menge startet bei der Haelfte
					//            des Typs, bei Nein wird sie halbiert, bis unter EscortShipsStep)
					// Eine Kuerzung wird nur uebernommen, wenn das Ergebnis weiterhin "Ja" ist UND die
					// Kosten nicht steigen. Begrenzt durch AutoFarm.EscortFineTuneMaxSims.
					int fineTuneMaxSims = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortFineTuneMaxSims", 40);
					int fineTuneSims = 0;
					bool fineTuneAborted = false;
					var current = new Dictionary<Buildables, long>(bestComposition);
					SimulatorResult currentResult = bestResult;
					long currentCost = bestCost;

					async Task<bool> TryReductionAsync(Dictionary<Buildables, long> trial) {
						if (trial.Count == 0 && fixedBase.Count == 0) {
							return false; // ganz ohne Eskorte ist hier kein Thema - dafuer ist die Bisektion da
						}
						SimulatorResult trialResult;
						try {
							Heartbeat();
							fineTuneSims++;
							trialResult = await _ogameService.SimulateBattle(new SimulateBattleRequest {
								Attacker = BuildAttackerFleet(WithFixedBase(trial)),
								Defender = defender,
								FleetToDebris = fleetToDebris,
								Simulations = intermediateSimulations,
							});
						} catch (Exception e) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Feintuning simulate-battle call failed ({e.Message}) for {target.Celestial.Coordinate} - aborting fine tuning, taking the best point so far.");
							fineTuneAborted = true;
							return false;
						}
						bool trialOk = trialResult.AttackerWinRate >= minWinRate && trialResult.CargoLossRate * 100 <= maxCargoLossPercent
							&& EscortLossOk(WithFixedBase(trial), trialResult);
						if (!trialOk) {
							return false;
						}
						long trialCost = CompositionCost(WithFixedBase(trial), trialResult);
						if (trialCost > currentCost) {
							return false;
						}
						current = trial;
						currentResult = trialResult;
						currentCost = trialCost;
						return true;
					}

					// Stufe 1: ganze Schiffsklasse weglassen
					foreach (var type in current.Keys.ToList()) {
						if (fineTuneAborted || fineTuneSims >= fineTuneMaxSims) break;
						if (!current.ContainsKey(type)) continue;
						var trial = new Dictionary<Buildables, long>(current);
						trial.Remove(type);
						if (await TryReductionAsync(trial)) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: Feintuning - {type} komplett weggelassen, weiterhin Ja (Kosten {currentCost:N0}).");
						}
					}

					// Stufe 2: pro Schiffstyp halbierend kuerzen
					long RoundDownToStep(long value) => step > 0 ? (value / step) * step : value;
					var reduceAmount = current.ToDictionary(kv => kv.Key, kv => Math.Max(step, RoundDownToStep(kv.Value / 2)));
					while (!fineTuneAborted && fineTuneSims < fineTuneMaxSims && reduceAmount.Any(kv => kv.Value >= step && current.ContainsKey(kv.Key))) {
						foreach (var type in reduceAmount.Keys.ToList()) {
							if (fineTuneAborted || fineTuneSims >= fineTuneMaxSims) break;
							if (!current.ContainsKey(type) || reduceAmount[type] < step) continue;
							long amount = Math.Min(reduceAmount[type], current[type]);
							var trial = new Dictionary<Buildables, long>(current);
							long newCount = current[type] - amount;
							if (newCount <= 0) {
								trial.Remove(type);
							} else {
								trial[type] = newCount;
							}
							if (await TryReductionAsync(trial)) {
								// Menge beibehalten, aber nicht groesser als der verbleibende Bestand
								reduceAmount[type] = current.ContainsKey(type) ? Math.Min(reduceAmount[type], Math.Max(step, current[type])) : 0;
							} else {
								reduceAmount[type] = RoundDownToStep(amount / 2);
							}
						}
					}

					if (!current.OrderBy(kv => kv.Key).SequenceEqual(bestComposition.OrderBy(kv => kv.Key))) {
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: fine tuning downwards ({fineTuneSims} sims): {FormatComposition(WithFixedBase(bestComposition))} -> {FormatComposition(WithFixedBase(current))}, cost {bestCost:N0} -> {currentCost:N0}.");
					}
					bestComposition = current;
					bestResult = currentResult;
				}

				// PATCH 61: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 44
				escort = WithFixedBase(bestComposition);
				lastResult = bestResult;
				totalEscort = escort.Values.Sum();
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: bisection seeded the search at {string.Join(", ", escort.Select(kv => $"{kv.Value}x{kv.Key}"))} ({totalEscort} ships total, win rate {lastResult.AttackerWinRate:P0}, cargo loss {lastResult.CargoLossRate * 100:F1}%) instead of starting from zero.");
			}

			// PATCH 60, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 45
			// PATCH 124: Verlustlimit auch hier
			if (lastResult != null && lastResult.AttackerWinRate >= minWinRate && lastResult.CargoLossRate * 100 <= maxCargoLossPercent
				&& EscortLossOk(escort, lastResult)) {
				var (accepted, confirmation, callFailed) = await ConfirmCompositionAsync(escort);
				if (callFailed) {
					_escortSearchCache.Remove(escortCacheKey);
					return PreferLowerLossComposition(escort, lastResult, fixedBase.Count > 0);
				}
				if (accepted) {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: bisection result confirmed at full precision ({simulations} runs) without any fine-tuning round needed - win rate {confirmation.AttackerWinRate:P0}, cargo loss {confirmation.CargoLossRate * 100:F1}%.");
					_escortSearchCache.Remove(escortCacheKey);
					return PreferLowerLossComposition(escort, confirmation, fixedBase.Count > 0);
				}
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: reduced-precision bisection result looked sufficient but full-precision confirmation did not ({confirmation.AttackerWinRate:P0} win rate, {confirmation.CargoLossRate * 100:F1}% cargo loss) - continuing with the fine-tuning search.");
				lastResult = confirmation;
			}

			for (int iter = 0; iter < maxIterations; iter++) {
				// PATCH 56: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 46
				long effectiveStep = step;
				if (lastResult != null) {
					double winRateGap = minWinRate - lastResult.AttackerWinRate;
					double lastCargoLossPercent = lastResult.CargoLossRate * 100;
					double cargoLossGapRatio = maxCargoLossPercent > 0 ? lastCargoLossPercent / maxCargoLossPercent : 1;
					double accelerationFactor;
					if (winRateGap > 0.05) {
						accelerationFactor = 8; // not even close to winning yet
					} else if (cargoLossGapRatio > 6) {
						accelerationFactor = 8;
					} else if (cargoLossGapRatio > 3) {
						accelerationFactor = 4;
					} else if (cargoLossGapRatio > 1.5) {
						accelerationFactor = 2;
					} else {
						accelerationFactor = 1; // close to the threshold - fine-tune at the configured step
					}
					effectiveStep = step * (long) accelerationFactor;
				}

				double bestWinRate = -1;
				Buildables bestType = Buildables.Null;
				SimulatorResult bestStepResult = null;

				foreach (Buildables candidateType in candidateTypes) {
					long currentOfType = escort.GetValueOrDefault(candidateType, 0);
					long available = origin.Ships.GetAmount(candidateType);
					if (currentOfType + effectiveStep > available || currentOfType + effectiveStep > shipCaps[candidateType]) {
						continue; // tapped out at the origin, or hit this type's own cap - skip it this round
					}

					// PATCH 46, 58: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 47
					var candidateComposition = new Dictionary<Buildables, long>(escort) {
						[candidateType] = currentOfType + effectiveStep
					};
					var attacker = BuildAttackerFleet(candidateComposition);

					// PATCH 58: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 48
					SimulatorResult result;
					try {
						Heartbeat();
						result = await _ogameService.SimulateBattle(new SimulateBattleRequest {
							Attacker = attacker,
							Defender = defender,
						FleetToDebris = fleetToDebris,
							Simulations = intermediateSimulations,
						});
					} catch (Exception e) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"simulate-battle call failed ({e.Message}) trying {candidateType} for {target.Celestial.Coordinate}.");
						continue;
					}

					// Selection order per round, each tier only decides ties from the one above it:
					//   1. clearly higher win rate (more than NearTieWinRate apart) always wins
					//   2. within NearTieWinRate of each other -> prefer LOWER cargo loss (this is
					//      the actual goal - a technically-higher win rate that costs more dead
					//      cargo ships is not an improvement worth making)
					//   3. win rate AND cargo loss both near-tied -> prefer the less-used type
					//      (diversify), falling back to AutoFarm.EscortShipTypes priority order
					//      only when usage is also tied. Critical for stuck-at-0% situations (hard
					//      defense a given type has no answer for): pure priority-order tie-breaking
					//      previously meant the search kept escalating the same priority type
					//      forever without ever meaningfully trying the others, since "more of a
					//      useless type" and "a bit of a potentially useful type" both look like 0%
					//      until proven otherwise.
					const double NearTieWinRate = 0.02; // 2 percentage points
					double resultCargoLoss = result.CargoLossRate;
					double bestCargoLoss = bestStepResult?.CargoLossRate ?? double.MaxValue;
					bool clearlyBetterWinRate = result.AttackerWinRate > bestWinRate + NearTieWinRate;
					bool winRateNearTied = bestType != Buildables.Null && Math.Abs(result.AttackerWinRate - bestWinRate) <= NearTieWinRate;
					bool betterCargoLossOnNearTie = winRateNearTied && resultCargoLoss < bestCargoLoss - 1e-9
						// still require the win rate to be at least as good, so a near-tie loss
						// on win rate can't be "fixed" by picking something with a worse win rate
						// just because it happens to keep more cargo alive
						&& result.AttackerWinRate >= bestWinRate - NearTieWinRate;
					long candidateUsage = escort.GetValueOrDefault(candidateType, 0);
					long bestTypeUsage = bestType != Buildables.Null ? escort.GetValueOrDefault(bestType, 0) : long.MaxValue;
					bool cargoLossAlsoTied = winRateNearTied && Math.Abs(resultCargoLoss - bestCargoLoss) <= 1e-9;
					bool tiedButLessUsed = cargoLossAlsoTied
						&& (candidateUsage < bestTypeUsage
							|| (candidateUsage == bestTypeUsage && candidateTypes.IndexOf(candidateType) < candidateTypes.IndexOf(bestType)));
					if (clearlyBetterWinRate || betterCargoLossOnNearTie || tiedButLessUsed) {
						bestWinRate = result.AttackerWinRate;
						bestType = candidateType;
						bestStepResult = result;
					}
				}

				if (bestType == Buildables.Null) {
					break; // no candidate type has supply left at the origin - nothing more to try
				}
				escort[bestType] = escort.GetValueOrDefault(bestType, 0) + effectiveStep;
				totalEscort += effectiveStep;
				lastResult = bestStepResult;
				double cargoLossPercent = bestStepResult.CargoLossRate * 100;
				// PATCH 75: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 49

				// PATCH 124: Verlustlimit (MaxEscortLossPercent) gehoert mit zum "Ja"
				if (bestWinRate >= minWinRate && cargoLossPercent <= maxCargoLossPercent && EscortLossOk(escort, bestStepResult)) {
					// PATCH 58: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 50
					var (accepted, confirmation, callFailed) = await ConfirmCompositionAsync(escort);
					if (callFailed) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Accepting the reduced-precision result instead for {target.Celestial.Coordinate}.");
						_escortSearchCache.Remove(escortCacheKey);
						return PreferLowerLossComposition(escort, lastResult, fixedBase.Count > 0);
					}
					double confirmationCargoLossPercent = confirmation.CargoLossRate * 100;
					if (accepted) {
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: confirmed at full precision ({simulations} runs) - win rate {confirmation.AttackerWinRate:P0}, cargo loss {confirmationCargoLossPercent:F1}%.");
						_escortSearchCache.Remove(escortCacheKey);
						return PreferLowerLossComposition(escort, confirmation, fixedBase.Count > 0);
					}
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: reduced-precision estimate looked sufficient but full-precision confirmation did not ({confirmation.AttackerWinRate:P0} win rate, {confirmationCargoLossPercent:F1}% cargo loss) - continuing search.");
					lastResult = confirmation;
				}
			}

			if (escort.Count > 0) {
				// PATCH 59, 57: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 51
				_escortSearchCache[escortCacheKey] = new EscortSearchProgress {
					Escort = new Dictionary<Buildables, long>(escort),
					LastResult = lastResult,
					CachedAtUtc = DateTime.UtcNow,
				};
				// PATCH 95: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 52
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: escort search exhausted without reaching AutoFarm.MinWinRate/MaxCargoLossPercent within the configured caps/iterations - caching progress ({string.Join(", ", escort.Select(kv => $"{kv.Value}x{kv.Key}"))}) for a possible retry.");
			}
			return null;
		}

		// Builds the defender payload for AutoFarmDetermineEscort from a real espionage report.
		// Split out since the greedy search calls simulate-battle many times per target but the
		// defender side never changes between those calls - build it once, reuse it.
		private SimulatorDefender AutoFarmBuildSimulatorDefender(EspionageReport report) {
			// Tech fallback: if the report doesn't carry researches (insufficient spy strength),
			// assume the target's own current tech rather than 0/0/0 - "inactive" is not a
			// guarantee of unresearched, an ex-active player's defenses keep their full tech
			// bonus regardless of how long they've been offline.
			int defWeapon = report.HasResearchesInformation ? report.WeaponsTechnology.GetValueOrDefault() : _tbotInstance.UserData.researches.WeaponsTechnology;
			int defShield = report.HasResearchesInformation ? report.ShieldingTechnology.GetValueOrDefault() : _tbotInstance.UserData.researches.ShieldingTechnology;
			int defArmour = report.HasResearchesInformation ? report.ArmourTechnology.GetValueOrDefault() : _tbotInstance.UserData.researches.ArmourTechnology;

			// PATCH 64: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 53
			int generalClassCombatResearchBonus = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "GeneralClassCombatResearchBonus", 2);
			if (report.CharacterClass == CharacterClass.General && generalClassCombatResearchBonus != 0) {
				int rawWeapon = defWeapon, rawShield = defShield, rawArmour = defArmour;
				defWeapon += generalClassCombatResearchBonus;
				defShield += generalClassCombatResearchBonus;
				defArmour += generalClassCombatResearchBonus;
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{report.Coordinate}: defender is class General - tech levels for the simulation raised from {rawWeapon}/{rawShield}/{rawArmour} to effectively {defWeapon}/{defShield}/{defArmour} (+{generalClassCombatResearchBonus} levels class bonus).");
			}

			// PATCH 137: see AutoFarmWorker.PATCHNOTES.md, section 155 - safety margin on the target's combat techs.
			// The espionage report only shows research levels, NO lifeform combat bonuses - a target with
			// such bonuses is otherwise simulated too weak (more losses than predicted). Default 0 = off.
			int targetCombatTechBonus = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "TargetCombatTechBonus", 0);
			if (targetCombatTechBonus > 0) {
				defWeapon += targetCombatTechBonus;
				defShield += targetCombatTechBonus;
				defArmour += targetCombatTechBonus;
				_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{report.Coordinate}: TargetCombatTechBonus +{targetCombatTechBonus} - simulating defender with {defWeapon}/{defShield}/{defArmour}.");
			}

			return new SimulatorDefender {
				Weapon = defWeapon,
				Shield = defShield,
				Armour = defArmour,
				Metal = report.Metal,
				Crystal = report.Crystal,
				Deuterium = report.Deuterium,
				LightFighter = report.LightFighter.GetValueOrDefault(),
				HeavyFighter = report.HeavyFighter.GetValueOrDefault(),
				Cruiser = report.Cruiser.GetValueOrDefault(),
				Battleship = report.Battleship.GetValueOrDefault(),
				Battlecruiser = report.Battlecruiser.GetValueOrDefault(),
				Bomber = report.Bomber.GetValueOrDefault(),
				Destroyer = report.Destroyer.GetValueOrDefault(),
				Deathstar = report.Deathstar.GetValueOrDefault(),
				SmallCargo = report.SmallCargo.GetValueOrDefault(),
				LargeCargo = report.LargeCargo.GetValueOrDefault(),
				ColonyShip = report.ColonyShip.GetValueOrDefault(),
				Recycler = report.Recycler.GetValueOrDefault(),
				EspionageProbe = report.EspionageProbe.GetValueOrDefault(),
				SolarSatellite = report.SolarSatellite.GetValueOrDefault(),
				Crawler = report.Crawler.GetValueOrDefault(),
				Reaper = report.Reaper.GetValueOrDefault(),
				Pathfinder = report.Pathfinder.GetValueOrDefault(),
				RocketLauncher = report.RocketLauncher.GetValueOrDefault(),
				LightLaser = report.LightLaser.GetValueOrDefault(),
				HeavyLaser = report.HeavyLaser.GetValueOrDefault(),
				GaussCannon = report.GaussCannon.GetValueOrDefault(),
				IonCannon = report.IonCannon.GetValueOrDefault(),
				PlasmaTurret = report.PlasmaTurret.GetValueOrDefault(),
				SmallShieldDome = report.SmallShieldDome.GetValueOrDefault(),
				LargeShieldDome = report.LargeShieldDome.GetValueOrDefault(),
				AntiBallisticMissiles = report.AntiBallisticMissiles.GetValueOrDefault(),
				InterplanetaryMissiles = report.InterplanetaryMissiles.GetValueOrDefault(),
			};
		}

		// PATCH 74, 47: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 54

		// PATCH 136: see AutoFarmWorker.PATCHNOTES.md, section 154 - next cycle timing in FillSlots mode.
		// - scan finished and nothing open -> false (regular CheckIntervalMin/Max).
		// - AutoFarm slots free and nothing waiting -> continue scanning after a few seconds.
		// - slots full or targets waiting for a slot -> until the next return of an own AutoFarm fleet
		//   (probes or attack) + buffer, at most CheckIntervalMax.
		private bool TryGetFillSlotsInterval(bool finishedFullScan, out long intervalMs) {
			intervalMs = 0;
			try {
				int pending = _tbotInstance.UserData.farmTargets.Count(t => t.State == FarmState.AttackPending);
				int probesOut = _tbotInstance.UserData.farmTargets.Count(t => t.State == FarmState.ProbesSent);
				if (finishedFullScan && pending == 0 && probesOut == 0)
					return false;

				long maxMs = (long) RandomizeHelper.CalcRandomInterval(
					(int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMax,
					(int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMax);
				if (maxMs <= 0)
					maxMs = 15 * 60 * 1000;

				Buildables cargoType = GetConfiguredCargoType();
				var budget = GetCurrentSlotBudget(cargoType);
				if (budget.AvailableSlots > 0 && pending == 0 && !finishedFullScan) {
					intervalMs = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"FillSlots: {budget.AvailableSlots} slot(s) still free - continuing scan in {TimeSpan.FromMilliseconds(intervalMs)}.");
					return true;
				}

				var nextReturnSec = (_tbotInstance.UserData.fleets ?? new List<Fleet>())
					.Where(f => AutoFarmSlotPlanner.IsOwnedFleet(f, cargoType) && (f.BackIn ?? 0) > 0)
					.Select(f => (long) (f.BackIn ?? 0))
					.DefaultIfEmpty(0)
					.Min();
				if (nextReturnSec <= 0) {
					intervalMs = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				} else {
					intervalMs = Math.Min(maxMs, nextReturnSec * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				}
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"FillSlots: slots busy ({budget.OwnedSlots}/{budget.MaxSlots}), {pending} target(s) waiting - next attempt in {TimeSpan.FromMilliseconds(intervalMs)} (next AutoFarm fleet return).");
				return true;
			} catch {
				return false;
			}
		}		private AutoFarmSlotBudget GetCurrentSlotBudget(Buildables cargoType) {
			return AutoFarmSlotPlanner.Calculate(
				(int) _tbotInstance.InstanceSettings.AutoFarm.MaxSlots,
				(int) (_tbotInstance.UserData.slots?.Free ?? 0),
				(int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree,
				_tbotInstance.UserData.fleets,
				cargoType);
		}

		private class SpyOriginResult {
			public SpyOriginResult(Celestial origin, int freeSlots) {
				Origin = origin;
				BackIn = 0;
				FreeSlots = freeSlots;
			}

			public SpyOriginResult(Celestial origin, int backin, int freeSlots) {
				Origin = origin;
				BackIn = backin;
				FreeSlots = freeSlots;
			}
			public Celestial Origin { get; set; }
			public int BackIn { get; set; }
			public int FreeSlots { get; set; }
		}

		private async Task<SpyOriginResult> GetBestOrigin(List<Celestial> closestCelestials,
			Dictionary<int, long> celestialProbes,
			FarmTarget target,
			int neededProbes,
			int slotsToLeaveFree,
			int freeSlots) {
			SpyOriginResult bestOrigin = new SpyOriginResult(closestCelestials.First(), int.MaxValue, freeSlots);
			foreach (var closest in closestCelestials) {
				var tempCelestial = await _tbotOgameBridge.UpdatePlanet(closest, UpdateTypes.Ships);
				celestialProbes.Remove(closest.ID);
				celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);

				if (celestialProbes[closest.ID] >= neededProbes) {
					bestOrigin = new SpyOriginResult(closest, freeSlots);
					break;
				}

				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

				if (freeSlots <= slotsToLeaveFree) {
					var espionageMissions = _calculationService.GetMissionsInProgress(closest.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets);
					if (espionageMissions.Any()) {
						var returningProbes = espionageMissions.Sum(f => f.Ships.EspionageProbe);
						if (celestialProbes[closest.ID] + returningProbes >= neededProbes) {
							var returningFleets = espionageMissions.OrderBy(f => f.BackIn).ToArray();
							long probesCount = 0;
							for (int i = 0; i < returningFleets.Length; i++) {
								probesCount += returningFleets[i].Ships.EspionageProbe;
								if (probesCount >= neededProbes) {
									if (bestOrigin.BackIn > returningFleets[i].BackIn)
										bestOrigin = new SpyOriginResult(closest, returningFleets[i].BackIn ?? int.MaxValue, freeSlots);
									break;
								}
							}
						}
					}
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Cannot spy {target.Celestial.Coordinate.ToString()} from {closest.Coordinate.ToString()}, insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
					if (bestOrigin.BackIn < int.MaxValue)
						continue;

					tempCelestial = await _tbotOgameBridge.UpdatePlanet(closest, UpdateTypes.Constructions);
					if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
						Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
						continue;
					}
					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Productions);
					if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
						continue;
					}

					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);
					var buildProbes = neededProbes - celestialProbes[closest.ID];
					var cost = _calculationService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
					if (tempCelestial.Resources.IsEnoughFor(cost)) {
						bestOrigin = new SpyOriginResult(closest, int.MaxValue, freeSlots);
					}
				}
			}

			if (bestOrigin.BackIn != 0) {
				if (bestOrigin.BackIn != int.MaxValue) {
					int interval = (int) ((1000 * bestOrigin.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));
					if (interval < 0)
						interval = 1000;
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Not enough free slots {freeSlots}/{slotsToLeaveFree}. Waiting {TimeSpan.FromMilliseconds(interval)} for probes to return...");
					Heartbeat();
					await Task.Delay(interval, _ct);
					bestOrigin.Origin = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Ships);
					bestOrigin.FreeSlots++;
				}
			}

			return bestOrigin;
		}

		// PATCH 74: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 55
		private async Task<int> WaitForFreeSlots(int freeSlots, int slotsToLeaveFree, int neededSlots = 1) {
			if (freeSlots - slotsToLeaveFree < neededSlots) {
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				freeSlots = _tbotInstance.UserData.slots.Free;
			}

			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			while (freeSlots - slotsToLeaveFree < neededSlots) {
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				if (_tbotInstance.UserData.fleets.Any()) {
					int interval = (int) ((1000 * _tbotInstance.UserData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Waiting {TimeSpan.FromMilliseconds(interval)} for fleet to return...");
					Heartbeat();
					await Task.Delay(interval, _ct);
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					freeSlots = _tbotInstance.UserData.slots.Free;
				} else {
					_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
					throw new Exception("No fleet slots available and no fleets returning!");
				}
			}
			return freeSlots;
		}

		protected override async Task Execute() {
			await RefreshAllianceWhitelistAsync();
			// PATCH 126: geplante Recycler-Starts nach einem Neustart wiederherstellen (einmalig)
			if (!_pendingRecyclersReconciled) {
				_pendingRecyclersReconciled = true;
				try {
					ReconcilePendingRecyclers();
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Restoring scheduled recyclers failed: {e.Message}");
				}
			}
			bool stop = false;
			bool stopAfterFullScan = false;
			bool finishedFullScan = false;
			int skippedEmptySystems = 0;
			// PATCH 102-Bugfix: muessen VOR dem aeusseren try deklariert sein (nicht nur vor dem
			// inneren do-while) - der finally-Block dieses try (siehe Zyklus-Umplanung ganz unten)
			// braucht beide Werte ebenfalls, und try/catch/finally haben in C# je eigenen Scope;
			// innerhalb des try deklariert waeren sie im finally schlicht nicht sichtbar (CS0103,
			// so beim ersten Deploy von PATCH 102 tatsaechlich passiert).
			bool sequentialModeActive = false;
			bool sequentialModeProbeInFlight = false;
			// PATCH 103: konfigurierbare Anzahl gleichzeitig "in Bearbeitung" erlaubter Spionage-
			// Angriffe im Sequential-Modus statt fest 1 - siehe AutoFarm.SequentialModeProbesPerCycle
			// (Default 1, unveraendertes Verhalten). Hoehere Werte beschleunigen den gefuehlten
			// Scan-Fortschritt (mehrere Ziele parallel in der Pipeline statt strikt eins nach dem
			// anderen), opfern dafuer aber etwas von der urspruenglichen Garantie "nie weiter
			// vorscannen als die Angriffskapazitaet hergibt" (PATCH 99/100/101) - je hoeher der Wert,
			// desto naeher am alten Batch-Verhalten.
			int sequentialModeProbesPerCycle = 1;
			// PATCH 136: see AutoFarmWorker.PATCHNOTES.md, section 154 - "fill slots" mode.
			// Must be declared before the try like sequentialModeActive - the finally block uses it to schedule the next cycle.
			bool fillSlotsActive = false;
			try {
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Running autofarm...");
				LoadPersistedTargets();
				// PATCH 106: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 126
				DeduplicateFarmTargets();
				EnsureActiveScanLoopStarted();
				stopAfterFullScan = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "StopAfterFullScan")
					&& (bool)_tbotInstance.InstanceSettings.AutoFarm.StopAfterFullScan;
				if ((bool) _tbotInstance.InstanceSettings.AutoFarm.Active) {				
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					
					int freeSlots = _tbotInstance.UserData.slots.Free;
					int slotsToLeaveFree = (int) _tbotInstance.InstanceSettings.AutoFarm.SlotsToLeaveFree;
					if (freeSlots <= slotsToLeaveFree) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to start auto farm, no slots available");
						return;
					}

					int numProbed = 0;
					bool stopAutoFarm = false;
					int interleavePass = 0;
					const int maxInterleavePasses = 20;
					bool keepInterleaving;
					// PATCH 108: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 128
					// Zaehlt die in DIESEM Zyklus bereits gescannten Systeme (unabhaengig davon, ob
					// darin Ziele gefunden wurden) - Grundlage fuer AutoFarm.ScanBlockSize weiter unten.
					int systemsScannedThisPass = 0;
					// PATCH 68: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 56
					do {
					interleavePass++;
					numProbed = 0;
					systemsScannedThisPass = 0;
					stopAutoFarm = false;
					_sequentialModeNextArrivalSeconds = null;
					_sequentialModeProbeSentAtUtc = null;
					// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 57
					sequentialModeActive = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "SequentialMode") &&
						(bool) _tbotInstance.InstanceSettings.AutoFarm.SequentialMode;
					// PATCH 103: siehe Kommentar bei der Feld-Deklaration oben.
					sequentialModeProbesPerCycle = sequentialModeActive
						? Math.Max(1, SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "SequentialModeProbesPerCycle", 1))
						: 1;
					// PATCH 108: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 128
					int scanBlockSize = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "ScanBlockSize", 0);
					sequentialModeProbeInFlight = sequentialModeActive &&
						_tbotInstance.UserData.farmTargets.Count(t => t.State == FarmState.ProbesSent) >= sequentialModeProbesPerCycle;
					// PATCH 136: see AutoFarmWorker.PATCHNOTES.md, section 154
					// passProbeLimit replaces the former TargetsProbedBeforeAttack check (0 = unlimited).
					// FillSlots off -> exactly as before. FillSlots on -> as many probes as AutoFarm slots are free
					// minus targets already ready to attack (each still needs an attack slot);
					// TargetsProbedBeforeAttack > 0 remains the upper limit.
					int configuredProbeLimit = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "TargetsProbedBeforeAttack")
						? (int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack : 0;
					int passProbeLimit = configuredProbeLimit;
					fillSlotsActive = !sequentialModeActive && SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FillSlots", false);
					DateTime? fillSlotsLastProbeArrivalUtc = null;
					try {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						// PATCH 140: evaluate losses of running/returning AutoFarm attacks
						TrackFleetLosses();
						if (fillSlotsActive) {
							_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
							var fillBudget = GetCurrentSlotBudget(GetConfiguredCargoType());
							int fillPending = _tbotInstance.UserData.farmTargets.Count(t => t.State == FarmState.AttackPending);
							int fillLimit = Math.Max(1, fillBudget.AvailableSlots - fillPending);
							passProbeLimit = configuredProbeLimit > 0 ? Math.Min(configuredProbeLimit, fillLimit) : fillLimit;
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"FillSlots: pass {interleavePass} - {fillBudget.AvailableSlots} AutoFarm slot(s) free, {fillPending} target(s) ready to attack -> up to {passProbeLimit} probe(s).");
						}
						await PruneOldReports(_tbotInstance.UserData.fleets);
						PruneEscortSearchCache();
						PersistTargets();

						var farmOrigins = GetFarmOrigins();
						var celestialProbes = GetCachedCelestialProbes(farmOrigins);
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

						// PATCH 110: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 130
						// "Block 11": statt die ActiveScan-Entdeckungs-Queue JEDEN Zyklus sofort und mit
						// Prioritaet abzuarbeiten (bisheriges Verhalten seit PATCH 82), wird sie nur noch
						// abgearbeitet, wenn entweder genug neue Ziele aufgelaufen sind
						// (AutoFarm.Block11MinCount) ODER seit der letzten Abarbeitung genug Zeit
						// vergangen ist (AutoFarm.Block11MaxAgeHours) - je nachdem, was zuerst eintritt.
						// Sind BEIDE Einstellungen 0/nicht gesetzt, bleibt das alte Verhalten unveraendert
						// (immer sofort abarbeiten - Default, volle Abwaertskompatibilitaet). Grund:
						// Nutzer-Wunsch, den normalen Block-Scan "in Ruhe" durchlaufen zu lassen, statt
						// ihn staendig fuer einzelne neue ActiveScan-Funde zu unterbrechen.
						int block11MinCount = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "Block11MinCount", 0);
						double block11MaxAgeHours = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "Block11MaxAgeHours", 0.0);
						bool block11ThresholdReached = block11MinCount > 0 && _activeScanDiscoveries.Count >= block11MinCount;
						bool block11TimeoutReached = block11MaxAgeHours > 0 && (DateTime.UtcNow - _lastActiveScanDrainUtc).TotalHours >= block11MaxAgeHours;
						bool block11LegacyImmediate = block11MinCount <= 0 && block11MaxAgeHours <= 0;
						bool shouldDrainActiveScan = block11LegacyImmediate || block11ThresholdReached || block11TimeoutReached;
						if (!shouldDrainActiveScan && !_activeScanDiscoveries.IsEmpty) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
								$"[Block 11] {_activeScanDiscoveries.Count} new target(s) still waiting (threshold {block11MinCount}, last drain {(DateTime.UtcNow - _lastActiveScanDrainUtc).TotalHours:F1}h ago of max. {block11MaxAgeHours}h) - the normal scan continues undisturbed.");
						}
						if (shouldDrainActiveScan && !_activeScanDiscoveries.IsEmpty)
							_lastActiveScanDrainUtc = DateTime.UtcNow;

						// PATCH 82, 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 58
						while (shouldDrainActiveScan && (!sequentialModeActive || (numProbed < sequentialModeProbesPerCycle && !sequentialModeProbeInFlight)) && _activeScanDiscoveries.TryDequeue(out Coordinate discoveredCoord)) {
							try {
								if (_tbotInstance.UserData.farmTargets.Any(t => t.HasCoords(discoveredCoord)))
									continue;

								GalaxyInfo freshInfo = await _ogameService.GetGalaxyInfo(discoveredCoord.Galaxy, discoveredCoord.System);
								var freshPlanet = freshInfo?.Planets?.FirstOrDefault(p => p != null && p.Coordinate.IsSame(discoveredCoord));
								if (freshPlanet == null || !AutoFarmSystemPolicy.IsEligibleTarget(freshPlanet)) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
										$"[ActiveScan] {discoveredCoord} is no longer suitable on re-check (possibly active again) - skipping.");
									continue;
								}
								if (ShouldExcludePlanet(freshPlanet))
									continue;

								var quickTarget = GetFarmTarget(freshPlanet);
								if (quickTarget == null)
									continue;

								_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
								_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
								var quickSlotBudget = GetCurrentSlotBudget(GetConfiguredCargoType());
								if (quickSlotBudget.AvailableSlots <= 0) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
										$"[ActiveScan] No free slot for prioritized target {discoveredCoord} - the normal scan will take it later.");
									continue;
								}

								List<Celestial> quickClosestOrigins = farmOrigins
									// PATCH 131: das zweite OrderBy ersetzte die Mond-Praeferenz bisher komplett (LINQ) -
									// effektiv wurde nur nach Distanz sortiert. Das bleibt so (kuerzeste Flugzeit zuerst),
									// der Mond entscheidet jetzt aber echt bei Gleichstand (gleiche Distanz -> Mond zuerst).
									.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, quickTarget.Celestial.Coordinate, _tbotInstance.UserData.serverData))
									.ThenByDescending(p => p.Coordinate.Type == Celestials.Moon)
									.ToList();
								if (!quickClosestOrigins.Any())
									continue;

								int quickNeededProbes = GetNeededProbes(quickTarget);
								var quickBestOrigin = await GetBestOrigin(quickClosestOrigins, celestialProbes, quickTarget, quickNeededProbes, slotsToLeaveFree, freeSlots);
								freeSlots = quickBestOrigin.FreeSlots;

								if (!celestialProbes.TryGetValue(quickBestOrigin.Origin.ID, out long quickHaveProbes) || quickHaveProbes < quickNeededProbes) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
										$"[ActiveScan] Not enough probes for prioritized target {discoveredCoord} ({quickHaveProbes}/{quickNeededProbes}) - the normal scan will take it later.");
									continue;
								}

								Ships quickShips = new();
								quickShips.Add(Buildables.EspionageProbe, quickNeededProbes);

								// PATCH 102: siehe Kommentar bei der gleichen Vorhersage im normalen Scan-
								// Pfad weiter unten - ohne das wuerde ein ueber diesen ActiveScan-Pfad
								// gesendeter Spionage-Angriff im Sequential-Modus keine exakte Ankunftszeit
								// liefern und der naechste Zyklus faellt auf den (langsameren) Kurz-Retry-
								// Fallback zurueck statt direkt auf die Ankunft zu takten.
								FleetPrediction quickProbePrediction = null;
								// PATCH 136: prediction also in FillSlots mode
								if (sequentialModeActive || fillSlotsActive) {
									quickBestOrigin.Origin = await _tbotOgameBridge.UpdatePlanet(quickBestOrigin.Origin, UpdateTypes.LFBonuses);
									quickProbePrediction = _calculationService.CalcFleetPrediction(quickBestOrigin.Origin.Coordinate, quickTarget.Celestial.Coordinate, quickShips, Missions.Spy, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, quickBestOrigin.Origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
								}

								var quickFleetId = await _fleetScheduler.SendFleet(quickBestOrigin.Origin, quickShips, quickTarget.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);

								if (quickFleetId > (int) SendFleetCode.GenericError) {
									freeSlots--;
									numProbed++;
									celestialProbes[quickBestOrigin.Origin.ID] -= quickNeededProbes;
									// PATCH 136: remember the latest probe arrival of this pass
									if (fillSlotsActive && quickProbePrediction != null) {
										DateTime quickArrival136 = DateTime.UtcNow.AddSeconds(quickProbePrediction.Time);
										if (!fillSlotsLastProbeArrivalUtc.HasValue || quickArrival136 > fillSlotsLastProbeArrivalUtc.Value)
											fillSlotsLastProbeArrivalUtc = quickArrival136;
									}
									if (quickProbePrediction != null && sequentialModeActive) {
										_sequentialModeNextArrivalSeconds = (long) quickProbePrediction.Time;
										_sequentialModeProbeSentAtUtc = DateTime.UtcNow;
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Sequential mode: espionage attack ({quickNeededProbes}x probe, ActiveScan priority) on its way to {quickTarget.Celestial.Coordinate}, arrival in ~{TimeSpan.FromSeconds(quickProbePrediction.Time)}.");
									}
									_tbotInstance.UserData.farmTargets.Remove(quickTarget);
									quickTarget.State = FarmState.ProbesSent;
									// PATCH 82: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 59
									_tbotInstance.UserData.farmTargets.Insert(0, quickTarget);
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
										$"[ActiveScan] Prioritized target {discoveredCoord} checked immediately with an espionage probe.");
								}
							} catch (Exception ex) {
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
									$"[ActiveScan] Error while handling prioritized target {discoveredCoord}: {ex.Message}");
							}
						}

						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Detecting farm targets...");

						// PATCH 105: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 125
						var scanRanges = GetEffectiveScanRanges();

						var allScanRanges = scanRanges;
						int totalSystemsAcrossAllGalaxies = allScanRanges.Sum(r => (int)r.EndSystem - (int)r.StartSystem + 1);
						int instanceHash = Math.Abs(_tbotInstance.InstanceAlias.GetHashCode());
						int minSpacing = 499;
						int numSlotsGlobal = Math.Max(1, totalSystemsAcrossAllGalaxies / minSpacing);
						int globalSlotIndex = instanceHash % numSlotsGlobal;
						int globalOffset = globalSlotIndex * minSpacing;

						int targetGalaxy = 1;
						int targetSystem = 1;
						int remainingOffset = globalOffset;
						foreach (var r in allScanRanges) {
							int systemsInRange = (int)r.EndSystem - (int)r.StartSystem + 1;
							if (remainingOffset < systemsInRange) {
								targetGalaxy = (int)r.Galaxy;
								targetSystem = (int)r.StartSystem + remainingOffset;
								break;
							}
							remainingOffset -= systemsInRange;
						}

						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Instance: {_tbotInstance.InstanceAlias}");
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Total systems across all galaxies: {totalSystemsAcrossAllGalaxies}");
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Num slots globally: {numSlotsGlobal}");
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Global slot index: {globalSlotIndex}");
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Global offset: {globalOffset} systems");
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"[GLOBAL SPACING] Target Galaxy: {targetGalaxy}, Target System: {targetSystem}");

						// PATCH 122: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 142 - Nutzer-Wunsch:
						// AutoFarm.ScanRange-Eintraege werden jetzt in KONFIGURIERTER Reihenfolge (also
						// der Reihenfolge im JSON-Array) abgearbeitet statt aufsteigend nach Galaxy/
						// StartSystem sortiert. Bisher wurde bei z.B. Eintrag1=Galaxy2, Eintrag2=Galaxy1
						// trotzdem zuerst Galaxy1 abgearbeitet (weil 1 < 2), was der Nutzer nicht
						// erwartet hatte. Jeder Eintrag bekommt weiterhin seinen eigenen Cursor-Index
						// (unveraendert ueber `rangeIndex`/`SaveScanCursor`); nach dem letzten Eintrag
						// der Liste springt der Cursor wie bisher zyklisch zurueck auf den ERSTEN Eintrag
						// (siehe `(startRangeIndex + rangeOffset) % orderedRanges.Count` weiter unten -
						// diese Modulo-Logik war bereits korrekt und musste nicht angepasst werden).
						var orderedRanges = scanRanges.ToList();

						if (!orderedRanges.Any()) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "No scan ranges match galaxies with planets. Skipping AutoFarm.");
							stopAutoFarm = true;
						}

						// PATCH 99: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 60
						if (!stopAutoFarm) {
							int existingAttackPendingCount = _tbotInstance.UserData.farmTargets.Count(t => t.State == FarmState.AttackPending);
							var preScanSlotBudget = GetCurrentSlotBudget(GetConfiguredCargoType());
							if (existingAttackPendingCount >= preScanSlotBudget.AvailableSlots) {
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
									$"AutoFarm attack slots full: {existingAttackPendingCount} attack-ready target(s) for {preScanSlotBudget.AvailableSlots} free of {preScanSlotBudget.MaxSlots} slot(s) ({preScanSlotBudget.OwnedSlots} already in use) - skipping further scanning this cycle so no probes are sent needlessly early (and outdated by the time of the attack).");
								stopAutoFarm = true;
							}
						}

						// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 61
						if (sequentialModeActive && !stopAutoFarm && sequentialModeProbeInFlight) {
							// Noch ein Bericht einer zuvor verschickten Sonde ausstehend (State bleibt
							// ProbesSent, bis AutoFarmProcessReports() ihn verarbeitet hat) - keine neue
							// Sonde verschicken, bevor dieser Bericht ausgewertet (und ggf. angegriffen)
							// wurde. Reihenfolge innerhalb EINES Execute()-Zyklus ist sonst Scan->Verarbeiten->
							// Angriff - ohne diese Bremse wuerde also schon die naechste Sonde raus, BEVOR
							// der gerade eingetroffene Bericht der vorigen ueberhaupt verarbeitet ist.
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, "Sequential mode: report of the previous espionage attack not evaluated yet - skipping the scan this cycle.");
							stopAutoFarm = true;
						}

						int startRangeIndex = _tbotInstance.UserData.autoFarmLastRangeIndex;
						if (startRangeIndex < 0 || startRangeIndex >= orderedRanges.Count) {
							startRangeIndex = 0;
						}
						if (_tbotInstance.UserData.autoFarmLastGalaxy == 0 && _tbotInstance.UserData.autoFarmLastSystem == 0) {
							// PATCH 134: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 152 - Global Spacing verteilt
							// MEHRERE Bot-Instanzen auf verschiedene Startpunkte. Bei nur einer Instanz hat es nach
							// jedem vollen Durchlauf den Start auf einen per Instanzname "zufaelligen" ScanRange-Eintrag
							// verschoben (z.B. Galaxie 4 statt Eintrag 1). Jetzt nur noch mit AutoFarm.GlobalSpacing=true,
							// sonst startet ein neuer Durchlauf immer beim ersten Eintrag.
							bool globalSpacingEnabled = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "GlobalSpacing", false);
							if (globalSpacingEnabled) {
								int idx = orderedRanges.FindIndex(r => (int)r.Galaxy == targetGalaxy);
								if (idx >= 0) startRangeIndex = idx;
							} else {
								startRangeIndex = 0;
							}
							_tbotInstance.UserData.autoFarmLastRangeIndex = startRangeIndex;
						}

						for (int rangeOffset = 0; rangeOffset < orderedRanges.Count; rangeOffset++) {
							int rangeIndex = (startRangeIndex + rangeOffset) % orderedRanges.Count;
							if (stopAutoFarm)
								break;
							// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 62
							if ((passProbeLimit > 0 && numProbed >= passProbeLimit)
								|| (sequentialModeActive && numProbed >= sequentialModeProbesPerCycle)) {
								break;
							}

							var range = orderedRanges[rangeIndex];

							int galaxy = (int) range.Galaxy;
							int originalStartSystem = (int) range.StartSystem;
							int endSystem = (int) range.EndSystem;
							int startSystem = originalStartSystem;
							// PATCH 125: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 145 - Resume nur noch
							// fuer den Range-Eintrag, in dem der Cursor tatsaechlich gespeichert wurde
							// (rangeOffset == 0 = rangeIndex == gespeicherter autoFarmLastRangeIndex).
							// Vorher reichte "gleiche Galaxie + System im Bereich" - bei zwei Eintraegen
							// in derselben Galaxie konnte so faelschlich der falsche Eintrag resumen.
							bool resumeThisRange = rangeOffset == 0 &&
								_tbotInstance.UserData.autoFarmLastGalaxy == galaxy &&
								_tbotInstance.UserData.autoFarmLastSystem >= originalStartSystem &&
								_tbotInstance.UserData.autoFarmLastSystem <= endSystem;
							if (resumeThisRange) {
								startSystem = _tbotInstance.UserData.autoFarmLastSystem;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
									$"Resuming scan from Galaxy {galaxy} System {startSystem} (ScanRange entry {rangeIndex + 1}/{orderedRanges.Count})");
							} else {
								startSystem = originalStartSystem;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
									$"[START] Galaxy {galaxy} - ScanRange entry {rangeIndex + 1}/{orderedRanges.Count} starts over");
							}

							// PATCH 104: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 65
							bool distanceBasedScanOrder = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "DistanceBasedScanOrder") &&
								(bool) _tbotInstance.InstanceSettings.AutoFarm.DistanceBasedScanOrder;
							IReadOnlyList<int> systemsToScan;
							if (distanceBasedScanOrder) {
								int homeSystemForGalaxy = GetHomeSystemForGalaxy(galaxy);
								int totalSystemsForDistance = (int) _tbotInstance.UserData.serverData.Systems;
								systemsToScan = AutoFarmScanPlanner.EnumerateSystemsByDistance(originalStartSystem, endSystem, resumeThisRange ? startSystem : (int?) null, homeSystemForGalaxy, totalSystemsForDistance);
							} else {
								systemsToScan = AutoFarmScanPlanner.EnumerateSystems(originalStartSystem, endSystem, resumeThisRange ? startSystem : (int?) null);
							}

							foreach (int system in systemsToScan) {

								// PATCH 92: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 63
								Heartbeat();

								if (stopAutoFarm)
									break;
								// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 64
								if ((passProbeLimit > 0 && numProbed >= passProbeLimit)
									|| (sequentialModeActive && numProbed >= sequentialModeProbesPerCycle)) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
									SaveScanCursor(rangeIndex, galaxy, system);
									stopAutoFarm = true;
									break;
								}
								// PATCH 108: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 128
								// Zusaetzliche Bremse unabhaengig von gefundenen/sondierten Zielen: nach
								// AutoFarm.ScanBlockSize gescannten Systemen (0 = deaktiviert, altes
								// Verhalten) wird der Scan angehalten und in die Angriffs-Verarbeitung
								// gesprungen, auch wenn in diesem Block kaum/keine Ziele gefunden wurden -
								// verhindert, dass ein einzelner, sehr dichter oder sehr leerer Bereich den
								// Zyklus unvorhersehbar lange dauern laesst (Nutzer-Vorschlag: feste
								// 50er-Bloecke statt zielabhaengiger Cutoffs).
								if (scanBlockSize > 0 && systemsScannedThisPass >= scanBlockSize) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"AutoFarm.ScanBlockSize erreicht ({scanBlockSize} Systeme gescannt), proceeding to attack.");
									SaveScanCursor(rangeIndex, galaxy, system);
									stopAutoFarm = true;
									break;
								}
								systemsScannedThisPass++;

								bool excludeSystem = ShouldExcludeSystem(galaxy, system);
								if (excludeSystem)
									continue;

								List<Celestial> scannedTargets;
								try {
									scannedTargets = await GetScannedTargetsFromGalaxy(galaxy, system);
								} catch (Exception e) {
									_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Skipping system {galaxy}:{system} after scan failure: {e.Message}");
									continue;
								}

								if (scannedTargets == null) {
									skippedEmptySystems++;
									continue;
								}

								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Found {scannedTargets.Count} targets on System {galaxy}:{system}");

								if (!scannedTargets.Any()) {
									continue;
								}

								if ((bool) _tbotInstance.InstanceSettings.AutoFarm.ExcludeMoons == false) {
									AddMoons(scannedTargets);
								}

								foreach (Celestial planet in scannedTargets) {
									// PATCH 92: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 65
									Heartbeat();
									if (stopAutoFarm)
										break;
									if (!IsTargetInMinimumRank(planet, scannedTargets)) {
										continue;
									}

									// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 66
									if ((passProbeLimit > 0 && numProbed >= passProbeLimit)
										|| (sequentialModeActive && numProbed >= sequentialModeProbesPerCycle)) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
										SaveScanCursor(rangeIndex, galaxy, system);
										stopAutoFarm = true;
										break;
									}

									if (ShouldExcludePlanet(planet))
										continue;

									var target = GetFarmTarget(planet);
									if (target == null)
										continue;

									List<Celestial> tempCelestials = farmOrigins;

									List<Celestial> closestCelestials = tempCelestials
										// PATCH 131: das zweite OrderBy ersetzte die Mond-Praeferenz bisher komplett (LINQ) -
										// effektiv wurde nur nach Distanz sortiert. Das bleibt so (kuerzeste Flugzeit zuerst),
										// der Mond entscheidet jetzt aber echt bei Gleichstand (gleiche Distanz -> Mond zuerst).
										.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, _tbotInstance.UserData.serverData))
										.ThenByDescending(planet => planet.Coordinate.Type == Celestials.Moon).ToList();

									if (!closestCelestials.Any()) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"No origin celestials available. Skipping target {target.Celestial.ToString()}");
										continue;
									}


									int neededProbes = GetNeededProbes(target);

									await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);

									_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
									_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
									var slotBudget = GetCurrentSlotBudget(GetConfiguredCargoType());
									if (slotBudget.AvailableSlots <= 0) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
											$"AutoFarm slot budget exhausted ({slotBudget.OwnedSlots}/{slotBudget.MaxSlots}); pausing further probes.");
										SaveScanCursor(rangeIndex, galaxy, system);
										stopAutoFarm = true;
										break;
									}

									var bestOrigin = await GetBestOrigin(closestCelestials,
										celestialProbes,
										target,
										neededProbes,
										slotsToLeaveFree,
										freeSlots);

									freeSlots = bestOrigin.FreeSlots;

									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Best origin found: {bestOrigin.Origin.Name} ({bestOrigin.Origin.Coordinate.ToString()})");

									if (_calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate))) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Probes already on route towards {target.ToString()}.");
										continue;
									}
									if (_calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Attack, _tbotInstance.UserData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate) && f.ReturnFlight == false)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Attack already on route towards {target.ToString()}.");
										continue;
									}

									if (celestialProbes[bestOrigin.Origin.ID] < neededProbes) {
										var tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Ships);
										celestialProbes.Remove(bestOrigin.Origin.ID);
										celestialProbes.Add(bestOrigin.Origin.ID, tempCelestial.Ships.EspionageProbe);
									}

									if (celestialProbes[bestOrigin.Origin.ID] < neededProbes) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Insufficient probes ({celestialProbes[bestOrigin.Origin.ID]}/{neededProbes}).");
										if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "BuildProbes") && _tbotInstance.InstanceSettings.AutoFarm.BuildProbes == true) {

											var tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Constructions);
											if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
												Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
												SaveScanCursor(rangeIndex, galaxy, system);
												break;
											}

											tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Productions);
											if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
												SaveScanCursor(rangeIndex, galaxy, system);
												break;
											}

											var buildProbes = neededProbes - celestialProbes[bestOrigin.Origin.ID];
											var cost = _calculationService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
											tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Resources);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {buildProbes}x{Buildables.EspionageProbe.ToString()}");

												try {
													await _ogameService.BuildShips(tempCelestial, Buildables.EspionageProbe, buildProbes);
													tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
													int interval = (int) (_calculationService.CalcProductionTime(Buildables.EspionageProbe, (int) buildProbes, _tbotInstance.UserData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
													await Task.Delay(interval, _ct);
												} catch {
													_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
												}
											} else {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Not enough resources to build probes.");
												_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
												var spyMissions = _calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets);
												if (spyMissions.Any()) {
													var spyMissionToWait = spyMissions.OrderBy(c => c.BackIn).First();
													int interval = (int) ((1000 * spyMissionToWait.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Waiting {TimeSpan.FromMilliseconds(interval)} for spy mission to return...");
													await Task.Delay(interval);
												} else {
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"There are not enough probes or resources to build them. Skipping this AutoFarm Execution.");
													SaveScanCursor(rangeIndex, galaxy, system);
													stopAutoFarm = true;
													break;
												}
											}
										}
									}

									if (celestialProbes[bestOrigin.Origin.ID] >= neededProbes) {

										Ships ships = new();
										ships.Add(Buildables.EspionageProbe, neededProbes);

										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Spying {target.ToString()} from {bestOrigin.Origin.ToString()} with {neededProbes} probes.");

										_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
										_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
										slotBudget = GetCurrentSlotBudget(GetConfiguredCargoType());
									if (slotBudget.AvailableSlots <= 0) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
											$"AutoFarm slot budget exhausted ({slotBudget.OwnedSlots}/{slotBudget.MaxSlots}); no probe dispatched.");
										SaveScanCursor(rangeIndex, galaxy, system);
										stopAutoFarm = true;
										break;
									}

										bestOrigin.Origin = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Ships);
										var availableProbes = bestOrigin.Origin.Ships.EspionageProbe;

										if (availableProbes < neededProbes) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
												$"Insufficient probes on {bestOrigin.Origin.ToString()} ({availableProbes}/{neededProbes}). Skipping {target.ToString()}.");
											continue;
										}

										// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 67
									FleetPrediction sequentialProbePrediction = null;
									// PATCH 136: prediction also in FillSlots mode (to wait for the reports)
									if (sequentialModeActive || fillSlotsActive) {
										bestOrigin.Origin = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.LFBonuses);
										sequentialProbePrediction = _calculationService.CalcFleetPrediction(bestOrigin.Origin.Coordinate, target.Celestial.Coordinate, ships, Missions.Spy, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, bestOrigin.Origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
									}
									var fleetId = await _fleetScheduler.SendFleet(bestOrigin.Origin, ships, target.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);

										if (fleetId > (int) SendFleetCode.GenericError) {
											freeSlots--;
											numProbed++;
											celestialProbes[bestOrigin.Origin.ID] -= neededProbes;

											// PATCH 136: remember the latest probe arrival of this pass
											if (fillSlotsActive && sequentialProbePrediction != null) {
												DateTime arrival136 = DateTime.UtcNow.AddSeconds(sequentialProbePrediction.Time);
												if (!fillSlotsLastProbeArrivalUtc.HasValue || arrival136 > fillSlotsLastProbeArrivalUtc.Value)
													fillSlotsLastProbeArrivalUtc = arrival136;
											}
											// PATCH 136: sequential mode only - otherwise the finally block would use the sequential timing
											if (sequentialProbePrediction != null && sequentialModeActive) {
												_sequentialModeNextArrivalSeconds = (long) sequentialProbePrediction.Time;
												// PATCH 102: siehe Kommentar beim Feld - Zeitpunkt HIER (kurz nach dem
												// tatsaechlichen Versand) festhalten, nicht erst am Ende von Execute().
												_sequentialModeProbeSentAtUtc = DateTime.UtcNow;
												_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Sequential mode: espionage attack ({neededProbes}x probe) on its way to {target.Celestial.Coordinate}, arrival in ~{TimeSpan.FromSeconds(sequentialProbePrediction.Time)}.");
											}

											if (target.State == FarmState.ProbesRequired || target.State == FarmState.FailedProbesRequired)
												continue;

											_tbotInstance.UserData.farmTargets.Remove(target);
											target.State = FarmState.ProbesSent;
											_tbotInstance.UserData.farmTargets.Add(target);

											continue;
										} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										} else if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Another worker took the last available slot; pausing AutoFarm instead of retrying repeatedly.");
											SaveScanCursor(rangeIndex, galaxy, system);
											stopAutoFarm = true;
											break;
										} else {
											continue;
										}
									}
								}
							}
						}
							if (!stopAutoFarm && orderedRanges.Any()) {
								SaveScanCursor(0, 0, 0);
								 finishedFullScan = true;
								 // PATCH 101: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 68
								 _mainScanCompletedSinceStart = true;
								 // PATCH 107: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 127
								 _stateStore.SaveMainScanCompleted(true);

								 _tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
										"Full scan cycle completed, resetting scan position for next cycle");

                              if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "StopAfterFullScan") &&
                               (bool)_tbotInstance.InstanceSettings.AutoFarm.StopAfterFullScan) {

									 _tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
									"StopAfterFullScan=true -> Full scan completed. Attacks will be processed before AutoFarm stops.");
								 }
							 }

					} catch (Exception e) {
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Exception: {e.Message}");
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to parse scan range");
					}

					if (skippedEmptySystems > 0)
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipped {skippedEmptySystems} systems still in the empty-system cooldown.");
					PersistTargets();

					// PATCH 89, 87: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 69
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

					// PATCH 136: see AutoFarmWorker.PATCHNOTES.md, section 154 - FillSlots: wait for the probes sent in
					// this pass so their reports are processed NOW and the targets are attacked in the same pass
					// (previously only in the next cycle, CheckIntervalMin/Max later).
					if (fillSlotsActive && fillSlotsLastProbeArrivalUtc.HasValue) {
						int maxProbeWaitSec = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FillSlotsMaxProbeWaitSec", 300);
						double waitSec = Math.Min((fillSlotsLastProbeArrivalUtc.Value - DateTime.UtcNow).TotalSeconds + 8, maxProbeWaitSec);
						if (waitSec > 0) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"FillSlots: waiting {TimeSpan.FromSeconds(Math.Ceiling(waitSec))} for this pass's espionage reports.");
							long remainingWaitMs = (long) (waitSec * 1000);
							while (remainingWaitMs > 0) {
								Heartbeat();
								long chunk = Math.Min(remainingWaitMs, 20000);
								await Task.Delay(TimeSpan.FromMilliseconds(chunk), _ct);
								remainingWaitMs -= chunk;
							}
						}
					}					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Processing espionage reports of found inactives...");

					// PATCH 74: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 70
					await AutoFarmProcessReports();
					PersistTargets();

					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					Buildables configuredCargoType = GetConfiguredCargoType();
					List<RankSlotsPriority> rankSlotsPriority = new() {
						new RankSlotsPriority(Feature.BrainAutoMine,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Brain,
							((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active && ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Active || (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active || (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)),
							(int) _tbotInstance.InstanceSettings.Brain.Transports.MaxSlots,
							(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Transport)),
						new RankSlotsPriority(Feature.Expeditions,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Expeditions,
							(bool) _tbotInstance.InstanceSettings.Expeditions.Active,
							(int) _tbotInstance.UserData.slots.ExpTotal,
							(int)_tbotInstance.UserData.slots.ExpInUse),
						new RankSlotsPriority(Feature.AutoFarm,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoFarm,
							(bool) _tbotInstance.InstanceSettings.AutoFarm.Active,
							(int) _tbotInstance.InstanceSettings.AutoFarm.MaxSlots,
							(int) _tbotInstance.UserData.fleets.Count(f => AutoFarmSlotPlanner.IsOwnedFleet(f, configuredCargoType))),
						new RankSlotsPriority(Feature.Colonize,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Colonize,
							(bool) _tbotInstance.InstanceSettings.AutoColonize.Active,
							(bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active ?
								(int) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MaxSlots :
								1,
							(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Colonize)),
						new RankSlotsPriority(Feature.AutoDiscovery,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoDiscovery,
							(bool) _tbotInstance.InstanceSettings.AutoDiscovery.Active,
							(int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots,
							(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Discovery)),
						new RankSlotsPriority(Feature.Harvest,
							(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoHarvest,
							(bool) _tbotInstance.InstanceSettings.AutoHarvest.Active,
							(int) _tbotInstance.InstanceSettings.AutoHarvest.MaxSlots,
							(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Harvest))
					};
					int MaxSlots = _calculationService.CalcSlotsPriority(Feature.AutoFarm, rankSlotsPriority, _tbotInstance.UserData.slots, _tbotInstance.UserData.fleets, (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree);

					// PATCH 96: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 71
					bool isUsingProbesForHalfSplit = configuredCargoType == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 1;

					// PATCH 109: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 129
					// Nutzer-Wunsch: unverteidigte Ziele (billig, keine Eskorten-Simulation noetig)
					// werden jetzt GRUNDSAETZLICH vor allen verteidigten einsortiert - erst alle
					// unverteidigten nach Beute absteigend "in Ruhe" abarbeiten, danach erst die
					// verteidigten (ebenfalls nach Beute absteigend), statt wie bisher rein nach Beute
					// gemischt (wo ein hochprofitables verteidigtes Ziel die teure Eskorten-Simulation
					// vor vielen kleinen, aber garantiert einfachen Zielen ausloesen konnte). Der
					// bestehende PATCH-96-Haelfte-Mechanismus direkt darunter bleibt zusaetzlich
					// bestehen und wirkt jetzt einfach auf diese bereits "unverteidigt zuerst"
					// sortierte Liste, statt auf eine rein nach Beute gemischte.
					Func<FarmTarget, long> lootMetric;
					if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Metal")
						lootMetric = t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Metal;
					else if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Crystal")
						lootMetric = t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Crystal;
					else if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Deuterium")
						lootMetric = t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Deuterium;
					else
						lootMetric = t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources;

					List<FarmTarget> attackTargets = _tbotInstance.UserData.farmTargets
						.Where(t => t.State == FarmState.AttackPending)
						.OrderByDescending(t => t.Report.IsDefenceless(isUsingProbesForHalfSplit))
						.ThenByDescending(lootMetric)
						.ToList();

					_defendedHalfToggle = !_defendedHalfToggle;
					bool activeHalf = _defendedHalfToggle;
					int deferredDefendedCount = 0;
					attackTargets = attackTargets.Where(t => {
						if (t.Report.IsDefenceless(isUsingProbesForHalfSplit)) {
							return true; // unverteidigt/billig - immer mitnehmen, keine Drosselung noetig
						}
						var c = t.Celestial.Coordinate;
						bool targetHalf = ((c.Galaxy * 100000) + (c.System * 1000) + c.Position) % 2 == 0;
						if (targetHalf == activeHalf) {
							return true;
						}
						deferredDefendedCount++;
						return false;
					}).ToList();
					if (deferredDefendedCount > 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"PATCH 96: {deferredDefendedCount} defended targets postponed this cycle (other half, {(activeHalf ? "A" : "B")} is up) - they come up next cycle.");
					}

					if (attackTargets.Count() > 0) {
						var resourceAmount = new Resources();
						attackTargets.ForEach(target => resourceAmount = resourceAmount.Sum(target.Report.Loot(_tbotInstance.UserData.userInfo.Class)));
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attacking suitable farm targets... (Estimated total profit: {resourceAmount.TransportableResources})");
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "No suitable targets found.");
						// PATCH 136: in FillSlots mode continue scanning (next pass) instead of ending the whole cycle
						if (!fillSlotsActive)
							return;
					}

					Buildables cargoShip = configuredCargoType;
					if (cargoShip == Buildables.Null) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip is Null");
						return;
					}
					if (cargoShip == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 0) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip set to EspionageProbe, but this universe does not have probe cargo.");
						return;
					}

					_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
					_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();
					int attackTargetsCount = 0;
					decimal lootFuelRatio = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") ? (decimal) _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio : (decimal) 0.0001;
					decimal speed = 0;
					foreach (FarmTarget target in attackTargets) {
						// PATCH 92: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 72
						Heartbeat();
						attackTargetsCount++;
						// PATCH 95: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 73
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Evaluating target {attackTargetsCount}/{attackTargets.Count()} at {target.Celestial.Coordinate.ToString()} for {target.Report.Loot(_tbotInstance.UserData.userInfo.Class).TransportableResources}.");

						// PATCH 135: see AutoFarmWorker.PATCHNOTES.md, section 153 - do not re-check/re-simulate blacklisted targets
						// (e.g. unprofitable from an earlier cycle).
						bool blacklistActive135 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
							SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "Active") &&
							(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;
						if (blacklistActive135 && _blacklist.IsBlacklisted(target.Celestial.Coordinate)) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipping {target.Celestial.Coordinate}: blacklisted.");
							target.State = FarmState.NotSuitable;
							continue;
						}

						// PATCH 43: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 74
						GalaxyInfo freshGalaxyInfo = await _ogameService.GetGalaxyInfo(target.Celestial.Coordinate.Galaxy, target.Celestial.Coordinate.System);
						Planet freshPlanet = freshGalaxyInfo?.Planets?.FirstOrDefault(p => p != null && p.HasCoords(target.Celestial.Coordinate));
						if (freshPlanet != null && !AutoFarmSystemPolicy.IsEligibleTarget(freshPlanet)) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {target.Celestial.Coordinate}: no longer inactive/eligible (player likely logged back in since the last scan).");
							target.State = FarmState.NotSuitable;
							continue;
						}

						// PATCH 130: siehe AutoFarmWorker.PATCHNOTES.md. Harte Obergrenze fuer das Alter des
						// Spionageberichts direkt vor Verteidigungspruefung/Eskortensuche. AutoFarm.KeepReportFor
						// steuert nur, wie lange ein Bericht sichtbar bleibt - ein bis zu 24h alter Bericht
						// ("keine Verteidigung") kann inzwischen veraltet sein, das Galaxy-Scan oben sieht keine
						// Verteidigung/Flotte. Ist der Bericht aelter als AutoFarm.MaxReportAgeMinutesForAttack
						// (Default 120, 0 = aus), wird nicht angegriffen und das Ziel fuer eine frische Sonde
						// vorgemerkt.
						int maxReportAgeMinutesForAttack = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxReportAgeMinutesForAttack", 120);
						if (maxReportAgeMinutesForAttack > 0 && target.Report != null) {
							var reportAge130 = (await _tbotOgameBridge.GetDateTime()) - target.Report.Date;
							if (reportAge130.TotalMinutes > maxReportAgeMinutesForAttack) {
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
									$"Attack on {target.Celestial.Coordinate} aborted: espionage report is {reportAge130.TotalMinutes:F0} min old (max. {maxReportAgeMinutesForAttack} min) - defense may have changed, requesting a fresh probe.");
								target.State = FarmState.ProbesPending;
								target.Report = null;
								continue;
							}
						}

						// PATCH 45, 46, 62, 63, 79: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 75
						Buildables plainCargoType = GetPlainCargoType();
						bool isUsingProbesForCargoCheck = configuredCargoType == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 1;
						if (target.Report.IsDefenceless(isUsingProbesForCargoCheck)) {
							cargoShip = plainCargoType;
						} else {
							cargoShip = configuredCargoType == Buildables.SmallCargo ? Buildables.LargeCargo : configuredCargoType;
						}

						var loot = target.Report.Loot(_tbotInstance.UserData.userInfo.Class);
						Celestial tempCelestial = _tbotInstance.UserData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).First();
						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
						float cargoBonus = tempCelestial.LFBonuses.GetShipCargoBonus(cargoShip);
						var numCargo = _calculationService.CalcShipNumberForPayload(loot, cargoShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "CargoSurplusPercentage") && (double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage > 0) {
							// PATCH 55: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 76
							numCargo = (long) Math.Round(numCargo + (numCargo * ((double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage / 100.0)), 0);
						}
						var attackingShips = new Ships().Add(cargoShip, numCargo);

						List<Celestial> tempCelestials = (_tbotInstance.InstanceSettings.AutoFarm.Origin.Length > 0) ? _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.AutoFarm.Origin, _tbotInstance.UserData.celestials) : _tbotInstance.UserData.celestials;
						List<Celestial> closestCelestials = tempCelestials
							// PATCH 131: das zweite OrderBy ersetzte die Mond-Praeferenz bisher komplett (LINQ) -
							// effektiv wurde nur nach Distanz sortiert. Das bleibt so (kuerzeste Flugzeit zuerst),
							// der Mond entscheidet jetzt aber echt bei Gleichstand (gleiche Distanz -> Mond zuerst).
							.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, _tbotInstance.UserData.serverData))
							.ThenByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
							.ToList();

						Celestial fromCelestial = null;
						foreach (var c in closestCelestials) {
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
							// PATCH 120: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 140
							long availableCargoShips120 = tempCelestial.Ships?.GetAmount(cargoShip) ?? 0;
							if (tempCelestial.Ships != null && availableCargoShips120 >= (numCargo + _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep)) {
								speed = 0;
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
									// PATCH 114: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 134
									// MaxFlightTime = 0 bedeutet ueberall sonst im Code "kein Limit" (siehe
									// weiter unten die Celestial-Auswahl: "!IsSettingSet || MaxFlightTime==0
									// || prediction.Time <= MaxFlightTime"). Hier wurde die 0 aber bisher 1:1
									// an CalcOptimalFarmSpeed durchgereicht, wo sie als "Time < 0" geprueft
									// wird - das ist NIE erfuellt, egal wie schnell die Flotte ist. Ergebnis:
									// bei MaxFlightTime=0 hat CalcOptimalFarmSpeed IMMER den teuersten
									// Fallback (Speeds.HundredPercent) zurueckgegeben, MinLootFuelRatio wurde
									// dadurch faktisch nie wirksam. Fix: 0 wird jetzt wie ueberall sonst als
									// "kein Limit" behandelt (long.MaxValue statt 0).
									long configuredMaxFlightTime114 = (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime;
									long maxFlightTime = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime")
										? 86400
										: (configuredMaxFlightTime114 == 0 ? long.MaxValue : configuredMaxFlightTime114);
									var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
									if (optimalSpeed == 0) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

									} else {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
										speed = optimalSpeed;
									}
								}
								if (speed == 0) {
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
										speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
										if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = Speeds.HundredPercent;
									}
								}
								FleetPrediction prediction = _calculationService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								// PATCH 120: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 140
								bool flightTimeOk120 = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ||
									(long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ||
									prediction.Time <= (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime;
								bool fuelOk120 = prediction.Fuel <= tempCelestial.Resources.Deuterium;
								if (flightTimeOk120 && fuelOk120) {
									fromCelestial = tempCelestial;
									break;
								}
								if (!flightTimeOk120) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{tempCelestial.Coordinate} discarded as origin for {target.Celestial.Coordinate}: flight time {TimeSpan.FromSeconds(prediction.Time)} exceeds AutoFarm.MaxFlightTime ({TimeSpan.FromSeconds((long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime)}).");
								} else {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{tempCelestial.Coordinate} discarded as origin for {target.Celestial.Coordinate}: fuel ({prediction.Fuel:N0}) exceeds available deuterium ({tempCelestial.Resources.Deuterium:N0}).");
								}
							} else if (tempCelestial.Ships != null) {
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{tempCelestial.Coordinate} discarded as origin for {target.Celestial.Coordinate}: only {availableCargoShips120:N0}x{cargoShip} available, needed {(numCargo + _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep):N0} (incl. MinCargosToKeep).");
							}
						}

						if (fromCelestial == null) {
							// PATCH 120: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 140 - genauer Grund steht als Debug-Log je Kandidat oben
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"No origin celestial available near destination {target.Celestial.ToString()} with enough cargo ships within flight time/fuel limits (details at debug log level).");
							foreach (var closest in closestCelestials) {
								tempCelestial = closest;
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
								speed = 0;
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
									speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
									if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = 0;
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
										// PATCH 114: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 134
									// MaxFlightTime = 0 bedeutet ueberall sonst im Code "kein Limit" (siehe
									// weiter unten die Celestial-Auswahl: "!IsSettingSet || MaxFlightTime==0
									// || prediction.Time <= MaxFlightTime"). Hier wurde die 0 aber bisher 1:1
									// an CalcOptimalFarmSpeed durchgereicht, wo sie als "Time < 0" geprueft
									// wird - das ist NIE erfuellt, egal wie schnell die Flotte ist. Ergebnis:
									// bei MaxFlightTime=0 hat CalcOptimalFarmSpeed IMMER den teuersten
									// Fallback (Speeds.HundredPercent) zurueckgegeben, MinLootFuelRatio wurde
									// dadurch faktisch nie wirksam. Fix: 0 wird jetzt wie ueberall sonst als
									// "kein Limit" behandelt (long.MaxValue statt 0).
									long configuredMaxFlightTime114 = (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime;
									long maxFlightTime = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime")
										? 86400
										: (configuredMaxFlightTime114 == 0 ? long.MaxValue : configuredMaxFlightTime114);
										var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
										if (optimalSpeed == 0) {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

										} else {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
											speed = optimalSpeed;
										}
									}
									if (speed == 0) {
										if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
											speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
											if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
												_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
												speed = Speeds.HundredPercent;
											}
										} else {
											speed = Speeds.HundredPercent;
										}
									}
								}
								FleetPrediction prediction = _calculationService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								if (
									tempCelestial.Ships.GetAmount(cargoShip) < numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep &&
									tempCelestial.Resources.Deuterium >= prediction.Fuel &&
									(
										!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ||
										(long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ||
										prediction.Time <= (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime
									)
								) {
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "BuildCargos") && _tbotInstance.InstanceSettings.AutoFarm.BuildCargos == true) {
										var neededCargos = numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep - tempCelestial.Ships.GetAmount(cargoShip);
										var cost = _calculationService.CalcPrice(cargoShip, (int) neededCargos);
										if (tempCelestial.Resources.IsEnoughFor(cost)) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {neededCargos}x{cargoShip.ToString()}");
										} else {
											var buildableCargos = _calculationService.CalcMaxBuildableNumber(cargoShip, tempCelestial.Resources);
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{cargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
											neededCargos = buildableCargos;
										}

										try {
											await _ogameService.BuildShips(tempCelestial, cargoShip, neededCargos);
											tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
											int interval = (int) (_calculationService.CalcProductionTime(cargoShip, (int) neededCargos, _tbotInstance.UserData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
											await Task.Delay(interval, _ct);
										} catch {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
										}
									}

									if (tempCelestial.Ships.GetAmount(cargoShip) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep < (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToSend) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Insufficient {cargoShip.ToString()} on {tempCelestial.Coordinate}, require {numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep} {cargoShip.ToString()}.");
										continue;
									}

									numCargo = tempCelestial.Ships.GetAmount(cargoShip) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep;
									fromCelestial = tempCelestial;
									break;
								}
							}
						}

						if (fromCelestial == null) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. No suitable origin celestial available near the destination.");
							continue;
						}

						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var slotBudget = GetCurrentSlotBudget(cargoShip);
						if (MaxSlots > 0 && slotBudget.AvailableSlots > 0) {
							fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Ships);
							var availableShips = fromCelestial.Ships.GetAmount(cargoShip) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep;
							if (availableShips <= 0) {
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"No {cargoShip.ToString()} available on {fromCelestial.ToString()} (all ships already in use). Skipping target.");
								continue;
							}
							if (availableShips < numCargo) {
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Only {availableShips} {cargoShip.ToString()} available (needed {numCargo}). Adjusting fleet size.");
								numCargo = availableShips;
								attackingShips = new Ships();
								attackingShips.Add(cargoShip, numCargo);

								var cargoCapacity = _calculationService.CalcFleetCapacity(
									attackingShips, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology,
									fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo); // PATCH ronnie32 v3.5.1-beta2: count probe cargo
								var totalLoot = target.Report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources;

								if (cargoCapacity < totalLoot) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
										$"Insufficient cargo space after adjustment. {numCargo} {cargoShip.ToString()} can carry {cargoCapacity:N0} but need {totalLoot:N0}. Skipping target.");
									continue;
								}
							}

							// PATCH 39: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 77
							bool isUsingProbesForEscort = cargoShip == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 1;
							SimulatorResult escortSimResult = null;
							// PATCH 70-Fix: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 78
							string escortBreakdownForLog = "";
							if (!target.Report.IsDefenceless(isUsingProbesForEscort)) {
								// PATCH 78: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 79
								bool attackDefendedTargets = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AttackDefendedTargets") &&
									(bool) _tbotInstance.InstanceSettings.AutoFarm.AttackDefendedTargets;
								if (!attackDefendedTargets) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipping defended target {target.Celestial.Coordinate}: AutoFarm.AttackDefendedTargets is disabled (target likely became AttackPending while it was still enabled).");
									continue;
								}
								// PATCH 57: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 80
								if (AutoFarmIsEscortPoolLow(fromCelestial)) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Pausing defended target {target.Celestial.Coordinate} this cycle: escort ship pool too low (tied up in earlier targets) - retrying next cycle instead of blacklisting.");
									continue;
								}
								var escort = await AutoFarmDetermineEscort(target, fromCelestial, cargoShip, numCargo);
								if (escort == null) {
									// PATCH 95: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 81
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipping defended target {target.Celestial.Coordinate}: AutoFarmDetermineEscort returned null (see specific reason logged above).");
									// PATCH 69: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 82
									continue;
								}

								// PATCH 63: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 83
								if (cargoShip != Buildables.LargeCargo && escort.Value.UsedFallbackTier) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: escort already needs Tier-2 ships (Bomber/Reaper) - speedfleet advantage of {cargoShip} void, trying GT (LargeCargo) instead...");
									Buildables gtCargoType = Buildables.LargeCargo;
									float gtCargoBonus = fromCelestial.LFBonuses.GetShipCargoBonus(gtCargoType);
									long gtNumCargo = _calculationService.CalcShipNumberForPayload(loot, gtCargoType, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, gtCargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "CargoSurplusPercentage") && (double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage > 0) {
										gtNumCargo = (long) Math.Round(gtNumCargo + (gtNumCargo * ((double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage / 100.0)), 0);
									}
									long gtAvailable = fromCelestial.Ships.GetAmount(gtCargoType) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep;
									if (gtAvailable < gtNumCargo) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: not enough GT ({gtAvailable}/{gtNumCargo}) at the origin for the switch - staying with {cargoShip}.");
									} else {
										var gtEscort = await AutoFarmDetermineEscort(target, fromCelestial, gtCargoType, gtNumCargo);
										if (gtEscort == null) {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: GT alone is not enough either - staying with {cargoShip}.");
										} else {
											cargoShip = gtCargoType;
											numCargo = gtNumCargo;
											attackingShips = new Ships();
											attackingShips.Add(cargoShip, numCargo);
											escort = gtEscort;
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: switched to GT ({numCargo}x{cargoShip}) instead of Pathfinder+Tier-2 escort.");
										}
									}
								}

								foreach (var kv in escort.Value.Escort) {
									attackingShips.Add(kv.Key, kv.Value);
								}
								// PATCH 44: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 84
								double escortReservePercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "EscortReservePercent", 5.0);
								foreach (var kv in escort.Value.Escort) {
									long buffer = (long) Math.Ceiling(kv.Value * (escortReservePercent / 100.0));
									long available = fromCelestial.Ships.GetAmount(kv.Key);
									long alreadyQueued = attackingShips.GetAmount(kv.Key);
									long extra = Math.Min(buffer, available - alreadyQueued);
									if (extra > 0) {
										attackingShips.Add(kv.Key, extra);
									}
								}
								escortSimResult = escort.Value.Result;
								escortBreakdownForLog = string.Join(", ", escort.Value.Escort.Select(kv => $"{kv.Value}x{kv.Key}"));
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Escort for {target.Celestial.Coordinate}: {escortBreakdownForLog} (+{escortReservePercent}% buffer applied where available).");
							}

							// PATCH 44: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 85
							{
								double cargoReservePercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "CargoReservePercent", 10.0);
								long cargoBuffer = (long) Math.Ceiling(numCargo * (cargoReservePercent / 100.0));
								long availableCargo = fromCelestial.Ships.GetAmount(cargoShip);
								long alreadyQueuedCargo = attackingShips.GetAmount(cargoShip);
								long extraCargo = Math.Min(cargoBuffer, availableCargo - alreadyQueuedCargo);
								if (extraCargo > 0) {
									attackingShips.Add(cargoShip, extraCargo);
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Added {extraCargo} extra {cargoShip} to {target.Celestial.Coordinate} as +10% safety buffer.");
								}
							}

							// PATCH 113: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 133
							// War vorher "Attacking ..." - klang nach bereits erfolgtem Versand, obwohl
							// erst DANACH (Speed-Berechnung + Wirtschaftlichkeitspruefung, Zeile ~2510)
							// entschieden wird ob ueberhaupt versendet wird. Hat im Log den falschen
							// Eindruck erweckt, es wuerde erst angegriffen und danach als unprofitabel
							// erkannt/blacklisted - tatsaechlich wird bei "not profitable" per continue
							// aus der Schleife gesprungen, BEVOR SendFleet (Zeile ~2616) erreicht wird.
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Evaluating attack on {target.ToString()} from {fromCelestial} with {numCargo} {cargoShip.ToString()}...");
							Ships ships = new();
							fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.LFBonuses);

							speed = 0;
							if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
								// PATCH 114: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 134 (MaxFlightTime=0 -> kein Limit statt "Time < 0")
							long configuredMaxFlightTime114 = (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime;
							long maxFlightTime = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime")
								? 86400
								: (configuredMaxFlightTime114 == 0 ? long.MaxValue : configuredMaxFlightTime114);
								var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
								if (optimalSpeed == 0) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

								} else {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
									speed = optimalSpeed;
								}
							}
							if (speed == 0) {
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
									speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
									if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = Speeds.HundredPercent;
								}
							}

							// PATCH 134: lohnt sich das Truemmerfeld ueberhaupt fuer Trigger-Sonde + Recycler?
							bool debrisWorthHarvesting = false;
							// PATCH 51: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 86
							if (escortSimResult != null) {
								FleetPrediction profitPrediction = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
								long predictedLosses = escortSimResult.AttackerLosses?.Total() ?? 0;
								long predictedFuel = profitPrediction.Fuel;
								long predictedCost = predictedLosses + predictedFuel;
								long predictedLoot = loot.TotalResources;
								// PATCH 70: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 87
								long distanceForLog = _calculationService.CalcDistance(fromCelestial.Coordinate, target.Celestial.Coordinate, _tbotInstance.UserData.serverData);
								// PATCH 76: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 88
								long predictedDebrisForLog = escortSimResult.Debris?.Total() ?? 0;
								// PATCH 132: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 150 - Truemmer als Ertrag.
								// Bisher zaehlte nur die Beute; ein Ziel mit grosser Flotte (z.B. 11,7 Mio. Flottenwert,
								// ~3,5 Mio. Truemmer, 0,6 Mio. Beute) wurde als unprofitabel 72h geblacklistet, obwohl
								// die Recycler ein Vielfaches einsammeln. Gutschrift nur, wenn AutoHarvestDebrisAfterAttack
								// aktiv ist und Recycler auf dem Start-Celestial stehen; begrenzt auf deren Ladekapazitaet,
								// abzueglich Recycler-Sprit, gewichtet mit AutoFarm.DebrisCreditPercent (Default 80,
								// Abschlag fuer Klau-/Ausfallrisiko). 0 = altes Verhalten (nur Beute).
								long debrisCredit = 0;
								long debrisCollectable = 0;
								long recyclerFuelForProfit = 0;
								bool debrisHarvestActive = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AutoHarvestDebrisAfterAttack")
									&& (bool) _tbotInstance.InstanceSettings.AutoFarm.AutoHarvestDebrisAfterAttack;
								double debrisCreditPercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "DebrisCreditPercent", 80.0);
								if (debrisHarvestActive && predictedDebrisForLog > 0) {
									try {
										long availableRecyclersForProfit = fromCelestial.Ships.GetAmount(Buildables.Recycler);
										if (availableRecyclersForProfit > 0) {
											float recyclerBonusForProfit = fromCelestial.LFBonuses?.GetShipCargoBonus(Buildables.Recycler) ?? 0;
											long recyclerCapacity = _calculationService.CalcShipCapacity(Buildables.Recycler, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, recyclerBonusForProfit, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (recyclerCapacity > 0) {
												debrisCollectable = Math.Min(predictedDebrisForLog, recyclerCapacity * availableRecyclersForProfit);
												long recyclersForProfit = (long) Math.Ceiling((double) debrisCollectable / recyclerCapacity);
												var debrisCoordForProfit = new Coordinate(target.Celestial.Coordinate.Galaxy, target.Celestial.Coordinate.System, target.Celestial.Coordinate.Position, Celestials.Debris);
												recyclerFuelForProfit = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, debrisCoordForProfit, new Ships().Add(Buildables.Recycler, recyclersForProfit), Missions.Harvest, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Fuel;
												debrisCredit = Math.Max(0, (long) (debrisCollectable * (debrisCreditPercent / 100.0)) - recyclerFuelForProfit);
											}
										}
									} catch (Exception e) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: debris credit could not be calculated ({e.Message}) - counting loot only.");
										debrisCredit = 0;
									}
								}
								long predictedGain = predictedLoot + debrisCredit;
								// PATCH 134: Recycler + Trigger-Sonde + Ankunftsabstimmung nur, wenn das einsammelbare Feld
								// nach Abzug des Recycler-Sprits mehr als AutoFarm.MinDebrisToHarvest bringt (Default 0).
								// Vorher gingen z.B. fuer 251 Truemmer 2 Recycler (4.139 Sprit, 2h20 Slot) raus und die
								// Hauptflotte wurde dafuer gebremst und verzoegert.
								long minDebrisToHarvest = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MinDebrisToHarvest", (long) 0);
								debrisWorthHarvesting = debrisCollectable - recyclerFuelForProfit > minDebrisToHarvest;
								if (debrisHarvestActive && predictedDebrisForLog > 0 && !debrisWorthHarvesting) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: debris field ({debrisCollectable:N0} collectable, recycler fuel {recyclerFuelForProfit:N0}) is not worth it - without trigger probe and recycler.");
								}
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: profitability check - distance {distanceForLog:N0}, speed {speed * 10}%, fleet {numCargo}x{cargoShip} + {escortBreakdownForLog}, fuel {predictedFuel:N0}, estimated escort losses {predictedLosses:N0}, estimated debris {predictedDebrisForLog:N0} (collectable {debrisCollectable:N0}, recycler fuel {recyclerFuelForProfit:N0}, credit {debrisCredit:N0}), loot {predictedLoot:N0}.");
								// PATCH 134: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 152 - bei der gewaehlten
								// Geschwindigkeit unprofitabel? Dann langsamere Stufen pruefen (nur der Sprit aendert sich,
								// die Verluste nicht), aber nicht unter AutoFarm.MinSpeedPercentForProfitRetry (Default 50)
								// und nicht ueber MaxFlightTime. Die schnellste profitable Stufe gewinnt.
								if (predictedCost >= predictedGain) {
									int minRetrySpeedPercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MinSpeedPercentForProfitRetry", 50);
									long maxFlightForRetry = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime")
										? 86400
										: ((long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ? long.MaxValue : (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime);
									foreach (decimal retrySpeed in _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Where(sp => sp < speed && sp * 10 >= minRetrySpeedPercent).OrderByDescending(sp => sp)) {
										FleetPrediction retryPrediction = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, retrySpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
										if (retryPrediction.Time > maxFlightForRetry) {
											continue;
										}
										if (predictedLosses + retryPrediction.Fuel < predictedGain) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: unprofitable at {speed * 10}% (fuel {predictedFuel:N0}), profitable at {retrySpeed * 10}% (fuel {retryPrediction.Fuel:N0}, losses {predictedLosses:N0}, gain {predictedGain:N0}) - flying slower.");
											speed = retrySpeed;
											predictedFuel = retryPrediction.Fuel;
											predictedCost = predictedLosses + predictedFuel;
											break;
										}
									}
								}
								if (predictedCost >= predictedGain) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {target.Celestial.Coordinate}: not profitable - predicted losses ({predictedLosses:N0}) + fuel ({predictedFuel:N0}) = {predictedCost:N0} >= loot ({predictedLoot:N0}) + debris credit ({debrisCredit:N0}) [distance {distanceForLog:N0}, speed {speed * 10}%, fleet {numCargo}x{cargoShip} + {escortBreakdownForLog}].");

									// PATCH 83: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 89
									bool blacklistActiveUnprofitable = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
										SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "Active") &&
										(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;
									if (blacklistActiveUnprofitable) {
										int unprofitableHoursUntilReset = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "UnprofitableResetAfterHours", 72);
										_blacklist.AddTarget(target.Celestial.Coordinate, BlacklistReason.Unprofitable, unprofitableHoursUntilReset);
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {target.Celestial.Coordinate} blacklisted for {unprofitableHoursUntilReset}h (unprofitable: fuel+losses {predictedCost:N0} >= loot {predictedLoot:N0} + debris credit {debrisCredit:N0}).");
									}
									// PATCH 135: see AutoFarmWorker.PATCHNOTES.md, section 153 - target leaves AttackPending,
									// otherwise it is re-simulated every cycle (X:31:8 with 68k cruisers: 7x). A new report
									// (new probe after the blacklist reset) can set it back to AttackPending.
									target.State = FarmState.NotSuitable;
									continue;
								}
							}

							// PATCH 74, 40: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 90
							Ships recyclerFleetToSend = null;
							Coordinate recyclerDebrisCoordinate = null;
							decimal recyclerSpeed = 0;
							// PATCH 126: Ankunftskoordination Hauptflotte/Recycler
							long recyclerDelaySec = 0;
							long attackDelaySec = 0;
							long recyclerPlannedAttackFlightSec = 0;
							// PATCH 127: fuer die Nachkorrektur mit echten Ankunftszeiten
							long recyclerGapTargetSec = 0;
							long recyclerCorrectedFlightSec = 0;
							// PATCH 133: Rueckkehr in die Schlafenszeit VOR Trigger-Sonde/Recycler pruefen. Rundflug
							// der Hauptflotte (+ Recycler, falls Truemmer eingesammelt werden) + 10 min Puffer fuer
							// moegliche Verlangsamung/Verzoegerung aus PATCH 126.
							{
								long sleepCheckAttackT = (long) _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
								long sleepCheckRoundTrip = 2 * sleepCheckAttackT;
								bool sleepCheckDebris = debrisWorthHarvesting && escortSimResult != null && escortSimResult.Debris != null && escortSimResult.Debris.Total() > 0
									&& SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AutoHarvestDebrisAfterAttack")
									&& (bool) _tbotInstance.InstanceSettings.AutoFarm.AutoHarvestDebrisAfterAttack
									&& fromCelestial.Ships.GetAmount(Buildables.Recycler) > 0;
								if (sleepCheckDebris) {
									var sleepCheckDebrisCoord = new Coordinate(target.Celestial.Coordinate.Galaxy, target.Celestial.Coordinate.System, target.Celestial.Coordinate.Position, Celestials.Debris);
									long sleepCheckRecT = (long) _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, sleepCheckDebrisCoord, new Ships().Add(Buildables.Recycler, 1), Missions.Harvest, _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Max(), _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
									// Recycler kommt ~2 min nach der Hauptflotte an und fliegt dann zurueck
									sleepCheckRoundTrip = Math.Max(sleepCheckRoundTrip, sleepCheckAttackT + 120 + sleepCheckRecT);
								}
								if (await WouldConflictWithSleepAsync(sleepCheckRoundTrip + 600)) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: attack skipped - fleets (round trip ~{TimeSpan.FromSeconds(sleepCheckRoundTrip)}) would be en route/returning during sleep time (SleepMode). Target stays marked for later.");
									continue;
								}
							}

							// PATCH 119: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 139
							int? triggerProbeFleetIdForGuard = null;
							if (debrisWorthHarvesting && escortSimResult != null && escortSimResult.Debris != null && escortSimResult.Debris.Total() > 0
								&& SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AutoHarvestDebrisAfterAttack")
								&& (bool) _tbotInstance.InstanceSettings.AutoFarm.AutoHarvestDebrisAfterAttack) {

								long availableTriggerProbes = fromCelestial.Ships.GetAmount(Buildables.EspionageProbe);
								if (availableTriggerProbes <= 0) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: no probe available for the debris field trigger - attack goes out without recycler preparation.");
								} else {
									// die Sonde fliegt als Attack-Mission (nicht Spionage) - nur eine
									// Attack-Mission erzeugt garantiert einen Truemmerfeld-Datensatz, egal ob
									// echter Kampf stattfindet. Sie kommt dabei nicht zurueck (wird beim Ziel
									// verbraucht/zerstoert) - es muss nur ihre Ankunft abgewartet werden.
									Ships triggerProbeFleet = new Ships().Add(Buildables.EspionageProbe, 1);
									decimal triggerSpeed = _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Max();
									FleetPrediction probePrediction = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, triggerProbeFleet, Missions.Attack, triggerSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
									var triggerFleetId = await _fleetScheduler.SendFleet(fromCelestial, triggerProbeFleet, target.Celestial.Coordinate, Missions.Attack, triggerSpeed);
									if (triggerFleetId <= (int) SendFleetCode.GenericError) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Debris field trigger probe for {target.Celestial.Coordinate} failed (code {triggerFleetId}) - attack goes out without recycler preparation.");
									} else {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Debris field trigger probe sent to {target.Celestial.Coordinate} (attack mission, does not return), arrival in ~{TimeSpan.FromSeconds(probePrediction.Time)}.");
										// PATCH 119: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 139
										triggerProbeFleetIdForGuard = triggerFleetId;
										// PATCH 88, 87: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 91
										// PATCH 129: mindestens bis zur berechneten Ankunft der Sonde (+10s) warten statt fix 30s -
										// bei weiten Zielen (andere Galaxie) war das Truemmerfeld nach 30s evtl. noch nicht da.
										// Obergrenze 300s, damit AutoFarm nicht zu lange blockiert.
										int triggerProbeGraceSeconds = (int) Math.Min(300, Math.Max(30, (long) probePrediction.Time + 10));
										Heartbeat();
										await Task.Delay(TimeSpan.FromSeconds(triggerProbeGraceSeconds), _ct);

										// 2 Slots noetig (Angriffsflotte + Recycler zusammen) - nicht
										// abbrechen, wenn die gerade nicht da sind, sondern warten.
										_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
										int slotsToLeaveFreeForBundle = (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree;
										if (_tbotInstance.UserData.slots.Free - slotsToLeaveFreeForBundle < 2) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: not enough free slots for attack+recycler ({_tbotInstance.UserData.slots.Free} free, {slotsToLeaveFreeForBundle} reserved) - waiting...");
											await WaitForFreeSlots(_tbotInstance.UserData.slots.Free, slotsToLeaveFreeForBundle, 2);
										}

										fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Ships);
										fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.LFBonuses);
										float recyclerCargoBonus = fromCelestial.LFBonuses?.GetShipCargoBonus(Buildables.Recycler) ?? 0;
										Resources predictedDebris = new Resources { Metal = escortSimResult.Debris.Metal, Crystal = escortSimResult.Debris.Crystal, Deuterium = escortSimResult.Debris.Deuterium };
										// PATCH 135: see AutoFarmWorker.PATCHNOTES.md, section 153 - the simulator ignored Pathfinder/
										// Reaper/Crawler and left deuterium out of the debris field (1:34:6: sim 33.0M,
										// actual 42.1M -> too few recyclers). Lower bound: fleet debris from the report + debris of
										// own losses (both incl. deuterium). The larger value wins.
										{
											double fleetToDebris135 = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FleetToDebrisPercent", 30.0) / 100.0;
											long reportFleetDebris135 = EstimateReportFleetDebris(target.Report);
											long ownLossDebris135 = (long) ((escortSimResult.AttackerLosses?.Total() ?? 0) * fleetToDebris135);
											long fallbackDebris135 = reportFleetDebris135 + ownLossDebris135;
											long simDebris135 = predictedDebris.TotalResources;
											if (fallbackDebris135 > simDebris135) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: simulated debris {simDebris135:N0} < estimate report {reportFleetDebris135:N0} + own losses {ownLossDebris135:N0} = {fallbackDebris135:N0} - recyclers sized by estimate.");
												predictedDebris = new Resources { Metal = predictedDebris.Metal + (fallbackDebris135 - simDebris135), Crystal = predictedDebris.Crystal, Deuterium = predictedDebris.Deuterium };
											}
										}
										long recyclersNeeded = _calculationService.CalcShipNumberForPayload(predictedDebris, Buildables.Recycler, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, recyclerCargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
										double recyclerReservePercent = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "RecyclerReservePercent", 0.0);
										recyclersNeeded += (long) Math.Ceiling(recyclersNeeded * (recyclerReservePercent / 100.0));
										long availableRecyclers = fromCelestial.Ships.GetAmount(Buildables.Recycler);

										if (availableRecyclers <= 0) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"No recycler available on {fromCelestial.ToString()} to collect debris at {target.Celestial.Coordinate}.");
										} else {
											long recyclersToSend = Math.Min(recyclersNeeded, availableRecyclers);
											recyclerDebrisCoordinate = new Coordinate(target.Celestial.Coordinate.Galaxy, target.Celestial.Coordinate.System, target.Celestial.Coordinate.Position, Celestials.Debris);
											recyclerFleetToSend = new Ships().Add(Buildables.Recycler, recyclersToSend);

											// PATCH 63: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 92
											FleetPrediction atkPredictionForRecycler = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
											decimal fastestRecyclerSpeed = _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Max();
											FleetPrediction recyclerAtFastest = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, recyclerDebrisCoordinate, recyclerFleetToSend, Missions.Harvest, fastestRecyclerSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
											// PATCH 126: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 146 - Ankunft koordinieren:
											// Recycler fliegt IMMER mit voller Geschwindigkeit (Vorrang), Ankunft soll
											// RecyclerArrivalGapMinSec..MaxSec nach der Hauptflotte liegen.
											//   Recycler waere zu frueh -> Recycler verzoegert starten (persistiert, Neustart-sicher)
											//   Recycler waere zu spaet -> Angriff langsamer (spart Sprit), Rest per kurzer
											//                              Angriffsverzoegerung (max. MaxAttackDelaySec)
											bool coordinateRecyclerArrival = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "CoordinateRecyclerArrival", true);
											if (coordinateRecyclerArrival) {
												long gapMin = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "RecyclerArrivalGapMinSec", (long) 15);
												long gapMax = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "RecyclerArrivalGapMaxSec", (long) 120);
												if (gapMax < gapMin) gapMax = gapMin;
												long gapTarget = (gapMin + gapMax) / 2;
												long maxAttackDelaySec = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxAttackDelaySec", (long) 600);
												// PATCH 127: berechnete Zeiten mit den gemessenen Korrekturfaktoren anpassen
												var flightCal = GetFlightCalibration();
												long recT = (long) Math.Round(recyclerAtFastest.Time * flightCal.RecyclerFactor);
												long atkT = (long) Math.Round(atkPredictionForRecycler.Time * flightCal.AttackFactor);
												recyclerGapTargetSec = gapTarget;
												recyclerCorrectedFlightSec = recT;
												recyclerSpeed = fastestRecyclerSpeed;

												if (atkT + gapMin > recT) {
													// Recycler waere bei voller Geschwindigkeit vor/zu knapp an der Hauptflotte
													recyclerDelaySec = atkT + gapTarget - recT;
												} else if (atkT + gapMax < recT) {
													// Recycler waere zu spaet - Angriff auf die langsamste Stufe bremsen, die
													// noch mindestens gapMin vor dem Recycler ankommt
													long configuredMaxFlight126 = !SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime")
														? 86400
														: ((long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ? long.MaxValue : (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime);
													decimal bestAttackSpeed = speed;
													long bestAttackT = atkT;
													foreach (decimal candidateSpeed in _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Where(s => s < speed)) {
														long candidateT = (long) Math.Round(_calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, candidateSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time * flightCal.AttackFactor);
														if (candidateT <= recT - gapMin && candidateT <= configuredMaxFlight126 && candidateT > bestAttackT) {
															bestAttackT = candidateT;
															bestAttackSpeed = candidateSpeed;
														}
													}
													if (bestAttackSpeed != speed) {
														_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: attack slowed from {speed * 10}% to {bestAttackSpeed * 10}% so the recycler (100%, {TimeSpan.FromSeconds(recT)}) arrives just behind it.");
														speed = bestAttackSpeed;
														atkT = bestAttackT;
													}
													if (atkT + gapMax < recT) {
														long neededDelay = recT - gapTarget - atkT;
														attackDelaySec = Math.Min(neededDelay, Math.Max(0, maxAttackDelaySec));
														if (attackDelaySec < neededDelay) {
															_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: required attack delay {neededDelay}s exceeds AutoFarm.MaxAttackDelaySec ({maxAttackDelaySec}s) - recycler arrives {neededDelay - attackDelaySec + gapTarget}s after the main fleet.");
														}
													}
												}
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: Ankunftsplan - Hauptflotte {speed * 10}% ({TimeSpan.FromSeconds(atkT)}{(attackDelaySec > 0 ? $", Start +{attackDelaySec}s" : "")}), Recycler 100% ({TimeSpan.FromSeconds(recT)}{(recyclerDelaySec > 0 ? $", Start +{TimeSpan.FromSeconds(recyclerDelaySec)}" : "")}), Zielabstand {gapTarget}s.");
												recyclerPlannedAttackFlightSec = atkT;
											} else if (recyclerAtFastest.Time >= atkPredictionForRecycler.Time) {
												recyclerSpeed = fastestRecyclerSpeed;
											} else {
												recyclerSpeed = 0;
												foreach (decimal candidateSpeed in _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).OrderByDescending(s => s)) {
													FleetPrediction candidatePrediction = _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, recyclerDebrisCoordinate, recyclerFleetToSend, Missions.Harvest, candidateSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
													if (candidatePrediction.Time >= atkPredictionForRecycler.Time) {
														recyclerSpeed = candidateSpeed;
														break;
													}
												}
												if (recyclerSpeed == 0) {
													recyclerSpeed = fastestRecyclerSpeed;
												}
												_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: attack fleet slowed to {TimeSpan.FromSeconds(atkPredictionForRecycler.Time)} flight time by the Tier-2 escort (Bomber/Reaper) - recycler throttled to {recyclerSpeed * 10}% so it only arrives afterwards.");
											}
										}
									}
								}
							}

							// PATCH 90, 86, 87: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 93
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							// PATCH 119: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 139 - eigene Truemmerfeld-Trigger-Sonde vom Check ausschliessen
							if (AutoFarmAttackPolicy.IsAttackInProgress(target, _tbotInstance.UserData.fleets, triggerProbeFleetIdForGuard)) {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Skipping attack on {target.Celestial.Coordinate}: an attack fleet is already in flight to this target (duplicate-attack guard).");
								_tbotInstance.UserData.farmTargets.Remove(target);
								target.State = FarmState.AttackSent;
								target.AttackSentAt = await _tbotOgameBridge.GetDateTime();
								_tbotInstance.UserData.farmTargets.Add(target);
								continue;
							}

							// PATCH 126: Recycler waere selbst mit 100% zu spaet -> Recycler ZUERST schicken,
							// Hauptflotte danach um attackDelaySec verzoegert, damit sie knapp vorher ankommt.
							bool recyclerAlreadyHandled = false;
							if (recyclerFleetToSend != null && attackDelaySec > 0) {
								DateTime recyclerGiveUpUtc = DateTime.UtcNow.AddSeconds(attackDelaySec + recyclerPlannedAttackFlightSec + 1800);
								long? actualRecyclerArriveIn = await SendRecyclerOrScheduleRetryAsync(fromCelestial, recyclerFleetToSend, recyclerDebrisCoordinate, recyclerSpeed, recyclerGiveUpUtc);
								freeSlots--;
								recyclerAlreadyHandled = true;
								// PATCH 129: Vorabversand gescheitert (z.B. "Kein Truemmerfeld vorhanden") - der Recycler
								// geht dann ohnehin erst nach dem Kampf raus, die Hauptflotte noch zu verzoegern bringt nichts.
								if (!actualRecyclerArriveIn.HasValue && attackDelaySec > 0) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: recycler pre-dispatch failed - main fleet starts immediately instead of in {attackDelaySec}s, recycler follows after the battle.");
									attackDelaySec = 0;
								}
								// PATCH 127: Angriffsverzoegerung an der ECHTEN Recycler-Ankunft ausrichten
								if (actualRecyclerArriveIn.HasValue && recyclerGapTargetSec > 0) {
									long correctedAtkT = Math.Max(0, recyclerPlannedAttackFlightSec);
									long maxAttackDelay127 = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "MaxAttackDelaySec", (long) 600);
									long newAttackDelay = Math.Clamp(actualRecyclerArriveIn.Value - recyclerGapTargetSec - correctedAtkT, 0, Math.Max(0, maxAttackDelay127));
									if (newAttackDelay != attackDelaySec) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: attack delay adjusted from {attackDelaySec}s to {newAttackDelay}s after the actual recycler arrival ({actualRecyclerArriveIn}s).");
										attackDelaySec = newAttackDelay;
									}
								}
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: Recycler vorausgeschickt, Hauptflotte startet in {attackDelaySec}s.");
								long remainingDelayMs = attackDelaySec * 1000;
								while (remainingDelayMs > 0) {
									Heartbeat();
									long chunk = Math.Min(remainingDelayMs, 20000);
									await Task.Delay(TimeSpan.FromMilliseconds(chunk), _ct);
									remainingDelayMs -= chunk;
								}
							}

							var fleetId = await _fleetScheduler.SendFleet(fromCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);
							if (recyclerAlreadyHandled && fleetId <= (int) SendFleetCode.GenericError) {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: main fleet NOT sent after the delay (code {fleetId}) - recyclers are cancelled/recalled.");
								// PATCH 133: sonst fliegt der Recycler umsonst bzw. ein geplanter Wiederholungsversuch laeuft ins Leere
								await CancelRecyclersForDebrisAsync(recyclerDebrisCoordinate);
							}

							if (fleetId > (int) SendFleetCode.GenericError) {
								freeSlots--;

								// PATCH 95: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 94
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attack sent: {target.Celestial.Coordinate} for {loot}.");
								_successfulTargets.RecordAttack(target.Celestial.Coordinate, loot);
								_tbotInstance.UserData.farmTargets.Remove(target);
								target.State = FarmState.AttackSent;
								// PATCH 86: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 95
								target.AttackSentAt = await _tbotOgameBridge.GetDateTime();
								target.ConsumedReportId = target.Report?.ID > 0 ? target.Report.ID : target.ConsumedReportId;
								_tbotInstance.UserData.farmTargets.Add(target);
								if (!_stateStore.RecordAttack(target, loot, DateTime.UtcNow, fromCelestial.Coordinate, Missions.Attack.ToString()))
									_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
										$"Attack sent to {target.Celestial.Coordinate}, but it could not be recorded in the AutoFarm database.");
								// PATCH 140: remember sent ships for loss tracking (real attacks only, no trigger probe)
								RecordFleetForLossTracking(fleetId, fromCelestial, target.Celestial.Coordinate, attackingShips, speed, loot);
								PersistTargets();

								// PATCH 74: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 96
								// PATCH 126: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 146 - Recycler je nach
								// Ankunftsplan sofort, nach kurzer Wartezeit (<= 120s, inline) oder zeitversetzt
								// (persistierter Auftrag, Neustart-sicher). Fehlschlaege (z.B. Truemmerfeld noch
								// nicht da, kein Slot frei) werden erneut versucht (Taktik siehe PATCH 128).
								// PATCH 127: echte Ankunft der Hauptflotte messen -> Kalibrierung + Recycler-Start
								// exakt daran ausrichten (statt nur an der berechneten Flugzeit)
								if (recyclerFleetToSend != null && !recyclerAlreadyHandled && recyclerGapTargetSec > 0) {
									long? actualAttackArriveIn = await GetActualArriveInAsync(fleetId);
									if (actualAttackArriveIn.HasValue) {
										long rawAttackPrediction = (long) _calculationService.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, fromCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time;
										RecordFlightSample(false, rawAttackPrediction, actualAttackArriveIn.Value);
										long newRecyclerDelay = Math.Max(0, actualAttackArriveIn.Value + recyclerGapTargetSec - recyclerCorrectedFlightSec);
										if (newRecyclerDelay != recyclerDelaySec) {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{target.Celestial.Coordinate}: recycler start adjusted from +{recyclerDelaySec}s to +{newRecyclerDelay}s after the actual arrival of the main fleet (in {actualAttackArriveIn}s).");
											recyclerDelaySec = newRecyclerDelay;
										}
										recyclerPlannedAttackFlightSec = actualAttackArriveIn.Value;
									}
								}
								if (recyclerFleetToSend != null && !recyclerAlreadyHandled) {
									DateTime recyclerGiveUpUtc = DateTime.UtcNow.AddSeconds(Math.Max(recyclerPlannedAttackFlightSec, 0) + 1800);
									if (recyclerDelaySec > 120) {
										SchedulePendingRecycler(fromCelestial, recyclerFleetToSend, recyclerDebrisCoordinate, recyclerSpeed, DateTime.UtcNow.AddSeconds(recyclerDelaySec), recyclerGiveUpUtc);
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Recycler ({recyclerFleetToSend.GetAmount(Buildables.Recycler)}x) scheduled for {recyclerDebrisCoordinate}: start in {TimeSpan.FromSeconds(recyclerDelaySec)} at {recyclerSpeed * 10}% (saved restart-safe).");
									} else {
										if (recyclerDelaySec > 0) {
											Heartbeat();
											await Task.Delay(TimeSpan.FromSeconds(recyclerDelaySec), _ct);
										}
										await SendRecyclerOrScheduleRetryAsync(fromCelestial, recyclerFleetToSend, recyclerDebrisCoordinate, recyclerSpeed, recyclerGiveUpUtc);
										freeSlots--;
									}
								}
							} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
								stop = true;
								return;
							} else if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
									"Another worker took the last available slot; keeping the attack pending for the next cycle.");
								break;
							} else {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
									$"Attack dispatch failed for {target.Celestial.Coordinate}; keeping the target pending.");
								continue;
							}
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
								$"AutoFarm attack budget exhausted: {_tbotInstance.UserData.slots.Free} slots free, {slotBudget.OwnedSlots}/{slotBudget.MaxSlots} AutoFarm slots used.");
							break;
						}
					}
					// PATCH 100: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 98
					keepInterleaving = !sequentialModeActive && stopAutoFarm && !finishedFullScan && numProbed > 0 && interleavePass < maxInterleavePasses;
					if (keepInterleaving) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Pass {interleavePass}: probe/slot limit reached, scan not complete yet - reports/attacks have already been processed, continuing to scan directly (pass {interleavePass + 1}).");
					}
					} while (keepInterleaving);
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarm Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
			} finally {
				PersistTargets();
				if (stopAfterFullScan && finishedFullScan) {
					stop = true;
				}

				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attacked targets: {_tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackSent).Count()}");
				// PATCH 123: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 143 - Bugfix:
				// Diese Zeile hat bisher AM ENDE JEDES ZYKLUS ausnahmslos ALLE Ziele im Status
				// ProbesSent geloescht (und sofort mitpersistiert). Da eine Sondenflugzeit immer
				// > 0 ist, kann der zugehoerige Spionagebericht praktisch nie im selben Zyklus
				// eintreffen, in dem die Sonde losgeschickt wurde - jedes noch auf seinen Bericht
				// wartende Ziel wurde also garantiert geloescht, BEVOR AutoFarmProcessReports() es
				// je auswerten konnte. Folgen: (1) SequentialMode's eigene Bremse
				// (sequentialModeProbeInFlight, siehe oben) zaehlte ProbesSent-Ziele und war dadurch
				// nie aktiv - es wurde immer sofort die naechste Sonde losgeschickt, obwohl der
				// Bericht der vorherigen nachweislich noch nicht ausgewertet war. (2) Kam der
				// Bericht erst nach diesem Loeschvorgang zurueck, fand AutoFarmProcessReports()
				// keinen passenden farmTargets-Eintrag mehr zu der Koordinate und der Bericht wurde
				// (ohne ProcessAllReports) stillschweigend verworfen - verschwendete Spionage-Slots
				// ohne je zu einem Angriff zu fuehren. Fix: Zeile ersatzlos entfernt. Ziele im
				// Status ProbesSent werden bereits regulaer durch AutoFarmProcessReports() aufgeloest
				// (State-Wechsel zu ProbesPending/AttackPending/blacklisted), sobald ihr Bericht
				// eintrifft - eine zusaetzliche pauschale Loeschung hier ist nicht noetig und war der
				// eigentliche Bug.
				PersistTargets();
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Stopping feature.");
						await EndExecution();
					} else {
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						// PATCH 100/102: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 99. PATCH 102-
						// Bugfix: zwischen dem tatsaechlichen Sondenversand (weiter oben im selben
						// Execute()-Aufruf) und HIER liegen AutoFarmProcessReports() + der komplette
						// Angriffs-Dispatch - je nach Eskortensimulationen u.U. mehrere Sekunden bis
						// Minuten. Ohne Korrektur wuerde die volle Flugzeit ERST AB JETZT nochmal
						// draufgerechnet, der naechste Zyklus liefe also systematisch zu spaet (um genau
						// die in diesem Zyklus verbrauchte Verarbeitungszeit verzoegert). Deshalb hier
						// die seit dem Versand (_sequentialModeProbeSentAtUtc) bereits verstrichene Zeit
						// abziehen, damit der naechste Zyklus wieder auf den tatsaechlichen
						// Ankunftszeitpunkt der Sonde trifft, nicht auf "Ankunftszeitpunkt + Zykluszeit".
						if (_sequentialModeNextArrivalSeconds.HasValue && _sequentialModeProbeSentAtUtc.HasValue) {
							double elapsedSeconds = (DateTime.UtcNow - _sequentialModeProbeSentAtUtc.Value).TotalSeconds;
							double remainingSeconds = _sequentialModeNextArrivalSeconds.Value - elapsedSeconds;
							long bufferMs = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
							interval = (long) Math.Max(0, remainingSeconds * 1000) + bufferMs;
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Sequential mode: next cycle in {TimeSpan.FromMilliseconds(interval)} (probe arrival in {_sequentialModeNextArrivalSeconds.Value}s, of which {elapsedSeconds:F1}s already elapsed during report/attack processing, + buffer).");
						} else if (sequentialModeActive) {
							// PATCH 102: dieser Zyklus hat KEINE neue Sonde verschickt - entweder weil der
							// Scan-Check (siehe Kommentar bei sequentialModeProbeInFlight weiter oben) noch
							// den GERADE ERST in diesem Zyklus verarbeiteten Bericht der vorherigen Sonde
							// gesehen hat (Scan-Check laeuft VOR AutoFarmProcessReports() im selben Zyklus,
							// blockiert die naechste Sonde also noch fuer genau diesen einen Durchlauf),
							// oder weil sonst kein Slot/keine Sonde verfuegbar war. Ohne diesen Sonderfall
							// griff hier bisher das normale (fuer Batch-Modus gedachte, viel traegere)
							// CheckIntervalMin/Max - dadurch entstand nach jedem Angriff/jeder Auswertung
							// eine unnoetig lange Pause, bevor die naechste Sonde ueberhaupt losging. Im
							// Sequential-Modus stattdessen kurz nachfassen, damit der naechste Zyklus (der
							// dann die neue Sonde verschickt) praktisch nahtlos an Pruefung/Angriff anschliesst.
							// PATCH 102: bewusst fix 1000ms statt eines der RandomizeHelper-IntervalType-
							// Tiers (kleinste Stufe AFewSeconds waere hier noch spuerbar traeger) - dieser
							// Zweig macht keine ziel-/serverseitige Anfrage, ist also kein "Request-Spam"-
							// Risiko wie bei echten Sonden-/Flottenversand-Intervallen; er soll nur so
							// schnell wie sinnvoll pruefbar wieder feuern, damit die naechste Sonde
							// praktisch nahtlos an Pruefung/Angriff anschliesst. Kein Random-Jitter noetig,
							// da hier kein an den Server gerichteter Request dahintersteckt.
							interval = 1000;
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Sequential mode: no new probe sent this cycle - short retry in {TimeSpan.FromMilliseconds(interval)} instead of CheckIntervalMin/Max.");
						} else if (fillSlotsActive && TryGetFillSlotsInterval(finishedFullScan, out long fillInterval)) {
							// PATCH 136: see AutoFarmWorker.PATCHNOTES.md, section 154
							interval = fillInterval;
						} else {
							interval = RandomizeHelper.CalcRandomInterval(
								(int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMin,
								(int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMax);
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						}
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm,
							$"Next autofarm check at {newTime.ToString()} (rechecking with any remaining fleet slots).");
						await _tbotOgameBridge.CheckCelestials();
					}
				}
			}
		}

		// PATCH 97: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 100
		private (bool IsHopeless, long MaxCapacity) AssessStorageCapacity(EspionageReport report, long minimumResourcesThreshold) {
			if (!report.HasBuildingsInformation) {
				return (false, -1);
			}
			long maxCapacity = _calculationService.CalcDepositCapacity(report.MetalStorage.GetValueOrDefault())
				+ _calculationService.CalcDepositCapacity(report.CrystalStorage.GetValueOrDefault())
				+ _calculationService.CalcDepositCapacity(report.DeuteriumTank.GetValueOrDefault());
			return (maxCapacity < minimumResourcesThreshold, maxCapacity);
		}

		// PATCH 134: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 152 - geschaetzte Truemmer der im Bericht
		// gesehenen Flotte (Flottenwert x FleetToDebrisPercent). Nur wenn verteidigte Ziele angegriffen und
		// Truemmer eingesammelt werden (sonst bringt die Flotte keinen Ertrag) - sonst 0.
		private long ReportFleetDebrisForThreshold(EspionageReport report) {
			try {
				if (!report.HasFleetInformation) return 0;
				bool attackDefended = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AttackDefendedTargets")
					&& (bool) _tbotInstance.InstanceSettings.AutoFarm.AttackDefendedTargets;
				bool harvest = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AutoHarvestDebrisAfterAttack")
					&& (bool) _tbotInstance.InstanceSettings.AutoFarm.AutoHarvestDebrisAfterAttack;
				if (!attackDefended || !harvest) return 0;
				return EstimateReportFleetDebris(report);
			} catch {
				return 0;
			}
		}

		// PATCH 135: see AutoFarmWorker.PATCHNOTES.md, section 153 - plain estimate of fleet debris
		// from the report (no config gating), also used for the recycler count.
		private long EstimateReportFleetDebris(EspionageReport report) {
			try {
				if (report == null || !report.HasFleetInformation) return 0;
				Ships fleet = new(
					report.LightFighter.GetValueOrDefault(),
					report.HeavyFighter.GetValueOrDefault(),
					report.Cruiser.GetValueOrDefault(),
					report.Battleship.GetValueOrDefault(),
					report.Battlecruiser.GetValueOrDefault(),
					report.Bomber.GetValueOrDefault(),
					report.Destroyer.GetValueOrDefault(),
					report.Deathstar.GetValueOrDefault(),
					report.SmallCargo.GetValueOrDefault(),
					report.LargeCargo.GetValueOrDefault(),
					report.ColonyShip.GetValueOrDefault(),
					report.Recycler.GetValueOrDefault(),
					report.EspionageProbe.GetValueOrDefault(),
					report.SolarSatellite.GetValueOrDefault(),
					report.Crawler.GetValueOrDefault(),
					report.Reaper.GetValueOrDefault(),
					report.Pathfinder.GetValueOrDefault()
				);
				double fleetToDebris = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm, "FleetToDebrisPercent", 30.0) / 100.0;
				return (long) (EstimateFleetResourceValue(fleet) * fleetToDebris);
			} catch {
				return 0;
			}
		}

		private async Task AutoFarmProcessReports() {
			List<EspionageReportSummary> summaryReports;
			try {
				summaryReports = await _ogameService.GetEspionageReports() ?? new List<EspionageReportSummary>();
			} catch (Exception e) {
				// Report cleanup is best effort. A temporary OGame/API failure must not
				// prevent the attack phase from processing targets already marked pending.
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
					$"Unable to retrieve espionage reports; keeping existing attack-pending targets: {e.Message}");
				return;
			}

			int nonActionableQueryLimit = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "ReportQueryLimitForNonActionable")
				? (int) _tbotInstance.InstanceSettings.AutoFarm.ReportQueryLimitForNonActionable
				: 3;

			foreach (var summary in summaryReports) {
				if (summary == null || summary.Type == EspionageReportType.Action)
					continue;

				// PATCH 73, 59-Kommentar: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 101
				if (_nonActionableReportAttempts.TryGetValue(summary.ID, out int priorAttempts) && priorAttempts >= nonActionableQueryLimit) {
					continue;
				}

				try {
					var report = await _ogameService.GetEspionageReport(summary.ID);
					if (report?.Coordinate == null) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
							$"Ignoring malformed espionage report {summary.ID}: no coordinate was returned.");
						continue;
					}

					// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 102
					bool reportBlacklistActive = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
						SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "Active") &&
						(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;
					if (reportBlacklistActive && _blacklist.IsBlacklisted(report.Coordinate)) {
						// PATCH 81, 83: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 103
						var blacklistedTarget = _blacklist.GetBlacklistedTarget(report.Coordinate);
						if (blacklistedTarget != null && (blacklistedTarget.Reason == BlacklistReason.HasDefense || blacklistedTarget.Reason == BlacklistReason.Unprofitable)) {
							// PATCH 141: logged once per report and interval instead of every cycle
							LogThrottled($"blreport:{report.ID}", LogLevel.Debug,
								$"Keeping espionage report {report.ID} for {blacklistedTarget.Reason}-blacklisted target {report.Coordinate} - not deleted.");
							continue;
						}
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
							$"Deleting espionage report {report.ID} for blacklisted target {report.Coordinate} (Reason: {blacklistedTarget?.Reason.ToString() ?? "unknown"}).");
						try {
							await _ogameService.DeleteReport(report.ID);
						} catch (Exception deleteEx) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
								$"Could not delete espionage report {report.ID} for blacklisted target {report.Coordinate}: {deleteEx.Message}");
						}
						continue;
					}
					// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 104
					if (_stateStore.HasConsumedReport(report.ID)) {
						// PATCH 141: logged once per report and interval instead of every cycle
						LogThrottled($"consumed:{report.ID}", LogLevel.Debug,
							$"Ignoring consumed espionage report {report.ID} for {report.Coordinate}; keeping it and waiting for a fresh report after re-spy.");
						continue;
					}
					if (DateTime.Compare(report.Date.AddMinutes((double) _tbotInstance.InstanceSettings.AutoFarm.KeepReportFor), await _tbotOgameBridge.GetDateTime()) < 0) {
						// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 105
						continue;
					}

					// PATCH 93, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 106
					bool deleteReportForInsufficientLoot = false;

					if (_tbotInstance.UserData.farmTargets.Any(t => t.HasCoords(report.Coordinate))) {
						FarmTarget target;
						var matchingTarget = _tbotInstance.UserData.farmTargets.Where(t => t.HasCoords(report.Coordinate));
						if (matchingTarget.Count() == 0) {
							if (!report.IsInactive)
								continue;
							var galaxyInfo = await _ogameService.GetGalaxyInfo(report.Coordinate.Galaxy, report.Coordinate.System);
							var planet = galaxyInfo.Planets.FirstOrDefault(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation && p.HasCoords(report.Coordinate));
							if (planet != null) {
								target = GetFarmTarget(planet);
								if (target == null)
									continue;
							} else {
								continue;
							}
						} else {
							target = matchingTarget.First();
						}

						if (!AutoFarmAttackPolicy.CanProcessReport(target, report)) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
								$"Ignoring already consumed or still active espionage report {report.ID} for {report.Coordinate}; keeping report in inbox.");
							continue;
						}

						if (AutoFarmAttackPolicy.IsRepeatedNonActionableReport(target, report)) {
							// PATCH 73, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 107
							_nonActionableReportAttempts[report.ID] = _nonActionableReportAttempts.GetValueOrDefault(report.ID) + 1;
							continue;
						}

						var newFarmTarget = target;

						if (target.Report != null && DateTime.Compare(report.Date, target.Report.Date) < 0) {
							// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 108
							continue;
						}

						// PATCH 65: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 109
						Buildables cargoShip = GetConfiguredCargoType();
						bool isUsingProbes = cargoShip == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 1 ? true : false;
						newFarmTarget.Report = report;
						// PATCH 134: Flottentruemmer zaehlen beim Beute-Minimum mit (siehe ReportFleetDebrisForThreshold)
						long reportFleetDebris = ReportFleetDebrisForThreshold(report);
						bool lootByFleetDebris = reportFleetDebris > 0 && report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources + reportFleetDebris > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources;
						if (lootByFleetDebris) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"{report.Coordinate}: loot {report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources:N0} + estimated fleet debris {reportFleetDebris:N0} reach AutoFarm.MinimumResources.");
						}
						if (lootByFleetDebris
							|| _tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Metal" && report.Loot(_tbotInstance.UserData.userInfo.Class).Metal > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| _tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Crystal" && report.Loot(_tbotInstance.UserData.userInfo.Class).Crystal > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| _tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Deuterium" && report.Loot(_tbotInstance.UserData.userInfo.Class).Deuterium > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "" && report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources)) {
							if (!report.HasFleetInformation || !report.HasDefensesInformation) {
								if (target.State == FarmState.ProbesRequired)
									newFarmTarget.State = FarmState.FailedProbesRequired;
								else if (target.State == FarmState.FailedProbesRequired)
									newFarmTarget.State = FarmState.NotSuitable;
								else
									newFarmTarget.State = FarmState.ProbesRequired;

								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Need more probes on {report.Coordinate}. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
							} else if (report.IsDefenceless(isUsingProbes)) {
								newFarmTarget.State = FarmState.AttackPending;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
							} else if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "AttackDefendedTargets") &&
								(bool) _tbotInstance.InstanceSettings.AutoFarm.AttackDefendedTargets) {
								// PATCH 39: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 110
								newFarmTarget.State = FarmState.AttackPending;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Defended target {report.Coordinate} kept pending for simulated escort evaluation. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
							} else {
								newFarmTarget.State = FarmState.NotSuitable;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - defences present.");
							// PATCH 69: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 111
							}
						} else {
							newFarmTarget.State = FarmState.NotSuitable;
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - insufficient loot ({report.Loot(_tbotInstance.UserData.userInfo.Class)})");
							// PATCH 94, 93: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 112
							deleteReportForInsufficientLoot = true;
							bool blacklistActiveLowRes = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
								SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "Active") &&
								(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;
							if (blacklistActiveLowRes) {
								int hoursUntilReset = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "ResetAfterHours", 48);
								// PATCH 97: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 113
								var storageAssessment = AssessStorageCapacity(report, (long) _tbotInstance.InstanceSettings.AutoFarm.MinimumResources);
								// PATCH 134: Monde haben keine Minen - dort liegen nur hingebrachte Rohstoffe, die das Lager nicht deckelt
								if (storageAssessment.IsHopeless && report.Coordinate.Type != Celestials.Moon) {
									hoursUntilReset = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "PermanentResetAfterHours", 8760);
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate}: maximum storage capacity ({storageAssessment.MaxCapacity:N0}) is below AutoFarm.MinimumResources - structurally never profitable, {hoursUntilReset}h instead of the usual {SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "ResetAfterHours", 48)}h blacklisted.");
								}
								_blacklist.AddTarget(report.Coordinate, BlacklistReason.LowResources, hoursUntilReset);
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} blacklisted for {hoursUntilReset}h (low resources: {report.Loot(_tbotInstance.UserData.userInfo.Class)}).");
							}
						}

						_tbotInstance.UserData.farmTargets.Remove(target);
						_tbotInstance.UserData.farmTargets.Add(newFarmTarget);
					} else {
					// PATCH 123: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 143 - Nutzer-Wunsch:
					// ProcessAllReports fest auf true hartkodiert (Config-Wert wird nicht mehr
					// gelesen/beachtet). Grund: durch den PATCH-123-Fix des ProbesSent-Wipe-Bugs
					// (siehe Execute()) sollte das eigentlich seltener noetig sein, aber als
					// zusaetzliches Sicherheitsnetz gegen verlorene/nicht zugeordnete Berichte
					// (z.B. durch Scan-Zyklus-Timing oder manuell ausgeloeste Spionage) soll dieser
					// Pfad immer aktiv sein, unabhaengig von der Config.
					bool processAllReports = true;
					if (processAllReports && report.IsInactive) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Processing report for {report.Coordinate} not scanned by TBot (ProcessAllReports enabled)...");
						var galaxyInfo = await _ogameService.GetGalaxyInfo(report.Coordinate.Galaxy, report.Coordinate.System);
						var planet = galaxyInfo.Planets.FirstOrDefault(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation && p.HasCoords(report.Coordinate));
						if (planet != null) {
							var target = GetFarmTarget(planet);
							if (target != null) {
								if (!AutoFarmAttackPolicy.CanProcessReport(target, report)) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
										$"Ignoring already consumed or still active espionage report {report.ID} for {report.Coordinate}; keeping report in inbox.");
									continue;
								}

								if (AutoFarmAttackPolicy.IsRepeatedNonActionableReport(target, report)) {
									// PATCH 73, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 114
									_nonActionableReportAttempts[report.ID] = _nonActionableReportAttempts.GetValueOrDefault(report.ID) + 1;
									continue;
								}

								var newFarmTarget = target;
								// PATCH 65: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 115
								Buildables cargoShip = GetConfiguredCargoType();
								bool isUsingProbes = cargoShip == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 1 ? true : false;
								newFarmTarget.Report = report;
								// PATCH 134: Flottentruemmer zaehlen beim Beute-Minimum mit
								if (report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources + ReportFleetDebrisForThreshold(report) > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources) {
									if (report.HasFleetInformation && report.HasDefensesInformation) {
										if (report.IsDefenceless(isUsingProbes)) {
											newFarmTarget.State = FarmState.AttackPending;
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
										} else {
											newFarmTarget.State = FarmState.NotSuitable;
											// PATCH 69: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 116
										}
									}
								} else {
									newFarmTarget.State = FarmState.NotSuitable;
									// PATCH 94: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 117
									deleteReportForInsufficientLoot = true;
									bool blacklistActiveLowRes2 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "Blacklist") &&
										(bool) _tbotInstance.InstanceSettings.AutoFarm.Blacklist.Active;
									if (blacklistActiveLowRes2) {
										int hoursUntilReset = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "ResetAfterHours", 48);
										// PATCH 97: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 118
										var storageAssessment2 = AssessStorageCapacity(report, (long) _tbotInstance.InstanceSettings.AutoFarm.MinimumResources);
										if (storageAssessment2.IsHopeless && report.Coordinate.Type != Celestials.Moon) {
											hoursUntilReset = SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "PermanentResetAfterHours", 8760);
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate}: maximum storage capacity ({storageAssessment2.MaxCapacity:N0}) is below AutoFarm.MinimumResources - structurally never profitable, {hoursUntilReset}h instead of the usual {SettingsService.GetSetting(_tbotInstance.InstanceSettings.AutoFarm.Blacklist, "ResetAfterHours", 48)}h blacklisted.");
										}
										_blacklist.AddTarget(report.Coordinate, BlacklistReason.LowResources, hoursUntilReset);
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} blacklisted for {hoursUntilReset}h (low resources: {report.Loot(_tbotInstance.UserData.userInfo.Class)}).");
									}
								}
								_tbotInstance.UserData.farmTargets.Remove(target);
								_tbotInstance.UserData.farmTargets.Add(newFarmTarget);
							}
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not scanned by TBot, ignoring...");
					}
					}

					// PATCH 59, 81: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 119
					if (reportBlacklistActive && _blacklist.IsBlacklisted(report.Coordinate)) {
						var freshlyBlacklistedTarget = _blacklist.GetBlacklistedTarget(report.Coordinate);
						if (freshlyBlacklistedTarget != null && (freshlyBlacklistedTarget.Reason == BlacklistReason.HasDefense || freshlyBlacklistedTarget.Reason == BlacklistReason.Unprofitable)) {
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
								$"Keeping espionage report {report.ID} for {report.Coordinate} blacklisted during evaluation (Reason: {freshlyBlacklistedTarget.Reason}) - not deleted.");
						} else {
							try {
								await _ogameService.DeleteReport(report.ID);
								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
									$"Deleted espionage report {report.ID} after target {report.Coordinate} was blacklisted during evaluation (Reason: {freshlyBlacklistedTarget?.Reason.ToString() ?? "unknown"}).");
							} catch (Exception deleteEx) {
								_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
									$"Could not delete newly-blacklisted espionage report {report.ID} for {report.Coordinate}: {deleteEx.Message}");
							}
						}
					} else if (deleteReportForInsufficientLoot) {
						// PATCH 93, 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 120
						try {
							await _ogameService.DeleteReport(report.ID);
							_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm,
								$"Deleted espionage report {report.ID} for {report.Coordinate} (insufficient loot, not blacklisted).");
						} catch (Exception deleteEx) {
							_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm,
								$"Could not delete espionage report {report.ID} for {report.Coordinate} (insufficient loot): {deleteEx.Message}");
						}
					}
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarmProcessReports Exception: {e.Message}");
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
					continue;
				}
			}

			// PATCH 59: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 121

		}
	}
}