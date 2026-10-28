using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace TBot.WebUI.Controllers {
	public sealed class LogistikController : Controller {
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
				string safe = Safe(selected);
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				string path = Path.Combine(dir, $"logistik_{safe}.json");
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
				result["pending"] = System.IO.File.Exists(Path.Combine(dir, $"logistik_anfrage_{safe}.txt"));
				result["settings"] = ReadSettings(dir, safe);
				result["autoOn"] = System.IO.File.Exists(Path.Combine(AppContext.BaseDirectory, "logistik.on"));
			} catch (Exception e) {
				result["error"] = e.Message;
			}
			return Content(result.ToString(Newtonsoft.Json.Formatting.None), "application/json");
		}

		[HttpPost]
		public async Task<IActionResult> Recalc(string? instance) {
			try {
				var instances = await GetInstanceAliases();
				var alias = Select(instances, instance);
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				Tbot.Common.IO.AtomicFile.WriteAllText(Path.Combine(dir, $"logistik_anfrage_{Safe(alias)}.txt"), DateTime.UtcNow.ToString("o"));
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SaveSettings(string? instance, double bufferPercent, double horizonHours, int reserveMinutes) {
			try {
				if (bufferPercent < 0 || bufferPercent > 500 || horizonHours < 0 || horizonHours > 240 || reserveMinutes < 0 || reserveMinutes > 600)
					return Json(new { success = false, error = "Value out of range." });
				var instances = await GetInstanceAliases();
				var alias = Select(instances, instance);
				string safe = Safe(alias);
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				var s = new JObject { ["BufferPercent"] = bufferPercent, ["HorizonHours"] = horizonHours, ["ReserveMinutes"] = reserveMinutes };
				Tbot.Common.IO.AtomicFile.WriteAllText(Path.Combine(dir, $"logistik_einstellung_{safe}.json"), s.ToString(Newtonsoft.Json.Formatting.Indented));
				Tbot.Common.IO.AtomicFile.WriteAllText(Path.Combine(dir, $"logistik_anfrage_{safe}.txt"), DateTime.UtcNow.ToString("o"));
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		private static JObject ReadSettings(string dir, string safe) {
			var s = new JObject { ["BufferPercent"] = 20, ["HorizonHours"] = 24, ["ReserveMinutes"] = 20 };
			try {
				string p = Path.Combine(dir, $"logistik_einstellung_{safe}.json");
				if (System.IO.File.Exists(p)) {
					var j = JObject.Parse(System.IO.File.ReadAllText(p));
					foreach (var k in new[] { "BufferPercent", "HorizonHours", "ReserveMinutes" })
						if (j[k] != null) s[k] = j[k];
				}
			} catch {
			}
			return s;
		}

		private static string Safe(string alias) => string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

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
