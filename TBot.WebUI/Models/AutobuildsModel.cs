namespace TBot.WebUI.Models {

	public sealed class AutobuildStarted {
		public string Label { get; set; } = "";
		public DateTime? StartedUtc { get; set; }
		public long CostM { get; set; }
		public long CostC { get; set; }
		public long CostD { get; set; }
	}

	public sealed class AutobuildNext {
		public string Label { get; set; } = "";
		public string Status { get; set; } = "";
		public int FleetId { get; set; }
		public string? LastDelivery { get; set; }
		public int TimesSeen { get; set; }
		public DateTime? FirstRequestedUtc { get; set; }
		public DateTime? LastSeenUtc { get; set; }
		public long CostM { get; set; }
		public long CostC { get; set; }
		public long CostD { get; set; }
		public long MissingM { get; set; }
		public long MissingC { get; set; }
		public long MissingD { get; set; }
		public float RoiDays { get; set; }
	}

	public sealed class AutobuildWorkerRow {
		public string Owner { get; set; } = "";
		public AutobuildStarted? Building { get; set; }
		public AutobuildNext? Next { get; set; }
	}

	public sealed class AutobuildBody {
		public long CelestialId { get; set; }
		public string Name { get; set; } = "";
		public string Coordinate { get; set; } = "";
		public bool IsMoon { get; set; }
		public string GroupKey { get; set; } = "";
		public List<AutobuildWorkerRow> Workers { get; set; } = new();
	}

	public sealed class AutobuildHistoryRow {
		public string Owner { get; set; } = "";
		public string Celestial { get; set; } = "";
		public string Label { get; set; } = "";
		public string Status { get; set; } = "";
		public DateTime? ClosedUtc { get; set; }
	}

	public sealed class AutobuildRoiInfo {
		public string Owner { get; set; } = "";
		public string Celestial { get; set; } = "";
		public string Coordinate { get; set; } = "";
		public string Label { get; set; } = "";
		public string Status { get; set; } = "";
		public float RoiDays { get; set; }
	}

	public sealed class AutobuildsSummary {
		public int Open { get; set; }
		public int Waiting { get; set; }
		public int InFlight { get; set; }
		public int Funded { get; set; }
		public int BuiltLast24h { get; set; }
		public int RoiCount { get; set; }
		public AutobuildRoiInfo? BestRoi { get; set; }
		public AutobuildRoiInfo? WorstRoi { get; set; }
	}

	public sealed class AutobuildsModel {
		public string SelectedInstance { get; set; } = "";
		public IReadOnlyList<string> Instances { get; set; } = new List<string>();
		public bool DataAvailable { get; set; }
		public string SourceFile { get; set; } = "";
		public DateTime? UpdatedUtc { get; set; }
		public AutobuildsSummary Summary { get; set; } = new();
		public List<AutobuildBody> Bodies { get; set; } = new();
		public List<AutobuildHistoryRow> History { get; set; } = new();
		public string? Error { get; set; }
	}
}