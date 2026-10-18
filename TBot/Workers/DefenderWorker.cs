using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Helpers;
using TBot.Model;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Tbot.Includes;
using System.Timers;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;

namespace Tbot.Workers {
	internal class DefenderWorker : WorkerBase {
		private static readonly ConcurrentDictionary<int, DateTime> _handledAttackIds = new();
		private static readonly TimeSpan _handledAttackTtl = TimeSpan.FromMinutes(60);

		private sealed class PendingFleetSave {
			public DateTime EarliestImpactUtc;
			public DateTime DispatchAtUtc;
			public long MinFlightTimeSeconds;
			public int Busy;
		}
		private static readonly ConcurrentDictionary<int, PendingFleetSave> _pendingFleetSaves = new();

		private static readonly ConcurrentDictionary<int, System.Threading.Timer> _dispatchTimers = new();

		private void ArmDispatchTimer(int celestialId) {
			if (!_pendingFleetSaves.TryGetValue(celestialId, out var p))
				return;
			long delayMs = (long) Math.Max(0, (p.DispatchAtUtc - DateTime.UtcNow).TotalMilliseconds);
#pragma warning disable VSTHRD101
			System.Threading.Timer timer = new System.Threading.Timer(async _ => {
				try {
					await DispatchPendingAsync(celestialId, "Timer");
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Dispatch timer: error during fleetsave dispatch: {e.Message}");
				}
			}, null, delayMs, System.Threading.Timeout.Infinite);
#pragma warning restore VSTHRD101
			_dispatchTimers.AddOrUpdate(celestialId, timer, (_, old) => { old.Dispose(); return timer; });
		}

		private void RemovePending(int celestialId) {
			_pendingFleetSaves.TryRemove(celestialId, out _);
			if (_dispatchTimers.TryRemove(celestialId, out var t))
				t.Dispose();
		}

		private async Task DispatchPendingAsync(int celestialId, string trackedAttacks) {
			if (!_pendingFleetSaves.TryGetValue(celestialId, out var pending))
				return;
			if (System.Threading.Interlocked.CompareExchange(ref pending.Busy, 1, 0) != 0)
				return;
			try {
				Celestial celestial = _tbotInstance.UserData.celestials.Unique().SingleOrDefault(c => c.ID == celestialId);
				if (celestial == null) {
					RemovePending(celestialId);
					return;
				}
				if (DateTime.UtcNow < pending.DispatchAtUtc.AddMilliseconds(-500)) {
					ArmDispatchTimer(celestialId);
					return;
				}

				TimeSpan safetyMargin = GetFleetSaveSafetyMargin();
				try {
					DoLog(LogLevel.Warning, $"Dispatching FleetSave for {celestial.ToString()} (tracked attacks: {trackedAttacks}), earliest impact {pending.EarliestImpactUtc:u}, safety margin {safetyMargin.TotalSeconds:0}s.");
					await _fleetScheduler.AutoFleetSave(celestial, false, pending.MinFlightTimeSeconds);
				} catch (Exception e) {
					DoLog(LogLevel.Error, $"Could not fleetsave {celestial.ToString()}: an exception has occurred: {e.Message}");
					DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				}

				if (DateTime.UtcNow >= pending.EarliestImpactUtc.AddMinutes(5)) {
					RemovePending(celestialId);
				} else {
					pending.DispatchAtUtc = DateTime.UtcNow.AddSeconds(FleetSaveFollowUpCheckSeconds);
					ArmDispatchTimer(celestialId);
				}
			} finally {
				System.Threading.Interlocked.Exchange(ref pending.Busy, 0);
			}
		}

		private const int DefaultFleetSaveSafetyMarginSeconds = 120;

		private const int FleetSaveFollowUpCheckSeconds = 20;

		private static readonly HttpClient _discordHttpClient = new();

