using Tbot.Workers.Brain;
using Xunit;

namespace TBot.Tests {
	public class MinePlanResearchCapTests {
		private static MinePlanSnapshot.ResearchPlanCfg Cfg(int maxPlasma = 20, int maxAstro = 23, bool prioPlasma = false, bool prioAstro = true)
			=> new MinePlanSnapshot.ResearchPlanCfg { MaxPlasma = maxPlasma, MaxAstro = maxAstro, PrioPlasma = prioPlasma, PrioAstro = prioAstro };

		[Fact]
		public void PlasmaIsPlannedUpToTheConfiguredMaxOnly() {
			var rc = Cfg(maxPlasma: 20);
			Assert.True(MinePlanSnapshot.PlasmaAllowed(rc, 19));
			Assert.False(MinePlanSnapshot.PlasmaAllowed(rc, 20));
		}

		[Fact]
		public void PlasmaWithMax65IsPlannedBeyond20() {
			var rc = Cfg(maxPlasma: 65);
			Assert.True(MinePlanSnapshot.PlasmaAllowed(rc, 20));
			Assert.False(MinePlanSnapshot.PlasmaAllowed(rc, 65));
		}

		[Fact]
		public void WithoutConfigurationThereIsNoCap() {
			Assert.True(MinePlanSnapshot.PlasmaAllowed(null, 500));
			Assert.Equal(0, MinePlanSnapshot.ForcedResearch(null, 1, 1));
		}

		[Fact]
		public void PrioritizedAstrophysicsComesFirstUntilItsMax() {
			var rc = Cfg(prioAstro: true, maxAstro: 23);
			Assert.Equal(2, MinePlanSnapshot.ForcedResearch(rc, 19, 20));
			Assert.Equal(2, MinePlanSnapshot.ForcedResearch(rc, 19, 22));
			Assert.Equal(0, MinePlanSnapshot.ForcedResearch(rc, 19, 23));
		}

		[Fact]
		public void PrioritizedPlasmaGoesBeforePrioritizedAstrophysicsAndStopsAtMax() {
			var rc = Cfg(prioPlasma: true, prioAstro: true, maxPlasma: 21, maxAstro: 23);
			Assert.Equal(1, MinePlanSnapshot.ForcedResearch(rc, 20, 20));
			Assert.Equal(2, MinePlanSnapshot.ForcedResearch(rc, 21, 20));
		}

		[Fact]
		public void NoPriorityFlagsMeansNothingForced() {
			var rc = Cfg(prioPlasma: false, prioAstro: false);
			Assert.Equal(0, MinePlanSnapshot.ForcedResearch(rc, 10, 10));
		}
	}
}
