#if FASTFARM
namespace TBot.WebUI.Models {
	public sealed class FastFarmDashboardModel {
		public string SelectedInstance { get; set; } = string.Empty;
		public IReadOnlyList<string> Instances { get; set; } = Array.Empty<string>();
		public bool Available { get; set; }
		public bool Active { get; set; }
		public bool OnlyWorker { get; set; } = true;
		public FastFarmConfigSummary Config { get; set; } = new();
		public FastFarmStateDto? State { get; set; }
	}

	public sealed class FastFarmConfigSummary {
		public string CargoType { get; set; } = "SmallCargo";
		public int ProbesPerTarget { get; set; } = 20;
		public int SlotsToLeaveFree { get; set; } = 1;
		public double WeightMetal { get; set; } = 3;
		public double WeightCrystal { get; set; } = 2;
		public double WeightDeuterium { get; set; } = 1;
		public long MinLoot { get; set; }
		public long MinDeuterium { get; set; }
		public int TopTargets { get; set; } = 36;
		public int ReportMaxAgeMinutes { get; set; } = 30;
		public double ShrinkDiscardPercent { get; set; } = 10;
	}

	public sealed class FastFarmStateDto {
		public Dictionary<string, FastFarmTargetDto> Targets { get; set; } = new();
		public string? SweepPhase { get; set; }
		public int SweepGalaxy { get; set; }
		public int SweepRound { get; set; }
		public long AttacksSent { get; set; }
		public long ProbesSent { get; set; }
		public long ReportsRead { get; set; }
		public long ExpectedMetal { get; set; }
		public long ExpectedCrystal { get; set; }
		public long ExpectedDeuterium { get; set; }
		public List<FastFarmAttackDto> Recent { get; set; } = new();
		public string? Status { get; set; }
		public int FreeSlots { get; set; }
		public int ProbesOut { get; set; }
		public int AttacksOut { get; set; }
		public string? FastestTarget { get; set; }
		public long FastestFlightSec { get; set; }
		public string? LongestTarget { get; set; }
		public long LongestFlightSec { get; set; }
		public DateTime UpdatedUtc { get; set; }
		public DateTime NextRunUtc { get; set; }
	}

	public sealed class FastFarmTargetDto {
		public int Galaxy { get; set; }
		public int System { get; set; }
		public int Position { get; set; }
		public string? Player { get; set; }
		public bool Pending { get; set; }
		public DateTime? ReportServer { get; set; }
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long Weighted { get; set; }
		public long SweepValue { get; set; }
		public long LastWeighted { get; set; }
		public DateTime? BlockedUntilUtc { get; set; }
		public string? BlockReason { get; set; }
		public DateTime? LastAttackUtc { get; set; }
		public int Attacks { get; set; }
		public string? DefShips { get; set; }
		public string? DefDefence { get; set; }
		public DateTime? DefReportServer { get; set; }
	}

	public sealed class FastFarmAttackDto {
		public DateTime TimeUtc { get; set; }
		public string Target { get; set; } = string.Empty;
		public string? Ships { get; set; }
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long FlightSec { get; set; }
	}
}
#endif
