using System.Collections.Generic;
using System.Linq;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using Xunit;
using static Tbot.Workers.RewardingLogic;

namespace TBot.Tests {
	public class RewardingLogicTests {
		private static RewardTask T(string text) {
			var t = new RewardTask { Text = text };
			Classify(t);
			return t;
		}

		[Theory]
		[InlineData("Versende eine Flotte", TaskKind.SendFleet)]
		[InlineData("Starte den Bau eines Gebäudes", TaskKind.StartBuilding)]
		[InlineData("Besuche den Rohstoffmarkt", TaskKind.VisitMarket)]
		[InlineData("Kaufe etwas im Offizierskasino", TaskKind.Manual)]
		[InlineData("Stelle ein Bauprojekt mit Dunkler Materie oder einem passenden Gegenstand sofort fertig.", TaskKind.Manual)]
		[InlineData("Versende eine Nachricht im Spiel", TaskKind.SendMessage)]
		[InlineData("Besuche die Highscore-Liste", TaskKind.VisitHighscore)]
		[InlineData("Transportiere 10000 Einheiten Metall zu einem deiner Planeten", TaskKind.Transport)]
		[InlineData("Baue ein Trümmerfeld ab. (Außer Event-Trümmerfelder)", TaskKind.CollectDebris)]
		[InlineData("Spioniere einen Planeten aus", TaskKind.Spy)]
		[InlineData("Starte ein Forschungsprojekt", TaskKind.StartResearch)]
		[InlineData("Start a research project", TaskKind.StartResearch)]
		[InlineData("Irgendeine neue Aufgabe", TaskKind.Unknown)]
		public void Classifies(string text, TaskKind kind) => Assert.Equal(kind, T(text).Kind);

		[Fact]
		public void ShipBuildOnHomeWorld() {
			var t = T("Baue 10x Recycler in der Schiffswerft");
			Assert.Equal(TaskKind.ShipBuild, t.Kind);
			Assert.Equal(Buildables.Recycler, t.Ship);
			Assert.Equal(10, t.Amount);
		}

		[Fact]
		public void ScrapMerchantVisit() {
			Assert.Equal(TaskKind.VisitScrap, T("Besuche den Schrotthändler").Kind);
		}

		[Fact]
		public void OfficersMessVisit() {
			Assert.Equal(TaskKind.VisitOfficers, T("Besuche die Offiziersmesse").Kind);
			Assert.Equal(TaskKind.VisitOfficers, T("Visit the Officers' Mess").Kind);
			Assert.Equal(TaskKind.VisitOfficers, T("Besuche das Offizierskasino").Kind);
			Assert.Equal(TaskKind.Manual, T("Kaufe einen Offizier in der Offiziersmesse").Kind);
		}

		[Fact]
		public void ShopVisitButNotPurchase() {
			Assert.Equal(TaskKind.VisitShop, T("Besuche den Shop").Kind);
			Assert.NotEqual(TaskKind.VisitShop, T("Kaufe etwas im Shop").Kind);
		}

		[Fact]
		public void TechtreeVisit() {
			Assert.Equal(TaskKind.VisitTechtree, T("Rufe einen beliebigen Techtree auf").Kind);
		}

		[Fact]
		public void ImportExportAndAuctionAreOtherWorker() {
			Assert.Equal(TaskKind.OtherWorker, T("Hole ein Item bei Import/Export ab").Kind);
			Assert.Equal(TaskKind.OtherWorker, T("Biete auf ein Item beim Auktionator").Kind);
		}

		[Fact]
		public void DefenceNameAndAmount() {
			var t = T("Baue 2x Plasmawerfer in der Verteidigung");
			Assert.Equal(TaskKind.Defence, t.Kind);
			Assert.Equal(Buildables.PlasmaTurret, t.Defence);
			Assert.Equal(2, t.Amount);
		}

		[Fact]
		public void ParsesStagesProgressAndDone() {
			string html = "<div class=\"tritiumstage\"><div class=\"tritiumvalue\">Tritium-Wert: 100</div></div>" +
				"<div class=\"rewardlist-item\"><h3>Verteidigung</h3><p>Baue&nbsp;100x&nbsp;Raketenwerfer in der Verteidigung</p><p>Fortschritt:&nbsp;40/100</p></div>" +
				"<div class=\"tritiumstage\"><div class=\"tritiumvalue\">Tritium-Wert: 200</div></div>" +
				"<div class=\"rewardlist-item\"><h3>Auf und davon!</h3><p>Versende eine Flotte</p><div class=\"reward-claimed-text\"></div></div>";
			var l = ParseTasks(html);
			Assert.Equal(2, l.Count);
			Assert.Equal(100, l[0].Tritium);
			Assert.False(l[0].Done);
			Assert.Equal(RewardingLogic.TaskKind.Defence, l[0].Kind);
			Assert.Equal(60, l[0].Amount);
			Assert.Equal(200, l[1].Tritium);
			Assert.True(l[1].Done);
		}

