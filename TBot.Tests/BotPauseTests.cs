using System;
using System.Collections.Generic;
using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class BotPauseTests {
		private static readonly DateTime Jetzt = new DateTime(2026, 10, 18, 22, 0, 0);

		private static PendingRecall Rueckruf(int minutenAb, string koord = "P:1:2:3") => new PendingRecall { At = Jetzt.AddMinutes(minutenAb), Coordinate = koord };

		[Theory]
		[InlineData(null, 15)]
		[InlineData(0, 1)]
		[InlineData(-5, 1)]
		[InlineData(10, 10)]
		[InlineData(999, 120)]
		public void ClampMinutes_begrenzt_auf_1_bis_Maximum(int? gewuenscht, int erwartet) {
			Assert.Equal(erwartet, BotPause.ClampMinutes(gewuenscht, new BotPauseConfig()));
		}

		[Fact]
		public void Ohne_Rueckrufe_darf_die_Pause_starten() {
			Assert.Null(BotPause.FindBlockingRecall(new List<PendingRecall>(), Jetzt, 15, 2));
			Assert.Null(BotPause.FindBlockingRecall(null, Jetzt, 15, 2));
		}

		[Theory]
		[InlineData(10, true)]
		[InlineData(17, true)]
		[InlineData(18, false)]
		[InlineData(120, false)]
		[InlineData(-3, true)]
		[InlineData(-30, false)]
		public void Rueckruf_im_Pausenfenster_blockiert(int minutenAb, bool blockiert) {
			var r = Rueckruf(minutenAb);
			Assert.Equal(blockiert, BotPause.FindBlockingRecall(new[] { r }, Jetzt, 15, 2) != null);
		}

		[Fact]
		public void Der_fruehste_Rueckruf_wird_gemeldet() {
			var frueh = Rueckruf(10, "M:1:1:1");
			var spaet = Rueckruf(14, "P:2:2:2");
			Assert.Same(frueh, BotPause.FindBlockingRecall(new[] { spaet, frueh }, Jetzt, 15, 2));
		}

		[Fact]
		public void Ablehnungstext_nennt_Koordinate_Zeit_und_Minuten() {
			Assert.Equal("Pause rejected: recall of the fleet from P:1:2:3 is due in 10 min (22:10).", BotPause.RejectionText(Rueckruf(10), Jetzt));
			Assert.Contains("now due", BotPause.RejectionText(Rueckruf(-3), Jetzt));
		}

		[Theory]
		[InlineData(10, 8)]
		[InlineData(3, 1)]
		[InlineData(2, 0)]
		[InlineData(1, 0)]
		public void MaxPossibleMinutes_endet_vor_dem_Rueckruf(int minutenAb, int erwartet) {
			Assert.Equal(erwartet, BotPause.MaxPossibleMinutes(new[] { Rueckruf(minutenAb) }, Jetzt, 2));
		}

		[Fact]
		public void MaxPossibleMinutes_ohne_Rueckruf_ist_unbegrenzt() {
			Assert.Equal(int.MaxValue, BotPause.MaxPossibleMinutes(new List<PendingRecall>(), Jetzt, 2));
		}
	}
}
