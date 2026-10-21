using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using TBot.WebUI.Models;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public sealed class ConfigController : Controller {
		private const string CustomPage = "Expeditions";
		private const string StartPage = "Start";
		private const string TestingPage = "Testing";
		private const string ExperimentalBlock = "Experimental";

		private static JObject DefaultExperimental() => new JObject {
			["BotPause"] = new JObject {
				["Active"] = true,
				["DurationMinutes"] = 15,
				["RecallMarginMinutes"] = 2,
				["MaxDurationMinutes"] = 120,
				["StopDefender"] = false,
				["AttackMarginMinutes"] = 15
			},
			["DefenderRecheck"] = new JObject {
				["Active"] = false,
				["TimeoutSeconds"] = 4
			},
			["ExtraDebugLog"] = new JObject {
				["Active"] = true,
				["Repatriate"] = true,
				["Ledger"] = true,
				["BotPause"] = true,
				["DefenderRecheck"] = true,
				["FleetsaveSlot"] = true,
				["MinePlanRanking"] = true,
				["AsteroidHunt"] = true,
				["ShadowFleet"] = true,
				["GalaxyCache"] = true
			},
			["GalaxyCache"] = new JObject {
				["Active"] = true,
				["MaxAgeHours"] = 12,
				["RangeSystems"] = 15,
				["DelayMinSeconds"] = 20,
				["DelayMaxSeconds"] = 45,
				["UseInFastFarm"] = true,
				["UseInColonize"] = true,
				["Events"] = true,
				["Telegram"] = true
			},
			["RepatriateLedgerHold"] = new JObject {
				["Ignore"] = false,
				["MinPercent"] = 30
			},
			["FleetsaveSlot"] = new JObject {
				["Active"] = true,
				["Slots"] = 1
			},
			["MinePlanRanking"] = new JObject {
				["Mode"] = "Payback",
				["HorizonDays"] = 90,
				["MaxPaybackDays"] = 200,
				["OnlySensible"] = false,
				["IncludeResearch"] = true,
				["DailyBudgetDeut"] = 0
			},
			["AsteroidHunt"] = new JObject {
				["Active"] = false,
				["ShipType"] = "Pathfinder",
				["ShipCount"] = 1,
				["MaxAttemptsPerDay"] = 0,
				["SearchSystems"] = 10,
				["SaveJson"] = true
			},
			["ShadowFleet"] = new JObject {
				["Active"] = false,
				["Celestial"] = new JObject { ["Galaxy"] = 1, ["System"] = 1, ["Position"] = 1, ["Type"] = "Planet" },
				["Ships"] = new JObject { ["Recycler"] = 0 },
				["SpeedPercent"] = 10,
				["MinBackInMinutes"] = 20,
				["MaxBackInMinutes"] = 60,
				["PauseMinSeconds"] = 30,
				["PauseMaxSeconds"] = 180,
				["DeuteriumFlights"] = 3,
				["WindowFrom"] = "",
				["WindowTo"] = "",
				["DefenderOff"] = false
			}
		};
		private static readonly string[] CustomPages = { "Expeditions", "AutoFarm", "Defender", "GhostSleep", "SleepMode", "Messenger", "General", "Brain", "AutoHarvest", "AutoColonize", "AutoDiscovery", "DebrisSave", "FastFarm" };
		private static readonly HashSet<string> NeverForm = new(StringComparer.OrdinalIgnoreCase) { "Credentials" };
		private static readonly HashSet<string> ProtectedBlocks = new(StringComparer.OrdinalIgnoreCase) { "TelegramMessenger", "DiscordWebhook" };
		private static readonly HashSet<string> ProtectedKeys = new(StringComparer.OrdinalIgnoreCase) { "Url", "WebhookUrl", "API", "ApiKey", "ChatId", "Token", "Password" };

		private static readonly Dictionary<string, string[]> HiddenPaths = new(StringComparer.OrdinalIgnoreCase) {
			["AutoFarm"] = new[] { "AllianceWhitelist.AllianceId" },
			["General"] = new[] { "Proxy.Username", "Proxy.Password", "CaptchaAPIKey", "BasicAuth.Username", "BasicAuth.Password" }
		};

		private static void HideFields(string block, JObject data) {
			if (!HiddenPaths.TryGetValue(block, out var paths))
				return;
			foreach (var path in paths)
				if (data.SelectToken(path)?.Parent is JProperty prop)
					prop.Remove();
		}

		private static void RestoreHidden(string block, JObject existing, JObject incoming) {
			if (!HiddenPaths.TryGetValue(block, out var paths))
				return;
			foreach (var path in paths) {
				if (incoming.SelectToken(path)?.Parent is JProperty prop)
					prop.Remove();
				var old = existing.SelectToken(path);
				if (old == null)
					continue;
				var parts = path.Split('.');
				JObject cursor = incoming;
				for (int i = 0; i < parts.Length - 1; i++) {
					if (cursor[parts[i]] is not JObject next)
						cursor[parts[i]] = next = new JObject();
					cursor = next;
				}
				cursor[parts[^1]] = old.DeepClone();
			}
		}

		private static string MainPathFor(string? scope, string block, string instancePath) {
			if (string.Equals(scope, "global", StringComparison.OrdinalIgnoreCase)) {
				if (!string.Equals(block, "TelegramMessenger", StringComparison.OrdinalIgnoreCase))
					throw new Exception("Only the TelegramMessenger block can be edited in the global settings.");
				return SettingsService.GlobalSettingsPath;
			}
			return instancePath;
		}

		private static string? SecretFileOf(JObject main, string block, string baseDir) {
			if (!ProtectedBlocks.Contains(block) || main[block] is not JObject blockMain)
				return null;
			var load = blockMain["Load"];
			var refs = new List<string>();
			if (load is JValue lv && lv.Type == JTokenType.String)
				refs.Add(lv.ToString());
			else if (load is JArray la)
				refs.AddRange(la.Select(t => t.ToString()));
			var secret = refs.FirstOrDefault(IsSecretRef);
			return secret == null ? null : SettingsService.ResolveLoadPath(baseDir, secret);
		}

		private static void MergeSecrets(string? secretFile, JObject data) {
			if (secretFile == null || !System.IO.File.Exists(secretFile))
				return;
			var secrets = ParseObject(System.IO.File.ReadAllText(secretFile));
			foreach (var p in secrets.Properties().Where(p => ProtectedKeys.Contains(p.Name)))
				data[p.Name] = p.Value.DeepClone();
		}

		private static async Task SplitSecrets(string? secretFile, JObject incoming) {
			if (secretFile == null)
				return;
			var secretPart = new JObject();
			foreach (var key in incoming.Properties().Where(p => ProtectedKeys.Contains(p.Name)).Select(p => p.Name).ToList()) {
				secretPart[key] = incoming[key]!.DeepClone();
				incoming.Remove(key);
			}
			if (secretPart.Count == 0)
				return;
			var existing = System.IO.File.Exists(secretFile) ? ParseObject(System.IO.File.ReadAllText(secretFile)) : new JObject();
			var merged = (JObject) existing.DeepClone();
			foreach (var p in secretPart.Properties())
				merged[p.Name] = p.Value;
			if (!JToken.DeepEquals(existing, merged)) {
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(secretFile)!);
				await SettingsService.WriteSettings(secretFile, merged.ToString(Formatting.Indented));
			}
		}

		private static string BaseDir => AppDomain.CurrentDomain.BaseDirectory;

		private static void StripUndefined(JToken token) {
			if (token is JObject obj) {
				foreach (var prop in obj.Properties().ToList()) {
					if (prop.Value.Type == JTokenType.Undefined)
						prop.Remove();
					else
						StripUndefined(prop.Value);
				}
			} else if (token is JArray arr) {
				foreach (var item in arr.ToList()) {
					if (item.Type == JTokenType.Undefined)
						item.Remove();
					else
						StripUndefined(item);
				}
			}
		}

		private static readonly Lazy<JObject?> DefaultsTemplate = new(() => {
			try {
				foreach (var path in new[] { System.IO.Path.Combine(BaseDir, "config.vorlage.json"), System.IO.Path.Combine(BaseDir, "config", "config.vorlage.json") })
					if (System.IO.File.Exists(path))
						return ParseObject(System.IO.File.ReadAllText(path));
			} catch {
			}
			return null;
		});

		private static List<string> FillDefaults(string block, JObject data) {
			var added = new List<string>();
			if (NeverForm.Contains(block) || ProtectedBlocks.Contains(block) || string.Equals(block, ExperimentalBlock, StringComparison.OrdinalIgnoreCase))
				return added;
			if (DefaultsTemplate.Value?[block] is JObject template)
				FillDefaultsInto(data, template, block, added);
			return added;
		}

		private static void FillDefaultsInto(JObject target, JObject template, string path, List<string> added) {
			foreach (var prop in template.Properties()) {
				if (prop.Name == "Load" || ProtectedKeys.Contains(prop.Name))
					continue;
				string childPath = path + "." + prop.Name;
				var existing = target[prop.Name];
				if (existing is JObject existingObj && prop.Value is JObject templateObj) {
					FillDefaultsInto(existingObj, templateObj, childPath, added);
				} else if (existing == null) {
					if (prop.Value is JObject newObj) {
						if (newObj["Active"] == null)
							continue;
						var copy = (JObject) newObj.DeepClone();
						StripArraysAndSecrets(copy);
						if (copy["Active"] is JValue)
							copy["Active"] = false;
						target[prop.Name] = copy;
						added.Add(childPath);
					} else if (prop.Value is JValue value) {
						target[prop.Name] = prop.Name == "Active" && value.Type == JTokenType.Boolean ? new JValue(false) : value.DeepClone();
						added.Add(childPath);
					}
				}
			}
		}

		private static void StripArraysAndSecrets(JObject obj) {
			foreach (var prop in obj.Properties().ToList()) {
				if (prop.Value is JArray || ProtectedKeys.Contains(prop.Name))
					prop.Remove();
				else if (prop.Value is JObject child)
					StripArraysAndSecrets(child);
			}
		}

		private static JObject ParseObject(string json) {
			using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
			return JObject.Load(reader);
		}

		private static bool IsSecretRef(string reference) {
			return reference.Replace('\\', '/').Trim().StartsWith(SettingsService.SecretsDirectory + "/", StringComparison.OrdinalIgnoreCase);
		}

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

		private static HashSet<string> GetEnabledPages() {
			var pages = new HashSet<string>(CustomPages, StringComparer.OrdinalIgnoreCase);
			try {
				var global = ParseObject(System.IO.File.ReadAllText(SettingsService.GlobalSettingsPath));
				var token = global["WebUI"]?["FormPages"];
				if (token is JArray arr) {
					pages = new HashSet<string>(arr.Select(t => t.ToString()), StringComparer.OrdinalIgnoreCase);
					foreach (var custom in CustomPages)
						pages.Add(custom);
				} else if (token != null && token.Type == JTokenType.String) {
					pages.Add(token.ToString());
				}
			} catch { }
			return pages;
		}

		private static string ModeFor(string block, HashSet<string> enabled) {
			if (NeverForm.Contains(block))
				return "secret";
			if (CustomPages.Contains(block, StringComparer.OrdinalIgnoreCase))
				return "custom";
			if (enabled.Contains("*") || enabled.Contains(block))
				return "generic";
			return "placeholder";
		}

		[HttpGet]
		public IActionResult Index(string? instance, string? block) {
			var model = new ConfigPageModel { Language = UiLanguage.Read() };
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
#if FASTFARM
				var deutOverride = FastFarmReader.GetOverride(model.Instance);
				if (deutOverride.Overrides) {
					var suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "AutoFarm", "Brain", "AutoHarvest", "AutoColonize", "AutoDiscovery", "DebrisSave" };
					if (!deutOverride.KeepExpeditions)
						suppressed.Add("Expeditions");
					foreach (var info in model.Blocks.Where(b => suppressed.Contains(b.Name))) {
						info.Overridden = true;
						info.OverrideTag = info.Name == "AutoFarm" ? "override" : "";
						info.OverrideText = info.Name == "AutoFarm"
							? "AutoFarm override active: FastFarm is on, so AutoFarm is switched off (the menu entry shows the FastFarm overview)."
							: "Switched off while FastFarm is active.";
					}
				}
