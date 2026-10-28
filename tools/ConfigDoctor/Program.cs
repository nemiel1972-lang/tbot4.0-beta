using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace ConfigDoctorTool {
	public static class Program {
		public static int Main(string[] args) {
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < args.Length; i++) {
				if (args[i] == "--dir" && i + 1 < args.Length) dir = Path.GetFullPath(args[++i]);
				else { Console.WriteLine("Usage: ConfigDoctor [--dir <folder with settings.json>]"); return 2; }
			}
			string global = Path.Combine(dir, "settings.json");
			if (!File.Exists(global)) {
				Console.WriteLine($"settings.json not found in {dir}");
				return 2;
			}
			var instances = new List<(string Alias, string Path)>();
			try {
				var g = JObject.Parse(File.ReadAllText(global));
				if (g["Instances"] is JArray arr) {
					foreach (var inst in arr.OfType<JObject>()) {
						string file = inst["Settings"]?.ToString();
						if (string.IsNullOrWhiteSpace(file)) continue;
						instances.Add((inst["Alias"]?.ToString() ?? file, Path.GetFullPath(Path.Combine(dir, file))));
					}
				}
			} catch (Exception e) {
				Console.WriteLine("settings.json could not be read: " + e.Message);
				return 2;
			}
			if (instances.Count == 0)
				instances.Add(("MAIN", global));

			int errors = 0, warnings = 0;
			foreach (var (alias, path) in instances) {
				Console.WriteLine($"== {alias} ({path})");
				try {
					var cfg = SettingsService.ReadResolvedObject(path);
					var findings = ConfigDoctor.Check(cfg);
					Print(findings, ref errors, ref warnings);
					if (findings.Count == 0) Console.WriteLine("   no problems found");
				} catch (Exception e) {
					Console.WriteLine("   [error] the config could not be read: " + e.Message);
					errors++;
				}
			}
			if (instances.Count > 1) {
				var shared = ConfigDoctor.CheckShared(instances);
				if (shared.Count > 0) {
					Console.WriteLine("== Shared files between instances");
					Print(shared, ref errors, ref warnings);
				}
			}
			Console.WriteLine();
			Console.WriteLine($"{errors} error(s), {warnings} warning(s).");
			return errors > 0 ? 1 : 0;
		}

		private static void Print(List<ConfigFinding> findings, ref int errors, ref int warnings) {
			foreach (var x in findings) {
				Console.WriteLine("   " + x);
				if (x.Severity == "error") errors++; else if (x.Severity == "warn") warnings++;
			}
		}
	}
}
