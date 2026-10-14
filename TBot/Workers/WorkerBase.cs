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

			// TimeSpan periodSpan = TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
			// ThreadName cannot be longer than 16 bytes, so
			string cutAlias = (_tbotInstance.InstanceAlias.Length > 6 ? _tbotInstance.InstanceAlias.Substring(0, 6) : _tbotInstance.InstanceAlias);
			_timer = new AsyncTimer(ExecutionWrapper, $"{cutAlias}{GetWorkerName()}");
			await _timer.StartAsync(ct, period, dueTime);
		}
		public async Task StartWorker(CancellationToken ct, TimeSpan dueTime) {
			await StartWorker(ct, Timeout.InfiniteTimeSpan, dueTime);
		}
		public async Task StopWorker() {
			// Stop also all the timers
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

		// PATCH (Execute-Timeout-Absicherung, 17.09.2026): Default fuer alle Worker, die keine
		// eigenen legitimen Langzeit-Wartezeiten in Execute() haben (siehe ExecutionWrapper). Ein
		// Worker mit einer solchen Wartezeit (z.B. AutoFarm) ueberschreibt das mit einem groesseren
		// Wert oder Timeout.InfiniteTimeSpan.
		protected virtual TimeSpan ExecuteTimeout => TimeSpan.FromSeconds(45);

		// PATCH (Heartbeat-Haenger-Erkennung, 22.09.2026): siehe Kommentar bei Heartbeat()/
		// LastHeartbeatUtc unten und in der ExecutionWrapper()-Abandon-Verzweigung fuer den
		// Hintergrund. Ersetzt die vorherige starre "Hard-Ceiling" (ExecuteTimeout + 5 Minuten
		// seit Zyklusstart), die bei Workern mit legitim sehr langen, aber aktiv fortschreitenden
		// Zyklen (z.B. AutoFarm vor PATCH 88/89 mit bis zu 3h legitimer Wartezeit) zwangslaeufig
		// entweder zu frueh (Duplikat-Aktionen durch parallele Zyklen) oder viel zu spaet (Silent-
		// Death durch einen kuenstlich riesigen Timeout) ausloeste - eine feste Zeit seit
		// Zyklusstart kann "macht noch Fortschritt" und "haengt wirklich fest" nicht unterscheiden,
		// wenn beide Faelle aehnlich lange dauern koennen. Misst stattdessen die Zeit seit dem
		// LETZTEN Heartbeat() - ein Worker mit legitim langen Wartezeiten meldet sich waehrenddessen
		// periodisch (z.B. vor/nach jedem teuren externen Aufruf), ein wirklich haengender Aufruf
		// (z.B. ein verklemmter ogamed-Call, der das CancellationToken nicht auswertet) tut das
		// nicht - und wird so unabhaengig von der legitimen Gesamtdauer zuverlaessig und deutlich
		// schneller erkannt. Ein Worker ohne eigene Heartbeat()-Aufrufe verhaelt sich effektiv wie
		// vorher (Zeit seit Zyklusstart), da der Baseline-Heartbeat beim Start von Execute() gesetzt
		// wird und danach nicht mehr aktualisiert wird.
		protected virtual TimeSpan HangDetectionTimeout => TimeSpan.FromMinutes(10);

		private long _lastHeartbeatTicks = DateTime.UtcNow.Ticks;

		// Von Execute() (auch aus lokalen Funktionen/Lambdas heraus, die als Instanzmethode gelten)
		// periodisch waehrend legitimer Langzeit-Operationen aufzurufen - z.B. vor/nach jedem
		// teuren externen Aufruf (SimulateBattle) oder vor/nach einer kontrollierten Wartezeit.
		// Threadsicher (Interlocked), da ein abgebrochener ("abandoned") Zyklus im Hintergrund
		// weiterlaeuft, waehrend die Ueberwachung dafuer in einem separaten Task passiert.
		protected void Heartbeat() {
			Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
		}

		private DateTime LastHeartbeatUtc => new DateTime(Interlocked.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);

		protected Task EndExecution() {
			// This is meant to be called within the worker callback, so we can't await _timer to end
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

			// PATCH (Execute-Timeout-Absicherung, 17.09.2026): steuert, ob der finally-Block unten
			// ReleaseWorker() aufruft. Siehe Kommentar bei der Abandon-Verzweigung weiter unten -
			// im Timeout-Fall wird die Freigabe bewusst NICHT hier ausgeloest, sondern an das
			// tatsaechliche Ende des haengenden Execute()-Aufrufs gehaengt.
			bool releaseNow = true;
			try {
				await WaitWorker();

				ct.ThrowIfCancellationRequested();

				// Neuer Zyklus beginnt - Heartbeat-Baseline setzen (siehe Heartbeat()/
				// HangDetectionTimeout oben), damit ein Worker OHNE eigene Heartbeat()-Aufrufe sich
				// weiterhin wie vor diesem Patch verhaelt (Haenger-Erkennung ab Zyklusstart).
				Heartbeat();

				// Execute() lief bisher komplett unbegrenzt - haengt ein einzelner Zyklus (z.B. ein
				// HTTP-Call an ogamed, der das CancellationToken nicht auswertet, siehe
				// AsyncTimer.StopAsync()-Fix vom selben Tag), blockiert das nicht nur den Stop/Restart,
				// sondern auch den naechsten regulaeren Zyklus dieses Workers auf unbestimmte Zeit - im
				// Normalbetrieb, nicht nur beim Shutdown. Reine Absicherung: laeuft Execute() laenger als
				// executeTimeout, wird hier nicht mehr gewartet und die Wrapper-Runde beendet; der
				// haengende Task laeuft im Hintergrund aus. Verhindert keine Fehlfunktion des haengenden
				// Zyklus selbst, nur dass der Worker dadurch dauerhaft stillsteht. WICHTIG: manche Worker
				// (z.B. AutoFarm, das synchron auf die Ankunft einer Truemmerfeld-Trigger-Sonde wartet,
				// teils deutlich laenger als 45s) haben legitime, lange Wartezeiten INNERHALB von
				// Execute() - fuer die ist ExecuteTimeout ueberschrieben (siehe AutoFarmWorker), damit
				// dieser generische Schutz sie nicht abwuergt. Manche Worker (z.B. Expeditions, das in
				// einem Zyklus mehrere Flotten nacheinander mit Pausen dazwischen verschickt) koennen
				// auch mit Default-Timeout legitim laenger als 45s brauchen, ohne dass etwas haengt -
				// dafuer ist WICHTIG, dass ReleaseWorker() in diesem Fall NICHT sofort feuert (siehe
				// unten), sonst koennte ein neuer Zyklus parallel zum noch laufenden alten starten und
				// z.B. doppelt Flotten verschicken (beobachtet 17.09.2026 bei Expeditions).
				var executeTimeout = ExecuteTimeout;
				var executeTask = Execute();
				var completedTask = await Task.WhenAny(executeTask, Task.Delay(executeTimeout, ct));
				if (completedTask != executeTask) {
					DoLog(LogLevel.Warning, $"{GetWorkerName()}: Execute() did not complete within {executeTimeout.TotalSeconds}s - abandoning this cycle, worker continues on next scheduled run.");
					// Semaphore bewusst NICHT jetzt freigeben - der naechste Zyklus wuerde sonst parallel
					// zum noch laufenden "abandonten" Execute() starten koennen. Freigabe erfolgt erst,
					// wenn executeTask tatsaechlich endet; ein etwaiger naechster Zyklus wartet dann in
					// WaitWorker() darauf, respektiert dabei aber weiterhin das CancellationToken (siehe
					// WaitWorker()-Implementierung oben) - beim Shutdown blockiert das also nicht.
					releaseNow = false;
					// PATCH (Heartbeat-Haenger-Erkennung, 22.09.2026 - ersetzt die vorherige feste
					// "Hard-Ceiling" vom 20.09.2026): die alte Variante gab die Semaphore spaetestens
					// ExecuteTimeout+5min NACH ZYKLUSSTART zwangsweise frei, egal ob der abgebrochene
					// Zyklus noch aktiv Fortschritt machte oder wirklich haengte - bei Workern mit
					// legitim sehr langen Zyklen (beobachtet: AutoFarm vor PATCH 88/89, teils mehrere
					// Stunden durch synchrones Warten auf Sondenflug/Eskorten-Timeouts) fuehrte das
					// dazu, dass ein NEUER Zyklus parallel zum noch echt laufenden alten startete -
					// beide mit ungeschuetztem Zugriff auf denselben State (beobachtet: 3+ gleichzeitige
					// Angriffsflotten auf dasselbe AutoFarm-Ziel). Jetzt wird stattdessen periodisch
					// geprueft, ob der abgebrochene Zyklus noch ueber Heartbeat() Lebenszeichen sendet -
					// nur wirklicher STILLSTAND (kein Heartbeat mehr fuer HangDetectionTimeout) fuehrt
					// zur Zwangsfreigabe, unabhaengig davon wie lange der Zyklus insgesamt schon laeuft.
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
				// OK
			} catch (Exception ex) {
				// PATCH (Silent-Death-Fix, 17.09.2026): VORHER wurde hier nur OperationCanceledException
				// gefangen. Jede andere Exception (z.B. eine unerwartete Antwort/Fehlerform von ogamed, die
				// Execute() nicht selbst abfaengt) lief ungefangen durch ExecutionWrapper() UND durch die
				// while-Schleife in AsyncTimer.StartAsync() (die ebenfalls nur OperationCanceledException
				// faengt) durch - dort stirbt der komplette Task.Run() endgueltig, IsRunning wird false, und
				// der Worker gibt fuer immer keinen Log-Output mehr aus. Kein Crash, kein sichtbarer Fehler -
				// nur Stille, weil niemand diese Task-Exception je beobachtet (.NET loggt unbeobachtete
				// Task-Exceptions standardmaessig nirgends). Beobachtet 17.09.2026: Expeditions-Worker nach
				// 11:26 Uhr komplett stumm, journalctl-Fenster um den letzten Log-Eintrag leer (kein
				// "exception"/"error"/"unhandled" - konsistent mit genau diesem stillen Tod statt einem
				// geloggten Fehler). Fix: Exception hier loggen statt die Schleife sterben zu lassen - der
				// Worker macht beim naechsten regulaeren Zyklus normal weiter.
				DoLog(LogLevel.Error, $"{GetWorkerName()}: unexpected exception in ExecutionWrapper, worker keeps running in the next cycle: {ex}");
			} finally {
				// releaseNow=false nur im Timeout-Abandon-Zweig oben (dort haengt die Freigabe stattdessen
				// am tatsaechlichen Ende von executeTask, siehe dort) - in jedem anderen Fall (normaler
				// Durchlauf, OperationCanceledException, sonstige Exception) wird hier wie bisher freigegeben.
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