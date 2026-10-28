using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TBot.Common.Logging {
	public static class CollectLogs {
		private const long MaxBytes = 300L * 1024 * 1024;
		private static readonly object _lock = new();
		private static bool _aktiv;
		private static bool _anonym = true;
		private static DateTime _start = DateTime.MinValue;
		private static DateTime _ende = DateTime.MinValue;
		private static string _datei;
		private static bool _endeGeschrieben;

		public static string Folder => Path.Combine(Directory.GetCurrentDirectory(), "devlogs");
		private static string StartMarker => Path.Combine(Folder, ".collect-start");

		public static void Configure(bool active, double hours, bool anonymize) {
			try {
				lock (_lock) {
					_anonym = anonymize;
					if (!active) {
						_aktiv = false;
						_datei = null;
						_endeGeschrieben = false;
						if (File.Exists(StartMarker))
							File.Delete(StartMarker);
						return;
					}
					if (hours <= 0)
						hours = 12;
					if (_datei == null) {
						Directory.CreateDirectory(Folder);
						DateTime start = DateTime.Now;
						string name = null;
						if (File.Exists(StartMarker)) {
							string[] z = File.ReadAllLines(StartMarker);
							if (z.Length >= 2 && DateTime.TryParse(z[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var s)) {
								start = s;
								name = z[1];
							}
						}
						if (name == null) {
							name = $"TBot-collect-{start:yyyyMMdd-HHmmss}.log";
							File.WriteAllLines(StartMarker, new[] { start.ToString("o"), name });
						}
						_start = start;
						_datei = Path.Combine(Folder, name);
					}
					_ende = _start.AddHours(hours);
					_aktiv = true;
				}
			} catch (Exception) {
				_aktiv = false;
			}
		}

		public static bool AnonymizeEnabled { get { lock (_lock) { return _anonym; } } }

		public static bool IsRunning { get { lock (_lock) { return _aktiv && DateTime.Now < _ende; } } }

		private static bool Laeuft() {
			if (!_aktiv)
				return false;
			if (DateTime.Now < _ende)
				return true;
			if (!_endeGeschrieben) {
				_endeGeschrieben = true;
				try { File.AppendAllText(_datei, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} --- Collection ended after {(_ende - _start).TotalHours:0.##} h. Switch Experimental.CollectLogs.Active off and on to collect again. ---{Environment.NewLine}"); } catch (Exception) { }
			}
			return false;
		}

		private static void Append(string kennung, string message) {
			try {
				lock (_lock) {
					if (!Laeuft())
						return;
					if (File.Exists(_datei) && new FileInfo(_datei).Length > MaxBytes)
						return;
					string text = _anonym ? LogAnonymizer.ApplyForSave(message) : message;
					File.AppendAllText(_datei, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {kennung} {text}{Environment.NewLine}");
				}
			} catch (Exception) {
			}
		}

		public static void Write(LogLevel level, string sender, string message) => Append($"{level} [{sender}]", message);

		public static void WriteExtra(string thema, string message) => Append($"Extra [{thema}]", message);
	}
}
