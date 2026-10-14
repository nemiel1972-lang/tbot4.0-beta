using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using TBot.WebUI.Models;

namespace TBot.WebUI.Services {
	// Liest das Bauwunsch-Protokoll (data/build_requests_*.json), das die Brain-Worker schreiben,
	// und bereitet es fuer die Seite "Autobuilds" auf. Nur lesend - es wird nichts veraendert.
	public sealed class AutobuildsReader {
		// Koordinate steht im Celestial-Namen, z.B. "Plan 4 [P:1:100:8]" oder "... [M:1:100:8]".
		private static readonly Regex CoordPattern = new(@"^(?<name>.*?)\s*\[(?<t>[PM]):(?<g>\d+):(?<s>\d+):(?<p>\d+)\]", RegexOptions.Compiled);

		// Feste Reihenfolge der vier Worker unter jedem Celestial.
		private static readonly string[] WorkerOrder = { "AutoMine", "AutoResearch", "LifeformsAutoMine", "LifeformsAutoResearch" };

		// Status-Texte frueherer Staende (deutsch) auf die englischen abbilden, damit alte build_requests_*.json weiter passen.
		private static string St(Newtonsoft.Json.Linq.JToken e) {
			switch (e.Value<string>("Status")) {
				case "Wartet auf Lieferung": return "Waiting for delivery";
				case "Lieferung unterwegs": return "Delivery in transit";
				case "Bestand gedeckt": return "Stock covered";
				case "Gebaut": return "Built";
				case "Abgeloest": return "Replaced";
				case null: return "";
				default: return e.Value<string>("Status");
			}
		}

