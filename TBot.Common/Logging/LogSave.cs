using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TBot.Common.Logging {
	public static class LogSave {
		private const long MaxBytesProTag = 50L * 1024 * 1024;
		private static readonly object _lock = new();
		private static DateTime _markerGeprueft = DateTime.MinValue;
		private static bool _aktiv;
		private static DateTime _aufgeraeumt = DateTime.MinValue;

		public static string MarkerPath => Path.Combine(AppContext.BaseDirectory, "logsave.on");

		private static bool Aktiv() {
			var jetzt = DateTime.UtcNow;
			if ((jetzt - _markerGeprueft).TotalSeconds >= 5) {
				_markerGeprueft = jetzt;
				try { _aktiv = File.Exists(MarkerPath); } catch (Exception) { _aktiv = false; }
			}
			return _aktiv;
		}

		public static void Write(LogLevel level, string sender, string message) {
			try {
				if (!Aktiv())
					return;
				string dir = Path.Combine(Directory.GetCurrentDirectory(), "devlogs");
				lock (_lock) {
					Directory.CreateDirectory(dir);
					Aufraeumen(dir);
					string datei = Path.Combine(dir, $"TBot-logsave-{DateTime.Now:yyyyMMdd}.log");
					if (File.Exists(datei) && new FileInfo(datei).Length > MaxBytesProTag)
						return;
					File.AppendAllText(datei, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} [{sender}] {LogAnonymizer.ApplyForSave(message)}{Environment.NewLine}");
				}
			} catch (Exception) {
			}
		}

		private static void Aufraeumen(string dir) {
			if ((DateTime.Now - _aufgeraeumt).TotalHours < 24)
				return;
			_aufgeraeumt = DateTime.Now;
			try {
				foreach (string f in Directory.GetFiles(dir, "TBot-logsave-*.log"))
					if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
						File.Delete(f);
			} catch (Exception) {
			}
		}
	}
}
