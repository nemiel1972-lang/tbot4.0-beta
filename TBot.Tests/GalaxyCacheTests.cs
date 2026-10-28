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

		[Fact]
		public void NextToScan_laesst_leere_Systeme_laenger_liegen() {
			var c = new GalaxyCache(null);
			c.Record(Sys(9), T0);
			var kandidaten = new[] { (1, 9) };
			Assert.Null(c.NextToScan(kandidaten, TimeSpan.FromHours(12), T0.AddHours(13), TimeSpan.FromHours(72)));
			Assert.Equal((1, 9), c.NextToScan(kandidaten, TimeSpan.FromHours(12), T0.AddHours(73), TimeSpan.FromHours(72)));
		}

		[Fact]
		public void Highscore_Datenbank_liefert_Systeme_inaktiver_Spieler_und_ueberschreibt_den_Status() {
			var c = new GalaxyCache(null);
			var hp = new System.Collections.Generic.List<HighscorePlayer> {
				new HighscorePlayer { ID = 10, Name = "A", Position = 5, Homeworld = new Coordinate(1, 20, 4, Celestials.Planet), Status = new System.Collections.Generic.List<string> { "inactive" } },
				new HighscorePlayer { ID = 11, Name = "B", Position = 6, Homeworld = new Coordinate(1, 30, 4, Celestials.Planet), Status = new System.Collections.Generic.List<string> { "inactive", "vacation" } },
				new HighscorePlayer { ID = 12, Name = "C", Position = 7, Homeworld = new Coordinate(1, 40, 4, Celestials.Planet), Status = new System.Collections.Generic.List<string> { "active" } }
			};
			c.RecordHighscore(hp, T0);
			c.FinishHighscore(T0.AddMinutes(-1), T0);
			Assert.Equal((1, 20), c.NextInactiveSystem(TimeSpan.FromHours(12), T0));
			c.Record(Sys(20, P(20, 4, 10, "A", inactive: true)), T0);
			Assert.Null(c.NextInactiveSystem(TimeSpan.FromHours(12), T0.AddHours(1)));
			c.Record(Sys(25, P(25, 2, 10, "A", inactive: true)), T0.AddHours(2));
			Assert.Equal((1, 20), c.NextInactiveSystem(TimeSpan.FromHours(12), T0.AddHours(13)));
			hp[0].Status = new System.Collections.Generic.List<string> { "active" };
			c.RecordHighscore(hp, T0.AddHours(3));
			c.FinishHighscore(T0.AddHours(3).AddMinutes(-1), T0.AddHours(3));
			var info = c.TryGet(1, 25, TimeSpan.FromHours(24), T0.AddHours(4));
			Assert.False(info.Planets[0].Inactive);
			Assert.Equal(5, info.Planets[0].Player.Rank);
		}

		[Fact]
		public void Inaktive_werden_auch_ohne_Highscore_Marker_aus_den_Galaxie_Scans_erkannt() {
			var c = new GalaxyCache(null);
			c.RecordHighscore(new[] { new HighscorePlayer { ID = 10, Name = "A", Position = 5, Homeworld = new Coordinate(1, 20, 4, Celestials.Planet), Status = new System.Collections.Generic.List<string>() } }, T0);
			c.FinishHighscore(T0.AddMinutes(-1), T0);
			c.Record(Sys(20, P(20, 4, 10, "A", inactive: true)), T0.AddHours(-20));
			Assert.Equal((1, 20), c.NextInactiveSystem(TimeSpan.FromHours(12), T0));
			var info = c.TryGet(1, 20, TimeSpan.FromHours(48), T0.AddHours(1));
			Assert.True(info.Planets[0].Inactive);
		}
	}
}
