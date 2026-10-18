using System.Text;

namespace Tbot.Common.IO {
	public static class AtomicFile {
		public static void WriteAllText(string path, string content, Encoding? encoding = null) {
			encoding ??= new UTF8Encoding(false);
			string full = Path.GetFullPath(path);
			string? dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			string tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try {
				using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
					byte[] bytes = encoding.GetBytes(content ?? "");
					byte[] preamble = encoding.GetPreamble();
					if (preamble.Length > 0)
						fs.Write(preamble, 0, preamble.Length);
					fs.Write(bytes, 0, bytes.Length);
					fs.Flush(true);
				}
				File.Move(tmp, full, true);
			} catch {
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				throw;
			}
		}

		public static async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null) {
			encoding ??= new UTF8Encoding(false);
			string full = Path.GetFullPath(path);
			string? dir = Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir))
				Directory.CreateDirectory(dir);
			string tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
			try {
				await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)) {
					byte[] bytes = encoding.GetBytes(content ?? "");
					byte[] preamble = encoding.GetPreamble();
					if (preamble.Length > 0)
						await fs.WriteAsync(preamble);
					await fs.WriteAsync(bytes);
					await fs.FlushAsync();
					fs.Flush(true);
				}
				File.Move(tmp, full, true);
			} catch {
				try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
				throw;
			}
		}
	}
}
