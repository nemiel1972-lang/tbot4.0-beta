using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class EspionageReport {
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long Energy { get; set; }
		public long Darkmatter { get; set; }
		public int ID { get; set; }
		public string Username { get; set; }
		public CharacterClass CharacterClass { get; set; }
		public AllianceClass AllianceClass { get; set; }
		public int LastActivity { get; set; }
		public int CounterEspionage { get; set; }
		public string APIKey { get; set; }
		public bool HasFleetInformation { get; set; }
		public bool HasDefensesInformation { get; set; }
		public bool HasBuildingsInformation { get; set; }
		public bool HasResearchesInformation { get; set; }
		public bool HonorableTarget { get; set; }
		public bool IsBandit { get; set; }
		public bool IsStarlord { get; set; }
		public bool IsInactive { get; set; }
		public bool IsLongInactive { get; set; }

		public int? MetalMine { get; set; }
		public int? CrystalMine { get; set; }
		public int? DeuteriumSynthesizer { get; set; }
		public int? SolarPlant { get; set; }
		public int? FusionReactor { get; set; }
		public int? SolarSatellite { get; set; }
		public int? MetalStorage { get; set; }
		public int? CrystalStorage { get; set; }
		public int? DeuteriumTank { get; set; }

		public int? RoboticsFactory { get; set; }
		public int? Shipyard { get; set; }
		public int? ResearchLab { get; set; }
		public int? AllianceDepot { get; set; }
		public int? MissileSilo { get; set; }
		public int? NaniteFactory { get; set; }
		public int? Terraformer { get; set; }
		public int? SpaceDock { get; set; }
		public int? LunarBase { get; set; }
		public int? SensorPhalanx { get; set; }
		public int? JumpGate { get; set; }

		public int? EnergyTechnology { get; set; }
		public int? LaserTechnology { get; set; }
		public int? IonTechnology { get; set; }
		public int? HyperspaceTechnology { get; set; }
		public int? PlasmaTechnology { get; set; }
		public int? CombustionDrive { get; set; }
		public int? ImpulseDrive { get; set; }
		public int? HyperspaceDrive { get; set; }
		public int? EspionageTechnology { get; set; }
		public int? ComputerTechnology { get; set; }
		public int? Astrophysics { get; set; }
		public int? IntergalacticResearchNetwork { get; set; }
		public int? GravitonTechnology { get; set; }
		public int? WeaponsTechnology { get; set; }
		public int? ShieldingTechnology { get; set; }
		public int? ArmourTechnology { get; set; }

		public long? RocketLauncher { get; set; }
		public long? LightLaser { get; set; }
		public long? HeavyLaser { get; set; }
		public long? GaussCannon { get; set; }
		public long? IonCannon { get; set; }
		public long? PlasmaTurret { get; set; }
		public long? SmallShieldDome { get; set; }
		public long? LargeShieldDome { get; set; }
		public long? AntiBallisticMissiles { get; set; }
		public long? InterplanetaryMissiles { get; set; }

		public long? LightFighter { get; set; }
		public long? HeavyFighter { get; set; }
		public long? Cruiser { get; set; }
		public long? Battleship { get; set; }
		public long? Battlecruiser { get; set; }
		public long? Bomber { get; set; }
		public long? Destroyer { get; set; }
		public long? Deathstar { get; set; }
		public long? SmallCargo { get; set; }
		public long? LargeCargo { get; set; }
		public long? ColonyShip { get; set; }
		public long? Recycler { get; set; }
		public long? EspionageProbe { get; set; }
		public long? Crawler { get; set; }
		public long? Reaper { get; set; }
		public long? Pathfinder { get; set; }
		public Coordinate Coordinate { get; set; }
		public EspionageReportType Type { get; set; }
		public DateTime Date { get; set; }

		public override string ToString() {
			return $"{Username} {Coordinate.ToString()}";
		}

		public bool IsDefenceless(bool isUsingProbes = false) {
			if (HasDefensesInformation && HasFleetInformation && !isUsingProbes) {
				return !HasShipOrDefence(
					LightFighter, HeavyFighter, Cruiser, Battleship, Battlecruiser,
					Bomber, Destroyer, Deathstar, SmallCargo, LargeCargo, Recycler,
					Reaper, Pathfinder, RocketLauncher, LightLaser, HeavyLaser,
					GaussCannon, IonCannon, PlasmaTurret, SmallShieldDome, LargeShieldDome);
			} else if (HasDefensesInformation && HasFleetInformation && isUsingProbes) {
				return !HasShipOrDefence(
					SolarSatellite, LightFighter, HeavyFighter, Cruiser, Battleship,
					Battlecruiser, Bomber, Destroyer, Deathstar, SmallCargo, LargeCargo,
					Recycler, Reaper, Pathfinder, RocketLauncher, LightLaser, HeavyLaser,
					GaussCannon, IonCannon, PlasmaTurret, SmallShieldDome, LargeShieldDome);
			}
			return false;
		}

		private static bool HasShipOrDefence(params long?[] amounts) {
			return amounts.Any(amount => amount.GetValueOrDefault() > 0);
		}

		public float PlunderRatio(CharacterClass playerClass) {
			if (IsInactive && playerClass == CharacterClass.Discoverer)
				return 0.75F;
			if (IsBandit)
				return 1F;
			if (!IsInactive && HonorableTarget)
				return 0.75F;
			return 0.5F;
		}

		public Resources Loot(CharacterClass playerClass) {
			float ratio = PlunderRatio(playerClass);
			return new Resources { Deuterium = (long) (Deuterium * ratio), Crystal = (long) (Crystal * ratio), Metal = (long) (Metal * ratio) };
		}

		public bool HasCoords(Coordinate coords) {
			return coords.Galaxy == Coordinate.Galaxy
				&& coords.System == Coordinate.System
				&& coords.Position == Coordinate.Position
				&& coords.Type == Coordinate.Type;
		}
	}

}
