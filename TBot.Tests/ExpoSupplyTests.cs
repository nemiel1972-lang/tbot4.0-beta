using System;
using System.IO;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class ExpoSupplyTests {
		private static Ships S(long lc = 0, long probe = 0, long reaper = 0, long pf = 0) {
			var s = new Ships();
			s.SetAmount(Buildables.LargeCargo, lc);
			s.SetAmount(Buildables.EspionageProbe, probe);
			s.SetAmount(Buildables.Reaper, reaper);
			s.SetAmount(Buildables.Pathfinder, pf);
			return s;
		}

		[Fact]
		public void MultiplyScalesEveryType() {
			var r = ExpoSupply.Multiply(S(3200, 1, 1, 1), 3);
			Assert.Equal(9600, r.GetAmount(Buildables.LargeCargo));
			Assert.Equal(3, r.GetAmount(Buildables.EspionageProbe));
			Assert.Equal(3, r.GetAmount(Buildables.Pathfinder));
			Assert.True(ExpoSupply.IsEmpty(ExpoSupply.Multiply(S(5), 0)));
		}

		[Fact]
		public void NeedIncludesBackup() {
			var need = ExpoSupply.Multiply(S(100, 1), 4 + 1);
			Assert.Equal(500, need.GetAmount(Buildables.LargeCargo));
			Assert.Equal(5, need.GetAmount(Buildables.EspionageProbe));
		}

		[Fact]
		public void MissingIsPerTypeAndNeverNegative() {
			var m = ExpoSupply.Missing(S(500, 5, 0, 2), S(700, 3, 9, 0));
			Assert.Equal(0, m.GetAmount(Buildables.LargeCargo));
			Assert.Equal(2, m.GetAmount(Buildables.EspionageProbe));
			Assert.Equal(0, m.GetAmount(Buildables.Reaper));
			Assert.Equal(2, m.GetAmount(Buildables.Pathfinder));
		}

		[Fact]
		public void CapLimitsToAvailable() {
			var c = ExpoSupply.Cap(S(100, 5), S(60, 9));
			Assert.Equal(60, c.GetAmount(Buildables.LargeCargo));
			Assert.Equal(5, c.GetAmount(Buildables.EspionageProbe));
		}

		[Fact]
		public void SumAndSubtract() {
			var s = ExpoSupply.Sum(S(10, 1), S(5, 2));
			Assert.Equal(15, s.GetAmount(Buildables.LargeCargo));
			Assert.Equal(3, s.GetAmount(Buildables.EspionageProbe));
			var d = ExpoSupply.Subtract(s, S(20, 1));
			Assert.Equal(0, d.GetAmount(Buildables.LargeCargo));
			Assert.Equal(2, d.GetAmount(Buildables.EspionageProbe));
		}

		[Fact]
		public void AuffuellGrenzeIstZweiDurchgaenge() {
			Assert.Equal(560_000, ExpoSupply.DeuteriumAuffuellGrenze(1_500_000, 30_000, 10_000, 7));
			Assert.Equal(400_000, ExpoSupply.DeuteriumAuffuellGrenze(400_000, 30_000, 10_000, 7));
		}

		[Fact]
		public void DeuteriumTargetMitFlugNimmtDenGroesseren() {
			Assert.Equal(300_000, ExpoSupply.DeuteriumTargetMitFlug(0, 30_000, 10_000, 7) + 20_000);
			Assert.Equal(500_000, ExpoSupply.DeuteriumTargetMitFlug(500_000, 30_000, 10_000, 7));
			Assert.Equal(0, ExpoSupply.DeuteriumTargetMitFlug(0, 0, 0, 5));
		}

		[Fact]
		public void DeuteriumTargetPrefersMinOnMoon() {
			Assert.Equal(500_000, ExpoSupply.DeuteriumTarget(500_000, 30_000, 5));
			Assert.Equal(150_000, ExpoSupply.DeuteriumTarget(0, 30_000, 5));
			Assert.Equal(0, ExpoSupply.DeuteriumTarget(0, 0, 5));
		}

		[Theory]
		[InlineData(0, 5, 1, 0, 13, 5)]
		[InlineData(0, 7, 2, 0, 13, 7)]
		[InlineData(0, 7, 2, 1, 13, 6)]
		[InlineData(3, 7, 2, 1, 13, 3)]
		[InlineData(0, 5, 3, 2, 13, 4)]
		public void ExpeditionsPerOriginMatchesWorker(int perOrigin, int max, int count, int index, int total, int expected) {
			Assert.Equal(expected, ExpoSupply.ExpeditionsFor(perOrigin, max, count, index, total));
		}

		[Fact]
		public void StateRoundTrips() {
			string alias = "expotest_" + Guid.NewGuid().ToString("N");
			string file = Path.Combine(AppContext.BaseDirectory, "data", $"expo_supply_{alias}.json");
			try {
				var e = new ExpoSupplyEntry { Coord = "2:405:8:Moon", Deut = 1234, Since = 99 };
				e.Ships = ExpoSupply.FromShips(S(10, 2));
				ExpoSupply.SaveState(alias, new System.Collections.Generic.List<ExpoSupplyEntry> { e });
				var back = ExpoSupply.LoadState(alias);
				Assert.Single(back);
				Assert.Equal("2:405:8:Moon", back[0].Coord);
				Assert.Equal(1234, back[0].Deut);
				Assert.Equal(10, ExpoSupply.ToShips(back[0].Ships).GetAmount(Buildables.LargeCargo));
				Assert.Empty(ExpoSupply.LoadState("missing_" + alias));
			} finally { try { File.Delete(file); } catch (Exception) { } }
		}
	}
}
