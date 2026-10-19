using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using TBot.WebUI.Services;
using Xunit;

namespace TBot.Tests {
	public class UpdateLogicTests : IDisposable {
		private readonly string _dir = Path.Combine(Path.GetTempPath(), "tbot-update-" + Guid.NewGuid().ToString("N"));

		public UpdateLogicTests() { Directory.CreateDirectory(_dir); }
		public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

		[Theory]
		[InlineData("v4.0-beta2", "v4.0-beta2a", -1)]
		[InlineData("v4.0-beta2a", "v4.0-beta2b", -1)]
		[InlineData("v4.0-beta2b", "v4.0-beta3", -1)]
		[InlineData("v4.0-beta9", "v4.0-beta10", -1)]
		[InlineData("v4.0-beta4", "v4.0-beta4", 0)]
		[InlineData("4.0-beta4", "v4.0-beta4", 0)]
		[InlineData("v4.0-beta4", "v4.0-beta3", 1)]
		[InlineData("v4.1", "v4.0-beta9", 1)]
		[InlineData("V4.0-BETA2", "v4.0-beta2", 0)]
		public void CompareVersionsOrdersTags(string a, string b, int expected) {
			Assert.Equal(expected, UpdateLogic.CompareVersions(a, b));
			Assert.Equal(-expected, UpdateLogic.CompareVersions(b, a));
		}

		[Fact]
		public void CompareVersionsHandlesHugeNumbersWithoutOverflow() {
			Assert.Equal(-1, UpdateLogic.CompareVersions("v99999999999999999999", "v100000000000000000000"));
		}

		[Fact]
		public void ReadCurrentVersionUsesFileOrFallsBackToDev() {
			Assert.Equal("dev", UpdateLogic.ReadCurrentVersion(_dir));
			File.WriteAllText(Path.Combine(_dir, "version.txt"), "  v4.0-beta4\n");
			Assert.Equal("v4.0-beta4", UpdateLogic.ReadCurrentVersion(_dir));
			File.WriteAllText(Path.Combine(_dir, "version.txt"), new string('x', 200));
			Assert.Equal("dev", UpdateLogic.ReadCurrentVersion(_dir));
		}

		private static string Release(string tag, string published, bool draft = false, bool pre = true, string assets = "") =>
			"{\"tag_name\":\"" + tag + "\",\"name\":\"" + tag + "\",\"body\":\"notes\",\"html_url\":\"https://example/\",\"published_at\":\"" + published
			+ "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (pre ? "true" : "false") + ",\"assets\":[" + assets + "]}";

		private string WriteFeed(params string[] releases) {
			string p = Path.Combine(_dir, "releases.json");
			File.WriteAllText(p, "[" + string.Join(",", releases) + "]");
			return p;
		}

		private static string Asset(string name, string url) => "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + url.Replace("\\", "/") + "\"}";

		[Fact]
		public async Task CheckPicksNewestPublishedAndSkipsDrafts() {
			string feed = WriteFeed(
				Release("v4.0-beta3", "2026-10-01T10:00:00Z"),
				Release("v4.0-beta5", "2026-10-20T10:00:00Z", draft: true),
				Release("v4.0-beta4", "2026-10-10T10:00:00Z"));
			var info = await UpdateLogic.CheckAsync(feed, "v4.0-beta3", "win-x64", _dir, new HttpClient());
			Assert.Equal("", info.Error);
			Assert.Equal("v4.0-beta4", info.Latest);
			Assert.True(info.Available);
		}

		[Fact]
		public async Task CheckReportsNoUpdateWhenCurrent() {
			string feed = WriteFeed(Release("v4.0-beta4", "2026-10-10T10:00:00Z"));
			var info = await UpdateLogic.CheckAsync(feed, "v4.0-beta4", "win-x64", _dir, new HttpClient());
			Assert.False(info.Available);
			Assert.False(info.CanInstall);
			Assert.Contains("latest", info.InstallBlock);
		}

		[Fact]
		public async Task DevBuildNeverOffersUpdate() {
			string feed = WriteFeed(Release("v4.0-beta4", "2026-10-10T10:00:00Z"));
			var info = await UpdateLogic.CheckAsync(feed, "dev", "win-x64", _dir, new HttpClient());
			Assert.False(info.Available);
			Assert.False(info.CanInstall);
			Assert.Contains("development build", info.InstallBlock);
		}

		[Fact]
		public async Task EmptyFeedGivesError() {
			string feed = WriteFeed();
			var info = await UpdateLogic.CheckAsync(feed, "v4.0-beta1", "win-x64", _dir, new HttpClient());
			Assert.Equal("No release found.", info.Error);
		}

		[Fact]
		public async Task PlainHttpFeedIsRejected() {
			var info = await UpdateLogic.CheckAsync("http://example.com/releases", "v4.0-beta1", "win-x64", _dir, new HttpClient());
			Assert.Contains("https", info.Error);
		}

