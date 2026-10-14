using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using TBot.Model;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {
	public interface ITBotMain {
		event EventHandler OnError;
		Task Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler);

		ValueTask DisposeAsync();

		dynamic InstanceSettings { get; }
		string InstanceAlias { get; }
		UserData UserData { get; set; }
		// true only while the expedition wave before sleeping is being sent (bypasses the bed-time check for expeditions)
		bool SleepWaveActive { get; set; }
		TelegramUserData TelegramUserData { get; }
		long SleepDuration { get; set; }
		DateTime NextWakeUpTime { get; set;}
		void log(LogLevel logLevel, LogSender sender, string format);
		Task InitializeFeature(Feature feat);
		Task StopFeature(Feature feat);
		bool IsFeatureRunning(Feature feat);
		Task SendTelegramMessage(string fmt, bool mirrorToDiscord = true);
		// PATCH Discord mirror: send only to the global Discord webhook; false if not active
		Task<bool> SendDiscordMessage(string fmt, NotificationKind kind = NotificationKind.Information);
		Task<bool> TelegramSwitch(decimal speed, Celestial attacked = null, bool fromTelegram = false);
		Task SleepNow(DateTime WakeUpTime);
		Task ListProfiles();
		Task LoadProfile(List<string> profileName);
		Task ResetProfile();

		// PATCH GhostSleep: von DefenderWorker aufgerufen (HandleAttack/Execute), wenn waehrend
		// GhostSleep-PASSIV ein echter Angriff erkannt wird (active=true, Eskalation auf AKTIV) bzw.
		// wenn keine Angriffswelle mehr aussteht (active=false, Deeskalation zurueck auf PASSIV mit
		// neuem Zufalls-Anker). Siehe TBotMain.cs, Abschnitt "GhostSleep", und
		// claude/ghostsleep-plan-adr.md. No-Op, wenn GhostSleep gerade gar nicht laeuft.
		void GhostSleepEscalate(bool active);

		// PATCH GhostSleep-Fix (Anker-Refresh, 28.09.2026, siehe DefenderWorker.cs/TBotMain.cs): von
		// DefenderWorker aufgerufen, sobald der zuletzt gesetzte GhostSleepNextAnchorUtc erreicht/
		// ueberschritten ist. Waehlt (falls PASSIV weiterhin aktiv ist) eine neue zufaellige rueckkehrende
		// Expedition als frischen Weck-Anker - ohne diesen Aufruf blieb der alte, jetzt vergangene Anker
		// stehen und der Worker feuerte praktisch im Sekundentakt weiter (Log-Spam, siehe
		// ghostsleep-plan-adr.md). No-Op, wenn GhostSleep gerade gar nicht laeuft oder kein Defender-Worker
		// gefunden wird.
		void GhostSleepRearmDefenderAnchor();

		// PATCH Angriffs-Lockdown (01.10.2026): stoppt (active=true) alle Worker ausser Defender/SleepMode bzw.
		// startet sie nach Ende der Angriffswelle wieder (active=false). Siehe TBotMain.cs.
		Task AttackLockdown(bool active);

		// PATCH Statuskachel: DefenderWorker meldet nach jedem Check die aktuelle Angriffsliste (leer = sicher),
		// damit die Kachel im Browser eingehende Angriffe anzeigen kann. fetchedUtc = Abrufzeitpunkt (ArriveIn gilt dazu).
		void PublishAttackSnapshot(List<AttackerFleet> attacks, DateTime fetchedUtc);
	}
}