using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class SmartExpeditionFleetTests {
		private static Ships Stock(long lc = 0, long sc = 0, long probe = 0, long pathfinder = 0, long reaper = 0, long destroyer = 0) {
			var s = new Ships();
			s.SetAmount(Buildables.LargeCargo, lc);
			s.SetAmount(Buildables.SmallCargo, sc);
			s.SetAmount(Buildables.EspionageProbe, probe);
			s.SetAmount(Buildables.Pathfinder, pathfinder);
			s.SetAmount(Buildables.Reaper, reaper);
			s.SetAmount(Buildables.Destroyer, destroyer);
			return s;
		}

		private static SmartExpeditionInput Input(Ships avail, int pending, long ideal = 3000, long extra = 0, Buildables mix = Buildables.SmallCargo) => new SmartExpeditionInput {
			Available = avail, Primary = Buildables.LargeCargo, Mix = mix, Pending = pending,
			IdealPrimary = ideal, ExtraCapacity = extra, CapacityPrimary = 50000, CapacityMix = 10000
		};

		[Fact]
		public void EnoughStockSendsIdealAmountWithAllEscorts() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 100000, sc: 50000, probe: 5, pathfinder: 5, reaper: 5), 1));
			Assert.Equal(3000, fleet.GetAmount(Buildables.LargeCargo));
			Assert.Equal(0, fleet.GetAmount(Buildables.SmallCargo));
			Assert.Equal(1, fleet.GetAmount(Buildables.EspionageProbe));
			Assert.Equal(1, fleet.GetAmount(Buildables.Pathfinder));
			Assert.Equal(1, fleet.GetAmount(Buildables.Reaper));
		}

		[Fact]
		public void ScarceStockIsSplitEquallyAcrossAllPlannedExpeditions() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 13000, sc: 50000, probe: 13, pathfinder: 13, reaper: 13), 13));
			Assert.Equal(1000, fleet.GetAmount(Buildables.LargeCargo));
			Assert.Equal(3846, fleet.GetAmount(Buildables.SmallCargo));
		}

		[Fact]
		public void MixOffLeavesMissingCapacityUnfilled() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 13000, sc: 50000), 13, mix: Buildables.Null));
			Assert.Equal(1000, fleet.GetAmount(Buildables.LargeCargo));
			Assert.Equal(0, fleet.GetAmount(Buildables.SmallCargo));
		}

		[Fact]
		public void OnlySmallCargoStillMakesAFleet() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 0, sc: 30000, probe: 1), 3, ideal: 100));
			Assert.Equal(0, fleet.GetAmount(Buildables.LargeCargo));
			Assert.Equal(500, fleet.GetAmount(Buildables.SmallCargo));
			Assert.Equal(1, fleet.GetAmount(Buildables.EspionageProbe));
		}

		[Fact]
		public void NoCargoMeansNoExpedition() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 0, sc: 0, probe: 5, pathfinder: 5, reaper: 5), 1));
			Assert.True(fleet.IsEmpty());
		}

		[Fact]
		public void EscortsOnlyWhenAvailable() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 100000), 1));
			Assert.Equal(0, fleet.GetAmount(Buildables.EspionageProbe));
			Assert.Equal(0, fleet.GetAmount(Buildables.Pathfinder));
			Assert.Equal(0, fleet.GetAmount(Buildables.Reaper));
		}

		[Fact]
		public void ReaperFirstThenOtherCombatShip() {
			var withReaper = SmartExpeditionFleet.Build(Input(Stock(lc: 100000, reaper: 1, destroyer: 9), 1));
			Assert.Equal(1, withReaper.GetAmount(Buildables.Reaper));
			Assert.Equal(0, withReaper.GetAmount(Buildables.Destroyer));
			var noReaper = SmartExpeditionFleet.Build(Input(Stock(lc: 100000, destroyer: 9), 1));
			Assert.Equal(1, noReaper.GetAmount(Buildables.Destroyer));
		}

		[Fact]
		public void PathfinderGoesAlongInAdditionToTheCombatShip() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 100000, pathfinder: 1, reaper: 1), 1));
			Assert.Equal(1, fleet.GetAmount(Buildables.Pathfinder));
			Assert.Equal(1, fleet.GetAmount(Buildables.Reaper));
		}

		[Fact]
		public void FuelToCarryAddsCargoCapacity() {
			var fleet = SmartExpeditionFleet.Build(Input(Stock(lc: 100000), 1, extra: 1000000));
			Assert.Equal(3020, fleet.GetAmount(Buildables.LargeCargo));
		}

		[Fact]
		public void SharesStayEqualWhileTheStockShrinks() {
			long first = SmartExpeditionFleet.Build(Input(Stock(lc: 100), 13, ideal: 1000, mix: Buildables.Null)).GetAmount(Buildables.LargeCargo);
			long second = SmartExpeditionFleet.Build(Input(Stock(lc: 100 - first), 12, ideal: 1000, mix: Buildables.Null)).GetAmount(Buildables.LargeCargo);
			Assert.Equal(7, first);
			Assert.True(second >= first);
		}
	}
}
