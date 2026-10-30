using System;
using Tbot.Workers;
using Xunit;

namespace TBot.Tests {
	public class ResourceLedgerBundleTests {
		[Fact]
		public void SurplusStaysReservedUntilFollowingBuildsAreDone() {
			string key = "bundle_" + Guid.NewGuid().ToString("N");
			long cel = 1; string o = "AutoMine"; var now = DateTime.UtcNow;
			Func<int, LedgerFleetInfo> fliegt = id => new LedgerFleetInfo(true, false, true);
			Func<int, LedgerFleetInfo> weg = id => new LedgerFleetInfo(false, false, false);

			var p1 = ResourceLedger.Plan(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), new LedgerAmt(0, 0, 0), now, fliegt, null, false);
			Assert.Equal(100, p1.Missing.M);

			ResourceLedger.RegisterFlight(key, cel, o, 77, new LedgerAmt(250, 0, 0), now.AddMinutes(5), now, null);
			var p2 = ResourceLedger.Plan(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), new LedgerAmt(0, 0, 0), now, fliegt, null, false);
			Assert.True(p2.Missing.IsZero());

			var p3 = ResourceLedger.Plan(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), new LedgerAmt(250, 0, 0), now.AddMinutes(6), weg, null, false);
			Assert.True(p3.Funded);

			ResourceLedger.OnBuilt(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), now.AddMinutes(7), null);
			var p4 = ResourceLedger.Plan(key, cel, o, "Mine 11", new LedgerAmt(100, 0, 0), new LedgerAmt(150, 0, 0), now.AddMinutes(8), weg, null, false);
			Assert.True(p4.Funded);
			Assert.True(p4.Missing.IsZero());
		}

		[Fact]
		public void NormalClaimIsRemovedAfterBuildAsBefore() {
			string key = "bundle_" + Guid.NewGuid().ToString("N");
			long cel = 2; string o = "AutoMine"; var now = DateTime.UtcNow;
			Func<int, LedgerFleetInfo> weg = id => new LedgerFleetInfo(false, false, false);
			ResourceLedger.Plan(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), new LedgerAmt(100, 0, 0), now, weg, null, false);
			ResourceLedger.OnBuilt(key, cel, o, "Mine 10", new LedgerAmt(100, 0, 0), now.AddMinutes(1), null);
			var p = ResourceLedger.Plan(key, cel, o, "Mine 11", new LedgerAmt(500, 0, 0), new LedgerAmt(0, 0, 0), now.AddMinutes(2), weg, null, false);
			Assert.Equal(500, p.Missing.M);
		}
	}
}
