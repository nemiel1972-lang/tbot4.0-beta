using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
	internal class GalaxyCacheConfig {
		public bool Active = true;
		public int MaxAgeHours = 12;
		public int RangeSystems = 15;
		public int DelayMinSeconds = 20;
		public int DelayMaxSeconds = 45;
		public bool UseInFastFarm = true;
		public bool UseInColonize = true;
		public bool Events = true;
		public bool Telegram = true;

		public TimeSpan MaxAge => TimeSpan.FromHours(Math.Max(1, MaxAgeHours));

		public static GalaxyCacheConfig Read(ITBotMain inst) {
			var c = new GalaxyCacheConfig();
			try {
				var g = inst?.InstanceSettings;
				if (g == null || !SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "GalaxyCache"))
					return c;
				var x = g.Experimental.GalaxyCache;
				if (SettingsService.IsSettingSet(x, "Active")) c.Active = (bool) x.Active;
				if (SettingsService.IsSettingSet(x, "MaxAgeHours")) c.MaxAgeHours = Math.Max(1, (int) x.MaxAgeHours);
				if (SettingsService.IsSettingSet(x, "RangeSystems")) c.RangeSystems = Math.Max(0, (int) x.RangeSystems);
				if (SettingsService.IsSettingSet(x, "DelayMinSeconds")) c.DelayMinSeconds = Math.Max(5, (int) x.DelayMinSeconds);
				if (SettingsService.IsSettingSet(x, "DelayMaxSeconds")) c.DelayMaxSeconds = Math.Max(c.DelayMinSeconds, (int) x.DelayMaxSeconds);
				if (SettingsService.IsSettingSet(x, "UseInFastFarm")) c.UseInFastFarm = (bool) x.UseInFastFarm;
				if (SettingsService.IsSettingSet(x, "UseInColonize")) c.UseInColonize = (bool) x.UseInColonize;
				if (SettingsService.IsSettingSet(x, "Events")) c.Events = (bool) x.Events;
				if (SettingsService.IsSettingSet(x, "Telegram")) c.Telegram = (bool) x.Telegram;
			} catch (Exception) {
			}
			return c;
		}
	}

	internal static class GalaxyCacheHub {
		private static readonly ConcurrentDictionary<string, GalaxyCache> _caches = new();

		public static GalaxyCache For(ITBotMain inst) {
			try {
				if (inst == null || !GalaxyCacheConfig.Read(inst).Active)
					return null;
				string alias = Path.GetFileName(inst.InstanceAlias ?? "default");
				string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				return _caches.GetOrAdd(safe, _ => {
					var cache = new GalaxyCache(Path.Combine(AppContext.BaseDirectory, "data", $"galaxycache_{safe}.json"));
					cache.Load();
					return cache;
				});
			} catch (Exception) {
				return null;
			}
		}

		public static async Task<GalaxyInfo> GetOrLiveAsync(ITBotMain inst, IOgameService ogame, int galaxy, int system, bool enabled) {
			if (enabled) {
				var cache = For(inst);
				var cfg = GalaxyCacheConfig.Read(inst);
				var hit = cache?.TryGet(galaxy, system, cfg.MaxAge, DateTime.UtcNow);
				if (hit != null) {
					ExtraLog.Write(inst, "GalaxyCache", $"Cache hit {galaxy}:{system} (age < {cfg.MaxAgeHours} h), live query saved.");
					return hit;
				}
			}
			return await ogame.GetGalaxyInfo(galaxy, system);
		}

		public static bool KnownOccupied(ITBotMain inst, Coordinate c, bool enabled) {
			if (!enabled)
				return false;
			try {
				var cache = For(inst);
				if (cache == null)
					return false;
				bool occupied = cache.IsOccupiedFresh(c, GalaxyCacheConfig.Read(inst).MaxAge, DateTime.UtcNow);
				if (occupied)
					ExtraLog.Write(inst, "GalaxyCache", $"Cache says {c.Galaxy}:{c.System}:{c.Position} is occupied, live query saved.");
				return occupied;
			} catch (Exception) {
				return false;
			}
		}
	}

	internal class GalaxyCacheWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private bool _observerSet;

		public GalaxyCacheWorker(ITBotMain parentInstance, IOgameService ogameService, ITBotOgamedBridge tbotOgameBridge) : base(parentInstance) {
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		private void GcLog(LogLevel level, string text) {
			DoLog(level, text);
			ExtraLog.Write(_tbotInstance, "GalaxyCache", $"[{level}] {text}");
		}

		private void OnScan(GalaxyInfo info) {
			var cfg = GalaxyCacheConfig.Read(_tbotInstance);
			if (!cfg.Active)
				return;
			var cache = GalaxyCacheHub.For(_tbotInstance);
			if (cache == null)
				return;
			try {
				cache.OwnPlayerId = _tbotInstance.UserData?.userInfo?.PlayerID ?? 0;
			} catch (Exception) {
			}
			var events = cache.Record(info, DateTime.UtcNow);
			cache.SaveIfDue(DateTime.UtcNow);
			if (!cfg.Events)
				return;
			foreach (var e in events)
				Melde(e, cfg);
		}

		private void Melde(GalaxyEvent e, GalaxyCacheConfig cfg) {
			string spieler = string.IsNullOrEmpty(e.PlayerName) ? "?" : e.PlayerName;
			string text = e.Type switch {
				GalaxyEventType.New => $"New celestial of {spieler} at {e.Coord} ({e.Detail})",
				GalaxyEventType.Gone => $"Celestial of {spieler} at {e.Coord} is gone ({e.Detail})",
				GalaxyEventType.Moon => $"{spieler} got a new moon at {e.Coord}",
				GalaxyEventType.Status => $"{spieler} at {e.Coord}: status {e.Detail}",
				GalaxyEventType.Move => $"{spieler} moved: {e.Detail} -> {e.Coord}",
				_ => $"{e.Type} {e.Coord}"
			};
			bool wichtig = e.Type == GalaxyEventType.New || e.Type == GalaxyEventType.Move;
			if (string.Equals(e.PlayerName, "space", StringComparison.OrdinalIgnoreCase))
				wichtig = false;
			DoLog(wichtig ? LogLevel.Warning : LogLevel.Information, text);
			ExtraLog.Write(_tbotInstance, "GalaxyCache", $"Event: {text}");
			if (cfg.Telegram && wichtig) {
				try {
					_ = _tbotInstance.SendTelegramMessage($"[GalaxyCache] {text}");
				} catch (Exception) {
				}
			}
		}

		private List<(int Galaxy, int System)> Candidates(int range) {
			var own = _tbotInstance.UserData?.celestials?.Where(c => c?.Coordinate != null)
				.Select(c => (c.Coordinate.Galaxy, c.Coordinate.System)).Distinct().ToList() ?? new();
			int maxSystem = Math.Max(1, _tbotInstance.UserData?.serverData?.Systems ?? 499);
			var result = new HashSet<(int, int)>();
			foreach (var (g, s) in own) {
				for (int d = -range; d <= range; d++) {
					int sys = s + d;
					if (sys < 1 || sys > maxSystem)
						continue;
					result.Add((g, sys));
				}
			}
			return result.ToList();
		}

		protected override async Task Execute() {
			var cfg = GalaxyCacheConfig.Read(_tbotInstance);
			long nextMs = 10 * 60 * 1000;
			try {
				if (!cfg.Active) {
					_ogameService.SetGalaxyObserver(null);
					_observerSet = false;
				} else {
					if (!_observerSet) {
						_ogameService.SetGalaxyObserver(OnScan);
						_observerSet = true;
						GcLog(LogLevel.Information, "Galaxy cache is on: every galaxy scan is stored; background scan around your planets runs slowly.");
					}
					var cache = GalaxyCacheHub.For(_tbotInstance);
					if (cache != null && !_tbotInstance.UserData.isSleeping && !_tbotInstance.IsBotPaused) {
						var next = cache.NextToScan(Candidates(cfg.RangeSystems), cfg.MaxAge, DateTime.UtcNow);
						if (next.HasValue) {
							await _ogameService.GetGalaxyInfo(next.Value.Galaxy, next.Value.System);
							ExtraLog.Write(_tbotInstance, "GalaxyCache", $"Background scan {next.Value.Galaxy}:{next.Value.System}, cache holds {cache.SystemCount} system(s).");
							nextMs = RandomizeHelper.CalcRandomIntervalSecToMs(cfg.DelayMinSeconds, cfg.DelayMaxSeconds);
						} else {
							cache.SaveIfDue(DateTime.UtcNow, true);
							nextMs = 10 * 60 * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
						}
					}
				}
			} catch (Exception e) {
				GcLog(LogLevel.Debug, $"Galaxy cache scan failed: {e.Message}");
				nextMs = 2 * 60 * 1000;
			}
			ChangeWorkerPeriod(nextMs);
		}

		public override bool IsWorkerEnabledBySettings() => GalaxyCacheConfig.Read(_tbotInstance).Active;

		public override string GetWorkerName() => "GalaxyCache";

		public override Feature GetFeature() => Feature.GalaxyCache;

		public override LogSender GetLogSender() => LogSender.GalaxyCache;
	}
}