		[Fact]
		public async Task MissingChecksumFileBlocksInstall() {
			string zip = Path.Combine(_dir, "TBot-v4.0-beta4-win-x64.zip");
			File.WriteAllText(zip, "data");
			string feed = WriteFeed(Release("v4.0-beta4", "2026-10-10T10:00:00Z", assets: Asset("TBot-v4.0-beta4-win-x64.zip", zip)));
			var info = await UpdateLogic.CheckAsync(feed, "v4.0-beta3", "win-x64", _dir, new HttpClient());
			Assert.False(info.CanInstall);
			Assert.Contains("SHA256SUMS", info.InstallBlock);
			Assert.Equal("TBot-v4.0-beta4-win-x64.zip", info.AssetName);
		}

		[Fact]
		public async Task NoMatchingPackageBlocksInstall() {
			string feed = WriteFeed(Release("v4.0-beta4", "2026-10-10T10:00:00Z", assets: Asset("TBot-v4.0-beta4-linux-x64.zip", "x")));
			var info = await UpdateLogic.CheckAsync(feed, "v4.0-beta3", "win-x64", _dir, new HttpClient());
			Assert.False(info.CanInstall);
			Assert.Contains("no package", info.InstallBlock);
		}

		private UpdateInfo InfoFor(byte[] zipBytes, string sumsLine) {
			string zip = Path.Combine(_dir, "src-TBot-win-x64.zip");
			File.WriteAllBytes(zip, zipBytes);
			string sums = Path.Combine(_dir, "SHA256SUMS.txt");
			File.WriteAllText(sums, sumsLine);
			return new UpdateInfo { AssetName = "TBot-win-x64.zip", AssetUrl = zip, ChecksumUrl = sums, Feed = "local" };
		}

		private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

		[Fact]
		public async Task DownloadAcceptsMatchingChecksum() {
			byte[] data = Encoding.UTF8.GetBytes("package-bytes");
			var info = InfoFor(data, Sha(data) + "  TBot-win-x64.zip\n");
			string target = await UpdateLogic.DownloadAsync(info, Path.Combine(_dir, "dl"), new HttpClient());
			Assert.Equal(data, File.ReadAllBytes(target));
			Assert.False(File.Exists(target + ".part"));
		}

		[Fact]
		public async Task DownloadAcceptsBinaryMarkerAndCrLf() {
			byte[] data = Encoding.UTF8.GetBytes("abc");
			var info = InfoFor(data, "0000000000000000000000000000000000000000000000000000000000000000  other.zip\r\n" + Sha(data).ToUpperInvariant() + " *TBot-win-x64.zip\r\n");
			string target = await UpdateLogic.DownloadAsync(info, Path.Combine(_dir, "dl"), new HttpClient());
			Assert.True(File.Exists(target));
		}

		[Fact]
		public async Task DownloadRejectsChecksumMismatchAndDeletesFile() {
			byte[] data = Encoding.UTF8.GetBytes("tampered");
			var info = InfoFor(data, new string('a', 64) + "  TBot-win-x64.zip\n");
			string dl = Path.Combine(_dir, "dl");
			var ex = await Assert.ThrowsAsync<Exception>(() => UpdateLogic.DownloadAsync(info, dl, new HttpClient()));
			Assert.Contains("Checksum mismatch", ex.Message);
			Assert.False(File.Exists(Path.Combine(dl, "TBot-win-x64.zip")));
			Assert.False(File.Exists(Path.Combine(dl, "TBot-win-x64.zip.part")));
		}

		[Fact]
		public async Task DownloadRejectsMissingChecksumEntry() {
			byte[] data = Encoding.UTF8.GetBytes("x");
			var info = InfoFor(data, Sha(data) + "  something-else.zip\n");
			var ex = await Assert.ThrowsAsync<Exception>(() => UpdateLogic.DownloadAsync(info, Path.Combine(_dir, "dl"), new HttpClient()));
			Assert.Contains("no entry", ex.Message);
		}

		[Fact]
		public async Task DownloadStripsDirectoryPartsFromAssetName() {
			byte[] data = Encoding.UTF8.GetBytes("x");
			var info = InfoFor(data, Sha(data) + "  evil.zip\n");
			info.AssetName = "../../evil.zip";
			string dl = Path.Combine(_dir, "dl");
			await Assert.ThrowsAsync<Exception>(() => UpdateLogic.DownloadAsync(info, dl, new HttpClient()));
			Assert.False(File.Exists(Path.Combine(_dir, "evil.zip")));
		}

		[Fact]
		public void FindUpdaterFindsBinaryNextToApp() {
			Assert.Null(UpdateLogic.FindUpdater(_dir));
			File.WriteAllText(Path.Combine(_dir, "TBotUpdater"), "");
			Assert.NotNull(UpdateLogic.FindUpdater(_dir));
		}
	
		[Fact]
		public void IsPackageName_AcceptsZipAndTarGz() {
			Assert.True(UpdateLogic.IsPackageName("TBot-v4.0-beta8-win-x64.zip"));
			Assert.True(UpdateLogic.IsPackageName("TBot-v4.0-beta8-linux-x64.tar.gz"));
			Assert.False(UpdateLogic.IsPackageName("SHA256SUMS.txt"));
		}
}
}
