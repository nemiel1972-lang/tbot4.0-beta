using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public class AutoMineCelestialWorker : CelestialWorkerBase {

		private readonly ICalculationService _calculationService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		private Buildables _pendingFundedBuildable = Buildables.Null;
		private int _pendingFundedLevel = 0;
		public AutoMineCelestialWorker(ITBotMain parentInstance,
			ITBotWorker parentWorker,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			Celestial celestial) :
			base(parentInstance, parentWorker, celestial) {
			_calculationService = calculationService;
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		private Buildings GetMaxBuildingsForCelestial(Celestial cel) {
			bool isHomeWorld =
				cel.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Galaxy &&
				cel.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.System &&
				cel.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Position &&
				cel.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Type);

			bool colonyConfigured = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "Colony");

			int maxMetalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMetalMine;
			int maxCrystalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxCrystalMine;
			int maxDeuteriumSynthetizer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDeuteriumSynthetizer;
			int maxSolarPlant = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSolarPlant;
			int maxFusionReactor = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxFusionReactor;
			int maxMetalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMetalStorage;
			int maxCrystalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxCrystalStorage;
			int maxDeuteriumTank = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDeuteriumTank;

			if (!isHomeWorld && colonyConfigured) {
				maxMetalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxMetalMine;
				maxCrystalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxCrystalMine;
				maxDeuteriumSynthetizer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxDeuteriumSynthetizer;
				maxSolarPlant = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxSolarPlant;
				maxFusionReactor = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxFusionReactor;
				maxMetalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxMetalStorage;
				maxCrystalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxCrystalStorage;
				maxDeuteriumTank = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxDeuteriumTank;
			}

			if (!isHomeWorld) {
				dynamic colonyOverride = AutoMineOverrideHelper.FindOverride(cel, _tbotInstance.InstanceSettings.Brain.AutoMine);
				if (colonyOverride != null) {
					maxMetalMine = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxMetalMine", maxMetalMine);
					maxCrystalMine = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxCrystalMine", maxCrystalMine);
					maxDeuteriumSynthetizer = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxDeuteriumSynthetizer", maxDeuteriumSynthetizer);
					maxSolarPlant = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxSolarPlant", maxSolarPlant);
					maxFusionReactor = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxFusionReactor", maxFusionReactor);
					maxMetalStorage = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxMetalStorage", maxMetalStorage);
					maxCrystalStorage = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxCrystalStorage", maxCrystalStorage);
					maxDeuteriumTank = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxDeuteriumTank", maxDeuteriumTank);
					DoLog(LogLevel.Information, $"ColonyOverride applied for {cel}: MetalMine={maxMetalMine}, CrystalMine={maxCrystalMine}, DeuteriumSynthesizer={maxDeuteriumSynthetizer}, SolarPlant={maxSolarPlant}, FusionReactor={maxFusionReactor}, MetalStorage={maxMetalStorage}, CrystalStorage={maxCrystalStorage}, DeuteriumTank={maxDeuteriumTank}");
				}
			}

			return new() {
				MetalMine = maxMetalMine,
				CrystalMine = maxCrystalMine,
				DeuteriumSynthesizer = maxDeuteriumSynthetizer,
				SolarPlant = maxSolarPlant,
				FusionReactor = maxFusionReactor,
				MetalStorage = maxMetalStorage,
				CrystalStorage = maxCrystalStorage,
				DeuteriumTank = maxDeuteriumTank
			};
		}

		private Facilities GetMaxFacilitiesForCelestial(Celestial cel) {
			bool isHomeWorld =
				cel.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Galaxy &&
				cel.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.System &&
				cel.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Position &&
				cel.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.HomeWorld.Type);

			bool colonyConfigured = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "Colony");

			int maxRoboticsFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxRoboticsFactory;
			int maxShipyard = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxShipyard;
			int maxResearchLab = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxResearchLab;
			int maxMissileSilo = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMissileSilo;
			int maxNaniteFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxNaniteFactory;
			int maxTerraformer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxTerraformer;
			int maxSpaceDock = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSpaceDock;

			if (!isHomeWorld && colonyConfigured) {
				maxRoboticsFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxRoboticsFactory;
				maxShipyard = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxShipyard;
				maxNaniteFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxNaniteFactory;
				maxResearchLab = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxResearchLab;
				maxMissileSilo = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxMissileSilo;
				maxTerraformer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxTerraformer;
				maxSpaceDock = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Colony.MaxSpaceDock;
			}

			if (!isHomeWorld) {
				dynamic colonyOverride = AutoMineOverrideHelper.FindOverride(cel, _tbotInstance.InstanceSettings.Brain.AutoMine);
				if (colonyOverride != null) {
					maxRoboticsFactory = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxRoboticsFactory", maxRoboticsFactory);
					maxShipyard = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxShipyard", maxShipyard);
					maxResearchLab = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxResearchLab", maxResearchLab);
					maxMissileSilo = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxMissileSilo", maxMissileSilo);
					maxNaniteFactory = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxNaniteFactory", maxNaniteFactory);
					maxTerraformer = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxTerraformer", maxTerraformer);
					maxSpaceDock = AutoMineOverrideHelper.Resolve(colonyOverride, _tbotInstance.InstanceSettings.Brain.AutoMine, "MaxSpaceDock", maxSpaceDock);
					DoLog(LogLevel.Information, $"ColonyOverride applied for {cel}: RoboticsFactory={maxRoboticsFactory}, Shipyard={maxShipyard}, ResearchLab={maxResearchLab}, MissileSilo={maxMissileSilo}, NaniteFactory={maxNaniteFactory}, Terraformer={maxTerraformer}, SpaceDock={maxSpaceDock}");
				}
			}

			return new() {
				RoboticsFactory = maxRoboticsFactory,
				Shipyard = maxShipyard,
				ResearchLab = maxResearchLab,
				MissileSilo = maxMissileSilo,
				NaniteFactory = maxNaniteFactory,
				Terraformer = maxTerraformer,
				SpaceDock = maxSpaceDock
			};
		}

		private int RundeSatelliten(int bedarf) {
			if (bedarf <= 0)
				return bedarf;
			int bund = 20;
			try {
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "SolarSatelliteBatch"))
					bund = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.SolarSatelliteBatch;
			} catch (Exception) {
				bund = 20;
			}
			if (bund <= 1)
				return bedarf;
			return (int) (((long) bedarf + bund - 1) / bund * bund);
		}

		private (Buildables, int) EnergieVorabbau(Planet planet, Buildings maxBuildings, bool satelliten, Buildables buildable, int level) {
			try {
				if (buildable == Buildables.Null || buildable == Buildables.SolarPlant || buildable == Buildables.FusionReactor || buildable == Buildables.SolarSatellite)
					return (buildable, level);
				bool istMine = buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer;
				long delta = istMine ? _calculationService.GetRequiredEnergyDelta(buildable, level) : _calculationService.CalcPrice(buildable, level, planet.LFBonuses).Energy;
				if (delta <= 0)
					return (buildable, level);
				long frei = planet.ResourcesProduction.Energy.Available;
				int proSat = _calculationService.GetSolarSatelliteOutput(planet, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
				long inProduktion = 0;
				foreach (Production p in planet.Productions) {
					if (p.ID == (int) Buildables.SolarSatellite)
						inProduktion += (long) p.Nbr * proSat;
				}
				long fehlt = delta - frei - inProduktion;
				if (fehlt <= 0)
					return (buildable, level);
				int prozent = 10;
				if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "EnergyReservePercent"))
					prozent = Math.Max(0, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.EnergyReservePercent);
				long ziel = (long) Math.Ceiling(fehlt * (1.0 + prozent / 100.0));
				Buildables quelle = _calculationService.GetNextEnergySourceToBuild(planet, maxBuildings.SolarPlant, maxBuildings.FusionReactor, satelliten);
				if (quelle == Buildables.SolarSatellite) {
					int bedarf = _calculationService.CalcNeededSolarSatellites(planet, ziel, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					DoLog(LogLevel.Information, $"Energy pre-build on {planet.ToString()}: {buildable.ToString()} level {level.ToString()} needs {delta:N0} energy, free {frei:N0} (+{inProduktion:N0} in production) - building solar satellites first (target +{prozent}%).");
					return (Buildables.SolarSatellite, RundeSatelliten(bedarf));
				}
				if (quelle == Buildables.SolarPlant || quelle == Buildables.FusionReactor) {
					DoLog(LogLevel.Information, $"Energy pre-build on {planet.ToString()}: {buildable.ToString()} level {level.ToString()} needs {delta:N0} energy, free {frei:N0} - building {quelle.ToString()} first.");
					return (quelle, _calculationService.GetNextLevel(planet, quelle));
				}
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Energy pre-build check failed: {e.Message}");
			}
			return (buildable, level);
		}

		private int GetCrawlerAmountToBuild(Planet planet) {
			bool fixedTargetSet = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "FixedCrawlerTarget") && (int) _tbotInstance.InstanceSettings.Brain.AutoMine.FixedCrawlerTarget > 0;
			if (fixedTargetSet) {
				int fixedTarget = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.FixedCrawlerTarget;
				int maxCrawlers = _calculationService.CalcMaxCrawlers(planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist);
				int target = Math.Min(fixedTarget, maxCrawlers);
				return Math.Max(0, target - (int) planet.Ships.Crawler);
			}
			return _calculationService.CalcOptimalCrawlers(planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData);
		}

		private int GetCapForBuildable(Buildables b, Buildings maxBuildings, Facilities maxFacilities) {
			switch (b) {
				case Buildables.MetalMine: return maxBuildings.MetalMine;
				case Buildables.CrystalMine: return maxBuildings.CrystalMine;
				case Buildables.DeuteriumSynthesizer: return maxBuildings.DeuteriumSynthesizer;
				case Buildables.SolarPlant: return maxBuildings.SolarPlant;
				case Buildables.FusionReactor: return maxBuildings.FusionReactor;
				case Buildables.MetalStorage: return maxBuildings.MetalStorage;
				case Buildables.CrystalStorage: return maxBuildings.CrystalStorage;
				case Buildables.DeuteriumTank: return maxBuildings.DeuteriumTank;
				case Buildables.RoboticsFactory: return maxFacilities.RoboticsFactory;
				case Buildables.Shipyard: return maxFacilities.Shipyard;
				case Buildables.ResearchLab: return maxFacilities.ResearchLab;
				case Buildables.MissileSilo: return maxFacilities.MissileSilo;
				case Buildables.NaniteFactory: return maxFacilities.NaniteFactory;
				case Buildables.Terraformer: return maxFacilities.Terraformer;
				case Buildables.SpaceDock: return maxFacilities.SpaceDock;
				default: return int.MaxValue;
			}
		}

		private bool KannVollstaendigLiefern(Celestial herkunft, Ships schiffe, Coordinate ziel, Resources menge, Resources zuruecklassen) {
			try {
				long mengeMetall = menge.Metal;
				long mengeKristall = menge.Crystal;
				long mengeDeut = menge.Deuterium;
				var vorhersage = _calculationService.CalcFleetPrediction(herkunft.Coordinate, ziel, schiffe, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, herkunft.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
				long sprit = (long) vorhersage.Fuel;
				bool okMetall = herkunft.Resources.Metal >= mengeMetall + zuruecklassen.Metal;
				bool okKristall = herkunft.Resources.Crystal >= mengeKristall + zuruecklassen.Crystal;
				bool okDeut = herkunft.Resources.Deuterium >= mengeDeut + sprit + zuruecklassen.Deuterium;
				Resources passt = _calculationService.CalcMaxTransportableResources(schiffe, new Resources { Metal = mengeMetall, Crystal = mengeKristall, Deuterium = mengeDeut }, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, herkunft.LFBonuses, _tbotInstance.UserData.userInfo.Class, 0, _tbotInstance.UserData.serverData.ProbeCargo);
				bool okLaderaum = passt.Metal >= mengeMetall && passt.Crystal >= mengeKristall && passt.Deuterium >= mengeDeut;
				if (!(okMetall && okKristall && okDeut && okLaderaum)) {
					DoLog(LogLevel.Debug, $"Partial delivery check {herkunft.ToString()}: metal {okMetall}, crystal {okKristall}, deuterium incl. fuel ({sprit}) {okDeut}, cargo space {okLaderaum}");
					return false;
				}
				return true;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Partial delivery check failed ({e.Message}) - nothing is sent.");
				return false;
			}
		}
		
		public override bool IsWorkerEnabledBySettings() {
			try {
				return ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active);
			} catch (Exception) {
				return false;
			}
		}

		public override string GetWorkerName() {
			return "AutoMine-" + celestial.ToString();
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoMine;
		}

		protected override async Task Execute() {
			try {
				Buildings maxBuildings = GetMaxBuildingsForCelestial(celestial);
				Facilities maxFacilities = GetMaxFacilitiesForCelestial(celestial);
				Facilities maxLunarFacilities = new() {
					LunarBase = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarBase,
					RoboticsFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarRoboticsFactory,
					SensorPhalanx = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSensorPhalanx,
					JumpGate = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxJumpGate,
					Shipyard = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarShipyard
				};
				AutoMinerSettings autoMinerSettings = new() {
					OptimizeForStart = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.OptimizeForStart,
					PrioritizeRobotsAndNanites = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites,
					MaxDaysOfInvestmentReturn = (float) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDaysOfInvestmentReturn,
					DepositHours = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DepositHours,
					BuildDepositIfFull = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildDepositIfFull,
					DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons,
					BuildSolarSatellites = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildSolarSatellites
				};
				Fields fieldsSettings = new() {
					Total = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinFields
				};
				Temperature temperaturesSettings = new() {
					Min = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinTemperatureAcceptable,
					Max = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MaxTemperatureAcceptable
				};

				await AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings, fieldsSettings, temperaturesSettings);

			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
		private async Task AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings, Fields fieldsSettings, Temperature temperaturesSettings) {
			int fleetId = (int) SendFleetCode.GenericError;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			long delayBuilding = 0;
			bool delayProduction = false;
			try {
				DoLog(LogLevel.Information, $"Running AutoMine on {celestial.ToString()}");
				LedgerTBot.SetContext("AutoMine", celestial.ID);
				await LedgerTBot.SyncFleets(_tbotInstance, _fleetScheduler);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourceSettings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
				Planet abaCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast) as Planet;
				_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();
				if (
					(!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "BuildCrawlers") || (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildCrawlers) &&
					celestial.Coordinate.Type == Celestials.Planet &&
					celestial.Resources.Energy >= 0 &&
					_tbotInstance.UserData.userInfo.Class == CharacterClass.Collector &&
					celestial.Facilities.Shipyard >= 5 &&
					_tbotInstance.UserData.researches.CombustionDrive >= 4 &&
					_tbotInstance.UserData.researches.ArmourTechnology >= 4 &&
					_tbotInstance.UserData.researches.LaserTechnology >= 4 &&
					!celestial.Productions.Any(p => p.ID == (int) Buildables.Crawler) &&
					celestial.Constructions.BuildingID != (int) Buildables.Shipyard &&
					celestial.Constructions.BuildingID != (int) Buildables.NaniteFactory &&
					celestial.Ships.Crawler < _calculationService.CalcMaxCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist) &&
					GetCrawlerAmountToBuild(celestial as Planet) > 0
				) {
					buildable = Buildables.Crawler;
					level = GetCrawlerAmountToBuild(celestial as Planet);
				} else {
					if (celestial.Fields.Free == 0) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: not enough fields available.");
						return;
					}
					int homePlanetId = _calculationService.GetHomePlanetId(_tbotInstance.UserData.celestials);
					if (celestial.ID != homePlanetId && _tbotInstance.UserData.celestials.Count > 1 && celestial.Coordinate.Type == Celestials.Planet && celestial.Fields.Built == 0 && (bool) _tbotInstance.InstanceSettings.AutoColonize.Abandon.Active) {
						if (abaCelestial != null && _calculationService.ShouldAbandon(celestial as Planet, celestial.Fields.Total, abaCelestial.Temperature.Max, fieldsSettings, temperaturesSettings)) {
							DoLog(LogLevel.Debug, $"Skipping {celestial.ToString()}: planet should be abandoned.");
							return;
						}
					}
					if (celestial.Constructions.BuildingID != 0) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: there is already a building in production.");
						if (
							celestial is Planet && (
								celestial.Constructions.BuildingID == (int) Buildables.MetalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.CrystalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.DeuteriumSynthesizer
							)
						) {
							var buildingBeingBuilt = (Buildables) celestial.Constructions.BuildingID;

							var levelBeingBuilt = _calculationService.GetNextLevel(celestial, buildingBeingBuilt);
							var DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildingBeingBuilt, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR > _tbotInstance.UserData.lastDOIR) {
								_tbotInstance.UserData.lastDOIR = DOIR;
							}
						}
						delayBuilding = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						return;
					}

					if (celestial is Planet) {
						buildable = _calculationService.GetNextBuildingToBuild(celestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings);
						level = _calculationService.GetNextLevel(celestial as Planet, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
						if (buildable == Buildables.SolarSatellite)
							level = RundeSatelliten(level);

						if (_pendingFundedBuildable != Buildables.Null) {
							int pendingCurrentLevel = celestial.GetLevel(_pendingFundedBuildable);
							if (pendingCurrentLevel + 1 != _pendingFundedLevel) {
								_pendingFundedBuildable = Buildables.Null;
								_pendingFundedLevel = 0;
								EnergyFundingTracker.Clear(celestial.ID);
							} else if (_pendingFundedLevel > GetCapForBuildable(_pendingFundedBuildable, maxBuildings, maxFacilities)) {
								DoLog(LogLevel.Information, $"Discarding previously funded {_pendingFundedBuildable.ToString()} level {_pendingFundedLevel.ToString()} on {celestial.ToString()}: exceeds current cap.");
								_pendingFundedBuildable = Buildables.Null;
								_pendingFundedLevel = 0;
								EnergyFundingTracker.Clear(celestial.ID);
							} else {
								bool pendingIsEnergySource = _pendingFundedBuildable == Buildables.SolarPlant || _pendingFundedBuildable == Buildables.FusionReactor;
								bool energyDeficit = !pendingIsEnergySource && _calculationService.ShouldBuildEnergySource(celestial as Planet);
								if (energyDeficit) {
									DoLog(LogLevel.Information, $"Deferring previously funded {_pendingFundedBuildable.ToString()} level {_pendingFundedLevel.ToString()} on {celestial.ToString()}: energy deficit takes priority.");
								} else {
									Resources pendingCost = _calculationService.CalcPrice(_pendingFundedBuildable, _pendingFundedLevel, celestial.LFBonuses);
									if (celestial.Resources.IsEnoughFor(pendingCost) && LedgerTBot.CanAfford(_tbotInstance, celestial, "AutoMine", pendingCost)) {
										DoLog(LogLevel.Information, $"Prioritizing previously funded {_pendingFundedBuildable.ToString()} level {_pendingFundedLevel.ToString()} on {celestial.ToString()} over freshly computed {buildable.ToString()}.");
										buildable = _pendingFundedBuildable;
										level = _pendingFundedLevel;
									}
								}
							}
						}
						if (_pendingFundedBuildable == Buildables.Null || buildable != _pendingFundedBuildable)
							(buildable, level) = EnergieVorabbau(celestial as Planet, maxBuildings, (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildSolarSatellites, buildable, level);
					} else {
						buildable = _calculationService.GetNextLunarFacilityToBuild(celestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
						level = _calculationService.GetNextLevel(celestial as Moon, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					}
				}

				if (buildable != Buildables.Null && level > 0) {
					DoLog(LogLevel.Information, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, $"Days of investment return: {Math.Round(DOIR, 2).ToString()} days.");
					}

					Resources xCostBuildable = _calculationService.CalcPrice(buildable, level, celestial.LFBonuses);
					float ledgerRoi = 0f;
					if (celestial is Planet && (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer)) {
						try {
							ledgerRoi = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (float.IsNaN(ledgerRoi) || float.IsInfinity(ledgerRoi) || ledgerRoi < 0f)
								ledgerRoi = 0f;
						} catch {
							ledgerRoi = 0f;
						}
					}
					if (celestial is Moon)
						xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;

					if (buildable == Buildables.Terraformer) {
						if (xCostBuildable.Energy > celestial.ResourcesProduction.Energy.CurrentProduction) {
							DoLog(LogLevel.Information, $"Not enough energy to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildSolarSatellites) {
								buildable = Buildables.SolarSatellite;
								level = _calculationService.CalcNeededSolarSatellites(celestial as Planet, xCostBuildable.Energy - celestial.ResourcesProduction.Energy.CurrentProduction, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
								level = RundeSatelliten(level);
								xCostBuildable = _calculationService.CalcPrice(buildable, level, celestial.LFBonuses);
							} else {
								DoLog(LogLevel.Information, $"Unable to build SolarSatellites for Terraformer. Stopping AutoMiner for celestial {celestial.ToString()}");
								stop = true;
								return;
							}
						}
					}

					if (celestial.Resources.IsEnoughFor(xCostBuildable) && LedgerTBot.CanAfford(_tbotInstance, celestial, "AutoMine", xCostBuildable) && buildable != Buildables.Null) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							if (!celestial.HasProduction()) {
								DoLog(LogLevel.Information, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								try {
									await _ogameService.BuildShips(celestial, buildable, level);
									result = true;
								} catch { }
							} else {
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
								delayProduction = true;
							}
						} else {
							DoLog(LogLevel.Information, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _ogameService.BuildConstruction(celestial, buildable);
								result = true;
							} catch { }
						}

						if (result) {
						LedgerTBot.OnBuilt(_tbotInstance, celestial, "AutoMine", buildable.ToString() + " " + level.ToString(), xCostBuildable);
							if (buildable == _pendingFundedBuildable && level == _pendingFundedLevel) {
								_pendingFundedBuildable = Buildables.Null;
								_pendingFundedLevel = 0;
							}
							EnergyFundingTracker.Clear(celestial.ID);
							if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
								float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
								if (DOIR > _tbotInstance.UserData.lastDOIR) {
									_tbotInstance.UserData.lastDOIR = DOIR;
								}
							}
							if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
								celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
								try {
									if (celestial.Productions.First().ID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
									} else {
										celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
										if (celestial.Resources.Energy >= 0) {
											started = true;
											DoLog(LogLevel.Information, $"{level.ToString()}x {buildable.ToString()} succesfully built");
										} else {
											DoLog(LogLevel.Warning, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknown error has occurred");
										}
									}
								} catch {
									started = true;
									DoLog(LogLevel.Information, $"Unable to determine if the production has started.");
								}
							} else {
								celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									DoLog(LogLevel.Information, "Building succesfully started.");
								} else {
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
									if (celestial.GetLevel(buildable) != level)
										DoLog(LogLevel.Warning, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										DoLog(LogLevel.Information, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler)
							DoLog(LogLevel.Warning, "Unable to start building construction: a network error has occurred");
					} else {
						if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
							float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
								_tbotInstance.UserData.nextDOIR = DOIR;
							}
						}
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							DoLog(LogLevel.Information, $"Not enough resources to build: {level.ToString()}x {buildable.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						} else {
							DoLog(LogLevel.Information, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						}
						LedgerTBot.Plan(_tbotInstance, celestial, "AutoMine", buildable.ToString() + " " + level.ToString(), xCostBuildable, ledgerRoi);
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
							if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler) {
								_pendingFundedBuildable = buildable;
								_pendingFundedLevel = level;
								EnergyFundingTracker.SetPending(celestial.ID, buildable);
							}
							_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							List<RankSlotsPriority> rankSlotsPriority = new() {
								new RankSlotsPriority(Feature.BrainAutoMine,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Brain,
									((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active && ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Active || (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active || (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)),
									(int) _tbotInstance.InstanceSettings.Brain.Transports.MaxSlots,
									(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Transport)),
								new RankSlotsPriority(Feature.Expeditions,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Expeditions,
									(bool) _tbotInstance.InstanceSettings.Expeditions.Active,
									(int) _tbotInstance.UserData.slots.ExpTotal,
									(int)_tbotInstance.UserData.slots.ExpInUse),
								new RankSlotsPriority(Feature.AutoFarm,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoFarm,
									(bool) _tbotInstance.InstanceSettings.AutoFarm.Active,
									(int) _tbotInstance.InstanceSettings.AutoFarm.MaxSlots,
									(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Attack)),
								new RankSlotsPriority(Feature.Colonize,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.Colonize,
									(bool) _tbotInstance.InstanceSettings.AutoColonize.Active,
									(bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active ?
										(int) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MaxSlots :
										1,
									(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Colonize)),
								new RankSlotsPriority(Feature.AutoDiscovery,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoDiscovery,
									(bool) _tbotInstance.InstanceSettings.AutoDiscovery.Active,
									(int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots,
									(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Discovery)),
								new RankSlotsPriority(Feature.Harvest,
									(int) _tbotInstance.InstanceSettings.General.SlotPriorityLevel.AutoHarvest,
									(bool) _tbotInstance.InstanceSettings.AutoHarvest.Active,
									(int) _tbotInstance.InstanceSettings.AutoHarvest.MaxSlots,
									(int) _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Harvest))
									};
							int MaxSlots = _calculationService.CalcSlotsPriority(Feature.BrainAutoMine, rankSlotsPriority, _tbotInstance.UserData.slots, _tbotInstance.UserData.fleets, (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree);

							if (MaxSlots > 0) {
								bool incomingTransport = _calculationService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets);
								if (!incomingTransport &&
									(bool) _tbotInstance.InstanceSettings.Brain.Transports.SendToTheMoonIfPossible &&
									celestial.Coordinate.Type == Celestials.Planet) {
									Celestial coLocatedMoon = _tbotInstance.UserData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
										.Where(c => c.Coordinate.System == celestial.Coordinate.System)
										.Where(c => c.Coordinate.Position == celestial.Coordinate.Position)
										.Where(c => c.Coordinate.Type == Celestials.Moon)
										.SingleOrDefault();
									if (coLocatedMoon != null) {
										incomingTransport = _calculationService.IsThereTransportTowardsCelestial(coLocatedMoon, _tbotInstance.UserData.fleets);
									}
								}
								LedgerTBotPlan ledgerPlan = LedgerTBot.Plan(_tbotInstance, celestial, "AutoMine", buildable.ToString() + " " + level.ToString(), xCostBuildable, ledgerRoi);
								if (!ledgerPlan.Legacy)
									incomingTransport = ledgerPlan.MissingIsZero;
								if (!incomingTransport) {
									Celestial origin = new() { ID = 0 };
									List<Celestial> allCelestials = _tbotInstance.UserData.celestials.ToList();
									if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Active) {
										for (int i = 0; i < allCelestials.Count(); i++) {
											allCelestials[i] = await _tbotOgameBridge.UpdatePlanet(allCelestials[i], UpdateTypes.Resources);
											allCelestials[i] = await _tbotOgameBridge.UpdatePlanet(allCelestials[i], UpdateTypes.Ships);
											allCelestials[i] = await _tbotOgameBridge.UpdatePlanet(allCelestials[i], UpdateTypes.LFBonuses);
										}
									} else {
										List<Celestial> candidateOrigins = new();
										Celestial coLocated = allCelestials.Unique()
											.Where(c => c.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
											.Where(c => c.Coordinate.System == celestial.Coordinate.System)
											.Where(c => c.Coordinate.Position == celestial.Coordinate.Position)
											.Where(c => c.Coordinate.Type != celestial.Coordinate.Type)
											.SingleOrDefault();
										if (coLocated != null)
											candidateOrigins.Add(coLocated);
										Celestial fixedOrigin = allCelestials.Unique()
											.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
											.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
											.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
											.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
											.SingleOrDefault();
										if (fixedOrigin != null && !candidateOrigins.Any(c => c.ID == fixedOrigin.ID))
											candidateOrigins.Add(fixedOrigin);

										foreach (var candidate in candidateOrigins) {
											int idx = allCelestials.FindIndex(x => x.ID == candidate.ID);
											if (idx >= 0) {
												allCelestials[idx] = await _tbotOgameBridge.UpdatePlanet(allCelestials[idx], UpdateTypes.Resources);
												allCelestials[idx] = await _tbotOgameBridge.UpdatePlanet(allCelestials[idx], UpdateTypes.Ships);
												allCelestials[idx] = await _tbotOgameBridge.UpdatePlanet(allCelestials[idx], UpdateTypes.LFBonuses);
											}
										}
									}
									Resources missingResources = ledgerPlan.Legacy ? xCostBuildable.Difference(celestial.Resources) : ledgerPlan.Missing;
									if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.CheckMoonOrPlanetFirst) {
										if (celestial.Coordinate.Type == Celestials.Planet && ((bool) _tbotInstance.InstanceSettings.Brain.Transports.CheckMoonOrPlanetFirst && _calculationService.IsThereMoonHere(allCelestials, celestial))) {
											origin = allCelestials.Unique()
												.Where(c => c.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
												.Where(c => c.Coordinate.System == celestial.Coordinate.System)
												.Where(c => c.Coordinate.Position == celestial.Coordinate.Position)
												.Where(c => c.Coordinate.Type == Celestials.Moon)
												.SingleOrDefault() ?? new() { ID = 0 };
										}
										if (celestial.Coordinate.Type == Celestials.Moon) {
											origin = allCelestials.Unique()
												.Where(c => c.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
												.Where(c => c.Coordinate.System == celestial.Coordinate.System)
												.Where(c => c.Coordinate.Position == celestial.Coordinate.Position)
												.Where(c => c.Coordinate.Type == Celestials.Planet)
												.SingleOrDefault() ?? new() { ID = 0 };
										}
										if (origin.ID != 0) {
											Resources resToLeave = new();
											resToLeave.Deuterium = TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, origin.Coordinate.Type);
											if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
												missingResources = _tbotInstance.InstanceSettings.Brain.Transports.RoundResources ? missingResources.Round() : missingResources;
												Buildables preferredShip = Buildables.SmallCargo;
												if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
													_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
													preferredShip = Buildables.SmallCargo;
												}
												long shipsNeeded = _calculationService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, origin.LFBonuses.GetShipCargoBonus(preferredShip), _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
												Ships brainSchiffe = _fleetScheduler.WaehleBrainSchiffe(origin, missingResources);
												if (brainSchiffe != null) {
													Ships ships = brainSchiffe;
													if (!KannVollstaendigLiefern(origin, ships, celestial.Coordinate, missingResources, resToLeave)) {
														DoLog(LogLevel.Warning, $"Partial delivery blocked: {origin.ToString()} cannot deliver {missingResources.TransportableResources} for {celestial.ToString()} completely (resources/fuel/cargo space).");
													} else {
														fleetId = await _fleetScheduler.SendFleet(origin, ships, celestial.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, deutToLeave: (TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, origin.Coordinate.Type)));
														if (fleetId == (int) SendFleetCode.AfterSleepTime) {
															stop = true;
														}
														if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
															delay = true;
														}
													}
												} else {
													DoLog(LogLevel.Warning, $"Not enough Cargo available. Teillieferung unterbunden, skipping CheckMoonOrPlanetFirst.");
												}
											} else {
												DoLog(LogLevel.Debug, $"Not enough resources available on {origin.ToString()} only to send resources to {celestial.ToString()}.");
											}
										} else {
											DoLog(LogLevel.Warning, $"No Moon available on {celestial.ToString()}.");
										}
									}

									if (fleetId <= 0) {
										if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Active) {
											origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
											List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Exclude, allCelestials);
											Buildables preferredShip = Buildables.SmallCargo;
											if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
												_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
												preferredShip = Buildables.SmallCargo;
											}
											TransportSettings transportsSettings = new((bool) _tbotInstance.InstanceSettings.Brain.Transports.Active,
												preferredShip,
												(long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeaveOnMoons,
												(bool) _tbotInstance.InstanceSettings.Brain.Transports.RoundResources,
												(bool) _tbotInstance.InstanceSettings.Brain.Transports.SendToTheMoonIfPossible,
												origin,
												(long) _tbotInstance.InstanceSettings.Brain.Transports.MaxSlots,
												(bool) _tbotInstance.InstanceSettings.Brain.Transports.CheckMoonOrPlanetFirst,
												(bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition,
												new MultipleOrigins((bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Active,
													(bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.OnlyFromMoons,
													(long) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.MinimumResourcesToSend,
													(bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.PriorityToProximityOverQuantity,
													celestialsToExclude)
												) { DeutToLeaveOnPlanets = SettingsService.GetSetting<long>(_tbotInstance.InstanceSettings.Brain.Transports, "DeutToLeaveOnPlanets", -1L) };

											Celestial destination;
											if ((bool) transportsSettings.SendToTheMoonIfPossible && celestial.Coordinate.Type == Celestials.Planet && _calculationService.IsThereMoonHere(allCelestials, celestial)) {
												destination = allCelestials
													.Unique()
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == Celestials.Moon)
													.SingleOrDefault() ?? new() { ID = 0 };
												if (destination.Ships.IsEmpty() || celestial.Resources.TotalResources == 0)
													destination = celestial;
											} else {
												destination = allCelestials
													.Unique()
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == celestial.Coordinate.Type)
													.SingleOrDefault() ?? new() { ID = 0 };
											}

											var resultOrigins = _calculationService.CalcMultipleOrigin(celestial, allCelestials, missingResources, transportsSettings, _tbotInstance.UserData.fleets, _tbotInstance.UserData, quellCelestial => _fleetScheduler.BrainQuellSchiffe(quellCelestial), _fleetScheduler.BrainMaxSendungen(MaxSlots));

											if (resultOrigins.Count() == 0) {
												DoLog(LogLevel.Information, $"No origin is available. This may be due to a lack of resources or cargo.");
												return;
											}
											if (resultOrigins.Count() > MaxSlots) {
												DoLog(LogLevel.Information, $"Not enough slots available to send all resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Slots needed: {resultOrigins.Count().ToString()}/{MaxSlots}.");
												delay = true;
												return;
											}

											var geplanteLieferungen = new List<(Celestial Herkunft, Ships Schiffe, Resources Menge, Coordinate Ziel)>();
											Resources gesamtMenge = new();
											foreach (var item in resultOrigins) {
												var shipment = item.FirstOrDefault();
												var shipmentOrigin = shipment.Key;
												var shipmentAmount = shipment.Value;
												var ships = _fleetScheduler.WaehleBrainSchiffe(shipmentOrigin, shipmentAmount);
												if (ships == null) {
													DoLog(LogLevel.Warning, $"Brain transport: {shipmentOrigin.ToString()} cannot carry {shipmentAmount.TransportableResources} with the available transporters (after reserve). Nothing more is sent.");
													return;
												}
												Coordinate zielKoordinate = (shipmentOrigin.Coordinate.IsSame(destination.Coordinate) && transportsSettings.SendToTheMoonIfPossible && destination.Coordinate.Type == Celestials.Moon) ? celestial.Coordinate : destination.Coordinate;
												geplanteLieferungen.Add((shipmentOrigin, ships, shipmentAmount, zielKoordinate));
												gesamtMenge = gesamtMenge.Sum(shipmentAmount);
											}
											if (gesamtMenge.Metal < missingResources.Metal || gesamtMenge.Crystal < missingResources.Crystal || gesamtMenge.Deuterium < missingResources.Deuterium) {
												DoLog(LogLevel.Warning, $"Partial delivery blocked: the origin planets together deliver only {gesamtMenge.TransportableResources} of {missingResources.TransportableResources} for {celestial.ToString()}. Nothing is sent.");
												return;
											}
											foreach (var lieferung in geplanteLieferungen) {
												long deutLassen = TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, lieferung.Herkunft.Coordinate.Type);
												if (!KannVollstaendigLiefern(lieferung.Herkunft, lieferung.Schiffe, lieferung.Ziel, lieferung.Menge, new Resources { Deuterium = deutLassen })) {
													DoLog(LogLevel.Warning, $"Partial delivery blocked: {lieferung.Herkunft.ToString()} cannot deliver {lieferung.Menge.TransportableResources} completely (resources/fuel/cargo space). Nothing is sent.");
													return;
												}
											}
											foreach (var lieferung in geplanteLieferungen) {
												fleetId = await _fleetScheduler.SendFleet(lieferung.Herkunft, lieferung.Schiffe, lieferung.Ziel, Missions.Transport, Speeds.HundredPercent, lieferung.Menge, deutToLeave: (TransportReserve.DeutToLeave(_tbotInstance.InstanceSettings.Brain.Transports, lieferung.Herkunft.Coordinate.Type)));
												
												if (fleetId == (int) SendFleetCode.AfterSleepTime) {
													stop = true;
													return;
												}
												if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
													delay = true;
													return;
												}
											}
										} else {
											Celestial destination;
											if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.SendToTheMoonIfPossible && celestial.Coordinate.Type == Celestials.Planet && _calculationService.IsThereMoonHere(_tbotInstance.UserData.celestials, celestial) && (!celestial.Ships.IsEmpty() || celestial.Resources.TotalResources > 0)) {
												destination = allCelestials
													.Unique()
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == Celestials.Moon)
													.SingleOrDefault() ?? new() { ID = 0 };
												if (destination.Ships.IsEmpty() || celestial.Resources.TotalResources == 0)
													destination = celestial;
											} else {
												destination = allCelestials
													.Unique()
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == celestial.Coordinate.Type)
													.SingleOrDefault() ?? new() { ID = 0 };
											}
											origin = allCelestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
											fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, destination, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
											if (fleetId == (int) SendFleetCode.AfterSleepTime) {
												stop = true;
											}
											if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
												delay = true;
											}
										}
									}
								} else {
									DoLog(LogLevel.Information, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									if (ledgerPlan.Legacy) {
										var incomingFleet = _tbotInstance.UserData.fleets.Where(f => f.Mission == Missions.Transport)
											.Where(f => f.Resources.TotalResources > 0)
											.Where(f => f.ReturnFlight == false)
											.Where(f => f.Destination.Galaxy == celestial.Coordinate.Galaxy)
											.Where(f => f.Destination.System == celestial.Coordinate.System)
											.Where(f => f.Destination.Position == celestial.Coordinate.Position)
											.FirstOrDefault();
										fleetId = incomingFleet != null ? incomingFleet.ID : 0;
									} else {
										fleetId = ledgerPlan.OwnFleetId;
									}
								}
							} else {
								if (_tbotInstance.InstanceSettings.General.SlotPriorityLevel.Brain > 0) {
									if (_tbotInstance.InstanceSettings.Brain.Transports.MaxSlots == 0)
										DoLog(LogLevel.Information, $"Transports.MaxSlots is set to 0, you should increase it.");
									else
										DoLog(LogLevel.Information, $"0 slots available.");
								}
								DoLog(LogLevel.Information, $"Not enough slots available for Transports.MaxSlots, delaying.");
								delay = true;
							}
						}
					}
				} else {
					DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: nothing to build.");
					if (celestial.Coordinate.Type == Celestials.Planet) {
						var nextDOIR = _calculationService.CalcNextDaysOfInvestmentReturn(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						if (
							(celestial as Planet).HasFacilities(maxFacilities) && (
								(celestial as Planet).HasMines(maxBuildings) ||
								nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) {
							if (nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn) {
								var nextMine = _calculationService.GetNextMineToBuild(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 100, 100, 100, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, autoMinerSettings.OptimizeForStart, float.MaxValue);
								var nexMineLevel = _calculationService.GetNextLevel(celestial, nextMine);
								if (nextDOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
									_tbotInstance.UserData.nextDOIR = nextDOIR;
								}
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine.MaxDaysOfInvestmentReturn to at least {Math.Round(nextDOIR, 2, MidpointRounding.ToPositiveInfinity).ToString()}.");
								DoLog(LogLevel.Debug, $"Next mine to build: {nextMine.ToString()} lv {nexMineLevel.ToString()}.");

							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine mines max levels");
							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine facilities max levels");
							}
							stop = true;
						}
					} else if (celestial.Coordinate.Type == Celestials.Moon) {
						if ((celestial as Moon).HasLunarFacilities(maxLunarFacilities)) {
							DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine lunar facilities max levels");
						}
						stop = true;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await _tbotOgameBridge.GetDateTime();
				string autoMineTimer = $"AutoMine-{celestial.ID.ToString()}";
				DateTime newTime;
				long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping AutoMine check for {celestial.ToString()}.");
					await EndExecution();
				} else if (delayProduction) {
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
					DoLog(LogLevel.Information, $"Delaying...");
					time = await _tbotOgameBridge.GetDateTime();
					try {
						interval = _calculationService.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, $"Delaying...");
					time = await _tbotOgameBridge.GetDateTime();
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					try {
						interval = (_tbotInstance.UserData.fleets.Where(f => f.Mission == Missions.Transport).OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (started) {
					interval = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}
				} else if (delayBuilding > 0) {

					newTime = time.AddMilliseconds(delayBuilding);
					ChangeWorkerPeriod(delayBuilding);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					interval = await CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					if (fleetId > 0) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) {
						if (_tbotInstance.UserData.fleets.Where(f => f.Mission == Missions.Transport).Count() > 0) {
							var transportfleet2 = _tbotInstance.UserData.fleets.Where(f => f.Mission == Missions.Transport)
								.Where(f => f.Destination.IsSame(celestial.Coordinate))
								.Where(f => f.Origin.Galaxy == celestial.Coordinate.Galaxy)
								.Where(f => f.Origin.System == celestial.Coordinate.System)
								.Where(f => f.Origin.Position == celestial.Coordinate.Position)
								.Where(f => f.Origin.Type == (celestial.Coordinate.Type == Celestials.Planet ? Celestials.Moon : Celestials.Planet))
								.ToList();
							if (transportfleet2.Count() > 0)
								interval = (long) (transportfleet2.First().BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						}
					}

					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}

				}
			}
		}
		private async Task<long> CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					DoLog(LogLevel.Information, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
						interval = _calculationService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000;
					} else if (buildable == Buildables.Crawler) {
						interval = (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						if (celestial.HasConstruction())
							interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
						else
							interval = 0;
					}
				} else if (celestial.HasConstruction()) {
					interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				} else {
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);

					if (buildable != Buildables.Null) {
						var price = _calculationService.CalcPrice(buildable, level, celestial.LFBonuses);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
						DateTime now = await _tbotOgameBridge.GetDateTime();
						if (
							celestial.Coordinate.Type == Celestials.Planet &&
							(price.Metal <= celestial.ResourcesProduction.Metal.StorageCapacity || price.Metal <= celestial.Resources.Metal) &&
							(price.Crystal <= celestial.ResourcesProduction.Crystal.StorageCapacity || price.Crystal <= celestial.Resources.Crystal) &&
							(price.Deuterium <= celestial.ResourcesProduction.Deuterium.StorageCapacity || price.Deuterium <= celestial.Resources.Deuterium)
						) {
							var missingResources = price.Difference(celestial.Resources);
							float metProdInASecond = celestial.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
							float cryProdInASecond = celestial.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
							float deutProdInASecond = celestial.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
							if (
								!(
									(missingResources.Metal > 0 && (metProdInASecond == 0 && celestial.Resources.Metal < price.Metal)) ||
									(missingResources.Crystal > 0 && (cryProdInASecond == 0 && celestial.Resources.Crystal < price.Crystal)) ||
									(missingResources.Deuterium > 0 && (deutProdInASecond == 0 && celestial.Resources.Deuterium < price.Deuterium))
								)
							) {
								float metProductionTime = float.IsNaN(missingResources.Metal / metProdInASecond) ? 0.0F : missingResources.Metal / metProdInASecond;
								float cryProductionTime = float.IsNaN(missingResources.Crystal / cryProdInASecond) ? 0.0F : missingResources.Crystal / cryProdInASecond;
								float deutProductionTime = float.IsNaN(missingResources.Deuterium / deutProdInASecond) ? 0.0F : missingResources.Deuterium / deutProdInASecond;
								productionTime = (long) (Math.Round(Math.Max(Math.Max(metProductionTime, cryProductionTime), deutProductionTime), 0) * 1000);
							}
						}

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var incomingFleets = _calculationService.GetIncomingFleetsWithResources(celestial, _tbotInstance.UserData.fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
						}

						var returningExpo = _calculationService.GetFirstReturningExpedition(celestial.Coordinate, _tbotInstance.UserData.fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
						}

						if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
							Celestial origin = _tbotInstance.UserData.celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							if (origin.ID != 0) {
								var returningExpoOrigin = _calculationService.GetFirstReturningExpedition(origin.Coordinate, _tbotInstance.UserData.fleets);
								if (returningExpoOrigin != null) {
									returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								}

								var incomingOriginFleets = _calculationService.GetIncomingFleetsWithResources(origin, _tbotInstance.UserData.fleets);
								if (incomingOriginFleets.Any()) {
									var fleet = incomingOriginFleets.First();
									transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
								}
							}
						}

						productionTime = productionTime < 0 || double.IsNaN(productionTime) ? long.MaxValue : productionTime;
						transportTime = transportTime < 0 || double.IsNaN(transportTime) ? long.MaxValue : transportTime;
						returningExpoTime = returningExpoTime < 0 || double.IsNaN(returningExpoTime) ? long.MaxValue : returningExpoTime;
						returningExpoOriginTime = returningExpoOriginTime < 0 || double.IsNaN(returningExpoOriginTime) ? long.MaxValue : returningExpoOriginTime;
						transportOriginTime = transportOriginTime < 0 || double.IsNaN(transportOriginTime) ? long.MaxValue : transportOriginTime;

						interval = Math.Min(Math.Min(Math.Min(Math.Min(productionTime, transportTime), returningExpoTime), returningExpoOriginTime), transportOriginTime);
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
		}
	}
}