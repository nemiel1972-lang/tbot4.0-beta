using System;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class ShadowFlightResult {
		public int FleetId;
		public string Problem;
		public Coordinate Debris;
		public DateTime RecallAt;
		public long RoundTripSeconds;
		public long Fuel;
		public long Payload;
		public long FlightsPossible;
		public DeutLevel Level;
	}
}
