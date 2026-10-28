using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace TBot.WebUI.Services {
	public static class HideDataDefault {
		private static bool _wert;
		private static DateTime _stand = DateTime.MinValue;

		public static bool Get() {
			if (DateTime.UtcNow - _stand < TimeSpan.FromSeconds(15))
				return _wert;
			bool v = false;
			try {
				string global = SettingsService.GlobalSettingsPath;
				string pfad = global;
				var g = JObject.Parse(File.ReadAllText(global));
				if (g["Instances"] is JArray inst) {
					string? datei = inst.OfType<JObject>().Select(i => i["Settings"]?.ToString()).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
					if (datei != null)
						pfad = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, datei));
				}
				var obj = SettingsService.ReadResolvedObject(pfad);
				v = obj["General"]?["AnonymizeLogs"]?.Type == JTokenType.Boolean && (bool) obj["General"]!["AnonymizeLogs"]!;
			} catch {
			}
			_wert = v;
			_stand = DateTime.UtcNow;
			return v;
		}
	}
}
