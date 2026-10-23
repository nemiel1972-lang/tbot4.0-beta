using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;

namespace TBot.WebUI.Controllers {
	public sealed class WhatsNewController : Controller {
		[HttpGet]
		public IActionResult Index() {
			return View(WhatsNew.Read(UiLanguage.Read()));
		}

		[HttpGet]
		public IActionResult Current() {
			try {
				var e = WhatsNew.Pending("web", UiLanguage.Read());
				if (e == null)
					return Json(new { pending = false });
				string more = UiLanguage.Read() == "de" ? "Alle Neuerungen ansehen" : "See what's new";
				return Json(new { pending = true, version = e.Version, title = e.Title, more });
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
