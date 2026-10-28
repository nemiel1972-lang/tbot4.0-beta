using System;
using Tbot.Workers;
using Xunit;

namespace TBot.Tests {
	public class LogistikRechnerTests {
		[Fact]
		public void Gesamtdauer_EinSlot_SummiertAlle() {
			Assert.Equal(600, LogistikRechner.Gesamtdauer(new long[] { 100, 200, 300 }, 1));
		}

		[Fact]
		public void Gesamtdauer_GenugSlots_LaengsterFlug() {
			Assert.Equal(300, LogistikRechner.Gesamtdauer(new long[] { 100, 200, 300 }, 3));
			Assert.Equal(300, LogistikRechner.Gesamtdauer(new long[] { 100, 200, 300 }, 10));
		}

		[Fact]
		public void Gesamtdauer_ZweiSlots_VerteiltLaengsteZuerst() {
			Assert.Equal(300, LogistikRechner.Gesamtdauer(new long[] { 100, 200, 300 }, 2));
			Assert.Equal(700, LogistikRechner.Gesamtdauer(new long[] { 500, 400, 300, 200 }, 2));
		}

		[Fact]
		public void Gesamtdauer_KeinSlotOderKeinFlug() {
			Assert.Equal(-1, LogistikRechner.Gesamtdauer(new long[] { 100 }, 0));
			Assert.Equal(0, LogistikRechner.Gesamtdauer(new long[0], 0));
		}

		[Fact]
		public void Wellen_RundetAuf() {
			Assert.Equal(3, LogistikRechner.Wellen(7, 3));
			Assert.Equal(1, LogistikRechner.Wellen(3, 3));
			Assert.Equal(0, LogistikRechner.Wellen(0, 3));
			Assert.Equal(-1, LogistikRechner.Wellen(2, 0));
		}

		[Fact]
		public void NaechsteUhrzeit_HeuteOderMorgen() {
			var jetzt = new DateTime(2026, 10, 24, 16, 0, 0);
			Assert.Equal(new DateTime(2026, 10, 25, 5, 0, 0), LogistikRechner.NaechsteUhrzeit("5:00", jetzt));
			Assert.Equal(new DateTime(2026, 10, 24, 23, 30, 0), LogistikRechner.NaechsteUhrzeit("23:30", jetzt));
			Assert.Null(LogistikRechner.NaechsteUhrzeit("abc", jetzt));
			Assert.Null(LogistikRechner.NaechsteUhrzeit("", jetzt));
		}

		[Fact]
		public void SpaetesterStart_ZiehtDauerUndReserveAb() {
			var schlaf = new DateTime(2026, 10, 25, 5, 0, 0);
			Assert.Equal(new DateTime(2026, 10, 25, 2, 40, 0), LogistikRechner.SpaetesterStart(schlaf, 2 * 3600, 20));
			Assert.Null(LogistikRechner.SpaetesterStart(schlaf, -1, 20));
		}

		[Fact]
		public void FehlendeSchiffe_RundetAuf() {
			Assert.Equal(0, LogistikRechner.FehlendeSchiffe(1000, 1000, 5000));
			Assert.Equal(1, LogistikRechner.FehlendeSchiffe(1001, 1000, 5000));
			Assert.Equal(3, LogistikRechner.FehlendeSchiffe(15000, 0, 5000));
			Assert.Equal(0, LogistikRechner.FehlendeSchiffe(100, 0, 0));
		}

		[Fact]
		public void Hochrechnung_BegrenztDurchSpeicher() {
			Assert.Equal(1240, LogistikRechner.Hochrechnung(1000, 10, 24, 0));
			Assert.Equal(1100, LogistikRechner.Hochrechnung(1000, 10, 24, 1100));
			Assert.Equal(2000, LogistikRechner.Hochrechnung(2000, 10, 24, 1500));
		}
	}
}
