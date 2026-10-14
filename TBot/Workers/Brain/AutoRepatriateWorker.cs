using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public class AutoRepatriateWorker : WorkerBase {
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoRepatriateWorker(ITBotMain parentInstance,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {			
			if (!_tbotInstance.UserData.isSleeping) {
				var result = await _fleetScheduler.CollectImpl(false);
				if (result == RepatriateCode.Stop) {
					DoLog(LogLevel.Information, $"Stopping feature.");
					await EndExecution();
				} else {
					long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMax);
					if (result == RepatriateCode.Delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						// PATCH Repatriate-Guard (11.10.2026): First() warf bei leerer Flottenliste eine Exception. Jetzt: naechste zurueckkehrende
						// Transport-Flotte (sie gibt Slots und Limit frei), sonst die naechste Flotte ueberhaupt, sonst kurze Pause.
						var flottenListe = _tbotInstance.UserData.fleets.ToList();
						var naechsteFlotte = flottenListe.Where(f => f.Mission == Missions.Transport && f.BackIn.HasValue).OrderBy(f => f.BackIn).FirstOrDefault()
							?? flottenListe.Where(f => f.BackIn.HasValue).OrderBy(f => f.BackIn).FirstOrDefault();
						interval = (naechsteFlotte?.BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					}
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					// PATCH Reverse-Repatriate (12.10.2026): while a pickup is running (outbound leg flying), the worker wakes up at the arrival
					// time (+2.5 to 4 s) so that the return leg starts right away. Without Reverse.Active the query returns -1 and nothing changes.
					try {
						long bisAnkunft = ReverseRepatriate.SekundenBisNaechsteAnkunft(_tbotInstance);
						if (bisAnkunft >= 0) {
							long ankunftMs = bisAnkunft * 1000 + 2500 + Random.Shared.Next(0, 1500);
							if (ankunftMs < interval)
								interval = ankunftMs;
						}
					} catch (Exception) {
					}
					var time = await _tbotOgameBridge.GetDateTime();
					var newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next repatriate check at {newTime.ToString()}");
				}
				await _tbotOgameBridge.CheckCelestials();
			}			
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active &&
					(bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoRepatriate";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoRepatriate;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoRepatriate;
		}
	}
}