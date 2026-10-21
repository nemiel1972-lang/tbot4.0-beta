using System;
using System.Linq;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class GalaxyCacheTests {
		private static readonly DateTime T0 = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

		private static Planet P(int system, int pos, int playerId, string name, bool inactive = false, bool moon = false) => new Planet {
			Name = "p" + pos,
			Coordinate = new Coordinate(1, system, pos, Celestials.Planet),
			Player = new Player { ID = playerId, Name = name },
			Inactive = inactive,
			Moon = moon ? new Moon() : null
		};

		private static GalaxyInfo Sys(int system, params Planet[] planets) => new GalaxyInfo { Galaxy = 1, System = system, Planets = planets.ToList() };

		[Fact]
		public void Erster_Scan_ist_nur_die_Grundlage() {
			var c = new GalaxyCache(null);
			Assert.Empty(c.Record(Sys(5, P(5, 3, 10, "A")), T0));
		}

		[Fact]
		public void Neu_Weg_Mond_und_Status_werden_erkannt_eigene_Kolonien_nicht() {
			var c = new GalaxyCache(null) { OwnPlayerId = 99 };
			c.Record(Sys(5, P(5, 3, 10, "A"), P(5, 8, 11, "B")), T0);
			var ev = c.Record(Sys(5, P(5, 3, 10, "A", true, true), P(5, 9, 12, "C"), P(5, 10, 99, "ich")), T0.AddHours(1));
			Assert.Contains(ev, e => e.Type == GalaxyEventType.Status && e.Position == 3);
			Assert.Contains(ev, e => e.Type == GalaxyEventType.Moon && e.Position == 3);
			Assert.Contains(ev, e => e.Type == GalaxyEventType.Gone && e.Position == 8);
			Assert.Contains(ev, e => e.Type == GalaxyEventType.New && e.Position == 9);
			Assert.DoesNotContain(ev, e => e.Position == 10);
		}

		[Fact]
		public void Umzug_wird_ueber_zwei_Scans_verknuepft() {
			var c = new GalaxyCache(null);
			c.Record(Sys(5, P(5, 8, 11, "B")), T0);
			c.Record(Sys(6), T0);
			c.Record(Sys(5), T0.AddHours(1));
			var ev = c.Record(Sys(6, P(6, 2, 11, "B")), T0.AddHours(2));
			Assert.Contains(ev, e => e.Type == GalaxyEventType.Move && e.Detail == "from 1:5:8");
		}

		[Fact]
		public void Kein_Umzug_nach_mehr_als_72_Stunden() {
			var c = new GalaxyCache(null);
			c.Record(Sys(5, P(5, 8, 11, "B")), T0);
			c.Record(Sys(6), T0);
			c.Record(Sys(5), T0.AddHours(1));
			var ev = c.Record(Sys(6, P(6, 2, 11, "B")), T0.AddHours(80));
			Assert.DoesNotContain(ev, e => e.Type == GalaxyEventType.Move);
		}

		[Fact]
		public void Besetzt_nur_bei_frischem_Eintrag_frei_oder_unbekannt_heisst_live_pruefen() {
			var c = new GalaxyCache(null);
			c.Record(Sys(5, P(5, 9, 12, "C")), T0);
			var frisch = TimeSpan.FromHours(12);
			Assert.True(c.IsOccupiedFresh(new Coordinate(1, 5, 9), frisch, T0.AddHours(2)));
			Assert.False(c.IsOccupiedFresh(new Coordinate(1, 5, 4), frisch, T0.AddHours(2)));
			Assert.False(c.IsOccupiedFresh(new Coordinate(1, 7, 9), frisch, T0.AddHours(2)));
			Assert.False(c.IsOccupiedFresh(new Coordinate(1, 5, 9), frisch, T0.AddHours(20)));
		}

		[Fact]
		public void TryGet_liefert_nur_frische_Systeme() {
			var c = new GalaxyCache(null);
			c.Record(Sys(5, P(5, 3, 10, "A", true)), T0);
			Assert.Null(c.TryGet(1, 5, TimeSpan.FromHours(1), T0.AddHours(5)));
			var info = c.TryGet(1, 5, TimeSpan.FromHours(12), T0.AddHours(5));
			Assert.Single(info.Planets);
			Assert.True(info.Planets[0].Inactive);
			Assert.Equal("A", info.Planets[0].Player.Name);
		}

		[Fact]
		public void NextToScan_nimmt_zuerst_nie_gesehene_dann_die_aeltesten() {
			var c = new GalaxyCache(null);
			c.Record(Sys(5), T0);
			c.Record(Sys(6), T0.AddHours(-1));
			var kandidaten = new[] { (1, 5), (1, 6), (1, 7) };
			Assert.Equal((1, 7), c.NextToScan(kandidaten, TimeSpan.FromHours(12), T0.AddHours(13)));
			c.Record(Sys(7), T0.AddHours(13));
			Assert.Equal((1, 6), c.NextToScan(kandidaten, TimeSpan.FromHours(12), T0.AddHours(13)));
			Assert.Null(c.NextToScan(kandidaten, TimeSpan.FromHours(12), T0.AddHours(1)));
		}
	}
}
