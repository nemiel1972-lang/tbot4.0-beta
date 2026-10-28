using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	internal class KeepBoostersWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private readonly ICalculationService _calculationService;

		private readonly Dictionary<(long, BoosterType), DateTime> _cooldown = new();
		private static readonly TimeSpan CooldownAfterTry = TimeSpan.FromMinutes(45);

		public KeepBoostersWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			ITBotOgamedBridge tbotOgameBridge,
			ICalculationService calculationService) :
			base(parentInstance) {
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
			_calculationService = calculationService;
		}

		protected override TimeSpan ExecuteTimeout => TimeSpan.FromMinutes(10);

		private bool _logItems = true;
		private static readonly TimeSpan ItemLogInterval = TimeSpan.FromHours(12);
		private DateTime _nextItemLog = DateTime.MinValue;
		private Dictionary<string, int> _reserve = new();
		private Dictionary<BoosterType, int> _minLevel = new();

		private IDictionary<string, object> Settings() {
			if (_tbotInstance.InstanceSettings.Brain is IDictionary<string, object> brain
				&& brain.TryGetValue("KeepBoosters", out var s) && s is IDictionary<string, object> d)
				return d;
			return null;
		}

		private static bool TryGetCI(IDictionary<string, object> d, string key, out object value) {
			value = null;
			if (d == null)
				return false;
			if (d.TryGetValue(key, out value))
				return true;
			foreach (var kv in d)
				if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase)) {
					value = kv.Value;
					return true;
				}
			return false;
		}

		private static bool GetBool(IDictionary<string, object> d, string key, bool def) {
			try {
				return TryGetCI(d, key, out var v) && v != null ? Convert.ToBoolean(v) : def;
			} catch {
				return def;
			}
		}

		private static int GetInt(IDictionary<string, object> d, string key, int def) {
			try {
				return TryGetCI(d, key, out var v) && v != null ? Convert.ToInt32(v) : def;
			} catch {
				return def;
			}
		}

		private List<string> GetTiers(IDictionary<string, object> d) {
			var chosen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			bool found = false;
			try {
				if (d != null && d.TryGetValue("Tiers", out var v) && v != null) {
					if (v is IDictionary<string, object> map) {
						found = true;
						foreach (var kv in map)
							if (GetBool(map, kv.Key, false))
								chosen.Add(kv.Key);
					} else if (v is System.Collections.IEnumerable list && !(v is string)) {
						found = true;
						foreach (var o in list)
							if (o != null)
								chosen.Add(o.ToString());
					}
				}
			} catch {
			}
			if (!found)
				return new List<string> { "bronze", "silver", "gold" };
			return BoosterMatcher.TierOrder.Where(chosen.Contains).ToList();
		}

		private Dictionary<string, int> GetReserves(IDictionary<string, object> d) {
			var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			object v = null;
			d?.TryGetValue("Reserve", out v);
			foreach (var tier in BoosterMatcher.TierOrder) {
				int value = 10;
				try {
					if (v is IDictionary<string, object> map)
						value = GetInt(map, tier, 10);
					else if (v is Newtonsoft.Json.Linq.JObject jo)
						value = jo.TryGetValue(tier, StringComparison.OrdinalIgnoreCase, out var jt) && jt.Type != Newtonsoft.Json.Linq.JTokenType.Null ? (int) jt : 10;
					else if (v is System.Collections.IDictionary legacy)
						value = legacy.Contains(tier) && legacy[tier] != null ? Convert.ToInt32(legacy[tier]) : 10;
					else if (v != null)
						value = Convert.ToInt32(v);
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Reserve for {tier} not readable ({v?.GetType().Name}: {e.Message}) - using 10.");
				}
				res[tier] = Math.Max(0, value);
			}
			if (_logItems)
				DoLog(LogLevel.Information, $"Reserve read ({v?.GetType().Name ?? "missing"}): " + string.Join(", ", res.Select(kv => $"{kv.Key}={kv.Value}")));
			return res;
		}

		private Dictionary<BoosterType, int> GetMinLevels(IDictionary<string, object> d) {
			var res = new Dictionary<BoosterType, int>();
			IDictionary<string, object> m = null;
			if (d != null && d.TryGetValue("MinMineLevel", out var v))
				m = v as IDictionary<string, object>;
			foreach (BoosterType t in new[] { BoosterType.Metal, BoosterType.Crystal, BoosterType.Deuterium })
				res[t] = Math.Max(0, GetInt(m, t.ToString(), 0));
			return res;
		}

		private void WriteStock(List<InventoryItem> inventory) {
			try {
				var events = inventory.Where(i => i.Amount > 0 && IsEventBooster(i.Name)).Select(i => new { name = i.Name, amount = i.Amount, expiresInSec = i.ExpiresInSec() }).ToList();
				var file = new { updated = DateTime.UtcNow.ToString("o"), stock = BoosterMatcher.StockByTier(inventory), events };
				string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
				System.IO.Directory.CreateDirectory(dir);
				string alias = System.IO.Path.GetFileName(_tbotInstance.InstanceAlias);
				Tbot.Common.IO.AtomicFile.WriteAllText(System.IO.Path.Combine(dir, $"booster_stock_{alias}.json"), Newtonsoft.Json.JsonConvert.SerializeObject(file, Newtonsoft.Json.Formatting.Indented));
			} catch {
			}
		}

		private List<BoosterType> GetTypes(IDictionary<string, object> d) {
			var res = new List<BoosterType>();
			IDictionary<string, object> t = null;
			if (d != null && d.TryGetValue("Types", out var v))
				t = v as IDictionary<string, object>;
			foreach (BoosterType type in Enum.GetValues(typeof(BoosterType))) {
				bool def = type != BoosterType.Energy;
				if (GetBool(t, type.ToString(), def))
					res.Add(type);
			}
			return res;
		}

		protected override async Task Execute() {
			var cfg = Settings();
			bool dryRun = GetBool(cfg, "DryRun", true);
			var tiers = GetTiers(cfg);
			var types = GetTypes(cfg);
			_logItems = GetBool(cfg, "LogItems", true);
			if (_logItems && DateTime.UtcNow >= _nextItemLog)
				_nextItemLog = DateTime.UtcNow + ItemLogInterval;
			else
				_logItems = false;
			_reserve = GetReserves(cfg);
			_minLevel = GetMinLevels(cfg);
			try {
				if (types.Count == 0) {
					DoLog(LogLevel.Information, "No booster types selected.");
				} else {
					await CheckAll(cfg, dryRun, tiers, types);
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Keep boosters failed: {e.Message}");
			}

			var time = await _tbotOgameBridge.GetDateTime();
			int min = GetInt(cfg, "CheckIntervalMin", 30), max = GetInt(cfg, "CheckIntervalMax", 60);
			var interval = RandomizeHelper.CalcRandomInterval(min, max);
			if (interval <= 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
			ChangeWorkerPeriod(interval);
			DoLog(LogLevel.Information, $"Next KeepBoosters check at {time.AddMilliseconds(interval)}");
		}

		private async Task CheckAll(IDictionary<string, object> cfg, bool dryRun, List<string> tiers, List<BoosterType> types) {
			List<Celestial> excluded = new();
			try {
				if (cfg != null && cfg.ContainsKey("Exclude"))
					excluded = _calculationService.ParseCelestialsList(cfg["Exclude"], _tbotInstance.UserData.celestials);
			} catch {
			}

			var planets = _tbotInstance.UserData.celestials.Where(c => c is Planet && !excluded.Any(e => e.ID == c.ID)).ToList();

			List<InventoryItem> inventory = null;
			if (planets.Count > 0) {
				try {
					inventory = await _ogameService.GetItems(planets[0]) ?? new List<InventoryItem>();
					WriteStock(inventory);
					if (_logItems) {
						var boosters = inventory.Where(i => i.Amount > 0).ToList();
						DoLog(LogLevel.Information, "Inventory (in stock): " + (boosters.Count == 0 ? "nothing" : string.Join("; ", boosters.Select(i => $"\"{i.Name}\" x{i.Amount}"))));
					}
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Could not read the inventory: {e.Message}");
					return;
				}
			}
			var status = new List<object>();
			var activeByPlanet = new Dictionary<long, List<ActiveItem>>();
			foreach (var planet in planets) {
				_ct.ThrowIfCancellationRequested();
				Heartbeat();
				List<ActiveItem> active;
				try {
					active = await _ogameService.GetActiveItems(planet) ?? new List<ActiveItem>();
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Could not read active items of {planet}: {e.Message}");
					continue;
				}

				activeByPlanet[planet.ID] = active;

				if (_logItems) {
					string text = active.Count == 0 ? "none" : string.Join("; ", active.Select(i => $"\"{i.Name}\" [{i.Ref}] {i.TimeRemaining / 3600}h{i.TimeRemaining % 3600 / 60:00}m left"));
					DoLog(LogLevel.Information, $"Active items on {planet}: {text}");
				}

				Celestial leveled = planet;
				if (planet is Planet pl && pl.Buildings == null) {
					try {
						leveled = await _tbotOgameBridge.UpdatePlanet(planet, UpdateTypes.Buildings) ?? planet;
					} catch (Exception e) {
						DoLog(LogLevel.Warning, $"Could not read buildings of {planet}: {e.Message}");
					}
				}

				var cells = new Dictionary<string, object>();
				foreach (var type in types) {
					int level = LevelOf(leveled, type);
					int minLevel = _minLevel.TryGetValue(type, out var ml) ? ml : 0;
					var running = BoosterMatcher.ActiveItemOf(active, type);
					if (running != null) {
						cells[type.ToString()] = new { state = "active", level, minLevel, item = running.Name, remainingSec = running.TimeRemaining };
						continue;
					}
					string what = $"No {type} booster running on {planet}";
					if (minLevel > 0 && level < minLevel) {
						cells[type.ToString()] = new { state = level < 0 ? "no-data" : "below-min", level, minLevel };
						if (_logItems)
							DoLog(LogLevel.Information, $"{type} booster skipped on {planet}: {(level < 0 ? "mine level unknown" : $"mine level {level} < {minLevel}")}.");
						continue;
					}
					if (_cooldown.TryGetValue((planet.ID, type), out var until) && until > DateTime.UtcNow) {
						cells[type.ToString()] = new { state = "missing", level, minLevel };
						continue;
					}

					var pick = BoosterMatcher.Pick(inventory, type, tiers, t => _reserve.TryGetValue(t, out var r) ? r : 10);
					if (pick == null) {
						DoLog(LogLevel.Information, $"No {type} booster running on {planet}, nothing to activate (tiers {string.Join(", ", tiers)}, reserve {string.Join("/", tiers.Select(t => _reserve.TryGetValue(t, out var r) ? r : 10))} kept).");
						_cooldown[(planet.ID, type)] = DateTime.UtcNow.AddMinutes(30);
						cells[type.ToString()] = new { state = "no-stock", level, minLevel };
						continue;
					}
					if (dryRun) {
						DoLog(LogLevel.Information, $"[DryRun] {what} - would activate \"{pick.Name}\" [{pick.Ref}] (x{pick.Amount}).");
						_cooldown[(planet.ID, type)] = DateTime.UtcNow.AddMinutes(30);
						cells[type.ToString()] = new { state = "would-activate", level, minLevel, item = pick.Name };
						continue;
					}

					DoLog(LogLevel.Information, $"{what} - activating \"{pick.Name}\" (x{pick.Amount} in stock).");
					_cooldown[(planet.ID, type)] = DateTime.UtcNow.Add(CooldownAfterTry);
					cells[type.ToString()] = new { state = "missing", level, minLevel };
					try {
						await _ogameService.ActivateItem(planet, pick.Ref);
						await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), _ct);
						inventory = await _ogameService.GetItems(planet) ?? inventory;
						WriteStock(inventory);
						active = await _ogameService.GetActiveItems(planet) ?? new List<ActiveItem>();
						var now = BoosterMatcher.ActiveItemOf(active, type);
						if (now != null) {
							DoLog(LogLevel.Information, $"{type} booster is now active on {planet}.");
							cells[type.ToString()] = new { state = "active", level, minLevel, item = now.Name, remainingSec = now.TimeRemaining };
							_ = _tbotInstance.SendTelegramMessage($"{planet}: {type} booster activated (\"{pick.Name}\").");
						} else {
							DoLog(LogLevel.Warning, $"Activated \"{pick.Name}\" on {planet}, but no running {type} booster shows up. Check the item names (see 'Active items' in the log).");
						}
					} catch (Exception e) {
						DoLog(LogLevel.Warning, $"Could not activate \"{pick.Name}\" on {planet}: {e.Message}");
					}
					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), _ct);
				}
				status.Add(new { id = planet.ID, name = planet.Name, coord = planet.Coordinate.ToString(), cells });
			}
			WriteStatus(status);

			try {
				await HandleEventBoosters(cfg, dryRun, planets, inventory, activeByPlanet);
			} catch (OperationCanceledException) {
				throw;
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Event boosters failed: {e.Message}");
			}
		}

		private readonly Dictionary<string, DateTime> _eventCooldown = new();
		private DateTime _nextEventDiag = DateTime.MinValue;

		private static bool IsEventBooster(string name) {
			var n = BoosterMatcher.Normalize(name);
			if (n.Length == 0 || n.Contains("expedition"))
				return false;
			return n.Contains("ressourcen-booster") || n.Contains("ressourcenbooster") || n.Contains("resource booster") || n.Contains("resource-booster");
		}

		private static double PlanetScore(Celestial c) {
			if (c.ResourcesProduction != null) {
				return (c.ResourcesProduction.Metal?.CurrentProduction ?? 0) / 3.0 + (c.ResourcesProduction.Crystal?.CurrentProduction ?? 0) / 2.0 + (c.ResourcesProduction.Deuterium?.CurrentProduction ?? 0);
			}
			if (c is Planet p && p.Buildings != null)
				return (p.Buildings.MetalMine + p.Buildings.CrystalMine + p.Buildings.DeuteriumSynthesizer) / 1000.0;
			return 0;
		}

		private async Task HandleEventBoosters(IDictionary<string, object> cfg, bool dryRun, List<Celestial> planets, List<InventoryItem> inventory, Dictionary<long, List<ActiveItem>> activeByPlanet) {
			IDictionary<string, object> ev = null;
			if (cfg != null && cfg.TryGetValue("EventBoosters", out var evv))
				ev = evv as IDictionary<string, object>;
			if (!GetBool(ev, "Active", true) || inventory == null || planets.Count == 0)
				return;
			long windowSec = Math.Max(1, GetInt(ev, "ActivateWithinHours", 24)) * 3600L;

			var items = inventory.Where(i => i.Amount > 0 && !string.IsNullOrEmpty(i.Ref) && IsEventBooster(i.Name)).ToList();
			if (items.Count == 0)
				return;

			if (DateTime.UtcNow >= _nextEventDiag) {
				_nextEventDiag = DateTime.UtcNow.AddHours(12);
				foreach (var it in items) {
					long? left = it.ExpiresInSec();
					string extra = it.Extra == null || it.Extra.Count == 0 ? "none" : string.Join(", ", it.Extra.Select(kv => $"{kv.Key}={kv.Value}"));
					DoLog(LogLevel.Information, $"Event booster in inventory: \"{it.Name}\" x{it.Amount}, expires in {(left.HasValue ? TimeSpan.FromSeconds(left.Value).ToString(@"d\.hh\:mm") : "unknown")}; fields: {extra}");
				}
			}

			bool EventRunning(Celestial c) => activeByPlanet.TryGetValue(c.ID, out var a) && a != null && a.Any(x => x.TimeRemaining > 0 && IsEventBooster(x.Name));
			var used = new HashSet<long>();

			foreach (var it in items.OrderBy(i => i.ExpiresInSec() ?? long.MaxValue)) {
				_ct.ThrowIfCancellationRequested();
				long? left = it.ExpiresInSec();
				if (!left.HasValue || left.Value > windowSec)
					continue;
				if (_eventCooldown.TryGetValue(it.Ref, out var until) && until > DateTime.UtcNow)
					continue;

				var target = planets.Where(c => !used.Contains(c.ID) && !EventRunning(c)).OrderByDescending(PlanetScore).FirstOrDefault();
				if (target == null) {
					DoLog(LogLevel.Information, $"Event booster \"{it.Name}\" ends in {left.Value / 3600}h {left.Value % 3600 / 60:00}m, but every planet already has one running.");
					_eventCooldown[it.Ref] = DateTime.UtcNow.AddMinutes(30);
					continue;
				}
				_eventCooldown[it.Ref] = DateTime.UtcNow.AddMinutes(45);
				if (dryRun) {
					DoLog(LogLevel.Information, $"[DryRun] Event booster \"{it.Name}\" ends in {left.Value / 3600}h {left.Value % 3600 / 60:00}m - would activate it on {target}.");
					used.Add(target.ID);
					continue;
				}
				DoLog(LogLevel.Information, $"Event booster \"{it.Name}\" ends in {left.Value / 3600}h {left.Value % 3600 / 60:00}m - activating it on {target}.");
				try {
					await _ogameService.ActivateItem(target, it.Ref);
					used.Add(target.ID);
					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), _ct);
					var act = await _ogameService.GetActiveItems(target) ?? new List<ActiveItem>();
					activeByPlanet[target.ID] = act;
					if (act.Any(x => x.TimeRemaining > 0 && IsEventBooster(x.Name))) {
						DoLog(LogLevel.Information, $"Event booster is now active on {target}.");
						_ = _tbotInstance.SendTelegramMessage($"{target}: event booster activated (\"{it.Name}\").");
					} else
						DoLog(LogLevel.Warning, $"Activated \"{it.Name}\" on {target}, but no running event booster shows up.");
					inventory = await _ogameService.GetItems(target) ?? inventory;
					WriteStock(inventory);
				} catch (Exception e) {
					DoLog(LogLevel.Warning, $"Could not activate event booster \"{it.Name}\" on {target}: {e.Message}");
				}
				await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), _ct);
			}
		}

		private static int LevelOf(Celestial c, BoosterType type) {
			if (type == BoosterType.Energy)
				return int.MaxValue;
			if (c is not Planet p || p.Buildings == null)
				return -1;
			return type == BoosterType.Metal ? p.Buildings.MetalMine : type == BoosterType.Crystal ? p.Buildings.CrystalMine : p.Buildings.DeuteriumSynthesizer;
		}

		private void WriteStatus(List<object> status) {
			try {
				var file = new { updated = DateTime.UtcNow.ToString("o"), planets = status };
				string dir = System.IO.Path.Combine(AppContext.BaseDirectory, "data");
				System.IO.Directory.CreateDirectory(dir);
				string alias = System.IO.Path.GetFileName(_tbotInstance.InstanceAlias);
				Tbot.Common.IO.AtomicFile.WriteAllText(System.IO.Path.Combine(dir, $"booster_status_{alias}.json"), Newtonsoft.Json.JsonConvert.SerializeObject(file, Newtonsoft.Json.Formatting.Indented));
			} catch {
			}
		}

		private void DoLog(LogLevel level, string text) => _tbotInstance.log(level, GetLogSender(), text);

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.KeepBoosters.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "KeepBoosters";
		}
		public override Feature GetFeature() {
			return Feature.BrainKeepBoosters;
		}
		public override LogSender GetLogSender() {
			return LogSender.KeepBoosters;
		}
	}
}
