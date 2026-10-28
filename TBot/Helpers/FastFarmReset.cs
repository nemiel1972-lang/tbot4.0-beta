using System;

namespace Tbot.Helpers {
	public static class FastFarmReset {
		public const int GapMinutes = 10;

		public sealed class Entscheidung {
			public string Ausloeser;
			public bool Zuruecksetzen;
			public DateTime? GesperrtBis;
		}

		public static Entscheidung Entscheide(bool aktiv, bool prozessStart, DateTime? naechsterLaufUtc, DateTime? letzterResetUtc, DateTime jetztUtc, double minHours) {
			var e = new Entscheidung();
			if (prozessStart) {
				e.Ausloeser = "start";
			} else if (!naechsterLaufUtc.HasValue || naechsterLaufUtc.Value == default(DateTime) || (jetztUtc - naechsterLaufUtc.Value).TotalMinutes > GapMinutes) {
				e.Ausloeser = "reactivation";
			}
			if (e.Ausloeser == null || !aktiv)
				return e;
			if (minHours > 0 && letzterResetUtc.HasValue && (jetztUtc - letzterResetUtc.Value).TotalHours < minHours) {
				e.GesperrtBis = letzterResetUtc.Value.AddHours(minHours);
				return e;
			}
			e.Zuruecksetzen = true;
			return e;
		}
	}
}
