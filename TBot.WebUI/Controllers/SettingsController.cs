using Microsoft.AspNetCore.Mvc;
using TBot.WebUI.Models;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Dynamic;
using Tbot.Common.Settings;
using Newtonsoft.Json.Linq;
using System.Text;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public class SettingsController : Controller {
		private string GetCurrentDirectory() {
			return AppDomain.CurrentDomain.BaseDirectory;
		}
		public static bool DoesPropertyExist(dynamic settings, string name) {
			if (settings is ExpandoObject)
				return ((IDictionary<string, object>) settings).ContainsKey(name);

			return settings.GetType().GetProperty(name) != null;
		}
		private async Task<List<string>> GetFileNames() {
			var fileNames = new List<string>() { new FileInfo(SettingsService.GlobalSettingsPath).Name };
			var settingsFile = await SettingsService.GetSettings(SettingsService.GlobalSettingsPath);

			if (!SettingsService.IsSettingSet(settingsFile, "Instances")) {
				return fileNames;
			}

			foreach (var instance in settingsFile.Instances) {
				fileNames.Add(instance.Settings);
			}

			foreach (var folder in new[] { SettingsService.DefaultLoadDirectory, SettingsService.SecretsDirectory }) {
				var dir = Path.Combine(GetCurrentDirectory(), folder);
				if (!Directory.Exists(dir))
					continue;
				foreach (var file in Directory.EnumerateFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
					fileNames.Add(folder + "/" + Path.GetFileName(file));
			}

			return fileNames;
		}

		[HttpGet]
		public async Task<IActionResult> Index() {
			var settingsModel = new SettingsModel();
			settingsModel.SettingsFiles = await GetFileNames();
			return View(settingsModel);
		}

		private async Task<string> ResolveAllowedPath(string fileName) {
			if (string.IsNullOrWhiteSpace(fileName))
				return null;
			var wanted = Path.GetFullPath(Path.Combine(GetCurrentDirectory(), fileName));
			foreach (var name in await GetFileNames()) {
				var allowed = Path.GetFullPath(Path.Combine(GetCurrentDirectory(), name));
				if (string.Equals(allowed, wanted, StringComparison.Ordinal) && allowed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
					return allowed;
			}
			return null;
		}

		private static bool IsSecretsFile(string fileName) {
			return fileName != null && fileName.Replace('\\', '/').StartsWith(SettingsService.SecretsDirectory + "/", StringComparison.OrdinalIgnoreCase);
		}

		[HttpGet]
		public async Task<IActionResult> GetFileContent(string fileName) {
			var path = await ResolveAllowedPath(fileName);
			if (path == null)
				return Json(new { data = "", error = "File not allowed" });
			string fileContents = await SettingsService.GetSettingsFileContents(path);
			return Json(new { data = SecretMasker.MaskJson(fileContents, IsSecretsFile(fileName)) });
		}

		[HttpPost]
		public async Task<IActionResult> SaveFileContent(string fileName, string content) {
			try {
				var path = await ResolveAllowedPath(fileName);
				if (path == null)
					return Json(new { success = false, error = "File not allowed" });
				string original = await SettingsService.GetSettingsFileContents(path);
				string jsonFormatted = SecretMasker.RestoreSecrets(JValue.Parse(content).ToString(Formatting.Indented), original, IsSecretsFile(fileName));
				try { System.IO.File.Copy(path, path + ".bak", true); } catch { }
				await SettingsService.WriteSettings(path, jsonFormatted);
				return Json(new { success = true });
			} catch (Exception ex) {
				return Json(new { success = false, error = ex.Message });
			}
		}
	}
}
