using System.Collections.Generic;

namespace TBot.Ogame.Infrastructure.Models {
	public class HighscoreInfo {
		public int NbPage { get; set; }
		public int CurrPage { get; set; }
		public int Category { get; set; }
		public int Type { get; set; }
		public List<HighscorePlayer> Players { get; set; } = new();
	}

	public class HighscorePlayer {
		public int Position { get; set; }
		public int ID { get; set; }
		public string Name { get; set; }
		public long Score { get; set; }
		public int AllianceID { get; set; }
		public long HonourPoints { get; set; }
		public Coordinate Homeworld { get; set; }
		public List<string> Status { get; set; } = new();

		public bool IsInactive => Status != null && (Status.Contains("inactive") || Status.Contains("longinactive"));
		public bool IsLongInactive => Status != null && Status.Contains("longinactive");
		public bool IsBanned => Status != null && Status.Contains("banned");
		public bool IsVacation => Status != null && Status.Contains("vacation");
	}
}
