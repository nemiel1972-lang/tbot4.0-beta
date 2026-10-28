using Tbot.Helpers;
using Xunit;

namespace TBot.Tests {
	public class LifeformBoostStagesTests {
		private static int[] Arr(params int[] v) {
			var a = new int[13];
			for (int i = 0; i < v.Length; i++) a[i + 1] = v[i];
			return a;
		}

		[Fact]
		public void UnlistedPositionDoesNotKeepT2PhaseOpen() {
			int[] levels = Arr(72, 83, 20, 9, 9, 15, 10, 30, 22, 10, 30, 10);
			int[] stage1 = Arr(47, 55, 7, 0, 9, 15, 10, 4, 5, 10, 30, 10);
			int[] current = Arr(71, 82, 20, 9, 9, 22, 10, 30, 23, 10, 13, 10);
			bool[] gelistet = new bool[13];
			foreach (int p in new[] { 1, 2, 3, 4, 8, 9 }) gelistet[p] = true;

			string note = LifeformTargets.ApplyBoostStages(levels, stage1, current, 15, 20, 12, gelistet);

			Assert.Equal("", note);
			Assert.Equal(72, levels[1]);
			Assert.Equal(83, levels[2]);
		}

		[Fact]
		public void WithoutListInfoOldBehaviourRemains() {
			int[] levels = Arr(72, 83, 20, 9, 9, 15, 10, 30, 22, 10, 30, 10);
			int[] stage1 = Arr(47, 55, 7, 0, 9, 15, 10, 4, 5, 10, 30, 10);
			int[] current = Arr(71, 82, 20, 9, 9, 22, 10, 30, 23, 10, 13, 10);

			string note = LifeformTargets.ApplyBoostStages(levels, stage1, current, 15, 20, 12);

			Assert.Contains("T2 phase", note);
			Assert.Equal(71, levels[1]);
		}

		[Fact]
		public void ListedPositionBelowT2TargetStillHoldsPhase() {
			int[] levels = Arr(72, 83, 20, 9, 9, 15, 10, 30, 22, 10, 30, 10);
			int[] stage1 = Arr(47, 55, 7, 0, 9, 15, 10, 4, 5, 10, 30, 10);
			int[] current = Arr(40, 50, 20, 9, 9, 22, 10, 30, 23, 10, 13, 10);
			bool[] gelistet = new bool[13];
			foreach (int p in new[] { 1, 2, 3, 4, 8, 9 }) gelistet[p] = true;

			string note = LifeformTargets.ApplyBoostStages(levels, stage1, current, 15, 20, 12, gelistet);

			Assert.Contains("T2 phase", note);
			Assert.Equal(47, levels[1]);
		}
	}
}
