using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace TBot.WebUI.Services {
	public static class LegacyConfigCheck {
		private static DateTime _checkedUtc = DateTime.MinValue;
		private static bool _legacy;
		private static readonly object _lock = new();

		public static bool IsLegacy() {
			lock (_lock) {
				if ((DateTime.UtcNow - _checkedUtc).TotalSeconds < 10)
					return _legacy;
				_checkedUtc = DateTime.UtcNow;
				_legacy = false;
				try {
					foreach (string path in InstanceFiles()) {
						var cfg = SettingsService.ReadResolvedObject(path);
						if (cfg["ConfigVersion"]?.Type == JTokenType.Integer && (int) cfg["ConfigVersion"] >= 4)
							continue;
						if (cfg["Brain"]?["LifeformAutoMine"] is JObject lf &&
							(lf["MaxT2Building"] != null || lf["MaxT3Building"] != null || lf["MaxBuilding6"] != null || lf["MaxBuilding12"] != null)) {
							_legacy = true;
							break;
						}
					}
				} catch {
				}
				return _legacy;
			}
		}

		private static IEnumerable<string> InstanceFiles() {
			string globalPath = SettingsService.GlobalSettingsPath;
			if (string.IsNullOrWhiteSpace(globalPath) || !File.Exists(globalPath))
				globalPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
			string baseDir = Path.GetDirectoryName(globalPath) ?? AppContext.BaseDirectory;
			var result = new List<string>();
			if (File.Exists(globalPath)) {
				var global = JObject.Parse(File.ReadAllText(globalPath));
				foreach (var instance in global["Instances"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) {
					string rel = instance.Value<string>("Settings");
					if (string.IsNullOrWhiteSpace(rel))
						continue;
					string full = Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(baseDir, rel));
					if (File.Exists(full))
						result.Add(full);
				}
			}
			if (result.Count == 0) {
				string def = Path.Combine(AppContext.BaseDirectory, "instance_settings.json");
				if (File.Exists(def))
					result.Add(def);
			}
			return result;
		}
	}
}
