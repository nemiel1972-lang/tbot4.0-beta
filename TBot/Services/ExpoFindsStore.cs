using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tbot.Common.IO;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {
	public class ExpoFindsStore {
		public sealed class Entry {
			public long Id;
			public DateTime Utc;
			public long Metal, Crystal, Deuterium, DarkMatter;
			public Dictionary<string, long> Ships = new();
		}

		public sealed class Summary {
			public int Reports, DaysWithData;
			public double Avg7ResDeut, Avg30ResDeut, Best7Of60ResDeut;
			public double Avg7ShipsDeut, Avg30ShipsDeut, Best7Of60ShipsDeut;
			public double AvgDarkMatterPerDay30;
			public string FirstUtc, LastUtc, UpdatedUtc;
		}

		private sealed class FileData {
			public List<Entry> Entries = new();
			public Summary Summary;
		}

		private const int KeepDays = 120;
		private readonly string _path;
		private readonly object _gate = new();
		private FileData _data = new();
		private DateTime _lastRefresh = DateTime.MinValue;

		public ExpoFindsStore(string path) {
			_path = path;
			try {
				if (File.Exists(path))
					_data = JsonConvert.DeserializeObject<FileData>(File.ReadAllText(path)) ?? new FileData();
			} catch (Exception) {
				_data = new FileData();
			}
		}

		public static string PathFor(string alias) {
			string safe = string.Concat((alias ?? "default").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
			return Path.Combine(AppContext.BaseDirectory, "data", $"expo_finds_{safe}.json");
		}

		public bool Due(DateTime now, TimeSpan every) => now - _lastRefresh >= every;

		public int Add(IEnumerable<ExpeditionMessage> messages, Func<string, double> shipValueDeut, DateTime now) {
			lock (_gate) {
				_lastRefresh = now;
				var known = _data.Entries.Select(e => e.Id).ToHashSet();
				int added = 0;
				foreach (var m in messages ?? Enumerable.Empty<ExpeditionMessage>()) {
					if (m == null || m.ID == 0 || known.Contains(m.ID))
						continue;
					var ships = (m.Ships ?? new()).Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
					var r = m.Resources;
					_data.Entries.Add(new Entry {
						Id = m.ID, Utc = m.CreatedAt.Kind == DateTimeKind.Utc ? m.CreatedAt : m.CreatedAt.ToUniversalTime(),
						Metal = r?.Metal ?? 0, Crystal = r?.Crystal ?? 0, Deuterium = r?.Deuterium ?? 0, DarkMatter = r?.Darkmatter ?? 0, Ships = ships
					});
					known.Add(m.ID);
					added++;
				}
				_data.Entries.RemoveAll(e => e.Utc < now.AddDays(-KeepDays));
				_data.Summary = Compute(_data.Entries, shipValueDeut, now);
				Save();
				return added;
			}
		}

		public Summary Current { get { lock (_gate) return _data.Summary; } }

		public static Summary Compute(List<Entry> entries, Func<string, double> shipValueDeut, DateTime now) {
			var s = new Summary { Reports = entries.Count, UpdatedUtc = now.ToString("o") };
			if (entries.Count == 0)
				return s;
			s.FirstUtc = entries.Min(e => e.Utc).ToString("o");
			s.LastUtc = entries.Max(e => e.Utc).ToString("o");
			double Res(Entry e) => e.Metal / Resources.MetalPerDeuterium + e.Crystal / Resources.CrystalPerDeuterium + e.Deuterium;
			double Shp(Entry e) => e.Ships.Sum(kv => kv.Value * shipValueDeut(kv.Key));
			var days = entries.GroupBy(e => e.Utc.Date).ToDictionary(g => g.Key, g => (Res: g.Sum(e => Res(e)), Ship: g.Sum(e => Shp(e)), Dm: g.Sum(e => (double) e.DarkMatter)));
			s.DaysWithData = days.Count;
			DateTime today = now.Date;
			double Window(int n, Func<(double Res, double Ship, double Dm), double> f) {
				int span = Math.Max(1, Math.Min(n, (int) (today - entries.Min(e => e.Utc).Date).TotalDays + 1));
				double sum = days.Where(d => d.Key > today.AddDays(-n)).Sum(d => f(d.Value));
				return sum / span;
			}
			s.Avg7ResDeut = Window(7, v => v.Res);
			s.Avg30ResDeut = Window(30, v => v.Res);
			s.Avg7ShipsDeut = Window(7, v => v.Ship);
			s.Avg30ShipsDeut = Window(30, v => v.Ship);
			s.AvgDarkMatterPerDay30 = Window(30, v => v.Dm);
			var last60 = days.Where(d => d.Key > today.AddDays(-60)).Select(d => d.Value).ToList();
			if (last60.Count > 0) {
				s.Best7Of60ResDeut = last60.OrderByDescending(v => v.Res).Take(7).Average(v => v.Res);
				s.Best7Of60ShipsDeut = last60.OrderByDescending(v => v.Ship).Take(7).Average(v => v.Ship);
			}
			return s;
		}

		private void Save() {
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(_path));
				AtomicFile.WriteAllText(_path, JsonConvert.SerializeObject(_data, Formatting.Indented));
			} catch (Exception) {
			}
		}
	}
}
