using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
#if FASTFARM
using Tbot.Common.Settings;
#endif
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {


	public abstract class WorkerBase : ITBotWorker {
		protected readonly ITBotMain _tbotInstance;

		protected CancellationToken _ct = CancellationToken.None;
		protected Dictionary<string, Timer> timers = new();

		private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
		private AsyncTimer _timer = null;

		protected IWorkerFactory _workerFactory;
		protected ConcurrentDictionary<Celestial, ITBotCelestialWorker> _celestialWorkers = new();

		public ConcurrentDictionary<Celestial, ITBotCelestialWorker> celestialWorkers {
			get {
				return (_celestialWorkers != null) ? _celestialWorkers : new();
			}
		}

		public TimeSpan DueTime {
			get {
				return (_timer != null) ? _timer.DueTime : TimeSpan.Zero;
			}
		}
		public TimeSpan Period {
			get {
				return (_timer != null) ? _timer.Period : TimeSpan.Zero;
			}
		}

		public DateTime? NextRunUtc => _timer?.NextFireUtc;
		public DateTime? LastRunUtc => _timer?.LastEndUtc;
		public bool IsBusy => _timer != null && _timer.CallbackRunning;

		public WorkerBase(ITBotMain parentInstance) {
			_tbotInstance = parentInstance;
		}

		protected abstract Task Execute();

		private TimeSpan? _gateRestorePeriod;
		private TimeSpan _gatePeriodSet;
		private void GateRestorePeriod() {
			if (_gateRestorePeriod == null)
				return;
			var alt = _gateRestorePeriod.Value;
			_gateRestorePeriod = null;
			if (Period == _gatePeriodSet)
				ChangeWorkerPeriod(alt);
		}

		public void DoLog(LogLevel level, string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		protected virtual void OnWorkerStarting() { }

		private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _workerAus = new();
		private string WorkerSchluessel => $"{_tbotInstance.InstanceAlias}|{GetWorkerName()}";
		private bool WorkerIstAus => _workerAus.TryGetValue(WorkerSchluessel, out var aus) && aus;
		private bool LogLifecycle { get { try { return IsWorkerEnabledBySettings(); } catch (Exception) { return true; } } }
		private bool MeldeAus() { bool war = WorkerIstAus; _workerAus[WorkerSchluessel] = true; return !war; }

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			if (LogLifecycle) DoLog(LogLevel.Information, $"Starting Worker \"{GetWorkerName()}\"..");
			try { OnWorkerStarting(); } catch (Exception) {  }

			await StopWorker();

			_ct = ct;

			string cutAlias = (_tbotInstance.InstanceAlias.Length > 6 ? _tbotInstance.InstanceAlias.Substring(0, 6) : _tbotInstance.InstanceAlias);
			_timer = new AsyncTimer(ExecutionWrapper, $"{cutAlias}{GetWorkerName()}");
			await _timer.StartAsync(ct, period, dueTime);
		}
		public async Task StartWorker(CancellationToken ct, TimeSpan dueTime) {
			await StartWorker(ct, Timeout.InfiniteTimeSpan, dueTime);
		}
		public async Task StopWorker() {
			RemoveAllTimers();
			if (_timer != null) {
				bool meldung = LogLifecycle;
				if (meldung) DoLog(LogLevel.Information, $"Closing Worker \"{GetWorkerName()}\"..");
				await _timer.DisposeAsync();
				if (meldung) DoLog(LogLevel.Information, $"Worker \"{GetWorkerName()}\" closed!");
				_timer = null;
			}
			await Task.WhenAll(_celestialWorkers.Values.ToList().Select(w => w.StopWorker()));
		}
		public void ChangeWorkerPeriod(long periodMs) {
			if (periodMs >= int.MaxValue)
				periodMs = int.MaxValue;
			ChangeWorkerPeriod(TimeSpan.FromMilliseconds(periodMs));
		}
		public void ChangeWorkerPeriod(TimeSpan period) {
			if (_timer != null)
				_timer.ChangePeriod(period);
		}
		public void ChangeWorkerDueTime(TimeSpan dueTime) {
			if (_timer != null)
				_timer.ChangeDueTime(dueTime);
		}
		public void ChangeWorkerDueTime(long dueTimeMs) {
			if (_timer != null)
				_timer.ChangeDueTime(TimeSpan.FromMilliseconds(dueTimeMs));
		}
		public async void RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) { try {
			if (LogLifecycle) DoLog(LogLevel.Information, $"Restarting Worker \"{GetWorkerName()}\"...");
			await Task.WhenAll(_celestialWorkers.Values.ToList().Select(w => w.StopWorker()));
			await StartWorker(ct, period, dueTime);
		} catch (Exception asyncVoidEx) { DoLog(LogLevel.Error, $"Unhandled error in RestartWorker: {asyncVoidEx}"); }
		}
		public bool IsWorkerRunning() {
			return (_timer != null) && (_timer.IsRunning);
		}
		public void SetSemaphore(SemaphoreSlim sem) {
			_sem.Dispose();
			_sem = sem;
		}

		public SemaphoreSlim GetSemaphore() {
			return _sem;
		}
		public async Task WaitWorker() {
			try {
				await _sem.WaitAsync(_ct);
			} catch (OperationCanceledException) {
			}
		}
		public void ReleaseWorker() {
			if (_sem.CurrentCount == 1) {
				DoLog(LogLevel.Warning, $"{GetWorkerName()} already released...");
			} else {
				_sem.Release();
			}
		}

		public abstract bool IsWorkerEnabledBySettings();
		public abstract string GetWorkerName();
		public abstract Feature GetFeature();
		public abstract LogSender GetLogSender();

		protected virtual TimeSpan ExecuteTimeout => TimeSpan.FromSeconds(45);

		protected virtual TimeSpan HangDetectionTimeout => TimeSpan.FromMinutes(10);

		private long _lastHeartbeatTicks = DateTime.UtcNow.Ticks;

		protected void Heartbeat() {
			Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
		}

		private DateTime LastHeartbeatUtc => new DateTime(Interlocked.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);

		protected Task EndExecution() {
			ChangeWorkerPeriod(Timeout.InfiniteTimeSpan);
			foreach (var worker in _celestialWorkers.Values) {
				worker.ChangeWorkerPeriod(Timeout.InfiniteTimeSpan);
			}
			return Task.CompletedTask;
		}

#if FASTFARM
		private bool IsSuppressedByFastFarm() {
			try {
				Feature feature = GetFeature();
				if (feature == Feature.FastFarm || feature == Feature.Defender || feature == Feature.SleepMode || feature == Feature.GalaxyCache)
					return false;
				var settings = _tbotInstance.InstanceSettings;
				if (!SettingsService.IsSettingSet(settings, "FastFarm"))
					return false;
				var fastFarm = settings.FastFarm;
				if (!SettingsService.IsSettingSet(fastFarm, "Active") || !(bool) fastFarm.Active)
					return false;
				if (SettingsService.IsSettingSet(fastFarm, "OnlyWorker") && !(bool) fastFarm.OnlyWorker)
					return false;
				if (feature == Feature.Expeditions && SettingsService.IsSettingSet(fastFarm, "KeepExpeditions") && (bool) fastFarm.KeepExpeditions)
					return false;
				return true;
			} catch (Exception) {
				return false;
			}
		}

		private DateTime _lastFastFarmBuildRun = DateTime.MinValue;
		private int FastFarmBuildAccess(out TimeSpan wait) {
			wait = TimeSpan.Zero;
			try {
				Feature feature = GetFeature();
				if (feature != Feature.BrainAutoMine && feature != Feature.BrainAutoResearch && feature != Feature.BrainLifeformAutoMine && feature != Feature.BrainLifeformAutoResearch)
					return 0;
				var fastFarm = _tbotInstance.InstanceSettings.FastFarm;
				int minutes = SettingsService.IsSettingSet(fastFarm, "BuildCheckMinutes") ? (int) fastFarm.BuildCheckMinutes : 60;
				if (minutes <= 0)
					return 0;
				var own = _tbotInstance.UserData.celestials;
				var fleets = _tbotInstance.UserData.fleets;
				if (own != null && fleets != null) {
					bool incoming = fleets.Any(f => f.Mission == Missions.Transport && !f.ReturnFlight && f.Resources != null && f.Resources.TotalResources > 0
						&& own.Any(c => c.Coordinate.Galaxy == f.Destination.Galaxy && c.Coordinate.System == f.Destination.System && c.Coordinate.Position == f.Destination.Position && c.Coordinate.Type == f.Destination.Type));
					if (incoming) {
						DoLog(LogLevel.Debug, $"{GetWorkerName()}: FastFarm is active, but transports with resources are on their way - running this cycle.");
						return 1;
					}
				}
				var seitLetztem = DateTime.UtcNow - _lastFastFarmBuildRun;
				if (seitLetztem >= TimeSpan.FromMinutes(minutes)) {
					_lastFastFarmBuildRun = DateTime.UtcNow;
					DoLog(LogLevel.Debug, $"{GetWorkerName()}: FastFarm is active - short check for possible constructions (every {minutes} min).");
					return 1;
				}
				wait = TimeSpan.FromMinutes(minutes) - seitLetztem + TimeSpan.FromSeconds(Random.Shared.Next(5, 60));
				return 2;
			} catch (Exception) {
				return 0;
			}
		}
#endif

		protected Task RunNowAsync(bool ignoreSwitches = false) => ExecutionWrapper(_ct, ignoreSwitches);

		private Task ExecutionWrapper(CancellationToken ct) => ExecutionWrapper(ct, false);

		private async Task ExecutionWrapper(CancellationToken ct, bool ignoreSwitches) {
			TBot.Ogame.Infrastructure.RequestStats.Current.Value = GetWorkerName();

			if (_tbotInstance.UserData.isSleeping == true) {
				DoLog(LogLevel.Debug, $"Sleeping... Ending {GetWorkerName()}");
				await EndExecution();
				return;
			} else if (IsWorkerEnabledBySettings() == false && !ignoreSwitches) {
				await EndExecution();
				return;
#if FASTFARM
			} else if (!ignoreSwitches && IsSuppressedByFastFarm()) {
				int zugang = FastFarmBuildAccess(out TimeSpan fastFarmWait);
				if (zugang == 2) {
					DoLog(LogLevel.Debug, $"{GetWorkerName()}: FastFarm is active, next construction check in {fastFarmWait:hh\\:mm\\:ss}.");
					ChangeWorkerPeriod(fastFarmWait);
					return;
				}
				if (zugang == 0) {
					DoLog(MeldeAus() ? LogLevel.Information : LogLevel.Debug, $"{GetWorkerName()} is switched off while FastFarm is active. Ending...");
					await EndExecution();
					return;
				}
#endif
			}

			_workerAus[WorkerSchluessel] = false;

			var pauseGate = _tbotInstance.PauseGateDelay(GetFeature());
			if (pauseGate != null) {
				if (_gateRestorePeriod == null)
					_gateRestorePeriod = Period;
				_gatePeriodSet = pauseGate.Value;
				DoLog(LogLevel.Debug, $"Pause: {GetWorkerName()} skips this cycle, next run in {pauseGate.Value:mm\\:ss}");
				ChangeWorkerPeriod(pauseGate.Value);
				return;
			}

			bool releaseNow = true;
			try {
				await WaitWorker();

				ct.ThrowIfCancellationRequested();

				Heartbeat();

				var executeTimeout = ExecuteTimeout;
				var executeTask = Execute();
				var completedTask = await Task.WhenAny(executeTask, Task.Delay(executeTimeout, ct));
				if (completedTask == executeTask && !executeTask.IsFaulted)
					GateRestorePeriod();
				if (completedTask != executeTask) {
					DoLog(LogLevel.Warning, $"{GetWorkerName()}: Execute() did not complete within {executeTimeout.TotalSeconds}s - abandoning this cycle, worker continues on next scheduled run.");
					releaseNow = false;
					int releasedOnce = 0;
					_ = executeTask.ContinueWith(t => {
						if (t.IsFaulted)
							DoLog(LogLevel.Error, $"{GetWorkerName()}: Execute() (abandoned cycle) later failed: {t.Exception.GetBaseException().Message}");
						if (Interlocked.Exchange(ref releasedOnce, 1) == 0) ReleaseWorker();
					});
					var hangDetectionTimeout = HangDetectionTimeout;
					_ = Task.Run(async () => {
						while (Interlocked.CompareExchange(ref releasedOnce, 0, 0) == 0 && !executeTask.IsCompleted) {
							await Task.Delay(TimeSpan.FromSeconds(30));
							if (DateTime.UtcNow - LastHeartbeatUtc > hangDetectionTimeout) {
								if (Interlocked.Exchange(ref releasedOnce, 1) == 0) {
									DoLog(LogLevel.Error, $"{GetWorkerName()}: abandoned Execute() cycle sent no heartbeat for {hangDetectionTimeout.TotalMinutes:F0}min - looks genuinely stuck (not just slow), force-releasing the worker lock so it doesn't stay silent forever.");
									ReleaseWorker();
								}
								return;
							}
						}
					});
					return;
				} else if (executeTask.IsFaulted) {
					DoLog(LogLevel.Error, $"{GetWorkerName()}: Execute() failed: {executeTask.Exception.GetBaseException().Message}");
				}

				if (Period != Timeout.InfiniteTimeSpan) {
					DoLog(LogLevel.Information, $"Next {GetWorkerName()} execution in {Period}");
				}
				else {
					DoLog(LogLevel.Information, $"{GetWorkerName()} Stopped.");
				}

			} catch(OperationCanceledException) {
			} catch (Exception ex) {
				DoLog(LogLevel.Error, $"{GetWorkerName()}: unexpected exception in ExecutionWrapper, worker keeps running in the next cycle: {ex}");
			} finally {
				if (releaseNow)
					ReleaseWorker();
			}
		}

		private void RemoveAllTimers() {
			foreach (var tim in timers) {
				DoLog(LogLevel.Information, $"Deleting timer \"{tim.Key}\" for worker \"{GetWorkerName()}\"");
				tim.Value.Dispose();
			}
			timers.Clear();
		}
	}
}