using System;
using System.Collections.Generic;
using System.Linq;
using Serilog.Events;

namespace TBot.Common.Logging {
	public sealed class TelegramLogLevels {
		private const int OffValue = int.MaxValue;

		public const string DefaultLevelName = "Warning";

		private readonly Dictionary<string, int> _perSender;

		public LogEventLevel Default { get; }

		private TelegramLogLevels(LogEventLevel defaultLevel, Dictionary<string, int> perSender) {
			Default = defaultLevel;
			_perSender = perSender;
		}

		public static TelegramLogLevels OnlyDefault(LogEventLevel level) {
			return new TelegramLogLevels(level, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));
		}

		public static TelegramLogLevels Parse(string defaultLevel, IEnumerable<KeyValuePair<string, string>> perSender) {
			LogEventLevel def = TryParseLevel(defaultLevel, out int d) && d != OffValue ? (LogEventLevel) d : LogEventLevel.Warning;
			var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			if (perSender != null) {
				foreach (var kv in perSender) {
					if (string.IsNullOrWhiteSpace(kv.Key))
						continue;
					if (string.IsNullOrWhiteSpace(kv.Value) || kv.Value.Trim().Equals("Default", StringComparison.OrdinalIgnoreCase))
						continue;
					if (TryParseLevel(kv.Value, out int v))
						map[kv.Key.Trim()] = v;
				}
			}
			return new TelegramLogLevels(def, map);
		}

		public TelegramLogLevels WithDefault(LogEventLevel level) {
			return new TelegramLogLevels(level, new Dictionary<string, int>(_perSender, StringComparer.OrdinalIgnoreCase));
		}

		public bool Allows(string sender, LogEventLevel level) {
			int threshold = (int) Default;
			if (!string.IsNullOrEmpty(sender) && _perSender.TryGetValue(sender, out int own))
				threshold = own;
			return threshold != OffValue && (int) level >= threshold;
		}

		public LogEventLevel Lowest {
			get {
				int lowest = (int) Default;
				foreach (int v in _perSender.Values) {
					if (v != OffValue && v < lowest)
						lowest = v;
				}
				return (LogEventLevel) lowest;
			}
		}

		public string Describe() {
			if (_perSender.Count == 0)
				return $"default {Default}";
			var parts = _perSender.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase)
				.Select(k => $"{k.Key}={(k.Value == OffValue ? "Off" : ((LogEventLevel) k.Value).ToString())}");
			return $"default {Default}, " + string.Join(", ", parts);
		}

		private static bool TryParseLevel(string text, out int value) {
			value = 0;
			if (string.IsNullOrWhiteSpace(text))
				return false;
			string t = text.Trim();
			if (t.Equals("Off", StringComparison.OrdinalIgnoreCase) || t.Equals("None", StringComparison.OrdinalIgnoreCase)) {
				value = OffValue;
				return true;
			}
			if (t.Equals("Trace", StringComparison.OrdinalIgnoreCase)) t = "Verbose";
			if (t.Equals("Critical", StringComparison.OrdinalIgnoreCase)) t = "Fatal";
			if (Enum.TryParse<LogEventLevel>(t, true, out var lvl) && Enum.IsDefined(typeof(LogEventLevel), lvl)) {
				value = (int) lvl;
				return true;
			}
			return false;
		}
	}
}
