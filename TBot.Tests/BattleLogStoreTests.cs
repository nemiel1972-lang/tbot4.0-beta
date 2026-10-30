using System;
using System.IO;
using Tbot.Common.Combat;
using Xunit;

namespace TBot.Tests {
    public class BattleLogStoreTests {
        private static void Fresh() => BattleLogStore.RootPath = Path.Combine(Path.GetTempPath(), "bl" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Links_actual_and_picks_the_closer_calculator() {
            Fresh();
            BattleLogStore.Append(new BattleLogRecord { BattleId = "b1", Target = "t", Go = new BattleLogSide { AttackerWin = 90, AttackerLosses = 1000 }, Rust = new BattleLogSide { AttackerWin = 88, AttackerLosses = 1500 } });
            BattleLogStore.AppendLink("b1", 42);
            Assert.True(BattleLogStore.TryAppendActualForFleet(42, new BattleLogActual { Won = true, AttackerLosses = new long[] { 1400, 0, 0 } }));
            var r = BattleLogStore.Read(null, null, null, false, 100, false, false);
            Assert.Single(r);
            Assert.Equal("rust", r[0].BestCalc);
            Assert.True(r[0].Actual!.Won);
        }

        [Fact]
        public void Unlinked_fleet_gets_no_actual() {
            Fresh();
            Assert.False(BattleLogStore.TryAppendActualForFleet(7, new BattleLogActual { Won = false }));
        }
    }
}
