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
		public bool ScanAll = true;
		public int RangeSystems = 15;
		public double HoursPerGalaxy = 1.0;
		public int FullScanIntervalHours = 24;
		public bool UseApi = true;
		public bool UseHighscore = true;
		public int HighscoreIntervalHours = 24;
		public int FastFarmMaxAgeHours = 36;
		public int AutoFarmMaxAgeHours = 36;
		public int EmptyMaxAgeHours = 72;
		public int DelayMinSeconds = 4;
		public int DelayMaxSeconds = 8;
		public bool UseInFastFarm = true;
		public bool UseInColonize = true;
		public bool UseInAutoFarm = true;
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
				if (SettingsService.IsSettingSet(x, "HoursPerGalaxy")) c.HoursPerGalaxy = Math.Max(0, Convert.ToDouble(x.HoursPerGalaxy));
				if (SettingsService.IsSettingSet(x, "FullScanIntervalHours")) c.FullScanIntervalHours = Math.Max(1, (int) x.FullScanIntervalHours);
				if (SettingsService.IsSettingSet(x, "UseApi")) c.UseApi = (bool) x.UseApi;
				if (SettingsService.IsSettingSet(x, "UseHighscore")) c.UseHighscore = (bool) x.UseHighscore;
				if (SettingsService.IsSettingSet(x, "HighscoreIntervalHours")) c.HighscoreIntervalHours = Math.Max(1, (int) x.HighscoreIntervalHours);
				if (SettingsService.IsSettingSet(x, "FastFarmMaxAgeHours")) c.FastFarmMaxAgeHours = Math.Max(1, (int) x.FastFarmMaxAgeHours);
				if (SettingsService.IsSettingSet(x, "AutoFarmMaxAgeHours")) c.AutoFarmMaxAgeHours = Math.Max(1, (int) x.AutoFarmMaxAgeHours);
				if (SettingsService.IsSettingSet(x, "ScanAll")) c.ScanAll = (bool) x.ScanAll;
				if (SettingsService.IsSettingSet(x, "EmptyMaxAgeHours")) c.EmptyMaxAgeHours = Math.Max(1, (int) x.EmptyMaxAgeHours);
				if (SettingsService.IsSettingSet(x, "RangeSystems")) c.RangeSystems = Math.Max(0, (int) x.RangeSystems);
				if (SettingsService.IsSettingSet(x, "DelayMinSeconds")) c.DelayMinSeconds = Math.Max(2, (int) x.DelayMinSeconds);
				if (SettingsService.IsSettingSet(x, "DelayMaxSeconds")) c.DelayMaxSeconds = Math.Max(c.DelayMinSeconds, (int) x.DelayMaxSeconds);
				if (SettingsService.IsSettingSet(x, "UseInFastFarm")) c.UseInFastFarm = (bool) x.UseInFastFarm;
				if (SettingsService.IsSettingSet(x, "UseInColonize")) c.UseInColonize = (bool) x.UseInColonize;
				if (SettingsService.IsSettingSet(x, "UseInAutoFarm")) c.UseInAutoFarm = (bool) x.UseInAutoFarm;
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

		public static async Task<GalaxyInfo> GetOrLiveAsync(ITBotMain inst, IOgameService ogame, int galaxy, int system, bool enabled, TimeSpan? maxAge = null) {
			if (enabled) {
				var cache = For(inst);
				var cfg = GalaxyCacheConfig.Read(inst);
				var hit = cache?.TryGet(galaxy, system, maxAge ?? cfg.MaxAge, DateTime.UtcNow);
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
		private DateTime _lastHighscoreTryUtc = DateTime.MinValue;

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

		private List<(int Galaxy, int System)> Candidates(GalaxyCacheConfig cfg) {
			var near = CandidatesNear(cfg.RangeSystems);
			if (!cfg.ScanAll)
				return near;
			int galaxies = Math.Max(1, _tbotInstance.UserData?.serverData?.Galaxies ?? 9);
			int maxSystem = Math.Max(1, _tbotInstance.UserData?.serverData?.Systems ?? 499);
			var home = _tbotInstance.UserData?.celestials?.FirstOrDefault(c => c?.Coordinate != null && c.Coordinate.Type == Celestials.Planet)
				?? _tbotInstance.UserData?.celestials?.FirstOrDefault(c => c?.Coordinate != null);
			int homeG = Math.Clamp(home?.Coordinate.Galaxy ?? 1, 1, galaxies);
			int homeS = Math.Clamp(home?.Coordinate.System ?? 1, 1, maxSystem);
			var result = new List<(int Galaxy, int System)>(galaxies * maxSystem);
			for (int dg = 0; result.Count < galaxies * maxSystem && dg < galaxies; dg++) {
				foreach (int g in new[] { homeG + dg, homeG - dg }.Distinct()) {
					if (g < 1 || g > galaxies)
						continue;
					for (int ds = 0; ds < maxSystem; ds++) {
						foreach (int s in new[] { homeS + ds, homeS - ds }.Distinct()) {
							if (s >= 1 && s <= maxSystem)
								result.Add((g, s));
						}
					}
				}
			}
			return result.Distinct().ToList();
		}

		private List<(int Galaxy, int System)> CandidatesNear(int range) {
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

		private static readonly System.Net.Http.HttpClient ApiClient = new() { Timeout = TimeSpan.FromSeconds(30) };

		private string ApiHost() {
			try {
				var wl = _tbotInstance.InstanceSettings?.AutoFarm?.AllianceWhitelist;
				if (wl != null && SettingsService.IsSettingSet(wl, "ServerHost")) {
					string h = (string) wl.ServerHost;
					if (!string.IsNullOrWhiteSpace(h) && !h.Contains("NNN", StringComparison.OrdinalIgnoreCase) && !h.Contains("xx", StringComparison.OrdinalIgnoreCase))
						return h.Trim();
				}
			} catch (Exception) {
			}
			var sd = _tbotInstance.UserData?.serverData;
			return sd != null && sd.Number > 0 && !string.IsNullOrWhiteSpace(sd.Language) ? $"s{sd.Number}-{sd.Language}.ogame.gameforge.com" : null;
		}

		private async Task<bool> RefreshFromApi(GalaxyCache cache) {
			string host = ApiHost();
			if (host == null)
				return false;
			try {
				DateTime start = DateTime.UtcNow;
				var players = System.Xml.Linq.XDocument.Parse(await ApiClient.GetStringAsync($"https://{host}/api/players.xml"));
				var ranks = new Dictionary<int, int>();
				try {
					var hs = System.Xml.Linq.XDocument.Parse(await ApiClient.GetStringAsync($"https://{host}/api/highscore.xml?category=1&type=0"));
					foreach (var e in hs.Descendants("player")) {
						if (int.TryParse((string) e.Attribute("id"), out int id) && int.TryParse((string) e.Attribute("position"), out int pos))
							ranks[id] = pos;
					}
				} catch (Exception) {
				}
				var list = new List<HighscorePlayer>();
				foreach (var e in players.Descendants("player")) {
					if (!int.TryParse((string) e.Attribute("id"), out int id))
						continue;
					string st = (string) e.Attribute("status") ?? "";
					var status = new List<string>();
					if (st.Contains('v')) status.Add("vacation");
					if (st.Contains('i')) status.Add("inactive");
					if (st.Contains('I')) { status.Add("inactive"); status.Add("longinactive"); }
					if (st.Contains('b')) status.Add("banned");
					if (st.Contains('o')) status.Add("outlaw");
					int.TryParse((string) e.Attribute("alliance"), out int ally);
					list.Add(new HighscorePlayer { ID = id, Name = (string) e.Attribute("name"), Position = ranks.TryGetValue(id, out int r) ? r : 0, AllianceID = ally, Status = status });
				}
				if (list.Count == 0)
					return false;
				cache.RecordHighscore(list, DateTime.UtcNow, true);
				cache.FinishHighscore(start, DateTime.UtcNow);
				cache.SaveIfDue(DateTime.UtcNow, true);
				GcLog(LogLevel.Information, $"Player list read from the OGame API ({host}): {list.Count} player(s), {ranks.Count} rank(s).");
				return true;
			} catch (Exception e) {
				GcLog(LogLevel.Debug, $"OGame API read failed ({host}): {e.Message}");
				return false;
			}
		}

		private async Task RefreshHighscore(GalaxyCache cache) {
			if (GalaxyCacheConfig.Read(_tbotInstance).UseApi && await RefreshFromApi(cache))
				return;
			DateTime start = DateTime.UtcNow;
			int seiten = 0, spieler = 0;
			try {
				int total = 1;
				for (int page = 1; page <= total; page++) {
					var hs = await _ogameService.GetHighscore(1, 0, page);
					if (hs == null)
						break;
					total = Math.Max(1, hs.NbPage);
					cache.RecordHighscore(hs.Players, DateTime.UtcNow);
					seiten++;
					spieler += hs.Players?.Count ?? 0;
					if (page < total)
						await Task.Delay(RandomizeHelper.CalcRandomIntervalSecToMs(2, 4));
				}
				if (seiten > 0 && seiten >= total) {
					cache.FinishHighscore(start, DateTime.UtcNow);
					cache.SaveIfDue(DateTime.UtcNow, true);
					GcLog(LogLevel.Information, $"Highscore read: {spieler} player(s) on {seiten} page(s); {cache.PlayerCount} player(s) in the database.");
				}
			} catch (Exception e) {
				GcLog(LogLevel.Warning, $"Highscore read failed after {seiten} page(s): {e.Message}");
			}
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
						if (cfg.UseHighscore && DateTime.UtcNow - cache.HighscoreUtc >= TimeSpan.FromHours(cfg.HighscoreIntervalHours) && DateTime.UtcNow - _lastHighscoreTryUtc >= TimeSpan.FromHours(1)) {
							_lastHighscoreTryUtc = DateTime.UtcNow;
							await RefreshHighscore(cache);
						}
						var now = DateTime.UtcNow;
						var next = cfg.UseHighscore ? cache.NextInactiveSystem(cfg.MaxAge, now) : null;
						string art = "inactive players";
						if (!next.HasValue) {
							art = cfg.ScanAll ? "full scan" : "surroundings";
							var grenze = cfg.ScanAll ? TimeSpan.FromHours(cfg.FullScanIntervalHours) : cfg.MaxAge;
							next = cache.NextToScan(Candidates(cfg), grenze, now, TimeSpan.FromHours(cfg.EmptyMaxAgeHours));
						}
						if (next.HasValue) {
							await _ogameService.GetGalaxyInfo(next.Value.Galaxy, next.Value.System);
							ExtraLog.Write(_tbotInstance, "GalaxyCache", $"Background scan ({art}) {next.Value.Galaxy}:{next.Value.System}, cache holds {cache.SystemCount} system(s), {cache.PlayerCount} player(s).");
							if (cfg.ScanAll && cfg.HoursPerGalaxy > 0) {
								int systems = Math.Max(1, _tbotInstance.UserData?.serverData?.Systems ?? 499);
								double mittel = cfg.HoursPerGalaxy * 3600.0 / systems;
								nextMs = (long) (Math.Max(2.0, mittel) * 1000 * (0.8 + 0.4 * Random.Shared.NextDouble()));
							} else {
								nextMs = RandomizeHelper.CalcRandomIntervalSecToMs(cfg.DelayMinSeconds, cfg.DelayMaxSeconds);
							}
							cache.SetScanInfo(new GalaxyScanInfo {
								Galaxies = Math.Max(1, _tbotInstance.UserData?.serverData?.Galaxies ?? 9),
								Systems = Math.Max(1, _tbotInstance.UserData?.serverData?.Systems ?? 499),
								IntervalHours = cfg.FullScanIntervalHours, MeanPauseSec = nextMs / 1000.0, ScanAll = cfg.ScanAll,
								LastGalaxy = next.Value.Galaxy, LastSystem = next.Value.System, LastUtc = DateTime.UtcNow, LastKind = art
							});
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
