using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using TBot.WebUI.Models;

namespace TBot.WebUI.Services {
	public static class GalaxyScanReader {
		private static readonly object Lock = new();
		private static readonly Dictionary<string, (DateTime Stamp, GalaxyScanStatus Status)> Memo = new();

		public static string GetPath(string instanceAlias) {
			string alias = Path.GetFileName(instanceAlias ?? "default");
			string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
			return Path.Combine(AppContext.BaseDirectory, "data", $"galaxycache_{safe}.json");
		}

		public static GalaxyScanStatus Read(string instanceAlias) {
			try {
				string path = GetPath(instanceAlias);
				if (!File.Exists(path))
					return new GalaxyScanStatus();
				DateTime stamp = File.GetLastWriteTimeUtc(path);
				lock (Lock) {
					if (Memo.TryGetValue(path, out var m) && m.Stamp == stamp)
						return m.Status;
				}
				var status = Berechne(JObject.Parse(File.ReadAllText(path)), DateTime.UtcNow);
				lock (Lock)
					Memo[path] = (stamp, status);
				return status;
			} catch {
				return new GalaxyScanStatus();
			}
		}

		public static GalaxyScanStatus Berechne(JObject root, DateTime nowUtc) {
			var info = root["ScanInfo"] as JObject;
			var systems = root["Systems"] as JObject ?? new JObject();
			var players = root["Players"] as JObject ?? new JObject();

			int galaxies = info?["Galaxies"]?.Value<int>() ?? 0, perGalaxy = info?["Systems"]?.Value<int>() ?? 0;
			int intervalHours = Math.Max(1, info?["IntervalHours"]?.Value<int>() ?? 24);
			double pause = info?["MeanPauseSec"]?.Value<double>() ?? 0;
			var fresh = nowUtc - TimeSpan.FromHours(intervalHours);

			int known = 0, freshCount = 0, maxG = 0;
			DateTime? oldest = null;
			foreach (var kv in systems) {
				var parts = kv.Key.Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[0], out int g) || !int.TryParse(parts[1], out int s))
					continue;
				if (galaxies > 0 && (g > galaxies || s > perGalaxy))
					continue;
				maxG = Math.Max(maxG, g);
				known++;
				var seen = kv.Value?["SeenUtc"]?.Value<DateTime>().ToUniversalTime();
				if (seen.HasValue) {
					if (seen.Value >= fresh)
						freshCount++;
					if (!oldest.HasValue || seen.Value < oldest.Value)
						oldest = seen.Value;
				}
			}
			if (galaxies <= 0) galaxies = Math.Max(1, maxG);
			if (perGalaxy <= 0) perGalaxy = 499;
			int total = galaxies * perGalaxy;

			var inactive = new HashSet<int>();
			var home = new Dictionary<int, (int G, int S, int P)>();
			foreach (var kv in players) {
				var p = kv.Value as JObject;
				if (p == null || !int.TryParse(kv.Key, out int id)) continue;
				home[id] = (p["HomeGalaxy"]?.Value<int>() ?? 0, p["HomeSystem"]?.Value<int>() ?? 0, p["HomePosition"]?.Value<int>() ?? 0);
				if ((p["Inactive"]?.Value<bool>() ?? false) && !(p["Vacation"]?.Value<bool>() ?? false) && !(p["Banned"]?.Value<bool>() ?? false))
					inactive.Add(id);
			}
			int targets = 0, colonies = 0;
			var scanInactive = new HashSet<int>();
			foreach (var kv in systems) {
				var parts = kv.Key.Split(':');
				if (parts.Length != 2) continue;
				var entries = kv.Value?["Entries"] as JArray;
				if (entries == null) continue;
				foreach (var e in entries) {
					if ((e["Inactive"]?.Value<bool>() ?? false) && !(e["Vacation"]?.Value<bool>() ?? false) && !(e["Banned"]?.Value<bool>() ?? false)) {
						int pid = e["PlayerId"]?.Value<int>() ?? 0;
						if (pid > 0) scanInactive.Add(pid);
					}
				}
			}
			foreach (var id in scanInactive) inactive.Add(id);
			foreach (var kv in systems) {
				var parts = kv.Key.Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[0], out int g) || !int.TryParse(parts[1], out int s)) continue;
				var entries = kv.Value?["Entries"] as JArray;
				if (entries == null) continue;
				foreach (var e in entries) {
					int pid = e["PlayerId"]?.Value<int>() ?? 0;
					if (!inactive.Contains(pid)) continue;
					targets++;
					int pos = e["Position"]?.Value<int>() ?? 0;
					if (home.TryGetValue(pid, out var h) && h.G > 0 && !(h.G == g && h.S == s && h.P == pos))
						colonies++;
				}
			}

			int remaining = Math.Max(0, total - freshCount);
			double? eta = pause > 0 ? remaining * pause : null;
			DateTime? nextDue = remaining == 0 && oldest.HasValue ? oldest.Value + TimeSpan.FromHours(intervalHours) : null;
			string last = info != null && (info["LastGalaxy"]?.Value<int>() ?? 0) > 0 ? $"{info["LastGalaxy"]}:{info["LastSystem"]}" : string.Empty;
			DateTime? lastUtc = info?["LastUtc"]?.Value<DateTime>().ToUniversalTime();
			DateTime? hs = root["HighscoreUtc"]?.Value<DateTime>().ToUniversalTime();
			if (hs.HasValue && hs.Value.Year < 2000) hs = null;

			return new GalaxyScanStatus {
				Available = true, TotalSystems = total, FreshSystems = freshCount, KnownSystems = known,
				Percent = total > 0 ? Math.Round(100.0 * freshCount / total, 1) : 0,
				LastCoord = last, LastKind = info?["LastKind"]?.Value<string>() ?? string.Empty, LastUtc = lastUtc,
				EtaSeconds = eta, NextDueUtc = nextDue,
				Players = players.Count, InactivePlayers = inactive.Count, Targets = targets, Colonies = colonies, HighscoreUtc = hs
			};
		}
	}
}
