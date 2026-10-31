using System;
using System.IO;
using System.Linq;

namespace TBot.WebUI.Services {
	public static class RewardsEvent {
		public static bool AnyActive() {
			try {
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				if (!Directory.Exists(dir)) return false;
				return Directory.EnumerateFiles(dir, "belohnungen_event_*.txt")
					.Any(f => { try { return File.ReadAllText(f).Trim().Equals("on", StringComparison.OrdinalIgnoreCase); } catch (Exception) { return false; } });
			} catch (Exception) {
				return false;
			}
		}
	}
}
