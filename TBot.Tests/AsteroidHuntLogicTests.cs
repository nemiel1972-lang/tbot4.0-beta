using System;
using System.Collections.Generic;
using Tbot.Workers;
using Xunit;

namespace TBot.Tests {
	public class AsteroidHuntLogicTests {
		private static readonly DateTime Jetzt = new DateTime(2026, 10, 19, 1, 0, 0, DateTimeKind.Utc);
		private static readonly AsteroidHuntConfig Cfg = new AsteroidHuntConfig { Active = true, MaxAttemptsPerDay = 1 };

		[Fact]
		public void Neuer_Tag_setzt_die_Zaehler_zurueck() {
			var s = new HuntState { Day = "2026-10-18", AttemptsToday = 1, FoundToday = true };
			AsteroidHuntLogic.RollDay(s, "2026-10-19");
			Assert.Equal("2026-10-19", s.Day);
			Assert.Equal(0, s.AttemptsToday);
			Assert.False(s.FoundToday);
		}

		[Fact]
		public void Gleicher_Tag_laesst_die_Zaehler_stehen() {
			var s = new HuntState { Day = "2026-10-19", AttemptsToday = 1 };
			AsteroidHuntLogic.RollDay(s, "2026-10-19");
			Assert.Equal(1, s.AttemptsToday);
		}

		[Fact]
		public void Ohne_Versuch_wird_gestartet() => Assert.Equal(HuntAction.Start, AsteroidHuntLogic.Decide(new HuntState(), Jetzt, Cfg));

		[Fact]
		public void Nach_Fund_ist_der_Tag_erledigt() =>
			Assert.Equal(HuntAction.DoneToday, AsteroidHuntLogic.Decide(new HuntState { FoundToday = true, AttemptsToday = 1 }, Jetzt, Cfg));

		[Fact]
		public void Tageslimit_erreicht_ist_erledigt() =>
			Assert.Equal(HuntAction.DoneToday, AsteroidHuntLogic.Decide(new HuntState { AttemptsToday = 1 }, Jetzt, Cfg));

		[Fact]
		public void Zweiter_Versuch_nur_wenn_erlaubt() {
			var s = new HuntState { AttemptsToday = 1 };
			Assert.Equal(HuntAction.Start, AsteroidHuntLogic.Decide(s, Jetzt, new AsteroidHuntConfig { MaxAttemptsPerDay = 2 }));
		}

		[Fact]
		public void Laufender_Angriff_wartet_bis_Ankunft_plus_Abstand() {
			var s = new HuntState { Pending = new HuntPending { ArrivalUtc = Jetzt.AddSeconds(100).ToString("o") } };
			Assert.Equal(HuntAction.Wait, AsteroidHuntLogic.Decide(s, Jetzt, Cfg));
			Assert.Equal(HuntAction.Wait, AsteroidHuntLogic.Decide(s, Jetzt.AddSeconds(120), Cfg));
			Assert.Equal(HuntAction.Resolve, AsteroidHuntLogic.Decide(s, Jetzt.AddSeconds(141), Cfg));
		}

		[Fact]
		public void Neue_Items_erkennt_Zuwachs_und_neue_Eintraege() {
			var vorher = new Dictionary<string, long> { ["a"] = 2, ["b"] = 1 };
			var nachher = new Dictionary<string, long> { ["a"] = 2, ["b"] = 2, ["c"] = 1 };
			Assert.Equal(new[] { "b", "c" }, AsteroidHuntLogic.NewItems(vorher, nachher));
		}

		[Fact]
		public void Keine_neuen_Items_bei_gleichem_oder_kleinerem_Bestand() {
			var vorher = new Dictionary<string, long> { ["a"] = 2 };
			Assert.Empty(AsteroidHuntLogic.NewItems(vorher, new Dictionary<string, long> { ["a"] = 1 }));
			Assert.Empty(AsteroidHuntLogic.NewItems(vorher, new Dictionary<string, long> { ["a"] = 2 }));
		}

		[Theory]
		[InlineData(100, 0, 3, 499, 100)]
		[InlineData(100, 1, 3, 499, 101)]
		[InlineData(100, 2, 3, 499, 99)]
		[InlineData(100, 3, 3, 499, 102)]
		[InlineData(100, 4, 3, 499, 98)]
		[InlineData(100, 5, 3, 499, 103)]
		[InlineData(100, 6, 3, 499, 97)]
		[InlineData(100, 7, 3, 499, 101)]
		[InlineData(1, 2, 3, 499, 499)]
		[InlineData(499, 1, 3, 499, 1)]
		[InlineData(100, 3, 0, 499, 100)]
		public void Zielsystem(int origin, int versuch, int maxOffset, int systeme, int erwartet) =>
			Assert.Equal(erwartet, AsteroidHuntLogic.TargetSystem(origin, versuch, maxOffset, systeme));

		[Fact]
		public void Sekunden_bis_zum_naechsten_Tag() {
			Assert.Equal(3600, AsteroidHuntLogic.SecondsUntilNextDay(new DateTime(2026, 10, 19, 23, 0, 0), 300));
			Assert.Equal(300, AsteroidHuntLogic.SecondsUntilNextDay(new DateTime(2026, 10, 19, 23, 59, 0), 300));
		}
	
		[Fact]
		public void Unbegrenzt_versucht_weiter_bis_zum_Fund_oder_zur_Suchsperre() {
			var cfg = new AsteroidHuntConfig { Active = true };
			Assert.Equal(HuntAction.Start, AsteroidHuntLogic.Decide(new HuntState { AttemptsToday = 25 }, Jetzt, cfg));
			Assert.Equal(HuntAction.DoneToday, AsteroidHuntLogic.Decide(new HuntState { AttemptsToday = AsteroidHuntLogic.HardAttemptCap }, Jetzt, cfg));
			Assert.Equal(HuntAction.DoneToday, AsteroidHuntLogic.Decide(new HuntState { SearchExhausted = true }, Jetzt, cfg));
			Assert.Equal(HuntAction.DoneToday, AsteroidHuntLogic.Decide(new HuntState { FoundToday = true }, Jetzt, cfg));
		}

		[Theory]
		[InlineData("Bericht von Surprisma Item geborgen: Deuterium-Booster Gold", ReportKind.Found)]
		[InlineData("Eine schnelle Untersuchung des Planeten hat leider kein Ergebnis geliefert und so ist unsere Flotte wieder auf dem Rückweg. Wir sollten es aber noch einmal versuchen.", ReportKind.Retry)]
		[InlineData("Auch nach weiteren Versuchen bleibt unsere Flotte leider erfolglos. Wir sollten eine Nacht darüber schlafen und es morgen nochmal versuchen.", ReportKind.Locked)]
		[InlineData("Auch nach weiteren Versuchen bleibt unsere Flotte leider erfolglos. Wir sollten eine Nacht dar&uuml;ber schlafen und es morgen nochmal versuchen.", ReportKind.Locked)]
		[InlineData("Auch nach weiteren Versuchen bleibt unsere Flotte leider erfolglos. Wir sollten eine Nacht darueber  schlafen und es morgen nochmal versuchen.", ReportKind.Locked)]
		[InlineData("", ReportKind.Unknown)]
		[InlineData("irgendwas anderes", ReportKind.Unknown)]
		public void Bericht_wird_eingeordnet(string text, ReportKind erwartet) {
			Assert.Equal(erwartet, AsteroidHuntLogic.Classify(text));
		}
}
}
