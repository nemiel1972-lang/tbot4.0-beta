using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace TBot.WebUI.Controllers {
	public sealed class MinenplanController : Controller {
		[HttpGet]
		public async Task<IActionResult> Index(string? instance) {
			var instances = await GetInstanceAliases();
			ViewBag.Instances = instances;
			ViewBag.Selected = Select(instances, instance);
			return View();
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Snapshot(string? instance) {
			var instances = await GetInstanceAliases();
			var selected = Select(instances, instance);
			var result = new JObject { ["instance"] = selected, ["available"] = false };
			try {
				string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
				string safe = string.Concat(selected.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string path = Path.Combine(dataDir, $"minenplan_{safe}.json");
				if (System.IO.File.Exists(path)) {
					JObject? data = null;
					for (var i = 0; i < 3 && data == null; i++) {
						try {
							using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
							using var sr = new StreamReader(fs);
							data = JObject.Parse(sr.ReadToEnd());
						} catch {
							await Task.Delay(80);
						}
					}
					if (data != null) {
						result["available"] = true;
						result["data"] = data;
					} else {
						result["error"] = "File could not be read (it may be being written right now).";
					}
				}
			} catch (Exception e) {
				result["error"] = e.Message;
			}
			try {
				string safe2 = string.Concat(selected.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string bpath = Path.Combine(AppContext.BaseDirectory, "data", $"booster_status_{safe2}.json");
				if (System.IO.File.Exists(bpath)) {
					using var fs = new FileStream(bpath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using var sr = new StreamReader(fs);
					result["boosters"] = JObject.Parse(sr.ReadToEnd());
				}
			} catch {
			}
			return Content(result.ToString(Newtonsoft.Json.Formatting.None), "application/json");
		}

		private static string Select(IReadOnlyList<string> instances, string? instance) =>
			instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase)) ?? instances.First();

		private static async Task<List<string>> GetInstanceAliases() {
			var aliases = new List<string>();
			var settingsPath = SettingsService.GlobalSettingsPath;
			if (!string.IsNullOrWhiteSpace(settingsPath) && System.IO.File.Exists(settingsPath)) {
				try {
					var settings = await SettingsService.GetSettings(settingsPath);
					if (SettingsService.IsSettingSet(settings, "Instances")) {
						foreach (var instance in settings.Instances) {
							var alias = (string) instance.Alias;
							if (!string.IsNullOrWhiteSpace(alias) && !aliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
								aliases.Add(alias);
						}
					}
				} catch {
				}
			}
			if (!aliases.Any())
				aliases.Add("MAIN");
			return aliases;
		}
	}
}
