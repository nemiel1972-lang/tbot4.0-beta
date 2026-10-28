using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MigrateConfig {
	public static class Program {
		private static readonly Dictionary<string, string> Legacy = LoadRemoved();

		private static Dictionary<string, string> LoadRemoved() {
			var d = new Dictionary<string, string>();
			foreach (string raw in ReadResource("removed_keys.txt").Split('\n')) {
				string line = raw.Trim();
				if (line.Length == 0 || line.StartsWith("#")) continue;
				int hash = line.IndexOf('#');
				string path = (hash >= 0 ? line.Substring(0, hash) : line).Trim();
				string why = hash >= 0 ? line.Substring(hash + 1).Trim() : "no longer used";
				if (path.Length > 0) d[path] = why;
			}
			return d;
		}

		private static bool _interactive = true;

		public static int Main(string[] args) {
			string dir = AppContext.BaseDirectory;
			bool dryRun = false, yes = false, clean = false;
			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--dir" when i + 1 < args.Length: dir = Path.GetFullPath(args[++i]); break;
					case "--dry-run": dryRun = true; break;
					case "--yes": yes = true; break;
					case "--clean": clean = true; break;
					default:
						Console.WriteLine("Usage: MigrateConfig [--clean] [--dir <folder>] [--dry-run] [--yes]");
						return Pause(2);
				}
			}
			_interactive = !yes;
			try {
				return Pause(clean ? RunClean(dir, dryRun, yes) : Run(dir, dryRun, yes));
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

		private static int RunClean(string dir, bool dryRun, bool yes) {
			Console.WriteLine("TBot config clean-up (removes entries that no longer exist)");
			Console.WriteLine("Folder: " + dir);
			Console.WriteLine();
			var files = FindInstanceFiles(dir);
			if (files.Count == 0) {
				Console.WriteLine("No instance_settings.json found in this folder.");
				return 1;
			}
			JObject template = JObject.Parse(ReadResource("config.vorlage.json"));
			var jobs = new List<CleanJob>();
			foreach (string file in files) {
				Console.WriteLine("--- " + Path.GetFileName(file));
				var job = new CleanJob { File = file, BaseDir = Path.GetDirectoryName(Path.GetFullPath(file)) };
				job.Files[job.File] = JObject.Parse(File.ReadAllText(file), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore });
				JObject json = job.Files[job.File];
				bool split = json.Descendants().OfType<JProperty>().Any(p => p.Name == "Load");
				var present = new List<string>();
				foreach (string k in Legacy.Keys) {
					if (RemoveIn(job, json, job.File, k.Split('.'), 0, false))
						present.Add(k);
				}
				if (split)
					Console.WriteLine("Split config (\"Load\"): entries inside the referenced files (conf.d/...) are cleaned there too.");
				if (present.Count == 0) {
					Console.WriteLine("Nothing to remove.");
				} else {
					Console.WriteLine("Will be removed (" + present.Count + "):");
					foreach (string k in present) Console.WriteLine("  " + k + "  (" + Legacy[k] + ")");
					job.Paths = present;
					jobs.Add(job);
				}
				{
					var unknown = new List<string>();
					Unknown(split ? MergeLoaded(job, json) : json, template, "", unknown);
					unknown = unknown.Where(u => !Legacy.Keys.Any(k => u == k || u.StartsWith(k + "."))).ToList();
					if (unknown.Count > 0) {
						Console.WriteLine("Not in the template and not on the list (NOT touched; may be your own entries or an old setting):");
						foreach (string u in unknown.Take(40)) Console.WriteLine("  " + u);
						if (unknown.Count > 40) Console.WriteLine("  ... and " + (unknown.Count - 40) + " more");
					}
				}
				Console.WriteLine();
			}
			if (jobs.Count == 0) {
				Console.WriteLine("Nothing to clean.");
				return 0;
			}
			if (dryRun) {
				Console.WriteLine("Dry run: nothing was written.");
				return 0;
			}
			if (!yes) {
				Console.Write("Make a backup and remove these entries? [y/N] ");
				string answer = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();
				if (answer != "y" && answer != "yes" && answer != "j" && answer != "ja") {
					Console.WriteLine("Cancelled, nothing was changed.");
					return 0;
				}
			}
			string backup = Path.Combine(dir, "backups", "config-clean-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
			Directory.CreateDirectory(backup);
			foreach (var job in jobs) {
				foreach (string k in job.Paths)
					RemoveIn(job, job.Files[job.File], job.File, k.Split('.'), 0, true);
				foreach (string f in job.Dirty) {
					string rel = Path.GetRelativePath(job.BaseDir, f);
					string bak = Path.Combine(backup, rel);
					Directory.CreateDirectory(Path.GetDirectoryName(bak));
					File.Copy(f, bak, true);
					File.WriteAllText(f, job.Files[f].ToString(Formatting.Indented), new UTF8Encoding(false));
				}
				foreach (string o in job.Orphans.Distinct())
					Console.WriteLine("Note: " + o + " is no longer referenced and can be deleted by hand.");
			}
			Console.WriteLine();
			Console.WriteLine("Done. Backup of the old config: " + backup);
			Console.WriteLine("Restart the bot for the change to take effect. Something wrong? Copy the file from the backup folder back.");
			return 0;
		}

		private static void Unknown(JObject node, JObject template, string path, List<string> result) {
			foreach (var prop in node.Properties()) {
				string p = path == "" ? prop.Name : path + "." + prop.Name;
				if (!template.TryGetValue(prop.Name, out JToken t))
					result.Add(p);
				else if (prop.Value is JObject o && t is JObject to)
					Unknown(o, to, p, result);
			}
		}

		private static JObject MergeLoaded(CleanJob job, JObject node) {
			var result = new JObject();
			foreach (string f in LoadFiles(node, job.BaseDir)) {
				if (!job.Files.TryGetValue(f, out JObject fo)) {
					try { fo = JObject.Parse(File.ReadAllText(f), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }); } catch { continue; }
					job.Files[f] = fo;
				}
				result.Merge(MergeLoaded(job, fo), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
			}
			foreach (var prop in node.Properties()) {
				if (prop.Name.Equals("Load", StringComparison.OrdinalIgnoreCase)) continue;
				if (prop.Value is JObject o && result.TryGetValue(prop.Name, out JToken ex) && ex is JObject eo) {
					eo.Merge(MergeLoaded(job, o), new JsonMergeSettings { MergeArrayHandling = MergeArrayHandling.Replace });
				} else {
					result[prop.Name] = prop.Value is JObject o2 ? MergeLoaded(job, o2) : prop.Value.DeepClone();
				}
			}
			return result;
		}

		private sealed class CleanJob {
			public string File, BaseDir;
			public List<string> Paths = new();
			public Dictionary<string, JObject> Files = new(StringComparer.OrdinalIgnoreCase);
			public HashSet<string> Dirty = new(StringComparer.OrdinalIgnoreCase);
			public List<string> Orphans = new();
		}

		private static List<string> LoadFiles(JObject node, string baseDir) {
			var result = new List<string>();
			if (!node.TryGetValue("Load", StringComparison.OrdinalIgnoreCase, out JToken t)) return result;
			var refs = t.Type == JTokenType.String ? new List<string> { t.Value<string>() }
				: t is JArray arr && arr.All(x => x.Type == JTokenType.String) ? arr.Select(x => x.Value<string>()).ToList() : new List<string>();
			foreach (string r in refs) {
				string path = (r ?? "").Trim().Replace('\\', '/');
				if (path.Length == 0 || Path.IsPathRooted(path)) continue;
				if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
				string full = Path.GetFullPath(Path.Combine(baseDir, path.Contains('/') ? path : Path.Combine("conf.d", path)));
				string root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full)) result.Add(full);
			}
			return result;
		}

		private static bool RemoveIn(CleanJob job, JObject node, string owner, string[] parts, int i, bool apply) {
			if (i >= parts.Length) return false;
			bool found = false;
			if (node.TryGetValue(parts[i], out JToken child)) {
				if (i == parts.Length - 1) {
					found = true;
					if (apply) {
						if (child is JObject co)
							foreach (string f in LoadFiles(co, job.BaseDir)) job.Orphans.Add(Path.GetRelativePath(job.BaseDir, f));
						node.Remove(parts[i]);
						job.Dirty.Add(owner);
					}
				} else if (child is JObject co) {
					found |= RemoveIn(job, co, owner, parts, i + 1, apply);
				}
			}
			foreach (string f in LoadFiles(node, job.BaseDir)) {
				if (!job.Files.TryGetValue(f, out JObject fo)) {
					try { fo = JObject.Parse(File.ReadAllText(f), new JsonLoadSettings { CommentHandling = CommentHandling.Ignore }); } catch { continue; }
					job.Files[f] = fo;
				}
				found |= RemoveIn(job, fo, f, parts, i, apply);
			}
			return found;
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
