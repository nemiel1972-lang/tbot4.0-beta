using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using TBot.WebUI.Services;

namespace TBot.WebUI.Controllers {
	public sealed class UpdateController : Controller {
		private static readonly HttpClient Http = CreateHttp();
		private static readonly object Gate = new();
		private static UpdateInfo? _cached;
		private static DateTime _cachedAt;
		private static bool _busy;

		private static HttpClient CreateHttp() {
			var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
			client.DefaultRequestHeaders.UserAgent.ParseAdd("TBot-Updater");
			return client;
		}

		private static (string Feed, string Pattern) ReadSettings() {
			string feed = UpdateLogic.DefaultFeed, pattern = "";
			try {
				var global = JObject.Parse(System.IO.File.ReadAllText(SettingsService.GlobalSettingsPath));
				var u = global["WebUI"]?["Update"];
				if (!string.IsNullOrWhiteSpace(u?["FeedUrl"]?.ToString()))
					feed = u!["FeedUrl"]!.ToString().Trim();
				pattern = u?["AssetPattern"]?.ToString().Trim() ?? "";
			} catch {
			}
			return (feed, pattern);
		}

		private static string ResultFile => Path.Combine(AppContext.BaseDirectory, "update", "result.json");

		[HttpGet]
		[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
		public async Task<IActionResult> Check(bool force = false) {
			UpdateInfo? info = null;
			lock (Gate) {
				if (!force && _cached != null && DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(10))
					info = _cached;
			}
			if (info == null) {
				var (feed, pattern) = ReadSettings();
				info = await UpdateLogic.CheckAsync(feed, UpdateLogic.ReadCurrentVersion(AppContext.BaseDirectory), pattern, AppContext.BaseDirectory, Http);
				if (string.IsNullOrEmpty(info.Error)) {
					lock (Gate) {
						_cached = info;
						_cachedAt = DateTime.UtcNow;
					}
				}
			}
			string? last = null;
			try {
				if (System.IO.File.Exists(ResultFile))
					last = System.IO.File.ReadAllText(ResultFile);
			} catch { }
			return Json(new { info, last, busy = _busy });
		}

		[HttpPost]
		public async Task<IActionResult> Start() {
			UpdateInfo? info;
			lock (Gate) {
				info = _cached;
				if (info == null || !info.Available || !info.CanInstall)
					return Json(new { success = false, error = "There is no installable update. Check for updates first." });
				if (_busy)
					return Json(new { success = false, error = "An update is already running." });
				_busy = true;
			}
			try {
				string appDir = AppContext.BaseDirectory;
				string? killModeProblem = UpdateLogic.ServiceKillModeProblem();
				if (killModeProblem != null)
					throw new Exception(killModeProblem);
				string zip = await UpdateLogic.DownloadAsync(info, Path.Combine(appDir, "update"), Http);
				UpdateLogic.LaunchUpdater(appDir, zip, info.Current, info.Latest);
				_ = Task.Run(async () => {
					await Task.Delay(2000);
					Environment.Exit(0);
				});
				return Json(new { success = true, message = "Update " + info.Latest + " is being installed. The bot stops now and restarts in about a minute." });
			} catch (Exception e) {
				lock (Gate)
					_busy = false;
				return Json(new { success = false, error = e.Message });
			}
		}

		[HttpPost]
		public IActionResult Dismiss() {
			try {
				if (System.IO.File.Exists(ResultFile))
					System.IO.File.Delete(ResultFile);
			} catch { }
			return Json(new { success = true });
		}
	}
}
