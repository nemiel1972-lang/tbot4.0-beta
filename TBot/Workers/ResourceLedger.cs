using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers {

	public struct LedgerAmt {
		public long M { get; set; }
		public long C { get; set; }
		public long D { get; set; }

		public LedgerAmt(long m, long c, long d) : this() {
			M = m;
			C = c;
			D = d;
		}

		public static LedgerAmt operator +(LedgerAmt a, LedgerAmt b) {
			return new LedgerAmt(a.M + b.M, a.C + b.C, a.D + b.D);
		}

		public static LedgerAmt operator -(LedgerAmt a, LedgerAmt b) {
			return new LedgerAmt(a.M - b.M, a.C - b.C, a.D - b.D);
		}

		public LedgerAmt Clamp0() {
			return new LedgerAmt(Math.Max(0, M), Math.Max(0, C), Math.Max(0, D));
		}

		public static LedgerAmt Min(LedgerAmt a, LedgerAmt b) {
			return new LedgerAmt(Math.Min(a.M, b.M), Math.Min(a.C, b.C), Math.Min(a.D, b.D));
		}

		public bool Covers(LedgerAmt other) {
			return M >= other.M && C >= other.C && D >= other.D;
		}

		public bool IsZero() {
			return M <= 0 && C <= 0 && D <= 0;
		}

		public bool SameAs(LedgerAmt other) {
			return M == other.M && C == other.C && D == other.D;
		}

		public long Get(int k) {
			return k == 0 ? M : (k == 1 ? C : D);
		}

		public LedgerAmt With(int k, long v) {
			return new LedgerAmt(k == 0 ? v : M, k == 1 ? v : C, k == 2 ? v : D);
		}

		public override string ToString() {
			return "M:" + M.ToString("N0") + " C:" + C.ToString("N0") + " D:" + D.ToString("N0");
		}
	}

	public struct LedgerFleetInfo {
		public bool Exists;
		public bool ReturnFlight;
		public bool HasCargo;

		public LedgerFleetInfo(bool exists, bool returnFlight, bool hasCargo) {
			Exists = exists;
			ReturnFlight = returnFlight;
			HasCargo = hasCargo;
		}
	}

	public class LedgerFlight {
		public int FleetId { get; set; }
		public LedgerAmt Amount { get; set; }
		public DateTime ArriveUtc { get; set; }
	}

	public class LedgerClaim {
		public string Owner { get; set; }
		public long CelestialId { get; set; }
		public string Label { get; set; }
		public LedgerAmt Reserved { get; set; }
		public LedgerAmt Held { get; set; }
		public List<LedgerFlight> Flights { get; set; } = new List<LedgerFlight>();
		public DateTime CreatedUtc { get; set; }
		public DateTime TouchedUtc { get; set; }
		public DateTime ProtectUntilUtc { get; set; }
	}

	public class LedgerPlan {
		public LedgerAmt Missing;
		public LedgerAmt Held;
		public LedgerAmt InFlight;
		public int OwnFleetId;
		public bool HasOwnFlights;
		public bool Funded;
		public int Rank;
		public int Total;
	}

	public static class ResourceLedger {
		private class State {
			public Dictionary<long, List<LedgerClaim>> Claims = new Dictionary<long, List<LedgerClaim>>();
			public bool Loaded;
		}

		private static readonly object Lock = new object();
		private static readonly Dictionary<string, State> States = new Dictionary<string, State>();

		public static Func<string, List<LedgerClaim>> LoadHook = null;
		public static Action<string, List<LedgerClaim>> SaveHook = null;

		public static TimeSpan ProtectWindow = TimeSpan.FromMinutes(3);
		public static TimeSpan MaxIdle = TimeSpan.FromHours(12);
		public static TimeSpan EarlyTolerance = TimeSpan.FromSeconds(10);
		public static TimeSpan StuckAfter = TimeSpan.FromMinutes(30);

		private static State GetState(string key) {
			State st;
			if (!States.TryGetValue(key, out st)) {
				st = new State();
				States[key] = st;
			}
			if (!st.Loaded) {
				st.Loaded = true;
				try {
					List<LedgerClaim> loaded = LoadHook != null ? LoadHook(key) : null;
					if (loaded != null) {
						foreach (LedgerClaim c in loaded) {
							if (c.Flights == null)
								c.Flights = new List<LedgerFlight>();
							List<LedgerClaim> l;
							if (!st.Claims.TryGetValue(c.CelestialId, out l)) {
								l = new List<LedgerClaim>();
								st.Claims[c.CelestialId] = l;
							}
							l.Add(c);
						}
					}
				} catch {
				}
			}
			return st;
		}

		private static void Save(string key, State st) {
			if (SaveHook == null)
				return;
			try {
				List<LedgerClaim> all = new List<LedgerClaim>();
				foreach (KeyValuePair<long, List<LedgerClaim>> kv in st.Claims)
					all.AddRange(kv.Value);
				SaveHook(key, all);
			} catch {
			}
		}

		private static List<LedgerClaim> ClaimsOf(State st, long cel) {
			List<LedgerClaim> l;
			if (!st.Claims.TryGetValue(cel, out l)) {
				l = new List<LedgerClaim>();
				st.Claims[cel] = l;
			}
			return l;
		}

		private static bool IsProtected(LedgerClaim c, DateTime now) {
			return c.Flights.Count > 0 || c.Held.Covers(c.Reserved) || now < c.ProtectUntilUtc;
		}

		private static LedgerAmt SumFlights(LedgerClaim c) {
			LedgerAmt s = new LedgerAmt();
			foreach (LedgerFlight f in c.Flights)
				s = s + f.Amount;
			return s;
		}

		public static bool PriorityByCost = true;

		private static long Weight(LedgerAmt cost) {
			return Math.Max(0, cost.M) + Math.Max(0, cost.C) + Math.Max(0, cost.D);
		}

		private static bool IsFunded(LedgerClaim c) {
			return !c.Reserved.IsZero() && c.Held.Covers(c.Reserved);
		}

		private static bool RanksBefore(long wa, DateTime createdA, string ownerA, long wb, DateTime createdB, string ownerB) {
			if (wa != wb)
				return wa < wb;
			if (createdA != createdB)
				return createdA < createdB;
			return string.CompareOrdinal(ownerA, ownerB) < 0;
		}

		private static LedgerAmt HeldByOthers(List<LedgerClaim> list, string owner, DateTime now, long myWeight, DateTime myCreated) {
			LedgerAmt s = new LedgerAmt();
			foreach (LedgerClaim c in list) {
				if (c.Owner == owner || !IsProtected(c, now))
					continue;
				bool blocks = !PriorityByCost || IsFunded(c) || RanksBefore(Weight(c.Reserved), c.CreatedUtc, c.Owner, myWeight, myCreated, owner);
				if (blocks)
					s = s + c.Held;
			}
			return s;
		}

		private static LedgerAmt HeldByOthers(List<LedgerClaim> list, string owner, DateTime now) {
			return HeldByOthers(list, owner, now, long.MaxValue, DateTime.MaxValue);
		}

		private static void Log(Action<string> log, string msg) {
			if (log != null)
				log(msg);
		}

		private static bool Reconcile(State st, long cel, LedgerAmt live, DateTime now, Func<int, LedgerFleetInfo> probe, Action<string> log) {
			List<LedgerClaim> list;
			if (!st.Claims.TryGetValue(cel, out list) || list.Count == 0)
				return false;
			bool changed = false;

			for (int i = list.Count - 1; i >= 0; i--) {
				LedgerClaim c = list[i];
				if (c.Flights.Count == 0 && now - c.TouchedUtc > MaxIdle) {
					Log(log, "Claim " + c.Owner + " (" + c.Label + ") expired: untouched for " + MaxIdle.TotalHours.ToString("0") + "h.");
					list.RemoveAt(i);
					changed = true;
				}
			}

			foreach (LedgerClaim c in list) {
				for (int j = c.Flights.Count - 1; j >= 0; j--) {
					LedgerFlight f = c.Flights[j];
					LedgerFleetInfo info = probe != null ? probe(f.FleetId) : new LedgerFleetInfo(false, false, false);
					if (info.Exists && !info.ReturnFlight) {
						if (now > f.ArriveUtc + StuckAfter) {
							Log(log, "Fleet " + f.FleetId + " (" + c.Owner + ") is past its arrival time - assignment discarded.");
							c.Flights.RemoveAt(j);
							changed = true;
						}
					} else if (info.Exists && info.ReturnFlight && info.HasCargo) {
						Log(log, "Fleet " + f.FleetId + " (" + c.Owner + ") was recalled - delivery " + f.Amount.ToString() + " discarded.");
						c.Flights.RemoveAt(j);
						changed = true;
					} else if (now >= f.ArriveUtc - EarlyTolerance) {
						c.Held = c.Held + f.Amount;
						Log(log, "Delivery " + f.Amount.ToString() + " for " + c.Owner + " (" + c.Label + ") arrived and was credited.");
						c.Flights.RemoveAt(j);
						changed = true;
					}
				}

				LedgerAmt capped = LedgerAmt.Min(c.Held, c.Reserved).Clamp0();
				if (!capped.SameAs(c.Held)) {
					c.Held = capped;
					changed = true;
				}

			}

			bool liveKnown = !(live.M <= 0 && live.C <= 0 && live.D <= 0);
			if (liveKnown) {
				for (int k = 0; k < 3; k++) {
					long total = 0;
					foreach (LedgerClaim c in list)
						total += c.Held.Get(k);
					long excess = total - live.Get(k);
					if (excess <= 0)
						continue;
					IEnumerable<LedgerClaim> cutOrder = PriorityByCost
						? list.OrderBy(x => IsFunded(x) ? 1 : 0).ThenByDescending(x => Weight(x.Reserved)).ThenByDescending(x => x.CreatedUtc).ToList()
						: list.OrderByDescending(x => x.CreatedUtc).ToList();
					foreach (LedgerClaim c in cutOrder) {
						if (excess <= 0)
							break;
						long have = c.Held.Get(k);
						long cut = Math.Min(have, excess);
						if (cut > 0) {
							c.Held = c.Held.With(k, have - cut);
							excess -= cut;
							changed = true;
							Log(log, "Stock of " + c.Owner + " (" + c.Label + ") cut by " + cut.ToString("N0") + " (live stock does not cover the reservation).");
						}
					}
				}
			}

			return changed;
		}

		public static LedgerAmt Available(string key, long cel, string owner, LedgerAmt live, DateTime now, Func<int, LedgerFleetInfo> probe, Action<string> log) {
			return Available(key, cel, owner, live, now, probe, log, null);
		}

		public static LedgerAmt Available(string key, long cel, string owner, LedgerAmt live, DateTime now, Func<int, LedgerFleetInfo> probe, Action<string> log, LedgerAmt? myCost) {
			lock (Lock) {
				State st = GetState(key);
				if (Reconcile(st, cel, live, now, probe, log))
					Save(key, st);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return live.Clamp0();
				long w = myCost.HasValue ? Weight(myCost.Value) : long.MaxValue;
				LedgerClaim own = list.FirstOrDefault(c => c.Owner == owner);
				DateTime created = own != null ? own.CreatedUtc : now;
				return (live - HeldByOthers(list, owner, now, w, created)).Clamp0();
			}
		}

		public static LedgerPlan Plan(string key, long cel, string owner, string label, LedgerAmt cost, LedgerAmt live, DateTime now, Func<int, LedgerFleetInfo> probe, Action<string> log, bool useExistingTarget) {
			lock (Lock) {
				State st = GetState(key);
				bool changed = Reconcile(st, cel, live, now, probe, log);
				List<LedgerClaim> list = ClaimsOf(st, cel);
				LedgerClaim mine = list.FirstOrDefault(c => c.Owner == owner);
				if (mine == null) {
					mine = new LedgerClaim {
						Owner = owner,
						CelestialId = cel,
						Label = label,
						Reserved = cost.Clamp0(),
						Held = new LedgerAmt(),
						CreatedUtc = now,
						TouchedUtc = now,
						ProtectUntilUtc = now + ProtectWindow
					};
					list.Add(mine);
					changed = true;
					Log(log, "Claim created: " + owner + " (" + label + ") needs " + cost.ToString() + " (" + list.Count + " claim(s) on this celestial)");
				} else if (!useExistingTarget && (mine.Label != label || !mine.Reserved.SameAs(cost.Clamp0()))) {
					Log(log, "Claim " + owner + " switched: " + mine.Label + " -> " + label);
					mine.Label = label;
					mine.Reserved = cost.Clamp0();
					changed = true;
				}
				mine.TouchedUtc = now;

				LedgerAmt inflight = SumFlights(mine);
				LedgerAmt free = (live - HeldByOthers(list, owner, now, Weight(mine.Reserved), mine.CreatedUtc) - mine.Held).Clamp0();
				LedgerAmt want = (mine.Reserved - mine.Held - inflight).Clamp0();
				LedgerAmt take = LedgerAmt.Min(want, free);
				if (!take.IsZero()) {
					mine.Held = mine.Held + take;
					changed = true;
				}

				LedgerPlan plan = new LedgerPlan();
				plan.Held = mine.Held;
				plan.InFlight = inflight;
				plan.Missing = (mine.Reserved - mine.Held - inflight).Clamp0();
				plan.Funded = mine.Held.Covers(mine.Reserved);
				long myW = Weight(mine.Reserved);
				plan.Total = list.Count;
				plan.Rank = 1 + list.Count(c => c != mine && RanksBefore(Weight(c.Reserved), c.CreatedUtc, c.Owner, myW, mine.CreatedUtc, mine.Owner));
				LedgerFlight first = mine.Flights.OrderBy(f => f.ArriveUtc).FirstOrDefault();
				plan.HasOwnFlights = first != null;
				plan.OwnFleetId = first != null ? first.FleetId : 0;

				if (changed)
					Save(key, st);
				return plan;
			}
		}

		public static bool RegisterFlight(string key, long cel, string owner, int fleetId, LedgerAmt amount, DateTime arriveUtc, DateTime now, Action<string> log) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return false;
				LedgerClaim mine = list.FirstOrDefault(c => c.Owner == owner);
				if (mine == null)
					return false;
				mine.Flights.RemoveAll(f => f.FleetId == fleetId);
				mine.Flights.Add(new LedgerFlight { FleetId = fleetId, Amount = amount, ArriveUtc = arriveUtc });
				mine.TouchedUtc = now;
				Log(log, "Fleet " + fleetId + " with " + amount.ToString() + " assigned to claim " + owner + " (" + mine.Label + ").");
				Save(key, st);
				return true;
			}
		}

		public static void OnBuilt(string key, long cel, string owner, string label, LedgerAmt cost, DateTime now, Action<string> log) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return;
				LedgerClaim mine = list.FirstOrDefault(c => c.Owner == owner);
				if (mine == null)
					return;
				if (mine.Label == label) {
					list.Remove(mine);
					Log(log, "Claim " + owner + " (" + label + ") completed.");
				} else {
					mine.Held = (mine.Held - LedgerAmt.Min(mine.Held, cost.Clamp0())).Clamp0();
					mine.TouchedUtc = now;
				}
				Save(key, st);
			}
		}

		public static void Release(string key, long cel, string owner, Action<string> log) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return;
				if (list.RemoveAll(c => c.Owner == owner) > 0) {
					Log(log, "Claim " + owner + " released.");
					Save(key, st);
				}
			}
		}

		public static bool HasForeignClaims(string key, long cel, string owner, DateTime now) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return false;
				return list.Any(c => c.Owner != owner && IsProtected(c, now) && !c.Held.IsZero());
			}
		}

		public static LedgerAmt AvailableExternal(string key, long cel, LedgerAmt live, DateTime now, Func<int, LedgerFleetInfo> probe, Action<string> log) {
			lock (Lock) {
				State st = GetState(key);
				if (Reconcile(st, cel, live, now, probe, log))
					Save(key, st);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return live.Clamp0();
				LedgerAmt held = new LedgerAmt();
				foreach (LedgerClaim c in list)
					held = held + c.Held;
				return (live - held).Clamp0();
			}
		}

		public static bool HasExternalHold(string key, long cel) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return false;
				return list.Any(c => !c.Held.IsZero());
			}
		}

		public static bool HasAnyFlights(string key) {
			lock (Lock) {
				State st = GetState(key);
				return st.Claims.Values.Any(l => l.Any(c => c.Flights.Count > 0));
			}
		}

		public static string DescribeForeign(string key, long cel, string owner, DateTime now) {
			lock (Lock) {
				State st = GetState(key);
				List<LedgerClaim> list;
				if (!st.Claims.TryGetValue(cel, out list))
					return "";
				return string.Join(", ", list.Where(c => c.Owner != owner && IsProtected(c, now) && !c.Held.IsZero()).Select(c => c.Owner + " (" + c.Label + ", held " + c.Held.ToString() + ")"));
			}
		}
	}
}