using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace TBotUpdater {
	public static class Program {
		private static readonly string[] ProtectedDirs = { "data", "log", "logs", "backups", "profiles", "secrets", "conf.d", "update", "config/secrets", "config/conf.d" };
		private static readonly string[] ProtectedNames = { "lifeform_targets.json" };

		private static string _target = "", _logFile = "", _resultFile = "";

		public static int Main(string[] args) {
			string package = null, restart = null, restartArgs = "", from = "?", to = "?", service = null;
			int pid = 0, waitStart = 40;
			bool noRestart = false;
			for (int i = 0; i < args.Length; i++) {
				switch (args[i]) {
					case "--package" when i + 1 < args.Length: package = Path.GetFullPath(args[++i]); break;
					case "--target" when i + 1 < args.Length: _target = Path.GetFullPath(args[++i]); break;
					case "--pid" when i + 1 < args.Length: int.TryParse(args[++i], out pid); break;
					case "--restart" when i + 1 < args.Length: restart = args[++i]; break;
					case "--restart-args" when i + 1 < args.Length: restartArgs = args[++i]; break;
					case "--from" when i + 1 < args.Length: from = args[++i]; break;
					case "--to" when i + 1 < args.Length: to = args[++i]; break;
					case "--wait-start" when i + 1 < args.Length: int.TryParse(args[++i], out waitStart); break;
					case "--service" when i + 1 < args.Length: service = args[++i]; break;
					case "--no-restart": noRestart = true; break;
				}
			}
			if (package == null || _target == "" || !File.Exists(package) || !Directory.Exists(_target)) {
				Console.WriteLine("Usage: TBotUpdater --package <zip> --target <folder> [--pid N] [--restart <exe>] [--restart-args \"...\"] [--from X] [--to Y] [--wait-start S] [--no-restart] [--service <systemd-Dienst>]");
				return 2;
			}
			string updateDir = Path.Combine(_target, "update");
			Directory.CreateDirectory(updateDir);
			_logFile = Path.Combine(updateDir, "updater.log");
			_resultFile = Path.Combine(updateDir, "result.json");
			try { File.Delete(_resultFile); } catch { }

			string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
			string backupDir = Path.Combine(_target, "backups", "update-" + stamp);
			string staging = Path.Combine(updateDir, "staging-" + stamp);
			var backedUp = new List<string>();
			var added = new List<string>();
			bool filesTouched = false;
			var configBackedUp = new List<string>();
			Log($"Update {from} -> {to}, Paket {package}, Ziel {_target}");

			try {
				if (pid > 0 && !WaitForExit(pid, TimeSpan.FromSeconds(120))) {
					Fail(false, "The bot did not stop within 2 minutes. Nothing was changed.", from, to, null);
					return 1;
				}
				KillLeftovers();

				Directory.CreateDirectory(staging);
				if (package.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || package.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) {
					using var fs = File.OpenRead(package);
					using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
					System.Formats.Tar.TarFile.ExtractToDirectory(gz, staging, true);
				} else ZipFile.ExtractToDirectory(package, staging, true);
				string root = FindPackageRoot(staging);
				if (root == null) {
					bool again = RestartOld(noRestart, restart, restartArgs, service);
					Fail(false, "The package contains no TBot executable (TBot, TBot.exe or TBot.dll). Nothing was changed." + (again ? " The old version was started again." : ""), from, to, null);
					return 1;
				}

				Directory.CreateDirectory(backupDir);
				filesTouched = true;
				configBackedUp = BackupConfig(backupDir);
				int copied = 0;
				int unchanged = 0;
				string extra = "";
				foreach (string src in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) {
					string rel = Path.GetRelativePath(root, src).Replace('\\', '/');
					string dst = Path.Combine(_target, rel);
					if (IsProtected(rel)) {
						if (Array.IndexOf(ProtectedNames, Path.GetFileName(rel).ToLowerInvariant()) >= 0 && File.Exists(dst) && !SameContent(src, dst)) {
							File.Copy(src, dst + ".new", true);
							extra += $" The package has a changed {rel}; it is saved as {rel}.new (yours was not touched).";
							Log($"{rel} differs from the package - new version saved as {rel}.new");
						}
						continue;
					}
					if (File.Exists(dst) && SameContent(src, dst)) {
						unchanged++;
						continue;
					}
					if (File.Exists(dst)) {
						string bak = Path.Combine(backupDir, rel);
						Directory.CreateDirectory(Path.GetDirectoryName(bak));
						File.Copy(dst, bak, true);
						backedUp.Add(rel);
					} else added.Add(rel);
					Directory.CreateDirectory(Path.GetDirectoryName(dst));
					CopyWithRetry(src, dst);
					copied++;
				}
				Log($"{copied} files replaced/added, {unchanged} unchanged, backup in {backupDir}");

				if (!OperatingSystem.IsWindows()) {
					foreach (string n in new[] { "TBot", "ogamed", "MigrateConfig", "TBotUpdater", "start.sh" }) {
						string f = Path.Combine(_target, n);
						try { if (File.Exists(f)) File.SetUnixFileMode(f, File.GetUnixFileMode(f) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute); } catch { }
					}
				}

				RunMigrateConfig();

				if (noRestart && !string.IsNullOrWhiteSpace(service))
					StartService(service);
				if (!noRestart && !string.IsNullOrWhiteSpace(restart)) {
					Process p = StartBot(restart, restartArgs);
					if (p == null) throw new Exception("The new version could not be started.");
					Log($"Bot started (PID {p.Id}), waiting {waitStart} s to check that it keeps running...");
					if (p.WaitForExit(waitStart * 1000))
						throw new Exception($"The new version stopped right after start (exit code {p.ExitCode}).");
				}

				Cleanup(staging, updateDir);
				PruneBackups();
				WriteResult(true, (noRestart ? "Update installed. Restart the bot (the service manager does that automatically)." : "Update installed, the bot is running.") + extra, from, to, backupDir);
				Log("Done.");
				return 0;
			} catch (Exception e) {
				Log("ERROR: " + e.Message);
				if (!filesTouched) {
					bool again = RestartOld(noRestart, restart, restartArgs, service);
					Fail(false, e.Message + " Nothing was changed." + (again ? " The old version was started again." : ""), from, to, null);
					return 1;
				}
				try {
					KillLeftovers(restart);
					foreach (string rel in backedUp)
						CopyWithRetry(Path.Combine(backupDir, rel), Path.Combine(_target, rel));
					foreach (string rel in configBackedUp)
						CopyWithRetry(Path.Combine(backupDir, "_config", rel), Path.Combine(_target, rel));
					foreach (string rel in added) {
						try { File.Delete(Path.Combine(_target, rel)); } catch { }
					}
					Log("Rollback done: old version restored.");
					if (!noRestart && !string.IsNullOrWhiteSpace(restart))
						StartBot(restart, restartArgs);
					else if (noRestart && !string.IsNullOrWhiteSpace(service))
						StartService(service);
					Fail(true, e.Message + " The old version was restored.", from, to, backupDir);
				} catch (Exception e2) {
					Log("ROLLBACK FAILED: " + e2.Message);
					Fail(false, e.Message + " Rollback failed (" + e2.Message + "). The old files are in " + backupDir + ".", from, to, backupDir);
				}
				return 1;
			}
		}

		private static bool RestartOld(bool noRestart, string restart, string restartArgs, string service = null) {
			try {
				if (noRestart && !string.IsNullOrWhiteSpace(service)) return StartService(service);
				if (noRestart || string.IsNullOrWhiteSpace(restart)) return false;
				var p = StartBot(restart, restartArgs);
				Log(p != null ? $"Old version started again (PID {p.Id})." : "The old version could not be started again.");
				return p != null;
			} catch (Exception e) {
				Log("Restart of the old version failed: " + e.Message);
				return false;
			}
		}

		private static List<string> BackupConfig(string backupDir) {
			var list = new List<string>();
			try {
				foreach (string f in Directory.EnumerateFiles(_target, "*", SearchOption.AllDirectories)) {
					string rel = Path.GetRelativePath(_target, f).Replace('\\', '/');
					string l = rel.ToLowerInvariant();
					if (l.StartsWith("backups/") || l.StartsWith("update/") || l.StartsWith("data/") || l.StartsWith("log/") || l.StartsWith("logs/") || l.StartsWith("profiles/")) continue;
					string name = Path.GetFileName(l);
					bool cfg = l.StartsWith("conf.d/") || l.StartsWith("secrets/") || l.StartsWith("config/conf.d/") || l.StartsWith("config/secrets/")
						|| (name.Contains("settings") && name.EndsWith(".json")) || name == "lifeform_targets.json";
					if (!cfg) continue;
					string dst = Path.Combine(backupDir, "_config", rel);
					Directory.CreateDirectory(Path.GetDirectoryName(dst));
					File.Copy(f, dst, true);
					list.Add(rel);
				}
				Log($"{list.Count} config files saved in {Path.Combine(backupDir, "_config")}");
			} catch (Exception e) { Log("Config backup incomplete: " + e.Message); }
			return list;
		}

		private static bool SameContent(string a, string b) {
			var fa = new FileInfo(a);
			var fb = new FileInfo(b);
			if (fa.Length != fb.Length) return false;
			using var sha = System.Security.Cryptography.SHA256.Create();
			using var sa = fa.OpenRead();
			using var sb = fb.OpenRead();
			return sha.ComputeHash(sa).AsSpan().SequenceEqual(sha.ComputeHash(sb));
		}

		private static bool IsProtected(string rel) {
			string l = rel.ToLowerInvariant();
			foreach (string d in ProtectedDirs)
				if (l == d || l.StartsWith(d + "/")) return true;
			string name = Path.GetFileName(l);
			if (Array.IndexOf(ProtectedNames, name) >= 0) return true;
			if (name.Contains("settings") && name.EndsWith(".json")) return true;
			if (name.EndsWith(".bak") || name.EndsWith(".db") || name.EndsWith(".db-wal") || name.EndsWith(".db-shm")) return true;
			return false;
		}

		private static string FindPackageRoot(string staging) {
			if (HasBot(staging)) return staging;
			foreach (string d in Directory.GetDirectories(staging))
				if (HasBot(d)) return d;
			return null;
		}
		private static bool HasBot(string dir) {
			foreach (string n in new[] { "TBot.exe", "TBot.dll", "TBot" }) {
				var f = new FileInfo(Path.Combine(dir, n));
				if (f.Exists && f.Length > 0) return true;
			}
			return false;
		}

		private static bool WaitForExit(int pid, TimeSpan timeout) {
			try {
				using var p = Process.GetProcessById(pid);
				Log($"Waiting for the bot (PID {pid}) to stop...");
				return p.WaitForExit((int) timeout.TotalMilliseconds);
			} catch (ArgumentException) {
				return true;
			}
		}

		private static void KillLeftovers(string except = null) {
			foreach (string name in new[] { "ogamed", "ogamed-tbot" }) {
				foreach (var p in Process.GetProcessesByName(name)) {
					try {
						string path = p.MainModule?.FileName ?? "";
						if (path.StartsWith(_target, StringComparison.OrdinalIgnoreCase)) {
							Log($"Stopping leftover process {name} (PID {p.Id})");
							p.Kill();
							p.WaitForExit(10000);
						}
					} catch {  }
					finally { p.Dispose(); }
				}
			}
		}

		private static void CopyWithRetry(string src, string dst) {
			for (int i = 0; ; i++) {
				try {
					File.Copy(src, dst, true);
					return;
				} catch (IOException) when (i < 20) {
					Thread.Sleep(500);
				}
			}
		}

		private static void RunMigrateConfig() {
			string exe = Path.Combine(_target, OperatingSystem.IsWindows() ? "MigrateConfig.exe" : "MigrateConfig");
			if (!File.Exists(exe)) { Log("MigrateConfig not found - config left as it is."); return; }
			try {
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute);
				var psi = new ProcessStartInfo(exe, $"--yes --dir \"{_target}\"") { WorkingDirectory = _target, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
				using var p = Process.Start(psi);
				string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
				if (!p.WaitForExit(60000)) { try { p.Kill(); } catch { } Log("MigrateConfig timed out."); return; }
				Log("MigrateConfig (exit " + p.ExitCode + "):" + Environment.NewLine + output.TrimEnd());
			} catch (Exception e) {
				Log("MigrateConfig could not run: " + e.Message);
			}
		}

		private static bool StartService(string unit) {
			try {
				if (!System.Text.RegularExpressions.Regex.IsMatch(unit, @"^[A-Za-z0-9_.@-]+\.service$")) return false;
				for (int i = 0; i < 5; i++) {
					if (SystemctlOut("is-active " + unit) == "active") { Log($"Service {unit} is already running again."); return true; }
					Thread.Sleep(1000);
				}
				SystemctlOut("start --no-block " + unit);
				Log($"Service {unit} started (systemctl start).");
				return true;
			} catch (Exception e) {
				Log($"Service {unit} could not be started: {e.Message}. Start it manually: systemctl start {unit}");
				return false;
			}
		}

		private static string SystemctlOut(string args) {
			var psi = new ProcessStartInfo("systemctl", args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
			using var p = Process.Start(psi);
			if (p == null || !p.WaitForExit(15000)) return "";
			return p.StandardOutput.ReadToEnd().Trim();
		}

		private static Process StartBot(string exe, string args) {
			var psi = new ProcessStartInfo(exe, args ?? "") { WorkingDirectory = _target, UseShellExecute = false };
			return Process.Start(psi);
		}

		private static void Cleanup(string staging, string updateDir) {
			try { Directory.Delete(staging, true); } catch { }
			try { foreach (string z in Directory.GetFiles(updateDir, "*.zip").Concat(Directory.GetFiles(updateDir, "*.tar.gz"))) File.Delete(z); } catch { }
		}

		private static void PruneBackups() {
			try {
				var dirs = new DirectoryInfo(Path.Combine(_target, "backups")).GetDirectories("update-*").OrderByDescending(d => d.Name).Skip(3);
				foreach (var d in dirs) d.Delete(true);
			} catch { }
		}

		private static void Fail(bool rolledBack, string message, string from, string to, string backup) {
			WriteResult(false, message, from, to, backup, rolledBack);
			Log("FAILED: " + message);
		}

		private static void WriteResult(bool ok, string message, string from, string to, string backup, bool rolledBack = false) {
			var o = new { ok, rolledBack, message, from, to, backup, finished = DateTime.Now.ToString("s") };
			File.WriteAllText(_resultFile, JsonSerializer.Serialize(o), new UTF8Encoding(false));
		}

		private static void Log(string text) {
			string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {text}";
			try { File.AppendAllText(_logFile, line + Environment.NewLine); } catch { }
			try { Console.WriteLine(line); } catch { }
		}
	}
}
