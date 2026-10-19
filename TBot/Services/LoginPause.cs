using System;
using System.IO;
using System.Text.Json;
using Tbot.Common.IO;

namespace Tbot.Services {
	public class LoginPauseState {
		public int Failures { get; set; }
		public DateTime UntilUtc { get; set; }
	}

	public class LoginPauseConfig {
		public bool Active = true;
		public int FailuresBeforePause = 2;
		public int PauseMinutes = 30;
		public int MaxPauseMinutes = 120;
	}

	public static class LoginPause {
		private static string PathFor(string alias) {
			string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dir);
			return System.IO.Path.Combine(dir, System.IO.Path.GetFileName($"login_pause_{alias}.json"));
		}

		public static LoginPauseState Read(string alias) {
			try {
				string f = PathFor(alias);
				if (File.Exists(f))
					return JsonSerializer.Deserialize<LoginPauseState>(File.ReadAllText(f)) ?? new LoginPauseState();
			} catch (Exception) {
			}
			return new LoginPauseState();
		}

		private static void Write(string alias, LoginPauseState s) {
			try {
				AtomicFile.WriteAllText(PathFor(alias), JsonSerializer.Serialize(s));
			} catch (Exception) {
			}
		}

		public static void Clear(string alias) {
			try {
				string f = PathFor(alias);
				if (File.Exists(f))
					File.Delete(f);
			} catch (Exception) {
			}
		}

		public static int PauseMinutes(int failures, LoginPauseConfig c) {
			if (failures < Math.Max(1, c.FailuresBeforePause))
				return 0;
			int stufe = failures - Math.Max(1, c.FailuresBeforePause);
			long min = Math.Max(1, c.PauseMinutes);
			for (int i = 0; i < stufe && min < c.MaxPauseMinutes; i++)
				min *= 2;
			return (int) Math.Min(min, Math.Max(c.PauseMinutes, c.MaxPauseMinutes));
		}

		public static DateTime? RegisterFailure(string alias, LoginPauseConfig c) {
			var s = Read(alias);
			s.Failures++;
			int min = PauseMinutes(s.Failures, c);
			s.UntilUtc = min > 0 ? DateTime.UtcNow.AddMinutes(min) : DateTime.MinValue;
			Write(alias, s);
			return min > 0 ? s.UntilUtc : null;
		}
	}
}
