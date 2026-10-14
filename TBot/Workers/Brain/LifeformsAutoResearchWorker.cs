using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using Microsoft.Extensions.Logging;
using System.Threading;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;

namespace Tbot.Workers.Brain {
	public class LifeformsAutoResearchWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public LifeformsAutoResearchWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			IWorkerFactory workerFactory) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
			_workerFactory = workerFactory;
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "LifeformsAutoResearch";
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoResearch;
		}

		public override LogSender GetLogSender() {
			return LogSender.LifeformsAutoResearch;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running Lifeform autoresearch...");

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)) {
					LFTechs maxLFTechs = new();
					maxLFTechs.IntergalacticEnvoys = maxLFTechs.VolcanicBatteries = maxLFTechs.CatalyserTechnology = maxLFTechs.HeatRecovery = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs11;
					maxLFTechs.HighPerformanceExtractors = maxLFTechs.AcousticScanning = maxLFTechs.PlasmaDrive = maxLFTechs.SulphideProcess = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs12;
					maxLFTechs.FusionDrives = maxLFTechs.HighEnergyPumpSystems = maxLFTechs.EfficiencyModule = maxLFTechs.PsionicNetwork = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs13;
					maxLFTechs.StealthFieldGenerator = maxLFTechs.CargoHoldExpansionCivilianShips = maxLFTechs.DepotAI = maxLFTechs.TelekineticTractorBeam = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs14;
					maxLFTechs.OrbitalDen = maxLFTechs.MagmaPoweredProduction = maxLFTechs.GeneralOverhaulLightFighter = maxLFTechs.EnhancedSensorTechnology = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs15;
					maxLFTechs.ResearchAI = maxLFTechs.GeothermalPowerPlants = maxLFTechs.AutomatedTransportLines = maxLFTechs.NeuromodalCompressor = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs16;
					maxLFTechs.HighPerformanceTerraformer = maxLFTechs.DepthSounding = maxLFTechs.ImprovedDroneAI = maxLFTechs.NeuroInterface = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs21;
					maxLFTechs.EnhancedProductionTechnologies = maxLFTechs.IonCrystalEnhancementHeavyFighter = maxLFTechs.ExperimentalRecyclingTechnology = maxLFTechs.InterplanetaryAnalysisNetwork = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs22;
					maxLFTechs.LightFighterMkII = maxLFTechs.ImprovedStellarator = maxLFTechs.GeneralOverhaulCruiser = maxLFTechs.OverclockingHeavyFighter = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs23;
					maxLFTechs.CruiserMkII = maxLFTechs.HardenedDiamondDrillHeads = maxLFTechs.SlingshotAutopilot = maxLFTechs.TelekineticDrive = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs24;
					maxLFTechs.ImprovedLabTechnology = maxLFTechs.SeismicMiningTechnology = maxLFTechs.HighTemperatureSuperconductors = maxLFTechs.SixthSense = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs25;
					maxLFTechs.PlasmaTerraformer = maxLFTechs.MagmaPoweredPumpSystems = maxLFTechs.GeneralOverhaulBattleship = maxLFTechs.Psychoharmoniser = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs26;
					maxLFTechs.LowTemperatureDrives = maxLFTechs.IonCrystalModules = maxLFTechs.ArtificialSwarmIntelligence = maxLFTechs.EfficientSwarmIntelligence = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs31;
					maxLFTechs.BomberMkII = maxLFTechs.OptimisedSiloConstructionMethod = maxLFTechs.GeneralOverhaulBattlecruiser = maxLFTechs.OverclockingLargeCargo = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs32;
					maxLFTechs.DestroyerMkII = maxLFTechs.DiamondEnergyTransmitter = maxLFTechs.GeneralOverhaulBomber = maxLFTechs.GravitationSensors = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs33;
					maxLFTechs.BattlecruiserMkII = maxLFTechs.ObsidianShieldReinforcement = maxLFTechs.GeneralOverhaulDestroyer = maxLFTechs.OverclockingBattleship = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs34;
					maxLFTechs.RobotAssistants = maxLFTechs.RuneShields = maxLFTechs.ExperimentalWeaponsTechnology = maxLFTechs.PsionicShieldMatrix = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs35;
					maxLFTechs.Supercomputer = maxLFTechs.RocktalCollectorEnhancement = maxLFTechs.MechanGeneralEnhancement = maxLFTechs.KaeleshDiscovererEnhancement = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs36;
					int maxResearchLevel = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel;
					List<int> priorityTechs = ReadPriorityTechs();
					
					List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialsToMine = new();
					List<ConstructionCandidate> constructionCandidates = new();

					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Debug, $"Skipping {celestial.ToString()} in planning phase: celestial in exclude list.");
							continue;
						}
						var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
						cel = await _tbotOgameBridge.UpdatePlanet(cel, UpdateTypes.LFTechs);
						cel = await _tbotOgameBridge.UpdatePlanet(cel, UpdateTypes.Resources);

						if (cel.LFtype == LFTypes.None) {
							DoLog(LogLevel.Information, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
							continue;
						}
						var nextLFTechToBuild = PickNextLFTech(cel, maxLFTechs, priorityTechs, out int level);
						if (nextLFTechToBuild != LFTechno.None) {

							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
							constructionCandidates.Add(new ConstructionCandidate {
								CelestialId = cel.ID,
								Score = _calculationService.CalcPrice(nextLFTechToBuild, level).ConvertedDeuterium
							});
							celestialsToMine.Add(cel);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform technology to build found. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
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
						var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialLifeformAutoResearch, _tbotInstance, _tbotOgameBridge, celestial);
						celestialWorker.StartAfter(previousExecution);
						dueTime = OrderedWorkerSchedule.NextDueTime(dueTime, TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds)));
						await celestialWorker.StartWorker(new CancellationTokenSource().Token, dueTime);
						previousExecution = celestialWorker.FirstExecutionCompleted;
					}
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Lifeform AutoResearch Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}

		private List<int> ReadPriorityTechs() {
			List<int> result = new();
			try {
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "PriorityTechs")) {
					foreach (var entry in _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.PriorityTechs) {
						int slot = (int) entry;
						if (!result.Contains(slot)) {
							result.Add(slot);
						}
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"PriorityTechs could not be read ({e.Message}), the normal order applies.");
				result.Clear();
			}
			return result;
		}

		private LFTechno PickNextLFTech(Celestial cel, LFTechs maxLFTechs, List<int> priorityTechs, out int level) {
			level = 0;
			LFTechs existing = RestrictToExisting(cel, maxLFTechs);
			foreach (int slot in priorityTechs) {
				if (!PrioSlotTechNames.ContainsKey(slot)) {
					DoLog(LogLevel.Warning, $"PriorityTechs: invalid slot number {slot} skipped (allowed: 11-16, 21-26, 31-36).");
					continue;
				}
				LFTechs stageMax = BuildStageMax(slot, existing, out string[] slotNames);
				if (stageMax == null) {
					continue;
				}
				LFTechno next = _calculationService.GetNextLFTechToBuild(cel, stageMax);
				if (next == LFTechno.None) {
					continue;
				}
				int nextLevel = _calculationService.GetNextLevel(cel, next);
				Resources nextCost = _calculationService.CalcPrice(next, nextLevel);
				LFTechno cheaper = _calculationService.GetLessExpensiveLFTechToBuild(cel, nextCost, stageMax);
				if (cheaper != LFTechno.None) {
					nextLevel = _calculationService.GetNextLevel(cel, cheaper);
					next = cheaper;
				}
				if (!slotNames.Contains(next.ToString())) {
					DoLog(LogLevel.Warning, $"PriorityTechs: slot {slot} returned {next.ToString()}, which does not belong to this slot. Priority for this slot ignored.");
					continue;
				}
				level = nextLevel;
				DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Priority research slot {slot}: {next.ToString()} lv {level.ToString()}.");
				return next;
			}

			LFTechno normal = _calculationService.GetNextLFTechToBuild(cel, existing);
			if (normal != LFTechno.None) {
				level = _calculationService.GetNextLevel(cel, normal);
				Resources normalCost = _calculationService.CalcPrice(normal, level);
				LFTechno cheaperNormal = _calculationService.GetLessExpensiveLFTechToBuild(cel, normalCost, existing);
				if (cheaperNormal != LFTechno.None) {
					level = _calculationService.GetNextLevel(cel, cheaperNormal);
					normal = cheaperNormal;
				}
			}
			return normal;
		}

		private static readonly Dictionary<int, string[]> PrioSlotTechNames = new() {
			{ 11, new[] { "IntergalacticEnvoys", "VolcanicBatteries", "CatalyserTechnology", "HeatRecovery" } },
			{ 12, new[] { "HighPerformanceExtractors", "AcousticScanning", "PlasmaDrive", "SulphideProcess" } },
			{ 13, new[] { "FusionDrives", "HighEnergyPumpSystems", "EfficiencyModule", "PsionicNetwork" } },
			{ 14, new[] { "StealthFieldGenerator", "CargoHoldExpansionCivilianShips", "DepotAI", "TelekineticTractorBeam" } },
			{ 15, new[] { "OrbitalDen", "MagmaPoweredProduction", "GeneralOverhaulLightFighter", "EnhancedSensorTechnology" } },
			{ 16, new[] { "ResearchAI", "GeothermalPowerPlants", "AutomatedTransportLines", "NeuromodalCompressor" } },
			{ 21, new[] { "HighPerformanceTerraformer", "DepthSounding", "ImprovedDroneAI", "NeuroInterface" } },
			{ 22, new[] { "EnhancedProductionTechnologies", "IonCrystalEnhancementHeavyFighter", "ExperimentalRecyclingTechnology", "InterplanetaryAnalysisNetwork" } },
			{ 23, new[] { "LightFighterMkII", "ImprovedStellarator", "GeneralOverhaulCruiser", "OverclockingHeavyFighter" } },
			{ 24, new[] { "CruiserMkII", "HardenedDiamondDrillHeads", "SlingshotAutopilot", "TelekineticDrive" } },
			{ 25, new[] { "ImprovedLabTechnology", "SeismicMiningTechnology", "HighTemperatureSuperconductors", "SixthSense" } },
			{ 26, new[] { "PlasmaTerraformer", "MagmaPoweredPumpSystems", "GeneralOverhaulBattleship", "Psychoharmoniser" } },
			{ 31, new[] { "LowTemperatureDrives", "IonCrystalModules", "ArtificialSwarmIntelligence", "EfficientSwarmIntelligence" } },
			{ 32, new[] { "BomberMkII", "OptimisedSiloConstructionMethod", "GeneralOverhaulBattlecruiser", "OverclockingLargeCargo" } },
			{ 33, new[] { "DestroyerMkII", "DiamondEnergyTransmitter", "GeneralOverhaulBomber", "GravitationSensors" } },
			{ 34, new[] { "BattlecruiserMkII", "ObsidianShieldReinforcement", "GeneralOverhaulDestroyer", "OverclockingBattleship" } },
			{ 35, new[] { "RobotAssistants", "RuneShields", "ExperimentalWeaponsTechnology", "PsionicShieldMatrix" } },
			{ 36, new[] { "Supercomputer", "RocktalCollectorEnhancement", "MechanGeneralEnhancement", "KaeleshDiscovererEnhancement" } },
		};

		private LFTechs RestrictToExisting(Celestial cel, LFTechs full) {
			LFTechs result = new();
			foreach (string name in PrioSlotTechNames.Values.SelectMany(n => n)) {
				if (!Enum.TryParse(name, out LFTechno tech) || cel.GetLevel(tech) < 1) {
					continue;
				}
				var prop = typeof(LFTechs).GetProperty(name);
				if (prop != null) {
					prop.SetValue(result, prop.GetValue(full));
					continue;
				}
				typeof(LFTechs).GetField(name)?.SetValue(result, typeof(LFTechs).GetField(name).GetValue(full));
			}
			return result;
		}

		private static LFTechs BuildStageMax(int slot, LFTechs full, out string[] slotNames) {
			slotNames = new string[0];
			if (!PrioSlotTechNames.TryGetValue(slot, out string[] names)) {
				return null;
			}
			LFTechs s = new();
			foreach (string name in names) {
				var prop = typeof(LFTechs).GetProperty(name);
				if (prop != null) {
					prop.SetValue(s, prop.GetValue(full));
					continue;
				}
				var field = typeof(LFTechs).GetField(name);
				if (field != null) {
					field.SetValue(s, field.GetValue(full));
				}
			}
			slotNames = names;
			return s;
		}
	}
}