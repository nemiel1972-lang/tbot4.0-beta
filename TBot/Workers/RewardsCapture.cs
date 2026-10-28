using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tbot.Common.IO;
using Microsoft.Extensions.Logging;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal static class RewardsCapture {
		public static string MarkerPath => Path.Combine(AppContext.BaseDirectory, "belohnungen.on");

		private static readonly string[] Kandidaten = { "rewarding" };

		public static async Task Loop(ITBotMain inst, IOgameService ogame, List<Celestial> celestials, CancellationToken ct) {
			try {
				if (!File.Exists(MarkerPath))
					return;
				while (!ct.IsCancellationRequested) {
					try {
						if (await Einmal(inst, ogame, celestials)) {
							AbschaltenNachLetzterBelohnung(inst);
							return;
						}
					} catch (Exception e) {
						inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards capture failed: {e.Message}");
					}
					DateTime bis = DateTime.UtcNow.AddHours(4);
					string stand = EinstellungsStand(inst);
					while (DateTime.UtcNow < bis) {
						await Task.Delay(TimeSpan.FromMinutes(1), ct);
						string neu = EinstellungsStand(inst);
						if (neu != stand) {
							inst.log(LogLevel.Information, LogSender.Tbot, "Rewards: settings changed in the WebUI, running now.");
							break;
						}
					}
				}
			} catch (OperationCanceledException) {
			} catch (Exception) { }
		}

		private static void AbschaltenNachLetzterBelohnung(ITBotMain inst) {
			try {
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				string sf = Path.Combine(dir, $"belohnungen_einstellung_{SafeAlias(inst)}.json");
				var o = new Newtonsoft.Json.Linq.JObject();
				if (File.Exists(sf)) {
					try { o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sf)); } catch { }
				}
				o["mode"] = "off";
				o["tasks"] = "off";
				AtomicFile.WriteAllText(sf, o.ToString(Formatting.Indented));
			} catch (Exception e) {
				inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: settings could not be switched off: {e.Message}");
			}
			inst.log(LogLevel.Information, LogSender.Tbot, "Rewards: the last rank reward has been collected - the Rewards worker is switched off (claiming and tasks set to off).");
			if (TelegramOn(inst)) { try { _ = inst.SendTelegramMessage("Universe rewards: the last rank reward has been collected, the rewards worker is switched off."); } catch { } }
		}

		private static bool TelegramOn(ITBotMain inst) {
			try {
				string sf = Path.Combine(AppContext.BaseDirectory, "data", $"belohnungen_einstellung_{SafeAlias(inst)}.json");
				if (!File.Exists(sf)) return true;
				return !string.Equals((string) Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sf))["telegram"], "off", StringComparison.OrdinalIgnoreCase);
			} catch { return true; }
		}

		private static string EinstellungsStand(ITBotMain inst) {
			try {
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				string safe = SafeAlias(inst);
				string a = Path.Combine(dir, $"belohnungen_einstellung_{safe}.json");
				string b = Path.Combine(dir, $"belohnungen_wahl_{safe}.json");
				return (File.Exists(a) ? File.GetLastWriteTimeUtc(a).Ticks : 0) + "|" + (File.Exists(b) ? File.GetLastWriteTimeUtc(b).Ticks : 0);
			} catch (Exception) {
				return "";
			}
		}

		private static async Task<bool> Einmal(ITBotMain inst, IOgameService ogame, List<Celestial> celestials) {
			bool fertig = false;
			var home = celestials?.FirstOrDefault(c => c is Planet) ?? celestials?.FirstOrDefault();
			if (home == null)
				return false;
			string cp = home.ID.ToString();
			string dir = Path.Combine(AppContext.BaseDirectory, "data", "belohnungen");
			Directory.CreateDirectory(dir);
			string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");

			string overview = await ogame.GetPageHtml(P("page", "ingame"), P("component", "overview"), P("cp", cp));
			var links = new List<string>();
			string comp = null;
			foreach (Match m in Regex.Matches(overview ?? "", "<a[^>]*href=\"([^\"]*component=([a-zA-Z]+)[^\"]*)\"[^>]*>(.*?)</a>", RegexOptions.Singleline)) {
				string text = Regex.Replace(m.Groups[3].Value, "<[^>]+>", " ");
				text = Regex.Replace(text, "\\s+", " ").Trim();
				links.Add($"{m.Groups[2].Value}\t{text}");
			}
			File.WriteAllLines(Path.Combine(dir, "menue_links.txt"), links.Distinct());

			var versuche = new List<string>();
			if (comp != null) versuche.Add(comp);
			versuche.AddRange(Kandidaten.Where(k => k != comp));
			foreach (var c in versuche) {
				string html = await ogame.GetPageHtml(P("page", "ingame"), P("component", c), P("cp", cp));
				await Task.Delay(700);
				if (string.IsNullOrWhiteSpace(html) || !Regex.IsMatch(html, "Tritium", RegexOptions.IgnoreCase))
					continue;
				string pool = Path.Combine(AppContext.BaseDirectory, "data", "belohnungen_pool.txt");
				File.WriteAllText(Path.Combine(dir, $"belohnungen_{stamp}.html"), Scrub(html));
				Sammeln(html, pool);
				inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards capture: page '{c}' saved ({stamp}).");
				var tm = Regex.Match(html, "token[\"']?\\s*[:=]\\s*[\"']([0-9a-fA-F]{32})");
				var tp = new List<KeyValuePair<string, string>> { P("page", "ingame"), P("component", c), P("tab", "tasks"), P("action", "fetchTasks"), P("ajax", "1"), P("cp", cp) };
				if (tm.Success) tp.Add(P("token", tm.Groups[1].Value));
				await Task.Delay(700);
				string tasks = await ogame.GetPageHtml(tp.ToArray());
				if (!string.IsNullOrWhiteSpace(tasks)) {
					File.WriteAllText(Path.Combine(dir, $"belohnungen_aufgaben_{stamp}" + (tasks.TrimStart().StartsWith("{") ? ".json" : ".html")), Scrub(tasks));
					Sammeln(tasks, pool);
					var tiers = await RangBelohnungen(inst, ogame, c, cp, tm.Success ? tm.Groups[1].Value : null, dir, stamp, pool);
					await Abholen(inst, ogame, c, tiers);
					fertig = RewardingLogic.AllClaimed(tiers);
					var ct = Regex.Match(html, "currentTier\\s*=\\s*(\\d+)");
					SchreibeStand(inst, home, tasks, tiers, ct.Success ? int.Parse(ct.Groups[1].Value) : -1);
					await Erledigen(inst, ogame, home, tasks);
					inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards capture: tasks saved ({stamp}, token {(tm.Success ? "found" : "missing")}).");
				} else {
					inst.log(LogLevel.Warning, LogSender.Tbot, "Rewards capture: empty answer for the tasks tab.");
				}
				return fertig;
			}
			inst.log(LogLevel.Warning, LogSender.Tbot, "Rewards capture: rewards page not found, see data/belohnungen/menue_links.txt.");
			return false;
		}

		private static async Task<List<RewardingLogic.Tier>> RangBelohnungen(ITBotMain inst, IOgameService ogame, string comp, string cp, string token, string dir, string stamp, string pool) {
			var tiers = new List<RewardingLogic.Tier>();
			try {
				for (int n = 1; n <= 7; n++) {
					var rp = new List<KeyValuePair<string, string>> { P("page", "ingame"), P("component", comp), P("tab", "rewards"), P("action", "fetchRewards"), P("ajax", "1"), P("cp", cp), P("tier", n.ToString()) };
					if (token != null) rp.Add(P("token", token));
					await Task.Delay(700);
					string ans = await ogame.GetPageHtml(rp.ToArray());
					if (string.IsNullOrWhiteSpace(ans))
						continue;
					string ext = ans.TrimStart().StartsWith("{") ? ".json" : ".html";
					File.WriteAllText(Path.Combine(dir, $"belohnungen_raenge_{stamp}_{n}{ext}"), Scrub(ans));
					Sammeln(ans, pool);
					var t = RewardingLogic.ParseTier(n, ans);
					if (t.Rewards.Count > 0)
						tiers.Add(t);
				}
				inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards capture: rank rewards read ({tiers.Count} ranks, {stamp}).");
			} catch (Exception e) {
				inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards capture: rank rewards failed: {e.Message}");
			}
			return tiers;
		}

		private static string SafeAlias(ITBotMain inst) =>
			string.Concat((inst.InstanceAlias ?? "MAIN").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

		private static async Task Abholen(ITBotMain inst, IOgameService ogame, string comp, List<RewardingLogic.Tier> tiers) {
			try {
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				string safe = SafeAlias(inst);
				string mode = "off";
				string sf = Path.Combine(dir, $"belohnungen_einstellung_{safe}.json");
				if (File.Exists(sf)) {
					try { mode = (string) Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sf))["mode"] ?? "off"; } catch { }
				}
				if (mode != "dry" && mode != "on")
					return;
				var choices = new Dictionary<int, (int id, string name)>();
				string cf = Path.Combine(dir, $"belohnungen_wahl_{safe}.json");
				if (File.Exists(cf)) {
					try {
						foreach (var p in Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(cf)).Properties())
							if (int.TryParse(p.Name, out int tn))
								choices[tn] = ((int) p.Value["id"], (string) p.Value["name"] ?? "");
					} catch { }
				}
				var ohneWahl = new List<int>();
				var plan = RewardingLogic.PlanClaims(tiers, choices, ohneWahl);
				if (ohneWahl.Count > 0)
					inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: rank(s) {string.Join(", ", ohneWahl)} reached but no valid choice saved on the Rewards page, nothing claimed.");
				foreach (var cl in plan) {
					if (mode == "dry") {
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would claim rank {cl.Tier}: {cl.Name} (id {cl.RewardId}).");
						continue;
					}
					await Task.Delay(1500);
					string answer = await ogame.PostPageForm("page=ingame&component=" + comp + "&tab=rewards&action=submitReward&asJson=1",
						P("selectedReward", cl.RewardId.ToString()), P("selectedTier", cl.Tier.ToString()), P("token", "auto"));
					bool ok = RewardingLogic.ClaimSucceeded(answer, out string msg);
					inst.log(ok ? LogLevel.Information : LogLevel.Warning, LogSender.Tbot, $"Rewards: claim rank {cl.Tier} '{cl.Name}' {(ok ? "done" : "FAILED")}: {msg}");
					if (!ok)
						break;
					var t = tiers.FirstOrDefault(x => x.Number == cl.Tier);
					if (t != null) t.Claimed = true;
					if (TelegramOn(inst)) { try { await inst.SendTelegramMessage($"Universe rewards: rank {cl.Tier} claimed: {cl.Name}"); } catch { } }
				}
			} catch (Exception e) {
				inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: claim failed: {e.Message}");
			}
		}

		private static async Task Erledigen(ITBotMain inst, IOgameService ogame, Celestial home, string tasksAnswer) {
			try {
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				string safe = SafeAlias(inst);
				string mode = "off";
				string sf = Path.Combine(dir, $"belohnungen_einstellung_{safe}.json");
				if (File.Exists(sf)) {
					try { mode = (string) Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(sf))["tasks"] ?? "off"; } catch { }
				}
				if (mode != "dry" && mode != "on")
					return;
				bool dry = mode == "dry";
				var open = RewardingLogic.ParseTasks(tasksAnswer).Where(t => !t.Done).ToList();
				string cp = home.ID.ToString();
				string gf = Path.Combine(dir, $"belohnungen_gebaut_{safe}.json");
				var gebaut = new Newtonsoft.Json.Linq.JObject();
				if (File.Exists(gf)) {
					try { gebaut = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(gf)); } catch { }
				}
				Resources res = null;
				foreach (var t in open) {
					if (t.Kind == RewardingLogic.TaskKind.VisitScrap) {
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would visit the scrap dealer for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						await ogame.GetPageHtml(P("page", "ingame"), P("component", "trader"), P("action", "scrap"), P("cp", cp));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: scrap dealer visited for '{t.Title}'.");
					} else if (t.Kind == RewardingLogic.TaskKind.VisitOfficers) {
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would visit the officers' mess for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						await ogame.GetPageHtml(P("page", "ingame"), P("component", "officersmess"), P("cp", cp));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: officers' mess visited for '{t.Title}'.");
					} else if (t.Kind == RewardingLogic.TaskKind.SendMessage) {
						string mkey = "msg|" + t.Title;
						if (gebaut.TryGetValue(mkey, out var mlast) && DateTime.TryParse((string) mlast, null, System.Globalization.DateTimeStyles.RoundtripKind, out var mt) && DateTime.UtcNow - mt.ToUniversalTime() < TimeSpan.FromHours(24)) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}' message already sent within 24 h, skipped.");
							continue;
						}
						var ziel = await FindeInaktiven(inst, ogame, home);
						if (ziel == null) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no inactive player found near the home planet, no message sent.");
							continue;
						}
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would send a short greeting to an inactive player ({ziel.Value.name}, system {ziel.Value.system}) for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						await ogame.SendMessage(ziel.Value.id, "Hello! Greetings from a neighbouring commander. Fly safe.");
						gebaut[mkey] = DateTime.UtcNow.ToString("o");
						AtomicFile.WriteAllText(gf, gebaut.ToString(Formatting.Indented));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: greeting sent to an inactive player for '{t.Title}'.");
					} else if (t.Kind == RewardingLogic.TaskKind.Transport && t.Amount > 0 && t.Resource != "") {
						string tkey = "transport|" + t.Title + "|" + t.Resource + "|" + t.Amount;
						if (gebaut.TryGetValue(tkey, out var tlast) && DateTime.TryParse((string) tlast, null, System.Globalization.DateTimeStyles.RoundtripKind, out var tt) && DateTime.UtcNow - tt.ToUniversalTime() < TimeSpan.FromHours(3)) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}' transport sent within 3 h, skipped.");
							continue;
						}
						var moon = inst.UserData?.celestials?.FirstOrDefault(c => c.Coordinate.Type == TBot.Ogame.Infrastructure.Enums.Celestials.Moon
							&& c.Coordinate.Galaxy == home.Coordinate.Galaxy && c.Coordinate.System == home.Coordinate.System && c.Coordinate.Position == home.Coordinate.Position);
						var calc = (inst as TBotMain)?.HelperService;
						if (moon == null || calc == null) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no home moon found, no transport sent.");
							continue;
						}
						var mres = await ogame.GetResources(moon);
						long vorrat = t.Resource == "metal" ? mres.Metal : t.Resource == "crystal" ? mres.Crystal : (long) (mres.Deuterium * 0.9);
						long menge = Math.Min(vorrat, t.Amount);
						if (menge <= 0) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no {t.Resource} on the home moon, no transport sent.");
							continue;
						}
						var mships = await ogame.GetShips(moon);
						var cargo = mships.GetAmount(TBot.Ogame.Infrastructure.Enums.Buildables.LargeCargo) > 0 ? TBot.Ogame.Infrastructure.Enums.Buildables.LargeCargo : TBot.Ogame.Infrastructure.Enums.Buildables.SmallCargo;
						long have = mships.GetAmount(cargo);
						if (have <= 0) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no cargo ships on the home moon, no transport sent.");
							continue;
						}
						var payload = new Resources { Metal = t.Resource == "metal" ? menge : 0, Crystal = t.Resource == "crystal" ? menge : 0, Deuterium = t.Resource == "deuterium" ? menge : 0 };
						int hyper = inst.UserData.researches?.HyperspaceTechnology ?? 0;
						float bonus = moon.LFBonuses?.GetShipCargoBonus(cargo) ?? 0;
						long need = calc.CalcShipNumberForPayload(payload, cargo, hyper, inst.UserData.serverData, bonus, inst.UserData.userInfo.Class, inst.UserData.serverData.ProbeCargo);
						long n = Math.Min(need, have);
						var fleet = new Ships().Add(cargo, n);
						long cap = calc.CalcFleetCapacity(fleet, inst.UserData.serverData, hyper, moon.LFBonuses, inst.UserData.userInfo.Class, inst.UserData.serverData.ProbeCargo);
						if (cap < menge) {
							menge = cap;
							payload = new Resources { Metal = t.Resource == "metal" ? menge : 0, Crystal = t.Resource == "crystal" ? menge : 0, Deuterium = t.Resource == "deuterium" ? menge : 0 };
						}
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would send {menge:N0} {t.Resource} with {n}x {cargo} from {moon.Coordinate} to {home.Coordinate} for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						int fid = await inst.FleetScheduler.SendFleet(moon, fleet, home.Coordinate, TBot.Ogame.Infrastructure.Enums.Missions.Transport, TBot.Ogame.Infrastructure.Enums.Speeds.HundredPercent, payload, inst.UserData.userInfo.Class, true);
						if (fid > (int) TBot.Ogame.Infrastructure.Enums.SendFleetCode.GenericError) {
							gebaut[tkey] = DateTime.UtcNow.ToString("o");
							AtomicFile.WriteAllText(gf, gebaut.ToString(Formatting.Indented));
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: sent {menge:N0} {t.Resource} from the home moon to the home planet for '{t.Title}'.");
						} else {
							inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: transport for '{t.Title}' could not be sent (code {fid}).");
						}
					} else if (t.Kind == RewardingLogic.TaskKind.CollectDebris) {
						string ckey = "debris|" + t.Title;
						if (gebaut.TryGetValue(ckey, out var clast) && DateTime.TryParse((string) clast, null, System.Globalization.DateTimeStyles.RoundtripKind, out var ct0) && DateTime.UtcNow - ct0.ToUniversalTime() < TimeSpan.FromHours(6)) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}' harvest sent within 6 h, skipped.");
							continue;
						}
						var eigene = inst.UserData?.celestials?.ToList() ?? new List<Celestial>();
						Coordinate bestDest = null;
						long bestRes = 0, bestNeeded = 0;
						foreach (var sys in eigene.Where(c => c is Planet).Select(c => (c.Coordinate.Galaxy, c.Coordinate.System)).Distinct()) {
							await Task.Delay(700);
							var gi = await ogame.GetGalaxyInfo(sys.Galaxy, sys.System);
							foreach (var p in gi?.Planets ?? new List<Planet>()) {
								if (p?.Coordinate == null || p.Debris == null)
									continue;
								long tot = p.Debris.Resources.TotalResources;
								if (tot > bestRes) {
									bestRes = tot;
									bestNeeded = p.Debris.RecyclersNeeded;
									bestDest = new Coordinate(p.Coordinate.Galaxy, p.Coordinate.System, p.Coordinate.Position, TBot.Ogame.Infrastructure.Enums.Celestials.Debris);
								}
							}
						}
						if (bestDest == null) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no debris field in the systems of your planets right now.");
							continue;
						}
						Celestial quelle = null;
						long quelleAnzahl = 0;
						double quelleDist = double.MaxValue;
						foreach (var c in eigene) {
							var sh = await ogame.GetShips(c);
							long rec = sh?.GetAmount(TBot.Ogame.Infrastructure.Enums.Buildables.Recycler) ?? 0;
							if (rec <= 0)
								continue;
							double dist = Math.Abs(c.Coordinate.Galaxy - bestDest.Galaxy) * 100000.0 + Math.Abs(c.Coordinate.System - bestDest.System) * 100.0 + Math.Abs(c.Coordinate.Position - bestDest.Position);
							if (dist < quelleDist) {
								quelleDist = dist; quelle = c; quelleAnzahl = rec;
							}
						}
						if (quelle == null) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}': no recyclers on any of your celestials.");
							continue;
						}
						long anzahl = Math.Max(1, Math.Min(quelleAnzahl, bestNeeded > 0 ? bestNeeded : quelleAnzahl));
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would harvest the debris field at {bestDest} ({bestRes:N0} resources) with {anzahl}x Recycler from {quelle.Coordinate} for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						int hid = await inst.FleetScheduler.SendFleet(quelle, new Ships().Add(TBot.Ogame.Infrastructure.Enums.Buildables.Recycler, anzahl), bestDest, TBot.Ogame.Infrastructure.Enums.Missions.Harvest, TBot.Ogame.Infrastructure.Enums.Speeds.HundredPercent, null, inst.UserData.userInfo.Class, true);
						if (hid > (int) TBot.Ogame.Infrastructure.Enums.SendFleetCode.GenericError) {
							gebaut[ckey] = DateTime.UtcNow.ToString("o");
							AtomicFile.WriteAllText(gf, gebaut.ToString(Formatting.Indented));
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: {anzahl}x Recycler from {quelle.Coordinate} sent to the debris field at {bestDest} for '{t.Title}'.");
						} else {
							inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: harvest for '{t.Title}' could not be sent (code {hid}).");
						}
					} else if (t.Kind == RewardingLogic.TaskKind.VisitTechtree) {
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would open a tech tree for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						await ogame.GetPageHtml(P("page", "ajax"), P("component", "technologytree"), P("ajax", "1"), P("technologyId", "1"), P("tab", "technologytree"), P("cp", cp));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: tech tree opened for '{t.Title}'.");
					} else if (t.Kind == RewardingLogic.TaskKind.VisitShop || t.Kind == RewardingLogic.TaskKind.VisitHighscore) {
						bool shop = t.Kind == RewardingLogic.TaskKind.VisitShop;
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would open the {(shop ? "shop" : "highscore")} page for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						if (shop)
							await ogame.GetPageHtml(P("page", "shop"), P("cp", cp));
						else
							await ogame.GetPageHtml(P("page", "ingame"), P("component", "highscore"), P("site", "1"), P("category", "1"), P("cp", cp));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: {(shop ? "shop" : "highscore")} page opened for '{t.Title}'.");
					} else if ((t.Kind == RewardingLogic.TaskKind.ShipBuild || t.Kind == RewardingLogic.TaskKind.Defence) && t.Amount > 0) {
						var unit = t.Kind == RewardingLogic.TaskKind.ShipBuild ? t.Ship : t.Defence;
						string key = t.Title + "|" + unit + "|" + t.Amount;
						if (gebaut.TryGetValue(key, out var last) && DateTime.TryParse((string) last, null, System.Globalization.DateTimeStyles.RoundtripKind, out var lt) && DateTime.UtcNow - lt.ToUniversalTime() < TimeSpan.FromHours(24)) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}' already queued within 24 h, skipped.");
							continue;
						}
						res ??= await ogame.GetResources(home);
						var one = await ogame.GetPrice(unit, 1);
						long n = t.Amount;
						if (one.Metal > 0) n = Math.Min(n, res.Metal / one.Metal);
						if (one.Crystal > 0) n = Math.Min(n, res.Crystal / one.Crystal);
						if (one.Deuterium > 0) n = Math.Min(n, res.Deuterium / one.Deuterium);
						if (n < t.Amount) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: '{t.Title}' needs {t.Amount}x {unit}, resources on the home planet only for {Math.Max(0, n)}, nothing built.");
							continue;
						}
						if (dry) {
							inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards (dry run): would build {t.Amount}x {unit} on {home.Coordinate} for '{t.Title}'.");
							continue;
						}
						await Task.Delay(1500);
						if (t.Kind == RewardingLogic.TaskKind.ShipBuild)
							await ogame.BuildShips(home, unit, t.Amount);
						else
							await ogame.BuildDefences(home, unit, t.Amount);
						res = null;
						gebaut[key] = DateTime.UtcNow.ToString("o");
						AtomicFile.WriteAllText(gf, gebaut.ToString(Formatting.Indented));
						inst.log(LogLevel.Information, LogSender.Tbot, $"Rewards: queued {t.Amount}x {unit} on {home.Coordinate} for '{t.Title}'.");
					}
				}
			} catch (Exception e) {
				inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: doing tasks failed: {e.Message}");
			}
		}

		private static async Task<(int id, string name, int system)?> FindeInaktiven(ITBotMain inst, IOgameService ogame, Celestial home) {
			try {
				int eigene = inst.UserData?.userInfo?.PlayerID ?? 0;
				int g = home.Coordinate.Galaxy, s0 = home.Coordinate.System;
				for (int d = 0; d <= 10; d++) {
					foreach (int s in d == 0 ? new[] { s0 } : new[] { s0 - d, s0 + d }) {
						if (s < 1) continue;
						var info = await ogame.GetGalaxyInfo(g, s);
						var p = info?.Planets?.FirstOrDefault(x => x != null && x.Inactive && x.Player != null && x.Player.ID > 0 && x.Player.ID != eigene);
						if (p != null)
							return (p.Player.ID, p.Player.Name ?? "", s);
						await Task.Delay(500);
					}
				}
			} catch (Exception e) {
				inst.log(LogLevel.Warning, LogSender.Tbot, $"Rewards: searching an inactive player failed: {e.Message}");
			}
			return null;
		}

		private static void SchreibeStand(ITBotMain inst, Celestial home, string tasks, List<RewardingLogic.Tier> tiers, int currentTier) {
			try {
				var list = RewardingLogic.ParseTasks(tasks);
				var file = new {
					Updated = DateTime.UtcNow,
					Home = home.Coordinate.ToString(),
					CurrentTier = currentTier,
					Tiers = tiers.Select(r => new {
						r.Number, r.Have, r.Need, r.Claimed,
						Rewards = r.Rewards.Select(x => new { x.Id, x.Name, x.Quantity, x.Additional }).ToList()
					}).ToList(),
					Tasks = list.Select(t => new {
						t.Title, t.Text, t.Tritium, t.Have, t.Need, t.Done,
						Kind = t.Kind.ToString(),
						Defence = t.Kind == RewardingLogic.TaskKind.Defence ? t.Defence.ToString() : null,
						Ship = t.Kind == RewardingLogic.TaskKind.ShipBuild ? t.Ship.ToString() : null,
						t.Amount, t.Why
					}).ToList()
				};
				string safe = string.Concat((inst.InstanceAlias ?? "MAIN").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				AtomicFile.WriteAllText(Path.Combine(dir, $"belohnungen_{safe}.json"), JsonConvert.SerializeObject(file, Formatting.Indented));
			} catch (Exception) {
			}
		}

		private static void Sammeln(string html, string pool) {
			string t = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			t = Regex.Replace(t, "<[^>]+>", "\n");
			t = System.Net.WebUtility.HtmlDecode(t);
			var neu = t.Split('\n').Select(z => Regex.Replace(z, "\\s+", " ").Trim()).Where(z => z.Length >= 8).Distinct().ToList();
			var bekannt = File.Exists(pool) ? new HashSet<string>(File.ReadAllLines(pool)) : new HashSet<string>();
			var dazu = neu.Where(z => !bekannt.Contains(z)).ToList();
			if (dazu.Count > 0)
				File.AppendAllLines(pool, dazu);
		}

		private static KeyValuePair<string, string> P(string k, string v) => new(k, v);

		private static string Scrub(string s) {
			s = Regex.Replace(s, "(name=\"ogame-session\" content=\")[^\"]*", "$1[removed]");
			s = Regex.Replace(s, "(token[\"']?\\s*[:=]\\s*[\"']?)[0-9A-Fa-f]{16,}", "$1[removed]", RegexOptions.IgnoreCase);
			s = Regex.Replace(s, "(value=\")[0-9a-f]{32}(\")", "$1[removed]$2");
			return s;
		}
	}
}
