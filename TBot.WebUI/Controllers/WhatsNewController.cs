using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;

namespace TBot.WebUI.Controllers {
	public sealed class WhatsNewController : Controller {
		[HttpGet]
		public IActionResult Current() {
			try {
				var e = WhatsNew.Pending("web", UiLanguage.Read());
				if (e == null)
					return Json(new { pending = false });
				return Json(new { pending = true, version = e.Version, title = e.Title, lines = e.Lines });
			} catch (Exception) {
				return Json(new { pending = false });
			}
		}

		[HttpPost]
		public IActionResult Dismiss(string version) {
			try {
				if (!string.IsNullOrWhiteSpace(version))
					WhatsNew.MarkShown("web", version.Trim());
				return Json(new { success = true });
			} catch (Exception e) {
				return Json(new { success = false, error = e.Message });
			}
		}
	}
}
