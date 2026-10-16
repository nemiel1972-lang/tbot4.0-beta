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
			public int RespyAfterAttackMinutes;
			public int DefendedRecheckHours;
			public bool DeleteReports;
			public int FleetSpeedPercent;
			public double CargoSurplusPercent;
			public double MinFillPercent;
			public int MaxSystemsPerScanStep;
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
				WeightMetal = Num("WeightMetal", 1.0),
				WeightCrystal = Num("WeightCrystal", 1.5),
				WeightDeuterium = Num("WeightDeuterium", 3.0),
				MinLoot = (long) Num("MinLoot", 100000),
				MinDeuterium = (long) Num("MinDeuterium", 0),
				TopTargets = Math.Max(1, (int) Num("TopTargets", 36)),
				ReportMaxAgeMinutes = Math.Max(1, (int) Num("ReportMaxAgeMinutes", 30)),
				RespyAfterAttackMinutes = Math.Max(0, (int) Num("RespyAfterAttackMinutes", 60)),
				DefendedRecheckHours = Math.Max(1, (int) Num("DefendedRecheckHours", 6)),
				DeleteReports = Flag("DeleteReports", true),
				FleetSpeedPercent = Math.Clamp((int) Num("FleetSpeedPercent", 100), 10, 100),
				CargoSurplusPercent = Math.Max(0, Num("CargoSurplusPercent", 0)),
				MinFillPercent = Math.Clamp(Num("MinFillPercent", 50), 1, 100),
				MaxSystemsPerScanStep = Math.Max(1, (int) Num("MaxSystemsPerScanStep", 25))
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
		}

		private void SaveState() {
			try {
				lock (_stateLock) {
					_state.UpdatedUtc = DateTime.UtcNow;
					string path = StatePath();
					string tmp = path + ".tmp";
					File.WriteAllText(tmp, JsonConvert.SerializeObject(_state, Formatting.Indented));
					File.Move(tmp, path, true);
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

		private async Task WaitSeconds(double seconds) {
			DateTime end = DateTime.UtcNow.AddSeconds(seconds);
			while (true) {
				double left = (end - DateTime.UtcNow).TotalMilliseconds;
				if (left <= 0)
					break;
				Heartbeat();
				await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(left, 15000)), _ct);
			}
		}


		private sealed class OriginState {
			public Celestial Celestial;
			public long ProbesLeft;
			public Dictionary<Buildables, long> CargoLeft = new Dictionary<Buildables, long>();
		}

		protected override async Task Execute() {
			bool stop = false;
			_nextDelayMs = 30000;
			try {
				LoadState();
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
			var origins = await PrepareOrigins(cfg);
			if (origins.Count == 0) {
				SetStatus("No usable origin celestial found (check FastFarm.Origin).");
				_nextDelayMs = 60000;
				return false;
			}

			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

			await CollectReports(cfg);

			int free = _tbotInstance.UserData.slots.Free - cfg.SlotsToLeaveFree;
			bool bedTime = false;

			if (free > 0) {
				var attack = await AttackPhase(cfg, origins, free);
				free = attack.freeLeft;
				bedTime = attack.bedTime;
			}
			int probesSentNow = 0;
			if (!bedTime && free > 0) {
				var spy = await SpyPhase(cfg, origins, free);
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
			if (slotsFull && backs.Count > 0)
				nextSec = Math.Min(nextSec, backs.Min());
			if (nextSec == double.MaxValue)
				nextSec = 60;

			SetStatus(BuildStatus(free));

			double scanBudget = Math.Min(nextSec, 90);
			if (scanBudget >= 3 && NeedMoreTargets(cfg)) {
				DateTime until = DateTime.UtcNow.AddSeconds(scanBudget);
				int scanned = 0;
				while (DateTime.UtcNow < until && scanned < cfg.MaxSystemsPerScanStep * 4) {
					bool more = await ScanStep(cfg, origins, cfg.MaxSystemsPerScanStep, until);
					scanned++;
					if (!more)
						break;
				}
				nextSec = Math.Max(2, nextSec - (scanBudget - Math.Max(0, (until - DateTime.UtcNow).TotalSeconds)));
			}

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
			return $"Targets known {_state.Targets.Count}, reports ready {ready}, probes pending {pending}, attacks out {_state.AttacksOut}, free slots {freeLeft}.";
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


		private async Task CollectReports(Cfg cfg) {
			var due = _state.Targets.Values.Where(t => t.Pending && t.ExpectedArrivalUtc <= DateTime.UtcNow.AddSeconds(1)).ToList();
			if (due.Count == 0)
				return;
			CharacterClass playerClass = _tbotInstance.UserData.userInfo.Class;
			foreach (var t in due) {
				Heartbeat();
				EspionageReport report = null;
				try {
					report = await _ogameService.GetEspionageReport(Coord(t));
				} catch (Exception e) {
					DoLog(LogLevel.Debug, $"{Key(t)}: no report yet ({e.Message}).");
				}

				bool fresh = report != null && report.Coordinate != null && report.Date >= t.SpySentServer.AddSeconds(-10);
				if (!fresh) {
					if (DateTime.UtcNow > t.ExpectedArrivalUtc.AddSeconds(120)) {
						t.Pending = false;
						t.SpyFails++;
						DoLog(LogLevel.Information, $"{Key(t)}: no report arrived (probes lost or target gone).");
						if (t.SpyFails >= 3) {
							t.BlockedUntilUtc = DateTime.UtcNow.AddHours(cfg.DefendedRecheckHours);
							t.BlockReason = "NoReport";
							t.SpyFails = 0;
						}
					}
					continue;
				}

				t.Pending = false;
				t.SpyFails = 0;
				_state.ReportsRead++;
				t.Player = report.Username ?? t.Player;

				if (!report.IsInactive) {
					t.BlockedUntilUtc = DateTime.UtcNow.AddHours(12);
					t.BlockReason = "Active";
					t.ReportServer = null;
				} else if (!report.IsDefenceless(false)) {
					t.BlockedUntilUtc = DateTime.UtcNow.AddHours(cfg.DefendedRecheckHours);
					t.BlockReason = report.HasDefensesInformation && report.HasFleetInformation ? "Defended" : "NoInfo";
					t.ReportServer = null;
				} else {
					Resources loot = report.Loot(playerClass);
					t.Metal = loot.Metal;
					t.Crystal = loot.Crystal;
					t.Deuterium = loot.Deuterium;
					t.Weighted = Weigh(cfg, loot);
					t.LastWeighted = t.Weighted;
					t.ReportServer = report.Date;
					t.BlockedUntilUtc = null;
					t.BlockReason = null;
				}

				if (cfg.DeleteReports) {
					try {
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

			var candidates = _state.Targets.Values
				.Where(t => t.ReportServer.HasValue
					&& !IsBlocked(t)
					&& (serverNow - t.ReportServer.Value).TotalMinutes <= cfg.ReportMaxAgeMinutes
					&& t.Weighted > 0
					&& (t.Metal + t.Crystal + t.Deuterium) >= cfg.MinLoot
					&& t.Deuterium >= cfg.MinDeuterium
					&& !fleets.Any(f => f.Mission == Missions.Attack && f.Destination != null && f.Destination.IsSame(Coord(t))))
				.OrderByDescending(t => t.Weighted)
				.Take(cfg.TopTargets)
				.ToList();
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
					if (code <= (int) SendFleetCode.GenericError)
						continue;

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


		private async Task<(int sent, bool bedTime)> SpyPhase(Cfg cfg, List<OriginState> origins, int free) {
			var fleets = _tbotInstance.UserData.fleets ?? new List<Fleet>();
			DateTime nowUtc = DateTime.UtcNow;

			Func<FastFarmTarget, bool> eligible = t =>
				!t.Pending
				&& !IsBlocked(t)
				&& !t.ReportServer.HasValue
				&& (!t.LastAttackUtc.HasValue || (nowUtc - t.LastAttackUtc.Value).TotalMinutes >= cfg.RespyAfterAttackMinutes)
				&& !fleets.Any(f => (f.Mission == Missions.Attack || f.Mission == Missions.Spy) && f.Destination != null && f.Destination.IsSame(Coord(t)));

			var pool = _state.Targets.Values.Where(eligible)
				.OrderByDescending(t => t.LastWeighted)
				.ThenBy(t => origins.Min(o => Distance(o.Celestial.Coordinate, Coord(t))))
				.ToList();

			int guard = 0;
			while (pool.Count < free && guard++ < 6) {
				bool more = await ScanStep(cfg, origins, cfg.MaxSystemsPerScanStep, DateTime.UtcNow.AddSeconds(30));
				pool = _state.Targets.Values.Where(eligible)
					.OrderByDescending(t => t.LastWeighted)
					.ThenBy(t => origins.Min(o => Distance(o.Celestial.Coordinate, Coord(t))))
					.ToList();
				if (!more)
					break;
			}
			if (pool.Count == 0)
				return (0, false);

			int sent = 0;
			foreach (var t in pool) {
				if (free <= 0)
					break;
				Heartbeat();
				Coordinate dest = Coord(t);
				OriginState origin = ByDistance(origins, dest).FirstOrDefault(o => o.ProbesLeft >= cfg.ProbesPerTarget);
				if (origin == null) {
					DoLog(LogLevel.Information, $"Not enough probes ({cfg.ProbesPerTarget} needed) on any origin.");
					break;
				}

				var ships = new Ships().Add(Buildables.EspionageProbe, cfg.ProbesPerTarget);
				DateTime serverNow = await _tbotOgameBridge.GetDateTime();
				int code = await _fleetScheduler.SendFleet(origin.Celestial, ships, dest, Missions.Spy, Speeds.HundredPercent);
				if (code == (int) SendFleetCode.AfterSleepTime)
					return (sent, true);
				if (code == (int) SendFleetCode.NotEnoughSlots)
					break;
				if (code <= (int) SendFleetCode.GenericError)
					continue;

				FleetPrediction prediction = _calculationService.CalcFleetPrediction(origin.Celestial.Coordinate, dest, ships, Missions.Spy, Speeds.HundredPercent,
					_tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.Celestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

				origin.ProbesLeft -= cfg.ProbesPerTarget;
				free--;
				sent++;
				_state.ProbesSent += cfg.ProbesPerTarget;
				t.Pending = true;
				t.SpySentServer = serverNow;
				t.ExpectedArrivalUtc = DateTime.UtcNow.AddSeconds(prediction.Time);
			}
			if (sent > 0) {
				DoLog(LogLevel.Information, $"{sent} probe fleets sent ({cfg.ProbesPerTarget} probes each).");
				SaveState();
			}
			return (sent, false);
		}


		private bool NeedMoreTargets(Cfg cfg) {
			int usable = _state.Targets.Values.Count(t => !IsBlocked(t));
			return usable < cfg.TopTargets * 3;
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

		private async Task<bool> ScanStep(Cfg cfg, List<OriginState> origins, int maxSystems, DateTime until) {
			var list = BuildScanList(origins);
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
					GalaxyInfo info = await _ogameService.GetGalaxyInfo(galaxy, system);
					if (info?.Planets == null)
						continue;
					foreach (var p in info.Planets.Where(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation)) {
						string key = Key(p.Coordinate);
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
