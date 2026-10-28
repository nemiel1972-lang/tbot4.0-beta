using System;
using System.Collections.Generic;
using System.Linq;

namespace TBot.Common.Logging {
	public sealed class TelegramRepeatLimit {
		private const int MaxEntries = 2000;

		private readonly object _sync = new object();
		private readonly int _default;
		private readonly Dictionary<string, int> _perSender;
		private readonly Dictionary<string, DateTime> _lastSent = new Dictionary<string, DateTime>();

		public int DefaultMinutes => _default;

		private TelegramRepeatLimit(int defaultMinutes, Dictionary<string, int> perSender) {
			_default = defaultMinutes;
			_perSender = perSender;
		}

		public static TelegramRepeatLimit Off() {
			return new TelegramRepeatLimit(0, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
		}

		public static TelegramRepeatLimit Parse(int defaultMinutes, IEnumerable<KeyValuePair<string, string>> perSender) {
			var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			if (perSender != null) {
				foreach (var kv in perSender) {
					if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
						continue;
					if (int.TryParse(kv.Value.Trim(), out int m) && m >= 0)
						map[kv.Key.Trim()] = m;
				}
			}
			return new TelegramRepeatLimit(Math.Max(0, defaultMinutes), map);
		}

		public int MinutesFor(string sender) {
			if (!string.IsNullOrEmpty(sender) && _perSender.TryGetValue(sender, out int own))
				return own;
			return _default;
		}

		public bool ShouldSend(string sender, string message, DateTime nowUtc) {
			int minutes = MinutesFor(sender);
			if (minutes <= 0)
				return true;
			string key = (sender ?? "").ToUpperInvariant() + "|" + Normalize(message);
			lock (_sync) {
				if (_lastSent.TryGetValue(key, out DateTime last) && nowUtc - last < TimeSpan.FromMinutes(minutes))
					return false;
				if (_lastSent.Count >= MaxEntries)
					Prune(nowUtc);
				_lastSent[key] = nowUtc;
				return true;
			}
		}

		private void Prune(DateTime nowUtc) {
			int longest = Math.Max(_default, _perSender.Count == 0 ? 0 : _perSender.Values.Max());
			var old = _lastSent.Where(k => nowUtc - k.Value >= TimeSpan.FromMinutes(longest)).Select(k => k.Key).ToList();
			foreach (var k in old)
				_lastSent.Remove(k);
			if (_lastSent.Count >= MaxEntries) {
				foreach (var k in _lastSent.OrderBy(x => x.Value).Take(MaxEntries / 2).Select(x => x.Key).ToList())
					_lastSent.Remove(k);
			}
		}

		internal static string Normalize(string message) {
			if (string.IsNullOrEmpty(message))
				return "";
			var sb = new System.Text.StringBuilder(message.Length);
			bool inDigits = false;
			foreach (char c in message) {
				if (char.IsDigit(c)) {
					if (!inDigits) sb.Append('#');
					inDigits = true;
				} else {
					sb.Append(c);
					inDigits = false;
				}
			}
			return sb.ToString();
		}

		public string Describe() {
			if (_default <= 0 && _perSender.Count == 0)
				return "repeat limit off";
			var parts = _perSender.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase).Select(k => $"{k.Key}={k.Value}");
			return $"repeat limit {_default} min" + (_perSender.Count > 0 ? ", " + string.Join(", ", parts) : "");
		}
	}
}
