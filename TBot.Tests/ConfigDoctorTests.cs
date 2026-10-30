using System.Linq;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Xunit;

namespace TBot.Tests {
	public class ConfigDoctorTests {
		private static JObject Cfg(string json) => JObject.Parse(json);
		private static string[] Pfade(System.Collections.Generic.List<ConfigFinding> f, string sev) => f.Where(x => x.Severity == sev).Select(x => x.Path).ToArray();

		[Fact]
		public void Saubere_Config_hat_keine_Meldung() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Active': true, 'Transports': { 'Active': true, 'MaxSlots': 2, 'Origin': { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Planet' } },
				'AutoMine': { 'Active': true, 'HomeWorld': { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Planet' } } } }"));
			Assert.Empty(f);
		}

		[Fact]
		public void Koordinate_ausserhalb_des_Bereichs_ist_ein_Fehler() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Transports': { 'Origin': { 'Galaxy': 10, 'System': 600, 'Position': 16, 'Type': 'Planet' } } } }"));
			Assert.Equal(3, Pfade(f, "error").Count(p => p == "Brain.Transports.Origin"));
		}

		[Fact]
		public void Vorlagenwert_1_1_1_wird_nur_bei_aktivem_Block_gemeldet() {
			var aktiv = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Active': true, 'Transports': { 'Active': true, 'MaxSlots': 1, 'Origin': { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet' } } } }"));
			Assert.Contains("Brain.Transports.Origin", Pfade(aktiv, "warn"));
			var aus = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Active': true, 'Transports': { 'Active': false, 'Origin': { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet' } } } }"));
			Assert.DoesNotContain("Brain.Transports.Origin", Pfade(aus, "warn"));
		}

		[Fact]
		public void Fremde_Koordinate_als_Origin_wird_gegen_das_Konto_geprueft() {
			var cfg = Cfg(@"{ 'Brain': { 'Active': true, 'Transports': { 'Active': true, 'MaxSlots': 1, 'Origin': { 'Galaxy': 1, 'System': 416, 'Position': 6, 'Type': 'Planet' } } } }");
			var owned = new[] { ConfigDoctor.CoordKey(2, 357, 8, "Planet") };
			Assert.Contains("Brain.Transports.Origin", Pfade(ConfigDoctor.Check(cfg, owned), "error"));
			Assert.DoesNotContain("Brain.Transports.Origin", Pfade(ConfigDoctor.Check(cfg, null), "error"));
		}

		[Fact]
		public void Worker_an_aber_Brain_aus_ist_ein_Widerspruch() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Active': false, 'AutoMine': { 'Active': true } } }"));
			Assert.Contains("Brain.AutoMine.Active", Pfade(f, "warn"));
		}

		[Fact]
		public void Transport_gewuenscht_aber_Transports_aus() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Brain': { 'Active': true, 'Transports': { 'Active': false }, 'AutoMine': { 'Active': true, 'HomeWorld': { 'Galaxy': 2, 'System': 3, 'Position': 4, 'Type': 'Planet' }, 'Transports': { 'Active': true } } } }"));
			Assert.Contains("Brain.AutoMine.Transports.Active", Pfade(f, "warn"));
		}

		[Fact]
		public void Schlafzeit_muss_eine_Uhrzeit_sein() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'SleepMode': { 'Active': true, 'GoToSleep': 'abends', 'WakeUp': '06:00:00' } }"));
			Assert.Contains("SleepMode.GoToSleep", Pfade(f, "error"));
		}

		[Fact]
		public void Minenplan_Modus_wird_geprueft() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Experimental': { 'MinePlanRanking': { 'Mode': 'Billig', 'PlanLookahead': 0 } } }"));
			Assert.Contains("Experimental.MinePlanRanking.Mode", Pfade(f, "error"));
			Assert.Contains("Experimental.MinePlanRanking.PlanLookahead", Pfade(f, "warn"));
		}

		[Fact]
		public void Expeditionen_ohne_Origin_sind_ein_Fehler() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Expeditions': { 'Active': true, 'Origin': [] } }"));
			Assert.Contains("Expeditions.Origin", Pfade(f, "error"));
		}

		[Fact]
		public void Gemeinsame_Load_Datei_zweier_Instanzen_wird_gemeldet() {
			string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cfgdoc-" + System.Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(dir);
			try {
				string a = System.IO.Path.Combine(dir, "a.json"), b = System.IO.Path.Combine(dir, "b.json");
				System.IO.File.WriteAllText(a, @"{ 'Brain': { 'Transports': { 'Load': 'Transports' } }, 'General': { 'Load': 'secrets/cred' } }".Replace('\'', '"'));
				System.IO.File.WriteAllText(b, @"{ 'Brain': { 'Transports': { 'Load': 'Transports' } }, 'General': { 'Load': 'secrets/cred' } }".Replace('\'', '"'));
				var f = ConfigDoctor.CheckShared(new[] { ("A", a), ("B", b) });
				Assert.Single(f);
				Assert.Equal("Brain.Transports", f[0].Path);
			} finally {
				System.IO.Directory.Delete(dir, true);
			}
		}
	

		[Fact]
		public void SmartShips_mit_aktivem_ManualShips_wird_gemeldet() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Expeditions': { 'Active': true, 'MinPrimaryToSend': 1, 'Origin': [ { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Moon' } ], 'SmartShips': true, 'ManualShips': { 'Active': true } } }"));
			Assert.Contains("Expeditions.SmartShips", Pfade(f, "warn"));
			var ok = ConfigDoctor.Check(Cfg(@"{ 'Expeditions': { 'Active': true, 'MinPrimaryToSend': 1, 'Origin': [ { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Moon' } ], 'SmartShips': true, 'SmartMixShip': 'SmallCargo', 'ManualShips': { 'Active': false } } }"));
			Assert.DoesNotContain("Expeditions.SmartShips", Pfade(ok, "warn"));
			Assert.DoesNotContain("Expeditions.SmartMixShip", Pfade(ok, "warn"));
		}

		[Fact]
		public void SmartMixShip_unbekannter_Typ_wird_gemeldet() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'Expeditions': { 'Active': true, 'MinPrimaryToSend': 1, 'Origin': [ { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Moon' } ], 'SmartShips': true, 'SmartMixShip': 'Cruiser' } }"));
			Assert.Contains("Expeditions.SmartMixShip", Pfade(f, "warn"));
		}

		[Fact]
		public void Harvest_grosse_Reichweite_und_negative_Werte() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': true, 'HarvestOwnDF': true, 'HarvestForeignDF': true, 'HarvestDeepSpace': true, 'DeepSpaceRange': 30, 'ForeignDFRange': -1, 'MaxSlots': 1 } }"));
			Assert.Contains("AutoHarvest.DeepSpaceRange", Pfade(f, "warn"));
			Assert.Contains("AutoHarvest.ForeignDFRange", Pfade(f, "error"));
			var klein = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': true, 'HarvestOwnDF': true, 'HarvestForeignDF': true, 'HarvestDeepSpace': true, 'DeepSpaceRange': 10, 'ForeignDFRange': 3, 'MaxSlots': 1 } }"));
			Assert.Empty(klein);
		}

		[Fact]
		public void Harvest_ohne_Auftrag_oder_Slots() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': true, 'HarvestOwnDF': false, 'HarvestForeignDF': false, 'HarvestDeepSpace': false, 'MaxSlots': 0 } }"));
			Assert.Contains("AutoHarvest", Pfade(f, "warn"));
			Assert.Contains("AutoHarvest.MaxSlots", Pfade(f, "warn"));
		}

		[Fact]
		public void Harvest_nur_Expo_Starts_ohne_Origins_faellt_auf_alle_zurueck() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': true, 'HarvestDeepSpace': true, 'ScanOnlyExpeditionOrigins': true, 'DeepSpaceRange': 5, 'MaxSlots': 1 }, 'Expeditions': { 'Active': false, 'Origin': [] } }"));
			Assert.Contains("AutoHarvest.ScanOnlyExpeditionOrigins", Pfade(f, "warn"));
			var ok = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': true, 'HarvestDeepSpace': true, 'ScanOnlyExpeditionOrigins': true, 'DeepSpaceRange': 5, 'MaxSlots': 1 }, 'Expeditions': { 'Active': false, 'Origin': [ { 'Galaxy': 2, 'System': 357, 'Position': 8, 'Type': 'Moon' } ] } }"));
			Assert.DoesNotContain("AutoHarvest.ScanOnlyExpeditionOrigins", Pfade(ok, "warn"));
		}

		[Fact]
		public void Harvest_inaktiv_wird_nicht_geprueft() {
			var f = ConfigDoctor.Check(Cfg(@"{ 'AutoHarvest': { 'Active': false, 'DeepSpaceRange': 99, 'MaxSlots': 0 } }"));
			Assert.Empty(f);
		}
	
		[Fact]
		public void Vorlagenkoordinaten_zum_Ersetzen_ohne_Exclude_und_ColonyOverrides() {
			var cfg = Cfg(@"{ 'Defender': { 'Home': { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet' } },
				'DebrisSave': { 'Celestials': [ { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet' } ] },
				'Brain': { 'AutoMine': { 'Exclude': [ { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet' } ],
					'ColonyOverrides': [ { 'Galaxy': 1, 'System': 1, 'Position': 1, 'Type': 'Planet', 'MaxMetalMine': 0 } ],
					'HomeWorld': { 'Galaxy': 5, 'System': 411, 'Position': 6, 'Type': 'Planet' } } } }");
			var offen = ConfigDoctor.FindTemplateCoordinates(cfg).Select(x => x.Path).OrderBy(x => x).ToArray();
			Assert.Equal(new[] { "DebrisSave.Celestials[0]", "Defender.Home" }, offen);
		}
}
}
