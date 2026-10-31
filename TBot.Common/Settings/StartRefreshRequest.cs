using System;
using System.IO;
using Tbot.Common.IO;

namespace Tbot.Common.Settings {
	public static class StartRefreshRequest {
		private static string PathFor(string alias) => Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"startrefresh_{alias}.flag"));

		public static void Request(string alias) {
			string p = PathFor(alias);
			Directory.CreateDirectory(Path.GetDirectoryName(p));
			AtomicFile.WriteAllText(p, DateTime.UtcNow.ToString("o"));
		}

		public static bool Consume(string alias) {
			try {
				string p = PathFor(alias);
				if (!File.Exists(p))
					return false;
				File.Delete(p);
				return true;
			} catch (Exception) { return false; }
		}
	}
}