		public AutobuildsModel Read(string instanceAlias) {
			var model = new AutobuildsModel();
			try {
				var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
				var file = FindFile(dataDir, instanceAlias);
				if (file == null)
					return model;

				var root = ReadJson(file);
				if (root == null) {
					model.Error = "File could not be read (it may be being written right now).";
					return model;
				}

				model.DataAvailable = true;
				model.SourceFile = Path.GetFileName(file);
				model.UpdatedUtc = ParseDate(root.Value<string>("UpdatedUtc"));

				var open = (root["Open"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();
				var history = (root["History"] as JArray)?.OfType<JObject>().ToList() ?? new List<JObject>();

				var bodies = new Dictionary<long, AutobuildBody>();

				// Offene Wuensche -> Spalte "Naechster Bau"
				foreach (var e in open) {
					var row = GetWorker(bodies, e);
					row.Next = new AutobuildNext {
						Label = e.Value<string>("Label") ?? "",
						Status = St(e),
						FleetId = e.Value<int?>("FleetId") ?? 0,
						LastDelivery = e.Value<string>("LastDelivery"),
						TimesSeen = e.Value<int?>("TimesSeen") ?? 0,
						FirstRequestedUtc = ParseDate(e.Value<string>("FirstRequestedUtc")),
						LastSeenUtc = ParseDate(e.Value<string>("LastSeenUtc")),
						CostM = e.Value<long?>("CostM") ?? 0,
						CostC = e.Value<long?>("CostC") ?? 0,
						CostD = e.Value<long?>("CostD") ?? 0,
						MissingM = e.Value<long?>("MissingM") ?? 0,
						MissingC = e.Value<long?>("MissingC") ?? 0,
						MissingD = e.Value<long?>("MissingD") ?? 0,
						RoiDays = e.Value<float?>("RoiDays") ?? 0f
					};
				}

				// Historie ist neueste-zuerst: pro Worker+Celestial der erste "Built"-Eintrag ist der zuletzt gestartete Bau.
				var since24h = DateTime.UtcNow.AddHours(-24);
				var builtLast24h = 0;
				foreach (var e in history) {
					if (St(e) != "Built")
						continue;
					var closed = ParseDate(e.Value<string>("ClosedUtc"));
					if (closed.HasValue && closed.Value >= since24h)
						builtLast24h++;
					var row = GetWorker(bodies, e);
					if (row.Building != null)
						continue;
					row.Building = new AutobuildStarted {
						Label = e.Value<string>("Label") ?? "",
						StartedUtc = closed,
						CostM = e.Value<long?>("CostM") ?? 0,
						CostC = e.Value<long?>("CostC") ?? 0,
						CostD = e.Value<long?>("CostD") ?? 0
					};
				}

				// Sortierung: Koordinate, Planet vor Mond; Worker in fester Reihenfolge.
				foreach (var b in bodies.Values)
					b.Workers = b.Workers
						.OrderBy(w => { var i = Array.IndexOf(WorkerOrder, w.Owner); return i < 0 ? 99 : i; })
						.ThenBy(w => w.Owner, StringComparer.OrdinalIgnoreCase)
						.ToList();
				model.Bodies = bodies.Values
					.OrderBy(b => SortKey(b.GroupKey))
					.ThenBy(b => b.IsMoon ? 1 : 0)
					.ThenBy(b => b.CelestialId)
					.ToList();

				model.Summary = new AutobuildsSummary {
					Open = open.Count,
					Waiting = open.Count(e => St(e) == "Waiting for delivery"),
					InFlight = open.Count(e => St(e) == "Delivery in transit"),
					Funded = open.Count(e => St(e) == "Stock covered"),
					BuiltLast24h = builtLast24h
				};

				// Bester/schlechtester ROI: nur offene Wuensche mit bekanntem ROI (> 0 Tage)
				var withRoi = bodies.Values
					.SelectMany(b => b.Workers.Where(w => w.Next != null && w.Next.RoiDays > 0f).Select(w => new { b, w }))
					.ToList();
				model.Summary.RoiCount = withRoi.Count;
				if (withRoi.Count > 0) {
					var best = withRoi.OrderBy(x => x.w.Next!.RoiDays).First();
					var worst = withRoi.OrderByDescending(x => x.w.Next!.RoiDays).First();
					model.Summary.BestRoi = ToRoiInfo(best.b, best.w);
					model.Summary.WorstRoi = ToRoiInfo(worst.b, worst.w);
				}

				model.History = history.Take(60).Select(e => new AutobuildHistoryRow {
					Owner = e.Value<string>("Owner") ?? "",
					Celestial = e.Value<string>("Celestial") ?? "",
					Label = e.Value<string>("Label") ?? "",
					Status = St(e),
					ClosedUtc = ParseDate(e.Value<string>("ClosedUtc"))
				}).ToList();
			} catch (Exception ex) {
				model.Error = ex.Message;
			}
			return model;
		}

		private static AutobuildRoiInfo ToRoiInfo(AutobuildBody b, AutobuildWorkerRow w) {
			return new AutobuildRoiInfo {
				Owner = w.Owner,
				Celestial = b.Name,
				Coordinate = b.Coordinate,
				Label = w.Next!.Label,
				Status = w.Next.Status,
				RoiDays = w.Next.RoiDays
			};
		}

		// Holt/erzeugt Celestial und Worker-Zeile zu einem JSON-Eintrag.
		private static AutobuildWorkerRow GetWorker(Dictionary<long, AutobuildBody> bodies, JObject e) {
			var id = e.Value<long?>("CelestialId") ?? 0;
			if (!bodies.TryGetValue(id, out var body)) {
				var full = e.Value<string>("Celestial") ?? "";
				var m = CoordPattern.Match(full);
				body = new AutobuildBody { CelestialId = id };
				if (m.Success) {
					body.Name = m.Groups["name"].Value.Trim();
					body.IsMoon = m.Groups["t"].Value == "M";
					body.Coordinate = $"{m.Groups["g"].Value}:{m.Groups["s"].Value}:{m.Groups["p"].Value}";
					body.GroupKey = body.Coordinate; // Planet und Mond teilen sich die Koordinate
				} else {
					body.Name = full;
					body.GroupKey = "id" + id;
				}
				bodies[id] = body;
			}
			var owner = e.Value<string>("Owner") ?? "";
			var row = body.Workers.FirstOrDefault(w => w.Owner == owner);
			if (row == null) {
				row = new AutobuildWorkerRow { Owner = owner };
				body.Workers.Add(row);
			}
			return row;
		}

		private static long SortKey(string groupKey) {
			var p = groupKey.Split(':');
			if (p.Length == 3 && int.TryParse(p[0], out var g) && int.TryParse(p[1], out var s) && int.TryParse(p[2], out var pos))
				return g * 1_000_000L + s * 100L + pos;
			return long.MaxValue;
		}

		// Sucht die passende build_requests-Datei. Bei mehreren Dateien: Name enthaelt Alias, sonst die neueste.
		private static string? FindFile(string dataDir, string alias) {
			if (!Directory.Exists(dataDir))
				return null;
			var files = Directory.GetFiles(dataDir, "build_requests_*.json");
			if (files.Length == 0)
				return null;
			if (files.Length == 1)
				return files[0];
			var byAlias = files.FirstOrDefault(f => Path.GetFileName(f).Contains(alias, StringComparison.OrdinalIgnoreCase));
			return byAlias ?? files.OrderByDescending(File.GetLastWriteTimeUtc).First();
		}

		// Lesen mit FileShare.ReadWrite, kurzer Retry falls der Bot gerade schreibt.
		private static JObject? ReadJson(string path) {
			for (var i = 0; i < 3; i++) {
				try {
					using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
					using var sr = new StreamReader(fs);
					return JObject.Parse(sr.ReadToEnd());
				} catch {
					Thread.Sleep(80);
				}
			}
			return null;
		}

		private static DateTime? ParseDate(string? value) {
			return string.IsNullOrWhiteSpace(value) ||
				!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
				? null
				: d;
		}
	}
}