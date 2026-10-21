using Tbot.Common.IO;
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
	public enum ReverseStatus {
		Gesendet,
		Wartet,
		KeineSchiffe,
		NichtMoeglich
	}

	public class ReverseHaken {
		public Func<Buildables, List<Buildables>> Kette;
		public Func<Celestial, Resources, Buildables, Ships> HinflugSchiffe;
		public Func<Celestial, Ships, Coordinate, Missions, decimal, Resources, bool, Task<int>> Senden;
	}

	public class ReverseEintrag {
		public long CelestialId { get; set; }
		public long HeimatId { get; set; }
		public int FlottenId { get; set; }
		public DateTime Gesendet { get; set; }
		public DateTime Ankunft { get; set; }
		public int Versuche { get; set; }
		public Dictionary<string, long> Schiffe { get; set; }
	}

	public class ReverseKonfig {
		public bool Aktiv;
		public double SpritFaktor = 1.1;
		public int TtlMinuten = 120;
		public double KapazitaetsFaktor = 1.1;
	}

	public static class ReverseRepatriate {
		private static readonly object _lock = new();
		private static List<ReverseEintrag> _eintraege = null;
		private static string _pfad = null;


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
				if (SettingsService.IsSettingSet(r, "PendingTtlMinutes"))
					k.TtlMinuten = Math.Max(5, (int) r.PendingTtlMinutes);

			} catch (Exception ex) {
				k.Aktiv = false;
				inst?.log(LogLevel.Error, LogSender.FleetScheduler,
					$"Reverse-Repatriate: config read failed; feature disabled for this cycle. Exception: {ex}");
			}
			inst?.log(LogLevel.Debug, LogSender.FleetScheduler,
				$"Reverse-Repatriate: config active={k.Aktiv}, fuelFactor={k.SpritFaktor}, capacityFactor={k.KapazitaetsFaktor}, ttl={k.TtlMinuten}m.");
			return k;
		}

		public static bool Aktiv(ITBotMain inst) {
			return LiesKonfig(inst).Aktiv;
		}


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
					} catch (Exception ex) {
						inst?.log(LogLevel.Warning, LogSender.FleetScheduler,
							$"Reverse-Repatriate: loading pending file '{pfad}' failed: {ex}");
						_eintraege = new List<ReverseEintrag>();
					}
				}
				return _eintraege;
			}
		}

		private static void Speichere(ITBotMain inst) {
			try {
				lock (_lock) {
					AtomicFile.WriteAllText(_pfad, JsonSerializer.Serialize(_eintraege, new JsonSerializerOptions { WriteIndented = true }));
				}
			} catch (Exception ex) {
				inst?.log(LogLevel.Error, LogSender.FleetScheduler,
					$"Reverse-Repatriate: saving pending file '{_pfad ?? "(unset)"}' failed: {ex}");
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

		public static bool HatOffene(ITBotMain inst, Celestial c) {
			try {
				return Schnappschuss(inst).Any(e => e.CelestialId == c.ID);
			} catch (Exception) {
				return false;
			}
		}

		public static long SekundenBisNaechsteAnkunft(ITBotMain inst) {
			try {
				if (!Aktiv(inst))
					return -1;
				return NaechsteAnkunftSekunden(Schnappschuss(inst).Select(e => e.Ankunft).ToList(), DateTime.UtcNow);
			} catch (Exception) {
				return -1;
			}
		}

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


		internal static long FehlendeKapazitaet(long ladung, double faktor, long vorhandeneKapazitaet) {
			if (ladung <= 0)
				return 0;
			long noetig = (long) Math.Ceiling((decimal) ladung * (decimal) Math.Max(1.0, faktor));
			return Math.Max(0L, noetig - Math.Max(0L, vorhandeneKapazitaet));
		}

		internal static long SpritZuladung(long spritRueckflug, double faktor, long laderaum, long heimatFrei) {
			if (spritRueckflug <= 0)
				return 0;
			long wunsch = (long) Math.Ceiling((decimal) spritRueckflug * (decimal) Math.Max(1.0, faktor));
			return Math.Max(0L, Math.Min(wunsch, Math.Min(Math.Max(0L, laderaum), Math.Max(0L, heimatFrei))));
		}


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


		public static async Task<ReverseStatus> RueckflugAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, ReverseKonfig k, Celestial c, Celestial heimat, Buildables bevorzugt, bool aktualisieren = true, Dictionary<string, long> vorgabe = null) {
			try {
				if (aktualisieren) {
					c = await bridge.UpdatePlanet(c, UpdateTypes.Resources);
					c = await bridge.UpdatePlanet(c, UpdateTypes.Ships);
					c = await bridge.UpdatePlanet(c, UpdateTypes.LFBonuses);
				}

				Dictionary<Buildables, long> vorhanden = Zaehle(c.Ships.GetMovableShips());
				Dictionary<Buildables, long> alle = new();
				if (vorgabe != null && vorgabe.Count > 0) {
					foreach (var p in vorgabe) {
						if (Enum.TryParse<Buildables>(p.Key, out Buildables typ) && vorhanden.TryGetValue(typ, out long da) && da > 0)
							alle[typ] = Math.Min(p.Value, da);
					}
				} else {
					HashSet<Buildables> transporter = new(h.Kette(bevorzugt));
					foreach (var p in vorhanden.Where(p => transporter.Contains(p.Key)))
						alle[p.Key] = p.Value;
				}
				if (alle.Count == 0) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: none of the ships that flew out lie there - nothing to fly home.");
					return ReverseStatus.KeineSchiffe;
				}

				Resources payload = BerechneNutzlast(inst, c);
				long budget = FreiesDeuterium(inst, c);
				Log(inst, LogLevel.Debug, $"{c}: return planning: ships=[{string.Join(", ", alle.Select(p => $"{p.Value} {p.Key}"))}], payload M={payload.Metal:N0}/C={payload.Crystal:N0}/D={payload.Deuterium:N0}, freeDeut={budget:N0}, home={heimat}.");

				Ships schiffe = ZuSchiffen(alle);
				long sprit = (long) Vorhersage(inst, calc, c, heimat.Coordinate, schiffe).Fuel;
				if (sprit > budget) {
					Log(inst, LogLevel.Warning, $"{c.ToString()}: not enough deuterium for the return leg (free {budget:N0}, needed {sprit:N0}) - ships stay, next attempt follows.");
					return ReverseStatus.Wartet;
				}
				var gewaehlt = alle;
				payload.Deuterium = Math.Min(payload.Deuterium, Math.Max(0L, budget - sprit));
				payload = calc.CalcMaxTransportableResources(schiffe, payload, inst.UserData.researches.HyperspaceTechnology, inst.UserData.serverData, c.LFBonuses, inst.UserData.userInfo.Class, inst.UserData.serverData.ProbeCargo);

				Log(inst, LogLevel.Debug, $"{c}: sending return leg to {heimat}: ships=[{string.Join(", ", gewaehlt.Select(p => $"{p.Value} {p.Key}"))}], fuel={sprit:N0}, cargo M={payload.Metal:N0}/C={payload.Crystal:N0}/D={payload.Deuterium:N0}.");
				int id = await h.Senden(c, schiffe, heimat.Coordinate, Missions.Deploy, Speeds.HundredPercent, payload, false);
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
				Log(inst, LogLevel.Warning, $"Return leg from {c?.ToString()} failed: {e}");
				return ReverseStatus.NichtMoeglich;
			}
		}


		public static async Task<ReverseStatus> StarteAbholungAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, Celestial c, Coordinate heimatKoordinate, Resources payload, Buildables bevorzugt) {
			try {
				ReverseKonfig k = LiesKonfig(inst);
				if (!k.Aktiv) {
					Log(inst, LogLevel.Debug, $"{c}: reverse pickup skipped because Reverse.Active is false.");
					return ReverseStatus.NichtMoeglich;
				}
				Log(inst, LogLevel.Debug, $"{c}: reverse pickup requested; payload M={payload?.Metal ?? 0:N0}/C={payload?.Crystal ?? 0:N0}/D={payload?.Deuterium ?? 0:N0}, preferred ship={bevorzugt}, home coordinate={heimatKoordinate}.");
				Celestial heimat = inst.UserData.celestials.ToList().FirstOrDefault(x => x.Coordinate.IsSame(heimatKoordinate));
				if (heimat == null) {
					Log(inst, LogLevel.Warning, $"{c}: reverse pickup cannot resolve home celestial at {heimatKoordinate}; falling back to classic path.");
					return ReverseStatus.NichtMoeglich;
				}

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
				Log(inst, LogLevel.Debug, $"{c}: capacity analysis: payload={gesamt:N0}, local transporter capacity={kapDort:N0}, factor={k.KapazitaetsFaktor}, missing capacity={fehlt:N0}, home={heimat}.");

				if (fehlt <= 0) {
					Log(inst, LogLevel.Information, $"{c.ToString()}: enough transporters on site (cargo space {kapDort:N0} for {gesamt:N0}) - return leg without outbound leg.");
					return await RueckflugAsync(inst, calc, bridge, h, k, c, heimat, bevorzugt, false, Zaehle(dort).ToDictionary(p => p.Key.ToString(), p => p.Value));
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

				var erwartet = Zaehle(hin);
				long spritRueck = (long) Vorhersage(inst, calc, c, heimat.Coordinate, ZuSchiffen(erwartet)).Fuel;

				long spritHin = (long) Vorhersage(inst, calc, heimat, c.Coordinate, hin).Fuel;
				long laderaumHin = Kapazitaet(inst, calc, heimat, hin);
				long heimatFrei = FreiesDeuterium(inst, heimat) - spritHin;
				long zuladung = SpritZuladung(spritRueck, k.SpritFaktor, laderaumHin, heimatFrei);
				Resources fracht = new Resources(0, 0, zuladung);
				Log(inst, LogLevel.Debug, $"{c}: outbound planning: ships=[{string.Join(", ", Zaehle(hin).Select(p => $"{p.Value} {p.Key}"))}], outboundFuel={spritHin:N0}, returnFuelEstimate={spritRueck:N0}, homeFreeDeutAfterOutboundFuel={heimatFrei:N0}, transporterCapacity={laderaumHin:N0}, deutLoaded={zuladung:N0}.");

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
					Versuche = 0,
					Schiffe = Zaehle(hin).ToDictionary(p => p.Key.ToString(), p => p.Value)
				};
				var liste = Lade(inst);
				lock (_lock) {
					liste.Add(eintrag);
				}
				Speichere(inst);
				Log(inst, LogLevel.Information, $"Outbound leg {hin} (+{zuladung:N0} deuterium for the return leg, estimated need {spritRueck:N0}) to {c.ToString()} started, arrival in {sekunden}s - return leg follows on arrival.");
				return ReverseStatus.Gesendet;
			} catch (Exception e) {
				Log(inst, LogLevel.Warning, $"Starting the pickup from {c?.ToString()} failed: {e} - classic path.");
				return ReverseStatus.NichtMoeglich;
			}
		}


		public static async Task VerarbeiteOffeneAsync(ITBotMain inst, ICalculationService calc, ITBotOgamedBridge bridge, ReverseHaken h, Buildables bevorzugt) {
			try {
				ReverseKonfig k = LiesKonfig(inst);
				if (!k.Aktiv)
					return;
				DateTime jetzt = DateTime.UtcNow;
				var flottenIds = new HashSet<int>(inst.UserData.fleets.ToList().Select(f => f.ID));
				var offene = Schnappschuss(inst);
				Log(inst, LogLevel.Debug, $"Pending queue check: {offene.Count} entry/entries; active fleet IDs=[{string.Join(",", flottenIds)}].");
				foreach (ReverseEintrag e in offene) {
					Log(inst, LogLevel.Debug, $"Pending entry: celestial={e.CelestialId}, home={e.HeimatId}, fleet={e.FlottenId}, sent={e.Gesendet:O}, estimated arrival={e.Ankunft:O}, attempts={e.Versuche}.");
					if (jetzt > e.Gesendet.AddMinutes(k.TtlMinuten)) {
						Log(inst, LogLevel.Warning, $"Pickup (celestial {e.CelestialId}, fleet {e.FlottenId}) dropped after {k.TtlMinuten} min. If ships still lie there, the next repatriation picks them up.");
						Entferne(inst, e);
						continue;
					}
					if (flottenIds.Contains(e.FlottenId) && jetzt < e.Ankunft.AddMinutes(10)) {
						Log(inst, LogLevel.Debug, $"Pending entry fleet {e.FlottenId} is still active and arrival grace period has not elapsed; waiting.");
						continue;
					}

					Celestial c = FindeCelestial(inst, e.CelestialId);
					Celestial heimat = FindeCelestial(inst, e.HeimatId);
					if (c == null || heimat == null) {
						Log(inst, LogLevel.Warning, $"Pending entry fleet {e.FlottenId} cannot resolve celestial (targetFound={c != null}, homeFound={heimat != null}); removing entry.");
						Entferne(inst, e);
						continue;
					}
					ReverseStatus st = await RueckflugAsync(inst, calc, bridge, h, k, c, heimat, bevorzugt, true, e.Schiffe);
					Log(inst, st == ReverseStatus.NichtMoeglich ? LogLevel.Warning : LogLevel.Debug, $"Pending entry fleet {e.FlottenId} processed with status {st}.");
					if (st == ReverseStatus.Gesendet) {
						Entferne(inst, e);
					} else if (st == ReverseStatus.Wartet) {
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
				Log(inst, LogLevel.Warning, $"Processing pending pickups failed: {ex}");
			}
		}
	}
}
