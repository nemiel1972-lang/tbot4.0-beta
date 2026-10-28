using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using TBot.Common.Logging;

namespace TBot.WebUI.Controllers {
	public sealed class BelohnungenController : Controller {
		private readonly ILoggerService<BelohnungenController> _logger;

		public BelohnungenController(ILoggerService<BelohnungenController> logger) {
			_logger = logger;
		}

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
				string path = Path.Combine(dataDir, $"belohnungen_{safe}.json");
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
				string safeC = string.Concat(selected.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string cpath = Path.Combine(AppContext.BaseDirectory, "data", $"belohnungen_wahl_{safeC}.json");
				if (System.IO.File.Exists(cpath)) {
					using var fs = new FileStream(cpath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using var sr = new StreamReader(fs);
					result["choices"] = JObject.Parse(sr.ReadToEnd());
				}
			} catch {
			}
			try {
				string safeM = string.Concat(selected.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string mpath = Path.Combine(AppContext.BaseDirectory, "data", $"belohnungen_einstellung_{safeM}.json");
				result["mode"] = "off";
				result["tasks"] = "off";
				result["telegram"] = "on";
				if (System.IO.File.Exists(mpath)) {
					using var fs = new FileStream(mpath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using var sr = new StreamReader(fs);
					var mj = JObject.Parse(sr.ReadToEnd());
					result["mode"] = (string?) mj["mode"] ?? "off";
					result["tasks"] = (string?) mj["tasks"] ?? "off";
					result["telegram"] = (string?) mj["telegram"] == "off" ? "off" : "on";
				}
			} catch {
			}
			return Content(result.ToString(Newtonsoft.Json.Formatting.None), "application/json");
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult Log() {
			var lines = new List<string>();
			try {
				foreach (var day in new[] { DateTime.Now.Date, DateTime.Now.Date.AddDays(-1) }) {
					var found = new List<string>();
					string prefix = $"TBot{day:yyyyMMdd}";
					var files = new List<string>();
					string first = Path.Combine(SettingsService.LogsPath, prefix + ".log");
					if (System.IO.File.Exists(first)) files.Add(first);
					files.AddRange(Directory.GetFiles(SettingsService.LogsPath, prefix + "_*.log").OrderBy(f => f));
					foreach (var f in files) {
						using var fs = System.IO.File.Open(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						using var sr = new StreamReader(fs);
						string line;
						while ((line = sr.ReadLine()) != null)
							if (line.Contains("Rewards", StringComparison.Ordinal))
								found.Add(line.Length > 600 ? line.Substring(0, 600) : line);
					}
					found.Reverse();
					lines.AddRange(found);
					if (lines.Count >= 50) break;
				}
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
			return Json(new { success = true, lines = lines.Take(300).ToList() });
		}

		[HttpPost]
		public async Task<IActionResult> SaveMode(string? instance, string? mode, string? tasks, string? telegram) {
			try {
				mode = (mode ?? "").ToLowerInvariant();
				tasks = (tasks ?? "").ToLowerInvariant();
				telegram = (telegram ?? "").ToLowerInvariant();
				if (telegram != "" && telegram != "on" && telegram != "off")
					return Json(new { success = false, error = "Invalid mode." });
				foreach (var v in new[] { mode, tasks })
					if (v != "" && v != "off" && v != "dry" && v != "on")
						return Json(new { success = false, error = "Invalid mode." });
				var instances = await GetInstanceAliases();
				var alias = Select(instances, instance);
				string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				string spath = Path.Combine(dir, $"belohnungen_einstellung_{safe}.json");
				var cur = new JObject();
				try { if (System.IO.File.Exists(spath)) cur = JObject.Parse(System.IO.File.ReadAllText(spath)); } catch { }
				if (mode != "") cur["mode"] = mode;
				if (tasks != "") cur["tasks"] = tasks;
				if (telegram != "") cur["telegram"] = telegram;
				Tbot.Common.IO.AtomicFile.WriteAllText(spath, cur.ToString(Newtonsoft.Json.Formatting.Indented));
				_logger.WriteLog(LogLevel.Information, LogSender.Tbot, $"[WebUI] Rewards settings saved for {alias}: claim ranks = {(string?) cur["mode"] ?? "off"}, automatic tasks = {(string?) cur["tasks"] ?? "off"}, Telegram messages = {(string?) cur["telegram"] ?? "on"}. The bot applies it within a minute.");
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SaveChoice(string? instance, int tier, int id, string? name) {
			try {
				if (tier < 1 || tier > 20 || id < 0)
					return Json(new { success = false, error = "Invalid choice." });
				var instances = await GetInstanceAliases();
				var alias = Select(instances, instance);
				string safe = string.Concat(alias.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				string file = Path.Combine(dir, $"belohnungen_wahl_{safe}.json");
				var all = new JObject();
				if (System.IO.File.Exists(file)) {
					try { all = JObject.Parse(System.IO.File.ReadAllText(file)); } catch { }
				}
				if (id == 0)
					all.Remove(tier.ToString());
				else
					all[tier.ToString()] = new JObject { ["id"] = id, ["name"] = (name ?? "").Length > 200 ? (name ?? "").Substring(0, 200) : name ?? "" };
				Tbot.Common.IO.AtomicFile.WriteAllText(file, all.ToString(Newtonsoft.Json.Formatting.Indented));
				_logger.WriteLog(LogLevel.Information, LogSender.Tbot, id == 0
					? $"[WebUI] Rewards choice for rank {tier} removed ({alias})."
					: $"[WebUI] Rewards choice for rank {tier} saved ({alias}): {(name ?? "").Replace("\r", " ").Replace("\n", " ")}.");
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
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
