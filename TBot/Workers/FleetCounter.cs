using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tbot.Common.IO;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public static class FleetCounter {
		private static readonly Dictionary<string, Dictionary<string, Dictionary<string, long>>> Data = new();
		private static readonly object _lock = new();
		private static bool _dirty;
		private static DateTime _lastSave = DateTime.MinValue;

		private static Dictionary<string, Dictionary<string, long>> Days(string alias) {
			if (!Data.TryGetValue(alias, out var d))
				Data[alias] = d = new Dictionary<string, Dictionary<string, long>>();
			return d;
		}

		public static void Count(string alias, string worker, Missions mission) {
			try {
				string day = DateTime.Now.ToString("yyyy-MM-dd");
				string key = (string.IsNullOrEmpty(worker) ? "Other" : worker) + "|" + mission;
				lock (_lock) {
					var days = Days(alias ?? "MAIN");
					if (!days.TryGetValue(day, out var m))
						days[day] = m = new Dictionary<string, long>();
					m[key] = (m.TryGetValue(key, out long v) ? v : 0) + 1;
					_dirty = true;
				}
			} catch (Exception) {  }
		}

		public static void Load(string alias, string path) {
			try {
				if (!File.Exists(path))
					return;
				var d = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, long>>>(File.ReadAllText(path));
				if (d == null)
					return;
				lock (_lock) { Data[alias ?? "MAIN"] = d; }
			} catch (Exception) {  }
		}

		public static void SaveIfDue(string alias, string path) {
			try {
				string json;
				lock (_lock) {
					if (!_dirty || (DateTime.UtcNow - _lastSave).TotalSeconds < 60)
						return;
					_dirty = false;
					_lastSave = DateTime.UtcNow;
					var days = Days(alias ?? "MAIN");
					string grenze = DateTime.Now.AddDays(-30).ToString("yyyy-MM-dd");
					foreach (var alt in days.Keys.Where(k => string.CompareOrdinal(k, grenze) < 0).ToList())
						days.Remove(alt);
					json = JsonConvert.SerializeObject(days);
				}
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				AtomicFile.WriteAllText(path, json);
			} catch (Exception) {  }
		}

		public static object Snapshot(string alias) {
			lock (_lock) {
				var days = Days(alias ?? "MAIN");
				string heute = DateTime.Now.ToString("yyyy-MM-dd");
				string g7 = DateTime.Now.AddDays(-6).ToString("yyyy-MM-dd");
				var rows = new Dictionary<string, long[]>();
				foreach (var day in days) {
					foreach (var kv in day.Value) {
						if (!rows.TryGetValue(kv.Key, out var r))
							rows[kv.Key] = r = new long[3];
						r[2] += kv.Value;
						if (string.CompareOrdinal(day.Key, g7) >= 0) r[1] += kv.Value;
						if (day.Key == heute) r[0] += kv.Value;
					}
				}
				return rows.OrderByDescending(r => r.Value[2]).Select(r => {
					var p = r.Key.Split('|');
					return new { worker = p[0], mission = p.Length > 1 ? p[1] : "", today = r.Value[0], week = r.Value[1], month = r.Value[2] };
				}).ToList();
			}
		}
	}
}
