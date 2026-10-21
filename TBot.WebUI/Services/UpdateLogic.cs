using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TBot.WebUI.Services {
	public sealed class UpdateInfo {
		public string Current { get; set; } = "";
		public string Latest { get; set; } = "";
		public string Name { get; set; } = "";
		public string Notes { get; set; } = "";
		public string Url { get; set; } = "";
		public string Published { get; set; } = "";
		public bool Prerelease { get; set; }
		public bool Available { get; set; }
		public string AssetName { get; set; } = "";
		public string AssetUrl { get; set; } = "";
		public string ChecksumUrl { get; set; } = "";
		public bool CanInstall { get; set; }
		public string InstallBlock { get; set; } = "";
		public string Error { get; set; } = "";
		public string Feed { get; set; } = "";
		public string CheckedAt { get; set; } = "";
	}

	public static class UpdateLogic {
		public const string DefaultFeed = "https://api.github.com/repos/nemiel1972-lang/tbot4.0-beta/releases?per_page=10";

		public static string ReadCurrentVersion(string appDir) {
			try {
				string f = Path.Combine(appDir, "version.txt");
				if (File.Exists(f)) {
					string v = File.ReadAllText(f).Trim();
					if (v.Length > 0 && v.Length < 64)
						return v;
				}
			} catch { }
			return "dev";
		}

		public static int CompareVersions(string a, string b) {
			var ta = Tokens(a);
			var tb = Tokens(b);
			for (int i = 0; i < Math.Min(ta.Count, tb.Count); i++) {
				string x = ta[i], y = tb[i];
				bool nx = char.IsDigit(x[0]), ny = char.IsDigit(y[0]);
				int c;
				if (nx && ny) c = BigCompare(x, y);
				else if (nx != ny) c = nx ? 1 : -1;
				else c = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
				if (c != 0) return c < 0 ? -1 : 1;
			}
			return ta.Count.CompareTo(tb.Count);
		}
		private static List<string> Tokens(string v) {
			var l = new List<string>();
			foreach (Match m in Regex.Matches(v ?? "", @"\d+|[A-Za-z]+")) {
				if (l.Count == 0 && m.Value.Equals("v", StringComparison.OrdinalIgnoreCase))
					continue;
				l.Add(m.Value);
			}
			return l;
		}
		private static int BigCompare(string x, string y) {
			x = x.TrimStart('0'); y = y.TrimStart('0');
			if (x.Length != y.Length) return x.Length.CompareTo(y.Length);
			return string.CompareOrdinal(x, y);
		}

		public static string DefaultAssetPattern() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return "win-x64";
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
				return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm64" : RuntimeInformation.OSArchitecture == Architecture.X64 ? "linux-x64" : "";
			return "";
		}
		public static bool IsPackageName(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

		public static async Task<UpdateInfo> CheckAsync(string feed, string current, string assetPattern, string appDir, HttpClient http) {
			var info = new UpdateInfo { Current = current, Feed = feed, CheckedAt = DateTime.Now.ToString("s") };
			try {
				string json;
				string feedDir = "";
				if (IsHttp(feed)) {
					if (!feed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
						throw new Exception("The update feed must be an https address.");
					json = await http.GetStringAsync(feed);
				} else {
					string path = Path.GetFullPath(feed);
					if (Directory.Exists(path)) path = Path.Combine(path, "releases.json");
					json = await File.ReadAllTextAsync(path);
					feedDir = Path.GetDirectoryName(path) ?? "";
				}
				using var doc = JsonDocument.Parse(json);
				var releases = new List<JsonElement>();
				if (doc.RootElement.ValueKind == JsonValueKind.Array) releases.AddRange(doc.RootElement.EnumerateArray());
				else if (doc.RootElement.ValueKind == JsonValueKind.Object) releases.Add(doc.RootElement);
				JsonElement? best = null;
				DateTime bestDate = DateTime.MinValue;
				foreach (var r in releases) {
					if (GetBool(r, "draft")) continue;
					DateTime.TryParse(GetStr(r, "published_at"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d);
					if (best == null || d > bestDate) { best = r; bestDate = d; }
				}
				if (best == null) { info.Error = "No release found."; return info; }
				var rel = best.Value;
				info.Latest = GetStr(rel, "tag_name");
				info.Name = GetStr(rel, "name");
				info.Notes = GetStr(rel, "body");
				info.Url = GetStr(rel, "html_url");
				info.Published = GetStr(rel, "published_at");
				info.Prerelease = GetBool(rel, "prerelease");

				bool dev = current == "dev";
				info.Available = !dev && info.Latest.Length > 0 && CompareVersions(info.Latest, current) > 0;

				if (string.IsNullOrWhiteSpace(assetPattern)) assetPattern = DefaultAssetPattern();
				string sums = "";
				if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array) {
					foreach (var a in assets.EnumerateArray()) {
						string name = GetStr(a, "name"), url = GetStr(a, "browser_download_url");
						if (url.Length == 0) url = GetStr(a, "url");
						url = ResolveUrl(url, feedDir);
						if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sums = url;
						else if (info.AssetUrl.Length == 0 && assetPattern.Length > 0 && IsPackageName(name)
							&& name.IndexOf(assetPattern, StringComparison.OrdinalIgnoreCase) >= 0) {
							info.AssetName = name; info.AssetUrl = url;
						}
					}
				}
				info.ChecksumUrl = sums;

				if (dev) info.InstallBlock = "This is a development build (no version.txt), so the version cannot be compared.";
				else if (!info.Available) info.InstallBlock = "You are on the latest version.";
				else if (assetPattern.Length == 0) info.InstallBlock = "Automatic installation is only available for Windows and Linux (x64/arm64). Download the release manually.";
				else if (info.AssetUrl.Length == 0) info.InstallBlock = "This release has no package for this system (looked for \"" + assetPattern + "\" .zip/.tar.gz).";
				else if (sums.Length == 0) info.InstallBlock = "This release has no SHA256SUMS.txt, so the download cannot be verified.";
				else if (FindUpdater(appDir) == null) info.InstallBlock = "The helper program (TBotUpdater) is missing next to the bot.";
				else info.CanInstall = true;
			} catch (Exception e) {
				info.Error = e.Message;
			}
			return info;
		}

		public static async Task<string> DownloadAsync(UpdateInfo info, string dir, HttpClient http) {
			Directory.CreateDirectory(dir);
			string target = Path.Combine(dir, Path.GetFileName(info.AssetName));
			string sumsText = await ReadTextAsync(info.ChecksumUrl, http);
			string? expected = null;
			foreach (string line in sumsText.Split('\n')) {
				var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
				if (m.Success && Path.GetFileName(m.Groups[2].Value.Trim()).Equals(info.AssetName, StringComparison.OrdinalIgnoreCase)) { expected = m.Groups[1].Value.ToLowerInvariant(); break; }
			}
			if (expected == null) throw new Exception("SHA256SUMS.txt has no entry for " + info.AssetName + ".");

			string tmp = target + ".part";
			if (IsHttp(info.AssetUrl)) {
				CheckDownloadHost(info.AssetUrl, info.Feed);
				using var resp = await http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
				resp.EnsureSuccessStatusCode();
				await using var src = await resp.Content.ReadAsStreamAsync();
				await using var dst = File.Create(tmp);
				await src.CopyToAsync(dst);
			} else File.Copy(info.AssetUrl, tmp, true);

			string actual;
			await using (var fs = File.OpenRead(tmp))
				actual = Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant();
			if (actual != expected) {
				try { File.Delete(tmp); } catch { }
				throw new Exception("Checksum mismatch - the download is not what the release says it is. Nothing was installed.");
			}
			File.Move(tmp, target, true);
			return target;
		}

		private static async Task<string> ReadTextAsync(string urlOrPath, HttpClient http) {
			if (IsHttp(urlOrPath)) return await http.GetStringAsync(urlOrPath);
			return await File.ReadAllTextAsync(urlOrPath);
		}

		private static void CheckDownloadHost(string url, string feed) {
			var u = new Uri(url);
			if (u.Scheme != Uri.UriSchemeHttps) throw new Exception("Downloads must use https.");
			if (feed.Contains("api.github.com", StringComparison.OrdinalIgnoreCase)
				&& !(u.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
				throw new Exception("Unexpected download host: " + u.Host);
		}

		public static string? ServiceKillModeProblem() {
			try {
				if (!IsSystemdService())
					return null;
				string cg = File.ReadAllText("/proc/self/cgroup");
				var m = Regex.Match(cg, @"/([^/\r\n]+\.service)");
				if (!m.Success) return null;
				string unit = m.Groups[1].Value;
				var psi = new ProcessStartInfo("systemctl", $"show -p KillMode --value {unit}") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
				using var p = Process.Start(psi);
				if (p == null || !p.WaitForExit(4000)) return null;
				string mode = p.StandardOutput.ReadToEnd().Trim();
				if (mode.Equals("control-group", StringComparison.OrdinalIgnoreCase) || mode.Equals("mixed", StringComparison.OrdinalIgnoreCase))
					return $"The bot runs as the systemd service {unit} with KillMode={mode}. systemd would stop the updater together with the bot, and the update would fail. Add KillMode=process under [Service] in the unit file, run 'sudo systemctl daemon-reload' and try again.";
			} catch (Exception) {  }
			return null;
		}

		public static bool IsSystemdService() {
			try {
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || Environment.GetEnvironmentVariable("INVOCATION_ID") == null)
					return false;
				string cg = File.ReadAllText("/proc/self/cgroup");
				var m = Regex.Match(cg, @"/([^/\r\n]+\.service)(?:/|\s|$)");
				if (!m.Success) return false;
				string unit = m.Groups[1].Value;
				return !unit.StartsWith("user@", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(cg, @"/session-\d+\.scope");
			} catch (Exception) {
				return false;
			}
		}

		public static string? FindUpdater(string appDir) {
			foreach (string n in new[] { "TBotUpdater.exe", "TBotUpdater" }) {
				string p = Path.Combine(appDir, n);
				if (File.Exists(p)) return p;
			}
			return null;
		}

		public static void LaunchUpdater(string appDir, string zipPath, string fromVersion, string toVersion) {
			string updater = FindUpdater(appDir) ?? throw new Exception("TBotUpdater not found.");
			string runDir = Path.Combine(appDir, "update", "run");
			Directory.CreateDirectory(runDir);
			string copy = Path.Combine(runDir, Path.GetFileName(updater));
			foreach (string f in Directory.GetFiles(appDir, "TBotUpdater*")) {
				if (!f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
					File.Copy(f, Path.Combine(runDir, Path.GetFileName(f)), true);
			}
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				File.SetUnixFileMode(copy, File.GetUnixFileMode(copy) | UnixFileMode.UserExecute);

			string exe = Environment.ProcessPath ?? "";
			var argv = Environment.GetCommandLineArgs();
			bool dotnetHost = Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
			var restartArgs = dotnetHost ? argv : argv.Skip(1);
			bool service = IsSystemdService();

			var sb = new StringBuilder();
			sb.Append("--package ").Append(Q(zipPath)).Append(" --target ").Append(Q(appDir.TrimEnd('/', '\\')));
			sb.Append(" --pid ").Append(Environment.ProcessId).Append(" --from ").Append(Q(fromVersion)).Append(" --to ").Append(Q(toVersion));
			if (service || exe.Length == 0) sb.Append(" --no-restart");
			else sb.Append(" --restart ").Append(Q(exe)).Append(" --restart-args ").Append(Q(string.Join(" ", restartArgs.Select(Q))));

			var psi = new ProcessStartInfo(copy, sb.ToString()) { WorkingDirectory = runDir, UseShellExecute = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) };
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
				string? setsid = new[] { "/usr/bin/setsid", "/bin/setsid", "/usr/local/bin/setsid" }.FirstOrDefault(File.Exists);
				string? sh = new[] { "/bin/sh", "/usr/bin/sh" }.FirstOrDefault(File.Exists);
				if (setsid != null && sh != null) {
					var detached = new ProcessStartInfo(sh) { WorkingDirectory = runDir, UseShellExecute = false };
					detached.ArgumentList.Add("-c");
					detached.ArgumentList.Add($"exec {setsid} -f {Q(copy)} {sb} >/dev/null 2>&1 </dev/null");
					Process.Start(detached);
					return;
				}
			}
			Process.Start(psi);
		}

		private static string Q(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
		private static bool IsHttp(string s) => s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
		private static string ResolveUrl(string url, string feedDir) {
			if (url.Length == 0 || IsHttp(url) || feedDir.Length == 0 || Path.IsPathRooted(url)) return url;
			return Path.GetFullPath(Path.Combine(feedDir, url));
		}
		private static string GetStr(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
		private static bool GetBool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
	}
}
