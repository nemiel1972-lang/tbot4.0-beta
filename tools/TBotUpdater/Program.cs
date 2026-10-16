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
			string package = null, restart = null, restartArgs = "", from = "?", to = "?";
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
					case "--no-restart": noRestart = true; break;
				}
			}
			if (package == null || _target == "" || !File.Exists(package) || !Directory.Exists(_target)) {
				Console.WriteLine("Usage: TBotUpdater --package <zip> --target <folder> [--pid N] [--restart <exe>] [--restart-args \"...\"] [--from X] [--to Y] [--wait-start S] [--no-restart]");
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
			Log($"Update {from} -> {to}, Paket {package}, Ziel {_target}");

			try {
				if (pid > 0 && !WaitForExit(pid, TimeSpan.FromSeconds(120))) {
					Fail(false, "The bot did not stop within 2 minutes. Nothing was changed.", from, to, null);
					return 1;
				}
				KillLeftovers();

				Directory.CreateDirectory(staging);
				ZipFile.ExtractToDirectory(package, staging, true);
				string root = FindPackageRoot(staging);
				if (root == null) {
					Fail(false, "The package contains no TBot.exe / TBot.dll. Nothing was changed.", from, to, null);
					return 1;
				}

				Directory.CreateDirectory(backupDir);
				filesTouched = true;
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

				RunMigrateConfig();

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
					Fail(false, e.Message + " Nothing was changed.", from, to, null);
					return 1;
				}
				try {
					KillLeftovers(restart);
					foreach (string rel in backedUp)
						CopyWithRetry(Path.Combine(backupDir, rel), Path.Combine(_target, rel));
					foreach (string rel in added) {
						try { File.Delete(Path.Combine(_target, rel)); } catch { }
					}
					Log("Rollback done: old version restored.");
					if (!noRestart && !string.IsNullOrWhiteSpace(restart))
						StartBot(restart, restartArgs);
					Fail(true, e.Message + " The old version was restored.", from, to, backupDir);
				} catch (Exception e2) {
					Log("ROLLBACK FAILED: " + e2.Message);
					Fail(false, e.Message + " Rollback failed (" + e2.Message + "). The old files are in " + backupDir + ".", from, to, backupDir);
				}
				return 1;
			}
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
			foreach (string n in new[] { "TBot.exe", "TBot.dll" }) {
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

		private static Process StartBot(string exe, string args) {
			var psi = new ProcessStartInfo(exe, args ?? "") { WorkingDirectory = _target, UseShellExecute = false };
			return Process.Start(psi);
		}

		private static void Cleanup(string staging, string updateDir) {
			try { Directory.Delete(staging, true); } catch { }
			try { foreach (string z in Directory.GetFiles(updateDir, "*.zip")) File.Delete(z); } catch { }
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
			Console.WriteLine(line);
			try { File.AppendAllText(_logFile, line + Environment.NewLine); } catch { }
		}
	}
}
