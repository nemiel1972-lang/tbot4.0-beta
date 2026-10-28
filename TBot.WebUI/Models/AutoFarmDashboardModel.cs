using System;
using System.Collections.Generic;

namespace TBot.WebUI.Models {
	public sealed class AutoFarmDashboardModel {
		public string SelectedInstance { get; init; } = string.Empty;
		public IReadOnlyList<string> Instances { get; init; } = Array.Empty<string>();
		public bool DatabaseAvailable { get; init; }
		public int CachedSystemCount { get; init; }
		public DateTime? LastSystemObservedAtUtc { get; init; }
		public IReadOnlyList<AutoFarmReportRow> Reports { get; init; } = Array.Empty<AutoFarmReportRow>();
		public IReadOnlyList<AutoFarmAttackRow> Attacks { get; init; } = Array.Empty<AutoFarmAttackRow>();
		public AutoFarmWorkerStatus Worker { get; init; } = new();
		public AutoFarmSlotStatus Slots { get; init; } = new();
		public AutoFarmScanStatus Scan { get; init; } = new();
		public IReadOnlyList<AutoFarmSystemRow> Systems { get; init; } = Array.Empty<AutoFarmSystemRow>();
		public IReadOnlyList<AutoFarmLogRow> Logs { get; init; } = Array.Empty<AutoFarmLogRow>();
		public IReadOnlyList<AutoFarmLogRow> Errors { get; init; } = Array.Empty<AutoFarmLogRow>();
		public IReadOnlyList<AutoFarmLossPeriod> Losses { get; init; } = Array.Empty<AutoFarmLossPeriod>();
		public IReadOnlyList<AutoFarmBlacklistRow> Blacklist { get; init; } = Array.Empty<AutoFarmBlacklistRow>();
		public int PendingCommands { get; init; }
		public AutoFarmDeuteriumStatus Deuterium { get; init; } = new();
		public GalaxyScanStatus Galaxy { get; init; } = new();

		public int PendingAttackCount => Reports.Count(report => report.State == "AttackPending");
	}

	public sealed class GalaxyScanStatus {
		public bool Available { get; init; }
		public int TotalSystems { get; init; }
		public int FreshSystems { get; init; }
		public int KnownSystems { get; init; }
		public double Percent { get; init; }
		public string LastCoord { get; init; } = string.Empty;
		public string LastKind { get; init; } = string.Empty;
		public DateTime? LastUtc { get; init; }
		public double? EtaSeconds { get; init; }
		public DateTime? NextDueUtc { get; init; }
		public int Players { get; init; }
		public int InactivePlayers { get; init; }
		public int Targets { get; init; }
		public int Colonies { get; init; }
		public DateTime? HighscoreUtc { get; init; }
	}

	public sealed class AutoFarmDeuteriumOrigin {
		public string Coordinate { get; init; } = string.Empty;
		public long Deuterium { get; init; }
		public bool Low { get; init; }
	}

	public sealed class AutoFarmDeuteriumStatus {
		public bool Available { get; init; }
		public DateTime? UpdatedUtc { get; init; }
		public long MinDeuterium { get; init; }
		public bool GuardEnabled { get; init; }
		public bool Override { get; init; }
		public bool SpeedFleetBlocked { get; init; }
		public List<AutoFarmDeuteriumOrigin> Origins { get; init; } = new();
	}

	public sealed class AutoFarmBlacklistRow {
		public string Coordinate { get; init; } = string.Empty;
		public string Reason { get; init; } = string.Empty;
		public DateTime? BlacklistedAtUtc { get; init; }
		public DateTime? ExpiresAtUtc { get; init; }
		public bool HasReport { get; set; }
		public DateTime? ReportDateUtc { get; set; }
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public string Ships { get; set; } = string.Empty;
		public string Defence { get; set; } = string.Empty;
		public string Player { get; set; } = string.Empty;
	}

	public sealed class AutoFarmLossShip {
		public string Ship { get; init; } = string.Empty;
		public long Count { get; init; }
	}

	public sealed class AutoFarmResourceSet {
		public long Metal { get; init; }
		public long Crystal { get; init; }
		public long Deuterium { get; init; }
		public long Total => Metal + Crystal + Deuterium;
	}

