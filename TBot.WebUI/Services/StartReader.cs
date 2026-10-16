using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;

namespace TBot.WebUI.Services {
	public static class StartReader {
		private static readonly string[] WorkerBlocks = {
			"Expeditions", "AutoFarm",
#if FASTFARM
			"FastFarm",
#endif
			"Defender", "SleepMode", "GhostSleep", "Brain", "AutoHarvest", "AutoColonize", "AutoDiscovery", "DebrisSave" 
		};

		private static bool RunsInOverride(string name) {
#if FASTFARM
			if (name == "FastFarm") return true;
#endif
			return name == "Defender" || name == "SleepMode" || name == "GhostSleep";
		}

		public static JObject Read(string alias, JObject mainConfig, bool legacy, bool modeOverride, bool keepExpeditions) {
			var result = new JObject();
			result["instance"] = alias;
			result["legacy"] = legacy;

			JObject? status = null;
			long ageSec = -1;
			try {
				string dir = AppContext.BaseDirectory;
				string perInstance = Path.Combine(dir, "data", Path.GetFileName($"status_{alias}.json"));
				string path = File.Exists(perInstance) ? perInstance : Path.Combine(dir, "sleepstatus.json");
				if (File.Exists(path)) {
					using var reader = new JsonTextReader(new StringReader(File.ReadAllText(path))) { DateParseHandling = DateParseHandling.None };
					status = JObject.Load(reader);
					long? updated = status.Value<long?>("updatedMs");
					if (updated.HasValue)
						ageSec = Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - updated.Value) / 1000);
				}
			} catch {
				status = null;
			}
			result["status"] = status;
			result["ageSec"] = ageSec;

			var workers = new JArray();
			foreach (var name in WorkerBlocks) {
				if (mainConfig[name] is not JObject block || block["Active"]?.Type != JTokenType.Boolean)
					continue;
				bool active = (bool) block["Active"]!;
				bool overridden = modeOverride && !RunsInOverride(name) && !(name == "Expeditions" && keepExpeditions);
				workers.Add(new JObject { ["name"] = name, ["active"] = active, ["overridden"] = overridden });
			}
			result["workers"] = workers;
			result["modeOverride"] = modeOverride;
			result["events"] = ReadEvents(12);
			return result;
		}

		private sealed class Row {
			public string Type = "", Sender = "", Time = "", Message = "";
		}

		private static JArray ReadEvents(int max) {
			var events = new JArray();
			try {
				string file = Path.Combine(SettingsService.LogsPath, $"TBot{DateTime.Now:yyyyMMdd}.csv");
				if (!File.Exists(file))
					return events;
				string tail;
				using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) {
					long take = Math.Min(fs.Length, 262144);
					fs.Seek(-take, SeekOrigin.End);
					var buf = new byte[take];
					int read = 0;
					while (read < take) {
						int n = fs.Read(buf, read, (int) take - read);
						if (n <= 0)
							break;
						read += n;
					}
					tail = Encoding.UTF8.GetString(buf, 0, read);
					if (fs.Length > take) {
						int nl = tail.IndexOf('\n');
						tail = nl >= 0 ? tail.Substring(nl + 1) : "";
					}
				}
				var rows = new List<Row>();
				var cfg = new CsvConfiguration(CultureInfo.InvariantCulture) { HasHeaderRecord = false, BadDataFound = null, MissingFieldFound = null };
				using (var csv = new CsvReader(new StringReader(tail), cfg)) {
					while (true) {
						try {
							if (!csv.Read())
								break;
							string type = csv.GetField(0) ?? "";
							if (type == "type")
								continue;
							rows.Add(new Row { Type = type, Sender = csv.GetField(1) ?? "", Time = csv.GetField(2) ?? "", Message = csv.GetField(3) ?? "" });
						} catch {
						}
					}
				}
				foreach (var r in rows.Where(r => r.Type == "Information" || r.Type == "Warning" || r.Type == "Error").Reverse().Take(max)) {
					string time = DateTime.TryParse(r.Time, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.ToString("HH:mm:ss") : "";
					string msg = r.Message.Replace("\r", " ").Replace("\n", " ");
					if (msg.Length > 200)
						msg = msg.Substring(0, 200) + "…";
					events.Add(new JObject { ["time"] = time, ["type"] = r.Type, ["sender"] = r.Sender.Trim('"'), ["message"] = msg });
				}
			} catch {
			}
			return events;
		}
	}
}
