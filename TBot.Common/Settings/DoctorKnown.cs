using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tbot.Common.IO;

namespace Tbot.Common.Settings {
	public static class DoctorKnown {
		private static string PathFor(string alias) => Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"doctor_known_{alias}.json"));

		public static HashSet<string> Read(string alias) {
			try {
				string p = PathFor(alias);
				if (!File.Exists(p))
					return new HashSet<string>();
				return JArray.Parse(File.ReadAllText(p)).Select(x => (string) x).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
			} catch (Exception) { return new HashSet<string>(); }
		}

		public static void Set(string alias, string key, bool known) {
			if (string.IsNullOrWhiteSpace(key))
				return;
			var set = Read(alias);
			if (known) set.Add(key); else set.Remove(key);
			Write(alias, set);
		}

		public static void Clear(string alias) => Write(alias, new HashSet<string>());

		private static void Write(string alias, HashSet<string> set) {
			string p = PathFor(alias);
			Directory.CreateDirectory(Path.GetDirectoryName(p));
			AtomicFile.WriteAllText(p, new JArray(set.OrderBy(x => x, StringComparer.Ordinal)).ToString(Newtonsoft.Json.Formatting.None));
		}
	}
}
