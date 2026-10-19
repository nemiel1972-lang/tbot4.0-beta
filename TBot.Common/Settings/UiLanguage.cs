using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Tbot.Common.Settings {
	public static class UiLanguage {
		public static readonly string[] Supported = { "en", "de" };

		public static string Normalize(string? lang) {
			string l = (lang ?? "").Trim().ToLowerInvariant();
			return Array.IndexOf(Supported, l) >= 0 ? l : "en";
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
