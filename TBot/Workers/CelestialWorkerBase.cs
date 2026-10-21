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

	public abstract class CelestialWorkerBase : ITBotWorker, ITBotCelestialWorker {
		protected readonly ITBotMain _tbotInstance;

		protected CancellationToken _ct = CancellationToken.None;
		protected Dictionary<string, Timer> timers = new();

		private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
		private AsyncTimer _timer = null;
		private TaskCompletionSource<bool> _firstExecutionCompletion = CreateCompletionSource();
		private Task _executionPredecessor = Task.CompletedTask;
		
		private Celestial _celestial = null;
		private ITBotWorker _parentWorker = null;

		public ITBotWorker parentWorker {
			get {
				return (_parentWorker != null) ? _parentWorker : null;
			}
		}

		public Celestial celestial {
			get {
				return (_celestial != null) ? _celestial : null;
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
		public Task FirstExecutionCompleted => _firstExecutionCompletion.Task;

		public ConcurrentDictionary<Celestial, ITBotCelestialWorker> celestialWorkers => throw new NotImplementedException();

		public CelestialWorkerBase(ITBotMain parentInstance, ITBotWorker parentWorker, Celestial celestial) {
			_tbotInstance = parentInstance;
			_parentWorker = parentWorker;
			_celestial = celestial;
		}

		protected abstract Task Execute();
		public void DoLog(LogLevel level, string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			DoLog(LogLevel.Information, $"Starting Worker \"{GetWorkerName()}\"..");

			await StopWorker();
			_firstExecutionCompletion = CreateCompletionSource();

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
			DoLog(LogLevel.Information, $"Restarting Worker \"{GetWorkerName()}\"...");
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
		public void StartAfter(Task predecessor) {
			_executionPredecessor = predecessor ?? Task.CompletedTask;
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



		protected Task EndExecution() {
			ChangeWorkerPeriod(Timeout.InfiniteTimeSpan);
			return Task.CompletedTask;
		}

		private async Task ExecutionWrapper(CancellationToken ct) {
			TBot.Ogame.Infrastructure.RequestStats.Current.Value = (_parentWorker != null) ? _parentWorker.GetWorkerName() : GetWorkerName();
			var firstExecutionCompletion = _firstExecutionCompletion;

			if (_tbotInstance.UserData.isSleeping == true) {
				DoLog(LogLevel.Debug, $"Sleeping... Ending {GetWorkerName()}");
				await EndExecution();
				firstExecutionCompletion.TrySetResult(true);
				return;
			} else if (IsWorkerEnabledBySettings() == false) {
				DoLog(LogLevel.Information, $"{GetWorkerName()} not enabled by settings. Ending...");
				await EndExecution();
				firstExecutionCompletion.TrySetResult(true);
				return;
			}

			try {
#pragma warning disable VSTHRD003
				await _executionPredecessor;
#pragma warning restore VSTHRD003
				await WaitWorker();

				ct.ThrowIfCancellationRequested();

				await Execute();

				if (Period != Timeout.InfiniteTimeSpan) {
					DoLog(LogLevel.Information, $"Next {GetWorkerName()} execution in {Period}");
				}
				else {
					DoLog(LogLevel.Information, $"{GetWorkerName()} Stopped.");
				}

			} catch(OperationCanceledException) {
			} finally {
				ReleaseWorker();
				firstExecutionCompletion.TrySetResult(true);
			}
		}

		private static TaskCompletionSource<bool> CreateCompletionSource() {
			return new(TaskCreationOptions.RunContinuationsAsynchronously);
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