		private async Task SendDiscordAlert(string message) {
			try {
				if (!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "Discord")) {
					await _tbotInstance.SendDiscordMessage(message, NotificationKind.Attack);
					return;
				}
				if (!(bool) _tbotInstance.InstanceSettings.Defender.Discord.Active)
					return;
				string webhookUrl = (string) _tbotInstance.InstanceSettings.Defender.Discord.WebhookUrl;
				if (string.IsNullOrWhiteSpace(webhookUrl))
					return;

				string escaped = message.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
				string json = $"{{\"content\":\"{escaped}\"}}";
				using var content = new StringContent(json, Encoding.UTF8, "application/json");
				var response = await _discordHttpClient.PostAsync(webhookUrl, content, _ct);
				if (!response.IsSuccessStatusCode) {
					DoLog(LogLevel.Warning, $"Discord webhook returned {(int) response.StatusCode} {response.StatusCode}");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Unable to send Discord alert: {e.Message}");
			}
		}

		public volatile bool GhostSleepPassiveMode = false;
		public DateTime? GhostSleepNextAnchorUtc = null;
		private volatile bool _ghostSleepEscalated = false;

		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public DefenderWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ITBotOgamedBridge tbotOgameBridge)
			: base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Checking attacks...");

				await FakeActivity();
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				bool isUnderAttack = await _ogameService.IsUnderAttack();
				DateTime time = await _tbotOgameBridge.GetDateTime();
				List<AttackerFleet> currentAttacks = new();
				DateTime attacksFetchedUtc = DateTime.UtcNow;
				if (isUnderAttack) {
					if ((bool) _tbotInstance.InstanceSettings.Defender.Alarm.Active)
						await Task.Run(() => ConsoleHelpers.PlayAlarm(), _ct);
					DoLog(LogLevel.Warning, "ENEMY ACTIVITY!!!");
					_tbotInstance.UserData.attacks = await _ogameService.GetAttacks();
					attacksFetchedUtc = DateTime.UtcNow;
					currentAttacks = _tbotInstance.UserData.attacks?.ToList() ?? new List<AttackerFleet>();
					foreach (AttackerFleet attack in currentAttacks) {
						await HandleAttack(attack, attacksFetchedUtc);
					}
				} else {
					DoLog(LogLevel.Information, "Your empire is safe");
				}

				try { _tbotInstance.PublishAttackSnapshot(currentAttacks, attacksFetchedUtc); } catch { }
				await ProcessPendingFleetSaves(currentAttacks, attacksFetchedUtc);

				if (_pendingFleetSaves.IsEmpty)
					_ = Task.Run(() => _tbotInstance.AttackLockdown(false));

				if (_ghostSleepEscalated && _pendingFleetSaves.IsEmpty) {
					_ghostSleepEscalated = false;
					GhostSleepPassiveMode = true;
					DoLog(LogLevel.Information, "GhostSleep: no further attack wave - back to PASSIVE.");
					_tbotInstance.GhostSleepEscalate(false);
				}

				long interval;
				if (GhostSleepPassiveMode && GhostSleepNextAnchorUtc.HasValue && DateTime.UtcNow >= GhostSleepNextAnchorUtc.Value) {
					_tbotInstance.GhostSleepRearmDefenderAnchor();
				}
				if (GhostSleepPassiveMode && GhostSleepNextAnchorUtc.HasValue) {
					interval = (long) Math.Max(1000, (GhostSleepNextAnchorUtc.Value - DateTime.UtcNow).TotalMilliseconds);
					DoLog(LogLevel.Information, $"GhostSleep PASSIVE: next short wake check at {GhostSleepNextAnchorUtc.Value.ToLocalTime()}.");
				} else {
					interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMax);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

