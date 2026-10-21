using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Tbot.Services {
	public class LfEffectsStore {
		public sealed class Row {
			public string Category = "";
			public string Target = "";
			public long CelestialId;
			public int Slot, Level, TechId;
			public string Tech = "";
			public List<double> Values = new();
		}

		public sealed class Effect {
			public string Category = "", Target = "";
			public Dictionary<string, List<double>> ByLevel = new();
			public double PerLevel;
		}

		public sealed class TechEntry {
			public string Name = "";
			public List<Effect> Effects = new();
		}

		public sealed class TechTable {
			public string Name = "";
			public Dictionary<string, string> Columns = new();
			public Dictionary<string, Dictionary<string, double>> Levels = new();
			public string UpdatedUtc = "";
		}

		private sealed class FileData {
			public Dictionary<string, TechEntry> Techs = new();
			public Dictionary<string, TechTable> Tables = new();
			public List<Row> Current = new();
			public string UpdatedUtc = "";
		}

		private readonly string _path;
		private readonly object _gate = new();
		private FileData _data = new();
		private DateTime _lastRefresh = DateTime.MinValue;

		public LfEffectsStore(string path) {
			_path = path;
			try {
				if (File.Exists(path)) _data = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path)) ?? new FileData();
			} catch (Exception) { _data = new FileData(); }
		}

		public static string PathFor(string alias) {
			string safe = string.Concat((alias ?? "default").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
			return Path.Combine(AppContext.BaseDirectory, "data", $"lf_effekte_{safe}.json");
		}

		public bool Due(DateTime now, TimeSpan every) => now - _lastRefresh >= every;

		private static readonly Regex HeadRx = new(@"<(bonus-item-heading|inner-bonus-item-heading)\b[^>]*?data-toggable=""([^""]+)""", RegexOptions.Compiled);
		private static readonly Regex RowRx = new(@"<space-object-technology>(.*?)</space-object-technology>", RegexOptions.Compiled | RegexOptions.Singleline);
		private static readonly Regex ItemRx = new(@"<(?:space-object-technology-item|bonus-item)\b[^>]*>([^<]*)</", RegexOptions.Compiled);
		private static readonly Regex TechRx = new(@"lifeformTech(\d+)\b[^>]*?title=""([^""]*)""", RegexOptions.Compiled);
		private static readonly Regex PctRx = new(@"^\s*([-+]?\d+(?:[.,]\d+)?)\s*%\s*$", RegexOptions.Compiled);

		public static List<Row> Parse(string html) {
			var rows = new List<Row>();
			if (string.IsNullOrEmpty(html)) return rows;
			var marks = new SortedDictionary<int, (string kind, string key)>();
			foreach (Match m in HeadRx.Matches(html)) marks[m.Index] = (m.Groups[1].Value, m.Groups[2].Value);
			string category = "", target = ""; long cel = 0;
			var rowMatches = RowRx.Matches(html).Cast<Match>().ToList();
			int ri = 0;
			foreach (var kv in marks.Concat(new[] { new KeyValuePair<int, (string, string)>(int.MaxValue, ("", "")) })) {
				while (ri < rowMatches.Count && rowMatches[ri].Index < kv.Key) {
					var row = ParseRow(rowMatches[ri].Groups[1].Value, category, target, cel);
					if (row != null) rows.Add(row);
					ri++;
				}
				var (kind, key) = kv.Value;
				if (kind == "bonus-item-heading") { category = key; target = ""; cel = 0; }
				else if (kind == "inner-bonus-item-heading") {
					if (long.TryParse(key, out long id) && id >= 1_000_000) cel = id;
					else { target = key; cel = 0; }
				}
			}
			return rows;
		}

		private static Row ParseRow(string inner, string category, string target, long cel) {
			var tm = TechRx.Match(inner);
			if (!tm.Success) return null;
			var texts = ItemRx.Matches(inner).Cast<Match>().Select(x => WebUtility.HtmlDecode(x.Groups[1].Value).Trim()).Where(s => s.Length > 0).ToList();
			if (texts.Count < 2 || !int.TryParse(texts[0], out int slot) || !int.TryParse(texts[1], out int level)) return null;
			var row = new Row { Category = category, Target = target, CelestialId = cel, Slot = slot, Level = level, TechId = int.Parse(tm.Groups[1].Value), Tech = WebUtility.HtmlDecode(tm.Groups[2].Value) };
			foreach (string s in texts.Skip(2)) {
				var pm = PctRx.Match(s);
				if (pm.Success && double.TryParse(pm.Groups[1].Value.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) row.Values.Add(v);
			}
			return row.Values.Count == 0 ? null : row;
		}

		public int Add(IEnumerable<Row> rows, DateTime now) {
			int changed = 0;
			rows = rows.ToList();
			lock (_gate) {
				_lastRefresh = now;
				foreach (var r in rows) {
					string id = r.TechId.ToString();
					if (!_data.Techs.TryGetValue(id, out var te)) _data.Techs[id] = te = new TechEntry();
					te.Name = r.Tech;
					var ef = te.Effects.FirstOrDefault(e => e.Category == r.Category && e.Target == r.Target);
					if (ef == null) te.Effects.Add(ef = new Effect { Category = r.Category, Target = r.Target });
					string lvl = r.Level.ToString();
					if (!ef.ByLevel.TryGetValue(lvl, out var old) || !old.SequenceEqual(r.Values)) { ef.ByLevel[lvl] = r.Values; changed++; }
				}
				foreach (var te in _data.Techs.Values)
					foreach (var ef in te.Effects) {
						var per = ef.ByLevel.Where(kv => int.Parse(kv.Key) > 0 && kv.Value.Count > 0).Select(kv => kv.Value[0] / int.Parse(kv.Key)).ToList();
						ef.PerLevel = per.Count == 0 ? 0 : Math.Round(per.Average(), 4);
					}
				_data.UpdatedUtc = now.ToUniversalTime().ToString("o");
				var list = rows as IList<Row> ?? rows.ToList();
				bool curChanged = list.Count > 0 && JsonConvert.SerializeObject(list, Formatting.None) != JsonConvert.SerializeObject(_data.Current, Formatting.None);
				if (curChanged) _data.Current = list.ToList();
				if (changed > 0 || curChanged || !File.Exists(_path)) Save();
			}
			return changed;
		}

		private static readonly Regex TableRowRx = new(@"<tr\b[^>]*>(.*?)</tr>", RegexOptions.Compiled | RegexOptions.Singleline);
		private static readonly Regex CellRx = new(@"<td class=""([a-z_]+)""[^>]*>\s*([^<]*?)\s*</td>", RegexOptions.Compiled | RegexOptions.Singleline);
		private static readonly Regex ThRx = new(@"<th>\s*([^<]*?)\s*</th>", RegexOptions.Compiled | RegexOptions.Singleline);

		public static double? ParseNumber(string s) {
			if (string.IsNullOrWhiteSpace(s)) return null;
			s = WebUtility.HtmlDecode(s).Trim();
			double mul = 1;
			foreach (var (suffix, factor) in new[] { ("Bio", 1e12), ("Brd", 1e15), ("Mrd", 1e9), ("M", 1e6), ("K", 1e3) }) {
				if (s.EndsWith(suffix, StringComparison.Ordinal)) { mul = factor; s = s.Substring(0, s.Length - suffix.Length).Trim(); break; }
			}
			s = s.Replace("%", "").Replace(" ", "");
			if (s.Contains(',')) s = s.Replace(".", "").Replace(',', '.');
			else if (Regex.IsMatch(s, @"^[-+]?\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
			return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? Math.Round(v * mul, 4) : null;
		}

		public static TechTable ParseTechInfo(string html) {
			var t = new TechTable();
			if (string.IsNullOrEmpty(html)) return t;
			var heads = ThRx.Matches(html).Cast<Match>().Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Replace("\n", " ").Trim()).ToList();
			int hi = 0;
			foreach (Match row in TableRowRx.Matches(html)) {
				var cells = CellRx.Matches(row.Groups[1].Value).Cast<Match>().ToList();
				if (cells.Count == 0 || cells[0].Groups[1].Value != "level") continue;
				if (!int.TryParse(cells[0].Groups[2].Value, out int level)) continue;
				var vals = new Dictionary<string, double>();
				for (int i = 1; i < cells.Count; i++) {
					string key = cells[i].Groups[1].Value;
					var v = ParseNumber(cells[i].Groups[2].Value);
					if (v.HasValue) vals[key] = v.Value;
					if (!t.Columns.ContainsKey(key)) t.Columns[key] = i < heads.Count ? heads[i] : key;
				}
				t.Levels[level.ToString()] = vals;
				hi++;
			}
			return t;
		}

		public bool HasFreshTable(int techId, TimeSpan maxAge, DateTime now) {
			lock (_gate) {
				if (!_data.Tables.TryGetValue(techId.ToString(), out var t) || t.Levels.Count == 0)
					return false;
				return DateTime.TryParse(t.UpdatedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var u) && now - u < maxAge;
			}
		}

		public int AddTable(int techId, string name, TechTable table, DateTime now) {
			if (table == null || table.Levels.Count == 0) return 0;
			int changed = 0;
			lock (_gate) {
				string id = techId.ToString();
				if (!_data.Tables.TryGetValue(id, out var cur)) _data.Tables[id] = cur = new TechTable();
				if (!string.IsNullOrEmpty(name)) cur.Name = name;
				foreach (var kv in table.Columns) cur.Columns[kv.Key] = kv.Value;
				foreach (var kv in table.Levels) {
					if (!cur.Levels.TryGetValue(kv.Key, out var old) || old.Count != kv.Value.Count || old.Any(o => !kv.Value.TryGetValue(o.Key, out var nv) || nv != o.Value)) {
						cur.Levels[kv.Key] = kv.Value;
						changed++;
					}
				}
				cur.UpdatedUtc = now.ToUniversalTime().ToString("o");
				if (changed > 0) Save();
			}
			return changed;
		}

		public double? TableValue(int techId, int level, string column) {
			lock (_gate) {
				if (_data.Tables.TryGetValue(techId.ToString(), out var t) && t.Levels.TryGetValue(level.ToString(), out var row) && row.TryGetValue(column, out var v)) return v;
				return null;
			}
		}

		public List<Row> CurrentRows() {
			lock (_gate) return _data.Current.ToList();
		}

		public double NextLevelDelta(int techId, string category, string target, int level) {
			lock (_gate) {
				if (!_data.Techs.TryGetValue(techId.ToString(), out var te)) return 0;
				var ef = te.Effects.FirstOrDefault(e => e.Category == category && e.Target == target);
				if (ef == null) return 0;
				if (ef.ByLevel.TryGetValue(level.ToString(), out var a) && ef.ByLevel.TryGetValue((level + 1).ToString(), out var b) && a.Count > 0 && b.Count > 0)
					return Math.Max(0, b[0] - a[0]);
				return ef.PerLevel;
			}
		}

		public double? Expected(int techId, string category, string target, int level, int valueIndex = 0) {
			lock (_gate) {
				if (!_data.Techs.TryGetValue(techId.ToString(), out var te)) return null;
				var ef = te.Effects.FirstOrDefault(e => e.Category == category && e.Target == target);
				if (ef == null) return null;
				if (ef.ByLevel.TryGetValue(level.ToString(), out var v) && v.Count > valueIndex) return v[valueIndex];
				return ef.PerLevel > 0 && valueIndex == 0 ? ef.PerLevel * level : null;
			}
		}

		private void Save() {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
				File.WriteAllText(_path, JsonConvert.SerializeObject(_data, Formatting.Indented));
			} catch (Exception) {  }
		}
	}
}
