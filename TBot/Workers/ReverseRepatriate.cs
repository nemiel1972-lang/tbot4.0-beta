using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	// REVERSE-REPATRIATE (12.10.2026): repatriation "the other way round". No ships are kept on the celestials. When a pickup is due,
	// the ships fly from the home celestial (repatriate target) to the celestial by DEPLOY (stationing) and bring generous deuterium
	// for the return leg. On arrival the return leg takes ALL ships lying on that celestial plus the free resources home (again by
	// Deploy). Warships built by the shipyard on site therefore fly home automatically.
	//
	// Settings (all optional; without the block or with Active=false the classic repatriation runs unchanged):
	//   Brain.AutoRepatriate.Reverse {
	//     "Active": false,
	//     "ReturnFuelFactor": 2.0,        // deuterium for the return leg = expected fuel of the return fleet x factor (the rest stays there or flies back)
	//     "ReturnAllShips": true,         // true = take every movable ship of the celestial, false = only the transport chain types
	//     "KeepShips": [],                // ship types that stay on the celestial (e.g. ["Pathfinder"])
	//     "PendingTtlMinutes": 120,       // how long a started pickup is tracked before it is dropped
	//     "OutboundCapacityFactor": 1.1   // cargo space on the outbound leg = free amount x factor (production during the flight)
	//   }
	// There is NO slot reservation: only real fleets (outbound / return leg) occupy slots. The return leg starts right on arrival
	// (the worker wakes up at the arrival time); if there is no free slot then, it is retried via force (SlotsToLeaveFree) or in the
	// next cycle - the ships and the deuterium simply stay on the celestial until then.
	public enum ReverseStatus {
		// Outbound leg or (direct) return leg was sent.
		Gesendet,
		// No free slots / sleep time - try again later.
		Wartet,
		// Nothing on the celestial that could fly home.
		KeineSchiffe,
		// Could not be planned (no suitable ships at the home celestial, calculation failed) - classic path.
		NichtMoeglich
	}

	// Connections to FleetScheduler (keeps this file independent of its private helpers).
	public class ReverseHaken {
		// Transport chain of the Brain deliveries (configured type + Brain.Transports.CargoFallback).
		public Func<Buildables, List<Buildables>> Kette;
		// Ship selection for a cargo at the home celestial (chain / mixed fleet / expedition reserve); null = not possible.
		public Func<Celestial, Resources, Buildables, Ships> HinflugSchiffe;
		// FleetScheduler.SendFleet (origin, ships, destination, mission, speed, payload, force).
		public Func<Celestial, Ships, Coordinate, Missions, decimal, Resources, bool, Task<int>> Senden;
	}

	// A started pickup that waits for the arrival of the outbound fleet (saved after every outbound leg).
	public class ReverseEintrag {
		public long CelestialId { get; set; }
		public long HeimatId { get; set; }
		public int FlottenId { get; set; }
		public DateTime Gesendet { get; set; }
		public DateTime Ankunft { get; set; }
		public int Versuche { get; set; }
	}

	public class ReverseKonfig {
		public bool Aktiv;
		public double SpritFaktor = 2.0;
		public bool AlleSchiffe = true;
		public HashSet<Buildables> Behalten = new();
		public int TtlMinuten = 120;
		public double KapazitaetsFaktor = 1.1;
	}

	public static class ReverseRepatriate {
		private static readonly object _lock = new();
		private static List<ReverseEintrag> _eintraege = null;
		private static string _pfad = null;

		// ------------------------------------------------------------------------------------------------------------------
		// Settings
		// ------------------------------------------------------------------------------------------------------------------

		public static ReverseKonfig LiesKonfig(ITBotMain inst) {
			ReverseKonfig k = new();
			try {
				var ar = inst.InstanceSettings.Brain.AutoRepatriate;
				if (!SettingsService.IsSettingSet(ar, "Reverse"))
					return k;
				var r = ar.Reverse;
				if (SettingsService.IsSettingSet(r, "Active"))
					k.Aktiv = (bool) r.Active;
				if (SettingsService.IsSettingSet(r, "ReturnFuelFactor"))
					k.SpritFaktor = Math.Max(1.0, (double) r.ReturnFuelFactor);
				if (SettingsService.IsSettingSet(r, "ReturnAllShips"))
					k.AlleSchiffe = (bool) r.ReturnAllShips;
				if (SettingsService.IsSettingSet(r, "PendingTtlMinutes"))
					k.TtlMinuten = Math.Max(5, (int) r.PendingTtlMinutes);
				if (SettingsService.IsSettingSet(r, "OutboundCapacityFactor"))
					k.KapazitaetsFaktor = Math.Max(1.0, (double) r.OutboundCapacityFactor);
				if (SettingsService.IsSettingSet(r, "KeepShips")) {
					foreach (var eintrag in r.KeepShips) {
						if (Enum.TryParse<Buildables>((string) eintrag, true, out Buildables typ))
							k.Behalten.Add(typ);
					}
				}
			} catch (Exception) {
				k.Aktiv = false;
			}
			return k;
		}

		public static bool Aktiv(ITBotMain inst) {
			return LiesKonfig(inst).Aktiv;
		}

		// ------------------------------------------------------------------------------------------------------------------
		// State (only touched while the feature is on, otherwise no file is ever read or written)
		// ------------------------------------------------------------------------------------------------------------------

		private static string Pfad(ITBotMain inst) {
			string spieler = inst?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universum = inst?.UserData?.serverData?.Name ?? "unknown";
			string dir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dir);
			return Path.Combine(dir, $"reverse_repatriate_{spieler}-{universum}.json");
		}

		private static List<ReverseEintrag> Lade(ITBotMain inst) {
			string pfad = Pfad(inst);
			lock (_lock) {
				if (_eintraege == null || _pfad != pfad) {
					_pfad = pfad;
					try {
						_eintraege = File.Exists(pfad) ? (JsonSerializer.Deserialize<List<ReverseEintrag>>(File.ReadAllText(pfad)) ?? new List<ReverseEintrag>()) : new List<ReverseEintrag>();
					} catch (Exception) {
						_eintraege = new List<ReverseEintrag>();
					}
				}
				return _eintraege;
			}
		}

		private static void Speichere(ITBotMain inst) {
			try {
				lock (_lock) {
					string tmp = _pfad + ".tmp";
					File.WriteAllText(tmp, JsonSerializer.Serialize(_eintraege, new JsonSerializerOptions { WriteIndented = true }));
					File.Copy(tmp, _pfad, true);
					File.Delete(tmp);
				}
			} catch (Exception) {
			}
		}

		private static List<ReverseEintrag> Schnappschuss(ITBotMain inst) {
			var liste = Lade(inst);
			lock (_lock) {
				return liste.ToList();
			}
		}

		private static void Entferne(ITBotMain inst, ReverseEintrag e) {
			var liste = Lade(inst);
			lock (_lock) {
				liste.RemoveAll(x => x.CelestialId == e.CelestialId && x.FlottenId == e.FlottenId);
			}
			Speichere(inst);
		}

		// true if a pickup for this celestial is already running (then do not start a new one).
		public static bool HatOffene(ITBotMain inst, Celestial c) {
			try {
				return Schnappschuss(inst).Any(e => e.CelestialId == c.ID);
			} catch (Exception) {
				return false;
			}
		}

		// Seconds until the next arrival of an outbound fleet (wake-up time for the worker); -1 = nothing pending.
		// If the arrival is already over and the pickup is still pending, 20 is returned (retry every 20 s until the TTL ends).
		public static long SekundenBisNaechsteAnkunft(ITBotMain inst) {
			try {
				if (!Aktiv(inst))
					return -1;
				return NaechsteAnkunftSekunden(Schnappschuss(inst).Select(e => e.Ankunft).ToList(), DateTime.UtcNow);
			} catch (Exception) {
				return -1;
			}
		}

		// Pure calculation (tested).
		internal static long NaechsteAnkunftSekunden(IList<DateTime> ankuenfte, DateTime jetzt) {
			if (ankuenfte == null || ankuenfte.Count == 0)
				return -1;
			long best = long.MaxValue;
			foreach (DateTime a in ankuenfte) {
				long s = (long) Math.Ceiling((a - jetzt).TotalSeconds);
				if (s <= 0)
					s = 20;
				if (s < best)
					best = s;
			}
			return best;
		}

		// ------------------------------------------------------------------------------------------------------------------
		// Pure calculation logic (no game access, tested)
		// ------------------------------------------------------------------------------------------------------------------

		// Cargo space the outbound leg additionally needs on top of the transporters already lying on the celestial.
		internal static long FehlendeKapazitaet(long ladung, double faktor, long vorhandeneKapazitaet) {
			if (ladung <= 0)
				return 0;
			// decimal instead of double: 168 M x 1.1 must not round up to 184,800,001 because of floating point noise.
			long noetig = (long) Math.Ceiling((decimal) ladung * (decimal) Math.Max(1.0, faktor));
			return Math.Max(0L, noetig - Math.Max(0L, vorhandeneKapazitaet));
		}

		// Deuterium the outbound leg carries for the return leg: fuel need x factor, limited by cargo space and by the free deuterium
		// at the home celestial (after the fuel of the outbound leg).
		internal static long SpritZuladung(long spritRueckflug, double faktor, long laderaum, long heimatFrei) {
			if (spritRueckflug <= 0)
				return 0;
			long wunsch = (long) Math.Ceiling((decimal) spritRueckflug * (decimal) Math.Max(1.0, faktor));
			return Math.Max(0L, Math.Min(wunsch, Math.Min(Math.Max(0L, laderaum), Math.Max(0L, heimatFrei))));
		}

		// Trims the return fleet until the fuel is enough. Order: first non-transporters with the highest fuel need per ship (first
		// dropped completely, then - once a subset fits - only as many as fit), then the transporters proportionally.
		// Transporters always fly along so that the resources get home. null = nothing feasible.
		internal static Dictionary<Buildables, long> KuerzeAufSprit(Dictionary<Buildables, long> alle, ISet<Buildables> transporter, long budget, Func<Dictionary<Buildables, long>, long> spritFuer, Func<Buildables, long> einzelSprit) {
			if (alle == null || alle.Count == 0)
				return null;
			var aktuell = alle.Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
			if (aktuell.Count == 0)
				return null;
			if (spritFuer(aktuell) <= budget)
				return aktuell;

			foreach (Buildables typ in aktuell.Keys.Where(t => !transporter.Contains(t)).OrderByDescending(t => einzelSprit(t)).ToList()) {
				long original = aktuell[typ];
				aktuell.Remove(typ);
				if (aktuell.Count == 0)
					continue;
				if (spritFuer(aktuell) <= budget) {
					// Fits with this type dropped completely: take back as many of it as still fit.
					long lo = 0, hi = original;
					while (lo < hi) {
						long mid = (lo + hi + 1) / 2;
						var test = new Dictionary<Buildables, long>(aktuell) { [typ] = mid };
						if (spritFuer(test) <= budget)
							lo = mid;
						else
							hi = mid - 1;
					}
					if (lo > 0)
						aktuell[typ] = lo;
					return aktuell;
				}
			}

			// Only transporters left: scale them down proportionally.
			var basis = aktuell.Where(p => transporter.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
			if (basis.Count == 0)
				return null;
			Dictionary<Buildables, long> best = null;
			double unten = 0.0, oben = 1.0;
			for (int i = 0; i < 30; i++) {
				double mitte = (unten + oben) / 2.0;
				var kandidat = basis.Select(p => new KeyValuePair<Buildables, long>(p.Key, (long) Math.Floor(p.Value * mitte))).Where(p => p.Value > 0).ToDictionary(p => p.Key, p => p.Value);
				if (kandidat.Count > 0 && spritFuer(kandidat) <= budget) {
					best = kandidat;
					unten = mitte;
				} else {
					oben = mitte;
				}
			}
			return best;
		}

		// ------------------------------------------------------------------------------------------------------------------
		// Helpers with game access
		// ------------------------------------------------------------------------------------------------------------------

		private static Dictionary<Buildables, long> Zaehle(Ships s) {
			Dictionary<Buildables, long> d = new();
			if (s == null)
				return d;
			foreach (Buildables typ in Enum.GetValues(typeof(Buildables))) {
				try {
					long n = s.GetAmount(typ);
					if (n > 0)
						d[typ] = n;
				} catch (Exception) {
				}
			}
			return d;
		}

		private static Ships ZuSchiffen(Dictionary<Buildables, long> d) {
			Ships s = new();
			foreach (var p in d)
				if (p.Value > 0)
					s.Add(p.Key, p.Value);
			return s;
		}

		private static long Kapazitaet(ITBotMain inst, ICalculationService calc, Celestial c, Ships s) {
			try {
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

		// Payload as in the classic repatriation: live stock minus LeaveDeut, limited to the free ledger share.
		private static Resources BerechneNutzlast(ITBotMain inst, Celestial c) {
			Resources p = new Resources(c.Resources.Metal, c.Resources.Crystal, c.Resources.Deuterium);
			p.Food = 0;
			try {
				var leave = inst.InstanceSettings.Brain.AutoRepatriate.LeaveDeut;
				long lassen = (long) leave.DeutToLeave;
				if (lassen > 0) {
					bool nurMonde = (bool) leave.OnlyOnMoons;
					if (!nurMonde || c.Coordinate.Type == Celestials.Moon)
						p.Deuterium = Math.Max(0L, p.Deuterium - lassen);
				}
			} catch (Exception) {
			}
			try {
				Resources frei = LedgerTBot.FreeForExternal(inst, c, out string gehalten);
				if (frei != null) {
					p = new Resources(Math.Min(p.Metal, frei.Metal), Math.Min(p.Crystal, frei.Crystal), Math.Min(p.Deuterium, frei.Deuterium));
					p.Food = 0;
				}
			} catch (Exception) {
			}
			return p;
		}

		// Deuterium that may be planned for the flight (free in the ledger's sense).
		private static long FreiesDeuterium(ITBotMain inst, Celestial c) {
			long live = c.Resources.Deuterium;
			try {
				Resources frei = LedgerTBot.FreeForExternal(inst, c, out string gehalten);
				if (frei != null)
					return Math.Min(live, frei.Deuterium);
			} catch (Exception) {
			}
			return live;
		}

		private static Celestial FindeCelestial(ITBotMain inst, long id) {
			return inst.UserData.celestials.ToList().FirstOrDefault(x => x.ID == id);
		}

		private static void Log(ITBotMain inst, LogLevel stufe, string text) {
			inst.log(stufe, LogSender.FleetScheduler, "Reverse-Repatriate: " + text);
		}

		// Transport limit as in the classic repatriation: Brain.Transports.MaxSlots minus AutoRepatriate.ReserveTransportSlots.
		// Counted are the account's transport fleets plus the outbound legs of this feature that are still flying.
		private static bool TransportLimitErreicht(ITBotMain inst, out string text) {
			text = "";
			try {
				int max = 0;
				if (SettingsService.IsSettingSet(inst.InstanceSettings.Brain, "Transports") && SettingsService.IsSettingSet(inst.InstanceSettings.Brain.Transports, "MaxSlots"))
					max = (int) inst.InstanceSettings.Brain.Transports.MaxSlots;
				if (max <= 0)
					return false;
				int reserve = 0;
				if (SettingsService.IsSettingSet(inst.InstanceSettings.Brain.AutoRepatriate, "ReserveTransportSlots"))
					reserve = (int) inst.InstanceSettings.Brain.AutoRepatriate.ReserveTransportSlots;
				var flotten = inst.UserData.fleets.ToList();
				int laufend = flotten.Count(f => f.Mission == Missions.Transport);
				var ids = new HashSet<int>(flotten.Select(f => f.ID));
				laufend += Schnappschuss(inst).Count(e => ids.Contains(e.FlottenId));
				if (laufend >= max - reserve) {
					text = $"transport limit reached ({laufend}/{max}, reserved {reserve})";
					return true;
				}
			} catch (Exception) {
			}
			return false;
		}

		// ------------------------------------------------------------------------------------------------------------------
		// Return leg: all ships + free stock from the celestial to the home celestial (Deploy)
		// ------------------------------------------------------------------------------------------------------------------

		public static async Task<ReverseStatus> RueckflugAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, ReverseKonfig k, Celestial c, Celestial heimat, Buildables bevorzugt, bool aktualisieren = true) {
			try {
				// aktualisieren=false: the caller has just loaded stock, ships and LF bonuses (saves API calls).
				if (aktualisieren) {
					c = await bridge.UpdatePlanet(c, UpdateTypes.Resources);
					c = await bridge.UpdatePlanet(c, UpdateTypes.Ships);
					c = await bridge.UpdatePlanet(c, UpdateTypes.LFBonuses);
				}

				HashSet<Buildables> transporter = new(h.Kette(bevorzugt));
				Dictionary<Buildables, long> alle = Zaehle(c.Ships.GetMovableShips());
				foreach (Buildables behalten in k.Behalten)
					alle.Remove(behalten);
				if (!k.AlleSchiffe) {
					foreach (Buildables typ in alle.Keys.Where(t => !transporter.Contains(t)).ToList())
						alle.Remove(typ);
				}
				if (alle.Count == 0) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: no ships to fly home.");
					return ReverseStatus.KeineSchiffe;
				}

				Resources payload = BerechneNutzlast(inst, c);
				long budget = FreiesDeuterium(inst, c);

				// Single-ship fuel is only used for the order when trimming (types with a high consumption are dropped first).
				Func<Buildables, long> einzel = t => {
					try {
						Ships eins = new();
						eins.Add(t, 1);
						return (long) Vorhersage(inst, calc, c, heimat.Coordinate, eins).Fuel;
					} catch (Exception) {
						return 0;
					}
				};
				Func<Dictionary<Buildables, long>, long> spritFuer = d => (long) Vorhersage(inst, calc, c, heimat.Coordinate, ZuSchiffen(d)).Fuel;

				var gewaehlt = KuerzeAufSprit(alle, transporter, budget, spritFuer, einzel);
				if (gewaehlt == null) {
					Log(inst, LogLevel.Warning, $"{c.ToString()}: not enough deuterium for the return leg (free {budget:N0}, needed {spritFuer(alle):N0}) - ships stay, next attempt follows.");
					return ReverseStatus.Wartet;
				}
				if (gewaehlt.Count != alle.Count || gewaehlt.Any(p => alle[p.Key] != p.Value))
					Log(inst, LogLevel.Information, $"{c.ToString()}: return fleet trimmed for fuel to {string.Join(", ", gewaehlt.Select(p => p.Value.ToString("N0") + " " + p.Key))} (free deuterium {budget:N0}).");

				Ships schiffe = ZuSchiffen(gewaehlt);
				long sprit = (long) Vorhersage(inst, calc, c, heimat.Coordinate, schiffe).Fuel;
				payload.Deuterium = Math.Min(payload.Deuterium, Math.Max(0L, budget - sprit));
				// Limit the cargo to the cargo space of the return fleet.
				payload = calc.CalcMaxTransportableResources(schiffe, payload, inst.UserData.researches.HyperspaceTechnology, inst.UserData.serverData, c.LFBonuses, inst.UserData.userInfo.Class, inst.UserData.serverData.ProbeCargo);

				int id = await h.Senden(c, schiffe, heimat.Coordinate, Missions.Deploy, Speeds.HundredPercent, payload, false);
				// No free slot besides the one SlotsToLeaveFree keeps free: that is what force is for.
				if (id == (int) SendFleetCode.NotEnoughSlots && inst.UserData.slots != null && inst.UserData.slots.Free > 0)
					id = await h.Senden(c, schiffe, heimat.Coordinate, Missions.Deploy, Speeds.HundredPercent, payload, true);
				if (id > 0) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: return leg {string.Join(", ", gewaehlt.Select(p => p.Value.ToString("N0") + " " + p.Key))} with {payload.TotalResources:N0} resources (M {payload.Metal:N0}, C {payload.Crystal:N0}, D {payload.Deuterium:N0}) to {heimat.ToString()} started.");
					return ReverseStatus.Gesendet;
				}
				if (id == (int) SendFleetCode.NotEnoughSlots || id == (int) SendFleetCode.AfterSleepTime) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: return leg waits ({(id == (int) SendFleetCode.NotEnoughSlots ? "no free slot" : "sleep time")}).");
					return ReverseStatus.Wartet;
				}
				Log(inst, LogLevel.Warning, $"{c.ToString()}: return leg failed (code {id}) - see the preceding 'Unable to send fleet' line for the reason.");
				return ReverseStatus.NichtMoeglich;
			} catch (Exception e) {
				Log(inst, LogLevel.Warning, $"Return leg from {c?.ToString()} failed: {e.Message}");
				return ReverseStatus.NichtMoeglich;
			}
		}

		// ------------------------------------------------------------------------------------------------------------------
		// Start of a pickup (from the repatriation loop): outbound leg, or the return leg directly if enough transporters lie there
		// ------------------------------------------------------------------------------------------------------------------

		public static async Task<ReverseStatus> StarteAbholungAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, Celestial c, Coordinate heimatKoordinate, Resources payload, Buildables bevorzugt) {
			try {
				ReverseKonfig k = LiesKonfig(inst);
				if (!k.Aktiv)
					return ReverseStatus.NichtMoeglich;
				Celestial heimat = inst.UserData.celestials.ToList().FirstOrDefault(x => x.Coordinate.IsSame(heimatKoordinate));
				if (heimat == null)
					return ReverseStatus.NichtMoeglich;

				// Transporters already lying there count (e.g. leftovers of a delayed return leg). Stock and ships were just loaded
				// by the repatriation loop, only the LF bonuses (cargo space) are missing.
				c = await bridge.UpdatePlanet(c, UpdateTypes.LFBonuses);
				List<Buildables> kette = h.Kette(bevorzugt);
				Ships dort = new();
				foreach (Buildables t in kette) {
					long n = c.Ships.GetAmount(t);
					if (n > 0)
						dort.Add(t, n);
				}
				long kapDort = dort.HasMovableFleet() ? Kapazitaet(inst, calc, c, dort) : 0;
				long gesamt = payload.Metal + payload.Crystal + payload.Deuterium;
				long fehlt = FehlendeKapazitaet(gesamt, k.KapazitaetsFaktor, kapDort);

				if (fehlt <= 0) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: enough transporters on site (cargo space {kapDort:N0} for {gesamt:N0}) - return leg without outbound leg.");
					return await RueckflugAsync(inst, calc, bridge, h, k, c, heimat, bevorzugt, false);
				}

				if (TransportLimitErreicht(inst, out string limitText)) {
					Log(inst, LogLevel.Information, $"{limitText} while planning {c.ToString()} - cycle is delayed.");
					return ReverseStatus.Wartet;
				}

				heimat = await bridge.UpdatePlanet(heimat, UpdateTypes.Resources);
				heimat = await bridge.UpdatePlanet(heimat, UpdateTypes.Ships);
				heimat = await bridge.UpdatePlanet(heimat, UpdateTypes.LFBonuses);

				Ships hin = h.HinflugSchiffe(heimat, new Resources(fehlt, 0, 0), bevorzugt);
				if (hin == null || !hin.HasMovableFleet()) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: not enough transporters at the home celestial {heimat.ToString()} for {fehlt:N0} cargo space - classic path.");
					return ReverseStatus.NichtMoeglich;
				}

				// Expected return fleet: what lies there (without KeepShips) plus the outbound ships. The total consumption counts for the fuel.
				var erwartet = Zaehle(c.Ships.GetMovableShips());
				foreach (var p in Zaehle(hin))
					erwartet[p.Key] = (erwartet.ContainsKey(p.Key) ? erwartet[p.Key] : 0) + p.Value;
				foreach (Buildables behalten in k.Behalten)
					erwartet.Remove(behalten);
				if (!k.AlleSchiffe) {
					var tr = new HashSet<Buildables>(kette);
					foreach (Buildables typ in erwartet.Keys.Where(t => !tr.Contains(t)).ToList())
						erwartet.Remove(typ);
				}
				long spritRueck = (long) Vorhersage(inst, calc, c, heimat.Coordinate, ZuSchiffen(erwartet)).Fuel;

				long spritHin = (long) Vorhersage(inst, calc, heimat, c.Coordinate, hin).Fuel;
				long laderaumHin = Kapazitaet(inst, calc, heimat, hin);
				long heimatFrei = FreiesDeuterium(inst, heimat) - spritHin;
				long zuladung = SpritZuladung(spritRueck, k.SpritFaktor, laderaumHin, heimatFrei);
				Resources fracht = new Resources(0, 0, zuladung);

				int id = await h.Senden(heimat, hin, c.Coordinate, Missions.Deploy, Speeds.HundredPercent, fracht, false);
				if (id == (int) SendFleetCode.NotEnoughSlots || id == (int) SendFleetCode.AfterSleepTime) {
					Log(inst, LogLevel.Information, $"Outbound leg to {c.ToString()} waits ({(id == (int) SendFleetCode.NotEnoughSlots ? "no free slot" : "sleep time")}).");
					return ReverseStatus.Wartet;
				}
				if (id <= 0) {
					Log(inst, LogLevel.Warning, $"Outbound leg to {c.ToString()} failed (code {id}) - classic path.");
					return ReverseStatus.NichtMoeglich;
				}

				long sekunden = (long) Vorhersage(inst, calc, heimat, c.Coordinate, hin).Time;
				try {
					Fleet f = inst.UserData.fleets.ToList().FirstOrDefault(x => x.ID == id);
					long laut = f != null ? Convert.ToInt64((object) f.ArriveIn) : 0;
					if (laut > 0)
						sekunden = laut;
				} catch (Exception) {
				}
				ReverseEintrag eintrag = new ReverseEintrag {
					CelestialId = c.ID,
					HeimatId = heimat.ID,
					FlottenId = id,
					Gesendet = DateTime.UtcNow,
					Ankunft = DateTime.UtcNow.AddSeconds(sekunden),
					Versuche = 0
				};
				var liste = Lade(inst);
				lock (_lock) {
					liste.Add(eintrag);
				}
				Speichere(inst);
				Log(inst, LogLevel.Information, $"Outbound leg {hin} (+{zuladung:N0} deuterium for the return leg, estimated need {spritRueck:N0}) to {c.ToString()} started, arrival in {sekunden}s - return leg follows on arrival.");
				return ReverseStatus.Gesendet;
			} catch (Exception e) {
				Log(inst, LogLevel.Warning, $"Starting the pickup from {c?.ToString()} failed: {e.Message} - classic path.");
				return ReverseStatus.NichtMoeglich;
			}
		}

		// ------------------------------------------------------------------------------------------------------------------
		// Process started pickups (at the beginning of every repatriation cycle, the fleet list is fresh)
		// ------------------------------------------------------------------------------------------------------------------

		public static async Task VerarbeiteOffeneAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, Buildables bevorzugt) {
			try {
				ReverseKonfig k = LiesKonfig(inst);
				if (!k.Aktiv)
					return;
				DateTime jetzt = DateTime.UtcNow;
				var flottenIds = new HashSet<int>(inst.UserData.fleets.ToList().Select(f => f.ID));
				foreach (ReverseEintrag e in Schnappschuss(inst)) {
					if (jetzt > e.Gesendet.AddMinutes(k.TtlMinuten)) {
						Log(inst, LogLevel.Warning, $"Pickup (celestial {e.CelestialId}, fleet {e.FlottenId}) dropped after {k.TtlMinuten} min. If ships still lie there, the next repatriation picks them up.");
						Entferne(inst, e);
						continue;
					}
					// Outbound fleet still flying: wait (no slot is reserved, nothing to do).
					if (flottenIds.Contains(e.FlottenId) && jetzt < e.Ankunft.AddMinutes(10))
						continue;

					Celestial c = FindeCelestial(inst, e.CelestialId);
					Celestial heimat = FindeCelestial(inst, e.HeimatId);
					if (c == null || heimat == null) {
						Entferne(inst, e);
						continue;
					}
					ReverseStatus st = await RueckflugAsync(inst, calc, bridge, h, k, c, heimat, bevorzugt);
					if (st == ReverseStatus.Gesendet) {
						Entferne(inst, e);
					} else if (st == ReverseStatus.Wartet) {
						// stays pending, next attempt in the next cycle
					} else {
						e.Versuche++;
						if (e.Versuche >= 5 || (st == ReverseStatus.KeineSchiffe && e.Versuche >= 3)) {
							Log(inst, LogLevel.Warning, $"Pickup (celestial {e.CelestialId}) dropped after {e.Versuche} attempts ({st}).");
							Entferne(inst, e);
						} else {
							Speichere(inst);
						}
					}
				}
			} catch (Exception ex) {
				Log(inst, LogLevel.Warning, $"Processing pending pickups failed: {ex.Message}");
			}
		}
	}
}
