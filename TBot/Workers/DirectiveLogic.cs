using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public static class DirectiveLogic {
		public enum GoalKind { Unknown, BuildingLevel, ResearchLevel, ShipCount, DefenceCount, Manual, Fleet, LifeformBuilding, LifeformResearch }

		public sealed class DirectiveAction {
			public string Text { get; set; } = "";
			public bool Done { get; set; }
			public GoalKind Kind { get; set; }
			public Buildables Item { get; set; }
			public LFBuildables LFItem { get; set; }
			public LFTechno LFTech { get; set; }
			public int Value { get; set; }
			public int PlanetIndex { get; set; }
			public bool OnColony { get; set; }
			public string FleetTag { get; set; } = "";
			public override string ToString() => $"[{(Done ? "x" : " ")}] {Kind} {(Item != Buildables.Null ? Item + " " + Value : FleetTag)} - {Text}";
		}

		public sealed class DirectiveTaskInfo {
			public long TaskId { get; set; }
			public string State { get; set; } = "";
			public long Progress { get; set; }
			public long Total { get; set; }
			public bool CanCollect { get; set; }
			public List<DirectiveAction> Actions { get; set; } = new();
		}

		public sealed class DirectiveChapterInfo {
			public int ChapterId { get; set; }
			public bool CanClaimChapter { get; set; }
			public string ChapterButtonClass { get; set; } = "";
			public List<DirectiveTaskInfo> Tasks { get; set; } = new();
		}

		static string Clean(string s) => Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(s ?? "", "<[^>]+>", " ")), "\\s+", " ").Trim();

		public static DirectiveChapterInfo ParseChapter(int chapterId, string html) {
			var ch = new DirectiveChapterInfo { ChapterId = chapterId };
			if (string.IsNullOrEmpty(html))
				return ch;
			foreach (Match m in Regex.Matches(html, "<(?:a|button|div|span)[^>]*class=\"([^\"]*)\"[^>]*>")) {
				string cls = m.Groups[1].Value;
				if (Regex.IsMatch(cls, "\\bclaimRewards\\b")) {
					ch.ChapterButtonClass = cls;
					if (!Regex.IsMatch(cls, "\\bdisabled\\b"))
						ch.CanClaimChapter = true;
				}
			}
			var starts = Regex.Matches(html, "<div[^>]*class=\"[^\"]*ipiTaskItem(?![A-Za-z])[^\"]*\"[^>]*>").Cast<Match>().ToList();
			for (int i = 0; i < starts.Count; i++) {
				string tag = starts[i].Value;
				var id = Regex.Match(tag, "data-taskid=\"(\\d+)\"");
				if (!id.Success)
					continue;
				int from = starts[i].Index + tag.Length;
				int to = i + 1 < starts.Count ? starts[i + 1].Index : html.Length;
				string block = html.Substring(from, Math.Max(0, to - from));
				var t = new DirectiveTaskInfo { TaskId = long.Parse(id.Groups[1].Value, CultureInfo.InvariantCulture) };
				var st = Regex.Match(tag, "data-state=\"(\\w+)\"");
				t.State = st.Success ? st.Groups[1].Value : "";
				var pt = Regex.Match(block, "<[^>]*ipiTaskItemProgress[^>]*>").Value;
				var p = Regex.Match(pt, "data-progress=\"(\\d+)\"");
				var tt = Regex.Match(pt, "data-total=\"(\\d+)\"");
				if (p.Success) t.Progress = long.Parse(p.Groups[1].Value, CultureInfo.InvariantCulture);
				if (tt.Success) t.Total = long.Parse(tt.Groups[1].Value, CultureInfo.InvariantCulture);
				foreach (Match a in Regex.Matches(block, "<a[^>]*class=\"([^\"]*)\"[^>]*>")) {
					string cls = a.Groups[1].Value;
					if (Regex.IsMatch(cls, "\\bclaimTaskRewards\\b") && !Regex.IsMatch(cls, "\\bdisabled\\b"))
						t.CanCollect = true;
				}
				foreach (Match li in Regex.Matches(block, "<li[^>]*class=\"[^\"]*ipiActionItem[^\"]*\"[^>]*>(.*?)</li>", RegexOptions.Singleline)) {
					string inner = li.Groups[1].Value;
					var act = Classify(Clean(inner));
					act.Done = inner.Contains("ipiActionCompletedMark") || li.Value.Contains("ipiActionCompletedMark");
					t.Actions.Add(act);
				}
				ch.Tasks.Add(t);
			}
			return ch;
		}

		static readonly (string Name, Buildables Item)[] Names = BuildNames();

		static string Norm(string s) {
			s = (s ?? "").ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");
			return Regex.Replace(s, "[^a-z0-9]+", "");
		}

		static (string, Buildables)[] BuildNames() {
			var l = new List<(string, Buildables)>();
			void A(Buildables b, params string[] names) { foreach (var n in names) l.Add((Norm(n), b)); }
			A(Buildables.MetalMine, "Metallmine", "Metal Mine");
			A(Buildables.CrystalMine, "Kristallmine", "Crystal Mine");
			A(Buildables.DeuteriumSynthesizer, "Deuteriumsynthetisierer", "Deuterium Synthesizer");
			A(Buildables.SolarPlant, "Solarkraftwerk", "Solar Plant");
			A(Buildables.FusionReactor, "Fusionskraftwerk", "Fusion Reactor");
			A(Buildables.MetalStorage, "Metallspeicher", "Metal Storage");
			A(Buildables.CrystalStorage, "Kristallspeicher", "Crystal Storage");
			A(Buildables.DeuteriumTank, "Deuteriumtank", "Deuterium Tank");
			A(Buildables.RoboticsFactory, "Roboterfabrik", "Robotics Factory");
			A(Buildables.Shipyard, "Raumschiffswerft", "Werft", "Shipyard");
			A(Buildables.ResearchLab, "Forschungslabor", "Research Lab");
			A(Buildables.AllianceDepot, "Allianzdepot", "Alliance Depot");
			A(Buildables.MissileSilo, "Raketensilo", "Missile Silo");
			A(Buildables.NaniteFactory, "Nanitenfabrik", "Nanite Factory");
			A(Buildables.Terraformer, "Terraformer");
			A(Buildables.InterstellarAnomalyScanner, "Interstellar Anomaly Scanner", "Interstellarer Anomalie-Scanner", "Interstellarer Anomaliescanner");
			A(Buildables.SpaceDock, "Raumdock", "Space Dock");
			A(Buildables.EspionageTechnology, "Spionagetechnik", "Espionage Technology");
			A(Buildables.ComputerTechnology, "Computertechnik", "Computer Technology");
			A(Buildables.WeaponsTechnology, "Waffentechnik", "Weapons Technology");
			A(Buildables.ShieldingTechnology, "Schildtechnik", "Shielding Technology");
			A(Buildables.ArmourTechnology, "Raumschiffpanzerung", "Armour Technology", "Armor Technology");
			A(Buildables.EnergyTechnology, "Energietechnik", "Energy Technology");
			A(Buildables.HyperspaceTechnology, "Hyperraumtechnik", "Hyperspace Technology");
			A(Buildables.CombustionDrive, "Verbrennungstriebwerk", "Combustion Drive");
			A(Buildables.ImpulseDrive, "Impulstriebwerk", "Impulse Drive");
			A(Buildables.HyperspaceDrive, "Hyperraumantrieb", "Hyperspace Drive");
			A(Buildables.LaserTechnology, "Lasertechnik", "Laser Technology");
			A(Buildables.IonTechnology, "Ionentechnik", "Ion Technology");
			A(Buildables.PlasmaTechnology, "Plasmatechnik", "Plasma Technology");
			A(Buildables.IntergalacticResearchNetwork, "Intergalaktisches Forschungsnetzwerk", "Intergalactic Research Network");
			A(Buildables.Astrophysics, "Astrophysik", "Astrophysics");
			A(Buildables.GravitonTechnology, "Gravitonforschung", "Graviton Technology");
			A(Buildables.SmallCargo, "Kleiner Transporter", "Small Cargo");
			A(Buildables.LargeCargo, "Grosser Transporter", "Großer Transporter", "Large Cargo");
			A(Buildables.LightFighter, "Leichter Jaeger", "Leichter Jäger", "Light Fighter");
			A(Buildables.HeavyFighter, "Schwerer Jaeger", "Schwerer Jäger", "Heavy Fighter");
			A(Buildables.Cruiser, "Kreuzer", "Cruiser");
			A(Buildables.Battleship, "Schlachtschiff", "Battleship");
			A(Buildables.ColonyShip, "Kolonieschiff", "Colony Ship");
			A(Buildables.Recycler, "Recycler");
			A(Buildables.EspionageProbe, "Spionagesonde", "Espionage Probe");
			A(Buildables.Bomber, "Bomber");
			A(Buildables.SolarSatellite, "Solarsatellit", "Solar Satellite");
			A(Buildables.Destroyer, "Zerstoerer", "Zerstörer", "Destroyer");
			A(Buildables.Deathstar, "Todesstern", "Deathstar");
			A(Buildables.Battlecruiser, "Schlachtkreuzer", "Battlecruiser");
			A(Buildables.Crawler, "Crawler");
			A(Buildables.Reaper, "Reaper");
			A(Buildables.Pathfinder, "Pathfinder");
			A(Buildables.RocketLauncher, "Raketenwerfer", "Rocket Launcher");
			A(Buildables.LightLaser, "Leichtes Lasergeschuetz", "Leichtes Lasergeschütz", "Light Laser");
			A(Buildables.HeavyLaser, "Schweres Lasergeschuetz", "Schweres Lasergeschütz", "Heavy Laser");
			A(Buildables.GaussCannon, "Gausskanone", "Gaußkanone", "Gauss Cannon");
			A(Buildables.IonCannon, "Ionengeschuetz", "Ionengeschütz", "Ion Cannon");
			A(Buildables.PlasmaTurret, "Plasmawerfer", "Plasma Turret");
			A(Buildables.SmallShieldDome, "Kleine Schildkuppel", "Small Shield Dome");
			A(Buildables.LargeShieldDome, "Grosse Schildkuppel", "Große Schildkuppel", "Large Shield Dome");
			return l.OrderByDescending(x => x.Item1.Length).ToArray();
		}

		public static Buildables FindItem(string text) {
			string n = Norm(text);
			foreach (var (name, item) in Names)
				if (n == name) return item;
			foreach (var (name, item) in Names)
				if (n.Contains(name)) return item;
			return Buildables.Null;
		}

		public static bool IsResearch(Buildables b) => (int) b >= 106 && (int) b <= 199;
		public static bool IsShip(Buildables b) => (int) b >= 202 && (int) b <= 219;
		public static bool IsDefence(Buildables b) => (int) b >= 401 && (int) b <= 408;

		public static LFBuildables FindLifeformBuilding(string low) {
			LFBuildables best = LFBuildables.None; int bestLen = 0;
			foreach (LFBuildables b in Enum.GetValues(typeof(LFBuildables))) {
				if (b == LFBuildables.None) continue;
				string name = Regex.Replace(b.ToString(), "(?<=[a-z])(?=[A-Z])", " ").ToLowerInvariant();
				if (low.Contains(name) && name.Length > bestLen) { best = b; bestLen = name.Length; }
			}
			return best;
		}

		public static LFTechno FindLifeformTech(string low) {
			string flat = Regex.Replace(low, "[^a-z]", "");
			LFTechno best = LFTechno.None; int bestLen = 0;
			foreach (LFTechno t in Enum.GetValues(typeof(LFTechno))) {
				if (t == LFTechno.None) continue;
				string name = t.ToString().ToLowerInvariant();
				if (flat.Contains(name) && name.Length > bestLen) { best = t; bestLen = name.Length; }
			}
			return best;
		}

		public static DirectiveAction Classify(string text) {
			var a = new DirectiveAction { Text = text ?? "" };
			string t = a.Text;
			string low = t.ToLowerInvariant();

			bool hasLevel = Regex.IsMatch(low, "stufe|level");
			if ((!hasLevel && Regex.IsMatch(low, "allianz|alliance")) || Regex.IsMatch(low, "buddy|umbenenn|benenne .* um\\b|rename|charakterklasse|character class|rohstoffh|resource trader|merchant|artefakt|artefact|artifact|rei(ss|ß)e .* ab|abrei|demolish|tear down|technologiebaum|technology tree|lebensform finden|lebensform .*zuweis|assign .*life|find .*life")) {
				a.Kind = GoalKind.Manual;
				return a;
			}
			if (Regex.IsMatch(low, "anderen spieler|another player|other player")) {
				a.Kind = GoalKind.Manual;
				return a;
			}
			if (Regex.IsMatch(low, "lithium") && Regex.IsMatch(low, "conversion|umwandl")) { a.Kind = GoalKind.Fleet; a.FleetTag = "lithium"; return a; }
			if (low.Contains("position 16")) { a.Kind = GoalKind.Fleet; a.FleetTag = "spy16"; return a; }
			if (Regex.IsMatch(low, "zur(ue|ü)ck|recall")) { a.Kind = GoalKind.Fleet; a.FleetTag = "recall"; return a; }
			if (Regex.IsMatch(low, "expedition")) { a.Kind = GoalKind.Fleet; a.FleetTag = "expedition"; return a; }
			if (Regex.IsMatch(low, "entdeckung|discovery")) { a.Kind = GoalKind.Fleet; a.FleetTag = "discovery"; return a; }
			if (Regex.IsMatch(low, "tr(ue|ü)mmerfeld|debris")) { a.Kind = GoalKind.Fleet; a.FleetTag = "debris"; return a; }
			if (!hasLevel && Regex.IsMatch(low, "kolonie (gr(ue|ü)nden|besiedeln)|gr(ue|ü)nde .*kolonie|colonize|colonise|found .*colony|establish .*colony")) { a.Kind = GoalKind.Fleet; a.FleetTag = "colonize"; return a; }
			if (Regex.IsMatch(low, "(leistung|production|produktion).*%|\\d+ ?%")) { a.Kind = GoalKind.Fleet; a.FleetTag = "minepower"; return a; }

			var mp = Regex.Match(low, "\\((\\d+)(?:st|nd|rd|th)? planet\\)|\\((\\d+)\\. planet\\)");
			if (mp.Success)
				a.PlanetIndex = int.Parse(mp.Groups[1].Success ? mp.Groups[1].Value : mp.Groups[2].Value, CultureInfo.InvariantCulture);
			a.OnColony = Regex.IsMatch(low, "kolonialisiert|kolonisiert|colonised|colonized|colony|kolonie");

			var mc = Regex.Match(t, "(\\d+)\\s*(?:mal|x|times)\\s+(.+)$", RegexOptions.IgnoreCase);
			if (mc.Success) {
				var it = FindItem(mc.Groups[2].Value);
				if (it != Buildables.Null && (IsShip(it) || IsDefence(it))) {
					a.Item = it; a.Value = int.Parse(mc.Groups[1].Value, CultureInfo.InvariantCulture);
					a.Kind = IsShip(it) ? GoalKind.ShipCount : GoalKind.DefenceCount;
					return a;
				}
			}
			var ml = Regex.Match(t, "(?:auf\\s+Stufe|to\\s+level|level)\\s+(\\d+)", RegexOptions.IgnoreCase);
			if (ml.Success) {
				if (Regex.IsMatch(low, "lebensform|lifeform|life form")) {
					a.Kind = GoalKind.Unknown;
					a.FleetTag = "lifeform";
					a.Value = int.Parse(ml.Groups[1].Value, CultureInfo.InvariantCulture);
					if (Regex.IsMatch(low, "(lifeform|life form) building|lebensform-?geb(ae|ä)ude")) {
						var lf = FindLifeformBuilding(low);
						if (lf != LFBuildables.None) { a.Kind = GoalKind.LifeformBuilding; a.LFItem = lf; a.FleetTag = ""; }
					} else if (Regex.IsMatch(low, "(lifeform|life form) research|lebensform-?forschung") && !Regex.IsMatch(low, "slot")) {
						var tech = FindLifeformTech(low);
						if (tech != LFTechno.None) { a.Kind = GoalKind.LifeformResearch; a.LFTech = tech; a.FleetTag = ""; }
					}
					return a;
				}
				var it = FindItem(t);
				if (it != Buildables.Null) {
					a.Item = it; a.Value = int.Parse(ml.Groups[1].Value, CultureInfo.InvariantCulture);
					a.Kind = IsResearch(it) ? GoalKind.ResearchLevel : GoalKind.BuildingLevel;
					return a;
				}
			}
			var single = FindItem(t);
			if (single != Buildables.Null && Regex.IsMatch(low, "^(baue|build)") && (IsShip(single) || IsDefence(single))) {
				a.Item = single; a.Value = 1; a.Kind = IsShip(single) ? GoalKind.ShipCount : GoalKind.DefenceCount;
				return a;
			}
			a.Kind = GoalKind.Unknown;
			return a;
		}
	}
}

