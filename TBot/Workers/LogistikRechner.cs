using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers {
	public static class LogistikRechner {
		public static long Gesamtdauer(IEnumerable<long> dauernSek, int slots) {
			var d = (dauernSek ?? Enumerable.Empty<long>()).Where(x => x > 0).OrderByDescending(x => x).ToList();
			if (d.Count == 0)
				return 0;
			if (slots <= 0)
				return -1;
			var last = new long[Math.Min(slots, d.Count)];
			foreach (long x in d) {
				int idx = 0;
				for (int i = 1; i < last.Length; i++)
					if (last[i] < last[idx])
						idx = i;
				last[idx] += x;
			}
			return last.Max();
		}

		public static int Wellen(int fluege, int slots) {
			if (fluege <= 0)
				return 0;
			if (slots <= 0)
				return -1;
			return (fluege + slots - 1) / slots;
		}

		public static DateTime? NaechsteUhrzeit(string hhmm, DateTime jetzt) {
			if (string.IsNullOrWhiteSpace(hhmm) || !TimeSpan.TryParse(hhmm.Trim(), out TimeSpan t) || t < TimeSpan.Zero || t >= TimeSpan.FromHours(24))
				return null;
			DateTime heute = jetzt.Date + t;
			return heute > jetzt ? heute : heute.AddDays(1);
		}

		public static DateTime? SpaetesterStart(DateTime schlaf, long gesamtdauerSek, int reserveMinuten) {
			if (gesamtdauerSek < 0)
				return null;
			return schlaf - TimeSpan.FromSeconds(gesamtdauerSek) - TimeSpan.FromMinutes(Math.Max(0, reserveMinuten));
		}

		public static long FehlendeSchiffe(long bedarf, long vorhandeneKapazitaet, long kapazitaetProSchiff) {
			if (bedarf <= vorhandeneKapazitaet || kapazitaetProSchiff <= 0)
				return 0;
			long fehlt = bedarf - Math.Max(0, vorhandeneKapazitaet);
			return (fehlt + kapazitaetProSchiff - 1) / kapazitaetProSchiff;
		}

		public static long Hochrechnung(long bestand, long proStunde, double stunden, long speicher) {
			double v = bestand + Math.Max(0, proStunde) * Math.Max(0.0, stunden);
			long r = (long) Math.Floor(v);
			if (speicher > 0 && r > speicher)
				r = Math.Max(bestand, speicher);
			return Math.Max(bestand, r);
		}

		public static string Dauer(long sek) {
			if (sek < 0)
				return "-";
			var t = TimeSpan.FromSeconds(sek);
			return t.TotalHours >= 1 ? $"{(int) t.TotalHours}h {t.Minutes:00}m" : $"{t.Minutes}m {t.Seconds:00}s";
		}
	}
}
