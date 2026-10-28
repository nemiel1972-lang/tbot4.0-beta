using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Helpers;
using Tbot.Services;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal static class DefenderTest {
		internal sealed class Line {
			public string L { get; set; } = "info";
			public string T { get; set; } = "";
		}

		public static string RequestPath(string alias) => Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"defendertest_request_{alias}.json"));
		public static string ResultPath(string alias) => Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"defendertest_result_{alias}.json"));

		private static Coordinate Parse(string text) {
			var t = (text ?? "").Replace("[", "").Replace("]", "").Replace(" ", "").Replace(".", ":").Replace(",", ":").Replace(";", ":").Trim();
			var parts = t.Split(':', StringSplitOptions.RemoveEmptyEntries).ToList();
			var type = Celestials.Planet;
			if (parts.Count == 4 && !char.IsDigit(parts[0][0])) {
				type = TypeOf(parts[0], type);
				parts.RemoveAt(0);
			} else if (parts.Count == 4) {
				type = TypeOf(parts[3], type);
				parts.RemoveAt(3);
			}
			if (parts.Count != 3 || !int.TryParse(parts[0], out int g) || !int.TryParse(parts[1], out int s) || !int.TryParse(parts[2], out int p))
				return null;
			return new Coordinate(g, s, p, type);
		}

		private static Celestials TypeOf(string v, Celestials dflt) {
			v = v.ToLowerInvariant();
			if (v == "m" || v == "moon" || v == "3")
				return Celestials.Moon;
			if (v == "p" || v == "planet" || v == "1")
				return Celestials.Planet;
			return dflt;
		}

		private static bool Flag(dynamic obj, string key, bool dflt) {
			try {
				if (SettingsService.IsSettingSet(obj, key))
					return (bool) obj[key];
			} catch (Exception) {
			}
			return dflt;
		}

		private static long Num(dynamic obj, string key, long dflt) {
			try {
				if (SettingsService.IsSettingSet(obj, key))
					return (long) obj[key];
			} catch (Exception) {
			}
			return dflt;
		}

		public static async System.Threading.Tasks.Task<List<Line>> Run(ITBotMain inst, JObject req) {
			var o = new List<Line>();
			void Add(string level, string text) => o.Add(new Line { L = level, T = text });
			try {
				var origin = Parse(req.Value<string>("origin"));
				var target = Parse(req.Value<string>("target"));
				int minutes = Math.Max(1, req.Value<int?>("arriveInMinutes") ?? 30);
				long attackerId = req.Value<long?>("attackerId") ?? 0;
				long attackerPoints = req.Value<long?>("attackerPoints") ?? 0;
				bool onlyProbes = req.Value<bool?>("onlyProbes") ?? false;
				bool missile = string.Equals(req.Value<string>("mission"), "Missile", StringComparison.OrdinalIgnoreCase);

				if (origin == null || target == null) {
					Add("bad", "Origin or target could not be read. Use 1:2:3, 1:2:3:M or [P:1:2:3] / [M:1:2:3] (P = planet, M = moon).");
					return o;
				}
				var s = inst.InstanceSettings;
				var def = s.Defender;
				Add("info", $"Test: {(missile ? "missile attack" : "attack")} from {origin} on {target}, arrives in {minutes} min" + (attackerId != 0 ? $", attacker ID {attackerId}" : "") + ". Nothing is sent, built or changed.");

				if (!Flag(def, "Active", false))
					Add("bad", "Defender.Active is off: no attack would be noticed at all.");
				bool defenderRunning = inst.IsFeatureRunning(Feature.Defender);
				if (inst.UserData.isSleeping)
					Add("bad", "The bot is asleep (SleepMode): the workers have ended, including the Defender. The attack would not be noticed until wake-up.");
				else if (!defenderRunning)
					Add("warn", "The Defender worker is not running at the moment.");
				else
					Add("ok", "Defender worker is running.");
				if (inst.IsBotPaused)
					Add("warn", "The bot is paused. The Defender keeps running during a pause unless Experimental.BotPause.StopDefender is on.");
				long cMin = Num(def, "CheckIntervalMin", 0), cMax = Num(def, "CheckIntervalMax", 0);
				Add("info", $"Check interval: {cMin} to {cMax} min. The attack is noticed within {cMax} min at the latest.");
				if (cMax * 60 >= minutes * 60 - 120)
					Add("warn", $"The attack arrives in {minutes} min, the check interval can be as long as {cMax} min: the safety window could pass before the next check.");

				var celestial = inst.UserData.celestials.Unique().FirstOrDefault(c => c.HasCoords(target));
				if (celestial == null) {
					celestial = inst.UserData.celestials.Unique().OrderByDescending(c => c.Ships?.GetFleetPoints() ?? 0).FirstOrDefault();
					if (celestial == null) {
						Add("bad", "The bot has no celestial data yet (try again in a minute).");
						return o;
					}
					Add("info", $"{target} is not one of your celestials. For the test it counts as yours; the data of {celestial} (largest fleet) is used.");
					target = celestial.Coordinate;
				}
				try {
					var fake = new AttackerFleet {
						ID = -1, MissionType = Missions.Attack, Origin = origin, Destination = target, ArriveIn = minutes * 60,
						ArrivalTime = DateTime.Now.AddMinutes(minutes), AttackerID = (int) attackerId, AttackerName = "(test)", Ships = new Ships()
					};
					inst.PublishAttackSnapshot(new List<AttackerFleet> { fake }, DateTime.UtcNow);
					Add("ok", "Start page tile: the test attack is shown for 60 seconds (red note \"incoming attack\").");
					_ = System.Threading.Tasks.Task.Run(async () => {
						await System.Threading.Tasks.Task.Delay(60000);
						try { inst.PublishAttackSnapshot(new List<AttackerFleet>(), DateTime.UtcNow); } catch (Exception) { }
					});
				} catch (Exception e) {
					Add("warn", $"Start page tile could not be triggered: {e.Message}");
				}
				if (Flag(def.Alarm, "Active", false)) {
					Add("ok", "Alarm sound plays now (on the machine the bot runs on).");
					string sound = "";
					try { if (SettingsService.IsSettingSet(def.Alarm, "SoundFile")) sound = (string) def.Alarm.SoundFile; } catch (Exception) { }
					_ = System.Threading.Tasks.Task.Run(() => { try { ConsoleHelpers.PlayAlarm(sound); } catch (Exception) { } });
				} else
					Add("info", "Alarm sound is off (Defender.Alarm.Active) - no sound in the test.");
				long ownPoints = celestial.Ships?.GetFleetPoints() ?? 0;
				long ownResources = celestial.Resources?.TotalResources ?? 0;
				Add("ok", $"Target found: {celestial}. Cached data: fleet {ownPoints:N0} points, resources {ownResources:N0} (may be a few minutes old).");
				try {
					if (ShadowFleetWorker.DefenderShouldIgnore(s, celestial.Coordinate))
						Add("warn", "ShadowFleet.DefenderOff is on for this celestial: the shadow fleet is not saved, resources and the other ships are.");
				} catch (Exception) {
				}

				bool skipped = false;
				if (missile) {
					Add("info", "Missile attack: the Defender builds anti-ballistic missiles if a missile silo of level 2 or more exists (Defender.DefendFromMissiles), no fleetsave.");
					Add(Flag(def, "DefendFromMissiles", true) ? "ok" : "warn", Flag(def, "DefendFromMissiles", true) ? "DefendFromMissiles is on." : "DefendFromMissiles is off: nothing would be built.");
					return o;
				}
				try {
					var wl = def.WhiteList;
					if (attackerId != 0 && wl != null) {
						IEnumerable<long> ids = wl switch {
							long[] a => a,
							int[] a => a.Select(x => (long) x),
							IEnumerable<long> e => e,
							IEnumerable<int> e => e.Select(x => (long) x),
							_ => Enumerable.Empty<long>()
						};
						if (ids.Contains(attackerId)) {
							Add("warn", $"Attacker {attackerId} is on the whitelist: the attack is skipped, no alarm, no fleetsave.");
							skipped = true;
						} else
							Add("ok", "Attacker is not on the whitelist.");
					}
				} catch (Exception) {
					Add("warn", "Whitelist could not be read.");
				}
				if (!skipped) {
					if (onlyProbes && Flag(def, "IgnoreProbes", false)) {
						Add("warn", "Only espionage probes and IgnoreProbes is on: skipped.");
						skipped = true;
					} else if (onlyProbes)
						Add("info", "Only probes, but IgnoreProbes is off: treated like an attack.");
				}
				if (!skipped && attackerPoints > 0) {
					long ratio = Math.Max(1, Num(def, "WeakAttackRatio", 1));
					if (Flag(def, "IgnoreWeakAttack", false) && attackerPoints < ownPoints / ratio) {
						Add("warn", $"Weak attack ({attackerPoints:N0} < {ownPoints:N0} / {ratio}): skipped.");
						skipped = true;
					} else
						Add("ok", $"Not a weak attack (attacker {attackerPoints:N0} points, own fleet {ownPoints:N0}, ratio {ratio}, IgnoreWeakAttack {(Flag(def, "IgnoreWeakAttack", false) ? "on" : "off")}).");
				} else if (!skipped)
					Add("info", "Weak-attack check not tested (no attacker fleet points given).");
				if (!skipped) {
					var ig = def.IgnoreAttackIfIHave;
					if (Flag(ig, "Active", false)) {
						long minRes = Num(ig, "MinResourcesToSave", 0), minFleet = Num(ig, "MinFleetToSave", 0);
						if (ownResources < minRes && ownPoints * 1000 < minFleet) {
							Add("warn", $"Not worth it (resources {ownResources:N0} < {minRes:N0} and fleet {ownPoints * 1000:N0} < {minFleet:N0}): skipped.");
							skipped = true;
						} else
							Add("ok", $"Worth saving (limits: resources {minRes:N0}, fleet {minFleet:N0}).");
					} else
						Add("info", "IgnoreAttackIfIHave is off: every attack counts.");
				}
				if (skipped) {
					Add("info", "Result: the Defender would do nothing for this attack.");
					return o;
				}

				Add(Flag(def.TelegramMessenger, "Active", false) ? "ok" : "info", Flag(def.TelegramMessenger, "Active", false) ? "Telegram message would be sent (attacker, target, arrival, composition)." : "Telegram messages for attacks are off.");
				bool discord = false;
				try { discord = Flag(def.DiscordWebhook, "Active", false); } catch (Exception) { }
				Add(discord ? "ok" : "info", discord ? "Discord alert would be sent." : "Discord alert is off.");
				if (Flag(def.SpyAttacker, "Active", false)) {
					long probes = Num(def.SpyAttacker, "Probes", 1);
					long have = celestial.Ships?.EspionageProbe ?? 0;
					Add(have > 0 ? "ok" : "warn", have > 0 ? $"Attacker would be spied on with {probes} probes from {celestial} ({have} there)." : "SpyAttacker is on, but no probes on the target celestial: no spying.");
				} else
					Add("info", "SpyAttacker is off.");
				Add(Flag(def.MessageAttacker, "Active", false) ? "ok" : "info", Flag(def.MessageAttacker, "Active", false) ? (attackerId != 0 ? "A message would be sent to the attacker." : "MessageAttacker is on, but no attacker ID given: no message.") : "MessageAttacker is off.");

				if (!Flag(def.Autofleet, "Active", false)) {
					Add("bad", "Defender.Autofleet is off: NO fleetsave. The ships and resources stay on the celestial.");
					return o;
				}
				long margin = Num(def.Autofleet, "SafetyMarginSeconds", 120);
				if (margin <= 0) margin = 120;
				var now = DateTime.UtcNow;
				var impact = now.AddMinutes(minutes);
				var dispatch = impact.AddSeconds(-margin);
				Add("ok", $"Fleetsave is planned. Impact {impact:HH:mm:ss} UTC, safety margin {margin} s, dispatch at {dispatch:HH:mm:ss} UTC (in {Math.Max(0, (dispatch - now).TotalMinutes):0.#} min).");
				if (dispatch <= now)
					Add("warn", "The safety window has already passed: the fleetsave starts immediately at the next check.");
				long minFlight = 2 * minutes * 60 + 120;
				Add("info", $"Minimum flight time for the fleetsave target: {minFlight / 60.0:0.#} min (twice the time left plus 2 min, so the recall comes after the last wave). The target and the mission are chosen when the fleetsave is sent.");
				Add("info", "An exact timer is armed for the dispatch time, the check cycle is only the safety net.");
				Add("warn", "While the fleetsave is open, the attack lockdown is active: all other workers stop until the fleetsave is done.");
				try {
					if (Flag(s.Experimental.DefenderRecheck, "Active", false))
						Add("info", "DefenderRecheck is on: shortly before the dispatch the attack list is fetched once more, a recalled attack cancels the fleetsave.");
				} catch (Exception) {
				}
				Add("info", "Fleetsave dry run (reads only, nothing is sent):");
				foreach (var l in await inst.FleetScheduler.DryRunFleetSave(celestial, minFlight)) {
					int k = l.IndexOf('|');
					Add(k > 0 ? l.Substring(0, k) : "info", k > 0 ? l.Substring(k + 1) : l);
				}
				Add("info", "Result: this attack would lead to an automatic fleetsave.");
			} catch (Exception e) {
				Add("bad", $"The test failed: {e.Message}");
			}
			return o;
		}
	}
}
