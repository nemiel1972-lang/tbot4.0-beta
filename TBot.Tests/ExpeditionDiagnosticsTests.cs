using System;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class ExpeditionDiagnosticsTests {

		[Fact]
		public void Alle_Expeditionsslots_belegt() {
			string r = ExpeditionDiagnostics.ExplainNoSend(13, 13, 4, 20, 1, false, false, null);
			Assert.Contains("13/13", r);
		}

		[Fact]
		public void Kein_freier_Flottenslot() {
			string r = ExpeditionDiagnostics.ExplainNoSend(13, 5, 0, 20, 1, false, false, null);
			Assert.Contains("no free fleet slot", r);
		}

		[Fact]
		public void Reserve_SlotsToLeaveFree_haelt_den_letzten_Slot_zurueck() {
			string edge = ExpeditionDiagnostics.ExplainNoSend(13, 5, 1, 20, 1, false, false, 0);
			Assert.Contains("SlotsToLeaveFree=1", edge);
			Assert.False(edge.Contains("priority"));
			Assert.Null(ExpeditionDiagnostics.ExplainNoSend(13, 5, 2, 20, 1, false, false, 1));
			Assert.Null(ExpeditionDiagnostics.ExplainNoSend(13, 5, 1, 20, 0, false, false, 1));
			string r = ExpeditionDiagnostics.ExplainNoSend(2, 0, 2, 3, 3, false, false, null);
			Assert.Contains("SlotsToLeaveFree=3", r);
		}

		[Fact]
		public void WaitForAll_nennt_die_noch_unterwegs_befindlichen_Expeditionen() {
			string r = ExpeditionDiagnostics.ExplainNoSend(13, 4, 9, 20, 1, true, false, null);
			Assert.Contains("WaitForAllExpeditions", r);
			Assert.Contains("4", r);
		}

		[Fact]
		public void WaitForAll_ohne_laufende_Expedition_sperrt_nicht() {
			Assert.Null(ExpeditionDiagnostics.ExplainNoSend(13, 0, 13, 20, 1, true, false, null));
		}

		[Fact]
		public void WaitForMajority_braucht_mehr_als_die_Haelfte_frei() {
			string r = ExpeditionDiagnostics.ExplainNoSend(13, 8, 5, 20, 1, false, true, null);
			Assert.Contains("WaitForMajorityOfExpeditions", r);
			Assert.Null(ExpeditionDiagnostics.ExplainNoSend(13, 6, 7, 20, 1, false, true, null));
		}

		[Fact]
		public void Prioritaet_ohne_Slot_wird_benannt_und_Wert_null_oder_negativ_zaehlt() {
			Assert.Contains("priority", ExpeditionDiagnostics.ExplainNoSend(13, 3, 10, 20, 1, false, false, 0));
			Assert.Contains("priority", ExpeditionDiagnostics.ExplainNoSend(13, 3, 10, 20, 1, false, false, -2));
			Assert.Null(ExpeditionDiagnostics.ExplainNoSend(13, 3, 10, 20, 1, false, false, 2));
		}

		[Fact]
		public void Reihenfolge_Slots_vor_WaitFlags() {
			string r = ExpeditionDiagnostics.ExplainNoSend(13, 13, 5, 20, 1, true, true, null);
			Assert.Contains("13/13", r);
		}

		[Fact]
		public void Gleiche_Meldung_wird_begrenzt_neue_und_abgelaufene_nicht() {
			var l = new ExpeditionDiagnostics.RepeatLimiter();
			var t0 = new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);
			var every = TimeSpan.FromMinutes(30);
			Assert.True(l.ShouldLog("k", "A", t0, every));
			Assert.False(l.ShouldLog("k", "A", t0.AddMinutes(5), every));
			Assert.False(l.ShouldLog("k", "A", t0.AddMinutes(29), every));
			Assert.True(l.ShouldLog("k", "B", t0.AddMinutes(30), every));
			Assert.True(l.ShouldLog("k", "A", t0.AddMinutes(31), every));
			Assert.True(l.ShouldLog("k", "A", t0.AddMinutes(62), every));
		}

		[Fact]
		public void Reset_laesst_denselben_Text_wieder_zu_und_Schluessel_sind_getrennt() {
			var l = new ExpeditionDiagnostics.RepeatLimiter();
			var t0 = DateTime.UtcNow;
			var every = TimeSpan.FromHours(1);
			Assert.True(l.ShouldLog("a", "X", t0, every));
			Assert.True(l.ShouldLog("b", "X", t0, every));
			Assert.False(l.ShouldLog("a", "X", t0.AddMinutes(1), every));
			l.Reset("a");
			Assert.True(l.ShouldLog("a", "X", t0.AddMinutes(2), every));
			Assert.False(l.ShouldLog("b", "X", t0.AddMinutes(2), every));
		}

		private static Fleet F(int? backIn) => new Fleet { BackIn = backIn };

		[Fact]
		public void BackIn_null_wird_uebersprungen_nicht_als_sofort_zurueck_gewertet() {
			Assert.Equal(300L, ExpeditionDiagnostics.NextReturnSeconds(new[] { F(null), F(900), F(300) }));
		}

		[Fact]
		public void Ohne_gueltige_Rueckkehrzeit_ist_das_Ergebnis_null() {
			Assert.Null(ExpeditionDiagnostics.NextReturnSeconds(new[] { F(null), F(null) }));
			Assert.Null(ExpeditionDiagnostics.NextReturnSeconds(Array.Empty<Fleet>()));
			Assert.Null(ExpeditionDiagnostics.NextReturnSeconds(null));
			Assert.Null(ExpeditionDiagnostics.NextReturnSeconds(new Fleet[] { null }));
		}

		[Fact]
		public void Negative_Rueckkehrzeit_zaehlt_als_null_Sekunden_echte_Null_bleibt_null_Sekunden() {
			Assert.Equal(0L, ExpeditionDiagnostics.NextReturnSeconds(new[] { F(-5), F(100) }));
			Assert.Equal(0L, ExpeditionDiagnostics.NextReturnSeconds(new[] { F(0) }));
		}

		[Fact]
		public void Deuterium_Schluessel_trennt_Instanz_und_Universum() {
			string a = ExpeditionDiagnostics.DeutKey("main", "Astrid", "1:2:3");
			Assert.NotEqual(a, ExpeditionDiagnostics.DeutKey("alt", "Astrid", "1:2:3"));
			Assert.NotEqual(a, ExpeditionDiagnostics.DeutKey("main", "Other", "1:2:3"));
			Assert.NotEqual(a, ExpeditionDiagnostics.DeutKey("main", "Astrid", "1:2:4"));
			Assert.Equal(a, ExpeditionDiagnostics.DeutKey("main", "Astrid", "1:2:3"));
			Assert.False(string.IsNullOrEmpty(ExpeditionDiagnostics.DeutKey(null, null, null)));
		}
	}
}
