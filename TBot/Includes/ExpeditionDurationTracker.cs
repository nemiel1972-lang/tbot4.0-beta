using Tbot.Common.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Includes {
	public class ExpeditionDurationTracker {
		private const int MaxSamplesPerOrigin = 30;
		private const int MinSamplesForEstimate = 3;
		private const int MinPlausibleSeconds = 300;
		private const int MaxPlausibleSeconds = 24 * 3600;

		private class Pending {
			public string OriginKey;
			public DateTime SentUtc;
			public DateTime EndUtc;
			public bool Seen;
		}

		private readonly object _lock = new();
		private readonly string _filePath;
		private readonly Action<string> _debugLog;
		private readonly Dictionary<int, Pending> _pending = new();
		private Dictionary<string, List<int>> _samples = new();

		public ExpeditionDurationTracker(string filePath, Action<string> debugLog = null) {
			_filePath = filePath;
			_debugLog = debugLog;
			Load();
		}

		public static string OriginKey(Coordinate c) {
			if (c == null)
				return "?";
			return $"{c.Galaxy}:{c.System}:{c.Position}:{c.Type}";
		}

		public void NoteSent(int fleetId, Coordinate origin, DateTime sentUtc) {
			if (fleetId <= 0)
				return;
			lock (_lock) {
				_pending[fleetId] = new Pending { OriginKey = OriginKey(origin), SentUtc = sentUtc, EndUtc = DateTime.MinValue, Seen = false };
			}
		}

		public void Observe(IEnumerable<Fleet> fleets, DateTime nowUtc) {
			if (fleets == null)
				return;
			var list = fleets.Where(f => f != null && f.Mission == Missions.Expedition).ToList();
			bool changed = false;
			lock (_lock) {
				foreach (var id in _pending.Keys.ToList()) {
					var p = _pending[id];
					var fleet = list.FirstOrDefault(f => f.ID == id);
					if (fleet != null) {
						if ((fleet.BackIn ?? 0) > 0) {
							p.EndUtc = nowUtc.AddSeconds(fleet.BackIn.Value);
							p.Seen = true;
						}
						continue;
					}
					if (p.Seen) {
						int seconds = (int) (p.EndUtc - p.SentUtc).TotalSeconds;
						if (seconds >= MinPlausibleSeconds && seconds <= MaxPlausibleSeconds) {
							if (!_samples.TryGetValue(p.OriginKey, out var originSamples)) {
								originSamples = new List<int>();
								_samples[p.OriginKey] = originSamples;
							}
							originSamples.Add(seconds);
							while (originSamples.Count > MaxSamplesPerOrigin)
								originSamples.RemoveAt(0);
							changed = true;
							_debugLog?.Invoke($"Expedition duration recorded for {p.OriginKey}: {seconds / 60} min ({originSamples.Count} samples).");
						}
						_pending.Remove(id);
					} else if ((nowUtc - p.SentUtc).TotalHours > 24) {
						_pending.Remove(id);
					}
				}
			}
			if (changed)
				Save();
		}

		public int? GetMaxDurationSeconds() {
			lock (_lock) {
				var perOrigin = _samples.Values.Where(v => v.Count >= MinSamplesForEstimate).Select(Percentile90).ToList();
				if (perOrigin.Count > 0)
					return perOrigin.Max();
				return PooledPercentile();
			}
		}

		private int? PooledPercentile() {
			var all = _samples.Values.SelectMany(v => v).ToList();
			if (all.Count < MinSamplesForEstimate)
				return null;
			return Percentile90(all);
		}

		private static int Percentile90(List<int> values) {
			var sorted = values.OrderBy(v => v).ToList();
			int index = (int) Math.Ceiling(sorted.Count * 0.9) - 1;
			return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
		}

		private void Load() {
			try {
				if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
					_samples = JsonConvert.DeserializeObject<Dictionary<string, List<int>>>(File.ReadAllText(_filePath)) ?? new();
			} catch (Exception e) {
				_debugLog?.Invoke($"Expedition duration data could not be loaded: {e.Message}");
				_samples = new();
			}
		}

		private void Save() {
			try {
				if (string.IsNullOrEmpty(_filePath))
					return;
				string json;
				lock (_lock) {
					json = JsonConvert.SerializeObject(_samples);
				}
				AtomicFile.WriteAllText(_filePath, json);
			} catch (Exception e) {
				_debugLog?.Invoke($"Expedition duration data could not be saved: {e.Message}");
			}
		}
	}
}