#endif
				model.Blocks.RemoveAll(b => string.Equals(b.Name, ExperimentalBlock, StringComparison.OrdinalIgnoreCase));
				model.Blocks.Add(new ConfigBlockInfo { Name = TestingPage, Label = "Testing", Mode = "custom" });
				if (string.Equals(block, ExperimentalBlock, StringComparison.OrdinalIgnoreCase))
					block = TestingPage;
					var discordEntry = model.Blocks.FirstOrDefault(b => b.Name == "DiscordWebhook");
					int messengerAt = discordEntry != null ? model.Blocks.IndexOf(discordEntry) : model.Blocks.Count;
					if (discordEntry != null)
						model.Blocks.Remove(discordEntry);
					model.Blocks.Insert(messengerAt, new ConfigBlockInfo { Name = "Messenger", Label = "Messenger (Telegram / Discord)", Mode = "custom" });
					if (string.Equals(block, "DiscordWebhook", StringComparison.OrdinalIgnoreCase) || string.Equals(block, "TelegramMessenger", StringComparison.OrdinalIgnoreCase))
						block = "Messenger";
					var sleepEntry = model.Blocks.FirstOrDefault(b => b.Name == "SleepMode");
					var ghostEntry = model.Blocks.FirstOrDefault(b => b.Name == "GhostSleep");
					if (sleepEntry != null && ghostEntry != null) {
						model.Blocks.Remove(ghostEntry);
						sleepEntry.Label = "Sleep / GhostSleep";
						if (string.Equals(block, "GhostSleep", StringComparison.OrdinalIgnoreCase))
							block = "SleepMode";
					}
					model.Blocks.Insert(0, new ConfigBlockInfo { Name = StartPage, Label = "Start", Mode = "start" });
					var selected = model.Blocks.FirstOrDefault(b => string.Equals(b.Name, block, StringComparison.OrdinalIgnoreCase))
					?? model.Blocks.First();
				if (selected != null) {
					model.Selected = selected.Name;
					model.SelectedMode = selected.Mode;
				}
			} catch (Exception e) {
				model.Error = e.Message;
			}
			return View(model);
		}

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
				throw new Exception($"Block '{block}' loads several files ({string.Join(", ", plain)}) - please edit the files directly on the server.");

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

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult AllyNames(string? instance) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string alias = chosen.Alias ?? GetInstances()[0].Alias;
				string file = System.IO.Path.Combine(AppContext.BaseDirectory, "data", $"ally_names_{System.IO.Path.GetFileName(alias)}.json");
				if (!System.IO.File.Exists(file))
					return Json(new { success = false, error = "Names are loaded by AutoFarm (needs the whitelist active) - not available yet." });
				return Content("{\"success\":true,\"names\":" + System.IO.File.ReadAllText(file) + "}", "application/json");
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult CommandList() {
			return Json(Tbot.Common.Commands.CommandCatalog.Commands.Select(c => new { name = c.Name, group = c.Group, description = c.Short, syntax = c.Syntax, example = c.Example }));
		}

		[HttpPost]
		public async Task<IActionResult> CheckCommand(string? line, string? instance) {
			var result = Tbot.Common.Commands.CommandCatalog.Check(line);
			string? preview = null;
			string? note = null;
			if (result.Ok && result.Command is "/collect" or "/collectall" && !string.IsNullOrEmpty(instance)) {
				try {
					string dir = AppContext.BaseDirectory;
					string id = Guid.NewGuid().ToString("N");
					string request = System.IO.Path.Combine(dir, $"cmdpreview_{id}.json");
					string answer = System.IO.Path.Combine(dir, $"cmdresult_{id}.json");
					System.IO.File.WriteAllText(request, new JObject { ["id"] = id, ["instance"] = instance, ["line"] = line }.ToString(Formatting.None));
					for (int i = 0; i < 24 && preview == null; i++) {
						await Task.Delay(250);
						if (System.IO.File.Exists(answer)) {
							try {
								preview = (string?) JObject.Parse(System.IO.File.ReadAllText(answer))["text"];
								System.IO.File.Delete(answer);
							} catch (IOException) {
							}
						}
					}
					if (preview == null) {
						try { System.IO.File.Delete(request); } catch { }
						note = "No preview: the bot did not answer in time (is it running?).";
					}
				} catch (Exception e) {
					note = "No preview: " + e.Message;
				}
			}
			return Json(new { result.Known, result.Ok, result.Command, result.Message, result.Syntax, result.Short, preview, note });
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult BoosterStock(string? instance) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string alias = chosen.Alias ?? GetInstances()[0].Alias;
				string file = System.IO.Path.Combine(AppContext.BaseDirectory, "data", $"booster_stock_{System.IO.Path.GetFileName(alias)}.json");
				if (!System.IO.File.Exists(file))
					return Json(new { success = false, error = "No data yet - the KeepBoosters worker writes it on its next run." });
				return Content("{\"success\":true,\"data\":" + System.IO.File.ReadAllText(file) + "}", "application/json");
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult AfLosses(string? instance) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string alias = chosen.Alias ?? GetInstances()[0].Alias;
				var storage = new TBot.WebUI.Services.AutoFarmDashboardReader().Read(alias);
				return Json(new { success = true, periods = storage.Losses }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

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
		public IActionResult StartData(string? instance) {
			try {
				var instances = GetInstances();
				var chosen = instances.FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				if (chosen.Alias == null)
					chosen = instances[0];
				var main = ParseObject(System.IO.File.ReadAllText(chosen.Path));
				bool overrides = false, keepExp = false;
#if FASTFARM
				(overrides, keepExp) = FastFarmReader.GetOverride(chosen.Alias);
#endif
				var data = StartReader.Read(chosen.Alias, main, LegacyConfigCheck.IsLegacy(), overrides, keepExp);
				return Content(data.ToString(Formatting.None), "application/json");
			} catch (Exception e) {
				return Json(new { error = e.Message });
			}
		}

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public IActionResult GetBlock(string? instance, string block, string? scope) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = MainPathFor(scope, block, chosen.Alias == null ? GetInstances()[0].Path : chosen.Path);
				if (NeverForm.Contains(block))
					return Json(new { success = false, error = "This block contains credentials - edit the config file directly on the server." });
				var main = ParseObject(System.IO.File.ReadAllText(mainPath));
				if (string.Equals(block, ExperimentalBlock, StringComparison.OrdinalIgnoreCase) && main[ExperimentalBlock] == null) {
					return Json(new {
						success = true,
						data = DefaultExperimental().ToString(Formatting.None),
						file = "(not in the config yet - saving creates the block \"Experimental\")",
						secretRefs = 0
					});
				}
				var data = ReadBlock(main, block, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!, out var targetFile, out var secretRefs);
				MergeSecrets(SecretFileOf(main, block, System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!), data);
				var addedDefaults = FillDefaults(block, data);
				HideFields(block, data);
				StripUndefined(data);
				return Json(new {
					success = true,
					added = addedDefaults,
					data = data.ToString(Formatting.None),
					file = targetFile == null ? "(inside the instance file)" : System.IO.Path.GetRelativePath(BaseDir, targetFile).Replace('\\', '/'),
					secretRefs
				});
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SetActive(string? instance, string block, bool active) {
			try {
				if (NeverForm.Contains(block))
					return Json(new { success = false, error = "This block contains credentials and cannot be switched here." });
				if (block.Equals("SleepMode", StringComparison.OrdinalIgnoreCase) || block.Equals("GhostSleep", StringComparison.OrdinalIgnoreCase) || block.Equals("Messenger", StringComparison.OrdinalIgnoreCase))
					return Json(new { success = false, error = "Use the switch on the page of this block." });
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = chosen.Alias == null ? GetInstances()[0].Path : chosen.Path;
				string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!;

				string mainText = System.IO.File.ReadAllText(mainPath);
				var main = ParseObject(mainText);
				var existing = ReadBlock(main, block, baseDir, out var targetFile, out _);
				if (existing["Active"]?.Type != JTokenType.Boolean)
					return Json(new { success = false, error = "This block has no Active switch." });
				var blockMain = (JObject) main[block]!;

				if (blockMain["Load"] == null || blockMain["Active"] != null || targetFile == null) {
					blockMain["Active"] = active;
					string mainNew = main.ToString(Formatting.Indented);
					try { System.IO.File.Copy(mainPath, mainPath + ".bak", true); } catch { }
					await SettingsService.WriteSettings(mainPath, mainNew);
				} else {
					var fileData = ParseObject(System.IO.File.ReadAllText(targetFile));
					fileData["Active"] = active;
					try { System.IO.File.Copy(targetFile, targetFile + ".bak", true); } catch { }
					await SettingsService.WriteSettings(targetFile, fileData.ToString(Formatting.Indented));
				}
				return Json(new { success = true, active });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

#if FASTFARM
		[HttpPost]
		public async Task<IActionResult> CreateFastFarm(string? instance) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = chosen.Alias == null ? GetInstances()[0].Path : chosen.Path;
				string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!;
				var main = ParseObject(System.IO.File.ReadAllText(mainPath));
				if (main["FastFarm"] != null)
					return Json(new { success = true, existed = true });

				var block = new JObject {
					["Active"] = false,
					["OnlyWorker"] = true,
					["KeepExpeditions"] = false,
					["CargoType"] = "SmallCargo",
					["ProbesPerTarget"] = 20,
					["SlotsToLeaveFree"] = 1,
					["WeightMetal"] = 3,
					["WeightCrystal"] = 2,
					["WeightDeuterium"] = 1,
					["MinLoot"] = 100000,
					["MinDeuterium"] = 0,
					["TopTargets"] = 36,
					["ReportMaxAgeMinutes"] = 30,
					["DeleteReports"] = true,
					["FleetSpeedPercent"] = 100,
					["CargoSurplusPercent"] = 0,
					["MinFillPercent"] = 50,
					["MaxSystemsPerScanStep"] = 25
				};
				try {
					var af = ReadBlock(main, "AutoFarm", baseDir, out _, out _);
					if (af["Origin"] != null) block["Origin"] = af["Origin"].DeepClone();
					if (af["ScanRange"] != null) block["ScanRange"] = af["ScanRange"].DeepClone();
				} catch { }
				if (block["Origin"] == null)
					block["Origin"] = new JArray(new JObject { ["Galaxy"] = 1, ["System"] = 1, ["Position"] = 1, ["Type"] = "Planet" });
				if (block["ScanRange"] == null)
					block["ScanRange"] = new JArray(new JObject { ["Galaxy"] = 1, ["StartSystem"] = 1, ["EndSystem"] = 499 });

				main["FastFarm"] = block;
				try { System.IO.File.Copy(mainPath, mainPath + ".bak", true); } catch { }
				await SettingsService.WriteSettings(mainPath, main.ToString(Formatting.Indented));
				return Json(new { success = true, existed = false });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}
#endif

		[HttpGet]
		public IActionResult LangScript(string lang) {
			Response.Headers["Cache-Control"] = "no-cache";
			string l = UiLanguage.Normalize(lang);
			var data = UiLanguage.LoadFile(l);
			if (data == null)
				return Content("", "application/javascript");
			var sb = new System.Text.StringBuilder();
			string code = Newtonsoft.Json.JsonConvert.ToString(l);
			if (data["ui"] != null)
				sb.Append("window.UI_I18N=window.UI_I18N||{};window.UI_I18N[").Append(code).Append("]=").Append(data["ui"]!.ToString(Newtonsoft.Json.Formatting.None)).Append(";\n");
			if (data["help"] != null)
				sb.Append("window.CFG_HELP=window.CFG_HELP||{};window.CFG_HELP[").Append(code).Append("]=").Append(data["help"]!.ToString(Newtonsoft.Json.Formatting.None)).Append(";\n");
			return Content(sb.ToString(), "application/javascript");
		}

		[HttpPost]
		public async Task<IActionResult> SetLanguage(string lang) {
			try {
				bool ok = await UiLanguage.Write(lang);
				return Json(new { success = ok, lang = UiLanguage.Normalize(lang) });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public async Task<IActionResult> SaveBlock(string? instance, string block, string content, string? scope) {
			try {
				var chosen = GetInstances().FirstOrDefault(i => string.Equals(i.Alias, instance, StringComparison.OrdinalIgnoreCase));
				string mainPath = MainPathFor(scope, block, chosen.Alias == null ? GetInstances()[0].Path : chosen.Path);
				if (NeverForm.Contains(block))
					return Json(new { success = false, error = "This block contains credentials - edit the config file directly on the server." });
				string baseDir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(mainPath))!;

				var incoming = ParseObject(content);
				string mainText = System.IO.File.ReadAllText(mainPath);
				var main = ParseObject(mainText);
				if (string.Equals(block, ExperimentalBlock, StringComparison.OrdinalIgnoreCase) && main[ExperimentalBlock] == null)
					main[ExperimentalBlock] = new JObject();
				var existingBlock = ReadBlock(main, block, baseDir, out var targetFile, out _);
				RestoreHidden(block, existingBlock, incoming);
				await SplitSecrets(SecretFileOf(main, block, baseDir), incoming);
				var blockMain = (JObject) main[block]!;

				if (blockMain["Load"] == null) {
					main[block] = incoming;
				} else {
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
