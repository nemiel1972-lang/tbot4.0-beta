using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Combat;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {
	public interface ISimulatorClient {
		Task<SimulatorResult> SimulateBattle(SimulateBattleRequest request, bool mirrorShadow = false);
	}

	internal sealed class OgameSimulatorClient : ISimulatorClient {
		private readonly IOgameService _service;
		public OgameSimulatorClient(IOgameService service) => _service = service;
		public Task<SimulatorResult> SimulateBattle(SimulateBattleRequest request, bool mirrorShadow = false) => _service.SimulateBattle(request);
	}

	internal sealed class SwitchingSimulatorClient : ISimulatorClient {
		private readonly ITBotMain _bot;
		private readonly ISimulatorClient _ogame;
		private readonly HttpClient _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		private readonly SemaphoreSlim _shadowSlots = new SemaphoreSlim(8, 8);
		private readonly object _stateLock = new object();
		private int _consecutiveFailures;
		private int _shadowDropped;
		private DateTime _circuitOpenUntilUtc = DateTime.MinValue;
		private DateTime _lastWarningUtc = DateTime.MinValue;
		private string _lastLoggedSource = "";
		private Process _externalProcess;
		private int _processStartAttempts;
		private DateTime _nextProcessStartUtc = DateTime.MinValue;
		private readonly Queue<DateTime> _processCrashTimesUtc = new Queue<DateTime>();
		private bool _shutdownRequested;
		private string _effectiveAuthToken = "";
		private readonly SemaphoreSlim _processStartLock = new SemaphoreSlim(1, 1);

		public SwitchingSimulatorClient(ITBotMain bot, IOgameService service) {
			_bot = bot;
			_ogame = new OgameSimulatorClient(service);
			AppDomain.CurrentDomain.ProcessExit += (_, __) => StopExternalProcess();
			Console.CancelKeyPress += (_, __) => StopExternalProcess();
		}

		public async Task<SimulatorResult> SimulateBattle(SimulateBattleRequest request, bool mirrorShadow = false) {
			string source = ReadString("CalcSource", "ogame").Trim().ToLowerInvariant();
			if (source == "ogame" || source.Length == 0) {
				LogSourceOnce("ogame");
				return await _ogame.SimulateBattle(request).ConfigureAwait(false);
			}
			if (source == "shadow") {
				LogSourceOnce("shadow (ogame decides)");
				var goWatch = Stopwatch.StartNew();
				var result = await _ogame.SimulateBattle(request).ConfigureAwait(false);
				goWatch.Stop();
				if (mirrorShadow) {
					result.BattleId = request.BattleId;
					QueueShadowComparison(request, result, goWatch.ElapsedMilliseconds);
				}
				return result;
			}
			if (source != "external")
				throw new InvalidOperationException($"Unknown AutoFarm.CalcSource '{source}'. Expected 'ogame', 'shadow', or 'external'.");

			bool circuitOpen;
			lock (_stateLock) circuitOpen = DateTime.UtcNow < _circuitOpenUntilUtc;
			if (circuitOpen) {
				if (ReadBool("ExternalCalc.FallbackToOgame", true)) {
					WarnFallbackOnce("external simulator circuit breaker is open");
					return await _ogame.SimulateBattle(request).ConfigureAwait(false);
				}
				throw new HttpRequestException("External combat simulator circuit breaker is open.");
			}

			try {
				var result = await SimulateExternal(request).ConfigureAwait(false);
				lock (_stateLock) { _consecutiveFailures = 0; _circuitOpenUntilUtc = DateTime.MinValue; }
				LogSourceOnce("external (rust)");
				if (ReadBool("ExternalCalc.CompareMode", false)) {
					var goResult = await _ogame.SimulateBattle(request).ConfigureAwait(false);
					LogComparison(goResult, result, "blocking CompareMode");
				}
				return result;
			} catch (ExternalSimulatorHttpException ex) when (ex.HttpStatus >= 400 && ex.HttpStatus < 500) {
				RequestStats.DevLog?.Invoke("CombatSimulator", $"External simulator rejected request (HTTP {ex.HttpStatus}): {ex.Message}");
				throw;
			} catch (InvalidOperationException ex) {
				RequestStats.DevLog?.Invoke("CombatSimulator", $"External simulator configuration/mapping error (no fallback): {ex.Message}");
				throw;
			} catch (Exception ex) {
				bool fallback = ReadBool("ExternalCalc.FallbackToOgame", true);
				RegisterFailure(ex);
				if (fallback) {
					WarnFallbackOnce(ex.Message);
					return await _ogame.SimulateBattle(request).ConfigureAwait(false);
				}
				throw;
			}
		}

		private void QueueShadowComparison(SimulateBattleRequest request, SimulatorResult goResult, double goMs) {
			if (!_shadowSlots.Wait(0)) {
				int dropped = Interlocked.Increment(ref _shadowDropped);
				RequestStats.DevLog?.Invoke("CombatSimulator", $"Shadow comparison dropped: bounded queue is full (dropped={dropped}).");
				return;
			}
			var copy = JsonConvert.DeserializeObject<SimulateBattleRequest>(JsonConvert.SerializeObject(request));
			copy.BattleId = request.BattleId; copy.Target = request.Target; copy.Mode = request.Mode;
			copy.Defender.CharacterClass = request.Defender.CharacterClass;
			copy.Defender.GeneralClassCombatResearchBonusApplied = request.Defender.GeneralClassCombatResearchBonusApplied;
			copy.Defender.UseRustGeneralClassBonus = request.Defender.UseRustGeneralClassBonus;
			copy.Attacker.LifeformBonuses = request.Attacker.LifeformBonuses;
			_ = Task.Run(async () => {
				var rustWatch = Stopwatch.StartNew();
				string rustVersion = null;
				try {
					var rust = await SimulateExternal(copy, version => rustVersion = version).ConfigureAwait(false);
					rustWatch.Stop();
					LogComparison(goResult, rust, "shadow; Go remains authoritative", copy, ReadInt("ExternalCalc.BattleLogRetentionDays", 30), goMs, rustWatch.ElapsedMilliseconds, rustVersion);
				} catch (Exception ex) {
					rustWatch.Stop();
					LogShadowFailure(goResult, copy, ex, ReadInt("ExternalCalc.BattleLogRetentionDays", 30), goMs, rustWatch.ElapsedMilliseconds);
					RequestStats.DevLog?.Invoke("CombatSimulator", $"Shadow comparison failed (ignored): {ex.GetType().Name}: {ex.Message}");
				} finally { _shadowSlots.Release(); }
			});
		}

		private static void LogShadowFailure(SimulatorResult goResult, SimulateBattleRequest request, Exception error, int retentionDays, double goMs, double rustMs) {
			try {
				BattleLogStore.Append(new BattleLogRecord {
					BattleId = string.IsNullOrWhiteSpace(request.BattleId) ? Guid.NewGuid().ToString("N") : request.BattleId,
					TimeUtc = DateTime.UtcNow, Mode = request.Mode ?? "shadow", Target = SafeTarget(request.Target), Name = SafeName(request.TargetName), BestCalc = null,
					Reason = request.Mode == "autofarm" ? "Final check of the chosen fleet before sending" : null,
					Simulations = goResult.Simulations, Go = ToLogSide(goResult), Rust = null, Input = ToLogInput(request), GoMs = goMs, RustMs = rustMs,
					Error = $"{error.GetType().Name}: {error.Message}".Length > 500 ? $"{error.GetType().Name}: {error.Message}"[..500] : $"{error.GetType().Name}: {error.Message}"
				}, retentionDays);
			} catch (Exception ex) { RequestStats.DevLog?.Invoke("CombatSimulator", "Battle JSONL error append failed: " + ex.Message); }
		}

		private static void LogComparison(SimulatorResult goResult, SimulatorResult rust, string mode, SimulateBattleRequest request = null, int retentionDays = 30, double? goMs = null, double? rustMs = null, string rustVersion = null) {
			double winDelta = Math.Abs(goResult.AttackerWin - rust.AttackerWin);
			double lossDelta = RelativeDeviation(goResult.AttackerLosses?.Total() ?? 0, rust.AttackerLosses?.Total() ?? 0);
			double debrisDelta = RelativeDeviation(goResult.Debris?.Total() ?? 0, rust.Debris?.Total() ?? 0);
			bool notable = winDelta >= 5 || lossDelta >= 0.10 || debrisDelta >= 0.10;
			if (request != null) {
				try {
					BattleLogStore.Append(new BattleLogRecord {
						BattleId = string.IsNullOrWhiteSpace(request.BattleId) ? Guid.NewGuid().ToString("N") : request.BattleId,
						TimeUtc = DateTime.UtcNow, Mode = request.Mode ?? "shadow", Target = SafeTarget(request.Target), Name = SafeName(request.TargetName), BestCalc = null,
						Reason = request.Mode == "autofarm" ? "Final check of the chosen fleet before sending" : null,
						Simulations = goResult.Simulations, Go = ToLogSide(goResult), Rust = ToLogSide(rust), Input = request == null ? null : ToLogInput(request), GoMs = goMs, RustMs = rustMs, RustVersion = rustVersion,
					}, retentionDays);
				} catch (Exception ex) { RequestStats.DevLog?.Invoke("CombatSimulator", "Battle JSONL append failed: " + ex.Message); }
			}
			RequestStats.DevLog?.Invoke("CombatSimulator", $"Combat comparison [{mode}] {(notable ? "DIFF" : "ok")}: Go/Rust win={goResult.AttackerWin}%/{rust.AttackerWin}% (delta {winDelta:F1} pp), losses={goResult.AttackerLosses?.Total() ?? 0}/{rust.AttackerLosses?.Total() ?? 0} (delta {lossDelta:P1}), debris={goResult.Debris?.Total() ?? 0}/{rust.Debris?.Total() ?? 0} (delta {debrisDelta:P1}), runs Go/Rust={goResult.Simulations}/{rust.Simulations}.");
		}

		private static string SafeName(string name) {
			if (string.IsNullOrWhiteSpace(name)) return null;
			return TBot.Common.Logging.LogAnonymizer.Enabled ? "*****" : name.Trim();
		}

		private static string SafeTarget(string target) {
			if (string.IsNullOrWhiteSpace(target)) return null;
			if (TBot.Common.Logging.LogAnonymizer.Enabled) return "*****";
			var value = target.Trim();
			return System.Text.RegularExpressions.Regex.IsMatch(value, @"^\d{1,2}:\d{1,3}:\d{1,2}$") ? value : null;
		}
		private static BattleLogInput ToLogInput(SimulateBattleRequest request) {
			return new BattleLogInput {
				AttackerTech = new[] { request.Attacker.Weapon, request.Attacker.Shield, request.Attacker.Armour },
				DefenderTech = new[] { request.Defender.Weapon, request.Defender.Shield, request.Defender.Armour },
				AttackerEntities = ToEntityMap(request.Attacker), DefenderEntities = ToEntityMap(request.Defender),
				Resources = new[] { Math.Max(0, request.Defender.Metal), Math.Max(0, request.Defender.Crystal), Math.Max(0, request.Defender.Deuterium) },
				Simulations = request.Simulations, FleetToDebris = request.FleetToDebris,
				BonusMode = request.Defender.UseRustGeneralClassBonus ? "rust_player_class" : "tech_levels"
			};
		}

		private static Dictionary<string, long> ToEntityMap(SimulatorFleet fleet) {
			var entities = new Dictionary<string, long>();
			foreach (var property in fleet.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
				if (property.PropertyType != typeof(long) || !Enum.TryParse(property.Name, out Buildables buildable) || !Enum.IsDefined(typeof(Buildables), buildable)) continue;
				long count = (long)(property.GetValue(fleet) ?? 0L);
				if (count > 0) entities[((int)buildable).ToString(System.Globalization.CultureInfo.InvariantCulture)] = count;
			}
			return entities;
		}

		private static long[] Mcd(SimulatorPrice p) => p == null ? null : new[] { p.Metal, p.Crystal, p.Deuterium };
		private static BattleLogSide ToLogSide(SimulatorResult result) => new BattleLogSide {
			AttackerWin = result.AttackerWin, DefenderWin = result.DefenderWin, Draw = result.Draw, Rounds = result.Rounds,
			AttackerLosses = result.AttackerLosses?.Total(), DefenderLosses = result.DefenderLosses?.Total(), Debris = result.Debris?.Total(),
			AttackerLossesMcd = Mcd(result.AttackerLosses), DefenderLossesMcd = Mcd(result.DefenderLosses), DebrisMcd = Mcd(result.Debris)
		};

		private static double RelativeDeviation(long baseline, long comparison) {
			if (baseline == 0) return comparison == 0 ? 0 : 1;
			return Math.Abs((double)comparison - baseline) / Math.Abs((double)baseline);
		}

		private async Task<SimulatorResult> SimulateExternal(SimulateBattleRequest request, Action<string> versionCallback = null) {
			string baseUrl = ReadString("ExternalCalc.Url", "http://127.0.0.1:3000");
			if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp ||
				!(uri.Host.Trim('[', ']').Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Trim('[', ']').Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.Trim('[', ']').Equals("::1", StringComparison.OrdinalIgnoreCase)))
				throw new InvalidOperationException("ExternalCalc.Url must use HTTP and a loopback host (127.0.0.1, localhost, or ::1).");

			uri = new UriBuilder(uri) {
				Host = "127.0.0.1",
				Port = ReadBool("ExternalCalc.AutoStart", false) ? Math.Max(1, Math.Min(65535, ReadInt("ExternalCalc.Port", 3000))) : uri.Port
			}.Uri;
			int timeoutSeconds = Math.Max(1, Math.Min(600, ReadInt("ExternalCalc.TimeoutSeconds", 60)));
			using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
			using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(uri, "/api/simulate?detail=summary"));
			await EnsureProcessReadyAsync(uri).ConfigureAwait(false);
			string token = string.IsNullOrWhiteSpace(_effectiveAuthToken) ? ReadString("ExternalCalc.AuthToken", "") : _effectiveAuthToken;
			if (!string.IsNullOrWhiteSpace(token)) message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			message.Content = new StringContent(JsonConvert.SerializeObject(ToRustRequest(request)), System.Text.Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
			string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (!response.IsSuccessStatusCode) throw new ExternalSimulatorHttpException((int)response.StatusCode, body);
			var summary = JsonConvert.DeserializeObject<RustSummary>(body);
			if (summary == null || summary.Schema != 1 || summary.Simulations <= 0)
				throw new InvalidOperationException("External simulator returned an unsupported or incomplete summary response.");
			versionCallback?.Invoke(summary.Version);
			return FromRustSummary(request, summary);
		}

		private async Task EnsureProcessReadyAsync(Uri serviceUri) {
			if (!ReadBool("ExternalCalc.AutoStart", false)) return;
			await _processStartLock.WaitAsync().ConfigureAwait(false);
			try {
				if (_externalProcess != null && !_externalProcess.HasExited) return;
				if (DateTime.UtcNow < _nextProcessStartUtc)
					throw new HttpRequestException("External simulator auto-start is in backoff.");
				if (_processStartAttempts >= 5) {
					RequestStats.DevLog?.Invoke("CombatSimulator", "External simulator auto-start gave up after five attempts.");
					throw new HttpRequestException("External simulator auto-start exhausted its restart attempts.");
				}
				_processStartAttempts++;
				string configuredPath = ReadString("ExternalCalc.ExecutablePath", "");
				string executable = string.IsNullOrWhiteSpace(configuredPath)
					? Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "tbot-sim.exe" : "tbot-sim")
					: (Path.IsPathRooted(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath));
				if (!File.Exists(executable)) throw new FileNotFoundException("External simulator executable was not found.", executable);
				int port = Math.Max(1, Math.Min(65535, ReadInt("ExternalCalc.Port", 3000)));
				string configuredToken = ReadString("ExternalCalc.AuthToken", "");
				_effectiveAuthToken = string.IsNullOrWhiteSpace(configuredToken) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant() : configuredToken;
				var start = new ProcessStartInfo { FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
				start.Environment["PORT"] = port.ToString(System.Globalization.CultureInfo.InvariantCulture);
				start.Environment["BIND_ADDR"] = "127.0.0.1";
				int configuredRuns = Math.Max(ReadInt("SimulationRuns", 500), ReadInt("EscortSearchSimulationRuns", 100));
				int maxSimulations = Math.Clamp(Math.Max(5000, configuredRuns), 5000, 50000);
				start.Environment["MAX_SIMULATIONS"] = maxSimulations.ToString(System.Globalization.CultureInfo.InvariantCulture);
				if (configuredRuns > 50000) RequestStats.DevLog?.Invoke("CombatSimulator", $"Configured simulation runs ({configuredRuns}) exceed the supported external limit of 50000; requests above the limit will be rejected.");
				start.Environment["AUTH_TOKEN"] = _effectiveAuthToken;
				var process = new Process { StartInfo = start, EnableRaisingEvents = true };
				process.Exited += (_, __) => OnExternalProcessExited(process);
				process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) RequestStats.DevLog?.Invoke("CombatSimulator", "tbot-sim: " + e.Data); };
				process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) RequestStats.DevLog?.Invoke("CombatSimulator", "tbot-sim stderr: " + e.Data); };
				if (!process.Start()) throw new HttpRequestException("Failed to start external simulator process.");
				_externalProcess = process;
				process.BeginOutputReadLine(); process.BeginErrorReadLine();
				try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception ex) { RequestStats.DevLog?.Invoke("CombatSimulator", "Could not lower tbot-sim process priority: " + ex.Message); }
				RequestStats.DevLog?.Invoke("CombatSimulator", $"Started tbot-sim process (attempt {_processStartAttempts}/5) on loopback port {port}.");
				var healthUri = new UriBuilder(serviceUri) { Port = port, Path = "/version", Query = "" }.Uri;
				DateTime deadline = DateTime.UtcNow.AddSeconds(10);
				while (DateTime.UtcNow < deadline && !process.HasExited) {
					try {
						using var healthCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
						using var response = await _http.GetAsync(healthUri, healthCts.Token).ConfigureAwait(false);
						if (response.IsSuccessStatusCode) {
							_processStartAttempts = 0; _nextProcessStartUtc = DateTime.MinValue;
							RequestStats.DevLog?.Invoke("CombatSimulator", "tbot-sim readiness check passed.");
							return;
						}
					} catch {  }
					await Task.Delay(250).ConfigureAwait(false);
				}
				_killedByUs.Add(process);
				try { if (!process.HasExited) process.Kill(true); } catch { }
				_nextProcessStartUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, _processStartAttempts - 1))));
				throw new HttpRequestException(process.HasExited ? "tbot-sim exited before readiness." : "tbot-sim did not become ready within 10 seconds.");
			} catch {
				if (_processStartAttempts > 0 && _nextProcessStartUtc == DateTime.MinValue)
					_nextProcessStartUtc = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, _processStartAttempts - 1))));
				throw;
			} finally { _processStartLock.Release(); }
		}

		private static RustRequest ToRustRequest(SimulateBattleRequest request) {
			if (request == null || request.Attacker == null || request.Defender == null) throw new ArgumentNullException(nameof(request));
			return new RustRequest {
				attacker = ToParty(request.Attacker, false),
				defender = ToParty(request.Defender, true),
				defender_bonuses = request.Defender.UseRustGeneralClassBonus ? new RustBonuses { player_class = "general", alliance_class = "none", has_engineer = false } : null,
				planet_resources = new RustResources { metal = (ulong)Math.Max(0, request.Defender.Metal), crystal = (ulong)Math.Max(0, request.Defender.Crystal), deuterium = (ulong)Math.Max(0, request.Defender.Deuterium) },
				debris_percentage = (float)(request.FleetToDebris * 100.0),
				use_rapid_fire = true,
				simulations = (uint)Math.Max(1, request.Simulations),
				enable_downscaling = null,
				universe_settings = new RustUniverseSettings { debris_fleet = (byte)Math.Max(0, Math.Min(100, Math.Round(request.FleetToDebris * 100.0, MidpointRounding.AwayFromZero))), debris_defence = 0, debris_deuterium = false },
				plunder_percentage = 50
			};
		}

		private static RustParty ToParty(SimulatorFleet fleet, bool includeDefence) {
			var entities = new Dictionary<int, uint>();
			Add(entities, Buildables.LightFighter, fleet.LightFighter); Add(entities, Buildables.HeavyFighter, fleet.HeavyFighter);
			Add(entities, Buildables.Cruiser, fleet.Cruiser); Add(entities, Buildables.Battleship, fleet.Battleship);
			Add(entities, Buildables.Battlecruiser, fleet.Battlecruiser); Add(entities, Buildables.Bomber, fleet.Bomber);
			Add(entities, Buildables.Destroyer, fleet.Destroyer); Add(entities, Buildables.Deathstar, fleet.Deathstar);
			Add(entities, Buildables.SmallCargo, fleet.SmallCargo); Add(entities, Buildables.LargeCargo, fleet.LargeCargo);
			Add(entities, Buildables.ColonyShip, fleet.ColonyShip); Add(entities, Buildables.Recycler, fleet.Recycler);
			Add(entities, Buildables.EspionageProbe, fleet.EspionageProbe); Add(entities, Buildables.SolarSatellite, fleet.SolarSatellite);
			Add(entities, Buildables.Crawler, fleet.Crawler); Add(entities, Buildables.Reaper, fleet.Reaper); Add(entities, Buildables.Pathfinder, fleet.Pathfinder);
			if (includeDefence && fleet is SimulatorDefender d) {
				Add(entities, Buildables.RocketLauncher, d.RocketLauncher); Add(entities, Buildables.LightLaser, d.LightLaser);
				Add(entities, Buildables.HeavyLaser, d.HeavyLaser); Add(entities, Buildables.GaussCannon, d.GaussCannon);
				Add(entities, Buildables.IonCannon, d.IonCannon); Add(entities, Buildables.PlasmaTurret, d.PlasmaTurret);
				Add(entities, Buildables.SmallShieldDome, d.SmallShieldDome); Add(entities, Buildables.LargeShieldDome, d.LargeShieldDome);
			}
			int appliedGeneralBonus = fleet is SimulatorDefender defender && defender.UseRustGeneralClassBonus ? defender.GeneralClassCombatResearchBonusApplied : 0;
			Dictionary<int, RustLfBonus> lifeform = null;
			if (fleet.LifeformBonuses != null && fleet.LifeformBonuses.Count > 0) {
				lifeform = new Dictionary<int, RustLfBonus>();
				foreach (var kv in fleet.LifeformBonuses) {
					if (!entities.ContainsKey(kv.Key) || kv.Value == null) continue;
					if (kv.Value.Weapon == 0 && kv.Value.Shield == 0 && kv.Value.Armour == 0) continue;
					lifeform[kv.Key] = new RustLfBonus { weapon = kv.Value.Weapon, shield = kv.Value.Shield, armour = kv.Value.Armour };
				}
				if (lifeform.Count == 0) lifeform = null;
			}
			return new RustParty { technology = new RustTechnology { weapon = ToTech(Math.Max(0, fleet.Weapon - appliedGeneralBonus)), shield = ToTech(Math.Max(0, fleet.Shield - appliedGeneralBonus)), armour = ToTech(Math.Max(0, fleet.Armour - appliedGeneralBonus)) }, entities = entities, lifeform = lifeform };
		}

		private static byte ToTech(int level) {
			if (level < 0 || level > 255) throw new InvalidOperationException($"Combat technology level {level} is outside the supported range 0..255.");
			return (byte)level;
		}
		private static void Add(Dictionary<int, uint> entities, Buildables id, long count) {
			if (count <= 0) return;
			if (count > uint.MaxValue) throw new InvalidOperationException($"Entity {id} count exceeds the Rust simulator limit.");
			entities[(int)id] = (uint)count;
		}

		private static SimulatorResult FromRustSummary(SimulateBattleRequest request, RustSummary summary) {
			int sent = ClampInt(request.Attacker.SmallCargo + request.Attacker.LargeCargo + request.Attacker.Pathfinder);
			double survived = 0;
			if (summary.AttackerSurvivorsAvg != null) {
				summary.AttackerSurvivorsAvg.TryGetValue(202, out double small);
				summary.AttackerSurvivorsAvg.TryGetValue(203, out double large);
				summary.AttackerSurvivorsAvg.TryGetValue(219, out double pathfinder);
				survived = small + large + pathfinder;
			}
			return new SimulatorResult {
				Simulations = (int)summary.Simulations,
				AttackerWin = Percent(summary.AttackerWins, summary.Simulations),
				DefenderWin = Percent(summary.DefenderWins, summary.Simulations),
				Draw = Percent(summary.Draws, summary.Simulations),
				Rounds = (int)Math.Round(summary.AverageRounds, MidpointRounding.AwayFromZero),
				AttackerLosses = Price(summary.AttackerLossesAvg), DefenderLosses = Price(summary.DefenderLossesAvg),
				Debris = Price(summary.DebrisAvg),
				Recycler = (int)Math.Ceiling((Price(summary.DebrisAvg).Total()) / 20000.0),
				Moonchance = (int)Math.Round(summary.MoonChance, MidpointRounding.AwayFromZero),
				AttackerCargoSent = sent, AttackerCargoSurvived = survived, Logs = ""
			};
		}
		private static int Percent(uint wins, uint simulations) => (int)Math.Round((double)wins * 100.0 / simulations, MidpointRounding.AwayFromZero);
		private static int ClampInt(long value) => (int)Math.Max(0, Math.Min(int.MaxValue, value));
		private static SimulatorPrice Price(RustResources r) => new SimulatorPrice { Metal = ToLong(r?.metal ?? 0), Crystal = ToLong(r?.crystal ?? 0), Deuterium = ToLong(r?.deuterium ?? 0) };
		private static long ToLong(ulong value) => value > long.MaxValue ? long.MaxValue : (long)value;

		private readonly HashSet<Process> _killedByUs = new HashSet<Process>();

		private void OnExternalProcessExited(Process process) {
			_processStartLock.Wait();
			try {
				if (_shutdownRequested || _killedByUs.Remove(process) || !ReferenceEquals(process, _externalProcess)) return;
				var now = DateTime.UtcNow;
				_processCrashTimesUtc.Enqueue(now);
				while (_processCrashTimesUtc.Count > 0 && now - _processCrashTimesUtc.Peek() > TimeSpan.FromMinutes(10)) _processCrashTimesUtc.Dequeue();
				if (_processCrashTimesUtc.Count >= 5) {
					_processStartAttempts = 0;
					_nextProcessStartUtc = now.AddMinutes(30);
					_bot?.log(Microsoft.Extensions.Logging.LogLevel.Warning, TBot.Common.Logging.LogSender.AutoFarm, "External combat simulator crashed five times within ten minutes; automatic restart is suspended for 30 minutes and Go fallback remains active.");
				} else {
					_processStartAttempts = Math.Min(5, _processStartAttempts + 1);
					_nextProcessStartUtc = now.AddSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Max(0, _processStartAttempts - 1))));
				}
			} finally { _processStartLock.Release(); }
			RequestStats.DevLog?.Invoke("CombatSimulator", $"tbot-sim process exited (code {SafeExitCode(process)}); restart backoff is active.");
		}

		private static string SafeExitCode(Process process) { try { return process.HasExited ? process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) : "running"; } catch { return "unknown"; } }


		public async Task<ManualSimOutput> RunManualAsync(ManualSimInput input) {
			var request = BuildManualRequest(input);
			var output = new ManualSimOutput();
			var goTask = RunSideAsync(() => _ogame.SimulateBattle(request));
			var rustTask = RunSideAsync(() => SimulateExternal(request));
			await Task.WhenAll(goTask, rustTask).ConfigureAwait(false);
			output.Ogame = goTask.Result.Side;
			output.Rust = rustTask.Result.Side;
			if (ReadBool("ExternalCalc.LogManualRuns", true)) {
				try {
					BattleLogStore.Append(new BattleLogRecord {
						Mode = "manual", BestCalc = null, Simulations = request.Simulations,
						Go = goTask.Result.Raw == null ? null : ToLogSide(goTask.Result.Raw), Rust = rustTask.Result.Raw == null ? null : ToLogSide(rustTask.Result.Raw),
						Input = ToLogInput(request), GoMs = output.Ogame.Ms, RustMs = output.Rust.Ms,
						Error = output.Rust.Ok ? null : Truncate(output.Rust.Error, 500)
					}, ReadInt("ExternalCalc.BattleLogRetentionDays", 30));
				} catch (Exception ex) { RequestStats.DevLog?.Invoke("CombatSimulator", "Battle JSONL manual append failed: " + ex.Message); }
			}
			return output;
		}

		private static string Truncate(string text, int max) => text == null ? null : (text.Length > max ? text.Substring(0, max) : text);

		private static async Task<(ManualSimSide Side, SimulatorResult Raw)> RunSideAsync(Func<Task<SimulatorResult>> call) {
			var watch = Stopwatch.StartNew();
			try {
				var result = await call().ConfigureAwait(false);
				watch.Stop();
				return (ToManualSide(result, watch.ElapsedMilliseconds), result);
			} catch (Exception ex) {
				watch.Stop();
				return (new ManualSimSide { Ok = false, Error = $"{ex.GetType().Name}: {ex.Message}", Ms = watch.ElapsedMilliseconds }, null);
			}
		}

		private static ManualSimSide ToManualSide(SimulatorResult r, double ms) => new ManualSimSide {
			Ok = true, Ms = ms, Simulations = r.Simulations, AttackerWin = r.AttackerWin, DefenderWin = r.DefenderWin, Draw = r.Draw, Rounds = r.Rounds,
			AttackerLosses = Triple(r.AttackerLosses), DefenderLosses = Triple(r.DefenderLosses), Debris = Triple(r.Debris), Recycler = r.Recycler, MoonChance = r.Moonchance
		};
		private static long[] Triple(SimulatorPrice p) => p == null ? new long[3] : new[] { p.Metal, p.Crystal, p.Deuterium };

		internal SimulateBattleRequest BuildManualRequest(ManualSimInput input) {
			if (input == null) throw new ArgumentException("No input.");
			int maxRuns = Math.Clamp(Math.Max(5000, Math.Max(ReadInt("SimulationRuns", 500), ReadInt("EscortSearchSimulationRuns", 100))), 5000, 50000);
			if (input.Simulations < 1 || input.Simulations > maxRuns) throw new ArgumentException($"Simulations must be between 1 and {maxRuns}.");
			if (input.FleetToDebrisPercent < 0 || input.FleetToDebrisPercent > 100) throw new ArgumentException("Fleet to debris must be between 0 and 100 percent.");
			var attackerTech = CheckTech(input.AttackerTech, "Attacker");
			var defenderTech = CheckTech(input.DefenderTech, "Defender");
			if (input.Resources == null || input.Resources.Length != 3 || input.Resources.Any(x => x < 0)) throw new ArgumentException("Resources must be three non-negative numbers.");

			int generalBonus = ReadInt("GeneralClassCombatResearchBonus", 2);
			bool general = input.DefenderIsGeneral && generalBonus != 0;
			var attacker = new SimulatorFleet { Weapon = attackerTech[0], Shield = attackerTech[1], Armour = attackerTech[2] };
			var defender = new SimulatorDefender {
				Weapon = defenderTech[0] + (general ? generalBonus : 0), Shield = defenderTech[1] + (general ? generalBonus : 0), Armour = defenderTech[2] + (general ? generalBonus : 0),
				Metal = input.Resources[0], Crystal = input.Resources[1], Deuterium = input.Resources[2],
				CharacterClass = input.DefenderIsGeneral ? CharacterClass.General : CharacterClass.NoClass,
				GeneralClassCombatResearchBonusApplied = general ? generalBonus : 0,
				UseRustGeneralClassBonus = general && generalBonus == 2
			};
			ApplyCounts(attacker, input.AttackerShips, "attacker");
			ApplyCounts(defender, input.DefenderEntities, "defender");
			return new SimulateBattleRequest {
				Attacker = attacker, Defender = defender, Simulations = input.Simulations,
				FleetToDebris = input.FleetToDebrisPercent / 100.0, Mode = "manual"
			};
		}

		private static int[] CheckTech(int[] tech, string side) {
			if (tech == null || tech.Length != 3 || tech.Any(x => x < 0 || x > 253)) throw new ArgumentException($"{side} technology must be three levels between 0 and 253.");
			return tech;
		}

		private static void ApplyCounts(SimulatorFleet target, Dictionary<string, long> counts, string side) {
			if (counts == null) return;
			foreach (var kv in counts) {
				if (kv.Value == 0) continue;
				if (kv.Value < 0 || kv.Value > uint.MaxValue) throw new ArgumentException($"Count for {kv.Key} ({side}) is out of range.");
				var property = target.GetType().GetProperty(kv.Key, BindingFlags.Public | BindingFlags.Instance);
				if (property == null || property.PropertyType != typeof(long) || !Enum.TryParse(kv.Key, out Buildables _))
					throw new ArgumentException($"Unknown {side} unit '{kv.Key}'.");
				property.SetValue(target, kv.Value);
			}
		}

		private void StopExternalProcess() {
			_shutdownRequested = true;
			try {
				if (_externalProcess != null && !_externalProcess.HasExited) {
					_externalProcess.Kill(true);
					_externalProcess.WaitForExit(2000);
					RequestStats.DevLog?.Invoke("CombatSimulator", "Stopped tbot-sim child process during TBot shutdown.");
				}
			} catch (Exception ex) { RequestStats.DevLog?.Invoke("CombatSimulator", "Could not stop tbot-sim child process: " + ex.Message); }
		}

		private string ReadString(string key, string fallback) { try { object v = ResolveSetting(key); return v == null ? fallback : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? fallback; } catch { return fallback; } }
		private int ReadInt(string key, int fallback) { try { object v = ResolveSetting(key); return v == null ? fallback : Convert.ToInt32(v); } catch { return fallback; } }
		private bool ReadBool(string key, bool fallback) { try { object v = ResolveSetting(key); return v == null ? fallback : Convert.ToBoolean(v); } catch { return fallback; } }
		private object ResolveSetting(string path) => ResolveSettingValue(_bot?.InstanceSettings?.AutoFarm, path);

		internal static object ResolveSettingValue(object root, string path) {
			object current = root;
			foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries)) {
				if (current == null) return null;
				if (current is IDictionary<string, object> dictionary) {
					var match = dictionary.FirstOrDefault(kv => string.Equals(kv.Key, part, StringComparison.OrdinalIgnoreCase));
					if (match.Key == null) return null;
					current = match.Value;
					continue;
				}
				if (current is JObject jo) { current = jo.GetValue(part, StringComparison.OrdinalIgnoreCase); continue; }
				if (current is JToken jt && jt.Type == JTokenType.Object) { current = ((JObject)jt).GetValue(part, StringComparison.OrdinalIgnoreCase); continue; }
				var prop = current.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
				if (prop != null) current = prop.GetValue(current);
				else return null;
			}
			if (current is JValue jv) return jv.Value;
			return current;
		}
		private void RegisterFailure(Exception ex) {
			lock (_stateLock) {
				_consecutiveFailures++;
				if (_consecutiveFailures >= 3) { _circuitOpenUntilUtc = DateTime.UtcNow.AddSeconds(60); _consecutiveFailures = 0; }
			}
			RequestStats.DevLog?.Invoke("CombatSimulator", $"External simulator failure: {ex.GetType().Name}: {ex.Message}");
		}
		private void WarnFallbackOnce(string message) {
			lock (_stateLock) { if ((DateTime.UtcNow - _lastWarningUtc).TotalMinutes < 1) return; _lastWarningUtc = DateTime.UtcNow; }
			_bot?.log(Microsoft.Extensions.Logging.LogLevel.Warning, TBot.Common.Logging.LogSender.AutoFarm, $"External combat simulator unavailable; using ogame fallback. {message}");
		}
		private void LogSourceOnce(string source) {
			lock (_stateLock) { if (_lastLoggedSource == source) return; _lastLoggedSource = source; }
			_bot?.log(Microsoft.Extensions.Logging.LogLevel.Information, TBot.Common.Logging.LogSender.AutoFarm, $"Combat calculator: {source}");
		}
	}

	internal static class SimulatorClientProvider {
		private static readonly ConcurrentDictionary<string, SwitchingSimulatorClient> Clients = new ConcurrentDictionary<string, SwitchingSimulatorClient>(StringComparer.OrdinalIgnoreCase);

		public static ISimulatorClient GetOrCreate(ITBotMain bot, IOgameService service) {
			string alias = string.IsNullOrWhiteSpace(bot?.InstanceAlias) ? "MAIN" : bot.InstanceAlias;
			var client = Clients.GetOrAdd(alias, _ => new SwitchingSimulatorClient(bot, service));
			SimulatorBridge.ManualSimulation = RunManualAsync;
			return client;
		}

		private static Task<ManualSimOutput> RunManualAsync(string alias, ManualSimInput input) {
			if (!Clients.TryGetValue(alias ?? "", out var client)) client = Clients.Values.FirstOrDefault();
			if (client == null) throw new InvalidOperationException("The simulator is not available yet: no AutoFarm worker has been started.");
			return client.RunManualAsync(input);
		}
	}

	internal sealed class ExternalSimulatorHttpException : HttpRequestException {
		public int HttpStatus { get; }
		public ExternalSimulatorHttpException(int statusCode, string message) : base($"HTTP {statusCode}: {message}") => HttpStatus = statusCode;
	}

	internal sealed class RustRequest {
		public RustParty attacker { get; set; } public RustParty defender { get; set; }
		public RustResources planet_resources { get; set; } public float debris_percentage { get; set; }
		public bool use_rapid_fire { get; set; } public uint simulations { get; set; }
		public object enable_downscaling { get; set; } public RustUniverseSettings universe_settings { get; set; }
		public object attacker_bonuses { get; set; } public object defender_bonuses { get; set; } public int plunder_percentage { get; set; }
	}
	internal sealed class RustParty {
		public RustTechnology technology { get; set; } public Dictionary<int, uint> entities { get; set; }
		[Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
		public Dictionary<int, RustLfBonus> lifeform { get; set; }
	}
	internal sealed class RustLfBonus { public float weapon { get; set; } public float shield { get; set; } public float armour { get; set; } }
	internal sealed class RustBonuses { public string player_class { get; set; } public string alliance_class { get; set; } public bool has_engineer { get; set; } }
	internal sealed class RustTechnology { public byte weapon { get; set; } public byte shield { get; set; } public byte armour { get; set; } }
	internal sealed class RustUniverseSettings { public byte debris_fleet { get; set; } public byte debris_defence { get; set; } public bool debris_deuterium { get; set; } }
	internal sealed class RustResources { public ulong metal { get; set; } public ulong crystal { get; set; } public ulong deuterium { get; set; } }
	internal sealed class RustSummary {
		public string version { get; set; } public int schema { get; set; } public uint simulations { get; set; } public uint attacker_wins { get; set; } public uint defender_wins { get; set; } public uint draws { get; set; }
		public double average_rounds { get; set; } public RustResources attacker_losses_avg { get; set; } public RustResources defender_losses_avg { get; set; } public RustResources debris_avg { get; set; }
		public double moon_chance { get; set; } public Dictionary<int, double> attacker_survivors_avg { get; set; }
		[JsonIgnore] public string Version { get => version; set => version = value; }
		[JsonIgnore] public int Schema { get => schema; set => schema = value; }
		[JsonIgnore] public uint Simulations { get => simulations; set => simulations = value; }
		[JsonIgnore] public uint AttackerWins { get => attacker_wins; set => attacker_wins = value; }
		[JsonIgnore] public uint DefenderWins { get => defender_wins; set => defender_wins = value; }
		[JsonIgnore] public uint Draws { get => draws; set => draws = value; }
		[JsonIgnore] public double AverageRounds { get => average_rounds; set => average_rounds = value; }
		[JsonIgnore] public RustResources AttackerLossesAvg { get => attacker_losses_avg; set => attacker_losses_avg = value; }
		[JsonIgnore] public RustResources DefenderLossesAvg { get => defender_losses_avg; set => defender_losses_avg = value; }
		[JsonIgnore] public RustResources DebrisAvg { get => debris_avg; set => debris_avg = value; }
		[JsonIgnore] public double MoonChance { get => moon_chance; set => moon_chance = value; }
		[JsonIgnore] public Dictionary<int, double> AttackerSurvivorsAvg { get => attacker_survivors_avg; set => attacker_survivors_avg = value; }
	}
}
