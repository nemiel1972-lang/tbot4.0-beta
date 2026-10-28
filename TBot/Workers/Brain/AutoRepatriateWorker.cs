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
		private readonly TBot.Ogame.Infrastructure.IOgameService _ogameService;
		public AutoRepatriateWorker(ITBotMain parentInstance,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			TBot.Ogame.Infrastructure.IOgameService ogameService = null) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
			_ogameService = ogameService;
		}

		protected override async Task Execute() {			
			if (!_tbotInstance.UserData.isSleeping) {
				DoLog(LogLevel.Debug, "AutoRepatriate worker cycle starting.");
				if (_ogameService != null)
					await ExpoSupply.RunAsync(_tbotInstance, _calculationService, _ogameService, _tbotOgameBridge, _fleetScheduler, (lvl, msg) => DoLog(lvl, msg));
				var result = await _fleetScheduler.CollectImpl(false);
				DoLog(result == RepatriateCode.Failure ? LogLevel.Warning : LogLevel.Debug, $"AutoRepatriate worker cycle finished with result={result}.");
				if (result == RepatriateCode.Stop) {
					DoLog(LogLevel.Information, $"Stopping feature.");
					await EndExecution();
				} else {
					long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMax);
					DoLog(LogLevel.Debug, $"AutoRepatriate next interval baseline={interval}ms, result={result}.");
					if (result == RepatriateCode.Delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var flottenListe = _tbotInstance.UserData.fleets.ToList();
						var naechsteFlotte = flottenListe.Where(f => f.Mission == Missions.Transport && f.BackIn.HasValue).OrderBy(f => f.BackIn).FirstOrDefault()
							?? flottenListe.Where(f => f.BackIn.HasValue).OrderBy(f => f.BackIn).FirstOrDefault();
						interval = (naechsteFlotte?.BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					}
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
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