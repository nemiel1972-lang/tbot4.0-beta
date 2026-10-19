using System;
using System.Collections.Generic;
using System.Linq;

namespace Tbot.Workers {
	public class AsteroidHuntConfig {
		public bool Active = false;
		public string ShipType = "Cruiser";
		public int ShipCount = 1;
		public int MaxAttemptsPerDay = 0;
		public int SearchSystems = 10;
	}

	public class HuntAttempt {
		public string Utc { get; set; }
		public string Day { get; set; }
		public string Origin { get; set; }
		public string Target { get; set; }
		public string Result { get; set; }
		public string Item { get; set; }
	}

	public class HuntPending {
		public string Day { get; set; }
		public string SentUtc { get; set; }
		public string ArrivalUtc { get; set; }
		public string Origin { get; set; }
		public string Target { get; set; }
		public bool BeforeKnown { get; set; }
		public Dictionary<string, long> Before { get; set; } = new();
		public int ReadFailures { get; set; }
		public bool ReportBaseKnown { get; set; }
		public long LastMsgId { get; set; }
		public int ReportWaits { get; set; }
	}

	public enum ReportKind {
		Unknown,
		Found,
		Retry,
		Locked
	}

	public class HuntState {
		public string Day { get; set; }
		public int AttemptsToday { get; set; }
		public bool FoundToday { get; set; }
		public int SearchIndex { get; set; }
		public bool SearchExhausted { get; set; }
		public int LastHitSystem { get; set; }
		public HuntPending Pending { get; set; }
		public List<HuntAttempt> Attempts { get; set; } = new();
	}

	public enum HuntAction {
		Wait,
		Resolve,
		Start,
		DoneToday
	}

	public static class AsteroidHuntLogic {
		public const int ArrivalGraceSeconds = 40;
		public const int MaxReadFailures = 3;

		public static void RollDay(HuntState s, string today) {
			if (s.Day == today)
				return;
			s.Day = today;
			s.AttemptsToday = 0;
			s.FoundToday = false;
			s.SearchIndex = 0;
			s.SearchExhausted = false;
		}

		public const int HardAttemptCap = 60;

		public static int AttemptLimit(AsteroidHuntConfig cfg) =>
			cfg.MaxAttemptsPerDay <= 0 ? HardAttemptCap : Math.Min(cfg.MaxAttemptsPerDay, HardAttemptCap);

		public static HuntAction Decide(HuntState s, DateTime nowUtc, AsteroidHuntConfig cfg) {
			if (s.Pending != null) {
				DateTime arrival = ParseUtc(s.Pending.ArrivalUtc, nowUtc);
				return nowUtc >= arrival.AddSeconds(ArrivalGraceSeconds) ? HuntAction.Resolve : HuntAction.Wait;
			}
			if (s.FoundToday || s.SearchExhausted || s.AttemptsToday >= AttemptLimit(cfg))
				return HuntAction.DoneToday;
			return HuntAction.Start;
		}

		public static ReportKind Classify(string text) {
			if (string.IsNullOrWhiteSpace(text))
				return ReportKind.Unknown;
			string t = text.ToLowerInvariant();
			if (t.Contains("geborgen") || t.Contains("im inventar"))
				return ReportKind.Found;
			if (t.Contains("nacht darüber schlafen") || t.Contains("nacht darueber schlafen") || t.Contains("morgen nochmal") || t.Contains("morgen noch einmal"))
				return ReportKind.Locked;
			if (t.Contains("kein ergebnis") || t.Contains("erfolglos") || t.Contains("noch einmal versuchen") || t.Contains("nochmal versuchen") || t.Contains("rückweg") || t.Contains("rueckweg"))
				return ReportKind.Retry;
			return ReportKind.Unknown;
		}

		public static List<string> NewItems(IDictionary<string, long> before, IDictionary<string, long> after) {
			var found = new List<string>();
			foreach (var kv in after) {
				before.TryGetValue(kv.Key, out long old);
				if (kv.Value > old)
					found.Add(kv.Key);
			}
			return found;
		}

		public static int TargetSystem(int originSystem, int attemptIndex, int maxOffset, int systems) {
			if (attemptIndex <= 0 || maxOffset <= 0 || systems <= 0)
				return originSystem;
			int step = (attemptIndex + 1) / 2;
			if (step > maxOffset)
				step = ((step - 1) % maxOffset) + 1;
			int offset = attemptIndex % 2 == 1 ? step : -step;
			int target = originSystem + offset;
			if (target < 1)
				target += systems;
			if (target > systems)
				target -= systems;
			return Math.Min(Math.Max(1, target), systems);
		}

		public static long SecondsUntilNextDay(DateTime gameNow, int minSeconds = 300) {
			DateTime next = gameNow.Date.AddDays(1);
			return Math.Max(minSeconds, (long) (next - gameNow).TotalSeconds);
		}

		public static DateTime ParseUtc(string text, DateTime fallback) {
			if (DateTime.TryParse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
				return d;
			return fallback;
		}
	}
}
