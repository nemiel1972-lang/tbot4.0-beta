using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MigrateConfig {
	public static class Program {
		private static readonly Dictionary<string, string> Legacy = new() {
			["Brain.LifeformAutoMine.MaxT2Building"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxT3Building"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding6"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding7"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding8"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding9"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding10"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding11"] = "now taken from lifeform_targets.json",
			["Brain.LifeformAutoMine.MaxBuilding12"] = "now taken from lifeform_targets.json",
			["Brain.AutoMine.DeutPlanets"] = "no longer exists - use Brain.AutoMine.ColonyOverrides",
			["Brain.AutoMine.DeutPlanet"] = "no longer exists - use Brain.AutoMine.ColonyOverrides"
		};

		private static bool _interactive = true;

		public static int Main(string[] args) {
			string dir = AppContext.BaseDirectory;
			bool dryRun = false, yes = false;
			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--dir" when i + 1 < args.Length: dir = Path.GetFullPath(args[++i]); break;
					case "--dry-run": dryRun = true; break;
					case "--yes": yes = true; break;
					default:
						Console.WriteLine("Usage: MigrateConfig [--dir <folder>] [--dry-run] [--yes]");
						return Pause(2);
				}
			}
			_interactive = !yes;
			try {
				return Pause(Run(dir, dryRun, yes));
			} catch (Exception e) {
				Console.WriteLine("ERROR: " + e.Message);
				Console.WriteLine("Nothing was changed.");
				return Pause(1);
			}
		}

		private static int Pause(int code) {
			if (_interactive && !Console.IsInputRedirected) {
				Console.WriteLine();
				Console.Write("Press Enter to close...");
				Console.ReadLine();
			}
			return code;
		}

		private static int Run(string dir, bool dryRun, bool yes) {
			Console.WriteLine("TBot config migration");
			Console.WriteLine("Folder: " + dir);
			Console.WriteLine();

			var files = FindInstanceFiles(dir);
			if (files.Count == 0) {
				Console.WriteLine("No instance_settings.json found in this folder. Put MigrateConfig next to your old config and run it again.");
				return 1;
			}

			JObject template = JObject.Parse(ReadResource("config.vorlage.json"));
			var plans = new List<Plan>();
			foreach (string file in files) {
				var plan = BuildPlan(file, template);
				if (plan != null)
					plans.Add(plan);
			}
			if (plans.Count == 0) {
				Console.WriteLine("Nothing to do.");
				return 0;
			}

			if (dryRun) {
				Console.WriteLine("Dry run: nothing was written.");
				return 0;
			}
			if (!yes) {
				Console.Write("Make a backup and write the new config? [y/N] ");
				string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
				if (answer != "y" && answer != "yes" && answer != "j" && answer != "ja") {
					Console.WriteLine("Cancelled, nothing was changed.");
					return 0;
				}
			}

			string backup = Path.Combine(dir, "backups", "config-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
			Directory.CreateDirectory(backup);
			foreach (var plan in plans)
				File.Copy(plan.File, Path.Combine(backup, Path.GetFileName(plan.File)), true);
			string settings = Path.Combine(dir, "settings.json");
			if (File.Exists(settings))
				File.Copy(settings, Path.Combine(backup, "settings.json"), true);

			foreach (var plan in plans)
				File.WriteAllText(plan.File, plan.Result.ToString(Formatting.Indented), new UTF8Encoding(false));

			string targets = Path.Combine(dir, "lifeform_targets.json");
			if (!File.Exists(targets)) {
				File.WriteAllText(targets, ReadResource("lifeform_targets.json"), new UTF8Encoding(false));
				Console.WriteLine("Created lifeform_targets.json (it was missing).");
			}

			Console.WriteLine();
			Console.WriteLine("Done. Backup of the old config: " + backup);
			Console.WriteLine();
			Console.WriteLine("=====================================================================");
			Console.WriteLine(" IMPORTANT");
			Console.WriteLine("  1. Stop TBot if it is running and RESTART it - the new config only");
			Console.WriteLine("     takes effect after a restart.");
			Console.WriteLine("  2. CHECK the config before you start (see the notes above), especially");
			Console.WriteLine("     everything marked ATTENTION. New workers are switched off.");
			Console.WriteLine("  3. Something wrong? Copy the file from the backup folder back.");
			Console.WriteLine("=====================================================================");
			return 0;
		}

		private sealed class Plan {
			public string File;
			public JObject Result;
		}

		private static Plan BuildPlan(string file, JObject template) {
			Console.WriteLine("--- " + Path.GetFileName(file));
			JObject old = JObject.Parse(File.ReadAllText(file), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });

			if (old.Descendants().OfType<JProperty>().Any(p => p.Name == "Load")) {
				Console.WriteLine("This config already uses the split format (\"Load\"). Skipped.");
				Console.WriteLine();
				return null;
			}

			var oldCopy = (JObject) old.DeepClone();
			var renamed = new List<string>();
			if (old["Brain"] is JObject brain && brain["AutoFleepJumpGate"] != null && brain["AutoFleetJumpGate"] == null) {
				var tok = brain["AutoFleepJumpGate"];
				brain.Remove("AutoFleepJumpGate");
				brain["AutoFleetJumpGate"] = tok;
				renamed.Add("Brain.AutoFleepJumpGate -> Brain.AutoFleetJumpGate (typo in the original)");
			}

			var added = new List<string>();
			JObject result = (JObject) template.DeepClone();
			Overlay(result, old, "", added);
			var addedBlocks = new List<string>();
			ForceInactive(result, old, "", addedBlocks);

			var notes = new List<string>();
			var unused = new List<string>();
			foreach (var kv in Legacy)
				if (PathExists(old, kv.Key))
					unused.Add(kv.Key + "  (" + kv.Value + ")");

			if (Flag(old, "Brain.LifeformAutoMine.Active") && Legacy.Keys.Any(k => k.StartsWith("Brain.LifeformAutoMine") && PathExists(old, k)))
				notes.Add("Lifeform AutoMine is ON: building levels for positions 4-12 now come from lifeform_targets.json, NOT from your old MaxT2Building/MaxT3Building/MaxBuilding6-12. " +
					"Open lifeform_targets.json and adjust the \"Fallback\" values to your account first, or switch Lifeform AutoMine off. " +
					"MaxBasePopulationBuilding / MaxBaseFoodBuilding / MaxBaseTechBuilding still work as an upper limit.");
			if (PathExists(old, "Brain.AutoMine.DeutPlanets"))
				notes.Add("DeutPlanets/DeutPlanet are gone. Planets that used them now get the Colony values until you add an entry to Brain.AutoMine.ColonyOverrides.");
			if (Flag(old, "AutoFarm.Active") && !PathExists(old, "AutoFarm.DeuteriumGuard"))
				notes.Add("AutoFarm: the low-deuterium guard is ON by default (10,000,000 minimum on the start celestial). It only affects attacks on defended targets and the speed fleet. Set AutoFarm.DeuteriumGuard to false if you do not want it.");

			var mismatches = new List<string>();
			Verify(old, result, "", mismatches);
			if (mismatches.Count > 0) {
				Console.WriteLine("Internal check failed, nothing will be written:");
				foreach (string m in mismatches.Take(10))
					Console.WriteLine("  " + m);
				throw new InvalidOperationException("The merged config does not contain all old values.");
			}

			bool anything = added.Count > 0 || addedBlocks.Count > 0 || unused.Count > 0 || renamed.Count > 0;
			if (!anything) {
				Console.WriteLine("This config is already up to date.");
				Console.WriteLine();
				return null;
			}

			Console.WriteLine("Your old values are kept (all " + CountLeaves(oldCopy) + " checked).");
			if (renamed.Count > 0) {
				Console.WriteLine("Renamed:");
				foreach (string r in renamed) Console.WriteLine("  " + r);
			}
			if (addedBlocks.Count > 0) {
				Console.WriteLine("New blocks/switches, added and switched OFF:");
				foreach (string b in addedBlocks) Console.WriteLine("  " + b);
			}
			if (added.Count > 0)
				Console.WriteLine("New entries taken from the template with default values: " + added.Count + " (explained in config/REFERENCE.md)");
			if (unused.Count > 0) {
				Console.WriteLine("Kept in the file, but not used by this version:");
				foreach (string u in unused) Console.WriteLine("  " + u);
			}
			foreach (string n in notes)
				Console.WriteLine("ATTENTION: " + n);
			Console.WriteLine();
			return new Plan { File = file, Result = result };
		}

		private static void Overlay(JObject target, JObject old, string path, List<string> added) {
			foreach (var prop in target.Properties().ToList()) {
				string p = path == "" ? prop.Name : path + "." + prop.Name;
				if (old.TryGetValue(prop.Name, out JToken oldValue)) {
					if (prop.Value is JObject tObj && oldValue is JObject oObj)
						Overlay(tObj, oObj, p, added);
					else
						prop.Value = oldValue.DeepClone();
				} else {
					added.Add(p);
				}
			}
			foreach (var prop in old.Properties())
				if (target[prop.Name] == null)
					target[prop.Name] = prop.Value.DeepClone();
		}

		private static void ForceInactive(JObject node, JObject old, string path, List<string> report, bool parentNew = false) {
			foreach (var prop in node.Properties().ToList()) {
				string p = path == "" ? prop.Name : path + "." + prop.Name;
				bool known = old != null && old.TryGetValue(prop.Name, out _);
				if (prop.Name == "Active" && prop.Value.Type == JTokenType.Boolean) {
					if (!known && (bool) prop.Value) {
						prop.Value = false;
						if (!parentNew)
							report.Add(p + " (new switch)");
					}
				} else if (prop.Value is JObject child) {
					JObject childOld = known ? old[prop.Name] as JObject : null;
					if (!known && !parentNew && child["Active"] != null)
						report.Add(p + " (new block)");
					ForceInactive(child, childOld, p, report, parentNew || !known);
				}
			}
		}

		private static void Verify(JToken old, JToken result, string path, List<string> mismatches) {
			if (old is JObject o) {
				if (result is not JObject r) { mismatches.Add(path + ": not an object any more"); return; }
				foreach (var prop in o.Properties()) {
					string p = path == "" ? prop.Name : path + "." + prop.Name;
					if (!r.TryGetValue(prop.Name, out JToken rv)) { mismatches.Add(p + ": missing"); continue; }
					Verify(prop.Value, rv, p, mismatches);
				}
			} else if (!JToken.DeepEquals(old, result)) {
				mismatches.Add(path + ": value changed");
			}
		}

		private static int CountLeaves(JToken t) =>
			t is JObject o ? o.Properties().Sum(p => CountLeaves(p.Value)) : 1;

		private static bool PathExists(JObject root, string path) {
			JToken cur = root;
			foreach (string part in path.Split('.')) {
				if (cur is not JObject o || !o.TryGetValue(part, out cur))
					return false;
			}
			return true;
		}

		private static bool Flag(JObject root, string path) {
			JToken cur = root;
			foreach (string part in path.Split('.')) {
				if (cur is not JObject o || !o.TryGetValue(part, out cur))
					return false;
			}
			return cur.Type == JTokenType.Boolean && (bool) cur;
		}

		private static List<string> FindInstanceFiles(string dir) {
			var result = new List<string>();
			string settings = Path.Combine(dir, "settings.json");
			try {
				if (File.Exists(settings)) {
					var s = JObject.Parse(File.ReadAllText(settings), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
					if (s["Instances"] is JArray arr) {
						foreach (var inst in arr.OfType<JObject>()) {
							string rel = inst.Value<string>("Settings");
							if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel) || rel.Contains(".."))
								continue;
							string full = Path.GetFullPath(Path.Combine(dir, rel));
							if (File.Exists(full) && !result.Contains(full, StringComparer.OrdinalIgnoreCase))
								result.Add(full);
						}
					}
				}
			} catch {
			}
			string def = Path.Combine(dir, "instance_settings.json");
			if (result.Count == 0 && File.Exists(def))
				result.Add(def);
			return result;
		}

		private static string ReadResource(string name) {
			using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
				?? throw new InvalidOperationException("Embedded resource missing: " + name);
			using var reader = new StreamReader(stream, Encoding.UTF8);
			return reader.ReadToEnd().TrimStart('﻿');
		}
	}
}
