using System;
using System.Collections.Generic;
using Tbot.Workers;
using Xunit;

namespace TBot.Tests {
	public class ShadowFleetLogicTests {
		[Fact]
		public void Ohne_Fenster_ist_immer_Zeit() => Assert.True(ShadowFleetLogic.InWindow(TimeSpan.FromHours(3), "", ""));

		[Theory]
		[InlineData(23, true)]
		[InlineData(3, true)]
		[InlineData(7, false)]
		[InlineData(12, false)]
		public void Fenster_ueber_Mitternacht(int stunde, bool erwartet) =>
			Assert.Equal(erwartet, ShadowFleetLogic.InWindow(TimeSpan.FromHours(stunde), "22:00", "07:00"));

		[Fact]
		public void Sekunden_bis_Fensterende_und_Start() {
			Assert.Equal(2 * 3600, ShadowFleetLogic.SecondsToWindowEnd(TimeSpan.FromHours(5), "22:00", "07:00"));
			Assert.Equal(0, ShadowFleetLogic.SecondsToWindowEnd(TimeSpan.FromHours(12), "22:00", "07:00"));
			Assert.Equal(10 * 3600, ShadowFleetLogic.SecondsToWindowStart(TimeSpan.FromHours(12), "22:00", "07:00"));
			Assert.Equal(0, ShadowFleetLogic.SecondsToWindowStart(TimeSpan.FromHours(23), "22:00", "07:00"));
		}

		[Fact]
		public void Flugdauer_bleibt_im_Rahmen() {
			var rnd = new Random(1);
			for (int i = 0; i < 50; i++) {
				long s = ShadowFleetLogic.FlightSeconds(20, 60, long.MaxValue, rnd);
				Assert.InRange(s, 20 * 60, 60 * 60);
			}
		}

		[Fact]
		public void Flugdauer_endet_vor_dem_Fensterende() {
			long s = ShadowFleetLogic.FlightSeconds(20, 60, 30 * 60, new Random(2));
			Assert.InRange(s, 20 * 60, 30 * 60 - 30);
			Assert.Equal(0, ShadowFleetLogic.FlightSeconds(20, 60, 60, new Random(2)));
		}

		[Fact]
		public void Deuterium_fuer_drei_Fluege() {
			var p = ShadowFleetLogic.PlanDeuterium(100000, 1000, 3, 1_000_000);
			Assert.Equal(3000, p.Payload);
			Assert.Equal(DeutLevel.Ok, p.Level);
		}

		[Fact]
		public void Fracht_ist_durch_Laderaum_und_Bestand_begrenzt() {
			Assert.Equal(500, ShadowFleetLogic.PlanDeuterium(100000, 1000, 3, 500).Payload);
			Assert.Equal(1500, ShadowFleetLogic.PlanDeuterium(2500, 1000, 3, 1_000_000).Payload);
		}

		[Fact]
		public void Warnstufen() {
			Assert.Equal(DeutLevel.None, ShadowFleetLogic.PlanDeuterium(900, 1000, 3, 10000).Level);
			Assert.Equal(DeutLevel.Last, ShadowFleetLogic.PlanDeuterium(1500, 1000, 3, 10000).Level);
			Assert.Equal(DeutLevel.Low, ShadowFleetLogic.PlanDeuterium(3500, 1000, 3, 10000).Level);
			Assert.Equal(DeutLevel.Ok, ShadowFleetLogic.PlanDeuterium(9000, 1000, 3, 10000).Level);
		}

		[Fact]
		public void Angriff_blockiert_nur_wenn_er_vor_der_Rueckkehr_trifft() {
			Assert.True(ShadowFleetLogic.AttackBlocks(new long[] { 1200 }, 1800, 120));
			Assert.False(ShadowFleetLogic.AttackBlocks(new long[] { 4000 }, 1800, 120));
			Assert.False(ShadowFleetLogic.AttackBlocks(new long[0], 1800, 120));
		}

		[Fact]
		public void Schiffsauswahl_null_heisst_alle() {
			var have = new Dictionary<string, long> { ["Recycler"] = 40, ["LargeCargo"] = 10 };
			var r = ShadowFleetLogic.PickShips(k => have.TryGetValue(k, out var v) ? v : 0,
				new Dictionary<string, long> { ["Recycler"] = 0, ["LargeCargo"] = 50, ["Cruiser"] = 5 });
			Assert.Equal(40, r["Recycler"]);
			Assert.Equal(10, r["LargeCargo"]);
			Assert.False(r.ContainsKey("Cruiser"));
			Assert.True(ShadowFleetLogic.CanHarvest(r));
			Assert.False(ShadowFleetLogic.CanHarvest(new Dictionary<string, long> { ["LargeCargo"] = 5 }));
		}
	}
}