		[Fact]
		public void ParsesRankRewards() {
			string html = "<div class=\"currentTritium\">Du hast <span class='overmark'>1080</span> von 4000 Tritium für diesen Rang erreicht.</div>" +
				"<div class=\"normalRewards\"><div class=\"col w-150 singleReward\" data-id=\"25\" disabled=disabled><div class=\"rewardName\">Artefakt-Paket (3.000)</div><div class=\"quantity\"><div class=\"timeIndicator\">&#x231b;</div>1</div></div></div>" +
				"<div id=\"header\">Erhalte zusätzlich:</div><div class=\"additionalRewards\">" +
				"<div class=\"col w-150 singleReward\" data-id=\"29\"><div class=\"rewardName\">Ingenieur</div><div class=\"quantity\">3 Tage</div></div></div><script>var x = 1;</script>";
			var t = RewardingLogic.ParseTier(4, html);
			Assert.Equal(1080, t.Have);
			Assert.Equal(4000, t.Need);
			Assert.False(t.Claimed);
			Assert.Equal(2, t.Rewards.Count);
			Assert.Equal("Artefakt-Paket (3.000)", t.Rewards[0].Name);
			Assert.Equal("1", t.Rewards[0].Quantity);
			Assert.False(t.Rewards[0].Additional);
			Assert.True(t.Rewards[1].Additional);
			Assert.Equal("3 Tage", t.Rewards[1].Quantity);
		}

		private static RewardingLogic.Tier TierOf(int n, int have, int need, bool claimed, params (int id, string name, bool add)[] rewards) {
			var t = new RewardingLogic.Tier { Number = n, Have = have, Need = need, Claimed = claimed };
			foreach (var r in rewards)
				t.Rewards.Add(new RewardingLogic.RankReward { Id = r.id, Name = r.name, Additional = r.add });
			return t;
		}

		[Fact]
		public void PlansOnlyReachedUnclaimedTiersWithValidChoice() {
			var tiers = new[] {
				TierOf(1, 1000, 1000, true, (1, "A", false)),
				TierOf(2, 2000, 2000, false, (9, "KRAKEN", false), (10, "NEWTRON", false), (13, "Admiral", true)),
				TierOf(3, 3000, 3000, false, (17, "Metall", false)),
				TierOf(4, 3000, 4000, false, (25, "Artefakt", false)),
			};
			var choices = new Dictionary<int, (int id, string name)> { [1] = (1, "A"), [2] = (10, "NEWTRON"), [4] = (25, "Artefakt") };
			var ohne = new List<int>();
			var plan = RewardingLogic.PlanClaims(tiers, choices, ohne);
			Assert.Single(plan);
			Assert.Equal(2, plan[0].Tier);
			Assert.Equal(10, plan[0].RewardId);
			Assert.Equal(new List<int> { 3 }, ohne);
		}

		[Fact]
		public void ChoiceExpiresWhenNameChanged() {
			var tiers = new[] { TierOf(2, 2000, 2000, false, (10, "Anderes Item", false)) };
			var plan = RewardingLogic.PlanClaims(tiers, new Dictionary<int, (int id, string name)> { [2] = (10, "NEWTRON") });
			Assert.Empty(plan);
		}

		[Fact]
		public void ClaimAnswerIsRecognised() {
			Assert.True(RewardingLogic.ClaimSucceeded("{\"status\":\"success\",\"success\":true,\"message\":\"ok\"}", out var m));
			Assert.Equal("ok", m);
			Assert.False(RewardingLogic.ClaimSucceeded("{\"status\":\"error\",\"success\":false,\"message\":\"nope\"}", out _));
			Assert.False(RewardingLogic.ClaimSucceeded("<html>", out _));
		}

		[Fact]
		public void AllClaimed_prueft_nur_den_hoechsten_Rang() {
			var t = new System.Collections.Generic.List<RewardingLogic.Tier> {
				new RewardingLogic.Tier { Number = 1, Claimed = true },
				new RewardingLogic.Tier { Number = 7, Claimed = false },
				new RewardingLogic.Tier { Number = 2, Claimed = true }
			};
			Assert.False(RewardingLogic.AllClaimed(t));
			t.First(x => x.Number == 7).Claimed = true;
			Assert.True(RewardingLogic.AllClaimed(t));
		}

		[Fact]
		public void AllClaimed_ohne_gelesene_Raenge_ist_false() {
			Assert.False(RewardingLogic.AllClaimed(new System.Collections.Generic.List<RewardingLogic.Tier>()));
			Assert.False(RewardingLogic.AllClaimed(null));
		}
	}
}
