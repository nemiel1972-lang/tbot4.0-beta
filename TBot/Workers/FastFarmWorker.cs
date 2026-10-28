using Tbot.Common.IO;
#if FASTFARM
using System;
using System.Collections.Generic;
using System.Globalization;
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
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class FastFarmWorker : WorkerBase {
		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(30);

		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		private readonly object _stateLock = new object();
		private FastFarmState _state;
		private bool _probesShort;
		private int _nextDelayMs = 30000;

		public FastFarmWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.FastFarm.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "FastFarm";
		}
		public override Feature GetFeature() {
			return Feature.FastFarm;
		}
		public override LogSender GetLogSender() {
			return LogSender.FastFarm;
		}


		private sealed class Cfg {
			public Buildables[] CargoTypes;
			public int ProbesPerTarget;
			public int SlotsToLeaveFree;
			public double WeightMetal, WeightCrystal, WeightDeuterium;
			public long MinLoot;
			public long MinDeuterium;
			public int TopTargets;
			public int ReportMaxAgeMinutes;
			public bool DeleteReports;
			public int NoReportHours;
			public int FleetSpeedPercent;
			public double CargoSurplusPercent;
			public double MinFillPercent;
			public int MaxSystemsPerScanStep;
			public double ShrinkDiscardPercent;
		}

		private double Num(string name, double def) {
			try {
				var s = _tbotInstance.InstanceSettings.FastFarm;
				if (!SettingsService.IsSettingSet(s, name))
					return def;
				return Convert.ToDouble(s[name], CultureInfo.InvariantCulture);
			} catch (Exception) {
				return def;
			}
		}

		private bool Flag(string name, bool def) {
			try {
				var s = _tbotInstance.InstanceSettings.FastFarm;
				if (!SettingsService.IsSettingSet(s, name))
					return def;
				return (bool) s[name];
			} catch (Exception) {
				return def;
			}
		}

		private string Text(string name, string def) {
			try {
				var s = _tbotInstance.InstanceSettings.FastFarm;
				if (!SettingsService.IsSettingSet(s, name))
					return def;
				return Convert.ToString(s[name]) ?? def;
			} catch (Exception) {
				return def;
			}
		}

		private Cfg ReadConfig() {
			string cargo = Text("CargoType", "SmallCargo").Trim();
			Buildables[] cargoTypes;
			if (string.Equals(cargo, "LargeCargo", StringComparison.OrdinalIgnoreCase))
				cargoTypes = new[] { Buildables.LargeCargo };
			else if (string.Equals(cargo, "Both", StringComparison.OrdinalIgnoreCase))
				cargoTypes = new[] { Buildables.SmallCargo, Buildables.LargeCargo };
			else
				cargoTypes = new[] { Buildables.SmallCargo };

			return new Cfg {
				CargoTypes = cargoTypes,
				ProbesPerTarget = Math.Max(1, (int) Num("ProbesPerTarget", 20)),
				SlotsToLeaveFree = Math.Max(0, (int) Num("SlotsToLeaveFree", 1)),
				WeightMetal = Num("WeightMetal", 3.0),
				WeightCrystal = Num("WeightCrystal", 2.0),
				WeightDeuterium = Num("WeightDeuterium", 1.0),
				MinLoot = (long) Num("MinLoot", 100000),
				MinDeuterium = (long) Num("MinDeuterium", 0),
				TopTargets = Math.Max(1, (int) Num("TopTargets", 36)),
				ReportMaxAgeMinutes = Math.Max(1, (int) Num("ReportMaxAgeMinutes", 30)),
				DeleteReports = Flag("DeleteReports", true),
				NoReportHours = Math.Max(0, (int) Num("NoReportHours", 24)),
				FleetSpeedPercent = Math.Clamp((int) Num("FleetSpeedPercent", 100), 10, 100),
				CargoSurplusPercent = Math.Max(0, Num("CargoSurplusPercent", 0)),
				MinFillPercent = Math.Clamp(Num("MinFillPercent", 50), 1, 100),
				MaxSystemsPerScanStep = Math.Max(1, (int) Num("MaxSystemsPerScanStep", 25)),
				ShrinkDiscardPercent = Math.Clamp(Num("ShrinkDiscardPercent", 10), 0, 100)
			};
		}

		private decimal GetSpeed(Cfg cfg) {
			decimal wanted = cfg.FleetSpeedPercent / 10m;
			var valid = _calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class);
			if (valid != null && valid.Count > 0 && !valid.Contains(wanted)) {
				var lower = valid.Where(v => v <= wanted).ToList();
				wanted = lower.Count > 0 ? lower.Max() : valid.Min();
			}
			return wanted;
		}


		public sealed class FastFarmTarget {
			public int Galaxy { get; set; }
			public int System { get; set; }
			public int Position { get; set; }
			public string Player { get; set; }
			public DateTime SeenUtc { get; set; }
			public bool Pending { get; set; }
			public DateTime ExpectedArrivalUtc { get; set; }
			public DateTime SpySentServer { get; set; }
			public long SpyFlightSec { get; set; }
			public DateTime? ReportServer { get; set; }
			public long Metal { get; set; }
			public long Crystal { get; set; }
			public long Deuterium { get; set; }
			public long Weighted { get; set; }
			public long LastWeighted { get; set; }
			public DateTime? BlockedUntilUtc { get; set; }
			public string BlockReason { get; set; }
			public DateTime? LastAttackUtc { get; set; }
			public int Attacks { get; set; }
			public int SpyFails { get; set; }
			public long SweepValue { get; set; }
			public bool Verify { get; set; }
			public string DefShips { get; set; }
			public string DefDefence { get; set; }
			public DateTime? DefReportServer { get; set; }
		}

		public sealed class FastFarmAttackLog {
			public DateTime TimeUtc { get; set; }
			public string Target { get; set; }
			public string Ships { get; set; }
			public long Metal { get; set; }
			public long Crystal { get; set; }
			public long Deuterium { get; set; }
			public long FlightSec { get; set; }
		}

		public sealed class FastFarmState {
			public Dictionary<string, FastFarmTarget> Targets { get; set; } = new Dictionary<string, FastFarmTarget>();
			public int ScanCursor { get; set; }
			public string SweepPhase { get; set; }
			public int SweepGalaxy { get; set; }
			public int SweepRound { get; set; }
			public Dictionary<string, DateTime> Ignored { get; set; } = new Dictionary<string, DateTime>();
			public string ScanSignature { get; set; }
			public long AttacksSent { get; set; }
			public long ProbesSent { get; set; }
			public long ReportsRead { get; set; }
			public long ExpectedMetal { get; set; }
			public long ExpectedCrystal { get; set; }
			public long ExpectedDeuterium { get; set; }
			public List<FastFarmAttackLog> Recent { get; set; } = new List<FastFarmAttackLog>();
			public string Status { get; set; }
			public int FreeSlots { get; set; }
			public int ProbesOut { get; set; }
			public int AttacksOut { get; set; }
			public string FastestTarget { get; set; }
			public long FastestFlightSec { get; set; }
			public string LongestTarget { get; set; }
			public long LongestFlightSec { get; set; }
			public DateTime UpdatedUtc { get; set; }
			public DateTime NextRunUtc { get; set; }
			public DateTime? LastResetUtc { get; set; }
		}

		private string StatePath() {
			string alias = Path.GetFileName(_tbotInstance.InstanceAlias ?? "default");
			string dir = Path.Combine(AppContext.BaseDirectory, "data");
			Directory.CreateDirectory(dir);
			return Path.Combine(dir, $"fastfarm_{alias}.json");
		}

		private void LoadState() {
			if (_state != null)
				return;
			try {
				string path = StatePath();
				if (File.Exists(path))
					_state = JsonConvert.DeserializeObject<FastFarmState>(File.ReadAllText(path));
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Could not read the state file, starting empty: {e.Message}");
			}
			_state ??= new FastFarmState();
			_state.Targets ??= new Dictionary<string, FastFarmTarget>();
			_state.Recent ??= new List<FastFarmAttackLog>();
			_state.Ignored ??= new Dictionary<string, DateTime>();
		}

		private (string key, bool desc) ReadSort() {
			try {
				string path = Path.Combine(Path.GetDirectoryName(StatePath()), $"fastfarm_{Path.GetFileName(_tbotInstance.InstanceAlias ?? "default")}_sort.json");
				if (File.Exists(path)) {
					var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(path));
					string key = (o.Value<string>("key") ?? "value").ToLowerInvariant();
					if (key == "value" || key == "metal" || key == "crystal" || key == "deuterium" || key == "target")
						return (key, !string.Equals(o.Value<string>("dir"), "asc", StringComparison.OrdinalIgnoreCase));
				}
			} catch (Exception) { }
			return ("value", true);
		}

		private static long SortValue(FastFarmTarget t, string key) {
			switch (key) {
				case "metal": return t.Metal;
				case "crystal": return t.Crystal;
				case "deuterium": return t.Deuterium;
				case "target": return t.Galaxy * 1000000L + t.System * 1000L + t.Position;
				default: return t.ReportServer.HasValue && t.Weighted > 0 ? t.Weighted : (t.SweepValue > 0 ? t.SweepValue : t.Weighted);
			}
		}

		private IOrderedEnumerable<FastFarmTarget> Ordered(IEnumerable<FastFarmTarget> src) {
			var (key, desc) = ReadSort();
			return desc ? src.OrderByDescending(t => SortValue(t, key)) : src.OrderBy(t => SortValue(t, key));
		}

		private void ApplyDeleteRequests() {
			try {
				string path = Path.Combine(Path.GetDirectoryName(StatePath()), $"fastfarm_{Path.GetFileName(_tbotInstance.InstanceAlias ?? "default")}_delete.json");
				var now = DateTime.UtcNow;
				foreach (var k in _state.Ignored.Where(kv => kv.Value < now).Select(kv => kv.Key).ToList())
					_state.Ignored.Remove(k);
				if (!File.Exists(path))
					return;
				var keys = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(path)) ?? new List<string>();
				File.Delete(path);
				int removed = 0;
				foreach (var key in keys.Distinct()) {
					if (_state.Targets.Remove(key))
						removed++;
					_state.Ignored[key] = now.AddHours(24);
				}
				if (keys.Count > 0)
					DoLog(LogLevel.Information, $"{removed} target(s) removed on request from the WebUI (not taken in again for 24 h).");
				SaveState();
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Could not process delete requests: {e.Message}");
			}
		}

		private void SaveState() {
			try {
				lock (_stateLock) {
					_state.UpdatedUtc = DateTime.UtcNow;
					string path = StatePath();
					AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(_state, Formatting.Indented));
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Could not write the state file: {e.Message}");
			}
		}

		private static string Key(Coordinate c) => $"{c.Galaxy}:{c.System}:{c.Position}";
		private static string Key(FastFarmTarget t) => $"{t.Galaxy}:{t.System}:{t.Position}";
		private static Coordinate Coord(FastFarmTarget t) => new Coordinate(t.Galaxy, t.System, t.Position, Celestials.Planet);

		private void SetStatus(string text) {
			_state.Status = text;
			DoLog(LogLevel.Information, text);
		}


		private sealed class OriginState {
			public Celestial Celestial;
			public long ProbesLeft;
			public Dictionary<Buildables, long> CargoLeft = new Dictionary<Buildables, long>();
		}

		private static readonly HashSet<string> _seitStartGelaufen = new();

		private void PruefeReset() {
			bool aktiv = Flag("ResetOnActivate", true);
			double minHours = Num("ResetMinHours", 6);
			bool prozessStart;
			lock (_seitStartGelaufen)
				prozessStart = _seitStartGelaufen.Add(_tbotInstance.InstanceAlias ?? "default");
			var e = FastFarmReset.Entscheide(aktiv, prozessStart, _state.NextRunUtc, _state.LastResetUtc, DateTime.UtcNow, minHours);
			if (e.Ausloeser == null)
				return;
			if (!aktiv) {
				DoLog(LogLevel.Debug, $"Reset on {e.Ausloeser}: switched off (ResetOnActivate = false), keeping the old data.");
				return;
			}
			if (!e.Zuruecksetzen) {
				DoLog(LogLevel.Information, $"Reset on {e.Ausloeser}: skipped, last reset was less than {minHours:0.#} h ago (next possible at {e.GesperrtBis.Value.ToLocalTime():dd.MM. HH:mm}).");
				return;
			}
			int ziele = _state.Targets.Count, gesperrt = _state.Targets.Values.Count(t => IsBlocked(t)), ignoriert = _state.Ignored.Count;
			try {
				string path = StatePath();
				if (File.Exists(path))
					File.Copy(path, path + ".bak", true);
			} catch (Exception ex) {
				DoLog(LogLevel.Debug, $"Could not back up the state file: {ex.Message}");
			}
			_state.Targets.Clear();
			_state.Ignored.Clear();
			_state.ScanCursor = 0;
			_state.SweepPhase = null;
			_state.SweepGalaxy = 0;
			_state.SweepRound = 0;
			_state.ScanSignature = null;
			_state.LastResetUtc = DateTime.UtcNow;
			DoLog(LogLevel.Information, $"Reset on {e.Ausloeser}: old data discarded ({ziele} targets, {gesperrt} blocked, {ignoriert} ignored); statistics kept. Next reset possible after {minHours:0.#} h.");
			SaveState();
		}

		protected override async Task Execute() {
			bool stop = false;
			_nextDelayMs = 30000;
			try {
				LoadState();
				PruefeReset();
				ApplyDeleteRequests();
				Cfg cfg = ReadConfig();
				stop = await RunCycle(cfg);
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"FastFarm exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				_nextDelayMs = 30000;
			} finally {
				if (_state != null) {
					_state.NextRunUtc = stop ? default(DateTime) : DateTime.UtcNow.AddMilliseconds(Math.Max(2000, _nextDelayMs));
					SaveState();
				}
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, "Stopping feature.");
						await EndExecution();
					} else {
						ChangeWorkerPeriod(Math.Max(2000, _nextDelayMs));
						DoLog(LogLevel.Information, $"Next FastFarm cycle in {TimeSpan.FromMilliseconds(Math.Max(2000, _nextDelayMs)):mm\\:ss}.");
					}
				}
			}
		}

		private async Task<bool> RunCycle(Cfg cfg) {
			_probesShort = false;
			var origins = await PrepareOrigins(cfg);
			if (origins.Count == 0) {
				SetStatus("No usable origin celestial found (check FastFarm.Origin).");
				_nextDelayMs = 60000;
				return false;
			}

			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

			await CollectReports(cfg);

			PruneOutOfRange(origins);

			{
				AdvanceSweep(cfg);
				if (_state.SweepPhase == "scan") {
					DateTime scanUntil = DateTime.UtcNow.AddSeconds(60);
					bool more = true;
					while (more && DateTime.UtcNow < scanUntil)
						more = await ScanStep(cfg, origins, cfg.MaxSystemsPerScanStep, scanUntil);
					if (!more) {
						_state.SweepPhase = "sweep";
						DoLog(LogLevel.Information, $"Sweep round {_state.SweepRound}, galaxy {_state.SweepGalaxy}: scan done, {_state.Targets.Count} inactive targets found.");
					}
					SetStatus(BuildStatus(0));
					_nextDelayMs = 2000;
					return false;
				}
			}

			int free = _tbotInstance.UserData.slots.Free - cfg.SlotsToLeaveFree;
			bool bedTime = false;

			if (free > 0 && _state.SweepPhase == "verify") {
				var attack = await AttackPhase(cfg, origins, free);
				free = attack.freeLeft;
				bedTime = attack.bedTime;
			}
			int probesSentNow = 0;
			if (!bedTime && free > 0) {
				var spy = await SweepSpyPhase(cfg, origins, free);
				probesSentNow = spy.sent;
				bedTime = spy.bedTime;
			}
			if (bedTime) {
				SetStatus("Bed time reached, no more fleets are sent.");
				_nextDelayMs = 60000;
				return false;
			}

			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			RefreshCounters();

			double nextSec = double.MaxValue;
			DateTime nowUtc = DateTime.UtcNow;
			foreach (var t in _state.Targets.Values.Where(t => t.Pending))
				nextSec = Math.Min(nextSec, Math.Max(2, (t.ExpectedArrivalUtc - nowUtc).TotalSeconds + 2));
			var backs = _tbotInstance.UserData.fleets
				.Where(f => f.BackIn.HasValue && f.BackIn.Value > 0)
				.Select(f => (double) f.BackIn.Value + 2).ToList();
			bool slotsFull = _tbotInstance.UserData.slots.Free - cfg.SlotsToLeaveFree - probesSentNow <= 0 || free <= 0;
			if ((slotsFull || _probesShort) && backs.Count > 0)
				nextSec = Math.Min(nextSec, backs.Min());
			if (nextSec == double.MaxValue)
				nextSec = 60;

			SetStatus(BuildStatus(free));

			_nextDelayMs = (int) Math.Min(nextSec * 1000, 15 * 60 * 1000);
			return false;
		}

		private void RefreshCounters() {
			var fleets = _tbotInstance.UserData.fleets ?? new List<Fleet>();
			_state.FreeSlots = _tbotInstance.UserData.slots?.Free ?? 0;
			_state.ProbesOut = fleets.Count(f => f.Mission == Missions.Spy);
			_state.AttacksOut = fleets.Count(f => f.Mission == Missions.Attack);
		}

		private string BuildStatus(int freeLeft) {
			int pending = _state.Targets.Values.Count(t => t.Pending);
			int ready = _state.Targets.Values.Count(t => t.ReportServer.HasValue && !IsBlocked(t));
			return $"Sweep round {_state.SweepRound}, galaxy {_state.SweepGalaxy}, phase {_state.SweepPhase}: targets {_state.Targets.Count}, swept {_state.Targets.Values.Count(t => t.SweepValue > 0)}, fresh reports {ready}, probes pending {pending}, attacks out {_state.AttacksOut}, free slots {freeLeft}.";
		}

		private static bool IsBlocked(FastFarmTarget t) {
			return t.BlockedUntilUtc.HasValue && t.BlockedUntilUtc.Value > DateTime.UtcNow;
		}


		private async Task<List<OriginState>> PrepareOrigins(Cfg cfg) {
			var all = _tbotInstance.UserData.celestials.ToList();
			List<Celestial> chosen;
			bool configured = false;
			try {
				var s = _tbotInstance.InstanceSettings.FastFarm;
				configured = SettingsService.IsSettingSet(s, "Origin") && s.Origin.Length > 0;
			} catch (Exception) { }
			if (configured)
				chosen = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.FastFarm.Origin, all);
			else
				chosen = all.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

			var result = new List<OriginState>();
			foreach (var c in chosen) {
				Celestial updated = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
				updated = await _tbotOgameBridge.UpdatePlanet(updated, UpdateTypes.LFBonuses);
				var st = new OriginState { Celestial = updated, ProbesLeft = updated.Ships?.EspionageProbe ?? 0 };
				foreach (var type in cfg.CargoTypes)
					st.CargoLeft[type] = type == Buildables.SmallCargo ? (updated.Ships?.SmallCargo ?? 0) : (updated.Ships?.LargeCargo ?? 0);
				result.Add(st);
			}
			return result;
		}

		private int Distance(Coordinate a, Coordinate b) {
			return _calculationService.CalcDistance(a, b, _tbotInstance.UserData.serverData);
		}

		private IEnumerable<OriginState> ByDistance(List<OriginState> origins, Coordinate target) {
			return origins.OrderBy(o => Distance(o.Celestial.Coordinate, target));
		}


		private readonly HashSet<int> _bekannteBerichte = new HashSet<int>();
		private readonly HashSet<int> _eigeneGeloescht = new HashSet<int>();
		private DateTime? _loeschErkanntUtc;
		private DateTime _zeitversatzLogUtc = DateTime.MinValue;
		private DateTime _berichtPauseBis = DateTime.MinValue;

		private async Task<Dictionary<string, int>> LeseBerichtsIndex() {
			try {
				var liste = await _ogameService.GetEspionageReports();
				if (liste == null)
					return null;
				var ids = new HashSet<int>(liste.Select(r => r.ID));
				int weg = _bekannteBerichte.Count(id => !ids.Contains(id) && !_eigeneGeloescht.Contains(id));
				if (weg > 0) {
					_loeschErkanntUtc = DateTime.UtcNow;
					DoLog(LogLevel.Information, $"{weg} espionage report(s) vanished without FastFarm deleting them (deleted by hand or by something else) - missing reports of probes that arrived before are not counted as failures.");
				}
				_bekannteBerichte.Clear();
				foreach (var id in ids)
					_bekannteBerichte.Add(id);
				_eigeneGeloescht.Clear();

				var index = new Dictionary<string, int>();
				foreach (var r in liste) {
					if (r.Type != EspionageReportType.Report || r.Target == null)
						continue;
					string k = $"{r.Target.Galaxy}:{r.Target.System}:{r.Target.Position}";
					if (!index.TryGetValue(k, out int alt) || r.ID > alt)
						index[k] = r.ID;
				}
				return index;
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Could not read the report inbox: {e.Message}");
				return null;
			}
		}

		private async Task CollectReports(Cfg cfg) {
			var due = _state.Targets.Values.Where(t => t.Pending && t.ExpectedArrivalUtc <= DateTime.UtcNow.AddSeconds(1)).ToList();
			if (due.Count == 0)
				return;
			var berichtsIndex = await LeseBerichtsIndex();
			CharacterClass playerClass = _tbotInstance.UserData.userInfo.Class;
			var ueberfaellig = due.Where(x => DateTime.UtcNow > x.ExpectedArrivalUtc.AddSeconds(120)).ToList();
			bool sammelausfall = berichtsIndex != null && ueberfaellig.Count >= 5
				&& !ueberfaellig.Any(x => berichtsIndex.ContainsKey($"{x.Galaxy}:{x.System}:{x.Position}"));
			if (sammelausfall) {
				_berichtPauseBis = DateTime.UtcNow.AddMinutes(10);
				DoLog(LogLevel.Warning, $"None of {ueberfaellig.Count} overdue probe targets has a report in the inbox ({berichtsIndex.Count} reports there). Not counted as target failures; no new probes for 10 minutes. Check that nothing deletes espionage reports (browser add-on, manual cleanup) and that the report tab is readable.");
			}
			foreach (var t in due) {
				Heartbeat();
				EspionageReport report = null;
				try {
					if (berichtsIndex != null) {
						if (berichtsIndex.TryGetValue($"{t.Galaxy}:{t.System}:{t.Position}", out int msgId))
							report = await _ogameService.GetEspionageReport(msgId);
						else
							DoLog(LogLevel.Debug, $"{Key(t)}: no report in the inbox yet ({berichtsIndex.Count} reports there).");
					} else {
						report = await _ogameService.GetEspionageReport(Coord(t));
					}
				} catch (Exception e) {
					DoLog(LogLevel.Debug, $"{Key(t)}: no report yet ({e.Message}).");
				}

				bool fresh = report != null && report.Coordinate != null && report.Date >= t.SpySentServer.AddSeconds(-10);
				if (report != null && report.Coordinate != null && !fresh) {
					for (int k = 1; k <= 2 && !fresh; k++) {
						double d = (report.Date.AddHours(k) - t.SpySentServer).TotalSeconds;
						if (d >= 0 && d <= 240) {
							fresh = true;
							report.Date = report.Date.AddHours(k);
							if ((DateTime.UtcNow - _zeitversatzLogUtc).TotalMinutes > 30) {
								_zeitversatzLogUtc = DateTime.UtcNow;
								DoLog(LogLevel.Warning, $"Report times are {k} h behind the server time (clock change? the ogamed proxy keeps the time zone offset from its login). Corrected for now; restart the whole bot service to fix it permanently.");
							}
						}
					}
				}
				if (report != null && !fresh)
					DoLog(LogLevel.Debug, $"{Key(t)}: report {report.ID} found, but too old: report time {report.Date:yyyy-MM-dd HH:mm:ss}, probes sent (server time) {t.SpySentServer:yyyy-MM-dd HH:mm:ss}.");
				if (!fresh) {
					if (DateTime.UtcNow > t.ExpectedArrivalUtc.AddSeconds(120)) {
						string flottenInfo;
						try {
							var spyFleets = (_tbotInstance.UserData.fleets ?? new List<Fleet>()).Where(f => f.Mission == Missions.Spy && f.Destination != null
								&& f.Destination.Galaxy == t.Galaxy && f.Destination.System == t.System && f.Destination.Position == t.Position).ToList();
							var outbound = spyFleets.Where(f => !f.ReturnFlight).OrderByDescending(f => f.ArriveIn).FirstOrDefault();
							if (outbound != null) {
								t.ExpectedArrivalUtc = DateTime.UtcNow.AddSeconds(Math.Max(2, outbound.ArriveIn) + 2);
								DoLog(LogLevel.Debug, $"{Key(t)}: probes are still flying (arrive in {outbound.ArriveIn}s) - calculated arrival was too early, reading the report later.");
								continue;
							}
							flottenInfo = spyFleets.Count > 0 ? "probes already on their way back (arrived, but no report)" : "no probe fleet any more (destroyed, or already home)";
						} catch (Exception) {
							flottenInfo = "fleet state unknown";
						}
						if (_loeschErkanntUtc.HasValue && DateTime.UtcNow - _loeschErkanntUtc.Value < TimeSpan.FromHours(2) && t.ExpectedArrivalUtc <= _loeschErkanntUtc.Value) {
							t.Pending = false;
							DoLog(LogLevel.Information, $"{Key(t)}: report missing, but reports were deleted meanwhile - not counted as a failure, the target will be probed again.");
							continue;
						}
						t.Pending = false;
						if (sammelausfall)
							continue;
						t.SpyFails++;
						DoLog(LogLevel.Debug, $"{Key(t)}: no report arrived ({flottenInfo}); probes were sent {(DateTime.UtcNow - t.ExpectedArrivalUtc).TotalSeconds + t.SpyFlightSec:0} s ago, calculated one-way flight {t.SpyFlightSec} s.");
						if (t.SpyFails >= 3) {
							_state.Targets.Remove(Key(t));
							if (cfg.NoReportHours > 0) {
								_state.Ignored[Key(t)] = DateTime.UtcNow.AddHours(cfg.NoReportHours);
								DoLog(LogLevel.Information, $"{Key(t)}: no report 3 times in a row - not probed again for {cfg.NoReportHours} h.");
							}
						}
					}
					continue;
				}

				t.Pending = false;
				t.SpyFails = 0;
				_state.ReportsRead++;
				t.Player = report.Username ?? t.Player;

				ProcessSweepReport(cfg, t, report, playerClass);
				if (cfg.DeleteReports) {
					try {
						_eigeneGeloescht.Add(report.ID);
						await _ogameService.DeleteReport(report.ID);
					} catch (Exception e) {
						DoLog(LogLevel.Debug, $"{Key(t)}: could not delete report {report.ID}: {e.Message}");
					}
				}
			}
			SaveState();
		}

		private static long Weigh(Cfg cfg, Resources loot) {
			return (long) (loot.Metal * cfg.WeightMetal + loot.Crystal * cfg.WeightCrystal + loot.Deuterium * cfg.WeightDeuterium);
		}


		private async Task<(int freeLeft, bool bedTime)> AttackPhase(Cfg cfg, List<OriginState> origins, int free) {
			DateTime serverNow = await _tbotOgameBridge.GetDateTime();
			var fleets = _tbotInstance.UserData.fleets ?? new List<Fleet>();

			{
				foreach (var st in _state.Targets.Values.Where(x => x.ReportServer.HasValue && (serverNow - x.ReportServer.Value).TotalMinutes > cfg.ReportMaxAgeMinutes)) {
					st.ReportServer = null;
					st.Verify = false;
				}
			}

			var candidates = _state.Targets.Values
				.Where(t => t.ReportServer.HasValue
					&& !IsBlocked(t)
					&& (serverNow - t.ReportServer.Value).TotalMinutes <= cfg.ReportMaxAgeMinutes
					&& t.Weighted > 0
					&& (t.Metal + t.Crystal + t.Deuterium) >= cfg.MinLoot
					&& t.Deuterium >= cfg.MinDeuterium
					&& !fleets.Any(f => f.Mission == Missions.Attack && f.Destination != null && f.Destination.IsSame(Coord(t))))
				.ToList();
			candidates = Ordered(candidates).Take(cfg.TopTargets).ToList();
			if (candidates.Count == 0)
				return (free, false);

			decimal speed = GetSpeed(cfg);
			int sent = 0;
			foreach (var t in candidates) {
				if (free <= 0)
					break;
				Heartbeat();
				Coordinate dest = Coord(t);
				long lootTotal = t.Metal + t.Crystal + t.Deuterium;

				foreach (var origin in ByDistance(origins, dest)) {
					Ships ships = PlanShips(cfg, origin, lootTotal);
					if (ships == null)
						continue;

					int code = await _fleetScheduler.SendFleet(origin.Celestial, ships, dest, Missions.Attack, speed);
					if (code == (int) SendFleetCode.AfterSleepTime)
						return (free, true);
					if (code == (int) SendFleetCode.NotEnoughSlots)
						return (0, false);
					if (code <= (int) SendFleetCode.GenericError) {
						DoLog(LogLevel.Warning, $"Attack on {Key(t)} from {origin.Celestial.Coordinate} failed (code {code}, origin deuterium {origin.Celestial.Resources?.Deuterium}).");
						continue;
					}

					free--;
					sent++;
					origin.CargoLeft[Buildables.SmallCargo] = origin.CargoLeft.GetValueOrDefault(Buildables.SmallCargo) - ships.SmallCargo;
					origin.CargoLeft[Buildables.LargeCargo] = origin.CargoLeft.GetValueOrDefault(Buildables.LargeCargo) - ships.LargeCargo;

					_state.AttacksSent++;
					_state.ExpectedMetal += t.Metal;
					_state.ExpectedCrystal += t.Crystal;
					_state.ExpectedDeuterium += t.Deuterium;
					long flightSec = 0;
					try {
						flightSec = _calculationService.CalcFlightTime(origin.Celestial.Coordinate, dest, ships, Missions.Attack, speed,
							_tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.Celestial.LFBonuses,
							_tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
					} catch (Exception) {
						flightSec = 0;
					}
					flightSec *= 2;
					if (flightSec > 0) {
						if (_state.FastestFlightSec <= 0 || flightSec < _state.FastestFlightSec) {
							_state.FastestFlightSec = flightSec;
							_state.FastestTarget = Key(t);
						}
						if (flightSec > _state.LongestFlightSec) {
							_state.LongestFlightSec = flightSec;
							_state.LongestTarget = Key(t);
						}
					}
					_state.Recent.Add(new FastFarmAttackLog {
						TimeUtc = DateTime.UtcNow,
						Target = Key(t),
						Ships = ships.ToString(),
						Metal = t.Metal,
						Crystal = t.Crystal,
						Deuterium = t.Deuterium,
						FlightSec = flightSec
					});
					if (_state.Recent.Count > 60)
						_state.Recent.RemoveRange(0, _state.Recent.Count - 60);
					t.LastAttackUtc = DateTime.UtcNow;
					t.Attacks++;
					t.ReportServer = null;
					DoLog(LogLevel.Information, $"Attack sent: {Key(t)} loot ~{t.Metal}/{t.Crystal}/{t.Deuterium} (M/C/D) with {ships}.");
					_state.Targets.Remove(Key(t));
					break;
				}
			}
			if (sent > 0)
				SaveState();
			return (free, false);
		}

		private Ships PlanShips(Cfg cfg, OriginState origin, long lootTotal) {
			long needed = (long) Math.Ceiling(lootTotal * (1.0 + cfg.CargoSurplusPercent / 100.0));
			if (needed <= 0)
				return null;
			int hyper = _tbotInstance.UserData.researches.HyperspaceTechnology;
			var ships = new Ships();
			long carried = 0;

			foreach (var type in cfg.CargoTypes) {
				long left = origin.CargoLeft.GetValueOrDefault(type);
				if (left <= 0)
					continue;
				long one = _calculationService.CalcFleetCapacity(new Ships().Add(type, 1), _tbotInstance.UserData.serverData, hyper, origin.Celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
				if (one <= 0)
					continue;
				long want = (long) Math.Ceiling((double) (needed - carried) / one);
				long take = Math.Min(want, left);
				if (take <= 0)
					continue;
				ships.Add(type, take);
				carried += take * one;
				if (carried >= needed)
					break;
			}

			if (ships.IsEmpty())
				return null;
			if (carried < needed && carried < lootTotal * (cfg.MinFillPercent / 100.0))
				return null;
			return ships;
		}


		private async Task<(int sent, bool bedTime)> SendSpies(Cfg cfg, List<OriginState> origins, List<FastFarmTarget> pool, int free, bool verify) {
			int sent = 0;
			int failedSends = 0;
			foreach (var t in pool) {
				if (free <= 0)
					break;
				Heartbeat();
				Coordinate dest = Coord(t);
				OriginState origin = ByDistance(origins, dest).FirstOrDefault(o => o.ProbesLeft >= cfg.ProbesPerTarget);
				if (origin == null) {
					DoLog(LogLevel.Information, $"Not enough probes ({cfg.ProbesPerTarget} needed) on any origin, waiting for returning probe fleets.");
					_probesShort = true;
					break;
				}

				var ships = new Ships().Add(Buildables.EspionageProbe, cfg.ProbesPerTarget);
				DateTime serverNow = await _tbotOgameBridge.GetDateTime();
				int code = await _fleetScheduler.SendFleet(origin.Celestial, ships, dest, Missions.Spy, Speeds.HundredPercent);
				if (code == (int) SendFleetCode.AfterSleepTime)
					return (sent, true);
				if (code == (int) SendFleetCode.NotEnoughSlots)
					break;
				if (code == (int) SendFleetCode.TargetInvalid) {
					DoLog(LogLevel.Information, $"Target {Key(t)} is no longer a valid spy target (empty position or noob protection) - removed from the round.");
					_state.Targets.Remove(Key(t));
					continue;
				}
				if (code <= (int) SendFleetCode.GenericError) {
					failedSends++;
					DoLog(LogLevel.Warning, $"Probe dispatch to {Key(t)} from {origin.Celestial.Coordinate} failed (code {code}, origin deuterium {origin.Celestial.Resources?.Deuterium}). Reason: see the FleetScheduler line above.");
					if (failedSends >= 3) {
						DoLog(LogLevel.Warning, "Three dispatches in a row failed, trying again next cycle.");
						break;
					}
					continue;
				}

				FleetPrediction prediction = _calculationService.CalcFleetPrediction(origin.Celestial.Coordinate, dest, ships, Missions.Spy, Speeds.HundredPercent,
					_tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.Celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

				origin.ProbesLeft -= cfg.ProbesPerTarget;
				free--;
				sent++;
				_state.ProbesSent += cfg.ProbesPerTarget;
				t.Pending = true;
				t.Verify = verify;
				t.SpySentServer = serverNow;
				t.ExpectedArrivalUtc = DateTime.UtcNow.AddSeconds(prediction.Time);
				t.SpyFlightSec = (long) prediction.Time;
			}
			if (sent > 0) {
				DoLog(LogLevel.Information, $"{sent} probe fleets sent ({cfg.ProbesPerTarget} probes each).");
				SaveState();
			}
			return (sent, false);
		}


		private void AdvanceSweep(Cfg cfg) {
			if (string.IsNullOrEmpty(_state.SweepPhase)) {
				_state.SweepPhase = "scan";
				_state.ScanCursor = 0;
				if (_state.SweepRound <= 0)
					_state.SweepRound = 1;
			}
			if (_state.SweepPhase == "sweep") {
				var fl = _tbotInstance.UserData.fleets ?? new List<Fleet>();
				bool open = _state.Targets.Values.Any(t => t.Pending || (!IsBlocked(t) && t.SweepValue == 0
					&& !fl.Any(f => (f.Mission == Missions.Attack || f.Mission == Missions.Spy) && f.Destination != null && f.Destination.IsSame(Coord(t)))));
				if (!open) {
					_state.SweepPhase = "verify";
					DoLog(LogLevel.Information, $"Sweep round {_state.SweepRound}: all targets spied, {_state.Targets.Values.Count(t => t.SweepValue > 0)} worth checking again, best first.");
				}
			}
			if (_state.SweepPhase == "verify") {
				bool open = _state.Targets.Values.Any(t => t.Pending || t.ReportServer.HasValue || (!IsBlocked(t) && t.SweepValue > 0));
				if (!open) {
					foreach (var key in _state.Targets.Where(kv => !kv.Value.Pending && !IsBlocked(kv.Value)).Select(kv => kv.Key).ToList())
						_state.Targets.Remove(key);
					_state.SweepRound++;
					_state.SweepGalaxy = NextSweepGalaxy();
					_state.ScanCursor = 0;
					_state.SweepPhase = "scan";
					DoLog(LogLevel.Information, $"Sweep round finished. Next round {_state.SweepRound} starts in galaxy {_state.SweepGalaxy}.");
				}
			}
		}

		private List<int> SweepGalaxies = new List<int>();

		private int NextSweepGalaxy() {
			if (SweepGalaxies.Count == 0)
				return _state.SweepGalaxy;
			int idx = SweepGalaxies.IndexOf(_state.SweepGalaxy);
			return SweepGalaxies[(idx + 1) % SweepGalaxies.Count];
		}

		private List<(int galaxy, int system)> CurrentScanList(Cfg cfg, List<OriginState> origins) {
			var list = BuildScanList(origins);
			SweepGalaxies = list.Select(x => x.galaxy).Distinct().ToList();
			if (SweepGalaxies.Count == 0)
				return list;
			if (!SweepGalaxies.Contains(_state.SweepGalaxy))
				_state.SweepGalaxy = SweepGalaxies[0];
			return list.Where(x => x.galaxy == _state.SweepGalaxy).ToList();
		}

		private static string UnitList(EspionageReport report, params string[] fields) {
			var parts = new List<string>();
			foreach (var f in fields) {
				var prop = typeof(EspionageReport).GetProperty(f);
				long n = Convert.ToInt64(prop?.GetValue(report) ?? 0L);
				if (n > 0)
					parts.Add($"{n:N0}x {f}");
			}
			return string.Join(", ", parts);
		}

		private void ProcessSweepReport(Cfg cfg, FastFarmTarget t, EspionageReport report, CharacterClass playerClass) {
			string key = Key(t);
			if (!report.IsInactive) {
				_state.Targets.Remove(key);
				return;
			}
			if (!report.IsDefenceless(false)) {
				t.BlockedUntilUtc = DateTime.UtcNow.AddHours(6);
				t.BlockReason = "Defended";
				Resources defLoot = report.Loot(playerClass);
				t.Metal = defLoot.Metal;
				t.Crystal = defLoot.Crystal;
				t.Deuterium = defLoot.Deuterium;
				t.Weighted = Weigh(cfg, defLoot);
				t.DefShips = UnitList(report, "LightFighter", "HeavyFighter", "Cruiser", "Battleship", "Battlecruiser", "Bomber", "Destroyer", "Deathstar", "SmallCargo", "LargeCargo", "ColonyShip", "Recycler", "Crawler", "Reaper", "Pathfinder");
				t.DefDefence = UnitList(report, "RocketLauncher", "LightLaser", "HeavyLaser", "GaussCannon", "IonCannon", "PlasmaTurret", "SmallShieldDome", "LargeShieldDome", "AntiBallisticMissiles", "InterplanetaryMissiles");
				t.DefReportServer = report.Date;
				t.ReportServer = null;
				t.SweepValue = 0;
				t.Verify = false;
				return;
			}
			Resources loot = report.Loot(playerClass);
			long weighted = Weigh(cfg, loot);
			long total = loot.Metal + loot.Crystal + loot.Deuterium;
			bool belowMin = total < cfg.MinLoot || loot.Deuterium < cfg.MinDeuterium || weighted <= 0;

			if (!t.Verify) {
				if (belowMin) {
					_state.Targets.Remove(key);
					return;
				}
				t.Metal = loot.Metal;
				t.Crystal = loot.Crystal;
				t.Deuterium = loot.Deuterium;
				t.Weighted = weighted;
				t.LastWeighted = weighted;
				t.SweepValue = weighted;
				t.ReportServer = null;
				return;
			}

			double limit = t.SweepValue * (1.0 - cfg.ShrinkDiscardPercent / 100.0);
			if (belowMin || weighted < limit) {
				DoLog(LogLevel.Information, $"{key}: discarded, loot {t.SweepValue} -> {weighted} (weighted).");
				_state.Targets.Remove(key);
				return;
			}
			t.Metal = loot.Metal;
			t.Crystal = loot.Crystal;
			t.Deuterium = loot.Deuterium;
			t.Weighted = weighted;
			t.LastWeighted = weighted;
			t.ReportServer = report.Date;
			t.BlockedUntilUtc = null;
			t.BlockReason = null;
		}

		private async Task<(int sent, bool bedTime)> SweepSpyPhase(Cfg cfg, List<OriginState> origins, int free) {
			if (DateTime.UtcNow < _berichtPauseBis) {
				DoLog(LogLevel.Information, $"Probe pause after a round without any report, {(_berichtPauseBis - DateTime.UtcNow).TotalSeconds:0} s left.");
				return (0, false);
			}
			var fleets = _tbotInstance.UserData.fleets ?? new List<Fleet>();
			Func<FastFarmTarget, bool> free2 = t => !t.Pending && !IsBlocked(t)
				&& !fleets.Any(f => (f.Mission == Missions.Attack || f.Mission == Missions.Spy) && f.Destination != null && f.Destination.IsSame(Coord(t)));
			List<FastFarmTarget> pool;
			bool verify = _state.SweepPhase == "verify";
			if (verify) {
				pool = Ordered(_state.Targets.Values.Where(t => free2(t) && t.SweepValue > 0 && !t.ReportServer.HasValue)).ToList();
			} else if (_state.SweepPhase == "sweep") {
				pool = _state.Targets.Values.Where(t => free2(t) && t.SweepValue == 0)
					.OrderBy(t => origins.Min(o => Distance(o.Celestial.Coordinate, Coord(t)))).ToList();
			} else {
				return (0, false);
			}
			if (pool.Count == 0) {
				int unswept = _state.Targets.Values.Count(t => t.SweepValue == 0);
				int blockedNow = _state.Targets.Values.Count(t => IsBlocked(t));
				DoLog(LogLevel.Information, $"No target to spy right now (phase {_state.SweepPhase}): {_state.Targets.Count} stored, {unswept} without sweep value, {blockedNow} blocked, {_state.Targets.Values.Count(t => t.Pending)} pending.");
				return (0, false);
			}
			DoLog(LogLevel.Debug, $"Spy pool: {pool.Count} targets, {free} free slots.");
			return await SendSpies(cfg, origins, pool, free, verify);
		}


		private List<(int galaxy, int system)> BuildScanList(List<OriginState> origins) {
			var list = new List<(int galaxy, int system)>();
			bool configured = false;
			try {
				var s = _tbotInstance.InstanceSettings.FastFarm;
				configured = SettingsService.IsSettingSet(s, "ScanRange") && s.ScanRange.Length > 0;
				if (configured) {
					foreach (var r in s.ScanRange) {
						int g = Convert.ToInt32(r.Galaxy);
						int from = Convert.ToInt32(r.StartSystem);
						int to = Convert.ToInt32(r.EndSystem);
						for (int sys = Math.Min(from, to); sys <= Math.Max(from, to); sys++)
							list.Add((g, sys));
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Invalid FastFarm.ScanRange: {e.Message}");
				list.Clear();
			}
			if (list.Count == 0) {
				int maxSystems = Math.Max(1, _tbotInstance.UserData.serverData.Systems);
				foreach (var o in origins) {
					for (int d = -100; d <= 100; d++) {
						int sys = o.Celestial.Coordinate.System + d;
						if (sys < 1)
							sys += maxSystems;
						if (sys > maxSystems)
							sys -= maxSystems;
						list.Add((o.Celestial.Coordinate.Galaxy, sys));
					}
				}
			}
			var distinct = list.Distinct().ToList();
			return distinct
				.OrderBy(x => origins.Min(o => Distance(o.Celestial.Coordinate, new Coordinate(x.galaxy, x.system, 1, Celestials.Planet))))
				.ThenBy(x => x.galaxy).ThenBy(x => x.system)
				.ToList();
		}

		private void PruneOutOfRange(List<OriginState> origins) {
			try {
				var list = BuildScanList(origins);
				if (list.Count == 0)
					return;
				var inRange = new HashSet<(int, int)>(list);
				string signature = list.Count + ":" + string.Join(",", list.Select(x => x.galaxy + "." + x.system).Take(6)) + ":" + list[list.Count - 1].galaxy + "." + list[list.Count - 1].system;
				var remove = _state.Targets.Where(kv => !kv.Value.Pending && !inRange.Contains((kv.Value.Galaxy, kv.Value.System))).Select(kv => kv.Key).ToList();
				foreach (var key in remove)
					_state.Targets.Remove(key);
				if (remove.Count > 0)
					DoLog(LogLevel.Information, $"Scan range changed: removed {remove.Count} saved target(s) outside the current range.");
				if (_state.ScanSignature != signature) {
					if (_state.ScanSignature != null)
						DoLog(LogLevel.Information, "Scan range changed: scan starts again from the beginning.");
					_state.ScanSignature = signature;
					_state.ScanCursor = 0;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Debug, $"Could not check the scan range: {e.Message}");
			}
		}

		private async Task<bool> ScanStep(Cfg cfg, List<OriginState> origins, int maxSystems, DateTime until) {
			var list = CurrentScanList(cfg, origins);
			if (list.Count == 0)
				return false;
			int done = 0;
			bool wrapped = false;
			while (done < maxSystems && DateTime.UtcNow < until) {
				Heartbeat();
				if (_state.ScanCursor >= list.Count) {
					_state.ScanCursor = 0;
					wrapped = true;
					break;
				}
				var (galaxy, system) = list[_state.ScanCursor];
				_state.ScanCursor++;
				done++;
				try {
					GalaxyInfo info = await GalaxyCacheHub.GetOrLiveAsync(_tbotInstance, _ogameService, galaxy, system, GalaxyCacheConfig.Read(_tbotInstance).UseInFastFarm, TimeSpan.FromHours(GalaxyCacheConfig.Read(_tbotInstance).FastFarmMaxAgeHours));
					if (info?.Planets == null)
						continue;
					foreach (var p in info.Planets.Where(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation)) {
						string key = Key(p.Coordinate);
						if (_state.Ignored.TryGetValue(key, out var ignUntil) && ignUntil > DateTime.UtcNow)
							continue;
						if (!_state.Targets.TryGetValue(key, out var target)) {
							target = new FastFarmTarget { Galaxy = p.Coordinate.Galaxy, System = p.Coordinate.System, Position = p.Coordinate.Position };
							_state.Targets[key] = target;
						}
						target.Player = p.Player?.Name ?? target.Player;
						target.SeenUtc = DateTime.UtcNow;
					}
					var inSystem = _state.Targets.Values
						.Where(t => t.Galaxy == galaxy && t.System == system && !t.Pending)
						.ToList();
					foreach (var t in inSystem) {
						bool stillInactive = info.Planets.Any(p => p != null && p.Inactive && !p.Vacation && !p.Banned && !p.Administrator
							&& p.Coordinate.Position == t.Position);
						if (!stillInactive) {
							_state.Targets.Remove(Key(t));
						}
					}
				} catch (OperationCanceledException) {
					throw;
				} catch (Exception e) {
					DoLog(LogLevel.Debug, $"Galaxy scan {galaxy}:{system} failed: {e.Message}");
				}
			}
			return !wrapped;
		}
	}
}
#endif
