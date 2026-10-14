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
	// NOTE (fleetsave-timing patch): recommended addition to instance_settings.json under
	// Defender.Autofleet - an optional "SafetyMarginSeconds" key (e.g. 120). It's read defensively via
	// SettingsService.IsSettingSet below, so older settings files without it keep working via
	// DefaultFleetSaveSafetyMarginSeconds.
	internal class DefenderWorker : WorkerBase {
		private static readonly ConcurrentDictionary<int, DateTime> _handledAttackIds = new();
		private static readonly TimeSpan _handledAttackTtl = TimeSpan.FromMinutes(60);

		// PATCH (fleetsave-timing): root-cause fix for reports of the Defender fleetsaving too early
		// (e.g. losing an expedition fleet that returned a few minutes after the save already fired,
		// because the attack itself was still ~10 minutes out when detected) and never revisiting that
		// decision (a second, faster attack on the same celestial did not bring the timer forward).
		//
		// Previously HandleAttack called _fleetScheduler.AutoFleetSave(...) synchronously, the moment an
		// attack was first seen - regardless of how far out the impact still was. The "minFlightTime"
		// passed to AutoFleetSave only constrains which destination/mission is picked (GetFleetSaveDestination
		// requires Duration >= minFlightTime / 2), it has never delayed the dispatch itself.
		//
		// Fix: track one pending FleetSave per attacked celestial (keyed by celestial ID) instead of
		// dispatching immediately. HandleAttack only registers/updates the earliest known impact time;
		// ProcessPendingFleetSaves (called once per Execute() cycle) is what actually decides whether it
		// is time to send yet, refreshes against the live attack list first (so a recalled/resolved attack
		// cancels the pending save, and a newer, faster attack on the same celestial pulls the dispatch
		// forward), and only then calls AutoFleetSave - which itself re-fetches live ships/resources, so a
		// fleet that returned in the meantime (e.g. an expedition) is automatically swept up.
		private sealed class PendingFleetSave {
			public DateTime EarliestImpactUtc;
			public DateTime DispatchAtUtc;
			public long MinFlightTimeSeconds;
			// PATCH Dispatch-Timer (01.10.2026): verhindert, dass Timer und Execute()-Zyklus gleichzeitig dispatchen
			public int Busy;
		}
		private static readonly ConcurrentDictionary<int, PendingFleetSave> _pendingFleetSaves = new();

		// PATCH Dispatch-Timer (01.10.2026): Vorfall 01.10. 10:23 - der Fleetsave war fuer 10:22:17 geplant,
		// wurde aber erst im naechsten Execute()-Zyklus um 10:23:15 ausgefuehrt (3 Sekunden vor dem Einschlag),
		// weil der Dispatch nur im Check-Zyklus geprueft wurde: Timer-Jitter plus der Vorlauf des Zyklus
		// (FakeActivity, UpdateFleets, IsUnderAttack, GetAttacks, HandleAttack mit Task.Delay) frassen die
		// 60s-Marge fast komplett auf. Jetzt feuert pro angegriffenem Himmelskoerper ein eigener Timer exakt
		// zur DispatchAtUtc-Zeit, unabhaengig vom Check-Intervall. Der Execute()-Zyklus bleibt als Sicherheitsnetz.
		private static readonly ConcurrentDictionary<int, System.Threading.Timer> _dispatchTimers = new();

		private void ArmDispatchTimer(int celestialId) {
			if (!_pendingFleetSaves.TryGetValue(celestialId, out var p))
				return;
			long delayMs = (long) Math.Max(0, (p.DispatchAtUtc - DateTime.UtcNow).TotalMilliseconds);
			System.Threading.Timer timer = new System.Threading.Timer(async _ => {
				try {
					await DispatchPendingAsync(celestialId, "Timer");
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Dispatch timer: error during fleetsave dispatch: {e.Message}");
				}
			}, null, delayMs, System.Threading.Timeout.Infinite);
			_dispatchTimers.AddOrUpdate(celestialId, timer, (_, old) => { old.Dispose(); return timer; });
		}

		private void RemovePending(int celestialId) {
			_pendingFleetSaves.TryRemove(celestialId, out _);
			if (_dispatchTimers.TryRemove(celestialId, out var t))
				t.Dispose();
		}

		// Gemeinsamer Dispatch-Pfad fuer Dispatch-Timer UND Execute()-Zyklus. Per Busy-Flag gegen
		// Doppel-Dispatch geschuetzt. Wird zu frueh aufgerufen (>0,5s vor DispatchAtUtc), passiert nichts.
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

				// PATCH Mehrfachwellen (01.10.2026): der Eintrag wird NICHT mehr beim ersten Einschlag entfernt -
				// sonst bekam eine zweite, spaetere Welle auf denselben Himmelskoerper nie ein Fleetsave
				// (HandleAttack ueberspringt bekannte Angriffe, "already handled"). Aufgeraeumt wird im
				// Execute()-Zyklus (keine Angriffe mehr -> abbrechen) bzw. 5 Minuten nach dem letzten bekannten Einschlag.
				if (DateTime.UtcNow >= pending.EarliestImpactUtc.AddMinutes(5)) {
					RemovePending(celestialId);
				} else {
					// Bedrohungsfenster noch offen - kurz weiter beobachten, falls noch eine Flotte
					// zurueckkehrt und ein Folge-Fleetsave braucht.
					pending.DispatchAtUtc = DateTime.UtcNow.AddSeconds(FleetSaveFollowUpCheckSeconds);
					ArmDispatchTimer(celestialId);
				}
			} finally {
				System.Threading.Interlocked.Exchange(ref pending.Busy, 0);
			}
		}

		// Default lead time before impact at which the FleetSave is actually dispatched, used unless
		// overridden by Defender.Autofleet.SafetyMarginSeconds in the instance settings. Large enough to
		// absorb SendFleet processing time and a bit of Defender check-interval jitter, small enough that
		// fleets returning from expeditions/other missions in the meantime are still on the celestial when
		// it fires instead of being saved separately (or not at all) hours in advance.
		private const int DefaultFleetSaveSafetyMarginSeconds = 120;

		// Once a FleetSave has been dispatched for a celestial but the threat window (EarliestImpactUtc)
		// hasn't actually passed yet, re-check this often for stragglers (a fleet returning seconds/minutes
		// after the first dispatch). AutoFleetSave is a safe no-op (logs and returns) when nothing is left
		// to send, so a spurious extra check just costs a log line, not a wasted fleet slot.
		private const int FleetSaveFollowUpCheckSeconds = 20;

		// PATCH 14: Discord attack alert. Independent of TelegramMessenger - fires an HTTP POST
		// to a Discord webhook URL whenever an attack alert would also go to Telegram. Never throws
		// out of HandleAttack: any failure (bad URL, network, Discord outage) is only logged.
		private static readonly HttpClient _discordHttpClient = new();

		private async Task SendDiscordAlert(string message) {
			try {
				// Own Defender.Discord block (PATCH 14) wins if present. If it is absent, fall back to
				// the global DiscordWebhook (ported from ronnie32 v3.5.1-beta1) as an Attack embed.
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

		// PATCH GhostSleep (siehe claude/ghostsleep-plan-adr.md und TBotMain.cs, Abschnitt "GhostSleep"):
		// von aussen (TBotMain) gesteuerter PASSIV/AKTIV-Schalter. PASSIV = Alarm bei Angriff laeuft wie
		// gewohnt (Telegram/Discord/Log in HandleAttack, siehe unten - unveraendert), aber
		// RegisterOrUpdatePendingFleetSave bleibt unterdrueckt (kein automatisches Fleetsave) UND der
		// Worker wird zwischen den von GhostSleep gesetzten Ankerzeitpunkten (GhostSleepNextAnchorUtc)
		// pausiert statt im normalen Defender.CheckIntervalMin/Max-Takt zu laufen - laut Nutzer zaehlt
		// bereits der reine IsUnderAttack()-Poll + FakeActivity() als sichtbare Aktivitaet, PASSIV muss
		// also echte Pausen zwischen den Execute()-Zyklen bedeuten, nicht nur eine unterdrueckte Aktion.
		public volatile bool GhostSleepPassiveMode = false;
		public DateTime? GhostSleepNextAnchorUtc = null;
		// true waehrend eine waehrend GhostSleep-PASSIV erkannte Angriffswelle noch aktiv bearbeitet wird
		// (Eskalation auf AKTIV) - siehe HandleAttack/ProcessPendingFleetSaves weiter unten.
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
				// PATCH Dispatch-Timer: ArriveIn gilt zum Abrufzeitpunkt von GetAttacks - nicht zum spaeteren "jetzt"
				// (Vorlauf/Task.Delay in HandleAttack haette den Einschlag sonst um Sekunden zu spaet geschaetzt).
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

				// PATCH (fleetsave-timing): dispatch any FleetSaves whose safety-margin window has arrived,
				// and drop tracking for celestials whose attacks were recalled/resolved in the meantime.
				// Passing an empty list when isUnderAttack is false is correct and intentional: the account
				// is authoritatively not under attack, so every pending entry gets cancelled below.
				// PATCH Statuskachel: aktuelle Angriffsliste (auch leer) fuer die Browser-Kachel veroeffentlichen
				try { _tbotInstance.PublishAttackSnapshot(currentAttacks, attacksFetchedUtc); } catch { }
				await ProcessPendingFleetSaves(currentAttacks, attacksFetchedUtc);

				// PATCH GhostSleep: sobald eine waehrend PASSIV eskalierte Angriffswelle komplett
				// abgearbeitet ist (keine pending FleetSaves mehr offen), zurueck auf PASSIV und TBotMain
				// informieren (setzt Expeditions.WaitForAllExpeditions zurueck, vergibt neuen Anker).
				// PATCH Angriffs-Lockdown: erst wenn kein Fleetsave mehr offen ist, laufen die anderen Worker wieder.
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
					// PATCH GhostSleep-Fix (Anker-Refresh, 28.09.2026): der Anker wurde bisher nur EINMAL beim
					// Einschlafen (bzw. nach Ende einer Eskalation, siehe TBotMain.GhostSleepEscalate) gesetzt
					// und danach nie erneuert. Sobald dieser Zeitpunkt verstrich, blieb GhostSleepNextAnchorUtc
					// fuer immer auf dem alten, jetzt vergangenen Wert stehen - der naechste Zweig unten
					// berechnete daraus ein negatives (auf 1000ms gedecktes) Intervall, der Worker feuerte
					// praktisch im Sekundentakt weiter statt bis zur naechsten Expeditions-Rueckkehr zu
					// pausieren, inklusive desselben, laengst veralteten Log-Eintrags (beobachtet 28.09.2026:
					// ueber 200x wiederholt, Anker immer noch auf einem Zeitpunkt aus den fruehen Morgenstunden).
					// Fix: sobald dieser Zyklus den Anker tatsaechlich erreicht/ueberschritten hat, TBotMain um
					// einen frischen Anker bitten (waehlt neu eine aktuell zurueckkehrende Expedition), statt
					// stumpf gegen den alten Wert weiterzurechnen. Falls waehrend dieses Zyklus weiter oben
					// (HandleAttack) bereits eskaliert wurde, ist GhostSleepPassiveMode inzwischen false und
					// dieser ganze Zweig wird beim naechsten Mal ohnehin uebersprungen.
					_tbotInstance.GhostSleepRearmDefenderAnchor();
				}
				if (GhostSleepPassiveMode && GhostSleepNextAnchorUtc.HasValue) {
					// PATCH GhostSleep: PASSIV ignoriert den normalen Defender.CheckIntervalMin/Max-Takt -
					// naechster Execute()-Zyklus erst wieder am von TBotMain gesetzten Anker (kurz vor der
					// zufaellig gewaehlten Expeditions-Rueckkehr), nicht frueher. Siehe TBotMain.cs,
					// RearmGhostSleepDefenderAnchor().
					interval = (long) Math.Max(1000, (GhostSleepNextAnchorUtc.Value - DateTime.UtcNow).TotalMilliseconds);
					DoLog(LogLevel.Information, $"GhostSleep PASSIVE: next short wake check at {GhostSleepNextAnchorUtc.Value.ToLocalTime()}.");
				} else {
					interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMax);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

					// PATCH (fleetsave-timing): don't oversleep past a pending FleetSave's dispatch time - wake
					// up in time to hit the safety window instead of waiting out the full randomized Defender
					// check interval (which is what originally let a "detected early, dispatched immediately"
					// save fire far earlier than necessary, and would otherwise let a delayed one fire far later
					// than intended too).
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

		// PATCH (fleetsave-timing): how long before an attack's calculated impact time the FleetSave
		// should actually be dispatched. Configurable via Defender.Autofleet.SafetyMarginSeconds (falls
		// back to DefaultFleetSaveSafetyMarginSeconds for older settings files that don't have the key).
		private TimeSpan GetFleetSaveSafetyMargin() {
			try {
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender.Autofleet, "SafetyMarginSeconds")) {
					long configured = (long) _tbotInstance.InstanceSettings.Defender.Autofleet.SafetyMarginSeconds;
					if (configured > 0)
						return TimeSpan.FromSeconds(configured);
				}
			} catch {
				// fall through to default
			}
			return TimeSpan.FromSeconds(DefaultFleetSaveSafetyMarginSeconds);
		}

		// PATCH (fleetsave-timing): register a newly-detected attack against the pending FleetSave for its
		// celestial, or - if one is already tracked - bring the dispatch time forward when this attack's
		// impact is earlier than what was previously known (a second, faster wave shortening the window).
		// A later-arriving attack on an already-tracked celestial intentionally leaves the existing,
		// earlier schedule untouched: it will still fire before the new attack lands too.
		private void RegisterOrUpdatePendingFleetSave(Celestial attackedCelestial, AttackerFleet attack, DateTime nowUtc) {
			DateTime impactUtc = nowUtc.AddSeconds(attack.ArriveIn);
			TimeSpan safetyMargin = GetFleetSaveSafetyMargin();
			DateTime dispatchAtUtc = impactUtc - safetyMargin;
			// PATCH Recall-nach-letzter-Welle: siehe ProcessPendingFleetSaves - 2x Restzeit + 2 Min Puffer
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
						// spaetere Welle: Recall-Zeitpunkt darf nur nach hinten wandern
						existing.MinFlightTimeSeconds = Math.Max(existing.MinFlightTimeSeconds, minFlightTime);
					}
					return existing;
				});

			// PATCH Dispatch-Timer: exakten Timer (neu) setzen - auch wenn ein schnellerer Angriff den Termin vorzieht
			ArmDispatchTimer(attackedCelestial.ID);

			if (isNew) {
				DoLog(LogLevel.Information, $"{attackedCelestial.ToString()}: scheduling FleetSave dispatch at {dispatchAtUtc:u} (impact {impactUtc:u}, safety margin {safetyMargin.TotalSeconds:0}s).");
				if (dispatchAtUtc <= nowUtc) {
					DoLog(LogLevel.Warning, $"{attackedCelestial.ToString()}: safety window for attack {attack.ID} has already passed at detection time - FleetSave will fire on this same check instead of waiting.");
				}
			}
		}

		// PATCH (fleetsave-timing): called once per Execute() cycle. For every tracked celestial: refresh
		// against the live attack list (dropping the entry entirely if nothing is incoming anymore - the
		// attack was recalled or has already resolved), recompute the earliest impact from that live list
		// (pulling the dispatch time forward if a newer attack is faster than what was tracked), and only
		// once "now" has reached the safety-margin dispatch time, actually call AutoFleetSave. AutoFleetSave
		// itself re-fetches live ships/resources, so anything that returned to the celestial in the
		// meantime (e.g. an expedition) is included automatically; it is also a safe no-op if there is
		// nothing left to send. The entry is kept (with a short follow-up recheck) until the threat's own
		// impact time has actually passed, so a fleet returning after the first dispatch still gets swept
		// up by a second one instead of being left behind.
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
				// PATCH Recall-nach-letzter-Welle (01.10.2026): AutoFleetSave plant den Recall bei minDuration/2.
				// Frueher basierte minDuration auf dem FRUEHESTEN Angriff (x1,3) - die Flotte kam dadurch schon zurueck,
				// waehrend spaetere Wellen (01.10. 10:32/10:42) noch ausstanden. Jetzt: 2x die Restzeit des SPAETESTEN
				// bekannten Angriffs auf diesen Himmelskoerper + 2 Minuten Puffer -> Recall erst nach dessen Einschlag.
				AttackerFleet latestAttack = stillIncoming.OrderByDescending(a => a.ArriveIn).First();
				pending.MinFlightTimeSeconds = 2 * latestAttack.ArriveIn + 120 + (RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);

				// PATCH Dispatch-Timer: Termin aktualisiert -> Timer neu setzen
				ArmDispatchTimer(celestialId);

				if (DateTime.UtcNow < pending.DispatchAtUtc) {
					continue; // not yet time to send
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
					// PATCH GhostSleep: waehrend PASSIV wuerde ein automatisches Fleetsave dem Plan
					// widersprechen ("nur kurz Alarm schlagen, im akuten Notfall manuell eingreifen") -
					// der Alarm oben (Telegram/Discord/Log) ist bereits unconditional gelaufen. Stattdessen
					// eskaliert der Angriff Defender auf AKTIV (durchgaengiger Takt, automatisches
					// Fleetsave) UND informiert TBotMain (deaktiviert Expeditions.WaitForAllExpeditions
					// temporaer, Variante B). Der aktuelle Angriff wird dabei sofort mitregistriert, nicht
					// erst im naechsten Zyklus.
					if (GhostSleepPassiveMode) {
						GhostSleepPassiveMode = false;
						_ghostSleepEscalated = true;
						DoLog(LogLevel.Warning, "GhostSleep PASSIVE: attack detected - escalating to ACTIVE (automatic fleetsave, no more waiting for the full expedition return).");
						_tbotInstance.GhostSleepEscalate(true);
					}
					// PATCH (fleetsave-timing): no longer dispatches AutoFleetSave immediately here. See the
					// class-level PATCH comment, RegisterOrUpdatePendingFleetSave and ProcessPendingFleetSaves.
					RegisterOrUpdatePendingFleetSave(attackedCelestial, attack, fetchedUtc);
					// PATCH Angriffs-Lockdown: alle anderen Worker aus, Fleetsave hat Vorrang. Bewusst NICHT awaited,
					// damit ein traeger StopWorker den Dispatch nie aufhaelt.
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