using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TBot.Common.Logging {
	public static class CollectLogs {
		private const long MaxBytes = 100L * 1024 * 1024;
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

		private struct Zeile { public DateTime Zeit; public string Kennung; public string Text; public bool Roh; }
		private static readonly System.Collections.Concurrent.BlockingCollection<Zeile> _queue = new(new System.Collections.Concurrent.ConcurrentQueue<Zeile>(), 50000);
		private static Thread _schreiber;
		private static long _verworfen;
		private static int _zaehler;

		private static string Rotiere(string alt) {
			string neu;
			try {
				string baseName = Path.GetFileNameWithoutExtension(alt);
				int teil = 2;
				var m = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*)-part(\d+)$");
				if (m.Success) { baseName = m.Groups[1].Value; teil = int.Parse(m.Groups[2].Value) + 1; }
				neu = Path.Combine(Folder, $"{baseName}-part{teil}.log");
				lock (_lock) {
					_datei = neu;
					if (File.Exists(StartMarker))
						File.WriteAllLines(StartMarker, new[] { _start.ToString("o"), Path.GetFileName(neu) });
				}
				string zu = alt;
				Task.Run(() => {
					try {
						string zip = Path.ChangeExtension(zu, ".zip");
						if (File.Exists(zip)) File.Delete(zip);
						using (var za = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
							za.CreateEntryFromFile(zu, Path.GetFileName(zu), System.IO.Compression.CompressionLevel.Optimal);
						File.Delete(zu);
					} catch (Exception) { }
				});
				return neu;
			} catch (Exception) {
				return alt;
			}
		}

		private static DateTime _letzteAufraeumung = DateTime.MinValue;
		private static void RaeumeAuf() {
			try {
				if ((DateTime.Now - _letzteAufraeumung).TotalHours < 24)
					return;
				_letzteAufraeumung = DateTime.Now;
				if (!Directory.Exists(Folder))
					return;
				string aktuell;
				lock (_lock) { aktuell = _datei; }
				foreach (string muster in new[] { "TBot-collect-*.log", "TBot-collect-*.zip" })
					foreach (string f in Directory.GetFiles(Folder, muster))
						if (!string.Equals(f, aktuell, StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
							File.Delete(f);
			} catch (Exception) {
			}
		}

		private static void StelleSchreiberSicher() {
			if (_schreiber != null)
				return;
			lock (_lock) {
				if (_schreiber != null)
					return;
				_schreiber = new Thread(SchreiberSchleife) { IsBackground = true, Name = "CollectLogsWriter" };
				_schreiber.Start();
			}
		}

		private static void Einreihen(Zeile z) {
			if (!_queue.TryAdd(z))
				Interlocked.Increment(ref _verworfen);
		}

		private static void SchreiberSchleife() {
			var sb = new System.Text.StringBuilder();
			foreach (var erste in _queue.GetConsumingEnumerable()) {
				RaeumeAuf();
				try {
					string datei; bool anonym;
					lock (_lock) { datei = _datei; anonym = _anonym; }
					if (datei == null)
						continue;
					sb.Clear();
					var z = erste;
					int n = 0;
					do {
						if (++_zaehler % 200 == 1 && File.Exists(datei) && new FileInfo(datei).Length > MaxBytes) {
							if (sb.Length > 0) { File.AppendAllText(datei, sb.ToString()); sb.Clear(); }
							datei = Rotiere(datei);
						}
						{
							string text = (!z.Roh && anonym) ? LogAnonymizer.ApplyForSave(z.Text) : z.Text;
							sb.Append(z.Zeit.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(' ');
							if (!string.IsNullOrEmpty(z.Kennung)) sb.Append(z.Kennung).Append(' ');
							sb.Append(text).Append(Environment.NewLine);
						}
					} while (++n < 500 && _queue.TryTake(out z));
					long weg = Interlocked.Exchange(ref _verworfen, 0);
					if (weg > 0)
						sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(" --- ").Append(weg).Append(" collect lines dropped (writer too slow) ---").Append(Environment.NewLine);
					if (sb.Length > 0)
						File.AppendAllText(datei, sb.ToString());
				} catch (Exception) {
				}
			}
		}

		private static void Append(string kennung, string message) {
			try {
				bool ende = false;
				lock (_lock) {
					if (!_aktiv)
						return;
					if (DateTime.Now >= _ende) {
						if (_endeGeschrieben)
							return;
						_endeGeschrieben = true;
						ende = true;
					}
				}
				StelleSchreiberSicher();
				if (ende) {
					Einreihen(new Zeile { Zeit = DateTime.Now, Kennung = "---", Text = "Collection ended. Switch Experimental.CollectLogs.Active off and on to collect again. ---", Roh = true });
					return;
				}
				Einreihen(new Zeile { Zeit = DateTime.Now, Kennung = kennung, Text = message });
			} catch (Exception) {
			}
		}

		public static void Write(LogLevel level, string sender, string message) => Append($"{level} [{sender}]", message);

		public static void WriteExtra(string thema, string message) => Append($"Extra [{thema}]", message);
	}
}
