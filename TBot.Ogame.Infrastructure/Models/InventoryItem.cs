using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TBot.Ogame.Infrastructure.Models {
	public class InventoryItem {
		public string Ref { get; set; } = "";
		public string Name { get; set; } = "";
		public string Title { get; set; } = "";
		public long Amount { get; set; }
		public long AmountFree { get; set; }
		public Dictionary<string, object> Extra { get; set; }

		private static readonly Regex KeyRx = new("expir|timeleft|time_left|remain|endtime|end_time|until|valid|countdown|duration_left", RegexOptions.IgnoreCase);
		private static readonly Regex TextRx = new(@"(?:(\d+)\s*[tTdD]\b)?\s*(?:(\d+)\s*[hH]\b)?\s*(?:(\d+)\s*[mM]\b)?", RegexOptions.Compiled);

		public long? ExpiresInSec() {
			if (Extra != null) {
				foreach (var kv in Extra) {
					if (!KeyRx.IsMatch(kv.Key) || kv.Value == null)
						continue;
					if (kv.Value is string sv && ParseText(sv) is long ts && ts > 0)
						return ts;
					if (kv.Value is bool)
						continue;
					if (double.TryParse(Convert.ToString(kv.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d > 0) {
						if (d > 1e12) return (long) Math.Max(0, d / 1000 - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
						if (d > 1e9) return (long) Math.Max(0, d - DateTimeOffset.UtcNow.ToUnixTimeSeconds());
						return (long) d;
					}
				}
			}
			var tm = TitleExpiryRx.Match(Title ?? "");
			return tm.Success ? ParseText(tm.Groups[1].Value) : null;
		}
		private static readonly Regex TitleExpiryRx = new(@"(?:l[aä]e?uft\s+ab|verf[aä]e?llt|ablauf|expires?|expiry)\D{0,40}(\d.{0,30})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private static long? ParseText(string text) {
			if (string.IsNullOrEmpty(text))
				return null;
			foreach (Match m in TextRx.Matches(text)) {
				if (m.Length == 0 || (!m.Groups[1].Success && !m.Groups[2].Success && !m.Groups[3].Success))
					continue;
				long sec = 0;
				if (m.Groups[1].Success) sec += long.Parse(m.Groups[1].Value) * 86400;
				if (m.Groups[2].Success) sec += long.Parse(m.Groups[2].Value) * 3600;
				if (m.Groups[3].Success) sec += long.Parse(m.Groups[3].Value) * 60;
				if (sec > 0) return sec;
			}
			return null;
		}
	}
}
