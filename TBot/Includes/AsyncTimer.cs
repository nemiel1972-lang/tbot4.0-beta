using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TBot.Common.Logging;

namespace Tbot.Includes {
	public delegate Task AsyncTimerCallback(CancellationToken ct);
	internal class AsyncTimer : IAsyncDisposable {
		private readonly AsyncTimerCallback _callback;
		private readonly string _name;
		private readonly object _changeLock = new();
		private SemaphoreSlim _startStopSemaphore = new SemaphoreSlim(1, 1);

		private bool _canSetBiggerPeriod = true;

		private TimeSpan _dueTime;
		private TimeSpan _period;

		public TimeSpan Period {
			get {
				lock (_changeLock) {
					return _period;
				}
			}
			private set {
				lock (_changeLock) {
					_period = value;
				}
			}
		}

		public TimeSpan DueTime {
			get {
				lock (_changeLock) {
					return _dueTime;
				}
			}
			private set {
				lock (_changeLock) {
					_dueTime = value;
				}
			}
		}

		private long _nextFireTicks;
		private long _lastEndTicks;
		private volatile bool _callbackRunning;
		public DateTime? NextFireUtc { get { long t = Interlocked.Read(ref _nextFireTicks); return t == 0 ? (DateTime?) null : new DateTime(t, DateTimeKind.Utc); } }
		public DateTime? LastEndUtc { get { long t = Interlocked.Read(ref _lastEndTicks); return t == 0 ? (DateTime?) null : new DateTime(t, DateTimeKind.Utc); } }
		public bool CallbackRunning => _callbackRunning;
		private void SetNextFire(TimeSpan wait) {
			Interlocked.Exchange(ref _nextFireTicks, wait == Timeout.InfiniteTimeSpan ? 0 : DateTime.UtcNow.Add(wait).Ticks);
		}

		public bool _IsRunning { get; private set; }
		public bool IsRunning {
			get {
				return _IsRunning;
			}
			private set {
				_IsRunning = value;
			}
		}

		private Task _scheduledAction = null;
		private CancellationTokenSource _cts = null;

		public AsyncTimer(AsyncTimerCallback callback, string name) {
			_callback = callback;
			_name = name;
		}

		public async Task StartAsync(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			await StopAsync();

			

			if (dueTime < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(dueTime), "due time must be equal or greater than zero");
			DueTime = EnsureMaximumTimeSpan(dueTime, TimeSpan.FromDays(1));

			if ((period < TimeSpan.Zero) && (period != Timeout.InfiniteTimeSpan))
				throw new ArgumentOutOfRangeException(nameof(period), "period must be equal or greater than zero");
			Period = EnsureMaximumTimeSpan(period, TimeSpan.FromDays(1));

			await _startStopSemaphore.WaitAsync();
			_canSetBiggerPeriod = true;
			try {
				var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
				_cts = cts;

				_scheduledAction = Task.Run(async () => {
					Thread.CurrentThread.Name = $"AT_{_name}";
					IsRunning = true;

					try {
						while (cts.Token.IsCancellationRequested == false) {

							SetNextFire(DueTime);
							await Task.Delay(DueTime, cts.Token);

							Interlocked.Exchange(ref _nextFireTicks, 0);
							_callbackRunning = true;
							try {
								await _callback(cts.Token);
							} finally {
								_callbackRunning = false;
								Interlocked.Exchange(ref _lastEndTicks, DateTime.UtcNow.Ticks);
							}

							lock (_changeLock) {
								_canSetBiggerPeriod = true;
							}

							if (Period == Timeout.InfiniteTimeSpan) {
								break;
							}

							SetNextFire(Period);
							await Task.Delay(Period, cts.Token);
						}
					} catch (OperationCanceledException) {
					} finally {
						if (_cts == null || ReferenceEquals(_cts, cts))
							IsRunning = false;
					}

				}, cts.Token);
			} finally {
				_startStopSemaphore.Release();
			}
		}

		public async Task StopAsync(TimeSpan? timeout = null) {
			await _startStopSemaphore.WaitAsync();
			try {
				if (_scheduledAction != null) {
					if (_cts != null) {
						_cts.Cancel();
					}
					var stopTimeout = timeout ?? TimeSpan.FromSeconds(10);
					var completed = await Task.WhenAny(_scheduledAction, Task.Delay(stopTimeout));
					if (completed != _scheduledAction) {
						Console.Error.WriteLine($"[AsyncTimer] \"{_name}\": callback did not stop within {stopTimeout.TotalSeconds}s after cancellation - abandoning wait.");
					}
					_cts = null;
					_scheduledAction = null;
				}
			} finally {
				_startStopSemaphore.Release();
			}
		}

		public async Task RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			await StopAsync();
			await StartAsync(ct, period, dueTime);
		}

		private bool IsLessThan(TimeSpan period, TimeSpan period2) {
			if (period2 == Timeout.InfiniteTimeSpan)
				return true;
			if (period == Timeout.InfiniteTimeSpan)
				return false;
			return period.TotalMilliseconds < period2.TotalMilliseconds;
		}

		private TimeSpan EnsureMaximumTimeSpan(TimeSpan timeSpan, TimeSpan maximum) {
			if (timeSpan == Timeout.InfiniteTimeSpan)
				return timeSpan;
			if (timeSpan.TotalMilliseconds < 0)
				return TimeSpan.FromMilliseconds(1);
			if (timeSpan > maximum)
				return maximum;
			return timeSpan;
		}

		public bool ChangePeriod(TimeSpan period) {
			lock (_changeLock) {
				period = EnsureMaximumTimeSpan(period, TimeSpan.FromDays(1));
				if (IsLessThan(period, _period) || _canSetBiggerPeriod) {
					if (period != Timeout.InfiniteTimeSpan && period.TotalMilliseconds < 0)
						period = TimeSpan.FromMilliseconds(1000);
					_period = period;
					_canSetBiggerPeriod = false;
					return true;
				}
				return false;
			}
		}

		public void ChangeDueTime(TimeSpan dueTime) {
			lock (_changeLock) {
				_dueTime = EnsureMaximumTimeSpan(dueTime, TimeSpan.FromDays(1));
			}
		}

		public async ValueTask DisposeAsync() {
			await StopAsync();
		}
	}
}
