using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public static class ExpeditionDiagnostics {
		public static string ExplainNoSend(int expTotal, int expInUse, int free, int total, int slotsToLeaveFree,
			bool waitForAll, bool waitForMajority, int? prioritySlots) {
			int expFree = expTotal - expInUse;
			int maxSlots = Math.Max(0, total - slotsToLeaveFree);
			int count = Math.Min(Math.Min(expFree, free), maxSlots);

			if (expFree <= 0)
				return $"all expedition slots are in use ({expInUse}/{expTotal}).";
			if (free <= 0)
				return $"no free fleet slot ({total - free}/{total} in use, expedition slots free: {expFree}).";
			if (slotsToLeaveFree > 0 && free <= slotsToLeaveFree)
				return $"only {free} fleet slot(s) free and General.SlotsToLeaveFree={slotsToLeaveFree} keeps them back (total {total}).";
			if (maxSlots <= 0 || count <= 0)
				return $"fleet slots are held back by General.SlotsToLeaveFree={slotsToLeaveFree} (total {total}, free {free}).";

			if (waitForAll && expInUse > 0)
				return $"WaitForAllExpeditions is on and {expInUse} expedition(s) are still away.";
			if (waitForMajority && count < Math.Round(expTotal / 2D, 0, MidpointRounding.ToZero) + 1D)
				return $"WaitForMajorityOfExpeditions is on: {count} of {expTotal} expedition slots free, more than half are needed.";

			if (prioritySlots.HasValue && prioritySlots.Value <= 0)
				return $"the slot priority leaves no slot for expeditions (free {free}/{total}, expedition slots free {expFree}, SlotsToLeaveFree {slotsToLeaveFree}).";
			return null;
		}

		public sealed class RepeatLimiter {
			private readonly object _lock = new();
			private readonly Dictionary<string, (string Text, DateTime At)> _last = new();

			public bool ShouldLog(string key, string text, DateTime nowUtc, TimeSpan every) {
				lock (_lock) {
					if (_last.TryGetValue(key, out var last) && last.Text == text && nowUtc - last.At < every)
						return false;
					_last[key] = (text, nowUtc);
					return true;
				}
			}

			public void Reset(string key) {
				lock (_lock) { _last.Remove(key); }
			}
		}

		public static long? NextReturnSeconds(IEnumerable<Fleet> fleets) {
			if (fleets == null)
				return null;
			var valid = fleets.Where(f => f != null && f.BackIn.HasValue).Select(f => Math.Max(0L, f.BackIn.Value)).ToList();
			return valid.Count == 0 ? null : valid.Min();
		}

		public static string DeutKey(string instanceAlias, string universe, string coordinate) {
			return $"{instanceAlias ?? "?"}|{universe ?? "?"}|{coordinate ?? "?"}";
		}
	}
}
