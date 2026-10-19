using System;
using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class DefenderRecheckTests {
		private static readonly DateTime Jetzt = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
		private static readonly TimeSpan Abstand = TimeSpan.FromSeconds(120);

		private static RecheckAction Pruefe(int[] arriveIn, bool? unterAngriff, int trackedInSek) =>
			DefenderRecheck.Evaluate(arriveIn, unterAngriff, Jetzt, Jetzt.AddSeconds(trackedInSek), Abstand, Jetzt, out _);

		[Fact]
		public void Leere_Liste_und_kein_Angriff_bricht_ab() => Assert.Equal(RecheckAction.Cancel, Pruefe(new int[0], false, 130));

		[Theory]
		[InlineData(true)]
		[InlineData(null)]
		public void Leere_Liste_aber_Angriff_gemeldet_sendet_trotzdem(bool? unterAngriff) => Assert.Equal(RecheckAction.Proceed, Pruefe(new int[0], unterAngriff, 130));

		[Fact]
		public void Gleiche_Ankunft_sendet() => Assert.Equal(RecheckAction.Proceed, Pruefe(new[] { 130 }, true, 130));

		[Fact]
		public void Fruehere_Ankunft_sendet() => Assert.Equal(RecheckAction.Proceed, Pruefe(new[] { 90 }, true, 130));

		[Fact]
		public void Spaetere_Ankunft_mit_Zeit_bis_zum_neuen_Termin_verschiebt() {
			var a = DefenderRecheck.Evaluate(new[] { 400 }, true, Jetzt, Jetzt.AddSeconds(130), Abstand, Jetzt, out var neu);
			Assert.Equal(RecheckAction.Postpone, a);
			Assert.Equal(Jetzt.AddSeconds(400), neu);
		}

		[Fact]
		public void Spaetere_Ankunft_aber_neuer_Termin_schon_erreicht_sendet() {
			var jetzt = Jetzt.AddSeconds(60);
			var a = DefenderRecheck.Evaluate(new[] { 100 }, true, jetzt, Jetzt.AddSeconds(130), Abstand, jetzt, out _);
			Assert.Equal(RecheckAction.Proceed, a);
		}

		[Fact]
		public void Kleine_Abweichung_unter_Toleranz_sendet() => Assert.Equal(RecheckAction.Proceed, Pruefe(new[] { 133 }, true, 130));

		[Fact]
		public void Mehrere_Angriffe_zaehlt_der_fruehste() => Assert.Equal(RecheckAction.Proceed, Pruefe(new[] { 500, 100 }, true, 130));
	}
}
