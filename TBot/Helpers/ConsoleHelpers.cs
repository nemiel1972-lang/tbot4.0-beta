using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tbot.Helpers {
	public static class ConsoleHelpers {
		public static void SetTitle(string content = "") {
			AssemblyName exeInfo = Assembly.GetExecutingAssembly().GetName();
			string info = $"{exeInfo.Name} v{exeInfo.Version}";
			Console.Title = (content != "") ? $"{content} - {info}" : info;
			return;
		}

		public static bool TryPlaySound(string pfad) {
			try {
				if (string.IsNullOrWhiteSpace(pfad) || !File.Exists(pfad))
					return false;
				if (OperatingSystem.IsWindows()) {
					SpieleWindows(pfad);
					return true;
				}
				string[] player = OperatingSystem.IsMacOS() ? new[] { "afplay" } : new[] { "paplay", "aplay" };
				foreach (string exe in player) {
					try {
						var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
						psi.ArgumentList.Add(pfad);
						using Process p = Process.Start(psi);
						if (p == null)
							continue;
						if (!p.WaitForExit(30000)) {
							try { p.Kill(); } catch (Exception) { }
						}
						if (p.HasExited && p.ExitCode == 0)
							return true;
					} catch (Exception) {
					}
				}
			} catch (Exception) {
			}
			return false;
		}

		private static void SpieleWindows(string pfad) {
			Type typ = Type.GetType("System.Media.SoundPlayer, System.Windows.Extensions")
				?? throw new InvalidOperationException("SoundPlayer not available");
			object sp = Activator.CreateInstance(typ, pfad);
			try {
				typ.GetMethod("PlaySync", Type.EmptyTypes).Invoke(sp, null);
			} finally {
				(sp as IDisposable)?.Dispose();
			}
		}

		public static void PlayAlarm(string soundFile) {
			if (TryPlaySound(soundFile))
				return;
			PlayAlarm();
		}

		public static void PlayAlarm() {
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			return;
		}
	}
}
