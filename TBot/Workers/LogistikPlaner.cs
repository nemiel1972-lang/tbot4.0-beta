using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.IO;
using Tbot.Common.Settings;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal static class LogistikPlaner {
		public static string MarkerPath => Path.Combine(AppContext.BaseDirectory, "logistik.on");

		private static string Safe(ITBotMain inst) =>
			string.Concat((inst.InstanceAlias ?? "MAIN").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
		private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
		private static string AnfragePfad(ITBotMain inst) => Path.Combine(DataDir, $"logistik_anfrage_{Safe(inst)}.txt");
		private static string EinstellungPfad(ITBotMain inst) => Path.Combine(DataDir, $"logistik_einstellung_{Safe(inst)}.json");
		private static string StandPfad(ITBotMain inst) => Path.Combine(DataDir, $"logistik_{Safe(inst)}.json");

		public static async Task Loop(ITBotMain inst, ITBotOgamedBridge bridge, ICalculationService calc, CancellationToken ct) {
			try {
				DateTime letzter = DateTime.MinValue;
				while (!ct.IsCancellationRequested) {
					await Task.Delay(TimeSpan.FromSeconds(15), ct);
					bool anfrage = File.Exists(AnfragePfad(inst));
					bool auto = File.Exists(MarkerPath) && DateTime.UtcNow - letzter > TimeSpan.FromMinutes(15);
					if (!anfrage && !auto)
						continue;
					try {
						if (anfrage)
							File.Delete(AnfragePfad(inst));
						await Berechne(inst, bridge, calc);
						letzter = DateTime.UtcNow;
					} catch (Exception e) {
						letzter = DateTime.UtcNow;
						inst.log(LogLevel.Warning, LogSender.Tbot, $"Logistics planner failed: {e.Message}");
					}
				}
			} catch (OperationCanceledException) {
			} catch (Exception) { }
		}

		private sealed class Einstellung {
			public double PufferProzent = 20;
			public double HorizontStunden = 24;
			public int ReserveMinuten = 20;
		}

		private static Einstellung LiesEinstellung(ITBotMain inst) {
			var e = new Einstellung();
			try {
				string p = EinstellungPfad(inst);
				if (File.Exists(p)) {
					var j = JObject.Parse(File.ReadAllText(p));
					if (j["BufferPercent"] != null) e.PufferProzent = Math.Max(0, Math.Min(500, (double) j["BufferPercent"]));
					if (j["HorizonHours"] != null) e.HorizontStunden = Math.Max(0, Math.Min(240, (double) j["HorizonHours"]));
					if (j["ReserveMinutes"] != null) e.ReserveMinuten = Math.Max(0, Math.Min(600, (int) j["ReserveMinutes"]));
				}
			} catch (Exception) { }
			return e;
		}

		private static long Kapazitaet(ITBotMain inst, ICalculationService calc, Celestial c, Buildables typ) {
			try {
				Ships s = new();
				s.Add(typ, 1);
				Resources gross = new Resources(1000000000000L, 0, 0);
				Resources passt = calc.CalcMaxTransportableResources(s, gross, inst.UserData.researches.HyperspaceTechnology, inst.UserData.serverData, c.LFBonuses, inst.UserData.userInfo.Class, inst.UserData.serverData.ProbeCargo);
				return passt.Metal + passt.Crystal + passt.Deuterium;
			} catch (Exception) {
				return 0;
			}
		}

		private static FleetPrediction Vorhersage(ITBotMain inst, ICalculationService calc, Celestial von, Coordinate nach, Ships s) {
			return calc.CalcFleetPrediction(von.Coordinate, nach, s, Missions.Deploy, Speeds.HundredPercent, inst.UserData.researches, inst.UserData.serverData, von.LFBonuses, inst.UserData.userInfo.Class, inst.UserData.allianceClass);
		}

		private static async Task Berechne(ITBotMain inst, ITBotOgamedBridge bridge, ICalculationService calc) {
			var ud = inst.UserData;
			if (ud?.celestials == null || ud.researches == null || ud.serverData == null)
				throw new InvalidOperationException("bot data not loaded yet");
			Einstellung es = LiesEinstellung(inst);
			var ar = inst.InstanceSettings.Brain.AutoRepatriate;

			var celestials = ud.celestials.ToList();
			List<Celestial> ziele = calc.ParseCelestialsList(ar.Target, celestials);
			List<Celestial> ausgenommen = new();
			try { ausgenommen = calc.ParseCelestialsList(ar.Exclude, celestials); } catch (Exception) { }
			bool ohneMonde = false;
			try { ohneMonde = (bool) ar.ExcludeMoons; } catch (Exception) { }
			Celestial basis = ziele.FirstOrDefault();
			if (basis == null)
				throw new InvalidOperationException("no repatriate target set (Brain.AutoRepatriate.Target)");

			long deutLassen = 0; bool nurMonde = false;
			try {
				deutLassen = (long) ar.LeaveDeut.DeutToLeave;
				nurMonde = (bool) ar.LeaveDeut.OnlyOnMoons;
			} catch (Exception) { }

			var typen = new List<Buildables>();
			void AddTyp(string n) { if (!string.IsNullOrWhiteSpace(n) && Enum.TryParse<Buildables>(n, out Buildables b) && !typen.Contains(b)) typen.Add(b); }
			try { if (SettingsService.IsSettingSet(ar, "CargoType")) AddTyp((string) ar.CargoType); } catch (Exception) { }
			try {
				var tr = inst.InstanceSettings.Brain.Transports;
				if (SettingsService.IsSettingSet(tr, "CargoType")) AddTyp((string) tr.CargoType);
				if (SettingsService.IsSettingSet(tr, "CargoFallback"))
					foreach (dynamic f in tr.CargoFallback) AddTyp((string) f);
			} catch (Exception) { }
			if (typen.Count == 0) typen.Add(Buildables.SmallCargo);
			Buildables haupt = typen[0];

			if (ud.slots == null || ud.slots.Total <= 0) {
				try { ud.slots = await bridge.UpdateSlots(); } catch (Exception) { }
			}
			int slotsGesamt = ud.slots?.Total ?? 0, slotsBelegt = ud.slots?.InUse ?? 0, frei = Math.Max(0, slotsGesamt - slotsBelegt);
			int frei2 = 0, maxTransporte = 0;
			try { frei2 = (int) inst.InstanceSettings.General.SlotsToLeaveFree; } catch (Exception) { }
			try { maxTransporte = (int) inst.InstanceSettings.Brain.Transports.MaxSlots; } catch (Exception) { }
			int slotsPlan = Math.Max(0, slotsGesamt - frei2);
			int slotsJetzt = Math.Max(0, frei - frei2);

			foreach (Celestial c in celestials) {
				try {
					await bridge.UpdatePlanet(c, UpdateTypes.Resources);
					await Task.Delay(400);
					await bridge.UpdatePlanet(c, UpdateTypes.Ships);
					await Task.Delay(400);
					if (c is Planet) {
						await bridge.UpdatePlanet(c, UpdateTypes.ResourcesProduction);
						await Task.Delay(400);
					}
					if (c.LFBonuses == null) {
						await bridge.UpdatePlanet(c, UpdateTypes.LFBonuses);
						await Task.Delay(400);
					}
				} catch (Exception e) {
					inst.log(LogLevel.Debug, LogSender.Tbot, $"Logistics planner: refresh of {c} failed: {e.Message}");
				}
			}

			var zeilen = new List<JObject>();
			var phase1Aus = new List<long>();
			var klassisch = new List<long>();
			var reverse = new List<long>();
			long fehlendGesamt = 0, spritPhase1 = 0, spritKlassisch = 0, spritReverse = 0, sammelGesamt = 0;
			int fluege = 0;
			foreach (Celestial c in celestials) {
				var z = new JObject {
					["name"] = c.Name,
					["coord"] = c.Coordinate.ToString(),
					["moon"] = c.Coordinate.Type == Celestials.Moon,
					["status"] = "ok"
				};
				string grund = null;
				if (ziele.Any(t => t.Coordinate.IsSame(c.Coordinate))) grund = "target (base)";
				else if (ausgenommen.Any(x => x.Coordinate.IsSame(c.Coordinate))) grund = "excluded";
				else if (c.Coordinate.Type == Celestials.Moon && ohneMonde) grund = "moons excluded";
				if (grund != null) {
					z["status"] = "skipped";
					z["reason"] = grund;
					zeilen.Add(z);
					continue;
				}
				long m = c.Resources?.Metal ?? 0, k = c.Resources?.Crystal ?? 0, d = c.Resources?.Deuterium ?? 0;
				long pm = 0, pk = 0, pd = 0, sm = 0, sk = 0, sd = 0;
				try {
					var rp = c.ResourcesProduction;
					if (rp != null) {
						pm = rp.Metal?.CurrentProduction ?? 0; pk = rp.Crystal?.CurrentProduction ?? 0; pd = rp.Deuterium?.CurrentProduction ?? 0;
						sm = rp.Metal?.StorageCapacity ?? 0; sk = rp.Crystal?.StorageCapacity ?? 0; sd = rp.Deuterium?.StorageCapacity ?? 0;
					}
				} catch (Exception) { }
				long hm = LogistikRechner.Hochrechnung(m, pm, es.HorizontStunden, sm);
				long hk = LogistikRechner.Hochrechnung(k, pk, es.HorizontStunden, sk);
				long hd = LogistikRechner.Hochrechnung(d, pd, es.HorizontStunden, sd);
				long lassen = (deutLassen > 0 && (!nurMonde || c.Coordinate.Type == Celestials.Moon)) ? deutLassen : 0;
				long jetzt = m + k + Math.Max(0, d - lassen);
				long projiziert = hm + hk + Math.Max(0, hd - lassen);
				long bedarf = (long) Math.Ceiling(projiziert * (1.0 + es.PufferProzent / 100.0));
				z["now"] = new JObject { ["metal"] = m, ["crystal"] = k, ["deuterium"] = d, ["collectable"] = jetzt };
				z["perHour"] = new JObject { ["metal"] = pm, ["crystal"] = pk, ["deuterium"] = pd };
				z["projected"] = projiziert;
				z["need"] = bedarf;

				var da = new JObject(); long hatKap = 0; Ships dort = new();
				foreach (Buildables t in typen) {
					long n = c.Ships?.GetAmount(t) ?? 0;
					if (n <= 0) continue;
					da[t.ToString()] = n;
					hatKap += n * Kapazitaet(inst, calc, c, t);
					dort.Add(t, n);
				}
				z["have"] = da;
				z["haveCapacity"] = hatKap;
				long kapHaupt = Kapazitaet(inst, calc, c, haupt);
				long fehlt = LogistikRechner.FehlendeSchiffe(bedarf, hatKap, kapHaupt);
				z["missing"] = fehlt;
				z["missingType"] = haupt.ToString();
				sammelGesamt += jetzt;
				if (bedarf <= 0 && hatKap <= 0) {
					z["status"] = "nothing";
					zeilen.Add(z);
					continue;
				}
				fehlendGesamt += fehlt;

				if (fehlt > 0) {
					Ships fehlSchiffe = new(); fehlSchiffe.Add(haupt, fehlt);
					var vp = Vorhersage(inst, calc, basis, c.Coordinate, fehlSchiffe);
					z["outSec"] = vp.Time; z["outFuel"] = vp.Fuel;
					phase1Aus.Add(vp.Time); spritPhase1 += vp.Fuel;
				}
				Ships alle = new();
				foreach (Buildables t in typen) { long n = dort.GetAmount(t); if (n > 0) alle.Add(t, n); }
				if (fehlt > 0) alle.Add(haupt, fehlt);
				if (alle.GetAmount(haupt) > 0 || typen.Any(t => alle.GetAmount(t) > 0)) {
					var vk = Vorhersage(inst, calc, c, basis.Coordinate, alle);
					z["homeSec"] = vk.Time; z["homeFuel"] = vk.Fuel;
					klassisch.Add(vk.Time); spritKlassisch += vk.Fuel;
					fluege++;
				}
				long brauchtReverse = kapHaupt > 0 ? (bedarf + kapHaupt - 1) / kapHaupt : 0;
				if (brauchtReverse > 0) {
					Ships rs = new(); rs.Add(haupt, brauchtReverse);
					var hin = Vorhersage(inst, calc, basis, c.Coordinate, rs);
					var zur = Vorhersage(inst, calc, c, basis.Coordinate, rs);
					z["reverseShips"] = brauchtReverse;
					z["reverseSec"] = hin.Time + zur.Time;
					z["reverseFuel"] = hin.Fuel + zur.Fuel;
					reverse.Add(hin.Time + zur.Time); spritReverse += hin.Fuel + zur.Fuel;
				}
				zeilen.Add(z);
			}

			long vorhandenBasis = 0;
			try { vorhandenBasis = basis.Ships?.GetAmount(haupt) ?? 0; } catch (Exception) { }

			var schlaf = new JArray();
			DateTime jetztLokal = DateTime.Now;
			foreach (string block in new[] { "SleepMode", "GhostSleep" }) {
				try {
					var sm2 = block == "SleepMode" ? inst.InstanceSettings.SleepMode : inst.InstanceSettings.GhostSleep;
					if (!SettingsService.IsSettingSet(sm2, "GoToSleep")) continue;
					string zeit = (string) sm2.GoToSleep;
					bool aktiv = SettingsService.IsSettingSet(sm2, "Active") && (bool) sm2.Active;
					DateTime? naechste = LogistikRechner.NaechsteUhrzeit(zeit, jetztLokal);
					if (naechste == null) continue;
					var o = new JObject { ["name"] = block, ["active"] = aktiv, ["goToSleep"] = zeit, ["next"] = naechste.Value.ToString("yyyy-MM-ddTHH:mm:ss") };
					foreach (var (suffix, dauern, slots) in new[] {
						("ClassicPlan", klassisch, slotsPlan), ("ClassicNow", klassisch, slotsJetzt),
						("ReversePlan", reverse, slotsPlan), ("ReverseNow", reverse, slotsJetzt) }) {
						long gesamt = LogistikRechner.Gesamtdauer(dauern, slots);
						DateTime? start = LogistikRechner.SpaetesterStart(naechste.Value, gesamt, es.ReserveMinuten);
						o["total" + suffix] = gesamt;
						o["waves" + suffix] = LogistikRechner.Wellen(dauern.Count, slots);
						o["start" + suffix] = start?.ToString("yyyy-MM-ddTHH:mm:ss");
					}
					schlaf.Add(o);
				} catch (Exception) { }
			}

			var datei = new JObject {
				["updated"] = DateTime.UtcNow,
				["base"] = $"{basis.Name} {basis.Coordinate}",
				["settings"] = new JObject { ["bufferPercent"] = es.PufferProzent, ["horizonHours"] = es.HorizontStunden, ["reserveMinutes"] = es.ReserveMinuten },
				["cargoTypes"] = new JArray(typen.Select(t => t.ToString())),
				["slots"] = new JObject {
					["total"] = slotsGesamt, ["inUse"] = slotsBelegt, ["free"] = frei, ["leaveFree"] = frei2,
					["plan"] = slotsPlan, ["now"] = slotsJetzt, ["transportsMax"] = maxTransporte
				},
				["phase1"] = new JObject {
					["type"] = haupt.ToString(), ["missingTotal"] = fehlendGesamt, ["atBase"] = vorhandenBasis,
					["shortAtBase"] = Math.Max(0, fehlendGesamt - vorhandenBasis),
					["maxOutSec"] = phase1Aus.Count > 0 ? phase1Aus.Max() : 0, ["fuel"] = spritPhase1
				},
				["phase2"] = new JObject {
					["fleets"] = fluege, ["maxHomeSec"] = klassisch.Count > 0 ? klassisch.Max() : 0, ["maxReverseSec"] = reverse.Count > 0 ? reverse.Max() : 0,
					["fuelClassic"] = spritKlassisch, ["fuelReverse"] = spritReverse, ["collectableNow"] = sammelGesamt
				},
				["sleep"] = schlaf,
				["celestials"] = new JArray(zeilen)
			};
			Directory.CreateDirectory(DataDir);
			AtomicFile.WriteAllText(StandPfad(inst), datei.ToString(Formatting.Indented));
			inst.log(LogLevel.Information, LogSender.Tbot, $"Logistics planner: plan written ({zeilen.Count} celestials, {fehlendGesamt} {haupt} missing, {fluege} fleets).");
		}
	}
}
