using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {

	public delegate Task WorkerFunction(CancellationToken ct);

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

		public WorkerBase(ITBotMain parentInstance) {
			_tbotInstance = parentInstance;
		}

		protected abstract Task Execute();
		public void DoLog(LogLevel level, string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			DoLog(LogLevel.Information, $"Starting Worker \"{GetWorkerName()}\"..");

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
				DoLog(LogLevel.Information, $"Closing Worker \"{GetWorkerName()}\"..");
				await _timer.DisposeAsync();
				DoLog(LogLevel.Information, $"Worker \"{GetWorkerName()}\" closed!");
				_timer = null;
			}
			foreach (var worker in _celestialWorkers.Values) {
				await worker.StopWorker();
			}
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
		public async void RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			DoLog(LogLevel.Information, $"Restarting Worker \"{GetWorkerName()}\"...");
			foreach (var worker in _celestialWorkers.Values) {
				await worker.StopWorker();
			}
			await StartWorker(ct, period, dueTime);
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

		private async Task ExecutionWrapper(CancellationToken ct) {

			if (_tbotInstance.UserData.isSleeping == true) {
				DoLog(LogLevel.Debug, $"Sleeping... Ending {GetWorkerName()}");
				await EndExecution();
				return;
			} else if (IsWorkerEnabledBySettings() == false) {
				DoLog(LogLevel.Information, $"{GetWorkerName()} not enabled by settings. Ending...");
				await EndExecution();
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