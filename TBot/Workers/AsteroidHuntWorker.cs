using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tbot.Common.IO;
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
	internal class AsteroidHuntWorker : WorkerBase {
		private void AhLog(LogLevel level, string text) {
			DoLog(level, text);
			ExtraLog.Write(_tbotInstance, "AsteroidHunt", $"[{level}] {text}");
		}

		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		private HuntState _memState;

		public AsteroidHuntWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(10);

		private bool _saveJson = true;

		private AsteroidHuntConfig ReadConfig() {
			AsteroidHuntConfig c = new();
			try {
				var g = _tbotInstance.InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "AsteroidHunt"))
					return c;
				var a = g.Experimental.AsteroidHunt;
				if (SettingsService.IsSettingSet(a, "Active"))
					c.Active = (bool) a.Active;
				if (SettingsService.IsSettingSet(a, "ShipType"))
					c.ShipType = (string) a.ShipType;
				if (SettingsService.IsSettingSet(a, "ShipCount"))
					c.ShipCount = Math.Max(1, (int) a.ShipCount);
				if (SettingsService.IsSettingSet(a, "MaxAttemptsPerDay"))
					c.MaxAttemptsPerDay = Math.Max(0, (int) a.MaxAttemptsPerDay);
				if (SettingsService.IsSettingSet(a, "SearchSystems"))
					c.SearchSystems = Math.Max(0, (int) a.SearchSystems);
				_saveJson = !SettingsService.IsSettingSet(a, "SaveJson") || (bool) a.SaveJson;
			} catch (Exception) {
			}
			return c;
		}

		private string StatePath() {
			string alias = Path.GetFileName(_tbotInstance.InstanceAlias);
			string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
			return Path.Combine(AppContext.BaseDirectory, "data", $"asteroiden_funde_{safe}.json");
		}

		private HuntState LoadState() {
			if (!_saveJson)
				return _memState ??= new HuntState();
			try {
				string path = StatePath();
				if (File.Exists(path)) {
					var s = JsonConvert.DeserializeObject<HuntState>(File.ReadAllText(path));
					if (s != null) {
						s.Attempts ??= new List<HuntAttempt>();
						return s;
					}
				}
			} catch (Exception e) {
				AhLog(LogLevel.Warning, $"Could not read the asteroid log: {e.Message}");
			}
			return new HuntState();
		}

		private void SaveState(HuntState s) {
			_memState = s;
			if (!_saveJson)
				return;
			try {
				if (s.Attempts.Count > 500)
					s.Attempts = s.Attempts.Skip(s.Attempts.Count - 500).ToList();
				AtomicFile.WriteAllText(StatePath(), JsonConvert.SerializeObject(s, Formatting.Indented));
			} catch (Exception e) {
				AhLog(LogLevel.Warning, $"Could not write the asteroid log: {e.Message}");
			}
		}

		protected override async Task Execute() {
			AsteroidHuntConfig cfg = ReadConfig();
			long nextMs = 10 * 60 * 1000;
			try {
				DateTime gameNow = await _tbotOgameBridge.GetDateTime();
				HuntState state = LoadState();
				AsteroidHuntLogic.RollDay(state, gameNow.ToString("yyyy-MM-dd"));
				switch (AsteroidHuntLogic.Decide(state, DateTime.UtcNow, cfg)) {
					case HuntAction.Wait: {
						DateTime arrival = AsteroidHuntLogic.ParseUtc(state.Pending.ArrivalUtc, DateTime.UtcNow);
						nextMs = Math.Max(5000, (long) (arrival.AddSeconds(AsteroidHuntLogic.ArrivalGraceSeconds) - DateTime.UtcNow).TotalMilliseconds);
						break;
					}
					case HuntAction.Resolve:
						nextMs = await ResolveAsync(state, cfg, gameNow);
						break;
					case HuntAction.Start:
						nextMs = await StartAsync(state, cfg, gameNow);
						break;
					default:
						nextMs = NextDayMs(gameNow);
						AhLog(LogLevel.Information, "Asteroid hunt: done for today.");
						break;
				}
				SaveState(state);
			} catch (Exception e) {
				AhLog(LogLevel.Warning, $"Asteroid hunt failed: {e.Message}");
				AhLog(LogLevel.Debug, $"Asteroid hunt failed, stack trace: {e.StackTrace}");
			}
			if (nextMs < 5000)
				nextMs = 5000;
			ChangeWorkerPeriod(nextMs);
		}

		private static long NextDayMs(DateTime gameNow) =>
			AsteroidHuntLogic.SecondsUntilNextDay(gameNow, 300) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);

		private long AfterAttemptMs(HuntState state, AsteroidHuntConfig cfg, DateTime gameNow) {
			if (state.FoundToday || state.SearchExhausted || state.AttemptsToday >= AsteroidHuntLogic.AttemptLimit(cfg))
				return NextDayMs(gameNow);
			return RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
		}

		private Celestial FindCelestial(string coord) =>
			_tbotInstance.UserData.celestials.FirstOrDefault(c => c.Coordinate.ToString() == coord) ?? _tbotInstance.UserData.celestials.FirstOrDefault();

		private async Task<(Dictionary<string, long> Stock, Dictionary<string, string> Names)> ReadInventory(Celestial celestial) {
			var items = await _ogameService.GetItems(celestial) ?? new List<InventoryItem>();
			var stock = items.Where(i => !string.IsNullOrEmpty(i.Ref)).GroupBy(i => i.Ref).ToDictionary(g => g.Key, g => g.Sum(i => i.Amount));
			var names = items.Where(i => !string.IsNullOrEmpty(i.Ref)).GroupBy(i => i.Ref).ToDictionary(g => g.Key, g => g.First().Name);
			return (stock, names);
		}

		private static string Shorten(string text) {
			text = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
			return text.Length > 200 ? text.Substring(0, 200) + "..." : text;
		}

		private async Task<long> ResolveAsync(HuntState state, AsteroidHuntConfig cfg, DateTime gameNow) {
			HuntPending p = state.Pending;
			string result = "unknown";
			string item = null;

			if (p.ReportBaseKnown) {
				try {
					var msgs = await _ogameService.GetOtherMessages() ?? new List<OtherMessage>();
					var fresh = msgs.Where(m => m.ID > p.LastMsgId && m.Title != null).OrderByDescending(m => m.ID).ToList();
					var report = fresh.FirstOrDefault(m => m.Title.Contains("Bericht", StringComparison.OrdinalIgnoreCase) || m.Title.Contains("Report from", StringComparison.OrdinalIgnoreCase))
						?? fresh.FirstOrDefault(m => AsteroidHuntLogic.Classify((m.Title + " " + m.Content).Trim()) != ReportKind.Unknown);
					if (report == null && p.ReportWaits < 3) {
						p.ReportWaits++;
						return 30 * 1000;
					}
					if (report == null)
						AhLog(LogLevel.Warning, $"Asteroid {p.Target}: no report found in the Other messages ({fresh.Count} new, last id before the attack {p.LastMsgId}, titles: {string.Join(" | ", fresh.Take(5).Select(m => Shorten(m.Title)))}) - using the inventory (can count unrelated new items).");
					if (report != null) {
						string text = (report.Title + " " + report.Content).Trim();
						switch (AsteroidHuntLogic.Classify(text)) {
							case ReportKind.Found:
								result = "found";
								item = Shorten(report.Content);
								break;
							case ReportKind.Locked:
								result = "locked";
								state.SearchExhausted = true;
								break;
							case ReportKind.Retry:
								result = "none";
								break;
							default:
								AhLog(LogLevel.Information, $"Asteroid {p.Target}: report not recognised, using the inventory: \"{Shorten(text)}\"");
								break;
						}
					}
				} catch (Exception e) {
					AhLog(LogLevel.Warning, $"Asteroid {p.Target}: could not read the messages ({e.Message}), using the inventory (can count unrelated new items).");
				}
			}

			if (result == "unknown" && !p.ReportBaseKnown)
				AhLog(LogLevel.Warning, $"Asteroid {p.Target}: message state before the attack is unknown - using the inventory (can count unrelated new items).");
			if (result == "unknown") try {
				var origin = FindCelestial(p.Origin);
				var (after, names) = await ReadInventory(origin);
				if (p.BeforeKnown) {
					var found = AsteroidHuntLogic.NewItems(p.Before ?? new Dictionary<string, long>(), after);
					if (found.Count > 0) {
						result = "found";
						item = string.Join(", ", found.Select(r => names.TryGetValue(r, out var n) && !string.IsNullOrEmpty(n) ? n : r));
					} else {
						result = "none";
					}
				}
			} catch (Exception e) {
				p.ReadFailures++;
				if (p.ReadFailures < AsteroidHuntLogic.MaxReadFailures) {
					AhLog(LogLevel.Warning, $"Asteroid {p.Target}: could not read the inventory ({e.Message}), trying again.");
					return 2 * 60 * 1000;
				}
			}

			state.Attempts.Add(new HuntAttempt { Utc = DateTime.UtcNow.ToString("o"), Day = p.Day, Origin = p.Origin, Target = p.Target, Result = result, Item = item });
			if (result == "found" && p.Day == state.Day)
				state.FoundToday = true;
			state.Pending = null;
			AhLog(LogLevel.Information, result == "found" ? $"Asteroid {p.Target}: item found: {item}"
				: result == "locked" ? $"Asteroid {p.Target}: daily limit reached - done for today."
				: result == "none" ? $"Asteroid {p.Target}: no item (attempt {state.AttemptsToday} today)"
				: $"Asteroid {p.Target}: result unknown (inventory not readable)");
			long wait = AfterAttemptMs(state, cfg, gameNow);
			if (!state.FoundToday && !state.SearchExhausted && state.AttemptsToday < AsteroidHuntLogic.AttemptLimit(cfg)) {
				double flight = (AsteroidHuntLogic.ParseUtc(p.ArrivalUtc, DateTime.UtcNow) - AsteroidHuntLogic.ParseUtc(p.SentUtc, DateTime.UtcNow)).TotalSeconds;
				wait = Math.Max(wait, (long) (Math.Max(0, flight) + 15) * 1000);
			}
			return wait;
		}

		private async Task<long> StartAsync(HuntState state, AsteroidHuntConfig cfg, DateTime gameNow) {
			if (!Enum.TryParse(cfg.ShipType, true, out Buildables shipType)) {
				AhLog(LogLevel.Warning, $"Asteroid hunt: unknown ship type \"{cfg.ShipType}\" - using Pathfinder.");
				shipType = Buildables.Pathfinder;
			}

			Celestial origin = null;
			long best = 0;
			foreach (Celestial c in _tbotInstance.UserData.celestials.ToList()) {
				Celestial updated = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
				long have = updated?.Ships?.GetAmount(shipType) ?? 0;
				if (have >= cfg.ShipCount && have > best) {
					best = have;
					origin = updated;
				}
			}
			if (origin == null) {
				AhLog(LogLevel.Information, $"Asteroid hunt: no {shipType} available - trying again later.");
				return (state.AttemptsToday > 0 ? 3 : 60) * 60 * 1000;
			}

			var serverData = _tbotInstance.UserData.serverData;
			Ships ships = new Ships();
			ships.Add(shipType, cfg.ShipCount);

			var pending = new HuntPending { Day = state.Day, Origin = origin.Coordinate.ToString() };
			try {
				var known = await _ogameService.GetOtherMessages() ?? new List<OtherMessage>();
				pending.LastMsgId = known.Count > 0 ? known.Max(m => m.ID) : 0;
				pending.ReportBaseKnown = true;
			} catch (Exception e) {
				AhLog(LogLevel.Warning, $"Asteroid hunt: messages not readable before the attack ({e.Message}) - using the inventory (can count unrelated new items).");
			}
			try {
				var (before, _) = await ReadInventory(origin);
				pending.Before = before;
				pending.BeforeKnown = true;
			} catch (Exception e) {
				AhLog(LogLevel.Warning, $"Asteroid hunt: inventory not readable before the attack ({e.Message}) - result will be unknown.");
			}

			int maxSteps = Math.Max(1, cfg.SearchSystems);
			int offsetLimit = (maxSteps + 1) / 2;
			var tried = new HashSet<int>();
			int step = state.SearchIndex;
			bool hintTried = state.LastHitSystem <= 0 || (state.SearchIndex > 0 && state.AttemptsToday == 0);
			while (step < maxSteps || !hintTried) {
				int system;
				if (!hintTried) {
					system = state.LastHitSystem;
					hintTried = true;
				} else {
					system = AsteroidHuntLogic.TargetSystem(origin.Coordinate.System, step, offsetLimit, serverData.Systems);
					step++;
					state.SearchIndex = step;
				}
				if (!tried.Add(system))
					continue;
				Coordinate target = new Coordinate(origin.Coordinate.Galaxy, system, 17, Celestials.Planet);
				pending.Target = target.ToString();
				var prediction = _calculationService.CalcFleetPrediction(origin.Coordinate, target, ships, Missions.Attack, Speeds.HundredPercent, _tbotInstance.UserData.researches, serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
				int fleetId = await _fleetScheduler.SendFleet(origin, ships, target, Missions.Attack, Speeds.HundredPercent, null, _tbotInstance.UserData.userInfo.Class);
				if (fleetId > 0) {
					state.AttemptsToday++;
					state.LastHitSystem = system;
					DateTime nowUtc = DateTime.UtcNow;
					pending.SentUtc = nowUtc.ToString("o");
					pending.ArrivalUtc = nowUtc.AddSeconds(prediction.Time).ToString("o");
					state.Pending = pending;
					AhLog(LogLevel.Information, $"Asteroid {target}: attacking with {cfg.ShipCount}x{shipType} from {origin.Coordinate} (arrives in {prediction.Time / 60} min).");
					return (prediction.Time + AsteroidHuntLogic.ArrivalGraceSeconds + 10) * 1000;
				}
				if (fleetId == (int) SendFleetCode.NotEnoughSlots || fleetId == (int) SendFleetCode.NotEnoughRessources || fleetId == (int) SendFleetCode.AfterSleepTime) {
					AhLog(LogLevel.Information, $"Asteroid {target}: not sent (code {fleetId}) - trying again later.");
					return 10 * 60 * 1000;
				}
				AhLog(LogLevel.Debug, $"Asteroid search: no target at {target} (code {fleetId}).");
			}

			state.SearchExhausted = true;
			state.Attempts.Add(new HuntAttempt { Utc = DateTime.UtcNow.ToString("o"), Day = state.Day, Origin = origin.Coordinate.ToString(), Target = $"{origin.Coordinate.Galaxy}:*:17", Result = "not-found" });
			AhLog(LogLevel.Information, $"Asteroid search: no asteroid found in {maxSteps} systems around {origin.Coordinate.Galaxy}:{origin.Coordinate.System}.");
			return AfterAttemptMs(state, cfg, gameNow);
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				var g = _tbotInstance.InstanceSettings;
				return SettingsService.IsSettingSet(g, "Experimental") && SettingsService.IsSettingSet(g.Experimental, "AsteroidHunt")
					&& SettingsService.IsSettingSet(g.Experimental.AsteroidHunt, "Active") && (bool) g.Experimental.AsteroidHunt.Active;
			} catch (Exception) {
				return false;
			}
		}

		public override string GetWorkerName() {
			return "AsteroidHunt";
		}

		public override Feature GetFeature() {
			return Feature.AsteroidHunt;
		}

		public override LogSender GetLogSender() {
			return LogSender.AsteroidHunt;
		}
	}
}
