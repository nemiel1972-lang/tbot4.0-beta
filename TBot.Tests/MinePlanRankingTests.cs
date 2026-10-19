using System.Linq;
using Tbot.Workers.Brain;
using Xunit;

namespace TBot.Tests {
	public class MinePlanRankingTests {
		private record Schritt(string Name, long Kosten, float Ertrag, bool Forschung = false);

		private static string[] Rang(Schritt[] s, RankConfig c) =>
			MinePlanRanking.Rank(s, x => x.Kosten, x => x.Ertrag, x => x.Forschung, c, 10).Select(x => x.Name).ToArray();

		private static readonly Schritt[] Beispiel = {
			new("A", 1000, 20),
			new("B", 100000, 1000),
			new("C", 5000, 50),
			new("D", 3000, 0),
			new("F", 2000, 30, true)
		};

		[Fact]
		public void Payback_sortiert_nach_kuerzester_Amortisation() =>
			Assert.Equal(new[] { "A", "F", "B", "C" }, Rang(Beispiel, new RankConfig { Mode = RankMode.Payback }));

		[Fact]
		public void Billigster_sortiert_nach_Kosten() =>
			Assert.Equal(new[] { "A", "F", "C", "B" }, Rang(Beispiel, new RankConfig { Mode = RankMode.Cheapest }));

		[Fact]
		public void Npv_sortiert_nach_Kapitalwert_und_laesst_Verlierer_weg() {
			Assert.Equal(new[] { "A", "F" }, Rang(Beispiel, new RankConfig { Mode = RankMode.Npv, HorizonDays = 90 }));
			Assert.Equal("B", Rang(Beispiel, new RankConfig { Mode = RankMode.Npv, HorizonDays = 365 })[0]);
		}

		[Fact]
		public void Nur_sinnvolle_blendet_zu_lange_Payback_aus() =>
			Assert.Equal(new[] { "A", "F" }, Rang(Beispiel, new RankConfig { Mode = RankMode.Payback, OnlySensible = true, MaxPaybackDays = 80 }));

		[Fact]
		public void Forschung_kann_ausgeschaltet_werden() =>
			Assert.DoesNotContain("F", Rang(Beispiel, new RankConfig { IncludeResearch = false }));

		[Fact]
		public void Schritte_ohne_Ertrag_stehen_nie_in_der_Liste() =>
			Assert.DoesNotContain("D", Rang(Beispiel, new RankConfig { Mode = RankMode.Cheapest }));

		[Theory]
		[InlineData("npv", RankMode.Npv)]
		[InlineData("Cheapest", RankMode.Cheapest)]
		[InlineData("Payback", RankMode.Payback)]
		[InlineData("quatsch", RankMode.Payback)]
		[InlineData(null, RankMode.Payback)]
		public void ParseMode_faellt_im_Zweifel_auf_Payback(string text, RankMode erwartet) =>
			Assert.Equal(erwartet, MinePlanRanking.ParseMode(text));
	}
}
