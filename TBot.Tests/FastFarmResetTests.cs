using System;
using Tbot.Helpers;
using Xunit;

namespace TBot.Tests {
	public class FastFarmResetTests {
		private static readonly DateTime Jetzt = new DateTime(2026, 10, 25, 20, 0, 0, DateTimeKind.Utc);

		[Fact]
		public void BotStartResetsWhenNoLockActive() {
			var e = FastFarmReset.Entscheide(true, true, Jetzt.AddMinutes(1), Jetzt.AddHours(-7), Jetzt, 6);
			Assert.Equal("start", e.Ausloeser);
			Assert.True(e.Zuruecksetzen);
		}

		[Fact]
		public void BotStartIsLockedWithin6Hours() {
			var e = FastFarmReset.Entscheide(true, true, Jetzt, Jetzt.AddHours(-2), Jetzt, 6);
			Assert.Equal("start", e.Ausloeser);
			Assert.False(e.Zuruecksetzen);
			Assert.Equal(Jetzt.AddHours(4), e.GesperrtBis);
		}

		[Fact]
		public void NormalCycleDoesNotTrigger() {
			var e = FastFarmReset.Entscheide(true, false, Jetzt.AddSeconds(-20), null, Jetzt, 6);
			Assert.Null(e.Ausloeser);
			Assert.False(e.Zuruecksetzen);
		}

		[Fact]
		public void ReactivationAfterPauseTriggers() {
			var e = FastFarmReset.Entscheide(true, false, Jetzt.AddMinutes(-30), Jetzt.AddHours(-8), Jetzt, 6);
			Assert.Equal("reactivation", e.Ausloeser);
			Assert.True(e.Zuruecksetzen);
		}

		[Fact]
		public void StoppedWorkerWithoutNextRunCountsAsReactivation() {
			var e = FastFarmReset.Entscheide(true, false, default(DateTime), null, Jetzt, 6);
			Assert.Equal("reactivation", e.Ausloeser);
			Assert.True(e.Zuruecksetzen);
		}

		[Fact]
		public void FrequentSwitchingIsBlockedByLock() {
			var e = FastFarmReset.Entscheide(true, false, Jetzt.AddMinutes(-20), Jetzt.AddMinutes(-40), Jetzt, 6);
			Assert.Equal("reactivation", e.Ausloeser);
			Assert.False(e.Zuruecksetzen);
		}

		[Fact]
		public void DisabledSettingNeverResets() {
			var e = FastFarmReset.Entscheide(false, true, null, null, Jetzt, 6);
			Assert.False(e.Zuruecksetzen);
		}

		[Fact]
		public void ZeroHoursMeansNoLock() {
			var e = FastFarmReset.Entscheide(true, true, Jetzt, Jetzt.AddMinutes(-1), Jetzt, 0);
			Assert.True(e.Zuruecksetzen);
		}
	}
}
