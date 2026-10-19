using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal class ShadowFleetWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private static readonly Random Rnd = new Random();

		private int _myFleetId;
		private DateTime _expectedBackUtc = DateTime.MinValue;
		private DateTime _departUtc = DateTime.MinValue;
		private DateTime _nextLaunchUtc = DateTime.MinValue;
		private bool _startWarned, _recallCancelled, _lastWarned, _noDeutWarned;

		public ShadowFleetWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(5);

		public static ShadowFleetConfig ReadConfig(dynamic g) {
			ShadowFleetConfig c = new();
			try {
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "ShadowFleet"))
					return c;
				var a = g.Experimental.ShadowFleet;
				if (SettingsService.IsSettingSet(a, "Active")) c.Active = (bool) a.Active;
				if (SettingsService.IsSettingSet(a, "Celestial")) {
					var cel = a.Celestial;
					if (SettingsService.IsSettingSet(cel, "Galaxy")) c.Galaxy = (int) cel.Galaxy;
					if (SettingsService.IsSettingSet(cel, "System")) c.System = (int) cel.System;
					if (SettingsService.IsSettingSet(cel, "Position")) c.Position = (int) cel.Position;
					if (SettingsService.IsSettingSet(cel, "Type")) c.Type = (string) cel.Type;
				}
				if (SettingsService.IsSettingSet(a, "Ships")) {
					var ships = new Dictionary<string, long>();
					object raw = a.Ships;
					if (raw is IDictionary<string, object> d) {
						foreach (var kv in d) ships[kv.Key] = Convert.ToInt64(kv.Value);
					} else if (raw is JObject jo) {
						foreach (var p in jo.Properties()) ships[p.Name] = (long) p.Value;
					}
					c.Ships = ships;
				}
				if (SettingsService.IsSettingSet(a, "SpeedPercent")) c.SpeedPercent = Math.Clamp((int) a.SpeedPercent, 10, 100);
				if (SettingsService.IsSettingSet(a, "MinBackInMinutes")) c.MinBackInMinutes = Math.Max(2, (int) a.MinBackInMinutes);
				if (SettingsService.IsSettingSet(a, "MaxBackInMinutes")) c.MaxBackInMinutes = Math.Max(c.MinBackInMinutes, (int) a.MaxBackInMinutes);
				if (SettingsService.IsSettingSet(a, "PauseMinSeconds")) c.PauseMinSeconds = Math.Max(0, (int) a.PauseMinSeconds);
				if (SettingsService.IsSettingSet(a, "PauseMaxSeconds")) c.PauseMaxSeconds = Math.Max(c.PauseMinSeconds, (int) a.PauseMaxSeconds);
				if (SettingsService.IsSettingSet(a, "DeuteriumFlights")) c.DeuteriumFlights = Math.Max(0, (int) a.DeuteriumFlights);
				if (SettingsService.IsSettingSet(a, "WindowFrom")) c.WindowFrom = (string) a.WindowFrom ?? "";
				if (SettingsService.IsSettingSet(a, "WindowTo")) c.WindowTo = (string) a.WindowTo ?? "";
				if (SettingsService.IsSettingSet(a, "DefenderOff")) c.DefenderOff = (bool) a.DefenderOff;
			} catch (Exception) {
			}
			return c;
		}

		public static bool DefenderShouldIgnore(dynamic g, Coordinate c) {
			ShadowFleetConfig cfg = ReadConfig(g);
			return cfg.Active && cfg.DefenderOff && c != null && cfg.Matches(c.Galaxy, c.System, c.Position, c.Type.ToString());
		}

		protected override async Task Execute() {
			ShadowFleetConfig cfg = ReadConfig(_tbotInstance.InstanceSettings);
			long nextMs = 5 * 60 * 1000;
			try {
				if (!_startWarned) {
					_startWarned = true;
					DoLog(LogLevel.Warning, $"ShadowFleet: keep a deuterium reserve on {cfg.Galaxy}:{cfg.System}:{cfg.Position} ({cfg.Type}). Every flight burns fuel and the cargo carries deuterium for {cfg.DeuteriumFlights} flights. When it is used up the flights stop.");
				}
				Celestial c = _tbotInstance.UserData.celestials.Unique().FirstOrDefault(x => cfg.Matches(x.Coordinate.Galaxy, x.Coordinate.System, x.Coordinate.Position, x.Coordinate.Type.ToString()));
				if (c == null) {
					DoLog(LogLevel.Warning, $"ShadowFleet: celestial {cfg.Galaxy}:{cfg.System}:{cfg.Position} ({cfg.Type}) not found on this account - nothing to do.");
					nextMs = 10 * 60 * 1000;
				} else {
					nextMs = await Cycle(cfg, c);
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"ShadowFleet failed: {e.Message}");
			}
			ChangeWorkerPeriod(Math.Max(5000, nextMs));
		}

		private static long Ms(long seconds) => Math.Max(5, seconds) * 1000;

		private async Task<long> Cycle(ShadowFleetConfig cfg, Celestial c) {
			DateTime nowUtc = DateTime.UtcNow;

			if (_myFleetId != 0 && nowUtc < _expectedBackUtc.AddSeconds(-60)) {
				await GuardInFlight(c);
				return Math.Min((long) (_expectedBackUtc - nowUtc).TotalMilliseconds + 30000, Rnd.Next(45, 90) * 1000L);
			}

			var fleets = await _fleetScheduler.UpdateFleets();
			_tbotInstance.UserData.fleets = fleets;
			var mine = fleets.Where(f => f.Mission == Missions.Harvest && f.Origin != null && f.Origin.IsSame(c.Coordinate)
				&& (_myFleetId == 0 || f.ID == _myFleetId)).ToList();
			if (mine.Count > 0) {
				var f = mine[0];
				_myFleetId = f.ID;
				long left = f.BackIn ?? f.ArriveIn;
				if (_expectedBackUtc < nowUtc)
					_expectedBackUtc = nowUtc.AddSeconds(Math.Max(60, left));
				await GuardInFlight(c);
				return Ms(Math.Max(45, Math.Min(left + 20, 90)));
			}
			if (_myFleetId != 0) {
				_myFleetId = 0;
				_recallCancelled = false;
				_nextLaunchUtc = nowUtc.AddSeconds(Rnd.Next(cfg.PauseMinSeconds, cfg.PauseMaxSeconds + 1));
				DoLog(LogLevel.Information, $"ShadowFleet: fleet is home again, next flight in {(int) (_nextLaunchUtc - nowUtc).TotalSeconds} s.");
			}
			if (nowUtc < _nextLaunchUtc)
				return Ms((long) (_nextLaunchUtc - nowUtc).TotalSeconds + 1);

			DateTime gameNow = await _tbotOgameBridge.GetDateTime();
			TimeSpan tod = gameNow.TimeOfDay;
			if (!ShadowFleetLogic.InWindow(tod, cfg.WindowFrom, cfg.WindowTo)) {
				long toStart = ShadowFleetLogic.SecondsToWindowStart(tod, cfg.WindowFrom, cfg.WindowTo);
				DoLog(LogLevel.Information, $"ShadowFleet: outside the time window {cfg.WindowFrom}-{cfg.WindowTo}, next start in {toStart / 60} min.");
				return Ms(toStart + Rnd.Next(5, 60));
			}
			long total = ShadowFleetLogic.FlightSeconds(cfg.MinBackInMinutes, cfg.MaxBackInMinutes, ShadowFleetLogic.SecondsToWindowEnd(tod, cfg.WindowFrom, cfg.WindowTo), Rnd);
			if (total <= 0) {
				DoLog(LogLevel.Information, "ShadowFleet: the time window ends too soon for another flight.");
				return Ms(Math.Max(120, ShadowFleetLogic.SecondsToWindowEnd(tod, cfg.WindowFrom, cfg.WindowTo) + 60));
			}

			try {
				var attacks = await _ogameService.GetAttacks() ?? new List<AttackerFleet>();
				var onMe = attacks.Where(a => a != null && a.Destination != null && a.Destination.IsSame(c.Coordinate) && !a.IsOnlyProbes()).Select(a => (long) a.ArriveIn);
				if (ShadowFleetLogic.AttackBlocks(onMe, total, 120)) {
					DoLog(LogLevel.Warning, $"ShadowFleet: an attack on {c.Coordinate} arrives before the fleet would be back - not starting.");
					return Ms(Rnd.Next(180, 300));
				}
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"ShadowFleet: attack list not readable ({e.Message}) - going on.");
			}

			c = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
			var pick = ShadowFleetLogic.PickShips(k => Enum.TryParse(k, true, out Buildables b) ? (c.Ships?.GetAmount(b) ?? 0) : 0, cfg.Ships);
			if (pick.Count == 0) {
				DoLog(LogLevel.Information, $"ShadowFleet: none of the chosen ships are on {c.Coordinate}.");
				return Ms(Rnd.Next(300, 600));
			}
			if (!ShadowFleetLogic.CanHarvest(pick)) {
				DoLog(LogLevel.Warning, "ShadowFleet: the harvest mission needs at least one Recycler (or Pathfinder) in the fleet - tick it in the ship list.");
				return Ms(600);
			}
			Ships ships = new Ships();
			foreach (var kv in pick)
				if (Enum.TryParse(kv.Key, true, out Buildables b)) ships.Add(b, kv.Value);

			decimal speed = cfg.SpeedPercent / 10m;
			ShadowFlightResult r = await _fleetScheduler.ShadowFlight(c, ships, cfg.DeuteriumFlights, total, speed);
			if (r.Level == DeutLevel.None) {
				if (!_noDeutWarned) {
					_noDeutWarned = true;
					await _tbotInstance.SendTelegramMessage($"ShadowFleet stopped: not enough deuterium on {c.Coordinate} for another flight (fuel {r.Fuel}). Put deuterium on the celestial; the flights go on by themselves.");
				}
				DoLog(LogLevel.Warning, $"ShadowFleet: not enough deuterium on {c.Coordinate} (needs {r.Fuel} per flight) - waiting.");
				return Ms(Rnd.Next(600, 900));
			}
			_noDeutWarned = false;
			if (r.FleetId <= 0) {
				DoLog(LogLevel.Warning, $"ShadowFleet: no flight - {r.Problem}.");
				return Ms(Rnd.Next(240, 420));
			}
			_myFleetId = r.FleetId;
			_recallCancelled = false;
			_departUtc = DateTime.UtcNow;
			_expectedBackUtc = DateTime.UtcNow.AddSeconds(r.RoundTripSeconds);
			DoLog(LogLevel.Information, $"ShadowFleet: fleet {r.FleetId} -> {r.Debris}, recall at {r.RecallAt:HH:mm:ss}, home in about {r.RoundTripSeconds / 60} min. Fuel {r.Fuel}, cargo {r.Payload} deuterium, {r.FlightsPossible} flights left in stock.");

			if (r.Level == DeutLevel.Last && !_lastWarned) {
				_lastWarned = true;
				await _tbotInstance.SendTelegramMessage($"ShadowFleet: deuterium for ONE more flight on {c.Coordinate}. Add deuterium or the flights stop.");
			} else if (r.Level == DeutLevel.Low) {
				DoLog(LogLevel.Warning, $"ShadowFleet: deuterium is running low - {r.FlightsPossible} flights left in stock.");
			} else if (r.Level == DeutLevel.Ok) {
				_lastWarned = false;
			}
			return Ms(r.RoundTripSeconds + Rnd.Next(30, 90));
		}

		private async Task GuardInFlight(Celestial c) {
			if (_recallCancelled || _myFleetId == 0) return;
			ShadowFleetConfig cfg = ReadConfig(_tbotInstance.InstanceSettings);
			var attacks = _tbotInstance.GetAttackSnapshot(out DateTime fetchedUtc);
			foreach (var a in attacks) {
				if (a == null || a.Destination == null || !a.Destination.IsSame(c.Coordinate) || a.IsOnlyProbes()) continue;
				DateTime impact = fetchedUtc.AddSeconds(a.ArriveIn);
				if (cfg.DefenderOff) {
					if (_expectedBackUtc <= impact.AddSeconds(-20)) return;
					double flown = (DateTime.UtcNow - _departUtc).TotalSeconds;
					if (DateTime.UtcNow.AddSeconds(flown + 15) > impact) {
						DoLog(LogLevel.Warning, $"ShadowFleet: attack {a.ID} on {c.Coordinate} lands too soon - the fleet cannot be home in time.");
						_recallCancelled = true;
						return;
					}
					var fleets = await _fleetScheduler.UpdateFleets();
					var f = fleets.FirstOrDefault(x => x.ID == _myFleetId);
					if (f == null || f.ReturnFlight) return;
					await _fleetScheduler.CancelFleet(f);
					_fleetScheduler.CancelPendingRecall(_myFleetId);
					_expectedBackUtc = DateTime.UtcNow.AddSeconds(flown + 15);
					_recallCancelled = true;
					DoLog(LogLevel.Warning, $"ShadowFleet: attack {a.ID} on {c.Coordinate} - fleet {_myFleetId} recalled, it is home in about {(int) flown} s (before impact).");
					_ = _tbotInstance.SendTelegramMessage($"ShadowFleet: attack on {c.Coordinate} - fleet {_myFleetId} is called home and will be there at impact.");
					return;
				}
				if (impact <= _expectedBackUtc.AddSeconds(120)) {
					_fleetScheduler.CancelPendingRecall(_myFleetId);
					_recallCancelled = true;
					DoLog(LogLevel.Warning, $"ShadowFleet: attack {a.ID} on {c.Coordinate} lands before the fleet would be back - recall cancelled, the fleet stays in the air.");
					_ = _tbotInstance.SendTelegramMessage($"ShadowFleet: attack on {c.Coordinate} - fleet {_myFleetId} stays in the air (recall cancelled).");
					return;
				}
			}
		}

		public static void ExcludeFromFleetSave(dynamic g, Celestial c) {
			ShadowFleetConfig cfg = ReadConfig(g);
			if (c?.Ships == null || !cfg.Active || !cfg.DefenderOff || !cfg.Matches(c.Coordinate.Galaxy, c.Coordinate.System, c.Coordinate.Position, c.Coordinate.Type.ToString())) return;
			foreach (var kv in cfg.Ships) {
				if (!Enum.TryParse(kv.Key, true, out Buildables b)) continue;
				long have = c.Ships.GetAmount(b);
				c.Ships.SetAmount(b, kv.Value <= 0 ? 0 : Math.Max(0, have - kv.Value));
			}
		}

		public override bool IsWorkerEnabledBySettings() => ReadConfig(_tbotInstance.InstanceSettings).Active;

		public override string GetWorkerName() {
			return "ShadowFleet";
		}

		public override Feature GetFeature() {
			return Feature.ShadowFleet;
		}

		public override LogSender GetLogSender() {
			return LogSender.ShadowFleet;
		}
	}
}
