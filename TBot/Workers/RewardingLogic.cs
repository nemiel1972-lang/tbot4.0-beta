using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public static class RewardingLogic {
		public enum TaskKind { Unknown, Defence, SendFleet, StartBuilding, VisitMarket, Manual, SendMessage, VisitHighscore, Transport, CollectDebris, Spy, ShipBuild, VisitScrap, VisitOfficers, VisitShop, VisitTechtree, OtherWorker, StartResearch }

		public class RewardTask {
			public string Title = "";
			public string Text = "";
			public int Tritium;
			public int Have = -1;
			public int Need = -1;
			public bool Done;
			public TaskKind Kind = TaskKind.Unknown;
			public Buildables Defence = Buildables.Null;
			public Buildables Ship = Buildables.Null;
			public int Amount;
			public string Why = "";
			public string Resource = "";
		}

		public class RankReward {
			public int Id;
			public string Name = "";
			public string Quantity = "";
			public bool Additional;
		}

		public class Tier {
			public int Number;
			public int Have = -1;
			public int Need = -1;
			public bool Claimed;
			public List<RankReward> Rewards = new();
		}

		public class ClaimPlan {
			public int Tier;
			public int RewardId;
			public string Name = "";
		}

		public static List<ClaimPlan> PlanClaims(IEnumerable<Tier> tiers, IDictionary<int, (int id, string name)> choices, List<int> ohneWahl = null) {
			var plan = new List<ClaimPlan>();
			foreach (var t in tiers.OrderBy(x => x.Number)) {
				if (t.Claimed || t.Need <= 0 || t.Have < t.Need)
					continue;
				if (choices != null && choices.TryGetValue(t.Number, out var c)
					&& t.Rewards.Any(r => !r.Additional && r.Id == c.id && r.Name == c.name)) {
					plan.Add(new ClaimPlan { Tier = t.Number, RewardId = c.id, Name = c.name });
				} else {
					ohneWahl?.Add(t.Number);
				}
			}
			return plan;
		}

		public static bool AllClaimed(IEnumerable<Tier> tiers) {
			var last = tiers?.OrderBy(x => x.Number).LastOrDefault();
			return last != null && last.Claimed;
		}

		public static bool ClaimSucceeded(string answer, out string message) {
			message = "";
			try {
				using var doc = JsonDocument.Parse(answer ?? "");
				var root = doc.RootElement;
				if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
					message = m.GetString() ?? "";
				bool ok = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
				if (root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
					ok = ok && st.GetString() == "success";
				return ok;
			} catch (JsonException) {
				message = "answer is not JSON";
				return false;
			}
		}

		public static Tier ParseTier(int number, string answer) {
			string html = answer ?? "";
			if (html.TrimStart().StartsWith("{")) {
				try {
					using var doc = JsonDocument.Parse(html);
					if (doc.RootElement.TryGetProperty("content", out var c) && c.TryGetProperty("rewarding", out var r))
						html = r.GetString() ?? "";
				} catch (JsonException) { }
			}
			var t = new Tier { Number = number };
			string head = Clean(Regex.Match(html, "<div class=\"currentTritium\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);
			var hm = Regex.Match(head, "(\\d+)\\s*(?:von|of)\\s*(\\d+)", RegexOptions.IgnoreCase);
			if (hm.Success) {
				t.Have = int.Parse(hm.Groups[1].Value);
				t.Need = int.Parse(hm.Groups[2].Value);
			}
			t.Claimed = Regex.IsMatch(html, "bereits erhalten|already (?:received|claimed)", RegexOptions.IgnoreCase);
			int extra = html.IndexOf("additionalRewards", StringComparison.Ordinal);
			var parts = Regex.Split(html, "(?=<div class=\"col w-150 singleReward\")");
			int pos = 0;
			foreach (var raw in parts) {
				int start = pos;
				pos += raw.Length;
				string part = raw;
				foreach (var marker in new[] { "<div id=\"header\"", "<script" }) {
					int k = part.IndexOf(marker, StringComparison.Ordinal);
					if (k > 0) part = part.Substring(0, k);
				}
				var m = Regex.Match(part, "^<div class=\"col w-150 singleReward\" data-id=\"(\\d+)\"");
				if (!m.Success) continue;
				var rw = new RankReward { Id = int.Parse(m.Groups[1].Value), Additional = extra >= 0 && start > extra };
				rw.Name = Clean(Regex.Match(part, "<div class=\"rewardName\">(.*?)</div>", RegexOptions.Singleline).Groups[1].Value);
				var q = Regex.Match(part, "<div class=\"quantity\">(.*?)(?=<div class=\"selectReward\"|$)", RegexOptions.Singleline);
				string qt = Regex.Replace(q.Groups[1].Value, "&#x231b;", " ");
				rw.Quantity = Clean(qt);
				t.Rewards.Add(rw);
			}
			return t;
		}

		public static List<RewardTask> ParseTasks(string answer) {
			string html = answer ?? "";
			if (html.TrimStart().StartsWith("{")) {
				try {
					using var doc = JsonDocument.Parse(html);
					if (doc.RootElement.TryGetProperty("content", out var c) && c.TryGetProperty("rewarding", out var r))
						html = r.GetString() ?? "";
				} catch (JsonException) { }
			}
			html = html.Replace(' ', ' ');
			var list = new List<RewardTask>();
			int tritium = 0;
			var tokens = Regex.Matches(html, "<div class=\"tritiumvalue\">[^<]*?(\\d+)\\s*</div>|<div class=\"rewardlist-item\">(.*?)(?=<div class=\"rewardlist-item\">|<div class=\"tritiumstage\">|<div id=\"commandingstaff\">|$)", RegexOptions.Singleline);
			foreach (Match m in tokens) {
				if (m.Groups[1].Success) {
					tritium = int.Parse(m.Groups[1].Value);
					continue;
				}
				string block = m.Groups[2].Value;
				var t = new RewardTask { Tritium = tritium };
				t.Title = Clean(Regex.Match(block, "<h3>(.*?)</h3>", RegexOptions.Singleline).Groups[1].Value);
				var ps = Regex.Matches(block, "<p>(.*?)</p>", RegexOptions.Singleline).Select(x => Clean(x.Groups[1].Value)).Where(x => x.Length > 0).ToList();
				t.Text = ps.FirstOrDefault(x => !x.StartsWith("Fortschritt", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("Progress", StringComparison.OrdinalIgnoreCase)) ?? "";
				var pm = Regex.Match(string.Join(" ", ps), "(?:Fortschritt|Progress):\\s*(\\d+)\\s*/\\s*(\\d+)", RegexOptions.IgnoreCase);
				if (pm.Success) { t.Have = int.Parse(pm.Groups[1].Value); t.Need = int.Parse(pm.Groups[2].Value); }
				t.Done = block.Contains("reward-claimed-text");
				Classify(t);
				list.Add(t);
			}
			return list;
		}

		private static string Clean(string s) {
			s = Regex.Replace(s ?? "", "<[^>]+>", " ");
			s = System.Net.WebUtility.HtmlDecode(s).Replace(' ', ' ');
			return Regex.Replace(s, "\\s+", " ").Trim();
		}

		private static readonly Regex Gesperrt = new("kaufe|kaufen|offizierskasino|dunkle materie|dunkler materie|gegenstand|item|sofort fertig|beschleunig|buy|purchase|officers? ?(?:mess|casino)|dark matter|instantly|speed ?up|premium", RegexOptions.IgnoreCase);

		private static readonly (string[] names, Buildables unit)[] Defences = {
			(new[] { "raketenwerfer", "rocket launcher" }, Buildables.RocketLauncher),
			(new[] { "leichtes lasergeschütz", "leichtes lasergeschuetz", "light laser" }, Buildables.LightLaser),
			(new[] { "schweres lasergeschütz", "schweres lasergeschuetz", "heavy laser" }, Buildables.HeavyLaser),
			(new[] { "gaußkanone", "gausskanone", "gauss cannon", "gauß cannon" }, Buildables.GaussCannon),
			(new[] { "ionengeschütz", "ionengeschuetz", "ion cannon" }, Buildables.IonCannon),
			(new[] { "plasmawerfer", "plasma turret", "plasma cannon" }, Buildables.PlasmaTurret),
			(new[] { "kleine schildkuppel", "small shield dome" }, Buildables.SmallShieldDome),
			(new[] { "große schildkuppel", "grosse schildkuppel", "large shield dome" }, Buildables.LargeShieldDome),
		};

		private static readonly (string[] names, Buildables unit)[] Ships = {
			(new[] { "kleiner transporter", "small cargo" }, Buildables.SmallCargo),
			(new[] { "großer transporter", "grosser transporter", "large cargo" }, Buildables.LargeCargo),
			(new[] { "leichter jäger", "leichter jaeger", "light fighter" }, Buildables.LightFighter),
			(new[] { "schwerer jäger", "schwerer jaeger", "heavy fighter" }, Buildables.HeavyFighter),
			(new[] { "schlachtkreuzer", "battlecruiser" }, Buildables.Battlecruiser),
			(new[] { "schlachtschiff", "battleship" }, Buildables.Battleship),
			(new[] { "kreuzer", "cruiser" }, Buildables.Cruiser),
			(new[] { "kolonieschiff", "colony ship" }, Buildables.ColonyShip),
			(new[] { "recycler" }, Buildables.Recycler),
			(new[] { "spionagesonde", "espionage probe" }, Buildables.EspionageProbe),
			(new[] { "bomber" }, Buildables.Bomber),
			(new[] { "solarsatellit", "solar satellite" }, Buildables.SolarSatellite),
			(new[] { "zerstörer", "zerstoerer", "destroyer" }, Buildables.Destroyer),
			(new[] { "todesstern", "deathstar", "death star" }, Buildables.Deathstar),
			(new[] { "reaper" }, Buildables.Reaper),
			(new[] { "pathfinder" }, Buildables.Pathfinder),
		};

		internal static void Classify(RewardTask t) {
			string all = (t.Title + " " + t.Text).ToLowerInvariant();
			if (Regex.IsMatch(all, "import/export|import / export|auktion|auction")) {
				t.Kind = TaskKind.OtherWorker;
				t.Why = "done by another worker";
				return;
			}
			if (Regex.IsMatch(all, "(?:besuch|visit).{0,40}(?:offiziersmesse|offizierskasino)|(?:besuch|visit).{0,40}officers.{0,3}\\s?(?:mess|casino)")
				&& !Regex.IsMatch(all, "kaufe|kaufen|buy|purchase|dunkle materie|dunkler materie|dark matter|anheuern|rekrutier|hire|recruit")) {
				t.Kind = TaskKind.VisitOfficers;
				return;
			}
			if (Regex.IsMatch(all, "(?:besuch|visit).{0,20}\\bshop\\b")
				&& !Regex.IsMatch(all, "kaufe|kaufen|buy|purchase|dunkle materie|dunkler materie|dark matter")) {
				t.Kind = TaskKind.VisitShop;
				return;
			}
			if (Gesperrt.IsMatch(all)) {
				t.Kind = TaskKind.Manual;
				t.Why = "costs Dark Matter / items / purchase";
				return;
			}
			var d = Regex.Match(t.Text, "(?:baue|build)\\s+(\\d+)\\s*x\\s+(.+?)(?:\\s+in der verteidigung|\\s+in (?:the )?defen[cs]e|$)", RegexOptions.IgnoreCase);
			if (d.Success) {
				string name = d.Groups[2].Value.ToLowerInvariant();
				foreach (var (names, unit) in Defences) {
					if (names.Any(n => name.Contains(n))) {
						t.Kind = TaskKind.Defence;
						t.Defence = unit;
						int total = int.Parse(d.Groups[1].Value);
						t.Amount = t.Have >= 0 && t.Need >= 0 ? Math.Max(0, t.Need - t.Have) : total;
						return;
					}
				}
			}
			var sb = Regex.Match(t.Text, "(?:baue|build)\\s+(\\d+)\\s*x\\s+(.+?)(?:\\s+in der schiffsw|\\s+in (?:the )?shipyard|$)", RegexOptions.IgnoreCase);
			if (sb.Success) {
				string sname = sb.Groups[2].Value.ToLowerInvariant();
				foreach (var (names, unit) in Ships) {
					if (names.Any(n => sname.Contains(n))) {
						t.Kind = TaskKind.ShipBuild;
						t.Ship = unit;
						int total = int.Parse(sb.Groups[1].Value);
						t.Amount = t.Have >= 0 && t.Need >= 0 ? Math.Max(0, t.Need - t.Have) : total;
						return;
					}
				}
			}
			if (Regex.IsMatch(all, "techtree|technologieinformation|technology ?information|tech ?tree"))
				t.Kind = TaskKind.VisitTechtree;
			else if (Regex.IsMatch(all, "schrotth(?:ä|ae)ndler|scrap ?merchant|junk ?(?:dealer|merchant)"))
				t.Kind = TaskKind.VisitScrap;
			else if (Regex.IsMatch(all, "versende eine flotte|send a fleet|send out a fleet"))
				t.Kind = TaskKind.SendFleet;
			else if (Regex.IsMatch(all, "starte den bau eines geb|start (?:the )?construction of a building|start building"))
				t.Kind = TaskKind.StartBuilding;
			else if (Regex.IsMatch(all, "forschungsprojekt|forschung starten|starte (?:eine|die) forschung|start (?:a |the )?research"))
				t.Kind = TaskKind.StartResearch;
			else if (Regex.IsMatch(all, "besuche den rohstoffmarkt|visit the resource market|resource market"))
				t.Kind = TaskKind.VisitMarket;
			else if (Regex.IsMatch(all, "versende eine nachricht|send a message"))
				t.Kind = TaskKind.SendMessage;
			else if (Regex.IsMatch(all, "highscore"))
				t.Kind = TaskKind.VisitHighscore;
			else if (Regex.IsMatch(all, "transportiere \\d+|transport \\d+")) {
				t.Kind = TaskKind.Transport;
				var tm = Regex.Match(all, "(?:transportiere|transport)\\s+(\\d+)\\s+(?:einheiten|units(?: of)?)\\s+(metall|metal|kristall|crystal|deuterium)");
				if (tm.Success) {
					int total = int.Parse(tm.Groups[1].Value);
					t.Amount = t.Have >= 0 && t.Need >= 0 ? Math.Max(0, t.Need - t.Have) : total;
					string r = tm.Groups[2].Value;
					t.Resource = r.StartsWith("met") ? "metal" : r.StartsWith("kr") || r == "crystal" ? "crystal" : "deuterium";
				}
			}
			else if (Regex.IsMatch(all, "trümmerfeld|debris field"))
				t.Kind = TaskKind.CollectDebris;
			else if (Regex.IsMatch(all, "spioniere|\\bspy\\b"))
				t.Kind = TaskKind.Spy;
		}
	}
}
