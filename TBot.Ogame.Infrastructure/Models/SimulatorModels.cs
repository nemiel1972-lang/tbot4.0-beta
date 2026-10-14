using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	// request/response models for POST /bot/simulate-battle (ogamed Patch 38,
	// pkg/simulator/simulator.go wired to a REST endpoint). Field names are PascalCase to
	// match the Go structs 1:1 (ogame.ShipsInfos / ogame.DefensesInfos have no json tags, so
	// Go's encoding/json uses the exact exported field name; Newtonsoft.Json's default
	// serializer already emits C# property names as-is, so no [JsonProperty] renaming needed).

	// Fleet-only ships, used for the attacking side (own tech + escort ships; cargo ships are
	// NOT included here - only ships that actually fight matter for the simulation, sending
	// pure cargo alongside is irrelevant to the outcome and just wastes JSON payload).
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

	// Defender additionally carries planetary defenses and the resources on the ground
	// (used by the Go simulator for the debris/moonchance calculation).
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
		// cargo-specific survival, separate from the aggregate AttackerLosses price
		// value - AttackerCargoSent is how many SmallCargo+LargeCargo went in, AttackerCargoSurvived
		// is the average surviving count across all simulation runs (fractional - it's an average).
		public int AttackerCargoSent { get; set; }
		public double AttackerCargoSurvived { get; set; }
		public string Logs { get; set; }

		// Fraction of simulated battles the attacker won outright. AttackerWin from ogamed is
		// itself already a PERCENTAGE (0-100, see simulator.go: "result.AttackerWin =
		// round(attackerWin/nbSimulations*100)") - NOT a raw count out of Simulations. Dividing
		// by Simulations again (an earlier version of this code did) silently produced numbers
		// ~5x too low once the true win rate approached 100%, e.g. reporting "20%" for a target
		// that was actually being beaten in every single simulation.
		public double AttackerWinRate => AttackerWin / 100.0;

		// Fraction of the committed cargo fleet expected to be lost. 0 if no cargo ships were
		// part of the simulated attacker (e.g. the ceiling check, which only sends the escort).
		public double CargoLossRate => AttackerCargoSent > 0 ? (AttackerCargoSent - AttackerCargoSurvived) / AttackerCargoSent : 0;
	}
}
