using Tbot.Common.IO;
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
	public class LedgerTBotPlan {
		public Resources Missing;
		public bool MissingIsZero;
		public int OwnFleetId;
		public bool Legacy;
		public bool Funded;
	}

	public static class LedgerTBot {
		private static readonly AsyncLocal<string> OwnerCtx = new AsyncLocal<string>();
		private static readonly AsyncLocal<long> CelCtx = new AsyncLocal<long>();
		public static string CurrentOwner => OwnerCtx.Value;

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
					AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(claims, Formatting.Indented));
				} catch {
				}
			};

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
					AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(file, Formatting.Indented));
				} catch {
				}
			};
		}

		public static bool Enabled(ITBotMain inst) {
			try {
				if (SettingsService.IsSettingSet(inst.InstanceSettings.Brain, "ResourceLedger")) {
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

		public static void SetContext(string owner, long celestialId) {
			OwnerCtx.Value = owner;
			CelCtx.Value = celestialId;
		}

		public static async Task SyncFleets(ITBotMain inst, IFleetScheduler scheduler) {
			try {
				if (Enabled(inst) && ResourceLedger.HasAnyFlights(Key(inst)))
					inst.UserData.fleets = await scheduler.UpdateFleets();
			} catch {
			}
		}

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

		public static bool IgnoreExternal(ITBotMain inst) {
			try {
				var g = inst.InstanceSettings;
				if (SettingsService.IsSettingSet(g, "Experimental") && SettingsService.IsSettingSet(g.Experimental, "RepatriateLedgerHold")) {
					var r = g.Experimental.RepatriateLedgerHold;
					return SettingsService.IsSettingSet(r, "Ignore") && (bool) r.Ignore;
				}
			} catch {
			}
			return false;
		}

		public static int ExternalMinPercent(ITBotMain inst) {
			try {
				var g = inst.InstanceSettings;
				if (SettingsService.IsSettingSet(g, "Experimental") && SettingsService.IsSettingSet(g.Experimental, "RepatriateLedgerHold")) {
					var r = g.Experimental.RepatriateLedgerHold;
					if (SettingsService.IsSettingSet(r, "MinPercent"))
						return Math.Max(0, Math.Min(100, (int) r.MinPercent));
				}
			} catch {
			}
			return 30;
		}

		public static Resources FreeForExternal(ITBotMain inst, Celestial c, out string heldText) {
			heldText = "";
			try {
				if (!Enabled(inst) || c == null || c.Resources == null || IgnoreExternal(inst))
					return null;
				string key = Key(inst);
				int minProzent = ExternalMinPercent(inst);
				ExtraLog.Write(inst, "Ledger", $"FreeForExternal {c}: minPercent={minProzent}, claims: {ResourceLedger.DescribeExternal(key, c.ID, minProzent)}");
				if (!ResourceLedger.HasExternalHold(key, c.ID, minProzent))
					return null;
				LedgerAmt live = Amt(c.Resources);
				LedgerAmt free = ResourceLedger.AvailableExternal(key, c.ID, live, DateTime.UtcNow, Probe(inst), Logger(inst), minProzent);
				heldText = "held: " + Min0(live - free).ToString() + " for " + ResourceLedger.DescribeHolders(key, c.ID, minProzent);
				return Res(free);
			} catch (Exception e) {
				Warn(inst, "FreeForExternal failed (" + e.Message + ") - using live stock.");
				return null;
			}
		}

		private static LedgerAmt Min0(LedgerAmt a) {
			return a.Clamp0();
		}

		public static bool CheckOrigin(ITBotMain inst, Celestial origin, Resources payload, long fuel) {
			try {
				if (!Enabled(inst) || payload == null)
					return true;
				string ctxOwner = OwnerCtx.Value;
				string owner = ctxOwner ?? "";
				string key = Key(inst);
				DateTime now = DateTime.UtcNow;
				bool extern_ = ctxOwner == null;
				if (extern_ && IgnoreExternal(inst))
					return true;
				int minProzent = extern_ ? ExternalMinPercent(inst) : 0;
				if (extern_ ? !ResourceLedger.HasExternalHold(key, origin.ID, minProzent) : !ResourceLedger.HasForeignClaims(key, origin.ID, owner, now))
					return true;
				LedgerAmt free = extern_
					? ResourceLedger.AvailableExternal(key, origin.ID, Amt(origin.Resources), now, Probe(inst), Logger(inst), minProzent)
					: ResourceLedger.Available(key, origin.ID, owner, Amt(origin.Resources), now, Probe(inst), Logger(inst));
				long maxD = Math.Max(0, free.D - fuel);
				bool fits = free.D >= fuel && payload.Metal <= free.M && payload.Crystal <= free.C && payload.Deuterium <= maxD;
				if (fits)
					return true;
				if (ctxOwner != null) {
					Warn(inst, "Transport from " + origin.ToString() + " rejected: stock is partly reserved for other workers (free: " + free.ToString() + ").");
					return false;
				}
				bool hatteLadung = payload.Metal + payload.Crystal + payload.Deuterium > 0;
				if (!hatteLadung)
					return true;
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