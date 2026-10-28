using System.Linq;
using Tbot.Workers;
using TBot.Ogame.Infrastructure.Enums;
using Xunit;
using static Tbot.Workers.DirectiveLogic;

namespace TBot.Tests {
	public class DirectiveLogicTests {
		[Theory]
		[InlineData("Baue Fusionskraftwerk auf Stufe 1 aus", GoalKind.BuildingLevel, Buildables.FusionReactor, 1)]
		[InlineData("Baue Raumschiffswerft auf Stufe 3 aus", GoalKind.BuildingLevel, Buildables.Shipyard, 3)]
		[InlineData("Erforsche Energietechnik auf Stufe 3", GoalKind.ResearchLevel, Buildables.EnergyTechnology, 3)]
		[InlineData("Erforsche Lasertechnik auf Stufe 6", GoalKind.ResearchLevel, Buildables.LaserTechnology, 6)]
		[InlineData("Baue 2 mal Solarsatellit", GoalKind.ShipCount, Buildables.SolarSatellite, 2)]
		[InlineData("Baue 1 mal Spionagesonde", GoalKind.ShipCount, Buildables.EspionageProbe, 1)]
		[InlineData("Baue 1 mal Leichtes Lasergeschütz", GoalKind.DefenceCount, Buildables.LightLaser, 1)]
		[InlineData("Baue Metallmine auf Stufe 14 aus", GoalKind.BuildingLevel, Buildables.MetalMine, 14)]
		[InlineData("Research Astrophysics to level 1", GoalKind.ResearchLevel, Buildables.Astrophysics, 1)]
		[InlineData("Upgrade Solar Plant on a colonised planet to level 5", GoalKind.BuildingLevel, Buildables.SolarPlant, 5)]
		[InlineData("Upgrade the Interstellar Anomaly Scanner (IAS) to level 10 (2nd planet)", GoalKind.BuildingLevel, Buildables.InterstellarAnomalyScanner, 10)]
		[InlineData("Produce 1x Espionage Probe", GoalKind.ShipCount, Buildables.EspionageProbe, 1)]
		public void Klassifiziert_Ziele(string text, GoalKind kind, Buildables item, int value) {
			var a = Classify(text);
			Assert.Equal(kind, a.Kind);
			Assert.Equal(item, a.Item);
			Assert.Equal(value, a.Value);
		}

		[Theory]
		[InlineData("Reiße Fusionskraftwerk ab")]
		[InlineData("Einen anderen Spieler ausspionieren")]
		[InlineData("Tritt einer Allianz bei")]
		[InlineData("Benenne deinen Heimatplaneten um")]
		public void Manuelle_Aufgaben(string text) => Assert.Equal(GoalKind.Manual, Classify(text).Kind);

		[Theory]
		[InlineData("Entsende eine Spionagemission mit Ressourcen auf Position 16", "spy16")]
		[InlineData("Rufe deine Flotte wieder zurück", "recall")]
		[InlineData("Entsende eine Expedition mit Kleinen Transportern", "expedition")]
		public void Flottenaufgaben(string text, string tag) {
			var a = Classify(text);
			Assert.Equal(GoalKind.Fleet, a.Kind);
			Assert.Equal(tag, a.FleetTag);
		}

		[Fact]
		public void Kolonie_Hinweis_und_Lebensform() {
			Assert.True(Classify("Baue auf einem kolonialisierten Planeten Metallmine auf Stufe 5 aus").OnColony);
			Assert.Equal("lifeform", Classify("Baue das Lebensformgebäude Biosphären-Farm auf Stufe 22 aus").FleetTag);
		}

		[Fact]
		public void Parst_Kapitel() {
			string html = "<div class=\"ipiTaskItem\" data-taskid=\"5001\" data-state=\"tracked\"><div class=\"ipiTaskItemProgress\" data-progress=\"1\" data-total=\"2\"></div>" +
				"<ul><li class=\"ipiActionItem\"><span class=\"ipiActionCompletedMark\"></span>Baue Metallmine auf Stufe 2 aus</li><li class=\"ipiActionItem\">Baue 2 mal Solarsatellit</li></ul>" +
				"<a class=\"claimTaskRewards disabled\">x</a></div>" +
				"<div class=\"ipiTaskItem\" data-taskid=\"5002\" data-state=\"completed\"><a class=\"claimTaskRewards\">x</a></div>";
			var ch = ParseChapter(4001, html);
			Assert.Equal(2, ch.Tasks.Count);
			Assert.False(ch.Tasks[0].CanCollect);
			Assert.True(ch.Tasks[1].CanCollect);
			Assert.True(ch.Tasks[0].Actions[0].Done);
			Assert.False(ch.Tasks[0].Actions[1].Done);
			Assert.Equal(Buildables.SolarSatellite, ch.Tasks[0].Actions[1].Item);
		}
	
		[Fact]
		public void Lithium_Umwandlung_ist_Sonderaufgabe() {
			var a = Classify("Start a Lithium conversion");
			Assert.Equal(GoalKind.Fleet, a.Kind);
			Assert.Equal("lithium", a.FleetTag);
		}
	
		[Fact]
		public void Voraussetzungen_werden_aufgeloest() {
			var lv = new System.Collections.Generic.Dictionary<Buildables, int> { { Buildables.ResearchLab, 4 }, { Buildables.Shipyard, 4 }, { Buildables.CombustionDrive, 6 } };
			System.Func<Buildables, int> f = b => lv.TryGetValue(b, out var v) ? v : 0;
			Assert.Null(DirectivePrereq.FirstMissing(Buildables.WeaponsTechnology, f));
			var m = DirectivePrereq.FirstMissing(Buildables.Recycler, f);
			Assert.Equal(Buildables.ResearchLab, m.Value.Item);
			Assert.Equal(6, m.Value.Level);
			Assert.True(DirectivePrereq.IsResearch(Buildables.Astrophysics));
			Assert.False(DirectivePrereq.IsResearch(Buildables.Shipyard));
		}
	
		[Theory]
		[InlineData("Upgrade the Residential Sector lifeform building to level 23", LFBuildables.ResidentialSector, 23)]
		[InlineData("Upgrade the Biosphere Farm lifeform building to level 24", LFBuildables.BiosphereFarm, 24)]
		[InlineData("Upgrade the Research Centre lifeform building to level 1", LFBuildables.ResearchCentre, 1)]
		public void Lebensform_Gebaeude_werden_erkannt(string text, LFBuildables item, int value) {
			var a = Classify(text);
			Assert.Equal(GoalKind.LifeformBuilding, a.Kind);
			Assert.Equal(item, a.LFItem);
			Assert.Equal(value, a.Value);
		}

		[Fact]
		public void Lebensform_Forschung_wird_erkannt() {
			var a = Classify("Advance the Intergalactic Envoys lifeform research to level 1");
			Assert.Equal(GoalKind.LifeformResearch, a.Kind);
			Assert.Equal(LFTechno.IntergalacticEnvoys, a.LFTech);
			Assert.Equal(1, a.Value);
			Assert.NotEqual(GoalKind.LifeformResearch, Classify("Unlock lifeform research slot 3").Kind);
		}
	}
}