					if (!_pendingFleetSaves.IsEmpty) {
						DateTime earliestPendingDispatch = _pendingFleetSaves.Values.Min(p => p.DispatchAtUtc);
						double msUntilDispatch = (earliestPendingDispatch - DateTime.UtcNow).TotalMilliseconds;
						if (msUntilDispatch < interval) {
							interval = (long) Math.Max(1000, msUntilDispatch);
						}
					}
				}

				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"An error has occurred while checking for attacks: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} finally {

			}
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.Defender.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Defender";
		}
		public override Feature GetFeature() {
			return Feature.Defender;
		}

		public override LogSender GetLogSender() {
			return LogSender.Defender;
		}


		private async Task FakeActivity() {

			Celestial celestial;
			Celestial randomCelestial;
			var randomActivity = (bool) _tbotInstance.InstanceSettings.Defender.RandomActivity;

			if (randomActivity == false) {
				celestial = _tbotInstance.UserData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Defender.Home.Galaxy)
				.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Defender.Home.System)
				.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Defender.Home.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Defender.Home.Type))
				.SingleOrDefault() ?? new() { ID = 0 };

				if (celestial.ID != 0) {
					DoLog(LogLevel.Information, $"Check from Home ({celestial.Coordinate.Galaxy}:{celestial.Coordinate.System}:{celestial.Coordinate.Position} {celestial.Coordinate.Type})");
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Defences);
				}
			} else {
				randomCelestial = _tbotInstance.UserData.celestials.Shuffle().FirstOrDefault() ?? new() { ID = 0 };

				if (randomCelestial.ID != 0) {
					DoLog(LogLevel.Information, $"Check from Random Celestial");
					randomCelestial = await _tbotOgameBridge.UpdatePlanet(randomCelestial, UpdateTypes.Defences);
				}
			}
			return;
		}

		private TimeSpan GetFleetSaveSafetyMargin() {
			try {
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender.Autofleet, "SafetyMarginSeconds")) {
					long configured = (long) _tbotInstance.InstanceSettings.Defender.Autofleet.SafetyMarginSeconds;
					if (configured > 0)
						return TimeSpan.FromSeconds(configured);
				}
			} catch {
			}
			return TimeSpan.FromSeconds(DefaultFleetSaveSafetyMarginSeconds);
		}

		private void RegisterOrUpdatePendingFleetSave(Celestial attackedCelestial, AttackerFleet attack, DateTime nowUtc) {
			DateTime impactUtc = nowUtc.AddSeconds(attack.ArriveIn);
			TimeSpan safetyMargin = GetFleetSaveSafetyMargin();
			DateTime dispatchAtUtc = impactUtc - safetyMargin;
			long minFlightTime = 2 * attack.ArriveIn + 120 + (RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);

			bool isNew = false;
			_pendingFleetSaves.AddOrUpdate(attackedCelestial.ID,
				addValueFactory: _ => {
					isNew = true;
					return new PendingFleetSave {
						EarliestImpactUtc = impactUtc,
						DispatchAtUtc = dispatchAtUtc,
						MinFlightTimeSeconds = minFlightTime
					};
				},
				updateValueFactory: (_, existing) => {
					if (impactUtc < existing.EarliestImpactUtc) {
						DoLog(LogLevel.Information, $"{attackedCelestial.ToString()}: attack {attack.ID} arrives earlier ({impactUtc:u}) than the tracked threat ({existing.EarliestImpactUtc:u}) - moving FleetSave dispatch from {existing.DispatchAtUtc:u} to {dispatchAtUtc:u}.");
						existing.EarliestImpactUtc = impactUtc;
						existing.DispatchAtUtc = dispatchAtUtc;
						existing.MinFlightTimeSeconds = Math.Max(existing.MinFlightTimeSeconds, minFlightTime);
					} else {
						existing.MinFlightTimeSeconds = Math.Max(existing.MinFlightTimeSeconds, minFlightTime);
					}
					return existing;
				});

			ArmDispatchTimer(attackedCelestial.ID);

			if (isNew) {
				DoLog(LogLevel.Information, $"{attackedCelestial.ToString()}: scheduling FleetSave dispatch at {dispatchAtUtc:u} (impact {impactUtc:u}, safety margin {safetyMargin.TotalSeconds:0}s).");
				if (dispatchAtUtc <= nowUtc) {
					DoLog(LogLevel.Warning, $"{attackedCelestial.ToString()}: safety window for attack {attack.ID} has already passed at detection time - FleetSave will fire on this same check instead of waiting.");
				}
			}
		}

		private async Task ProcessPendingFleetSaves(IEnumerable<AttackerFleet> currentAttacks, DateTime nowUtc) {
			if (_pendingFleetSaves.IsEmpty)
				return;

			List<AttackerFleet> attacksList = currentAttacks?.Where(a => a != null).ToList() ?? new List<AttackerFleet>();

			foreach (var kv in _pendingFleetSaves.ToArray()) {
				int celestialId = kv.Key;
				PendingFleetSave pending = kv.Value;

				Celestial celestial = _tbotInstance.UserData.celestials.Unique().SingleOrDefault(c => c.ID == celestialId);
				if (celestial == null) {
					DoLog(LogLevel.Debug, $"Pending FleetSave: celestial {celestialId} no longer present in account data - dropping.");
					RemovePending(celestialId);
					continue;
				}

				List<AttackerFleet> stillIncoming = attacksList.Where(a => celestial.HasCoords(a.Destination)).ToList();

				if (stillIncoming.Count == 0) {
					DoLog(LogLevel.Information, $"{celestial.ToString()}: no more incoming attacks - cancelling pending FleetSave (was scheduled for {pending.DispatchAtUtc:u}).");
					RemovePending(celestialId);
					continue;
				}

				AttackerFleet earliestAttack = stillIncoming.OrderBy(a => a.ArriveIn).First();
				DateTime recomputedImpact = nowUtc.AddSeconds(earliestAttack.ArriveIn);
				TimeSpan safetyMargin = GetFleetSaveSafetyMargin();
				if (recomputedImpact < pending.EarliestImpactUtc) {
					DoLog(LogLevel.Information, $"{celestial.ToString()}: a faster attack ({earliestAttack.ID}) shortens the FleetSave window from {pending.EarliestImpactUtc:u} to {recomputedImpact:u}.");
				}
				pending.EarliestImpactUtc = recomputedImpact;
				pending.DispatchAtUtc = recomputedImpact - safetyMargin;
				AttackerFleet latestAttack = stillIncoming.OrderByDescending(a => a.ArriveIn).First();
				pending.MinFlightTimeSeconds = 2 * latestAttack.ArriveIn + 120 + (RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);

				ArmDispatchTimer(celestialId);

				if (DateTime.UtcNow < pending.DispatchAtUtc) {
					continue;
				}

				await DispatchPendingAsync(celestialId, string.Join(",", stillIncoming.Select(a => a.ID)));
			}
		}

		private async Task HandleAttack(AttackerFleet attack, DateTime fetchedUtc) {
			try {
				var nowUtc = DateTime.UtcNow;
				foreach (var kv in _handledAttackIds.ToArray()) {
					if (nowUtc - kv.Value > _handledAttackTtl)
						_handledAttackIds.TryRemove(kv.Key, out _);
				}
				if (attack != null && attack.ID != 0 &&
					_handledAttackIds.TryGetValue(attack.ID, out var seenAt) &&
					(nowUtc - seenAt) <= _handledAttackTtl) {
					DoLog(LogLevel.Information, $"Attack {attack.ID} already handled recently; skipping duplicate actions.");
					return;
				}
				if (attack != null && attack.ID != 0) {
					_handledAttackIds[attack.ID] = nowUtc;
				}

			if (_tbotInstance.UserData.celestials.Count() == 0) {
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Warning, "Unable to handle attack at the moment: bot is still getting account info.");
				DoLog(LogLevel.Information,  $"Next check at {newTime.ToString()}");
				return;
			}

			Celestial attackedCelestial = _tbotInstance.UserData.celestials.Unique().FirstOrDefault(planet => planet.HasCoords(attack.Destination));
			if (attackedCelestial == null) {
				DoLog(LogLevel.Warning, $"Unable to handle attack {attack.ID}: attacked celestial not found in account data.");
				return;
			}
			attackedCelestial = await _tbotOgameBridge.UpdatePlanet(attackedCelestial, UpdateTypes.Ships);
			try {
				if ((bool)_tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.Active) {
					attackedCelestial = await _tbotOgameBridge.UpdatePlanet(attackedCelestial, UpdateTypes.Resources);
				}
			} catch {
			}


			try {
				var wlObj = _tbotInstance.InstanceSettings.Defender.WhiteList;
				IEnumerable<long> whiteListIds = wlObj switch {
					long[] a => a,
					int[] a => a.Select(x => (long)x),
					IEnumerable<long> e => e,
					IEnumerable<int> e => e.Select(x => (long)x),
					_ => Enumerable.Empty<long>()
				};

				if (!whiteListIds.Any() && wlObj != null) {
					DoLog(LogLevel.Debug, $"Defender WhiteList present but unsupported type: {wlObj.GetType().FullName}");
				}

				foreach (var playerId in whiteListIds) {
					if (attack.AttackerID == playerId) {
						DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: attacker {attack.AttackerName} whitelisted.");
						return;
					}
				}
			} catch (Exception ex) {
				DoLog(LogLevel.Warning, $"An error has occurred while checking Defender WhiteList: {ex.Message}");
			}

			try {
				if (attack.MissionType == Missions.MissileAttack) {
					if ((bool) _tbotInstance.InstanceSettings.Defender.TelegramMessenger.Active) {
						await _tbotInstance.SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} with IPM!", mirrorToDiscord: false);
					}
					await SendDiscordAlert($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} with IPM!");
					DoLog(LogLevel.Information, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} with IPM!");
					if (
						!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "DefendFromMissiles") ||
						(SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "DefendFromMissiles") && (bool) _tbotInstance.InstanceSettings.Defender.DefendFromMissiles)
					) {
						Celestial defenderCelestial = attackedCelestial;
						if (attackedCelestial.Coordinate.Type == Celestials.Moon) {
							 defenderCelestial = _tbotInstance.UserData.celestials.Unique().SingleOrDefault(planet => planet.HasCoords(new Coordinate {
								 Galaxy = attackedCelestial.Coordinate.Galaxy,
								 System = attackedCelestial.Coordinate.System,
								 Position = attackedCelestial.Coordinate.Position,
								 Type = Celestials.Planet
							}));
						}
						if (defenderCelestial == null) {
							DoLog(LogLevel.Warning, $"Missile attack detected on {attack.Destination.ToString()} but planet celestial was not found in account data. Skipping missile defence.");
							return;
						}
						defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Facilities);
						if (defenderCelestial.Facilities.MissileSilo >= 2) {
							defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Defences);
							defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Productions);
							if (defenderCelestial.Productions.Count == 0) {
								var availableSpace = defenderCelestial.Facilities.MissileSilo - defenderCelestial.Defences.AntiBallisticMissiles - (2 * defenderCelestial.Defences.InterplanetaryMissiles);
								defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Resources);
								if (availableSpace > 0) {
									DoLog(LogLevel.Information, $"Building {availableSpace} AntiBallisticMissiles on {defenderCelestial.ToString()}");
									await _ogameService.BuildDefences(defenderCelestial, Buildables.AntiBallisticMissiles, availableSpace);
								}
								else {
									DoLog(LogLevel.Information, $"Unable to build AntiBallisticMissiles on {defenderCelestial.ToString()}: there is no space");
								}
							}
							else {
								DoLog(LogLevel.Information, $"Unable to build AntiBallisticMissiles on {defenderCelestial.ToString()}: a production is ongoing");
							}
						}
						else {
							DoLog(LogLevel.Information, $"No MissileSilo level >= 2 on {defenderCelestial.ToString()}");
						}
					}
					return;
				}
				if (attack.Ships != null && _tbotInstance.UserData.researches.EspionageTechnology >= 8) {
					if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "IgnoreProbes") && (bool) _tbotInstance.InstanceSettings.Defender.IgnoreProbes && attack.IsOnlyProbes()) {
						if (attack.MissionType == Missions.Spy)
							DoLog(LogLevel.Information, "Attacker sent only Probes! Espionage action skipped.");
						else
							DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: only Espionage Probes.");

						return;
					}
					if (
						(bool) _tbotInstance.InstanceSettings.Defender.IgnoreWeakAttack &&
						attack.Ships.GetFleetPoints() < (attackedCelestial.Ships.GetFleetPoints() / (int) _tbotInstance.InstanceSettings.Defender.WeakAttackRatio)
					) {
						DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: weak attack.");
						return;
					}
				} else {
					DoLog(LogLevel.Information, "Unable to detect fleet composition.");
				}
				var ignoreAttackIfIHaveActive = (bool) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.Active;
				var totalResources = attackedCelestial.Resources?.TotalResources ?? 0;
				var fleetPoints = attackedCelestial.Ships?.GetFleetPoints() ?? 0;

				if (
					ignoreAttackIfIHaveActive &&
					totalResources < (long) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.MinResourcesToSave &&
					(fleetPoints * 1000) < (long) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.MinFleetToSave
				) {
					DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: it's not worth it.");
					return;
				}
			} catch {
				DoLog(LogLevel.Warning, "An error has occurred while checking attacker fleet composition");
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.TelegramMessenger.Active) {
				await _tbotInstance.SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}", mirrorToDiscord: false);
				if (attack.Ships != null) {
					await Task.Delay(1000, _ct);
					await _tbotInstance.SendTelegramMessage($"The attack is composed by: {attack.Ships.ToString()}", mirrorToDiscord: false);
				}
			}
			{
				string discordMsg = $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}";
				if (attack.Ships != null) {
					discordMsg += $"\nComposition: {attack.Ships.ToString()}";
				}
				await SendDiscordAlert(discordMsg);
			}
			DoLog(LogLevel.Warning, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attackedCelestial.ToString()} arriving at {attack.ArrivalTime.ToString()}");
			if (attack.Ships != null) {
				await Task.Delay(1000, _ct);
				DoLog(LogLevel.Warning, $"The attack is composed by: {attack.Ships.ToString()}");
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.SpyAttacker.Active) {
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				if (attackedCelestial.Ships.EspionageProbe == 0) {
					DoLog(LogLevel.Warning, "Could not spy attacker: no probes available.");
				} else {
					try {
						Coordinate destination = attack.Origin;
						Ships ships = new() { EspionageProbe = (int) _tbotInstance.InstanceSettings.Defender.SpyAttacker.Probes };
						int fleetId = await _fleetScheduler.SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), _tbotInstance.UserData.userInfo.Class);
						var fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
						if (fleet == null) {
							DoLog(LogLevel.Warning, $"SpyAttacker: SendFleet returned id={fleetId}, but fleet was not found in current fleet list (send may have failed or list not updated yet).");
						} else {
							DoLog(LogLevel.Information, $"Spying attacker from {attackedCelestial.ToString()} to {destination.ToString()} with {_tbotInstance.InstanceSettings.Defender.SpyAttacker.Probes} probes. Arrival at {fleet.ArrivalTime.ToString()}");
						}
					} catch (Exception e) {
						DoLog(LogLevel.Error, $"Could not spy attacker: an exception has occurred: {e.Message}");
						DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
					}
				}
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.MessageAttacker.Active) {
				try {
					if (attack.AttackerID != 0) {
						Random random = new();
						string[] messages = _tbotInstance.InstanceSettings.Defender.MessageAttacker.Messages;
						string message = messages.ToList().Shuffle().First();
						DoLog(LogLevel.Information, $"Sending message \"{message}\" to attacker {attack.AttackerName}");
						try {
							await _ogameService.SendMessage(attack.AttackerID, message);
							DoLog(LogLevel.Information, "Message succesfully sent.");
						} catch {
							DoLog(LogLevel.Warning, "Unable send message.");
						}
					} else {
						DoLog(LogLevel.Warning, "Unable send message.");
					}

				} catch (Exception e) {
					DoLog(LogLevel.Error, $"Could not message attacker: an exception has occurred: {e.Message}");
					DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				}
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.Autofleet.Active) {
				try {
					if (GhostSleepPassiveMode) {
						GhostSleepPassiveMode = false;
						_ghostSleepEscalated = true;
						DoLog(LogLevel.Warning, "GhostSleep PASSIVE: attack detected - escalating to ACTIVE (automatic fleetsave, no more waiting for the full expedition return).");
						_tbotInstance.GhostSleepEscalate(true);
					}
					RegisterOrUpdatePendingFleetSave(attackedCelestial, attack, fetchedUtc);
					_ = Task.Run(() => _tbotInstance.AttackLockdown(true));
				} catch (Exception e) {
					DoLog(LogLevel.Error, $"Could not schedule fleetsave: an exception has occurred: {e.Message}");
					DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				}
			}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"HandleAttack error for attack {attack?.ID}: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			}
		}
	}
}