	public sealed class AutoFarmLossPeriod {
		public string Label { get; init; } = string.Empty;
		public int Attacks { get; init; }
		public int Destroyed { get; init; }
		public int Unknown { get; init; }
		public int Pending { get; init; }
		public IReadOnlyList<AutoFarmLossShip> Ships { get; init; } = Array.Empty<AutoFarmLossShip>();
		public long LossValue { get; init; }
		public long PlannedLoot { get; init; }
		public int ActualCount { get; init; }
		public long ActualLoot { get; init; }
		public long Fuel { get; init; }
		public long Net => ActualLoot - LossValue - Fuel;
		public AutoFarmResourceSet LossRes { get; init; } = new();
		public AutoFarmResourceSet PlannedRes { get; init; } = new();
		public AutoFarmResourceSet ActualRes { get; init; } = new();
		public AutoFarmResourceSet NetRes => new() {
			Metal = ActualRes.Metal - LossRes.Metal,
			Crystal = ActualRes.Crystal - LossRes.Crystal,
			Deuterium = ActualRes.Deuterium - LossRes.Deuterium - Fuel
		};
	}

	public sealed class AutoFarmWorkerStatus {
		public string State { get; init; } = "Unknown";
		public DateTime? LastExecutionUtc { get; init; }
		public DateTime? LastActivityUtc { get; init; }
		public DateTime? NextExecutionUtc { get; init; }
		public string NextExecutionText { get; init; } = "Unknown";
	}

	public sealed class AutoFarmSlotStatus {
		public int? MaxSlots { get; init; }
		public int? UsedSlots { get; init; }
		public int? AvailableSlots { get; init; }
		public int ProbesInFlight { get; init; }
		public int AttacksInFlight { get; init; }
	}

	public sealed class AutoFarmScanStatus {
		public int CursorGalaxy { get; init; }
		public int CursorSystem { get; init; }
		public int CachedSystems { get; init; }
		public int ScannedSystemsInRange { get; init; }
		public int TotalSystemsInRange { get; init; }
		public int ProgressPercent { get; init; }
		public DateTime? CursorUpdatedAtUtc { get; init; }
		public DateTime? LastSystemObservedAtUtc { get; init; }
	}

	public sealed class AutoFarmSystemRow {
		public string Coordinate { get; init; } = string.Empty;
		public string Status { get; init; } = string.Empty;
		public string Reason { get; init; } = string.Empty;
		public DateTime? LastScannedUtc { get; init; }
		public DateTime? NextRefreshUtc { get; init; }
	}

	public sealed class AutoFarmReportRow {
		public string TargetName { get; init; } = string.Empty;
		public string Coordinate { get; init; } = string.Empty;
		public string State { get; init; } = string.Empty;
		public int ReportId { get; init; }
		public DateTime? ReportDateUtc { get; init; }
		public DateTime? UpdatedAtUtc { get; init; }
		public long Metal { get; init; }
		public long Crystal { get; init; }
		public long Deuterium { get; init; }
		public int? ConsumedReportId { get; init; }
		public string Reason { get; init; } = string.Empty;
		public string ReasonDetail { get; init; } = string.Empty;
		public long TotalResources => Metal + Crystal + Deuterium;
	}

	public sealed class AutoFarmAttackRow {
		public string TargetName { get; init; } = string.Empty;
		public string Coordinate { get; init; } = string.Empty;
		public string Origin { get; init; } = string.Empty;
		public string Mission { get; init; } = string.Empty;
		public int ReportId { get; init; }
		public DateTime DispatchedAtUtc { get; init; }
		public long Metal { get; init; }
		public long Crystal { get; init; }
		public long Deuterium { get; init; }
		public long TotalResources => Metal + Crystal + Deuterium;
	}

	public sealed class AutoFarmLogRow {
		public string Level { get; init; } = string.Empty;
		public string Sender { get; init; } = string.Empty;
		public DateTime? DateTimeUtc { get; init; }
		public string Message { get; init; } = string.Empty;
		public string CopyText { get; init; } = string.Empty;
	}
}
