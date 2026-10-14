using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
	// Ergebnis der Bedarfsrechnung fuer einen Worker (Anbindung von ResourceLedger an TBot).
	public class LedgerTBotPlan {
		// Was noch per Transport angefordert werden muss (volle Kosten - gehaltener Bestand - eigene Flotten unterwegs).
		public Resources Missing;
		// true = nichts mehr anzufordern (Bestand und eigene Lieferungen decken die Kosten).
		public bool MissingIsZero;
		// Eine eigene, noch fliegende Lieferflotte (0 = keine) - fuer die Terminplanung des Workers.
		public int OwnFleetId;
		// true = Ledger aus oder Fehler: der Worker soll sich wie vor dem Patch verhalten.
		public bool Legacy;
		public bool Funded;
	}

	// RESOURCE-LEDGER (10.10.2026): duenne Anbindung der Konto-Klasse an TBot. Alles hier ist "fail-open": bei Fehlern oder
	// abgeschaltetem Ledger (Brain.ResourceLedger.Active = false) verhaelt sich der Bot wie vorher.
	public static class LedgerTBot {
		// Welcher Worker gerade auf welchem Celestial arbeitet. Fliesst durch alle awaits bis in FleetScheduler.SendFleet,
		// damit dort eine gestartete Transportflotte dem richtigen Anspruch zugewiesen werden kann.
		private static readonly AsyncLocal<string> OwnerCtx = new AsyncLocal<string>();
		private static readonly AsyncLocal<long> CelCtx = new AsyncLocal<long>();

		static LedgerTBot() {
			ResourceLedger.LoadHook = path => {
				try {
					if (!File.Exists(path))
						return new List<LedgerClaim>();
					return JsonConvert.DeserializeObject<List<LedgerClaim>>(File.ReadAllText(path)) ?? new List<LedgerClaim>();
				} catch {
					return new List<LedgerClaim>();
				}
			};
			ResourceLedger.SaveHook = (path, claims) => {
				try {
					File.WriteAllText(path, JsonConvert.SerializeObject(claims, Formatting.Indented));
				} catch {
				}
			};

			// Bauwunsch-Protokoll: robuste JSON (atomar geschrieben, kaputte Datei wird gesichert statt ueberschrieben).
			BuildRequestLog.LoadHook = path => {
				try {
					if (!File.Exists(path))
						return new BuildRequestFile();
					return JsonConvert.DeserializeObject<BuildRequestFile>(File.ReadAllText(path)) ?? new BuildRequestFile();
				} catch {
					try {
						File.Copy(path, path + ".broken-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"), true);
					} catch {
					}
					return new BuildRequestFile();
				}
			};
			BuildRequestLog.SaveHook = (path, file) => {
				try {
					string tmp = path + ".tmp";
					File.WriteAllText(tmp, JsonConvert.SerializeObject(file, Formatting.Indented));
					File.Copy(tmp, path, true);
					File.Delete(tmp);
				} catch {
				}
			};
		}

		public static bool Enabled(ITBotMain inst) {
			try {
				if (SettingsService.IsSettingSet(inst.InstanceSettings.Brain, "ResourceLedger")) {
					// Rang nach Kosten (billigster Bauwunsch pro Celestial zuerst), Standard: an
					try {
						ResourceLedger.PriorityByCost = !SettingsService.IsSettingSet(inst.InstanceSettings.Brain.ResourceLedger, "PriorityByCost")
							|| (bool) inst.InstanceSettings.Brain.ResourceLedger.PriorityByCost;
					} catch {
						ResourceLedger.PriorityByCost = true;
					}
					return (bool) inst.InstanceSettings.Brain.ResourceLedger.Active;
				}
				return true;
			} catch {
				return true;
			}
		}

		private static string Key(ITBotMain inst) {
			string playerTag = inst?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universeTag = inst?.UserData?.serverData?.Name ?? "unknown";
			string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataDir);
			return Path.Combine(dataDir, $"resource_ledger_{playerTag}-{universeTag}.json");
		}

		private static string RequestPath(ITBotMain inst) {
			string playerTag = inst?.UserData?.userInfo?.PlayerName ?? "unknown";
			string universeTag = inst?.UserData?.serverData?.Name ?? "unknown";
			string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dataDir);
			return Path.Combine(dataDir, $"build_requests_{playerTag}-{universeTag}.json");
		}

		// Wunsch ins Bauwunsch-Protokoll schreiben (fail-open).
		private static void LogRequest(ITBotMain inst, Celestial c, string owner, string label, Resources cost, Resources missing, string status, int fleetId, float roiDays = 0f) {
			try {
				LedgerAmt co = Amt(cost);
				LedgerAmt mi = Amt(missing);
				BuildRequestLog.Request(RequestPath(inst), owner, c.ID, c.ToString(), label, co.M, co.C, co.D, mi.M, mi.C, mi.D, status, fleetId, DateTime.UtcNow, roiDays);
			} catch {
			}
		}

		private static Action<string> Logger(ITBotMain inst) {
			return s => {
				try {
					inst.log(LogLevel.Information, LogSender.FleetScheduler, "[Ledger] " + s);
				} catch {
				}
			};
		}

		private static void Warn(ITBotMain inst, string msg) {
			try {
				inst.log(LogLevel.Warning, LogSender.FleetScheduler, "[Ledger] " + msg);
			} catch {
			}
		}

		private static Func<int, LedgerFleetInfo> Probe(ITBotMain inst) {
			return id => {
				try {
					Fleet f = inst.UserData.fleets.ToList().FirstOrDefault(x => x.ID == id);
					if (f == null)
						return new LedgerFleetInfo(false, false, false);
					return new LedgerFleetInfo(true, f.ReturnFlight, f.Resources != null && f.Resources.TotalResources > 0);
				} catch {
					return new LedgerFleetInfo(false, false, false);
				}
			};
		}

		private static LedgerAmt Amt(Resources r) {
			if (r == null)
				return new LedgerAmt();
			return new LedgerAmt(r.Metal, r.Crystal, r.Deuterium);
		}

		private static Resources Res(LedgerAmt a) {
			Resources r = new Resources();
			r.Metal = a.M;
			r.Crystal = a.C;
			r.Deuterium = a.D;
			return r;
		}

		// Am Anfang jedes Worker-Zyklus: merkt sich, wer auf welchem Celestial arbeitet.
		public static void SetContext(string owner, long celestialId) {
			OwnerCtx.Value = owner;
			CelCtx.Value = celestialId;
		}

		// Frische Flottenliste, damit angekommene Lieferungen sicher erkannt werden. Nur wenn ueberhaupt Flotten
		// zugewiesen sind - sonst kein zusaetzlicher Aufruf.
		public static async Task SyncFleets(ITBotMain inst, IFleetScheduler scheduler) {
			try {
				if (Enabled(inst) && ResourceLedger.HasAnyFlights(Key(inst)))
					inst.UserData.fleets = await scheduler.UpdateFleets();
			} catch {
			}
		}

		// Was der Worker "owner" auf dem Celestial ausgeben darf (Live-Bestand minus fremder gehaltener Bestand).
		public static Resources Available(ITBotMain inst, Celestial c, string owner) {
			if (!Enabled(inst))
				return c.Resources;
			try {
				return Res(ResourceLedger.Available(Key(inst), c.ID, owner, Amt(c.Resources), DateTime.UtcNow, Probe(inst), Logger(inst)));
			} catch (Exception e) {
				Warn(inst, "Available failed (" + e.Message + ") - using live stock.");
				return c.Resources;
			}
		}

		public static bool CanAfford(ITBotMain inst, Celestial c, string owner, Resources cost) {
			if (!Enabled(inst))
				return true;
			try {
				// Kosten des Ziels mitgeben: bestimmen den Rang (billigster zuerst) gegenueber anderen Anspruechen.
				LedgerAmt av = ResourceLedger.Available(Key(inst), c.ID, owner, Amt(c.Resources), DateTime.UtcNow, Probe(inst), Logger(inst), Amt(cost));
				bool ok = av.Covers(Amt(cost));
				if (!ok && Amt(c.Resources).Covers(Amt(cost))) {
					string foreign = ResourceLedger.DescribeForeign(Key(inst), c.ID, owner, DateTime.UtcNow);
					if (!string.IsNullOrEmpty(foreign))
						Logger(inst)(c.ToString() + ": stock is sufficient but reserved for " + foreign + " - " + owner + " is waiting.");
				}
				return ok;
			} catch (Exception e) {
				Warn(inst, "CanAfford failed (" + e.Message + ") - live stock decides.");
				return true;
			}
		}

		// roiDays: Amortisationszeit in Tagen (nur Minen), 0 = unbekannt; landet im Bauwunsch-Protokoll fuer die Autobuilds-Seite.
		public static LedgerTBotPlan Plan(ITBotMain inst, Celestial c, string owner, string label, Resources cost, float roiDays = 0f) {
			LedgerTBotPlan r = new LedgerTBotPlan();
			if (!Enabled(inst)) {
				r.Legacy = true;
				r.Missing = cost.Difference(c.Resources);
				LogRequest(inst, c, owner, label, cost, r.Missing, BuildRequestLog.StWaiting, 0, roiDays);
				return r;
			}
			try {
				LedgerPlan p = ResourceLedger.Plan(Key(inst), c.ID, owner, label, Amt(cost), Amt(c.Resources), DateTime.UtcNow, Probe(inst), Logger(inst), false);
				r.Missing = Res(p.Missing);
				r.MissingIsZero = p.Missing.IsZero();
				r.Funded = p.Funded;
				r.OwnFleetId = OwnTransportFleetId(inst, p.OwnFleetId);
				string status = r.Funded ? BuildRequestLog.StFunded : (r.OwnFleetId > 0 || r.MissingIsZero) ? BuildRequestLog.StInFlight : BuildRequestLog.StWaiting;
				LogRequest(inst, c, owner, label, cost, r.Missing, status, r.OwnFleetId, roiDays);
				return r;
			} catch (Exception e) {
				Warn(inst, "Plan failed (" + e.Message + ") - worker behaves as before.");
				r.Legacy = true;
				r.Missing = cost.Difference(c.Resources);
				return r;
			}
		}

		// Nur eine Flotte zurueckgeben, die gerade wirklich als Transport auf dem Hinflug in der Liste steht
		// (die Worker suchen sie spaeter mit Single(...) heraus).
		private static int OwnTransportFleetId(ITBotMain inst, int id) {
			if (id <= 0)
				return 0;
			try {
				Fleet f = inst.UserData.fleets.ToList().FirstOrDefault(x => x.ID == id && x.Mission == Missions.Transport && !x.ReturnFlight);
				return f != null ? id : 0;
			} catch {
				return 0;
			}
		}

		// Aus HandleMinerTransport: Bedarf gegen das Konto statt gegen den Live-Bestand. applied = false heisst:
		// kein Worker-Kontext oder Ledger aus -> alter Weg (Kosten - Live-Bestand).
		public static Resources MissingForTransport(ITBotMain inst, Resources fullCost, Celestial destination, out bool applied) {
			applied = false;
			try {
				string owner = OwnerCtx.Value;
				if (!Enabled(inst) || owner == null || CelCtx.Value != destination.ID)
					return fullCost.Difference(destination.Resources);
				LedgerPlan p = ResourceLedger.Plan(Key(inst), destination.ID, owner, "?", Amt(fullCost), Amt(destination.Resources), DateTime.UtcNow, Probe(inst), Logger(inst), true);
				applied = true;
				return Res(p.Missing);
			} catch (Exception e) {
				Warn(inst, "MissingForTransport failed (" + e.Message + ") - using the old path.");
				applied = false;
				return fullCost.Difference(destination.Resources);
			}
		}

		// Der Worker hat gebaut bzw. die Forschung gestartet.
		public static void OnBuilt(ITBotMain inst, Celestial c, string owner, string label, Resources cost) {
			try {
				LedgerAmt co = Amt(cost);
				BuildRequestLog.Built(RequestPath(inst), owner, c.ID, c.ToString(), label, co.M, co.C, co.D, DateTime.UtcNow);
			} catch {
			}
			if (!Enabled(inst))
				return;
			try {
				ResourceLedger.OnBuilt(Key(inst), c.ID, owner, label, Amt(cost), DateTime.UtcNow, Logger(inst));
			} catch {
			}
		}

		// Aus FleetScheduler.SendFleet nach erfolgreichem Versand: Transport dem Anspruch des aktiven Workers zuweisen.
		public static void OnTransportSent(ITBotMain inst, Coordinate destination, int fleetId, Resources payload, long flightSeconds) {
			try {
				string owner = OwnerCtx.Value;
				if (owner == null || !Enabled(inst))
					return;
				long cel = CelCtx.Value;
				Celestial c = inst.UserData.celestials.ToList().FirstOrDefault(x => x.ID == cel);
				if (c == null)
					return;
				if (!c.Coordinate.IsSame(destination)) {
					inst.log(LogLevel.Debug, LogSender.FleetScheduler, "[Ledger] Fleet " + fleetId + " is flying to a different body than " + c.ToString() + " - not assigned.");
					return;
				}
				LedgerAmt amt = Amt(payload);
				if (amt.IsZero())
					return;
				DateTime now = DateTime.UtcNow;
				ResourceLedger.RegisterFlight(Key(inst), cel, owner, fleetId, amt, now.AddSeconds(flightSeconds + 5), now, Logger(inst));
				BuildRequestLog.Delivery(RequestPath(inst), owner, cel, fleetId, amt.ToString(), now);
			} catch {
			}
		}

		// PATCH Repatriate-Frei (11.10.2026): freier Anteil eines Celestials fuer Nicht-Worker-Versender (Rueckholung, Sammeln):
		// Live-Bestand minus ALLE gehaltenen Mengen der Ansprueche. Rueckgabe null = Ledger aus, kein gehaltener Bestand oder
		// Fehler -> der Aufrufer rechnet wie vorher mit dem Live-Bestand. Damit kann die Mindestmenge von AutoRepatriate auf
		// dem wirklich abtransportierbaren Rest laufen und nicht erst nach dem Kuerzen in CheckOrigin.
		public static Resources FreeForExternal(ITBotMain inst, Celestial c, out string heldText) {
			heldText = "";
			try {
				if (!Enabled(inst) || c == null || c.Resources == null)
					return null;
				string key = Key(inst);
				if (!ResourceLedger.HasExternalHold(key, c.ID))
					return null;
				LedgerAmt live = Amt(c.Resources);
				LedgerAmt free = ResourceLedger.AvailableExternal(key, c.ID, live, DateTime.UtcNow, Probe(inst), Logger(inst));
				heldText = "held: " + Min0(live - free).ToString();
				return Res(free);
			} catch (Exception e) {
				Warn(inst, "FreeForExternal failed (" + e.Message + ") - using live stock.");
				return null;
			}
		}

		private static LedgerAmt Min0(LedgerAmt a) {
			return a.Clamp0();
		}

		// Aus FleetScheduler.SendFleet vor dem Absenden: schuetzt gehaltenen Bestand auf dem HERKUNFTS-Celestial.
		// Funding-Transporte (Worker-Kontext) werden bei Verstoss abgelehnt (alles oder nichts), alle anderen
		// Versender (Collect, Repatriate, ...) bekommen die Ladung auf den freien Anteil gekuerzt.
		public static bool CheckOrigin(ITBotMain inst, Celestial origin, Resources payload, long fuel) {
			try {
				if (!Enabled(inst) || payload == null)
					return true;
				string ctxOwner = OwnerCtx.Value;
				string owner = ctxOwner ?? "";
				string key = Key(inst);
				DateTime now = DateTime.UtcNow;
				// Worker-Transporte: Sicht des Workers (nur geschuetzte fremde Ansprueche). Alle anderen Versender
				// (Rueckholung, Sammeln, ...): jeder gehaltene Bestand ist tabu, auch im Wartezustand.
				bool extern_ = ctxOwner == null;
				if (extern_ ? !ResourceLedger.HasExternalHold(key, origin.ID) : !ResourceLedger.HasForeignClaims(key, origin.ID, owner, now))
					return true;
				LedgerAmt free = extern_
					? ResourceLedger.AvailableExternal(key, origin.ID, Amt(origin.Resources), now, Probe(inst), Logger(inst))
					: ResourceLedger.Available(key, origin.ID, owner, Amt(origin.Resources), now, Probe(inst), Logger(inst));
				long maxD = Math.Max(0, free.D - fuel);
				bool fits = free.D >= fuel && payload.Metal <= free.M && payload.Crystal <= free.C && payload.Deuterium <= maxD;
				if (fits)
					return true;
				if (ctxOwner != null) {
					Warn(inst, "Transport from " + origin.ToString() + " rejected: stock is partly reserved for other workers (free: " + free.ToString() + ").");
					return false;
				}
				// PATCH Repatriate-Frei (11.10.2026): reicht das freie Deuterium nicht fuer den Flugbedarf, ging frueher die
				// UNGEKUERZTE Ladung (samt gehaltenem Bestand) raus. Jetzt wird abgelehnt. Ebenso, wenn nach dem Kuerzen nichts
				// mehr uebrig ist (sonst fliegt ein leerer Transporter und belegt einen Slot).
				bool hatteLadung = payload.Metal + payload.Crystal + payload.Deuterium > 0;
				if (!hatteLadung)
					return true; // Mission ohne Ladung (Expedition, Angriff, ...): wie vorher durchlassen
				if (free.D < fuel) {
					Warn(inst, "Dispatch from " + origin.ToString() + " rejected: free deuterium is not enough for the flight (free: " + free.ToString() + ", fuel needed " + fuel.ToString("N0") + ").");
					return false;
				}
				payload.Metal = Math.Min(payload.Metal, free.M);
				payload.Crystal = Math.Min(payload.Crystal, free.C);
				payload.Deuterium = Math.Min(payload.Deuterium, maxD);
				if (payload.Metal + payload.Crystal + payload.Deuterium <= 0) {
					Warn(inst, "Dispatch from " + origin.ToString() + " rejected: nothing is left after cutting down to the free share (free: " + free.ToString() + ").");
					return false;
				}
				Warn(inst, "Cargo from " + origin.ToString() + " cut down to the unreserved share (free: " + free.ToString() + ").");
				return true;
			} catch {
				return true;
			}
		}
	}
}