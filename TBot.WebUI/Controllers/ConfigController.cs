using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using TBot.WebUI.Models;

namespace TBot.WebUI.Controllers {
	// Formular-Ansicht der Instanz-Config, Block fuer Block. Nur Expeditions hat eine eigene Seite; alle anderen Bloecke sind
	// Platzhalter und werden ueber settings.json -> WebUI.FormPages freigeschaltet (dann mit automatisch erzeugtem Formular).
	// Zugangsdaten (secrets/) werden hier nie gelesen oder geschrieben.
	public sealed class ConfigController : Controller {
		private const string CustomPage = "Expeditions";
		private static readonly HashSet<string> NeverForm = new(StringComparer.OrdinalIgnoreCase) { "Credentials", "DiscordWebhook" };

		private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

		private static JObject ParseObject(string json) {
			using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
			return JObject.Load(reader);
		}

		private static bool IsSecretRef(string reference) {
			return reference.Replace('\\', '/').Trim().StartsWith(SettingsService.SecretsDirectory + "/", StringComparison.OrdinalIgnoreCase);
		}

		// Instanz-Aliase und -Dateien aus der globalen settings.json (ohne Instances: die Datei selbst)
		private static List<(string Alias, string Path)> GetInstances() {
			var result = new List<(string, string)>();
			var globalPath = SettingsService.GlobalSettingsPath;
			try {
				var global = ParseObject(System.IO.File.ReadAllText(globalPath));
				if (global["Instances"] is JArray instances) {
					foreach (var inst in instances.OfType<JObject>()) {
						string? file = inst["Settings"]?.ToString();
						if (string.IsNullOrWhiteSpace(file))
							continue;
						string alias = inst["Alias"]?.ToString() ?? file;
						result.Add((alias, System.IO.Path.GetFullPath(System.IO.Path.Combine(BaseDir, file))));
					}
				}
			} catch { }
			if (result.Count == 0)
				result.Add(("MAIN", globalPath));
			return result;
		}

		// settings.json: "WebUI": { "FormPages": ["Expeditions", "AutoFarm"] } oder "*" fuer alle. Standard: nur Expeditions.
		private static HashSet<string> GetEnabledPages() {
			var pages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CustomPage };
			try {
				var global = ParseObject(System.IO.File.ReadAllText(SettingsService.GlobalSettingsPath));
				var token = global["WebUI"]?["FormPages"];
				if (token is JArray arr) {
					pages = new HashSet<string>(arr.Select(t => t.ToString()), StringComparer.OrdinalIgnoreCase);
					pages.Add(CustomPage);
				} else if (token != null && token.Type == JTokenType.String) {
					pages.Add(token.ToString());
				}
			} catch { }
			return pages;
		}

		private static string ModeFor(string block, HashSet<string> enabled) {
			if (NeverForm.Contains(block))
				return "secret";
			if (string.Equals(block, CustomPage, StringComparison.OrdinalIgnoreCase))
				return "custom";
			if (enabled.Contains("*") || enabled.Contains(block))
				return "generic";
			return "placeholder";
		}

		[HttpGet]
		public IActionResult Index(string? instance, string? block) {
			var model = new ConfigPageModel();
			try {
				var instances = GetInstances();
				model.Instances = instances.Select(i => i.Alias).ToList();
				var chosen = instances.FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				if (chosen.Alias == null)
					chosen = instances[0];
				model.Instance = chosen.Alias;

				var main = ParseObject(System.IO.File.ReadAllText(chosen.Path));
				var enabled = GetEnabledPages();
				foreach (var prop in main.Properties()) {
					if (prop.Value is not JObject obj)
						continue;
					model.Blocks.Add(new ConfigBlockInfo {
						Name = prop.Name,
						Active = obj["Active"]?.Type == JTokenType.Boolean ? (bool?) obj["Active"] : null,
						Mode = ModeFor(prop.Name, enabled)
					});
				}
				var selected = model.Blocks.FirstOrDefault(b => string.Equals(b.Name, block, StringComparison.OrdinalIgnoreCase))
					?? model.Blocks.FirstOrDefault(b => b.Name == CustomPage)
					?? model.Blocks.FirstOrDefault();
				if (selected != null) {
					model.Selected = selected.Name;
					model.SelectedMode = selected.Mode;
				}
			} catch (Exception e) {
				model.Error = e.Message;
			}
			return View(model);
		}

