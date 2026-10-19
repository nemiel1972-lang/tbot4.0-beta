using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Services {
	public class DefenderRecheckConfig {
		public bool Active = false;
		public int TimeoutSeconds = 4;
	}

	public enum RecheckAction {
		Proceed,
		Postpone,
		Cancel
	}

	public static class DefenderRecheck {
		public const int PostponeToleranceSeconds = 5;

		public static RecheckAction Evaluate(IReadOnlyCollection<int> arriveIn, bool? stillUnderAttack, DateTime fetchedUtc, DateTime trackedImpactUtc, TimeSpan safetyMargin, DateTime nowUtc, out DateTime newImpactUtc) {
			newImpactUtc = trackedImpactUtc;
			if (arriveIn == null || arriveIn.Count == 0) {
				return stillUnderAttack == false ? RecheckAction.Cancel : RecheckAction.Proceed;
			}
			newImpactUtc = fetchedUtc.AddSeconds(arriveIn.Min());
			if (newImpactUtc > trackedImpactUtc.AddSeconds(PostponeToleranceSeconds)) {
				DateTime dispatchAt = newImpactUtc - safetyMargin;
				if (nowUtc < dispatchAt.AddMilliseconds(-500))
					return RecheckAction.Postpone;
			}
			return RecheckAction.Proceed;
		}
	}
}
