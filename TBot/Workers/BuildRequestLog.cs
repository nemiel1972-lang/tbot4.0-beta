using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers {
	// BAUWUNSCH-PROTOKOLL (10.10.2026): haelt dauerhaft fest, WAS die Worker bauen/forschen wollen, was dafuer noch fehlt und
	// wie der Stand ist (wartet / Lieferung unterwegs / Bestand gedeckt / gebaut / abgeloest). Reine Logik ohne TBot-Typen;
	// die Datei-Anbindung (Laden/Speichern) kommt ueber LoadHook/SaveHook aus LedgerTBot.
	public class BuildRequestEntry {
		public string Owner;
		public long CelestialId;
		public string Celestial;
		public string Label;
		public long CostM;
		public long CostC;
		public long CostD;
		public long MissingM;
		public long MissingC;
		public long MissingD;
		public string Status;
		public int FleetId;
		public string LastDelivery;
		public int TimesSeen;
		// Amortisationszeit in Tagen (ROI) laut Bot-Rechnung; 0 = unbekannt (nur Minen haben einen ROI). Kleiner = besser.
		public float RoiDays;
		public DateTime FirstRequestedUtc;
		public DateTime LastSeenUtc;
		public DateTime? ClosedUtc;
	}

	public class BuildRequestFile {
		public DateTime UpdatedUtc;
		// Aktuelle Wuensche: genau einer pro Worker und Celestial.
		public List<BuildRequestEntry> Open = new List<BuildRequestEntry>();
		// Abgeschlossene Wuensche (gebaut oder abgeloest), neueste zuerst.
		public List<BuildRequestEntry> History = new List<BuildRequestEntry>();
	}

	public static class BuildRequestLog {
		public const string StWaiting = "Waiting for delivery";
		public const string StInFlight = "Delivery in transit";
		public const string StFunded = "Stock covered";
		public const string StBuilt = "Built";
		public const string StReplaced = "Replaced";

		public static Func<string, BuildRequestFile> LoadHook = null;
		public static Action<string, BuildRequestFile> SaveHook = null;
		public static int MaxHistory = 500;
		// Unveraenderte Wuensche werden hoechstens so oft neu gespeichert (schont die Platte).
		public static TimeSpan SaveEvery = TimeSpan.FromMinutes(5);

		private static readonly object Lock = new object();
		private static readonly Dictionary<string, BuildRequestFile> Files = new Dictionary<string, BuildRequestFile>();
		private static readonly Dictionary<string, DateTime> LastSaved = new Dictionary<string, DateTime>();

		private static BuildRequestFile Get(string path) {
			BuildRequestFile f;
			if (Files.TryGetValue(path, out f))
				return f;
			try {
				f = LoadHook != null ? LoadHook(path) : null;
			} catch {
				f = null;
			}
			if (f == null)
				f = new BuildRequestFile();
			if (f.Open == null)
				f.Open = new List<BuildRequestEntry>();
			if (f.History == null)
				f.History = new List<BuildRequestEntry>();
			Files[path] = f;
			return f;
		}

		private static void Save(string path, BuildRequestFile f, DateTime now) {
			f.UpdatedUtc = now;
			LastSaved[path] = now;
			try {
				if (SaveHook != null)
					SaveHook(path, f);
			} catch {
			}
		}

		private static void Close(BuildRequestFile f, BuildRequestEntry e, string status, DateTime now) {
			e.Status = status;
			e.ClosedUtc = now;
			f.Open.Remove(e);
			f.History.Insert(0, e);
			if (f.History.Count > MaxHistory)
				f.History.RemoveRange(MaxHistory, f.History.Count - MaxHistory);
		}

		// Ein Worker meldet (erneut), dass er label auf dem Celestial haben will. Aendert sich das Ziel des Workers,
		// wird der alte Wunsch als "abgeloest" ins Archiv verschoben.
		public static void Request(string path, string owner, long celestialId, string celestialName, string label,
			long costM, long costC, long costD, long missM, long missC, long missD, string status, int fleetId, DateTime now, float roiDays = 0f) {
			lock (Lock) {
				BuildRequestFile f = Get(path);
				bool changed = false;
				BuildRequestEntry e = f.Open.FirstOrDefault(x => x.Owner == owner && x.CelestialId == celestialId);
				if (e != null && e.Label != label) {
					Close(f, e, StReplaced, now);
					e = null;
					changed = true;
				}
				if (e == null) {
					e = new BuildRequestEntry {
						Owner = owner,
						CelestialId = celestialId,
						Celestial = celestialName,
						Label = label,
						FirstRequestedUtc = now
					};
					f.Open.Add(e);
					changed = true;
				}
				if (e.Status != status || e.FleetId != fleetId || e.CostM != costM || e.CostC != costC || e.CostD != costD ||
					e.MissingM != missM || e.MissingC != missC || e.MissingD != missD || e.Celestial != celestialName ||
					Math.Abs(e.RoiDays - roiDays) >= 0.1f)
					changed = true;
				e.RoiDays = roiDays;
				e.Celestial = celestialName;
				e.CostM = costM;
				e.CostC = costC;
				e.CostD = costD;
				e.MissingM = missM;
				e.MissingC = missC;
				e.MissingD = missD;
				e.Status = status;
				e.FleetId = fleetId;
				e.LastSeenUtc = now;
				e.TimesSeen++;
				DateTime last;
				if (!LastSaved.TryGetValue(path, out last))
					last = DateTime.MinValue;
				if (changed || now - last >= SaveEvery)
					Save(path, f, now);
			}
		}

		// Eine Lieferflotte wurde fuer den Wunsch des Workers gestartet.
		public static void Delivery(string path, string owner, long celestialId, int fleetId, string amountText, DateTime now) {
			lock (Lock) {
				BuildRequestFile f = Get(path);
				BuildRequestEntry e = f.Open.FirstOrDefault(x => x.Owner == owner && x.CelestialId == celestialId);
				if (e == null)
					return;
				e.FleetId = fleetId;
				e.Status = StInFlight;
				e.LastDelivery = amountText + " (fleet " + fleetId + ", " + now.ToString("u") + ")";
				e.LastSeenUtc = now;
				Save(path, f, now);
			}
		}

		// Der Bau/die Forschung wurde gestartet. Gab es vorher keinen offenen Wunsch (sofort bezahlbar), wird trotzdem ein
		// Archiv-Eintrag angelegt - so steht jeder Bau der Worker im Protokoll.
		public static void Built(string path, string owner, long celestialId, string celestialName, string label,
			long costM, long costC, long costD, DateTime now) {
			lock (Lock) {
				BuildRequestFile f = Get(path);
				BuildRequestEntry e = f.Open.FirstOrDefault(x => x.Owner == owner && x.CelestialId == celestialId);
				if (e != null && e.Label != label) {
					Close(f, e, StReplaced, now);
					e = null;
				}
				if (e == null) {
					e = new BuildRequestEntry {
						Owner = owner,
						CelestialId = celestialId,
						Celestial = celestialName,
						Label = label,
						CostM = costM,
						CostC = costC,
						CostD = costD,
						FirstRequestedUtc = now,
						LastSeenUtc = now,
						TimesSeen = 1
					};
					f.Open.Add(e);
				}
				e.MissingM = 0;
				e.MissingC = 0;
				e.MissingD = 0;
				e.LastSeenUtc = now;
				Close(f, e, StBuilt, now);
				Save(path, f, now);
			}
		}
	}
}
