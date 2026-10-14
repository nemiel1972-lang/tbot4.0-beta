using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {

	public class SimulatorFleet {
		public int Weapon { get; set; }
		public int Shield { get; set; }
		public int Armour { get; set; }
		public long LightFighter { get; set; }
		public long HeavyFighter { get; set; }
		public long Cruiser { get; set; }
		public long Battleship { get; set; }
		public long Battlecruiser { get; set; }
		public long Bomber { get; set; }
		public long Destroyer { get; set; }
		public long Deathstar { get; set; }
		public long SmallCargo { get; set; }
		public long LargeCargo { get; set; }
		public long ColonyShip { get; set; }
		public long Recycler { get; set; }
		public long EspionageProbe { get; set; }
		public long SolarSatellite { get; set; }
		public long Crawler { get; set; }
		public long Reaper { get; set; }
		public long Pathfinder { get; set; }
	}

	public class SimulatorDefender : SimulatorFleet {
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long RocketLauncher { get; set; }
		public long LightLaser { get; set; }
		public long HeavyLaser { get; set; }
		public long GaussCannon { get; set; }
		public long IonCannon { get; set; }
		public long PlasmaTurret { get; set; }
		public long SmallShieldDome { get; set; }
		public long LargeShieldDome { get; set; }
		public long AntiBallisticMissiles { get; set; }
		public long InterplanetaryMissiles { get; set; }
	}

	public class SimulateBattleRequest {
		public SimulatorFleet Attacker { get; set; }
		public SimulatorDefender Defender { get; set; }
		public int Simulations { get; set; }
		public double FleetToDebris { get; set; }
	}

	public class SimulatorPrice {
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }

		public long Total() => Metal + Crystal + Deuterium;
	}

	public class SimulatorResult {
		public int Simulations { get; set; }
		public int AttackerWin { get; set; }
		public int DefenderWin { get; set; }
		public int Draw { get; set; }
		public int Rounds { get; set; }
		public SimulatorPrice AttackerLosses { get; set; }
		public SimulatorPrice DefenderLosses { get; set; }
		public SimulatorPrice Debris { get; set; }
		public int Recycler { get; set; }
		public int Moonchance { get; set; }
		public int AttackerCargoSent { get; set; }
		public double AttackerCargoSurvived { get; set; }
		public string Logs { get; set; }

		public double AttackerWinRate => AttackerWin / 100.0;

		public double CargoLossRate => AttackerCargoSent > 0 ? (AttackerCargoSent - AttackerCargoSurvived) / AttackerCargoSent : 0;
	}
}
