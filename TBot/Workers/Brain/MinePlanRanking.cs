using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers.Brain {
	public enum RankMode {
		Payback,
		Cheapest,
		Npv
	}

	public class RankConfig {
		public RankMode Mode = RankMode.Payback;
		public int HorizonDays = 90;
		public int MaxPaybackDays = 1000;
		public bool OnlySensible = false;
		public bool IncludeResearch = true;
	}

	public static class MinePlanRanking {
		public static RankMode ParseMode(string text) {
			if (!string.IsNullOrWhiteSpace(text) && Enum.TryParse(text.Trim(), true, out RankMode m))
				return m;
			return RankMode.Payback;
		}

		public static float PaybackDays(long cost, float gainPerDay) => gainPerDay > 0 && cost > 0 ? cost / gainPerDay : 0;

		public static float Npv(long cost, float gainPerDay, int horizonDays) => gainPerDay * horizonDays - cost;

		public static bool IsSensible(long cost, float gainPerDay, int maxPaybackDays) {
			if (gainPerDay <= 0)
				return false;
			return cost / gainPerDay <= maxPaybackDays;
		}

		public static List<T> Rank<T>(IEnumerable<T> steps, Func<T, long> cost, Func<T, float> gain, Func<T, bool> isResearch, RankConfig cfg, int take) {
			cfg ??= new RankConfig();
			IEnumerable<T> q = steps.Where(s => gain(s) > 0);
			if (!cfg.IncludeResearch)
				q = q.Where(s => !isResearch(s));
			if (cfg.OnlySensible)
				q = q.Where(s => IsSensible(cost(s), gain(s), cfg.MaxPaybackDays));
			switch (cfg.Mode) {
				case RankMode.Cheapest:
					return q.OrderBy(s => cost(s)).ThenBy(s => PaybackDays(cost(s), gain(s))).Take(take).ToList();
				case RankMode.Npv:
					return q.Where(s => Npv(cost(s), gain(s), cfg.HorizonDays) > 0)
						.OrderByDescending(s => Npv(cost(s), gain(s), cfg.HorizonDays)).Take(take).ToList();
				default:
					return q.OrderBy(s => PaybackDays(cost(s), gain(s))).Take(take).ToList();
			}
		}
	}
}