		// Liefert den Block ohne Zugangsdaten: Inhalte der (nicht geheimen) Load-Dateien, darueber die Schluessel direkt im Block.
		private static JObject ReadBlock(JObject main, string block, string baseDir, out string? targetFile, out int secretRefs) {
			targetFile = null;
			secretRefs = 0;
			if (main[block] is not JObject blockMain)
				throw new Exception($"Block '{block}' not found.");

			var data = new JObject();
			var refs = new List<string>();
			var load = blockMain["Load"];
			if (load is JValue lv && lv.Type == JTokenType.String)
				refs.Add(lv.ToString());
			else if (load is JArray la)
				refs.AddRange(la.Select(t => t.ToString()));

			var plain = new List<string>();
			foreach (var r in refs) {
				if (IsSecretRef(r)) {
					secretRefs++;
					continue;
				}
				plain.Add(r);
			}
			if (plain.Count > 1)
				throw new Exception($"Block '{block}' loads several files ({string.Join(", ", plain)}) - please use the JSON editor under Settings.");

			if (plain.Count == 1) {
				string path = SettingsService.ResolveLoadPath(baseDir, plain[0]);
				if (!System.IO.File.Exists(path))
					throw new Exception($"File for '{block}' not found: {plain[0]}");
				targetFile = path;
				data = ParseObject(System.IO.File.ReadAllText(path));
			}
			foreach (var p in blockMain.Properties()) {
				if (p.Name == "Load")
					continue;
				data[p.Name] = p.Value.DeepClone();
			}
			return data;
		}

		// Momentaufnahme der Lebensform-Boni fuer Expeditionen (vom ExpeditionsWorker geschrieben)
		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult LfSnapshot(string? instance) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string alias = chosen.Alias ?? GetInstances()[0].Alias;
				string file = System.IO.Path.Combine(AppContext.BaseDirectory, "data", $"lf_expedition_{System.IO.Path.GetFileName(alias)}.json");
				if (!System.IO.File.Exists(file))
					return Json(new { success = false, error = "No data yet - the Expeditions worker writes it on its next run." });
				return Content("{\"success\":true,\"data\":" + System.IO.File.ReadAllText(file) + "}", "application/json");
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetBlock(string? instance, string block) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = chosen.Alias == null ? GetInstances()[0].Path : chosen.Path;
				if (NeverForm.Contains(block))
					return Json(new { success = false, error = "This block contains credentials - use the JSON editor under Settings." });
				var main = ParseObject(System.IO.File.ReadAllText(mainPath));
				var data = ReadBlock(main, block, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!, out var targetFile, out var secretRefs);
				return Json(new {
					success = true,
					data = data.ToString(Formatting.None),
					file = targetFile == null ? "(inside the instance file)" : System.IO.Path.GetRelativePath(BaseDir, targetFile).Replace('\\', '/'),
					secretRefs
				});
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SaveBlock(string? instance, string block, string content) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = chosen.Alias == null ? GetInstances()[0].Path : chosen.Path;
				if (NeverForm.Contains(block))
					return Json(new { success = false, error = "This block contains credentials - use the JSON editor under Settings." });
				string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!;

				var incoming = ParseObject(content);
				string mainText = System.IO.File.ReadAllText(mainPath);
				var main = ParseObject(mainText);
				// Nur lesen, um Fehler (mehrere Dateien, fehlende Datei) vor dem Schreiben zu melden
				ReadBlock(main, block, baseDir, out var targetFile, out _);
				var blockMain = (JObject) main[block]!;

				if (blockMain["Load"] == null) {
					main[block] = incoming;
				} else {
					// Schluessel direkt neben Load gewinnen gegenueber der Datei: dort aktualisieren, nicht in die Datei schreiben
					var inline = blockMain.Properties().Where(p => p.Name != "Load").Select(p => p.Name).ToList();
					if (incoming["Active"] != null && !inline.Contains("Active"))
						inline.Add("Active");
					foreach (var key in inline) {
						if (incoming[key] != null)
							blockMain[key] = incoming[key]!.DeepClone();
					}
					if (targetFile != null) {
						var fileData = new JObject();
						foreach (var p in incoming.Properties()) {
							if (!inline.Contains(p.Name))
								fileData[p.Name] = p.Value.DeepClone();
						}
						string newText = fileData.ToString(Formatting.Indented);
						string oldText = System.IO.File.ReadAllText(targetFile);
						if (!JToken.DeepEquals(ParseObject(oldText), fileData)) {
							try { System.IO.File.Copy(targetFile, targetFile + ".bak", true); } catch { }
							await SettingsService.WriteSettings(targetFile, newText);
						}
					} else {
						// Load ohne (nicht geheime) Datei: der Rest steht im Block selbst
						foreach (var p in incoming.Properties())
							blockMain[p.Name] = p.Value.DeepClone();
					}
				}

				string mainNew = main.ToString(Formatting.Indented);
				if (!JToken.DeepEquals(ParseObject(mainText), ParseObject(mainNew))) {
					try { System.IO.File.Copy(mainPath, mainPath + ".bak", true); } catch { }
					await SettingsService.WriteSettings(mainPath, mainNew);
				}
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}
	}
}
