using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TBot.Ogame.Infrastructure.Models;
using Tbot.Common.Settings;
using Tbot.Services;

namespace Tbot.Workers {
	public static class ExtraLog {
		private static readonly object _lock = new object();
		private const long MaxBytesProTag = 50L * 1024 * 1024;

		public static bool Aktiv(ITBotMain inst, string thema) {
			try {
				var g = inst?.InstanceSettings;
				if (g == null)
					return false;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "ExtraDebugLog"))
					return KnownTopic(thema);
				var x = g.Experimental.ExtraDebugLog;
				if (SettingsService.IsSettingSet(x, "Active") && !(bool) x.Active)
					return false;
				switch (thema) {
					case "Repatriate": return !SettingsService.IsSettingSet(x, "Repatriate") || (bool) x.Repatriate;
					case "Ledger": return !SettingsService.IsSettingSet(x, "Ledger") || (bool) x.Ledger;
					case "BotPause": return !SettingsService.IsSettingSet(x, "BotPause") || (bool) x.BotPause;
					case "DefenderRecheck": return !SettingsService.IsSettingSet(x, "DefenderRecheck") || (bool) x.DefenderRecheck;
					case "FleetsaveSlot": return !SettingsService.IsSettingSet(x, "FleetsaveSlot") || (bool) x.FleetsaveSlot;
					case "MinePlanRanking": return !SettingsService.IsSettingSet(x, "MinePlanRanking") || (bool) x.MinePlanRanking;
					case "AsteroidHunt": return !SettingsService.IsSettingSet(x, "AsteroidHunt") || (bool) x.AsteroidHunt;
					case "GalaxyCache": return !SettingsService.IsSettingSet(x, "GalaxyCache") || (bool) x.GalaxyCache;
					case "ShadowFleet": return !SettingsService.IsSettingSet(x, "ShadowFleet") || (bool) x.ShadowFleet;
					case "Requests": return !SettingsService.IsSettingSet(x, "Requests") || (bool) x.Requests;
					case "BrowserProxy": return !SettingsService.IsSettingSet(x, "BrowserProxy") || (bool) x.BrowserProxy;
					case "Directives": return !SettingsService.IsSettingSet(x, "Directives") || (bool) x.Directives;
					case "LFAutoMine": return !SettingsService.IsSettingSet(x, "LFAutoMine") || (bool) x.LFAutoMine;
					case "Harvest": return !SettingsService.IsSettingSet(x, "Harvest") || (bool) x.Harvest;
					case "FleetTargets": return !SettingsService.IsSettingSet(x, "FleetTargets") || (bool) x.FleetTargets;
					default: return false;
				}
			} catch (Exception) {
				return false;
			}
		}

		private static readonly Regex Mail = new Regex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+", RegexOptions.Compiled);
		private static readonly Regex Ip = new Regex(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);
		private static readonly Regex Koord = new Regex(@"\b([1-9]):(\d{1,3}):([1-9]\d?)\b", RegexOptions.Compiled);
		private static readonly Regex Token = new Regex(@"\b[A-Za-z0-9_\-:]{28,}\b", RegexOptions.Compiled);

		public static string Anonymisiere(ITBotMain inst, string text) {
			try {
				if (string.IsNullOrEmpty(text))
					return text;
				string spieler = inst?.UserData?.userInfo?.PlayerName;
				string universum = inst?.UserData?.serverData?.Name;
				if (!string.IsNullOrWhiteSpace(spieler))
					text = text.Replace(spieler, "Player");
				if (!string.IsNullOrWhiteSpace(universum))
					text = text.Replace(universum, "Universe");
				var namen = inst?.UserData?.celestials?.Select(c => c.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderByDescending(n => n.Length).ToList();
				if (namen != null)
					foreach (string n in namen)
						text = text.Replace(n + " [", "Celestial [");
				text = Koord.Replace(text, "x:x:$3");
				text = Mail.Replace(text, "<mail>");
				text = Ip.Replace(text, "<ip>");
				text = Token.Replace(text, "<token>");
			} catch (Exception) {
			}
			return text;
		}

		private static bool KnownTopic(string thema) =>
			thema == "Repatriate" || thema == "Ledger" || thema == "BotPause" || thema == "DefenderRecheck" || thema == "FleetsaveSlot"
			|| thema == "MinePlanRanking" || thema == "AsteroidHunt" || thema == "ShadowFleet" || thema == "GalaxyCache" || thema == "Requests" || thema == "BrowserProxy" || thema == "FleetTargets" || thema == "Directives" || thema == "LFAutoMine" || thema == "Harvest";

		private static DateTime _letzteAufraeumung = DateTime.MinValue;
		private static void RaeumeAuf(string dir) {
			if ((DateTime.Now - _letzteAufraeumung).TotalHours < 24)
				return;
			_letzteAufraeumung = DateTime.Now;
			try {
				foreach (string f in Directory.GetFiles(dir, "TBot-debug-*.log"))
					if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7))
						File.Delete(f);
			} catch (Exception) {
			}
		}

		public static bool Reduziert(ITBotMain inst) {
			bool standard = true;
			try {
				var g = inst?.InstanceSettings;
				if (g == null || !SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "ReduceLogs"))
					return standard;
				return (bool) g.Experimental.ReduceLogs;
			} catch (Exception) {
				return standard;
			}
		}

		public static bool Erzeugen(ITBotMain inst) => TBot.Common.Logging.CollectLogs.IsRunning && !Reduziert(inst);

		public static void Write(ITBotMain inst, string thema, Func<string> text) {
			try {
				if (!Erzeugen(inst))
					return;
				Write(inst, thema, text());
			} catch (Exception) {
			}
		}

		public static void Write(ITBotMain inst, string thema, string text) {
			try {
				TBot.Common.Logging.CollectLogs.WriteExtra(thema, text);
				if (!TBot.Common.Logging.CollectLogs.IsRunning || !Aktiv(inst, thema))
					return;
				string dir = Path.Combine(Directory.GetCurrentDirectory(), "devlogs");
				Directory.CreateDirectory(dir);
				RaeumeAuf(dir);
				string datei = Path.Combine(dir, $"TBot-debug-{DateTime.Now:yyyyMMdd}.log");
				lock (_lock) {
					if (File.Exists(datei) && new FileInfo(datei).Length > MaxBytesProTag)
						return;
					File.AppendAllText(datei, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{thema}] {(TBot.Common.Logging.CollectLogs.AnonymizeEnabled ? Anonymisiere(inst, text) : text)}{Environment.NewLine}");
				}
			} catch (Exception) {
			}
		}
	}
}
