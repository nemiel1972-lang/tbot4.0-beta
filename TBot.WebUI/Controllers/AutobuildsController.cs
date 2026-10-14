using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;
using TBot.WebUI.Models;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	// Seite "Autobuilds": zeigt pro Celestial, was die Brain-Worker bauen wollen / gebaut haben.
	public sealed class AutobuildsController : Controller {
		private readonly AutobuildsReader _reader = new();

		[HttpGet]
		public async Task<IActionResult> Index(string? instance) {
			var instances = await GetInstanceAliases();
			return View(BuildModel(instances, instance));
		}

		// JSON fuer das automatische Nachladen im Browser.
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Snapshot(string? instance) {
			var instances = await GetInstanceAliases();
			return Json(BuildModel(instances, instance));
		}

		private AutobuildsModel BuildModel(IReadOnlyList<string> instances, string? instance) {
			var selected = instances.FirstOrDefault(a => string.Equals(a, instance, StringComparison.OrdinalIgnoreCase))
				?? instances.First();
			var model = _reader.Read(selected);
			model.SelectedInstance = selected;
			model.Instances = instances;
			return model;
		}

		// Gleiche Logik wie beim AutoFarm-Controller: Instanz-Aliase aus der globalen settings.json.
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
					// Seite bleibt benutzbar, auch wenn settings.json kurz nicht lesbar ist.
				}
			}
			if (!aliases.Any())
				aliases.Add("MAIN");
			return aliases;
		}
	}
}
