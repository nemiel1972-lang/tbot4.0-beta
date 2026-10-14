using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using System.Threading;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers.Brain {
	public class LifeformsAutoMineWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		public LifeformsAutoMineWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOGameBridge,
			IWorkerFactory workerFactory) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOGameBridge;
			_workerFactory = workerFactory;
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "LifeformsAutoMine";
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.LifeformsAutoMine;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running Lifeform automine...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active)) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
					};

					List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialsToMine = new();
					List<ConstructionCandidate> constructionCandidates = new();
					LFBuildings maxLFBuildings = new();
					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						// same fix as Patch 18 (AutoMineWorker) - Exclude was only applied much
						// later, so every excluded planet still got touched here for the account-wide
						// planning phase, triggering OGame's "recent activity" indicator regardless of
						// LifeformAutoMine.Exclude. Skip excluded celestials before any data is fetched.
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Debug, $"Skipping {celestial.ToString()} in planning phase: celestial in exclude list.");
							continue;
						}
						var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);

						if ((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
							continue;
						}
						bool preventIfMoreExpensiveThanNextMine = (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.PreventIfMoreExpensiveThanNextMine;

						cel = await _tbotOgameBridge.UpdatePlanet(cel, UpdateTypes.LFBuildings);
						cel = await _tbotOgameBridge.UpdatePlanet(cel, UpdateTypes.Resources);
						cel = await _tbotOgameBridge.UpdatePlanet(cel, UpdateTypes.ResourcesProduction);
						maxLFBuildings = new();
						// Zielwerte (Rasse x Slot) kommen ausschliesslich aus lifeform_targets.json.
						// Die alten Max...-Felder der Config werden nicht mehr gelesen. Fehlt die Datei oder ist sie kaputt,
						// wird der LF-Bau fuer diesen Planeten uebersprungen (statt mit falschen Caps zu bauen).
						if (!LifeformTargets.TryApplyMaxLevels((object) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine, cel, maxLFBuildings, (lvl, msg) => DoLog(lvl, msg))) {
							DoLog(LogLevel.Warning, $"Skipping {cel.ToString()}: LF target values not available (lifeform_targets.json missing/invalid or race unknown).");
							continue;
						}
						var nextLFBuilding = _calculationService.GetNextLFBuildingToBuild(cel, maxLFBuildings, preventIfMoreExpensiveThanNextMine);
						if (nextLFBuilding != LFBuildables.None) {
							var lv = _calculationService.GetNextLevel(cel, nextLFBuilding);
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Lifeform building: {nextLFBuilding.ToString()} lv {lv.ToString()}.");
							constructionCandidates.Add(new ConstructionCandidate {
								CelestialId = cel.ID,
								Score = _calculationService.CalcPrice(
									nextLFBuilding,
									lv,
									_calculationService.CalcLFBuildingsResourcesCostBonus(cel),
									0,
									_calculationService.CalcLFBuildingsPopulationCostBonus(cel)
								).ConvertedDeuterium
							});
							celestialsToMine.Add(cel);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
						}
					}

					celestialsToMine = AccountConstructionPlanner.OrderCelestials(celestialsToMine, constructionCandidates).ToList();

					var dueTime = TimeSpan.Zero;
					Task previousExecution = Task.CompletedTask;
					foreach (Celestial celestial in celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialLifeformAutoMine, _tbotInstance, _tbotOgameBridge, celestial);
						celestialWorker.StartAfter(previousExecution);
						dueTime = OrderedWorkerSchedule.NextDueTime(dueTime, TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds)));
						await celestialWorker.StartWorker(new CancellationTokenSource().Token, dueTime);
						previousExecution = celestialWorker.FirstExecutionCompleted;
					}
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Lifeform AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}