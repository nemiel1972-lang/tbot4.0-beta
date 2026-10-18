using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Common.IO;
using Xunit;

namespace TBot.Tests {
	public class AtomicFileTests : IDisposable {
		private readonly string _dir = Path.Combine(Path.GetTempPath(), "tbot-atomic-" + Guid.NewGuid().ToString("N"));

		public AtomicFileTests() { Directory.CreateDirectory(_dir); }
		public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

		[Fact]
		public void WritesNewFileAndLeavesNoTempFiles() {
			string p = Path.Combine(_dir, "state.json");
			AtomicFile.WriteAllText(p, "{\"a\":1}");
			Assert.Equal("{\"a\":1}", File.ReadAllText(p));
			Assert.Single(Directory.GetFiles(_dir));
		}

		[Fact]
		public void ReplacesExistingContentCompletely() {
			string p = Path.Combine(_dir, "state.json");
			File.WriteAllText(p, new string('x', 5000));
			AtomicFile.WriteAllText(p, "short");
			Assert.Equal("short", File.ReadAllText(p));
			Assert.Single(Directory.GetFiles(_dir));
		}

		[Fact]
		public void CreatesMissingDirectory() {
			string p = Path.Combine(_dir, "sub", "deeper", "x.json");
			AtomicFile.WriteAllText(p, "ok");
			Assert.Equal("ok", File.ReadAllText(p));
		}

		[Fact]
		public void DefaultEncodingIsUtf8WithoutBom() {
			string p = Path.Combine(_dir, "u.json");
			AtomicFile.WriteAllText(p, "äöü");
			byte[] b = File.ReadAllBytes(p);
			Assert.False(b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF);
			Assert.Equal("äöü", Encoding.UTF8.GetString(b));
		}

		[Fact]
		public async Task AsyncVariantWritesAndReplaces() {
			string p = Path.Combine(_dir, "a.json");
			await AtomicFile.WriteAllTextAsync(p, "one");
			await AtomicFile.WriteAllTextAsync(p, "two");
			Assert.Equal("two", File.ReadAllText(p));
			Assert.Single(Directory.GetFiles(_dir));
		}

		[Fact]
		public void FailedWriteKeepsOldContentAndCleansUp() {
			string p = Path.Combine(_dir, "keep.json");
			File.WriteAllText(p, "old");
			string blocked = Path.Combine(_dir, "blocked");
			Directory.CreateDirectory(blocked);
			Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(blocked, "new"));
			Assert.Equal("old", File.ReadAllText(p));
			Assert.Empty(Directory.GetFiles(_dir).Where(f => f.EndsWith(".tmp")));
		}

		[Fact]
		public void ConcurrentWritersNeverProduceMixedContent() {
			string p = Path.Combine(_dir, "race.json");
			string[] contents = { new string('a', 20000), new string('b', 20000), new string('c', 20000) };
			Parallel.For(0, 60, i => AtomicFile.WriteAllText(p, contents[i % 3]));
			string result = File.ReadAllText(p);
			Assert.Contains(result, contents);
			Assert.Single(Directory.GetFiles(_dir));
		}
	}
}
