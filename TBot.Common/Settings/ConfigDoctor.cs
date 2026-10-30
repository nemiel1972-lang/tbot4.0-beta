using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Tbot.Common.Settings {
	public sealed class ConfigFinding {
		public string Severity = "warn";
		public string Path = "";
		public string Message = "";
		public string Key = "";
		public override string ToString() => $"[{Severity}] {Path}: {Message}";
	}

	public static class ConfigDoctor {
		private static readonly string[] BrainWorkers = { "AutoMine", "LifeformAutoMine", "LifeformAutoResearch", "AutoResearch", "AutoCargo", "AutoDefence", "AutoRepatriate", "BuyOfferOfTheDay", "KeepBoosters", "AutoFleetJumpGate" };
		private static readonly string[] TransportUsers = { "AutoMine", "LifeformAutoMine", "LifeformAutoResearch", "AutoResearch" };
		private static readonly string[] RankModes = { "Payback", "Cheapest", "Npv" };
		private static readonly string[] SmartMixShips = { "SmallCargo", "LargeCargo", "Null" };
		private const int HarvestRangeWarn = 15;

		public static string CoordKey(int galaxy, int system, int position, string type) => $"{galaxy}:{system}:{position}:{type}";

		public static List<ConfigFinding> Check(JObject config, ICollection<string> owned = null) {
			var f = new List<ConfigFinding>();
			if (config == null) {
				Add(f, "error", "", "The configuration is empty or could not be read.");
				return f;
			}
			try { CheckCoordinates(config, f, owned); } catch (Exception e) { Add(f, "info", "", "Coordinate check skipped: " + e.Message); }
			try { CheckBrain(config, f); } catch (Exception e) { Add(f, "info", "Brain", "Check skipped: " + e.Message); }
			try { CheckGeneral(config, f); } catch (Exception e) { Add(f, "info", "General", "Check skipped: " + e.Message); }
			try { CheckSleep(config, f); } catch (Exception e) { Add(f, "info", "SleepMode", "Check skipped: " + e.Message); }
			try { CheckExpeditions(config, f); } catch (Exception e) { Add(f, "info", "Expeditions", "Check skipped: " + e.Message); }
			try { CheckHarvest(config, f); } catch (Exception e) { Add(f, "info", "AutoHarvest", "Check skipped: " + e.Message); }
			try { CheckMinePlan(config, f); } catch (Exception e) { Add(f, "info", "Experimental", "Check skipped: " + e.Message); }
			return f;
		}

		public static List<ConfigFinding> CheckShared(IEnumerable<(string Alias, string Path)> instances) {
			var f = new List<ConfigFinding>();
			var uses = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var (alias, path) in instances) {
				try {
					using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None };
					var root = JObject.Load(reader);
					foreach (var (blockPath, refs) in FindLoads(root, "")) {
						foreach (string r in refs) {
							if (r.StartsWith(SettingsService.SecretsDirectory + "/", StringComparison.OrdinalIgnoreCase) || r.StartsWith(SettingsService.SecretsDirectory + "\\", StringComparison.OrdinalIgnoreCase))
								continue;
							string key = blockPath + " <- " + r.Replace('\\', '/');
							if (!uses.TryGetValue(key, out var list)) uses[key] = list = new List<string>();
							if (!list.Contains(alias)) list.Add(alias);
						}
					}
				} catch {
				}
			}
			foreach (var kv in uses.Where(k => k.Value.Count > 1))
				Add(f, "warn", kv.Key.Split(new[] { " <- " }, StringSplitOptions.None)[0],
					$"This block is loaded from the same file ({kv.Key.Split(new[] { " <- " }, StringSplitOptions.None)[1]}) by the instances {string.Join(", ", kv.Value)}: a change in one instance also changes the others. Give each instance its own file if the values should differ (for example coordinates).");
			return f;
		}

		private static IEnumerable<(string Path, List<string> Refs)> FindLoads(JToken t, string path) {
			if (t is JObject o) {
				if (o.TryGetValue("Load", StringComparison.OrdinalIgnoreCase, out var l)) {
					var refs = new List<string>();
					if (l.Type == JTokenType.String) refs.Add(l.ToString());
					else if (l is JArray a) refs.AddRange(a.Where(x => x.Type == JTokenType.String).Select(x => x.ToString()));
					if (refs.Count > 0) yield return (path, refs);
				}
				foreach (var p in o.Properties())
					foreach (var x in FindLoads(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name))
						yield return x;
			} else if (t is JArray arr) {
				for (int i = 0; i < arr.Count; i++)
					foreach (var x in FindLoads(arr[i], path + "[" + i + "]"))
						yield return x;
			}
		}

		private static void CheckCoordinates(JObject root, List<ConfigFinding> f, ICollection<string> owned) {
			foreach (var (path, o) in FindObjects(root, "", t => t.ContainsKey("Galaxy") && t.ContainsKey("System") && t.ContainsKey("Position"))) {
				if (!TryInt(o["Galaxy"], out int g) || !TryInt(o["System"], out int s) || !TryInt(o["Position"], out int p)) {
					Add(f, "error", path, "Galaxy, System and Position must be whole numbers.");
					continue;
				}
				string type = o["Type"]?.ToString() ?? "Planet";
				bool outOfRange = false;
				if (g < 1 || g > 9) { Add(f, "error", path, $"Galaxy {g} is outside 1-9."); outOfRange = true; }
				if (s < 1 || s > 499) { Add(f, "error", path, $"System {s} is outside 1-499."); outOfRange = true; }
				if (p < 1 || p > 15) { Add(f, "error", path, $"Position {p} is outside 1-15 (16 is the expedition target, not a celestial)."); outOfRange = true; }
				if (o["Type"] != null && !new[] { "Planet", "Moon", "Debris" }.Contains(type, StringComparer.OrdinalIgnoreCase))
					Add(f, "error", path, $"Type '{type}' is not Planet, Moon or Debris.");
				if (outOfRange)
					continue;
				if (g == 1 && s == 1 && p == 1 && IsActiveContext(root, path) && !path.Contains("ColonyOverrides[", StringComparison.OrdinalIgnoreCase))
					Add(f, "warn", path, "Coordinates 1:1:1 are the template default. Enter a celestial of your own.");
				else if (owned != null && owned.Count > 0 && IsOwnCelestialPath(path) && IsActiveContext(root, path) && !owned.Contains(CoordKey(g, s, p, NormType(type))))
					Add(f, "error", path, $"{g}:{s}:{p} ({NormType(type)}) is not one of your celestials.");
			}
		}

		public static List<(string Path, bool HasType)> FindTemplateCoordinates(JObject root) {
			var result = new List<(string, bool)>();
			string[] skip = { "Exclude", "Whitelist", "Blacklist", "ColonyOverrides" };
			foreach (var (path, o) in FindObjects(root, "", t => t.ContainsKey("Galaxy") && t.ContainsKey("System") && t.ContainsKey("Position"))) {
				if (!TryInt(o["Galaxy"], out int g) || !TryInt(o["System"], out int s) || !TryInt(o["Position"], out int p)) continue;
				if (g != 1 || s != 1 || p != 1) continue;
				bool skipped = path.Split('.').Any(seg => skip.Any(k => seg.Equals(k, StringComparison.OrdinalIgnoreCase) || seg.StartsWith(k + "[", StringComparison.OrdinalIgnoreCase)));
				if (skipped) continue;
				result.Add((path, o["Type"] != null));
			}
			return result;
		}

		private static bool IsOwnCelestialPath(string path) {
			string[] own = { "Brain.Transports.Origin", "Brain.AutoMine.HomeWorld", "Brain.AutoResearch.Target", "Brain.AutoRepatriate.Target", "Expeditions.Origin", "AutoDiscovery.Origin", "AutoColonize.Origin" };
			return own.Any(x => path.Equals(x, StringComparison.OrdinalIgnoreCase) || path.StartsWith(x + "[", StringComparison.OrdinalIgnoreCase));
		}

		private static string NormType(string t) => string.Equals(t, "Moon", StringComparison.OrdinalIgnoreCase) ? "Moon" : "Planet";

		private static bool IsActiveContext(JObject root, string path) {
			string cur = "";
			foreach (string part in SplitPath(path)) {
				cur = cur.Length == 0 ? part : cur + "." + part;
				var tok = Resolve(root, cur);
				if (tok is JObject o && o.TryGetValue("Active", StringComparison.OrdinalIgnoreCase, out var a) && a.Type == JTokenType.Boolean && !(bool) a)
					return false;
			}
			return true;
		}

		private static IEnumerable<string> SplitPath(string path) => path.Split('.');

		private static JToken Resolve(JObject root, string dottedPath) {
			JToken cur = root;
			foreach (string seg in dottedPath.Split('.')) {
				string name = seg; int idx = -1;
				int br = seg.IndexOf('[');
				if (br >= 0) { name = seg.Substring(0, br); int.TryParse(seg.Substring(br + 1).TrimEnd(']'), out idx); }
				if (cur is JObject o && o.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var next)) cur = next; else return null;
				if (idx >= 0) { if (cur is JArray a && idx < a.Count) cur = a[idx]; else return null; }
			}
			return cur;
		}

		private static IEnumerable<(string Path, JObject Obj)> FindObjects(JToken t, string path, Func<JObject, bool> match) {
			if (t is JObject o) {
				if (path.Length > 0 && match(o)) yield return (path, o);
				foreach (var p in o.Properties())
					foreach (var x in FindObjects(p.Value, path.Length == 0 ? p.Name : path + "." + p.Name, match))
						yield return x;
			} else if (t is JArray arr) {
				for (int i = 0; i < arr.Count; i++)
					foreach (var x in FindObjects(arr[i], path + "[" + i + "]", match))
						yield return x;
			}
		}

		private static void CheckBrain(JObject root, List<ConfigFinding> f) {
			if (!(root["Brain"] is JObject brain))
				return;
			bool brainOn = Bool(brain, "Active");
			var transports = brain["Transports"] as JObject;
			bool transportsOn = transports != null && Bool(transports, "Active");
			foreach (string w in BrainWorkers) {
				if (!(brain[w] is JObject wo) || !Bool(wo, "Active"))
					continue;
				if (!brainOn)
					Add(f, "warn", "Brain." + w + ".Active", "The worker is on but Brain.Active is off: it does not run.");
				if (brainOn && TransportUsers.Contains(w) && wo["Transports"] is JObject wt && Bool(wt, "Active") && !transportsOn)
					Add(f, "warn", "Brain." + w + ".Transports.Active", "The worker wants deliveries but Brain.Transports.Active is off: it can only build with what is already on the celestial.");
			}
			if (transports != null && transportsOn) {
				if (TryInt(transports["MaxSlots"], out int ms) && ms < 1)
					Add(f, "warn", "Brain.Transports.MaxSlots", "Transports are on but MaxSlots is below 1: no delivery can start.");
				if (transports["Origin"] is JObject og && !(og.ContainsKey("Galaxy") && og.ContainsKey("System") && og.ContainsKey("Position")))
					Add(f, "error", "Brain.Transports.Origin", "The origin needs Galaxy, System and Position.");
				if (transports["Origin"] == null)
					Add(f, "error", "Brain.Transports.Origin", "Transports are on but no origin is set.");
			}
			if (brain["AutoMine"] is JObject am && Bool(am, "Active")) {
				if (am["HomeWorld"] == null)
					Add(f, "warn", "Brain.AutoMine.HomeWorld", "AutoMine is on but no home world is set (the colony rules cannot tell it apart).");
				if (TryInt(am["MaxDaysOfInvestmentReturn"], out int d) && d < 1)
					Add(f, "warn", "Brain.AutoMine.MaxDaysOfInvestmentReturn", "Below 1 day: no mine will ever pay back, AutoMine builds nothing.");
			}
		}

		private static void CheckGeneral(JObject root, List<ConfigFinding> f) {
			if (!(root["General"] is JObject g))
				return;
			if (g["Port"] != null) {
				if (!TryInt(g["Port"], out int port) || port < 1 || port > 65535)
					Add(f, "error", "General.Port", "The port must be a number from 1 to 65535.");
			}
			if (g["Host"] != null && string.IsNullOrWhiteSpace(g["Host"].ToString()))
				Add(f, "warn", "General.Host", "The host is empty.");
			if (TryInt(g["SlotsToLeaveFree"], out int sl) && (sl < 0 || sl > 20))
				Add(f, "warn", "General.SlotsToLeaveFree", $"{sl} looks wrong (expected 0-20).");
		}

		private static void CheckSleep(JObject root, List<ConfigFinding> f) {
			foreach (string block in new[] { "SleepMode", "GhostSleep" }) {
				if (!(root[block] is JObject s) || !Bool(s, "Active"))
					continue;
				string go = s["GoToSleep"]?.ToString(), wake = s["WakeUp"]?.ToString();
				bool goOk = DateTime.TryParse(go, out var g), wakeOk = DateTime.TryParse(wake, out var w);
				if (!goOk) Add(f, "error", block + ".GoToSleep", $"'{go}' is not a valid time (expected HH:mm:ss).");
				if (!wakeOk) Add(f, "error", block + ".WakeUp", $"'{wake}' is not a valid time (expected HH:mm:ss).");
				if (goOk && wakeOk && g.TimeOfDay == w.TimeOfDay)
					Add(f, "warn", block, "GoToSleep and WakeUp are the same time.");
			}
		}

		private static void CheckExpeditions(JObject root, List<ConfigFinding> f) {
			if (!(root["Expeditions"] is JObject e) || !Bool(e, "Active"))
				return;
			if (e["Origin"] is JArray origins) {
				if (origins.Count == 0)
					Add(f, "error", "Expeditions.Origin", "Expeditions are on but the origin list is empty.");
			} else if (e["Origin"] == null)
				Add(f, "error", "Expeditions.Origin", "Expeditions are on but no origin is set.");
			if (TryInt(e["MinPrimaryToSend"], out int min) && min < 1)
				Add(f, "warn", "Expeditions.MinPrimaryToSend", "Below 1: an expedition with no ships could start.");
			if (TryInt(e["FuelToCarry"], out int fuel) && fuel < 0)
				Add(f, "error", "Expeditions.FuelToCarry", "Negative value.");
			if (Bool(e, "SmartShips")) {
				if (e["ManualShips"] is JObject ms && Bool(ms, "Active"))
					Add(f, "warn", "Expeditions.SmartShips", "SmartShips is on but ManualShips is active: the fixed fleet is used and SmartShips has no effect. Switch ManualShips off to use it.");
				string mix = e["SmartMixShip"]?.ToString();
				if (mix != null && !SmartMixShips.Contains(mix, StringComparer.OrdinalIgnoreCase))
					Add(f, "warn", "Expeditions.SmartMixShip", $"'{mix}' is not one of {string.Join(", ", SmartMixShips)}; SmallCargo is used instead.");
			}
		}

		private static void CheckHarvest(JObject root, List<ConfigFinding> f) {
			if (!(root["AutoHarvest"] is JObject h) || !Bool(h, "Active"))
				return;
			bool own = Bool(h, "HarvestOwnDF"), foreign = Bool(h, "HarvestForeignDF"), deep = Bool(h, "HarvestDeepSpace");
			if (!own && !foreign && !deep)
				Add(f, "warn", "AutoHarvest", "AutoHarvest is on but own, foreign and deep-space debris are all switched off: nothing is harvested.");
			if (TryInt(h["MaxSlots"], out int slots) && slots < 1)
				Add(f, "warn", "AutoHarvest.MaxSlots", "Below 1: no harvest fleet can start.");
			if (TryInt(h["DeepSpaceRange"], out int dr)) {
				if (dr < 0) Add(f, "error", "AutoHarvest.DeepSpaceRange", "Negative value.");
				else if (deep && dr > HarvestRangeWarn) Add(f, "warn", "AutoHarvest.DeepSpaceRange", $"{dr} systems to each side of every planet means up to {2 * dr + 1} galaxy calls per planet and cycle: long cycles. Values up to {HarvestRangeWarn} are usual; 'ScanOnlyExpeditionOrigins' reduces the scan centers.");
				else if (!deep && dr > 0) Add(f, "info", "AutoHarvest.DeepSpaceRange", "Has no effect while HarvestDeepSpace is off.");
			}
			if (TryInt(h["ForeignDFRange"], out int fr)) {
				if (fr < 0) Add(f, "error", "AutoHarvest.ForeignDFRange", "Negative value.");
				else if (foreign && fr > HarvestRangeWarn) Add(f, "warn", "AutoHarvest.ForeignDFRange", $"{fr} systems to each side of every planet means up to {2 * fr + 1} galaxy calls per planet and cycle: long cycles. Values up to {HarvestRangeWarn} are usual.");
				else if (!foreign && fr > 0) Add(f, "info", "AutoHarvest.ForeignDFRange", "Has no effect while HarvestForeignDF is off.");
			}
			if (Bool(h, "ScanOnlyExpeditionOrigins")) {
				bool noOrigins = !(root["Expeditions"] is JObject e && e["Origin"] is JArray oa && oa.Count > 0);
				if (noOrigins)
					Add(f, "warn", "AutoHarvest.ScanOnlyExpeditionOrigins", "On, but Expeditions.Origin has no entries: all planets are scanned instead.");
				if (!deep && !foreign)
					Add(f, "info", "AutoHarvest.ScanOnlyExpeditionOrigins", "Has no effect while deep-space and foreign debris are both off.");
			}
		}

		private static void CheckMinePlan(JObject root, List<ConfigFinding> f) {
			if (!(root["Experimental"] is JObject x) || !(x["MinePlanRanking"] is JObject r))
				return;
			string mode = r["Mode"]?.ToString();
			if (mode != null && !RankModes.Contains(mode, StringComparer.OrdinalIgnoreCase))
				Add(f, "error", "Experimental.MinePlanRanking.Mode", $"'{mode}' is not one of {string.Join(", ", RankModes)}; Payback is used instead.");
			bool usePlan = Bool(r, "UsePlanForAutoMine");
			if (TryInt(r["PlanLookahead"], out int la) && la < 1)
				Add(f, "warn", "Experimental.MinePlanRanking.PlanLookahead", "Below 1: no planet is ever due.");
			if (TryInt(r["MaxPaybackDays"], out int mp) && mp < 1)
				Add(f, "warn", "Experimental.MinePlanRanking.MaxPaybackDays", "Below 1: no step pays back within the limit.");
			if (usePlan && root["Brain"] is JObject b && !(Bool(b, "Active") && b["AutoMine"] is JObject am && Bool(am, "Active")))
				Add(f, "warn", "Experimental.MinePlanRanking.UsePlanForAutoMine", "The plan is on but AutoMine is off: the plan only shows a preview.");
			if (string.Equals(mode, "Cheapest", StringComparison.OrdinalIgnoreCase) && !Bool(r, "OnlySensible"))
				Add(f, "info", "Experimental.MinePlanRanking.OnlySensible", "Mode Cheapest ignores the payback limit as long as 'Only sensible steps' is off: expensive steps with a very long payback also enter the plan.");
		}

		private static void Add(List<ConfigFinding> f, string sev, string path, string msg) => f.Add(new ConfigFinding { Severity = sev, Path = path, Message = msg });
		private static bool Bool(JObject o, string key) => o.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var v) && v.Type == JTokenType.Boolean && (bool) v;
		private static bool TryInt(JToken t, out int value) {
			value = 0;
			if (t == null) return false;
			if (t.Type == JTokenType.Integer) { value = (int) (long) t; return true; }
			if (t.Type == JTokenType.Float) { double d = (double) t; if (d == Math.Floor(d)) { value = (int) d; return true; } return false; }
			if (t.Type == JTokenType.String) return int.TryParse(t.ToString(), out value);
			return false;
		}
	}
}
