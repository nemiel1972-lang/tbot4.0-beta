using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Services {
	public class BotPauseConfig {
		public bool Active = true;
		public int DurationMinutes = 15;
		public int RecallMarginMinutes = 2;
		public int MaxDurationMinutes = 120;
		public bool StopDefender = false;
		public int AttackMarginMinutes = 15;
	}

	public class PendingRecall {
		public DateTime At { get; set; }
		public string Coordinate { get; set; }
	}

	public static class BotPause {
		public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

		public static int ClampMinutes(int? wished, BotPauseConfig c) {
			int m = wished ?? c.DurationMinutes;
			m = Math.Max(1, m);
			return Math.Min(m, Math.Max(1, c.MaxDurationMinutes));
		}

		public static PendingRecall FindBlockingRecall(IEnumerable<PendingRecall> recalls, DateTime gameNow, int minutes, int marginMinutes) {
			if (recalls == null)
				return null;
			DateTime limit = gameNow.AddMinutes(Math.Max(1, minutes) + Math.Max(0, marginMinutes));
			DateTime oldest = gameNow - StaleAfter;
			return recalls
				.Where(r => r != null && r.At >= oldest && r.At <= limit)
				.OrderBy(r => r.At)
				.FirstOrDefault();
		}

		public static int? FindBlockingAttackSeconds(IEnumerable<int> arriveInSeconds, int minutes, int attackMarginMinutes, bool stopDefender) {
			if (arriveInSeconds == null)
				return null;
			int limit = (Math.Max(0, attackMarginMinutes) + (stopDefender ? Math.Max(1, minutes) : 0)) * 60;
			var hits = arriveInSeconds.Where(s => s > 0 && s <= limit).OrderBy(s => s).ToList();
			return hits.Count == 0 ? (int?) null : hits[0];
		}

		public static bool BlocksMission(Missions m) {
			switch (m) {
				case Missions.Spy:
				case Missions.Attack:
				case Missions.FederalAttack:
				case Missions.Expedition:
				case Missions.Colonize:
				case Missions.Discovery:
				case Missions.Harvest:
				case Missions.Destroy:
				case Missions.MissileAttack:
					return true;
				default:
					return false;
			}
		}

		public static string RejectionText(PendingRecall r, DateTime gameNow) {
			int inMin = (int) Math.Ceiling((r.At - gameNow).TotalMinutes);
			string when = inMin <= 0 ? "now due" : $"due in {inMin} min";
			return $"Pause rejected: recall of the fleet from {r.Coordinate ?? "?"} is {when} ({r.At:HH:mm}).";
		}

		public static int MaxPossibleMinutes(IEnumerable<PendingRecall> recalls, DateTime gameNow, int marginMinutes) {
			if (recalls == null)
				return int.MaxValue;
			DateTime oldest = gameNow - StaleAfter;
			var first = recalls.Where(r => r != null && r.At >= oldest).OrderBy(r => r.At).FirstOrDefault();
			if (first == null)
				return int.MaxValue;
			double free = (first.At - gameNow).TotalMinutes - Math.Max(0, marginMinutes);
			return free < 1 ? 0 : (int) Math.Floor(free);
		}
	}
}
