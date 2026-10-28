using Tbot.Common.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public sealed class ExpoNeed {
		public Celestial Origin;
		public int Expeditions;
		public Ships PerExpedition;
		public Ships Need;
		public long DeuteriumTarget;
	}

	public sealed class ExpoSupplyEntry {
		public string Coord;
		public Dictionary<string, long> Ships = new();
		public long Deut;
		public long Since;
	}

	public static class ExpoSupply {
		public static readonly Buildables[] Types = {
			Buildables.LightFighter, Buildables.HeavyFighter, Buildables.Cruiser, Buildables.Battleship, Buildables.Battlecruiser,
			Buildables.Bomber, Buildables.Destroyer, Buildables.Deathstar, Buildables.SmallCargo, Buildables.LargeCargo,
			Buildables.ColonyShip, Buildables.Recycler, Buildables.EspionageProbe, Buildables.Reaper, Buildables.Pathfinder
		};
		const int TtlMinutes = 180;
		static readonly object _lock = new();


		public static Ships Multiply(Ships per, long times) {
			var r = new Ships();
			if (per == null || times <= 0) return r;
			foreach (var t in Types) {
				long a = per.GetAmount(t);
				if (a > 0) r.SetAmount(t, a * times);
			}
			return r;
		}

		public static Ships Missing(Ships need, Ships present) {
			var r = new Ships();
			if (need == null) return r;
			foreach (var t in Types) {
				long m = need.GetAmount(t) - (present?.GetAmount(t) ?? 0);
				if (m > 0) r.SetAmount(t, m);
			}
			return r;
		}

		public static Ships Cap(Ships wanted, Ships available) {
			var r = new Ships();
			if (wanted == null) return r;
			foreach (var t in Types) {
				long a = Math.Min(wanted.GetAmount(t), available?.GetAmount(t) ?? 0);
				if (a > 0) r.SetAmount(t, a);
			}
			return r;
		}

		public static Ships Sum(Ships a, Ships b) {
			var r = new Ships();
			foreach (var t in Types) {
				long s = (a?.GetAmount(t) ?? 0) + (b?.GetAmount(t) ?? 0);
				if (s > 0) r.SetAmount(t, s);
			}
			return r;
		}

		public static Ships Subtract(Ships a, Ships b) => Missing(a, b);

		public static bool IsEmpty(Ships s) => s == null || Types.All(t => s.GetAmount(t) <= 0);

		public static long DeuteriumTarget(long minOnMoon, long fuelToCarry, int expeditions) {
			if (minOnMoon > 0) return minOnMoon;
			return Math.Max(0, fuelToCarry) * Math.Max(0, expeditions);
		}

		public static int ExpeditionsFor(int perOrigin, int maxPerOrigin, int originCount, int originIndex, int expTotal) {
			if (perOrigin > 0) return perOrigin;
			int d = maxPerOrigin;
			if (originCount > 1 && expTotal > 0)
				d = Math.Min(maxPerOrigin, expTotal / originCount + (originIndex < expTotal % originCount ? 1 : 0));
			return Math.Max(0, d);
		}


		public static bool OverrideActive(ITBotMain inst) {
			try {
				var jg = inst.InstanceSettings.Brain.AutoFleetJumpGate;
				return SettingsService.IsSettingSet(jg, "ExpoOverride") && (bool) jg.ExpoOverride;
			} catch (Exception) { return false; }
		}

		public static bool SupplyActive(ITBotMain inst) {
			try {
				var ar = inst.InstanceSettings.Brain.AutoRepatriate;
				return SettingsService.IsSettingSet(ar, "ExpoSupply") && SettingsService.IsSettingSet(ar.ExpoSupply, "Active") && (bool) ar.ExpoSupply.Active;
			} catch (Exception) { return false; }
		}

		public static int BackupExpeditions(ITBotMain inst) {
			try {
				var e = inst.InstanceSettings.Expeditions;
				if (SettingsService.IsSettingSet(e, "BackupExpeditions"))
					return Math.Max(0, Convert.ToInt32(e.BackupExpeditions));
			} catch (Exception) { }
			return 1;
		}

		static string Key(Coordinate c) => $"{c.Galaxy}:{c.System}:{c.Position}:{c.Type}";


		static LFBonuses _lfCache;
		static DateTime _lfCacheAt = DateTime.MinValue;

		static async Task<LFBonuses> LfBonuses(IOgameService ogame) {
			if (_lfCache != null && (DateTime.UtcNow - _lfCacheAt).TotalMinutes < 10) return _lfCache;
			try { _lfCache = await ogame.GetLFBonuses(); _lfCacheAt = DateTime.UtcNow; } catch (Exception) { }
			return _lfCache;
		}

		static long ReadManual(dynamic ms, Buildables t) {
			try {
				switch (t) {
					case Buildables.LightFighter: return (long) ms.LightFighter;
					case Buildables.HeavyFighter: return (long) ms.HeavyFighter;
					case Buildables.Cruiser: return (long) ms.Cruiser;
					case Buildables.Battleship: return (long) ms.Battleship;
					case Buildables.Battlecruiser: return (long) ms.Battlecruiser;
					case Buildables.Bomber: return (long) ms.Bomber;
					case Buildables.Destroyer: return (long) ms.Destroyer;
					case Buildables.Deathstar: return (long) ms.Deathstar;
					case Buildables.SmallCargo: return (long) ms.SmallCargo;
					case Buildables.LargeCargo: return (long) ms.LargeCargo;
					case Buildables.ColonyShip: return (long) ms.ColonyShip;
					case Buildables.Recycler: return (long) ms.Recycler;
					case Buildables.EspionageProbe: return (long) ms.EspionageProbe;
					case Buildables.Reaper: return (long) ms.Reaper;
					case Buildables.Pathfinder: return (long) ms.Pathfinder;
				}
			} catch (Exception) { }
			return 0;
		}

		static Ships PerExpeditionFleet(ITBotMain inst, ICalculationService calc, LFBonuses lf) {
			var exp = inst.InstanceSettings.Expeditions;
			if ((bool) exp.ManualShips.Active) {
				var ms = exp.ManualShips.Ships;
				var r = new Ships();
				foreach (var t in Types) {
					long a = ReadManual(ms, t);
					if (a > 0) r.SetAmount(t, a);
				}
				return r;
			}
			if (!Enum.TryParse<Buildables>(exp.PrimaryShip.ToString(), true, out Buildables primary) || primary == Buildables.Null)
				return new Ships();
			var huge = new Ships();
			foreach (var t in Types) huge.SetAmount(t, 1_000_000_000L);
			var res = inst.UserData.researches; var sd = inst.UserData.serverData; var cls = inst.UserData.userInfo.Class;
			long fuel = SettingsService.IsSettingSet(exp, "FuelToCarry") ? Convert.ToInt64(exp.FuelToCarry) : 0;
			Func<Buildables, int> capacityOf = ship => {
				float cargoBonus = lf != null && lf.LfShipBonusesInt != null && lf.LfShipBonusesInt.TryGetValue((int) ship, out var sb) ? sb.CargoCapacity : 0;
				return calc.CalcShipCapacity(ship, res.HyperspaceTechnology, sd, cargoBonus, cls, sd.ProbeCargo);
			};
			bool smart = SettingsService.IsSettingSet(exp, "SmartShips") && (bool) exp.SmartShips;
			if (smart) {
				Buildables mix = Buildables.SmallCargo;
				if (SettingsService.IsSettingSet(exp, "SmartMixShip") && !Enum.TryParse<Buildables>((string) exp.SmartMixShip, true, out mix))
					mix = Buildables.SmallCargo;
				float expBonus = lf?.LfResourceBonuses != null ? lf.LfResourceBonuses.ResourcesExpedition : 0;
				var ideal = calc.CalcIdealExpeditionShips(primary, res.HyperspaceTechnology, expBonus, lf?.LfShipBonusesInt, sd, cls, sd.ProbeCargo);
				return SmartExpeditionFleet.Build(new SmartExpeditionInput {
					Available = huge, Primary = primary, Mix = mix, Pending = 1,
					IdealPrimary = ideal.GetAmount(primary), ExtraCapacity = fuel,
					CapacityPrimary = capacityOf(primary), CapacityMix = mix == Buildables.Null ? 0 : capacityOf(mix)
				});
			}
			var fleet = calc.CalcFullExpeditionShips(huge, primary, 1, sd, res, lf, cls, sd.ProbeCargo);
			if (fuel > 0 && fleet.GetAmount(primary) > 0) {
				int cap = capacityOf(primary);
				if (cap > 0) fleet.Add(primary, (fuel + cap - 1) / cap);
			}
			return fleet;
		}

		public static async Task<List<ExpoNeed>> PlanAsync(ITBotMain inst, ICalculationService calc, IOgameService ogame, Action<LogLevel, string> log) {
			var result = new List<ExpoNeed>();
			try {
				var exp = inst.InstanceSettings.Expeditions;
				var configured = exp.Origin;
				if (configured == null || configured.Length == 0) return result;
				var lf = await LfBonuses(ogame);
				var per = PerExpeditionFleet(inst, calc, lf);
				if (IsEmpty(per)) return result;
				int backup = BackupExpeditions(inst);
				int maxPerOrigin = (int?) exp.MaxExpeditionsPerOrigin ?? 1;
				int expTotal = inst.UserData.slots != null ? (int) inst.UserData.slots.ExpTotal : 0;
				long fuel = SettingsService.IsSettingSet(exp, "FuelToCarry") ? Convert.ToInt64(exp.FuelToCarry) : 0;
				long minDeut = SettingsService.IsSettingSet(exp, "MinDeuteriumOnMoon") ? Convert.ToInt64(exp.MinDeuteriumOnMoon) : 0;
				int idx = 0;
				foreach (var o in configured) {
					Celestials type = Enum.Parse<Celestials>((string) o.Type.ToString(), true);
					int og = Convert.ToInt32(o.Galaxy), os = Convert.ToInt32(o.System), op = Convert.ToInt32(o.Position);
					List<Celestial> all = inst.UserData.celestials ?? new List<Celestial>();
					Celestial moon = all.FirstOrDefault(c => c?.Coordinate != null
						&& c.Coordinate.Galaxy == og && c.Coordinate.System == os && c.Coordinate.Position == op && c.Coordinate.Type == type);
					int index = idx++;
					if (moon == null || moon.Coordinate.Type != Celestials.Moon) continue;
					int perOrigin = 0;
					try { if (SettingsService.IsSettingSet(o, "MaxExpeditions")) perOrigin = Convert.ToInt32(o.MaxExpeditions); } catch (Exception) { }
					int n = ExpeditionsFor(perOrigin, maxPerOrigin, configured.Length, index, expTotal);
					if (n <= 0) continue;
					result.Add(new ExpoNeed {
						Origin = moon, Expeditions = n, PerExpedition = per,
						Need = Multiply(per, n + backup),
						DeuteriumTarget = DeuteriumTarget(minDeut, fuel, n)
					});
				}
			} catch (Exception e) {
				log?.Invoke(LogLevel.Warning, $"ExpoSupply: need could not be calculated, ignored: {e.Message}");
				return new List<ExpoNeed>();
			}
			return result;
		}

		public static Dictionary<string, Ships> LeaveOverrides(List<ExpoNeed> needs) {
			var d = new Dictionary<string, Ships>();
			foreach (var n in needs ?? new List<ExpoNeed>())
				d[Key(n.Origin.Coordinate)] = n.Need;
			return d;
		}

		public static string CoordKey(Coordinate c) => Key(c);


		static string PathFor(string alias) => Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"expo_supply_{alias}.json"));

		public static List<ExpoSupplyEntry> LoadState(string alias) {
			try {
				string p = PathFor(alias);
				if (!File.Exists(p)) return new List<ExpoSupplyEntry>();
				var a = JArray.Parse(File.ReadAllText(p));
				var list = new List<ExpoSupplyEntry>();
				foreach (JObject o in a.OfType<JObject>()) {
					var e = new ExpoSupplyEntry { Coord = (string) o["coord"], Deut = (long?) o["deut"] ?? 0, Since = (long?) o["since"] ?? 0 };
					if (o["ships"] is JObject s)
						foreach (var kv in s.Properties()) e.Ships[kv.Name] = (long) kv.Value;
					if (!string.IsNullOrEmpty(e.Coord)) list.Add(e);
				}
				return list;
			} catch (Exception) { return new List<ExpoSupplyEntry>(); }
		}

		public static void SaveState(string alias, List<ExpoSupplyEntry> list) {
			try {
				var a = new JArray();
				foreach (var e in list) {
					var ships = new JObject();
					foreach (var kv in e.Ships) ships[kv.Key] = kv.Value;
					a.Add(new JObject { ["coord"] = e.Coord, ["ships"] = ships, ["deut"] = e.Deut, ["since"] = e.Since });
				}
				string p = PathFor(alias);
				Directory.CreateDirectory(Path.GetDirectoryName(p));
				AtomicFile.WriteAllText(p, a.ToString(Newtonsoft.Json.Formatting.None));
			} catch (Exception) { }
		}

		public static Ships ToShips(Dictionary<string, long> d) {
			var r = new Ships();
			foreach (var t in Types)
				if (d != null && d.TryGetValue(t.ToString(), out long v) && v > 0) r.SetAmount(t, v);
			return r;
		}

		public static Dictionary<string, long> FromShips(Ships s) {
			var d = new Dictionary<string, long>();
			foreach (var t in Types) { long a = s?.GetAmount(t) ?? 0; if (a > 0) d[t.ToString()] = a; }
			return d;
		}


		public static async Task RunAsync(ITBotMain inst, ICalculationService calc, IOgameService ogame, ITBotOgamedBridge bridge, IFleetScheduler scheduler, Action<LogLevel, string> log) {
			if (!SupplyActive(inst) || inst.UserData.isSleeping) return;
			try {
				var needs = await PlanAsync(inst, calc, ogame, log);
				if (needs.Count == 0) return;

				List<Celestial> targets = calc.ParseCelestialsList((object) inst.InstanceSettings.Brain.AutoRepatriate.Target, (List<Celestial>) inst.UserData.celestials);
				var home = targets?.FirstOrDefault(c => c.Coordinate.Type == Celestials.Planet) ?? targets?.FirstOrDefault();
				if (home == null) { log(LogLevel.Warning, "ExpoSupply: no home celestial (Brain.AutoRepatriate.Target), skipped."); return; }

				inst.UserData.fleets = await scheduler.UpdateFleets();
				var fleets = inst.UserData.fleets?.ToList() ?? new List<Fleet>();
				var state = LoadState(inst.InstanceAlias);
				long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
				state.RemoveAll(e => now - e.Since > TtlMinutes * 60L || !needs.Any(n => Key(n.Origin.Coordinate) == e.Coord));

				Celestial homeFresh = null;
				foreach (var need in needs) {
					string key = Key(need.Origin.Coordinate);
					var moon = await bridge.UpdatePlanet(need.Origin, UpdateTypes.Ships);
					moon = await bridge.UpdatePlanet(moon, UpdateTypes.Resources);

					var present = moon.Ships?.GetMovableShips() ?? new Ships();
					long deutPresent = (long) (moon.Resources?.Deuterium ?? 0);
					foreach (var f in fleets) {
						if (f?.Ships == null) continue;
						bool expo = f.Mission == Missions.Expedition && f.Origin != null && f.Origin.IsSame(need.Origin.Coordinate);
						bool inbound = f.Mission == Missions.Deploy && !f.ReturnFlight && f.Destination != null && f.Destination.IsSame(need.Origin.Coordinate) && f.Origin != null && !f.Origin.IsSame(need.Origin.Coordinate);
						if (expo) present = Sum(present, f.Ships);
						if (inbound) { present = Sum(present, f.Ships); deutPresent += (long) (f.Resources?.Deuterium ?? 0); }
					}
					var missing = Missing(need.Need, present);
					long deutMissing = Math.Max(0, need.DeuteriumTarget - deutPresent);

					var entry = state.FirstOrDefault(e => e.Coord == key);
					if (entry != null && (!IsEmpty(ToShips(entry.Ships)) || entry.Deut > 0)) {
						var wantShips = Cap(ToShips(entry.Ships), missing);
						long wantDeut = Math.Min(entry.Deut, deutMissing);
						if (!IsEmpty(wantShips) || wantDeut > 0) {
							homeFresh ??= await bridge.UpdatePlanet(await bridge.UpdatePlanet(home, UpdateTypes.Ships), UpdateTypes.Resources);
							var sendShips = Cap(wantShips, homeFresh.Ships?.GetMovableShips());
							long homeDeut = (long) (homeFresh.Resources?.Deuterium ?? 0);
							long sendDeut = Math.Min(wantDeut, homeDeut);
							if (IsEmpty(sendShips) && sendDeut <= 0) {
								log(LogLevel.Warning, $"ExpoSupply: {need.Origin.Coordinate} needs ships ({wantShips}) / deuterium ({wantDeut:N0}), but the base has nothing to send.");
							} else {
								var payload = new Resources { Deuterium = sendDeut };
								int id = await scheduler.SendFleet(homeFresh, IsEmpty(sendShips) ? PickCarrier(homeFresh, sendDeut, inst) : sendShips, need.Origin.Coordinate, Missions.Deploy, Speeds.HundredPercent, payload, inst.UserData.userInfo.Class, false, false, 0, false, true, false);
								if (id > 0) {
									log(LogLevel.Information, $"ExpoSupply: sent {sendShips} and {sendDeut:N0} deuterium from {homeFresh.Coordinate} to expedition moon {need.Origin.Coordinate}.");
									missing = Subtract(missing, sendShips);
									deutMissing = Math.Max(0, deutMissing - sendDeut);
									homeFresh = null;
								} else if (id == (int) SendFleetCode.NotEnoughSlots || id == (int) SendFleetCode.AfterSleepTime) {
									log(LogLevel.Information, $"ExpoSupply: delivery to {need.Origin.Coordinate} waits ({(id == (int) SendFleetCode.NotEnoughSlots ? "no free slot" : "sleep time")}).");
									continue;
								} else {
									log(LogLevel.Warning, $"ExpoSupply: delivery to {need.Origin.Coordinate} failed (code {id}).");
								}
							}
						}
					}

					if (IsEmpty(missing) && deutMissing <= 0) {
						state.RemoveAll(e => e.Coord == key);
					} else {
						if (entry == null) { entry = new ExpoSupplyEntry { Coord = key, Since = now }; state.Add(entry); }
						entry.Ships = FromShips(missing); entry.Deut = deutMissing;
						log(LogLevel.Information, $"ExpoSupply: {need.Origin.Coordinate} is short of {missing}{(deutMissing > 0 ? $" and {deutMissing:N0} deuterium" : "")}; delivery from the base in the next repatriate cycle.");
					}
				}
				SaveState(inst.InstanceAlias, state);
			} catch (Exception e) {
				log(LogLevel.Warning, $"ExpoSupply failed: {e.Message}");
			}
		}

		static Ships PickCarrier(Celestial home, long deut, ITBotMain inst) {
			var r = new Ships();
			var avail = home.Ships;
			if (avail == null) return r;
			foreach (var t in new[] { Buildables.SmallCargo, Buildables.LargeCargo }) {
				long have = avail.GetAmount(t);
				if (have <= 0) continue;
				int perShip = t == Buildables.SmallCargo ? 5000 : 25000;
				long need = Math.Max(1, (deut + perShip - 1) / perShip);
				r.SetAmount(t, Math.Min(have, need));
				return r;
			}
			return r;
		}
	}
}
