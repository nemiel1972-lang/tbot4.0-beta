using Tbot.Common.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;
using TBot.WebUI.Models;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public sealed class AutoFarmController : Controller {
		private readonly AutoFarmDashboardReader _reader = new();

		[HttpGet]
		public async Task<IActionResult> Index(string instance, bool classic = false) {
			var instances = await GetInstanceAliases();
#if FASTFARM
			var deut = FastFarmController.BuildModel(instances, instance);
			ViewBag.FastFarmAvailable = deut.Available;
			ViewBag.FastFarmCompiled = true;
			ViewBag.FastFarmActive = deut.Active;
			if (deut.Active && !classic)
				return View("~/Views/FastFarm/Index.cshtml", deut);
#endif
			return View(BuildModel(instances, instance));
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Snapshot(string instance) {
			var instances = await GetInstanceAliases();
			return Json(BuildModel(instances, instance));
		}

		private static readonly object CommandLock = new();
		private static readonly HashSet<string> AllowedCommands = new() { "clearBlacklist", "removeBlacklist", "removeTarget", "clearSystems", "resetDb", "attackDefendedNow" };

		[HttpPost]
		public async Task<IActionResult> Command(string instance, string cmd, string? coord) {
			try {
				var instances = await GetInstanceAliases();
				var selected = instances.FirstOrDefault(alias => string.Equals(alias, instance, StringComparison.OrdinalIgnoreCase));
				if (selected == null)
					return Json(new { success = false, error = "Unknown instance." });
				if (!AllowedCommands.Contains(cmd ?? string.Empty))
					return Json(new { success = false, error = "Unknown command." });
				if ((cmd == "removeBlacklist" || cmd == "removeTarget" || cmd == "attackDefendedNow") && !System.Text.RegularExpressions.Regex.IsMatch(coord ?? string.Empty, @"^\d{1,2}:\d{1,3}:\d{1,2}$"))
					return Json(new { success = false, error = "Invalid coordinate." });

				string path = AutoFarmDashboardReader.GetCommandPath(selected);
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				lock (CommandLock) {
					var list = new List<Dictionary<string, string>>();
					if (System.IO.File.Exists(path)) {
						try { list = Newtonsoft.Json.JsonConvert.DeserializeObject<List<Dictionary<string, string>>>(System.IO.File.ReadAllText(path)) ?? list; } catch { }
					}
					var entry = new Dictionary<string, string> { ["cmd"] = cmd! };
					if (!string.IsNullOrEmpty(coord))
						entry["coord"] = coord;
					list.Add(entry);
					AtomicFile.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(list));
				}
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		private AutoFarmDashboardModel BuildModel(IReadOnlyList<string> instances, string instance) {
			var selected = instances.FirstOrDefault(alias => string.Equals(alias, instance, StringComparison.OrdinalIgnoreCase))
				?? instances.First();
			var storage = _reader.Read(selected);

			return new AutoFarmDashboardModel {
				SelectedInstance = selected,
				Instances = instances,
				DatabaseAvailable = storage.DatabaseAvailable,
				CachedSystemCount = storage.CachedSystemCount,
				LastSystemObservedAtUtc = storage.LastSystemObservedAtUtc,
				Reports = storage.Reports,
				Attacks = storage.Attacks,
				Worker = storage.Worker,
				Slots = storage.Slots,
				Scan = storage.Scan,
				Systems = storage.Systems,
				Logs = storage.Logs,
				Errors = storage.Errors,
				Losses = storage.Losses,
				Blacklist = storage.Blacklist,
				PendingCommands = storage.PendingCommands,
				Deuterium = storage.Deuterium,
				Galaxy = GalaxyScanReader.Read(selected)
			};
		}

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
