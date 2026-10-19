using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers {
	public class ShadowFleetConfig {
		public bool Active = false;
		public int Galaxy = 1, System = 1, Position = 1;
		public string Type = "Planet";
		public Dictionary<string, long> Ships = new() { { "Recycler", 0 } };
		public int SpeedPercent = 10;
		public int MinBackInMinutes = 20;
		public int MaxBackInMinutes = 60;
		public int PauseMinSeconds = 30;
		public int PauseMaxSeconds = 180;
		public int DeuteriumFlights = 3;
		public string WindowFrom = "", WindowTo = "";
		public bool DefenderOff = false;

		public bool Matches(int galaxy, int system, int position, string type) =>
			Galaxy == galaxy && System == system && Position == position && string.Equals(Type, type, StringComparison.OrdinalIgnoreCase);
	}

	public enum DeutLevel { Ok, Low, Last, None }

	public class DeutPlan {
		public long Payload;
		public long FlightsPossible;
		public DeutLevel Level;
	}

	public static class ShadowFleetLogic {
		public static bool TryParseTime(string text, out TimeSpan t) {
			t = TimeSpan.Zero;
			if (string.IsNullOrWhiteSpace(text)) return false;
			return TimeSpan.TryParse(text.Trim(), out t) && t >= TimeSpan.Zero && t < TimeSpan.FromHours(24);
		}

		public static bool HasWindow(string from, string to) => TryParseTime(from, out _) && TryParseTime(to, out _);

		public static bool InWindow(TimeSpan now, string from, string to) {
			if (!TryParseTime(from, out var f) || !TryParseTime(to, out var t)) return true;
			if (f == t) return true;
			return f < t ? now >= f && now < t : now >= f || now < t;
		}

		public static long SecondsToWindowEnd(TimeSpan now, string from, string to) {
			if (!HasWindow(from, to) || !InWindow(now, from, to)) return HasWindow(from, to) ? 0 : long.MaxValue;
			TryParseTime(to, out var t);
			double d = (t - now).TotalSeconds;
			if (d <= 0) d += 24 * 3600;
			return (long) d;
		}

		public static long SecondsToWindowStart(TimeSpan now, string from, string to) {
			if (!HasWindow(from, to) || InWindow(now, from, to)) return 0;
			TryParseTime(from, out var f);
			double d = (f - now).TotalSeconds;
			if (d < 0) d += 24 * 3600;
			return (long) d;
		}

		public static long FlightSeconds(int minMinutes, int maxMinutes, long secondsToWindowEnd, Random rnd) {
			int lo = Math.Max(2, minMinutes), hi = Math.Max(lo, maxMinutes);
			long maxSec = hi * 60L;
			if (secondsToWindowEnd < maxSec) maxSec = secondsToWindowEnd - 30;
			long minSec = lo * 60L;
			if (maxSec < minSec) return maxSec >= 120 ? maxSec : 0;
			return minSec + (long) (rnd.NextDouble() * (maxSec - minSec));
		}

		public static DeutPlan PlanDeuterium(long planetDeut, long fuel, int flightsWanted, long capacity) {
			var p = new DeutPlan();
			if (fuel <= 0) { p.FlightsPossible = long.MaxValue; p.Level = DeutLevel.Ok; return p; }
			p.FlightsPossible = planetDeut / fuel;
			if (p.FlightsPossible < 1) { p.Level = DeutLevel.None; return p; }
			long want = fuel * Math.Max(0, flightsWanted);
			p.Payload = Math.Max(0, Math.Min(Math.Min(want, planetDeut - fuel), Math.Max(0, capacity)));
			p.Level = p.FlightsPossible == 1 ? DeutLevel.Last : p.FlightsPossible <= Math.Max(1, flightsWanted) ? DeutLevel.Low : DeutLevel.Ok;
			return p;
		}

		public static bool AttackBlocks(IEnumerable<long> arriveInSeconds, long roundTripSeconds, int marginSeconds) =>
			arriveInSeconds.Any(a => a <= roundTripSeconds + marginSeconds);

		public static Dictionary<string, long> PickShips(Func<string, long> available, Dictionary<string, long> wanted) {
			var r = new Dictionary<string, long>();
			foreach (var kv in wanted) {
				long have = Math.Max(0, available(kv.Key));
				long n = kv.Value <= 0 ? have : Math.Min(kv.Value, have);
				if (n > 0) r[kv.Key] = n;
			}
			return r;
		}

		public static bool CanHarvest(IDictionary<string, long> ships) =>
			ships.TryGetValue("Recycler", out var r) && r > 0 || ships.TryGetValue("Pathfinder", out var p) && p > 0;
	}
}
