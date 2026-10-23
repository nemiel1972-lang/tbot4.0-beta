using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tbot.Common.Settings {
	public class WhatsNewEntry {
		public string Version { get; set; } = "";
		public string Title { get; set; } = "";
		public List<string> Lines { get; set; } = new();

		public string AsText() => Title + "\n" + string.Join("\n", Lines.Select(l => "• " + l));

		public string AsShortText(string lang) => Title + "\n" + (lang == "de" ? "Alle Neuerungen: Weboberfläche → Seite \"What's new\" (/WhatsNew)." : "All changes: web UI → \"What's new\" page (/WhatsNew).");
	}

	public static class WhatsNew {
		private static string FilePath() {
			string p = Path.Combine(AppContext.BaseDirectory, "whatsnew.json");
			return p;
		}

		private static string MarkerPath(string kind) => Path.Combine(AppContext.BaseDirectory, "data", $"whatsnew_{kind}.txt");

		public static WhatsNewEntry? Read(string lang) {
			try {
				string path = FilePath();
				if (!File.Exists(path))
					return null;
				var o = JObject.Parse(File.ReadAllText(path));
				string version = o["version"]?.ToString() ?? "";
				if (version.Length == 0)
					return null;
				string l = string.IsNullOrEmpty(lang) ? "en" : lang;
				string title = o["title"]?[l]?.ToString() ?? o["title"]?["en"]?.ToString() ?? $"What's new in {version}";
				var arr = o["lines"]?[l] as JArray ?? o["lines"]?["en"] as JArray;
				var lines = arr?.Select(x => x.ToString()).Where(x => x.Length > 0).ToList() ?? new List<string>();
				if (lines.Count == 0)
					return null;
				return new WhatsNewEntry { Version = version, Title = title, Lines = lines };
			} catch (Exception) {
				return null;
			}
		}

		private static string? ReadMarker(string kind) {
			try {
				string p = MarkerPath(kind);
				return File.Exists(p) ? File.ReadAllText(p).Trim() : null;
			} catch (Exception) {
				return null;
			}
		}

		private static void WriteMarker(string kind, string version) {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath(kind))!);
				File.WriteAllText(MarkerPath(kind), version);
			} catch (Exception) {
			}
		}

		public static WhatsNewEntry? Pending(string kind, string lang) {
			var e = Read(lang);
			if (e == null || string.Equals(ReadMarker(kind), e.Version, StringComparison.Ordinal))
				return null;
			return e;
		}

		public static void MarkShown(string kind, string version) => WriteMarker(kind, version);

		private static readonly object _lock = new();
		public static WhatsNewEntry? ClaimBotNotice(string lang) {
			lock (_lock) {
				var e = Pending("bot", lang);
				if (e != null)
					MarkShown("bot", e.Version);
				return e;
			}
		}
	}
}