namespace Tbot.Workers {
	public static class DirectivePrereq {
		static (Buildables Item, int Level) R(Buildables b, int l) => (b, l);
		static readonly Dictionary<Buildables, (Buildables Item, int Level)[]> Req = new() {
			[Buildables.Shipyard] = new[] { R(Buildables.RoboticsFactory, 2) },
			[Buildables.NaniteFactory] = new[] { R(Buildables.RoboticsFactory, 10), R(Buildables.ComputerTechnology, 10) },
			[Buildables.SpaceDock] = new[] { R(Buildables.Shipyard, 2) },
			[Buildables.MissileSilo] = new[] { R(Buildables.Shipyard, 1) },
			[Buildables.Terraformer] = new[] { R(Buildables.NaniteFactory, 1), R(Buildables.EnergyTechnology, 12) },
			[Buildables.FusionReactor] = new[] { R(Buildables.DeuteriumSynthesizer, 5), R(Buildables.EnergyTechnology, 5) },
			[Buildables.EnergyTechnology] = new[] { R(Buildables.ResearchLab, 1) },
			[Buildables.LaserTechnology] = new[] { R(Buildables.ResearchLab, 1), R(Buildables.EnergyTechnology, 2) },
			[Buildables.IonTechnology] = new[] { R(Buildables.ResearchLab, 4), R(Buildables.LaserTechnology, 5), R(Buildables.EnergyTechnology, 4) },
			[Buildables.HyperspaceTechnology] = new[] { R(Buildables.ResearchLab, 7), R(Buildables.EnergyTechnology, 5), R(Buildables.ShieldingTechnology, 5) },
			[Buildables.PlasmaTechnology] = new[] { R(Buildables.ResearchLab, 4), R(Buildables.EnergyTechnology, 8), R(Buildables.LaserTechnology, 10), R(Buildables.IonTechnology, 5) },
			[Buildables.CombustionDrive] = new[] { R(Buildables.ResearchLab, 1), R(Buildables.EnergyTechnology, 1) },
			[Buildables.ImpulseDrive] = new[] { R(Buildables.ResearchLab, 2), R(Buildables.EnergyTechnology, 1) },
			[Buildables.HyperspaceDrive] = new[] { R(Buildables.ResearchLab, 7), R(Buildables.HyperspaceTechnology, 3) },
			[Buildables.EspionageTechnology] = new[] { R(Buildables.ResearchLab, 3) },
			[Buildables.ComputerTechnology] = new[] { R(Buildables.ResearchLab, 1) },
			[Buildables.Astrophysics] = new[] { R(Buildables.ResearchLab, 3), R(Buildables.EspionageTechnology, 4), R(Buildables.ImpulseDrive, 3) },
			[Buildables.IntergalacticResearchNetwork] = new[] { R(Buildables.ResearchLab, 10), R(Buildables.ComputerTechnology, 8), R(Buildables.HyperspaceTechnology, 8) },
			[Buildables.GravitonTechnology] = new[] { R(Buildables.ResearchLab, 12) },
			[Buildables.WeaponsTechnology] = new[] { R(Buildables.ResearchLab, 4) },
			[Buildables.ShieldingTechnology] = new[] { R(Buildables.ResearchLab, 6), R(Buildables.EnergyTechnology, 3) },
			[Buildables.ArmourTechnology] = new[] { R(Buildables.ResearchLab, 2) },
			[Buildables.SmallCargo] = new[] { R(Buildables.Shipyard, 2), R(Buildables.CombustionDrive, 2) },
			[Buildables.LargeCargo] = new[] { R(Buildables.Shipyard, 4), R(Buildables.CombustionDrive, 6) },
			[Buildables.LightFighter] = new[] { R(Buildables.Shipyard, 1), R(Buildables.CombustionDrive, 1) },
			[Buildables.HeavyFighter] = new[] { R(Buildables.Shipyard, 3), R(Buildables.ArmourTechnology, 2), R(Buildables.ImpulseDrive, 2) },
			[Buildables.Cruiser] = new[] { R(Buildables.Shipyard, 5), R(Buildables.ImpulseDrive, 4), R(Buildables.IonTechnology, 2) },
			[Buildables.Battleship] = new[] { R(Buildables.Shipyard, 7), R(Buildables.HyperspaceDrive, 4) },
			[Buildables.Battlecruiser] = new[] { R(Buildables.Shipyard, 8), R(Buildables.HyperspaceTechnology, 5), R(Buildables.LaserTechnology, 12), R(Buildables.HyperspaceDrive, 5) },
			[Buildables.Bomber] = new[] { R(Buildables.Shipyard, 8), R(Buildables.ImpulseDrive, 6), R(Buildables.IonTechnology, 5) },
			[Buildables.Destroyer] = new[] { R(Buildables.Shipyard, 9), R(Buildables.HyperspaceTechnology, 5), R(Buildables.HyperspaceDrive, 6) },
			[Buildables.Deathstar] = new[] { R(Buildables.Shipyard, 12), R(Buildables.HyperspaceTechnology, 6), R(Buildables.HyperspaceDrive, 7), R(Buildables.GravitonTechnology, 1) },
			[Buildables.Recycler] = new[] { R(Buildables.Shipyard, 4), R(Buildables.CombustionDrive, 6), R(Buildables.ShieldingTechnology, 2) },
			[Buildables.ColonyShip] = new[] { R(Buildables.Shipyard, 4), R(Buildables.ImpulseDrive, 3) },
			[Buildables.EspionageProbe] = new[] { R(Buildables.Shipyard, 3), R(Buildables.CombustionDrive, 3), R(Buildables.EspionageTechnology, 2) },
			[Buildables.SolarSatellite] = new[] { R(Buildables.Shipyard, 1) },
			[Buildables.RocketLauncher] = new[] { R(Buildables.Shipyard, 1) },
			[Buildables.LightLaser] = new[] { R(Buildables.Shipyard, 2), R(Buildables.EnergyTechnology, 1), R(Buildables.LaserTechnology, 3) },
			[Buildables.HeavyLaser] = new[] { R(Buildables.Shipyard, 4), R(Buildables.EnergyTechnology, 3), R(Buildables.LaserTechnology, 6) },
			[Buildables.GaussCannon] = new[] { R(Buildables.Shipyard, 6), R(Buildables.EnergyTechnology, 6), R(Buildables.WeaponsTechnology, 3), R(Buildables.ShieldingTechnology, 1) },
			[Buildables.IonCannon] = new[] { R(Buildables.Shipyard, 4), R(Buildables.IonTechnology, 4) },
			[Buildables.PlasmaTurret] = new[] { R(Buildables.Shipyard, 8), R(Buildables.PlasmaTechnology, 7) },
			[Buildables.SmallShieldDome] = new[] { R(Buildables.Shipyard, 1), R(Buildables.ShieldingTechnology, 2) },
			[Buildables.LargeShieldDome] = new[] { R(Buildables.Shipyard, 6), R(Buildables.ShieldingTechnology, 6) },
		};

		public static bool IsResearch(Buildables b) => (int) b >= 106 && (int) b <= 199;

		public static (Buildables Item, int Level)? FirstMissing(Buildables item, Func<Buildables, int> level, int depth = 0) {
			if (depth > 8 || !Req.TryGetValue(item, out var reqs)) return null;
			foreach (var r in reqs) {
				if (level(r.Item) >= r.Level) continue;
				if (level(r.Item) == 0) {
					var deeper = FirstMissing(r.Item, level, depth + 1);
					if (deeper != null) return deeper;
				}
				return r;
			}
			return null;
		}
	}
}
