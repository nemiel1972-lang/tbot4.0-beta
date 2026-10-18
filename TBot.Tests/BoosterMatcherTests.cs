using System.Collections.Generic;
using Tbot.Workers.Brain;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class BoosterMatcherTests {
		[Theory]
		[InlineData("Metal Booster Bronze", BoosterType.Metal, true)]
		[InlineData("Metallbooster Gold", BoosterType.Metal, true)]
		[InlineData("Kristall-Booster Silber", BoosterType.Crystal, true)]
		[InlineData("Deuterium Booster Platinum", BoosterType.Deuterium, true)]
		[InlineData("Energie-Booster Bronze", BoosterType.Energy, true)]
		[InlineData("Metal Booster Bronze", BoosterType.Crystal, false)]
		[InlineData("KRAKEN Metal Bronze", BoosterType.Metal, false)]
		[InlineData("Detroid Deuterium Gold", BoosterType.Deuterium, false)]
		[InlineData("", BoosterType.Metal, false)]
		public void TypeMatching(string name, BoosterType type, bool expected) {
			Assert.Equal(expected, BoosterMatcher.IsBoosterOfType(name, type));
		}

		[Theory]
		[InlineData("Metal Booster Bronze", "bronze")]
		[InlineData("Kristall-Booster Silber", "silver")]
		[InlineData("Booster Gold", "gold")]
		[InlineData("Booster Platin", "platinum")]
		[InlineData("Booster", null)]
		public void Tiers(string name, string expected) {
			Assert.Equal(expected, BoosterMatcher.TierOf(name));
		}

		[Fact]
		public void ActiveIgnoresExpiredAndOtherTypes() {
			var active = new List<ActiveItem> {
				new ActiveItem { Name = "Metal Booster Gold", TimeRemaining = 0 },
				new ActiveItem { Name = "Kristall-Booster Gold", TimeRemaining = 3600 },
			};
			Assert.False(BoosterMatcher.IsActive(active, BoosterType.Metal));
			Assert.True(BoosterMatcher.IsActive(active, BoosterType.Crystal));
			Assert.False(BoosterMatcher.IsActive(null, BoosterType.Metal));
		}

		[Fact]
		public void EventResourceBoostersAreIgnored() {
			var active = new List<ActiveItem> {
				new ActiveItem { Name = "Ressourcen-Booster (40 %) Bronze", TimeRemaining = 3600 },
				new ActiveItem { Name = "Expeditions-Ressourcenbooster (40 %) Bronze", TimeRemaining = 3600 },
			};
			Assert.False(BoosterMatcher.IsActive(active, BoosterType.Metal));
			Assert.False(BoosterMatcher.IsActive(active, BoosterType.Deuterium));
			var inv = new List<InventoryItem> { new InventoryItem { Ref = "r", Name = "Ressourcen-Booster (40 %) Bronze", Amount = 5 } };
			Assert.Null(BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "bronze" }));
			Assert.Equal(0L, BoosterMatcher.StockByTier(inv)["bronze"]["Metal"]);
		}

		[Fact]
		public void ReservePerTierAndStock() {
			var inv = new List<InventoryItem> {
				new InventoryItem { Ref = "b", Name = "Metall-Booster Bronze", Amount = 12 },
				new InventoryItem { Ref = "s", Name = "Metall-Booster Silber", Amount = 3 },
				new InventoryItem { Ref = "k", Name = "Kristall-Booster Gold", Amount = 4 },
				new InventoryItem { Ref = "x", Name = "KRAKEN Bronze", Amount = 9 },
			};
			var tiers = new[] { "bronze", "silver" };
			Assert.Equal("b", BoosterMatcher.Pick(inv, BoosterType.Metal, tiers, t => t == "bronze" ? 10 : 0).Ref);
			Assert.Equal("s", BoosterMatcher.Pick(inv, BoosterType.Metal, tiers, t => t == "bronze" ? 12 : 0).Ref);
			Assert.Null(BoosterMatcher.Pick(inv, BoosterType.Metal, tiers, t => 12));
			var stock = BoosterMatcher.StockByTier(inv);
			Assert.Equal(12L, stock["bronze"]["Metal"]);
			Assert.Equal(3L, stock["silver"]["Metal"]);
			Assert.Equal(4L, stock["gold"]["Crystal"]);
			Assert.Equal(0L, stock["bronze"]["Crystal"]);
		}

		[Fact]
		public void PickFollowsTierOrderAndStock() {
			var inv = new List<InventoryItem> {
				new InventoryItem { Ref = "g", Name = "Metal Booster Gold", Amount = 2 },
				new InventoryItem { Ref = "b", Name = "Metal Booster Bronze", Amount = 0 },
				new InventoryItem { Ref = "s", Name = "Metal Booster Silver", Amount = 1 },
				new InventoryItem { Ref = "c", Name = "Crystal Booster Bronze", Amount = 5 },
			};
			Assert.Equal("s", BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "bronze", "silver", "gold" }).Ref);
			Assert.Equal("g", BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "gold", "silver" }).Ref);
			Assert.Null(BoosterMatcher.Pick(inv, BoosterType.Deuterium, new[] { "bronze" }));
			Assert.Null(BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "platinum" }));
			Assert.Null(BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "silver", "gold" }, 2));
			Assert.Equal("g", BoosterMatcher.Pick(inv, BoosterType.Metal, new[] { "silver", "gold" }, 1).Ref);
		}
	}
}
