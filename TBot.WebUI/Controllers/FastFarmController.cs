#if FASTFARM
using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;
using TBot.WebUI.Models;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public sealed class FastFarmController : Controller {
		[HttpGet]
		public async Task<IActionResult> Index(string? instance) {
			var instances = await GetInstanceAliases();
			return View(BuildModel(instances, instance));
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Snapshot(string? instance) {
			var instances = await GetInstanceAliases();
			return Json(BuildModel(instances, instance));
		}

		[HttpPost]
		public async Task<IActionResult> DeleteTargets(string? instance, string? keys) {
			try {
				var instances = await GetInstanceAliases();
				var alias = instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase)) ?? instances.First();
				var wanted = (keys ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.Where(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"^\d+:\d+:\d+$")).Distinct().ToList();
				if (wanted.Count == 0)
					return Json(new { success = false, error = "No valid target." });
				string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
				System.IO.Directory.CreateDirectory(dir);
				string file = System.IO.Path.Combine(dir, $"fastfarm_{System.IO.Path.GetFileName(alias)}_delete.json");
				var all = new List<string>();
				if (System.IO.File.Exists(file)) {
					try { all = Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(System.IO.File.ReadAllText(file)) ?? new List<string>(); } catch { }
				}
				all.AddRange(wanted);
				Tbot.Common.IO.AtomicFile.WriteAllText(file, Newtonsoft.Json.JsonConvert.SerializeObject(all.Distinct().ToList()));
				return Json(new { success = true, count = wanted.Count });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SetSort(string? instance, string? key, string? dir) {
			try {
				var instances = await GetInstanceAliases();
				var alias = instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase)) ?? instances.First();
				var allowed = new[] { "value", "metal", "crystal", "deuterium", "target" };
				key = (key ?? "value").ToLowerInvariant();
				if (!allowed.Contains(key))
					return Json(new { success = false, error = "Unknown sort key." });
				string d = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";
				string folder = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
				System.IO.Directory.CreateDirectory(folder);
				string file = System.IO.Path.Combine(folder, $"fastfarm_{System.IO.Path.GetFileName(alias)}_sort.json");
				Tbot.Common.IO.AtomicFile.WriteAllText(file, Newtonsoft.Json.JsonConvert.SerializeObject(new { key, dir = d }));
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> GetSort(string? instance) {
			var instances = await GetInstanceAliases();
			var alias = instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase)) ?? instances.First();
			string file = System.IO.Path.Combine(AppContext.BaseDirectory, "data", $"fastfarm_{System.IO.Path.GetFileName(alias)}_sort.json");
			try {
				if (System.IO.File.Exists(file))
					return Content(System.IO.File.ReadAllText(file), "application/json");
			} catch { }
			return Json(new { key = "value", dir = "desc" });
		}

		public static FastFarmDashboardModel BuildModel(IReadOnlyList<string> instances, string? instance) {
			var selected = instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase)) ?? instances.First();
			return FastFarmReader.Read(selected, instances);
		}

		public static async Task<List<string>> GetInstanceAliases() {
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
#endif
