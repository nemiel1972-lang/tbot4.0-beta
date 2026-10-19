using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tbot.Common.Settings {
	public static class UiLanguage {
		public static string LangDir() {
			try {
				string path = SettingsService.GlobalSettingsPath;
				string? dir = string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(Path.GetFullPath(path));
				if (!string.IsNullOrEmpty(dir) && Directory.Exists(Path.Combine(dir, "lang")))
					return Path.Combine(dir, "lang");
			} catch (Exception) { }
			return Path.Combine(AppContext.BaseDirectory, "lang");
		}

		private static bool ValidCode(string l) {
			if (l.Length < 2 || l.Length > 8) return false;
			foreach (char c in l)
				if (!((c >= 'a' && c <= 'z') || c == '-')) return false;
			return true;
		}

		public static string FilePath(string l) => Path.Combine(LangDir(), l + ".json");

		public static List<(string Code, string Name)> Available() {
			var list = new List<(string, string)> { ("en", "English") };
			try {
				string dir = LangDir();
				if (Directory.Exists(dir)) {
					foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) {
						string code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
						if (code == "en" || !ValidCode(code)) continue;
						string name = code;
						try { name = Parse(File.ReadAllText(f))["name"]?.ToString() ?? code; } catch (Exception) { }
						list.Add((code, name));
					}
				}
			} catch (Exception) { }
			return list;
		}

		public static string Normalize(string? lang) {
			string l = (lang ?? "").Trim().ToLowerInvariant();
			if (l == "en" || !ValidCode(l)) return "en";
			return File.Exists(FilePath(l)) ? l : "en";
		}

		public static JObject? LoadFile(string lang) {
			try {
				string l = Normalize(lang);
				if (l == "en") return null;
				return Parse(File.ReadAllText(FilePath(l)));
			} catch (Exception) {
				return null;
			}
		}

		public static string Read() {
			try {
				string path = SettingsService.GlobalSettingsPath;
				if (string.IsNullOrEmpty(path) || !File.Exists(path))
					return "en";
				var obj = Parse(File.ReadAllText(path));
				return Normalize(obj["WebUI"]?["Language"]?.ToString());
			} catch (Exception) {
				return "en";
			}
		}

		public static async Task<bool> Write(string? lang) {
			string l = Normalize(lang);
			string path = SettingsService.GlobalSettingsPath;
			if (string.IsNullOrEmpty(path) || !File.Exists(path))
				return false;
			var obj = Parse(File.ReadAllText(path));
			if (obj["WebUI"] is not JObject webUi) {
				webUi = new JObject();
				obj["WebUI"] = webUi;
			}
			if (string.Equals(webUi["Language"]?.ToString(), l, StringComparison.Ordinal))
				return true;
			webUi["Language"] = l;
			try { File.Copy(path, path + ".bak", true); } catch (Exception) { }
			await SettingsService.WriteSettings(path, obj.ToString(Formatting.Indented));
			return true;
		}

		private static JObject Parse(string json) {
			using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
			return JObject.Load(reader);
		}
	}
}
