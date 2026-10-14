using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Tbot.Common.Extensions;
using Tbot.Common.Settings;
using Tbot.Exceptions;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Workers;
using Tbot.Workers.Brain;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Exceptions;
using TBot.Ogame.Infrastructure.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static System.Reflection.Metadata.BlobBuilder;

namespace Tbot.Services {
	public class TBotMain : IEquatable<TBotMain>, IAsyncDisposable, ITBotMain {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ILoggerService<TBotMain> _logger;
		private readonly ICalculationService _helpersService;
		private readonly IWorkerFactory _workerFactory;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private readonly SemaphoreSlim _settingsReloadSemaphore = new SemaphoreSlim(1, 1);

		private ITelegramMessenger telegramMessenger;
		// global Discord webhook
		private IDiscordWebhookMessenger discordWebhookMessenger;

		private bool loggedIn = false;
		private Dictionary<string, Timer> timers = new();
		private ConcurrentDictionary<Feature, ITBotWorker> workers = new();
		private CancellationTokenSource cts = new();


		public UserData userData = new();
		public TelegramUserData telegramUserData = new();

		public DateTime startTime = DateTime.UtcNow;
		public SettingsFileWatcher settingsWatcher = null;

		public event EventHandler OnError;

		public dynamic InstanceSettings { get; private set; }
		private string InstanceSettingsPath { get; set; }
		public string InstanceAlias { get; private set; }
		public bool SleepWaveActive { get; set; }
		public UserData UserData {
			set {
				userData = value;
			}
			get {
				return userData;
			}
		}
		public TelegramUserData TelegramUserData {
			set {
				telegramUserData = value;
			}
			get {
				return telegramUserData;
			}
		}
		public IOgameService OgamedInstance {
			get {
				return _ogameService;
			}
		}
		public ICalculationService HelperService {
			get {
				return _helpersService;
			}
		}
		public IFleetScheduler FleetScheduler {
			get {
				return _fleetScheduler;
			}
		}
		public IWorkerFactory WorkerFactory {
			get {
				return _workerFactory;
			}
		}

		public long SleepDuration { get; set; }
		public DateTime NextWakeUpTime { get; set; }

		public TBotMain(
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService helpersService,
			IWorkerFactory workerFactory,
			ILoggerService<TBotMain> logger) {

			_ogameService = ogameService;
			_logger = logger;
			_helpersService = helpersService;
			_fleetScheduler = fleetScheduler;
			_workerFactory = workerFactory;
			_tbotOgameBridge = new TBotOgamedBridge(this, _ogameService);

			// Avoid circular dependency
			_fleetScheduler.SetTBotInstance(this);
			_fleetScheduler.SetTBotOgameBridge(_tbotOgameBridge);

			_ogameService.OnError += _ogameService_OnError;
		}

		private void _ogameService_OnError(object sender, EventArgs e) {
			log(LogLevel.Information, LogSender.Tbot, "Ogamed encountered an error.");
			if (OnError != null) {
				OnError(this, EventArgs.Empty);
			}
		}

		private Credentials GetCredentialsFromSettings() {
			// Login-Mail und Universum sofort aus allen Logs heraushalten (Spielername folgt nach dem Login)
			TBot.Common.Logging.LogAnonymizer.Register((string) InstanceSettings.Credentials.Email, "[Email]");
			TBot.Common.Logging.LogAnonymizer.Register((string) InstanceSettings.Credentials.Universe, "[Universe]");
			return new() {
				Universe = ((string) InstanceSettings.Credentials.Universe).FirstCharToUpper(),
				Username = (string) InstanceSettings.Credentials.Email,
				Password = (string) InstanceSettings.Credentials.Password,
				Language = ((string) InstanceSettings.Credentials.Language).ToLower(),
				IsLobbyPioneers = (bool) InstanceSettings.Credentials.LobbyPioneers,
				BasicAuthUsername = (string) InstanceSettings.Credentials.BasicAuth.Username,
				BasicAuthPassword = (string) InstanceSettings.Credentials.BasicAuth.Password
			};
		}

		private Device GetDeviceFromSettings() {
			return new() {
				Name = ((string) InstanceSettings.Credentials.DeviceConf.Name),
				System = (string) InstanceSettings.Credentials.DeviceConf.System ?? "",
				Browser = (string) InstanceSettings.Credentials.DeviceConf.Browser ?? "",
				UserAgent = (string) InstanceSettings.Credentials.DeviceConf.UserAgent ?? "",
				Memory = (int) InstanceSettings.Credentials.DeviceConf.Memory,
				Concurrency = (int) InstanceSettings.Credentials.DeviceConf.Concurrency,
				Color = (int) InstanceSettings.Credentials.DeviceConf.Color,
				Width = (int) InstanceSettings.Credentials.DeviceConf.Width,
				Height = (int) InstanceSettings.Credentials.DeviceConf.Height,
				Timezone = (string) InstanceSettings.Credentials.DeviceConf.Timezone ?? "",
				Lang = (string) InstanceSettings.Credentials.DeviceConf.Lang ?? ""
			};
		}


		private async Task InitializeOgame() {
			string host = (string) InstanceSettings.General.Host ?? "localhost";
			string port = (string) InstanceSettings.General.Port ?? "8080";
			if (!_ogameService.IsPortAvailable(host, int.Parse(port))) {
				throw new Exception("Port " + port + " is not available");
			}

			string captchaKey = (string) InstanceSettings.General.CaptchaAPIKey ?? "";
			ProxySettings proxy = new();
			string cookiesPath = "cookies" + (string) InstanceSettings.Credentials.Email + ".txt";

			if ((bool) InstanceSettings.General.Proxy.Enabled && (string) InstanceSettings.General.Proxy.Address != "") {
				log(LogLevel.Information, LogSender.Tbot, "Initializing proxy");
				string proxyType = ((string) InstanceSettings.General.Proxy.Type).ToLower();
				string proxyAddress = (string) InstanceSettings.General.Proxy.Address;
				if (proxyType == "https") {
					proxyType = "http";
				}
				if (proxyType == "http" && proxyAddress.Contains(':') && !proxyAddress.StartsWith("http://")) {
					proxyAddress = "http://" + proxyAddress;
				}
				if (proxyType == "socks5" || proxyType == "http") {
					proxy.Enabled = (bool) InstanceSettings.General.Proxy.Enabled;
					proxy.Address = proxyAddress;
					proxy.Type = proxyType;
					proxy.Username = (string) InstanceSettings.General.Proxy.Username ?? "";
					proxy.Password = (string) InstanceSettings.General.Proxy.Password ?? "";
				} else {
					log(LogLevel.Warning, LogSender.Tbot, "Unable to initialize proxy: unsupported proxy type");
					log(LogLevel.Warning, LogSender.Tbot, "Press enter to continue");
				}
			}


			_ogameService.Initialize(GetCredentialsFromSettings(), GetDeviceFromSettings(), proxy, (string) host, int.Parse(port), (string) captchaKey);
			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
		}

		private async Task ResolveCaptcha() {

			var captchaChallenge = await _ogameService.GetCaptchaChallenge();
			if (captchaChallenge.Id.Length == 0) {
				log(LogLevel.Warning, LogSender.Tbot, "No captcha found. Unable to login.");
				log(LogLevel.Warning, LogSender.Tbot, "Please check your credentials, language and universe name.");
				log(LogLevel.Warning, LogSender.Tbot, "If your credentials are correct try refreshing your IP address.");
				log(LogLevel.Warning, LogSender.Tbot, "If you are using a proxy, a VPN or hosting TBot on a VPS, be warned that Ogame blocks datacenters' IPs. You probably need a residential proxy.");
			} else {
				log(LogLevel.Information, LogSender.Tbot, "Trying to solve captcha...");
				int answer = 0;
				if (captchaChallenge.Icons != "" && captchaChallenge.Question != "" && captchaChallenge.Icons != null && captchaChallenge.Question != null) {
					answer = OgameCaptchaSolver.GetCapcthaSolution(captchaChallenge.Icons, captchaChallenge.Question);
				}
				await _ogameService.SolveCaptcha(captchaChallenge.Id, answer);
				await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				await _ogameService.Login();
			}
		}

		private async Task InitUserData() {
			userData.serverInfo = await _tbotOgameBridge.UpdateServerInfo();
			userData.serverData = await _tbotOgameBridge.UpdateServerData();
			userData.userInfo = await _tbotOgameBridge.UpdateUserInfo();
			userData.staff = await _tbotOgameBridge.UpdateStaff();
			userData.allianceClass = await _tbotOgameBridge.UpdateAllianceClass();

			var serverTime = await _tbotOgameBridge.GetDateTime();

			log(LogLevel.Information, LogSender.Tbot, $"Server time: {serverTime.ToString()}");
			TBot.Common.Logging.LogAnonymizer.Register(userData.userInfo.PlayerName, "[Player]");
			log(LogLevel.Information, LogSender.Tbot, $"Player name: {userData.userInfo.PlayerName}");
			log(LogLevel.Information, LogSender.Tbot, $"Player class: {userData.userInfo.Class.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Alliance class: {userData.allianceClass.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player rank: {userData.userInfo.Rank}");
			log(LogLevel.Information, LogSender.Tbot, $"Player points: {userData.userInfo.Points}");
			log(LogLevel.Information, LogSender.Tbot, $"Player honour points: {userData.userInfo.HonourPoints}");
		}

		public async Task Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler) {

			InstanceSettingsPath = settingPath;
			InstanceAlias = alias;
			InstanceSettings = await SettingsService.GetSettings(settingPath);

			telegramMessenger = telegramHandler;
			try {
				await InitializeOgame();
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Tbot, $"Unable to start ogamed: {e.Message}");
				throw;
			}

			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));

			try {
				await _ogameService.Login();
			} catch (OgamedException oe) {
				log(LogLevel.Warning, LogSender.Tbot, $"Unable to login (\"{oe.Message}\"). Checking captcha...");
				await ResolveCaptcha();
			} catch (System.Net.Http.HttpRequestException) {
				try {
					await ResolveCaptcha();
				} catch (Exception ex) {
					log(LogLevel.Error, LogSender.Tbot, $"Unable to login after captcha. (\"{ex.Message}\")");
					throw new UnableToLoginException("Unable to login", ex);
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Tbot, $"Unable to login. (\"{e.Message}\")");
				throw new UnableToLoginException("Unable to login", e);
			}

			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));

			loggedIn = true;
			log(LogLevel.Information, LogSender.Tbot, "Logged in!");			

			await InitUserData();

			var isVacationMode = await _ogameService.IsVacationMode();

			if (isVacationMode) {
				log(LogLevel.Warning, LogSender.Tbot, "Account in vacation mode");
				loggedIn = false;
				await _ogameService.Logout();
				throw new AccountInVacationModeException("Account is in vacation mode");
			}

			if (telegramMessenger != null) {
				await telegramMessenger.AddTbotInstance(this, _tbotOgameBridge);
			}
			userData.lastDOIR = 0;
			userData.nextDOIR = 0;

			log(LogLevel.Information, LogSender.Tbot, "Initializing data...");
			userData.celestials = await _tbotOgameBridge.GetPlanets();
			userData.researches = await _tbotOgameBridge.UpdateResearches();
			// re-arm oder sofort ausloesen, was ein Neustart am
			// AutoFleetSave/Ghost-Recall-Timer verloren hat (siehe FleetScheduler.ReconcilePendingRecalls).
			await _fleetScheduler.ReconcilePendingRecalls();
			userData.scheduledFleets = new();
			userData.farmTargets = new();

			if (userData.celestials.Count == 1) {
				await EditSettings(userData.celestials.First());
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);
			}

			ConfigureDiscordWebhook();

			// SleepMode will know if features must be enabled or not
			InitializeSleepMode();

			// eigener, von SleepMode komplett unabhaengiger Zeitfenster-Scheduler
			//. Nutzt denselben Timer-in-Dictionary-Mechanismus wie
			// SleepMode, aber eigene Uhrzeiten (InstanceSettings.GhostSleep.GoToSleep/WakeUp) und eigenes
			// Verhalten (Blanket-Stop ausser Expeditions/Defender statt globalem isSleeping-Flag). Bleibt
			// technisch komplett getrennt vom manuellen Telegram-Befehl /ghost (eigener "GhostSleepTimer"
			// in FleetScheduler.cs) - beide Mechanismen laufen unabhaengig nebeneinander her.
			InitializeGhostSleepSchedule();

			// schreibt alle 30 s sleepstatus.json fuer die Kachel im Browser (ogamed /bot/sleepstatus)
			InitializeSleepStatusWriter();

			// Up and running. Lets initialize notification for settings file
			settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, InstanceSettingsPath,
				new[] { SettingsService.DefaultLoadDirectory + "/**/*.json", SettingsService.SecretsDirectory + "/**/*.json" });
		}

		public async ValueTask DisposeAsync() {
			log(LogLevel.Information, LogSender.Tbot, "Deinitializing instance...");

			if (settingsWatcher != null) {
				settingsWatcher.deinitWatch();
				settingsWatcher = null;
			}

			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing instance from telegram...");
				await telegramMessenger.RemoveTBotInstance(this);
			}
			discordWebhookMessenger = null;

			// Deinitialize workers
			List<Task> deInitTasks = new();
			foreach (var worker in workers) {
				log(LogLevel.Information, LogSender.Tbot, $"Deinitializing worker {worker.Value.GetWorkerName()}");

				deInitTasks.Add(worker.Value.StopWorker());
			}
			await Task.WhenAll(deInitTasks);
			deInitTasks.Clear();

			log(LogLevel.Information, LogSender.Tbot, "Deinitializing timers...");
			foreach (var entry in timers) {
				log(LogLevel.Information, LogSender.Tbot, $"Disposing timer \"{entry.Key}\"");
				entry.Value.Dispose();
			}
			timers.Clear();

			log(LogLevel.Information, LogSender.Tbot, "Deinitializing ogamed...");
			if (_ogameService != null) {
				if (loggedIn) {
					loggedIn = false;
					log(LogLevel.Information, LogSender.Tbot, "Logging out");
					await _ogameService.Logout();
				}

				log(LogLevel.Information, LogSender.Tbot, "Killing ogamed instance");
				_ogameService.KillOgamedExecutable();
			}

			log(LogLevel.Information, LogSender.Tbot, "Deinitialization completed");
		}

		public bool Equals(TBotMain other) {
			if (other == null)
				return false;
			return object.ReferenceEquals(this, other);
		}

		public override string ToString() {
			if (loggedIn && (userData.userInfo != null) && (userData.serverData != null))
				return $"{userData.userInfo.PlayerName}@{userData.serverData.Name}";
			else
				return $"{InstanceAlias}";
		}

		public void log(LogLevel logLevel, LogSender sender, string format) {
			_logger.WriteLog(logLevel, sender, $"[{ToString()}] {format}");
		}

		private async void InitializeFeatures() {
			foreach(var feat in Features.AllFeatures) {
				await InitializeFeature(feat);
			}
		}

		public async Task InitializeFeature(Feature feat) {
			if (feat == Feature.SleepMode) {
				return;// always enabled
			}
			long dueTime = feat switch {
				Feature.Defender => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainAutobuildCargo => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.BrainAutobuildDefence => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.BrainAutoRepatriate => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.BrainAutoMine => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainLifeformAutoMine => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainLifeformAutoResearch => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainOfferOfTheDay => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.BrainAutoResearch => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoFarm => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.Expeditions => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Harvest => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Colonize => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoDiscovery => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				_ => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds)
			};

			// Its ok to add an infinite period time, since each worker will change its period accordingly after first execution
			if (workers.TryGetValue(feat, out var worker)) {
				worker.RestartWorker(cts.Token, Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(dueTime));
			} else {
				ITBotWorker newWorker = _workerFactory.InitializeWorker(feat, this, _tbotOgameBridge);
				if (newWorker != null) {
					workers.TryAdd(feat, newWorker);
					await newWorker.StartWorker(cts.Token, TimeSpan.FromMilliseconds(dueTime));
				} else {
					log(LogLevel.Warning, LogSender.Tbot, $"Cannot start worker for {feat.ToString()}");
				}
			}
		}

		public async Task StopFeature(Feature feat) {
			if (workers.TryGetValue(feat, out var worker)) {
				await worker.StopWorker();
			}
		}

		public bool IsFeatureRunning(Feature feat) {
			if (workers.TryGetValue(feat, out var worker)) {
				return worker.IsWorkerRunning();
			} else if (feat == Feature.SleepMode) {
				return true;    // Always running
			} else {
				return false;
			}
		}

		public async Task<bool> EditSettings(Celestial celestial = null, Feature feature = Feature.Null, string recall = "", int cargo = 0) {
			await Task.Delay(500, cts.Token);
			// Werte werden dorthin geschrieben, wo sie stehen (Hauptdatei oder per Load eingebundene Datei in conf.d/)
			var changes = new List<KeyValuePair<string, object>>();
			void Set(string path, object value) => changes.Add(new KeyValuePair<string, object>(path, value));

			if (recall != "") {
				Set("SleepMode.AutoFleetSave.Recall", Convert.ToBoolean(recall));
			}

			if (cargo > 0) {
				Set("Expeditions.MinPrimaryToSend", (int) cargo);
			}

			if (celestial != null) {
				string type = "";
				if (celestial.Coordinate.Type == Celestials.Moon)
					type = "Moon" ?? "Planet";
				else
					type = "Planet";

				void SetCoord(string prefix, string typeValue) {
					Set(prefix + ".Galaxy", (int) celestial.Coordinate.Galaxy);
					Set(prefix + ".System", (int) celestial.Coordinate.System);
					Set(prefix + ".Position", (int) celestial.Coordinate.Position);
					Set(prefix + ".Type", typeValue);
				}

				if (feature == Feature.BrainAutoMine || feature == Feature.Null) {
					SetCoord("Brain.Transports.Origin", type);
				}

				if (feature == Feature.BrainAutoResearch || feature == Feature.Null) {
					SetCoord("Brain.AutoResearch.Target", "Planet");
					SetCoord("Brain.Transports.Origin", type);
				}

				if (feature == Feature.BrainAutoRepatriate || feature == Feature.Null) {
					SetCoord("Brain.AutoRepatriate.Target[0]", type);
				}

				if (feature == Feature.Expeditions || feature == Feature.Null) {
					SetCoord("Expeditions.Origin[0]", type);
				}

				if (feature == Feature.AutoDiscovery || feature == Feature.Null) {
					SetCoord("AutoDiscovery.Origin[0]", type);
				}

				if (feature == Feature.Colonize || feature == Feature.Null) {
					SetCoord("AutoColonize.Origin", "Planet");
				}
			}

			await SettingsService.SetValuesAsync(InstanceSettingsPath, changes);

			return true;
		}

		// Telegram-Befehle haben Vorrang: kurz auf laufende Worker-Zyklen warten (Standard 20 s), danach laeuft der Befehl
		// trotzdem. Gibt nur die Semaphoren zurueck, die wirklich erhalten wurden - nur diese werden spaeter freigegeben.
		public async Task<List<SemaphoreSlim>> WaitFeature(TimeSpan? timeout = null) {
			TimeSpan wait = timeout ?? TimeSpan.FromSeconds(20);
			var semaphores = new List<SemaphoreSlim>();
			foreach (var feat in Features.AllFeatures) {
				ITBotWorker worker = _workerFactory.GetWorker(feat);
				if (worker != null) {
					var semaphore = worker.GetSemaphore();
					if (!semaphores.Contains(semaphore)) // Brain-Worker teilen sich eine Semaphore
						semaphores.Add(semaphore);
				}
			}
			var results = await Task.WhenAll(semaphores.Select(async sem => (sem, got: await sem.WaitAsync(wait))));
			var held = results.Where(r => r.got).Select(r => r.sem).ToList();
			if (held.Count < semaphores.Count)
				log(LogLevel.Warning, LogSender.Telegram, $"Telegram command runs without waiting for {semaphores.Count - held.Count} busy worker group(s) (timeout {wait.TotalSeconds:0}s).");
			return held;
		}

		public void releaseFeature(List<SemaphoreSlim> held) {
			if (held == null) return;
			foreach (var sem in held) {
				try { sem.Release(); } catch (SemaphoreFullException) { }
			}
		}
		private DateTime _lastReloadFinished = DateTime.MinValue;
		private async void OnSettingsChanged() {
			// eigene Schreibvorgaenge der Kachel (Speicherwert bereits gesetzt) loesen keinen Reload aus
			if (DateTime.UtcNow < _suppressReloadUntilUtc) {
				log(LogLevel.Debug, LogSender.Tbot, "Settings reload suppressed (change came from the slot tile).");
				return;
			}
			DateTime onSettingsLaunched = DateTime.Now;
			await _settingsReloadSemaphore.WaitAsync();
			try {
				if (_lastReloadFinished > onSettingsLaunched) {
					return;
				}
				// Erst pruefen, ob die neue Config (inkl. Load-Dateien) lesbar ist; sonst laeuft der Bot mit den alten Settings weiter
				dynamic newSettings;
				try {
					newSettings = await SettingsService.GetSettings(InstanceSettingsPath);
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.Tbot, $"Settings reload aborted, keeping the running settings: {e.Message}");
					return;
				}
				log(LogLevel.Information, LogSender.Tbot, "Settings file change detected! Waiting workers to complete ongoing activities...");

				// Wait on feature to be shut down
				cts.Cancel();
				foreach (var worker in workers) {
					log(LogLevel.Information, LogSender.Tbot, $"Stopping feature {worker.Key.ToString()}...");
					await worker.Value.StopWorker();
					log(LogLevel.Information, LogSender.Tbot, $"Feature {worker.Key.ToString()} stopped for settings reload!");
				}

				log(LogLevel.Information, LogSender.Tbot, "Reloading Settings file");
				InstanceSettings = newSettings;
				ConfigureDiscordWebhook();

				cts = new();
				// If wakeUp, then all features will be restored
				await HandleSleepModeAsync(null);
				_lastReloadFinished = DateTime.Now;
			}
			finally {
				_settingsReloadSemaphore.Release();
			}
			
		}
		public async Task ListProfiles() {
			string profilesDir = Path.Combine(Path.GetDirectoryName(InstanceSettingsPath), "profiles");
			string profileList = "Available profiles:\n";
			if (Directory.Exists(profilesDir)) {
				var profileFiles = Directory.GetFiles(profilesDir, "*.json");
				log(LogLevel.Information, LogSender.Tbot, "Available profiles:");
				foreach (var profileFile in profileFiles) {
					profileList += $"- {Path.GetFileNameWithoutExtension(profileFile)}\n";
					log(LogLevel.Information, LogSender.Tbot, $"- {Path.GetFileNameWithoutExtension(profileFile)}");
				}
			} else {
				log(LogLevel.Warning, LogSender.Tbot, "Profiles directory not found.");
				profileList = "No profiles found.";
			}
			await SendTelegramMessage(profileList);
		}
		public async Task ListRunningProfiles() {
			List<string> profiles = userData.runningProfiles;
			if (profiles.Count <= 0 || profiles == null) {
				await SendTelegramMessage("No profile is currently running.");
				return;
			}
			string txt = profiles.Count > 1 ? "Profiles currently running:\n" : "Profile currently running:\n";
			foreach (var profile in profiles) {
				txt += $"- {profile}\n";
			}
			await SendTelegramMessage(txt);
		}
		public async Task LoadProfile(List<string> profileName) {
			for (int i = 0; i < profileName.Count; i++)
				profileName[i] = profileName[i].Trim();

			log(LogLevel.Information, LogSender.Tbot, "Loading profile detected !");
			await _settingsReloadSemaphore.WaitAsync();

			try {
				List<string> profilePath = new();
				for (int i = 0; i < profileName.Count; i++) {
					string pPath = Path.Combine(Path.GetDirectoryName(InstanceSettingsPath), "profiles", profileName[i] + ".json");
					profilePath.Add(pPath);
					if (!File.Exists(pPath)) {
						log(LogLevel.Warning, LogSender.Tbot, $"Profile {profileName[i]} not found in the profile folder, loading abandoned.");
						await SendTelegramMessage($"Profile {profileName[i]} not found in the profile folder, loading abandoned.");
						return;
					}
				}
				string txt = " "+ profileName.First();
				foreach (var p in profileName.Skip(1)) {
					txt += " + "+ p;
				}
				if (profilePath.Count > 1)
					log(LogLevel.Information, LogSender.Tbot, $"Loading profiles{txt}. Waiting workers to complete ongoing activities...");
				else
					log(LogLevel.Information, LogSender.Tbot, $"Loading profile{txt}. Waiting workers to complete ongoing activities...");
				
				cts.Cancel();
				foreach (var worker in workers) {
					log(LogLevel.Information, LogSender.Tbot, $"Stopping feature {worker.Key.ToString()}...");
					await worker.Value.StopWorker();
					log(LogLevel.Information, LogSender.Tbot, $"Feature {worker.Key.ToString()} stopped for settings reload!");
				}

				InstanceSettings = await SettingsService.GetMergedSettings(InstanceSettingsPath, profilePath);

				if (profilePath.Count > 1) {
					log(LogLevel.Information, LogSender.Tbot, $"Profiles{txt} loaded successfully.");
					await SendTelegramMessage($"Profiles{txt} loaded successfully.");
				} else {
					log(LogLevel.Information, LogSender.Tbot, $"Profile{txt} loaded successfully.");
					await SendTelegramMessage($"Profile{txt} loaded successfully.");
				}
				userData.runningProfiles = profileName;

				_lastReloadFinished = DateTime.Now;
			} catch (Newtonsoft.Json.JsonException jsonEx) {
				log(LogLevel.Error, LogSender.Tbot, $"Invalid JSON Format: {jsonEx.Message}");
				await SendTelegramMessage($"Invalid JSON Format: {jsonEx.Message}");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				await SendTelegramMessage($"Error loading profile: refer to logs.");
			} finally {
				if (cts.IsCancellationRequested) {
					cts = new();
					// If wakeUp, then all features will be restored
					await HandleSleepModeAsync(null);
				}
				_settingsReloadSemaphore.Release();
			}
		}
		public async Task ResetProfile() {

			log(LogLevel.Information, LogSender.Tbot, "Unloading profile detected! Waiting workers to complete ongoing activities...");
			await _settingsReloadSemaphore.WaitAsync();

			try {
				// Wait on feature to be shut down
				cts.Cancel();
				foreach (var worker in workers) {
					log(LogLevel.Information, LogSender.Tbot, $"Stopping feature {worker.Key.ToString()}...");
					await worker.Value.StopWorker();
					log(LogLevel.Information, LogSender.Tbot, $"Feature {worker.Key.ToString()} stopped for settings reload!");
				}

				log(LogLevel.Information, LogSender.Tbot, "Loading origin settings file");
				await SendTelegramMessage($"Loading origin settings file");
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);
				userData.runningProfiles = new List<string>();

				_lastReloadFinished = DateTime.Now;
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				await SendTelegramMessage($"Error loading profile: refer to logs.");
			} finally {
				if (cts.IsCancellationRequested) {
					cts = new();
					// If wakeUp, then all features will be restored
					await HandleSleepModeAsync(null);
				}
				_settingsReloadSemaphore.Release();
			}
		}

		private void InitializeSleepMode() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing sleep mode...");
			StopSleepMode(false);
			timers.Add("SleepModeTimer", new Timer(HandleSleepMode, null, 0, Timeout.Infinite));
		}

		private void StopSleepMode(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping sleep mode...");
			if (timers.TryGetValue("SleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("SleepModeTimer");
		}

		// ===================== GhostSleep (Plan ) =====================
		// Geplantes, zeitgesteuertes Aequivalent zum manuellen "/ghost Xh"-Telegram-Befehl: feste
		// GoToSleep/WakeUp-Uhrzeiten statt Dauer. BEWUSST komplett getrennt vom manuellen /ghost
		// (eigener "GhostSleepTimer" in FleetScheduler.cs) - kein gemeinsamer Code, keine gemeinsamen
		// Timer/Flags, laufen unabhaengig nebeneinander. GhostSleep setzt NIE das globale
		// userData.isSleeping (das wuerde ExpeditionsWorker.Execute()'s finally-Block lahmlegen, siehe
		// Plan-Dokument), sondern stoppt gezielt alle Features ausser Expeditions/Defender/SleepMode.
		private bool _ghostSleeping = false;
		private bool? _ghostSleepPrevWaitForAllExpeditions = null;
		private List<Feature> _ghostSleepStoppedFeatures = new();
		private bool _ghostSleepEscalatedActive = false;

		// kurz VOR dem Einschlafen sollen alle Expeditions-Slots
		// gefuellt sein, bevor der Fleetsave die uebrigen Schiffe vom Mond mitnimmt (Expeditions-Schiffe
		// in der Luft bleiben davon unberuehrt und kehren spaeter selbst zurueck). In der Vorphase
		// (GhostSleep.PreSleepExpeditionMinutes vor GoToSleep, Standard 30, 0 = aus) werden
		// WaitForAllExpeditions und WaitForMajorityOfExpeditions temporaer auf false gesetzt, damit der
		// ExpeditionsWorker alle freien Slots sofort fuellt. Beim Einschlafen werden die Originalwerte
		// wiederhergestellt (danach setzt Schritt 3 WaitForAll wie gehabt auf true). Gilt NUR fuer
		// GhostSleep, nicht fuer den normalen SleepMode.
		private bool _preSleepExpoActive = false;
		private bool? _preSleepPrevWaitForAll = null;
		private bool? _preSleepPrevWaitForMajority = null;

		private int GhostSleepPreSleepMinutes() {
			try {
				return Math.Max(0, (int) InstanceSettings.GhostSleep.PreSleepExpeditionMinutes);
			} catch {
				return 30; // Key fehlt (aeltere Config) -> Standard
			}
		}

		// Stoesst den ExpeditionsWorker an, sofort zu laufen (statt bis zu 5 Minuten bis zum naechsten Check zu warten).
		private void GhostSleepTriggerExpeditionsNow() {
			try {
				if (workers.TryGetValue(Feature.Expeditions, out var raw) && raw is ExpeditionsWorker expWorker)
					expWorker.ChangeWorkerPeriod(TimeSpan.FromSeconds(3));
				else
					log(LogLevel.Warning, LogSender.Tbot, "GhostSleep pre-phase: expeditions worker not found, cannot trigger it.");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep pre-phase: triggering the expeditions worker failed: {e.Message}");
			}
		}

		// Expeditions.SleepWave: {"Active": bool, "SpeedPercent": 10..100}. Returns the speed step (1-10) or 0 if the
		// feature is off / not configured.
		private int SleepWaveSpeedStep() {
			try {
				if (!SettingsService.IsSettingSet(InstanceSettings.Expeditions, "SleepWave"))
					return 0;
				dynamic wave = InstanceSettings.Expeditions.SleepWave;
				if (!SettingsService.IsSettingSet(wave, "Active") || !(bool) wave.Active)
					return 0;
				double percent = SettingsService.IsSettingSet(wave, "SpeedPercent") ? Convert.ToDouble(wave.SpeedPercent) : 100;
				return (int) Math.Max(1, Math.Min(10, Math.Round(percent / 10.0)));
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Expeditions.SleepWave could not be read, wave disabled: {e.Message}");
				return 0;
			}
		}

		// Expeditions.SleepWave.ReturnAt: optional target return time ("WakeUp", "WakeUp+15", "WakeUp-10", "HH:mm").
		// Set = the speed/target system is calculated per expedition, SpeedPercent is only the fallback.
		private string SleepWaveReturnAt() {
			try {
				if (!SettingsService.IsSettingSet(InstanceSettings.Expeditions, "SleepWave"))
					return null;
				dynamic wave = InstanceSettings.Expeditions.SleepWave;
				if (!SettingsService.IsSettingSet(wave, "ReturnAt"))
					return null;
				string v = Convert.ToString(wave.ReturnAt);
				return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
			} catch {
				return null;
			}
		}

		private ExpeditionsWorker GetExpeditionsWorker() {
			return workers.TryGetValue(Feature.Expeditions, out var raw) ? raw as ExpeditionsWorker : null;
		}

		// Normal SleepMode: one expedition wave with the configured speed right before the fleetsave.
		private async Task SleepWaveBeforeSleepAsync() {
			try {
				int step = SleepWaveSpeedStep();
				if (step <= 0)
					return;
				if (!(bool) InstanceSettings.Expeditions.Active)
					return;
				var worker = GetExpeditionsWorker();
				if (worker == null) {
					log(LogLevel.Warning, LogSender.SleepMode, "Sleep wave: expeditions worker not running, wave skipped.");
					return;
				}
				bool ok = await worker.RunSleepWaveAsync(step, TimeSpan.FromMinutes(2), SleepWaveReturnAt(), (string) InstanceSettings.SleepMode.WakeUp);
				log(LogLevel.Information, LogSender.SleepMode, ok ? $"Sleep wave sent at {step * 10}% speed." : "Sleep wave was not sent.");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"Sleep wave failed, going to sleep anyway: {e.Message}");
			}
		}

		private void GhostSleepPreSleepBegin() {
			try {
				try { _preSleepPrevWaitForAll = (bool) InstanceSettings.Expeditions.WaitForAllExpeditions; } catch { _preSleepPrevWaitForAll = null; }
				try { _preSleepPrevWaitForMajority = (bool) InstanceSettings.Expeditions.WaitForMajorityOfExpeditions; } catch { _preSleepPrevWaitForMajority = null; }
				if (_preSleepPrevWaitForAll.HasValue)
					InstanceSettings.Expeditions.WaitForAllExpeditions = false;
				if (_preSleepPrevWaitForMajority.HasValue)
					InstanceSettings.Expeditions.WaitForMajorityOfExpeditions = false;
				int waveStep = SleepWaveSpeedStep();
				if (waveStep > 0) {
					var waveWorker = GetExpeditionsWorker();
					waveWorker?.SetSleepWaveSpeed(waveStep);
					waveWorker?.SetSleepWaveReturn(SleepWaveReturnAt(), (string) InstanceSettings.GhostSleep.WakeUp);
					SleepWaveActive = true; // Bettzeit-Pruefung in SendFleet darf die Welle nicht blocken
				}
				_preSleepExpoActive = true;
				log(LogLevel.Information, LogSender.Tbot, "GhostSleep pre-phase: expeditions are fully refilled before falling asleep (WaitForAll/WaitForMajority temporarily off).");
				GhostSleepTriggerExpeditionsNow();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep pre-phase: start failed: {e.Message}");
			}
		}

		private void GhostSleepPreSleepRestore(string reason) {
			if (!_preSleepExpoActive)
				return;
			try {
				if (_preSleepPrevWaitForAll.HasValue)
					InstanceSettings.Expeditions.WaitForAllExpeditions = _preSleepPrevWaitForAll.Value;
				if (_preSleepPrevWaitForMajority.HasValue)
					InstanceSettings.Expeditions.WaitForMajorityOfExpeditions = _preSleepPrevWaitForMajority.Value;
				GetExpeditionsWorker()?.SetSleepWaveSpeed(0);
				GetExpeditionsWorker()?.SetSleepWaveReturn(null, null);
				SleepWaveActive = false;
				log(LogLevel.Information, LogSender.Tbot, $"GhostSleep pre-phase finished ({reason}): expedition settings restored.");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep pre-phase: restore failed: {e.Message}");
			}
			_preSleepPrevWaitForAll = null;
			_preSleepPrevWaitForMajority = null;
			_preSleepExpoActive = false;
		}

		// Wartet kurz vor dem Fleetsave (max. 3 Minuten), bis alle Expeditions-Slots belegt sind. Fehlen am Ende
		// welche (z.B. nicht genug Schiffe auf dem Mond), wird das gewarnt - der Fleetsave laeuft trotzdem.
		private async Task GhostSleepExpeditionTopUpAsync() {
			try {
				DateTime deadline = DateTime.UtcNow.AddMinutes(3);
				DateTime lastTrigger = DateTime.UtcNow;
				while (true) {
					var slots = await _tbotOgameBridge.UpdateSlots();
					if (slots.ExpFree <= 0) {
						log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: all {slots.ExpTotal} expeditions are in flight, fleetsave can start.");
						return;
					}
					if (DateTime.UtcNow >= deadline) {
						string msg = $"GhostSleep: only {slots.ExpInUse}/{slots.ExpTotal} expeditions in flight (probably too few ships on the moon) - fleetsave starts anyway.";
						log(LogLevel.Warning, LogSender.Tbot, msg);
						await SendTelegramMessage(msg);
						return;
					}
					if ((DateTime.UtcNow - lastTrigger).TotalSeconds > 45) {
						GhostSleepTriggerExpeditionsNow();
						lastTrigger = DateTime.UtcNow;
					}
					await Task.Delay(TimeSpan.FromSeconds(15), cts.Token);
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: refilling expeditions before the fleetsave failed, continuing anyway: {e.Message}");
			}
		}

		private static readonly Feature[] GhostSleepNeverStopFeatures = new[] {
			Feature.Expeditions, Feature.Defender, Feature.SleepMode
		};

		// der urspruengliche Ansatz (ein Timer, der sich per
		// Timer.Change() nach jedem Tick selbst auf das naechst berechnete Intervall umplant - identisch
		// zum bestehenden SleepModeTimer-Muster) hat sich als UNZUVERLAESSIG erwiesen: nach dem
		// allerersten Tick feuerte der Timer nie wieder, weder bei einem mehrstuendigen Intervall noch
		// (verifiziert 28.09.2026, Neustart 02:58:15, GoToSleep 3:00, immer noch nichts um 3:04) bei
		// einem auf 3 Minuten gedeckelten. Siehe volle Herleitung in
		// claude/ghost-recall-changeworkerduetime-bug.md - passt zum bereits bekannten
		// "ChangeWorkerDueTime"-Bug-Muster, hier aber erstmals bei einem rohen System.Threading.Timer
		// beobachtet, nicht nur beim WorkerBase/AsyncTimer-Wrapper. `Timer.Change()` selbst ist also
		// offenbar in dieser Umgebung nicht zuverlaessig genug, um sich darauf zu verlassen.
		//
		// Fix: der Timer wird EINMALIG mit einer FESTEN Periode erstellt (60s) und `Change()` wird
		// AB DA NIE WIEDER aufgerufen - es gibt schlicht nichts mehr umzuplanen. Jeder Tick bewertet nur
		// noch den aktuellen Zustand (ComputeShouldBeAsleep) und wechselt bei Bedarf den Zustand; ein
		// Uebergang wird dadurch im schlimmsten Fall bis zu 60s zu spaet erkannt, faellt aber nie mehr
		// komplett aus. Gleichzeitig deutlich einfacher als die alte 8-Fall-Fallunterscheidung.
		private const int GhostSleepScheduleTickMs = 60 * 1000;
		private readonly SemaphoreSlim _ghostSleepScheduleLock = new SemaphoreSlim(1, 1);

		private void InitializeGhostSleepSchedule() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing GhostSleep schedule...");
			StopGhostSleepSchedule(false);
			timers.Add("GhostSleepScheduleTimer", new Timer(HandleGhostSleepSchedule, null, 0, GhostSleepScheduleTickMs));
		}

		private void StopGhostSleepSchedule(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping GhostSleep schedule...");
			if (timers.TryGetValue("GhostSleepScheduleTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepScheduleTimer");
		}

		private async void HandleGhostSleepSchedule(object state) {
			await HandleGhostSleepScheduleAsync(state);
		}

		// Liefert true, wenn "time" (Server-Zeit) innerhalb des GhostSleep-Fensters liegt. Behandelt
		// Mitternachts-Wraparound (goToSleep > wakeUp, z.B. 22:00 -> 6:00) genauso wie den einfachen
		// Fall am selben Tag (goToSleep < wakeUp, z.B. 2:00 -> 10:05) - ersetzt die alte 8-Fall-
		// Fallunterscheidung aus HandleSleepModeAsync, die hier bewusst NICHT uebernommen wurde, weil sie
		// eigentlich nur fuer die (jetzt entfallene) Intervall-Berechnung noetig war, nicht fuer die
		// reine Ja/Nein-Frage "bin ich gerade im Schlaffenster".
		private static bool ComputeShouldBeAsleep(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			if (goToSleep < wakeUp) {
				return time >= goToSleep && time < wakeUp;
			} else {
				return time >= goToSleep || time < wakeUp;
			}
		}

		private async Task HandleGhostSleepScheduleAsync(object state) {
			// Ueberspringt diesen Tick, falls der vorherige (z.B. wegen eines langsamen GoToSleep-/
			// WakeUp-Uebergangs mit Netzwerkaufrufen) noch laeuft - non-blocking, kein Aufstauen.
			if (!await _ghostSleepScheduleLock.WaitAsync(0))
				return;
			try {
				// der vorherige Guard rief
				// SettingsService.IsSettingSet(InstanceSettings, "GhostSleep") auf - bisher im gesamten
				// Codebase wird IsSettingSet aber immer nur mit einem SUB-Objekt aufgerufen (z.B.
				// IsSettingSet(InstanceSettings.Defender, "Discord")), nie mit dem Root-Objekt selbst.
				// Verdacht: dieser Root-Level-Aufruf verhaelt sich anders/liefert immer false, wodurch
				// hier bei JEDEM 60s-Tick still und ohne jedes Log returned wurde (verifiziert: mehrere
				// Testlaeufe mit Active=true, korrekt konfiguriertem GoToSleep/WakeUp, trotzdem ausser
				// der Start-Zeile absolut kein GhostSleep-Log-Eintrag, auch mit dem festen 60s-Timer aus
				// v2). Ersetzt durch einen direkten try/catch-Zugriff, der ohnehin schon fuer "Active"
				// noetig ist und robust genug ist, ein komplett fehlendes GhostSleep-Objekt abzufangen.
				// die bisherige v3/v4-Diagnose loggte
				// bei JEDEM 60s-Tick Rohwert+Typ von GhostSleep.Active auf Info-Level, unabhaengig vom
				// Ergebnis - eingebaut, um den (mittlerweile gefundenen und behobenen) "GhostSleep faelschlich
				// unter SleepMode verschachtelt"-Bug zu diagnostizieren (siehe ghostsleep-plan-adr.md,
				// WakeUp-Uebergang 28.09. 04:00:57 im Log bestaetigt). Ab jetzt nur noch bei einem
				// tatsaechlichen Lese-/Cast-Fehler geloggt (Warning), nicht mehr bei jedem erfolgreichen Tick.
				bool active = false;
				try {
					active = (bool) InstanceSettings.GhostSleep.Active;
				} catch (Exception ex) {
					object rawActive = null;
					try { rawActive = InstanceSettings.GhostSleep.Active; } catch { /* bleibt null */ }
					log(LogLevel.Warning, LogSender.Tbot,
						$"GhostSleep: could not read/cast Active (raw={(rawActive?.ToString() ?? "null")}, type {(rawActive?.GetType().FullName ?? "null")}): {ex.Message}");
				}

				if (!active) {
					GhostSleepPreSleepRestore("GhostSleep deaktiviert");
					if (_ghostSleeping)
						await GhostSleepWakeUpAsync(null);
					return;
				}

				if (!DateTime.TryParse((string) InstanceSettings.GhostSleep.GoToSleep, out DateTime goToSleep) ||
					!DateTime.TryParse((string) InstanceSettings.GhostSleep.WakeUp, out DateTime wakeUp) ||
					goToSleep == wakeUp) {
					log(LogLevel.Warning, LogSender.Tbot, "GhostSleep: GoToSleep/WakeUp time invalid or configured identically - GhostSleep stays inactive.");
					GhostSleepPreSleepRestore("invalid times");
					if (_ghostSleeping)
						await GhostSleepWakeUpAsync(null);
					return;
				}

				DateTime time = await _tbotOgameBridge.GetDateTime();
				bool shouldBeAsleep = ComputeShouldBeAsleep(time, goToSleep, wakeUp);

				// Vorphase: innerhalb von PreSleepExpeditionMinutes vor GoToSleep (Tageszeit-Vergleich, damit auch
				// ueber Mitternacht korrekt).
				int preMinutes = GhostSleepPreSleepMinutes();
				double minutesUntilSleep = (goToSleep.TimeOfDay - time.TimeOfDay).TotalMinutes;
				if (minutesUntilSleep < 0)
					minutesUntilSleep += 24 * 60;
				bool inPreWindow = preMinutes > 0 && !shouldBeAsleep && !_ghostSleeping && minutesUntilSleep <= preMinutes;
				if (inPreWindow) {
					if (!_preSleepExpoActive)
						GhostSleepPreSleepBegin();
				} else if (_preSleepExpoActive && !shouldBeAsleep) {
					GhostSleepPreSleepRestore("Zeitfenster verlassen");
				}

				if (shouldBeAsleep && !_ghostSleeping) {
					await GhostSleepGoToSleepAsync(wakeUp);
				} else if (!shouldBeAsleep && _ghostSleeping) {
					await GhostSleepWakeUpAsync(goToSleep);
				}
				// sonst: Zustand stimmt bereits - naechster Tick (spaetestens in 60s) prueft erneut.
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: An error has occurred while handling the schedule: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			} finally {
				_ghostSleepScheduleLock.Release();
			}
		}

		// GoToSleep-Uebergang (siehe Plan, Abschnitt "Ablauf bei GhostSleep-Start"):
		// 1) Fleetsave vom Heimatmond, 2) Blanket-Stop aller Features ausser Expeditions/Defender/SleepMode,
		// 3) Expeditions.WaitForAllExpeditions auf true erzwingen (nativer Batch-Rhythmus),
		// 4) Defender auf PASSIV + ersten zufaelligen Anker aus den gerade laufenden Expeditionsrueckkehrern.
		// Offener Punkt aus dem Plan (Nr. 1): kein Pre-Check auf laufenden Angriff vor dem Start - das
		// Fleetsave in Schritt 1 deckt das Risiko ausreichend ab, siehe Plan-Dokument.
		private async Task GhostSleepGoToSleepAsync(DateTime? wakeUpEta) {
			try {
				_ghostSleeping = true;
				log(LogLevel.Information, LogSender.Tbot, "GhostSleep: Going to sleep...");
				if (wakeUpEta.HasValue)
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next schedule check/wake-up window at {wakeUpEta.Value.ToString()}");

				// 0) Vorphase abschliessen: Expeditions-Slots auffuellen, dann Originaleinstellungen zurueck
				// (Schritt 3 unten setzt WaitForAll danach wie gehabt auf true).
				if (_preSleepExpoActive) {
					await GhostSleepExpeditionTopUpAsync();
					GhostSleepPreSleepRestore("falling asleep");
				}

				// 1) Fleetsave ALLER Celestials (dieselbe Methode, die DefenderWorker fuer Angriffs-Fleetsaves
				// nutzt). PATCH GhostSleep (konfigurierbare Prioritaets-Include-Liste, 28.09.2026 - Nutzer-
				// Vorgabe: "heimatmond und planet sollten IMMER bei jedem save rausgehn", danach generalisiert
				// zu einer Config-Liste statt nur einer festen Koordinate): Origin
				// (InstanceSettings.Brain.Transports.Origin - dasselbe explizit konfigurierte Home-Celestial,
				// das an anderer Stelle im Code bereits als kanonisches "Zuhause" verwendet wird, siehe
				// TelegramGetCurrentCelestial()) bleibt automatisch immer priorisiert. Zusaetzlich kann der
				// Nutzer beliebig viele weitere G:S:P-Koordinaten ueber den neuen, optionalen Config-Key
				// InstanceSettings.GhostSleep.PriorityFleetsaveCelestials hinterlegen (Array von
				// {"Galaxy":.., "System":.., "Position":..}) - an JEDER dieser Koordinaten werden automatisch
				// BEIDE dort vorhandenen Celestials (Mond UND Planet, falls beide existieren) IMMER zuerst
				// gefleetsaved, unabhaengig vom jeweils konfigurierten Type. Aeltere Configs ohne diesen Key
				// verhalten sich unveraendert (nur Origin-Paar priorisiert). Danach ALLE uebrigen Celestials
				// der Reihe nach - AutoFleetSave() ist laut den Kommentaren in FleetScheduler.cs bereits ein
				// sicherer No-Op, wenn ein Celestial keine Flotte zum Verschicken hat (loggt nur, tut sonst
				// nichts), Kolonien ohne stationierte Flotte kosten also nur einen billigen API-Call, keinen
				// verschwendeten Flottenslot. Jeder Celestial einzeln try/catch-abgesichert, damit ein
				// Fehlschlag bei einem die uebrigen nicht verhindert. Bewusst unconditional (wie schon zuvor) -
				// ignoriert Defender.Autofleet.Active, das nur die ANGRIFFS-Fleetsave-Logik in DefenderWorker
				// steuert, nicht GhostSleep.
				List<(int Galaxy, int System, int Position)> priorityCoords = new();
				try {
					int originGalaxy = (int) InstanceSettings.Brain.Transports.Origin.Galaxy;
					int originSystem = (int) InstanceSettings.Brain.Transports.Origin.System;
					int originPosition = (int) InstanceSettings.Brain.Transports.Origin.Position;
					priorityCoords.Add((originGalaxy, originSystem, originPosition));
				} catch (Exception e) {
					log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not read Brain.Transports.Origin: {e.Message}");
				}
				try {
					var configuredList = InstanceSettings.GhostSleep.PriorityFleetsaveCelestials;
					if (configuredList != null) {
						foreach (dynamic entry in (System.Collections.IEnumerable) configuredList) {
							try {
								int g = (int) entry.Galaxy;
								int s = (int) entry.System;
								int p = (int) entry.Position;
								if (!priorityCoords.Contains((g, s, p)))
									priorityCoords.Add((g, s, p));
							} catch (Exception entryEx) {
								log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: invalid entry in PriorityFleetsaveCelestials skipped: {entryEx.Message}");
							}
						}
					}
				} catch {
					// GhostSleep.PriorityFleetsaveCelestials nicht konfiguriert (aeltere Config-Datei) - kein
					// Fehler, es zaehlt dann einfach nur die Origin-Koordinate oben als Prioritaet.
				}

				var allCelestials = userData.celestials.Unique().ToList();
				List<Celestial> priorityCelestials = new();
				foreach (var (g, s, p) in priorityCoords) {
					var atCoord = allCelestials
						.Where(c => c.Coordinate.Galaxy == g && c.Coordinate.System == s && c.Coordinate.Position == p)
						.Where(c => c.Coordinate.Type == Celestials.Moon || c.Coordinate.Type == Celestials.Planet)
						.OrderBy(c => c.Coordinate.Type == Celestials.Moon ? 0 : 1) // Mond zuerst, dann Planet - reine Log-Kosmetik
						.ToList();
					if (atCoord.Count == 0) {
						log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: no moon/planet found at prioritized coordinate {g}:{s}:{p}.");
						continue;
					}
					foreach (var c in atCoord) {
						if (!priorityCelestials.Any(existing => existing.ID == c.ID))
							priorityCelestials.Add(c);
					}
				}

				var priorityIds = priorityCelestials.Select(c => c.ID).ToHashSet();
				var orderedCelestials = priorityCelestials.Concat(allCelestials.Where(c => !priorityIds.Contains(c.ID)));

				foreach (var celestial in orderedCelestials) {
					try {
						if (celestial.Coordinate.Type == Celestials.Planet) {
							// Planeten sind phalanxbar - wenn ALLE eigenen
							// Celestials im selben Moment eine Fleetsave-Flotte verschicken, ist schon
							// der SENDEZEITPUNKT selbst ein erkennbares Bulk-Muster (unabhaengig von der
							// unterschiedlichen Speed aus GetFleetSaveDestination). Planeten-Fleetsave
							// laeuft deshalb als Fire-and-Forget-Task mit 0-20 Minuten Zufalls-Delay -
							// Monde (nicht phalanxbar) speichern weiterhin sofort, und der Rest von
							// GhostSleep (Feature-Stop, Defender-PASSIV unten) wartet nicht auf die
							// verzoegerten Planeten. Eigenes try/catch im Task, damit ein Fehlschlag
							// hier nicht mit der aeusseren Schleife kollidiert bzw. verloren geht.
							Celestial planetSnapshot = celestial;
							int delayMs = new Random().Next(0, 20 * 60 * 1000 + 1);
							_ = Task.Run(async () => {
								try {
									await Task.Delay(delayMs);
									Celestial updatedPlanet = await _tbotOgameBridge.UpdatePlanet(planetSnapshot, UpdateTypes.Ships);
									await _fleetScheduler.AutoFleetSave(updatedPlanet, true);
									log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: delayed planet fleetsave of {planetSnapshot.ToString()} executed after {delayMs / 1000}s.");
								} catch (Exception e) {
									log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: delayed fleetsave of {planetSnapshot.ToString()} failed: {e.Message}");
								}
							});
						} else {
							Celestial updated = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
							await _fleetScheduler.AutoFleetSave(updated, true);
						}
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: fleetsave of {celestial.ToString()} failed: {e.Message}");
					}
				}

				// 2) Blanket-Stop: alle Features ausser Expeditions/Defender/SleepMode
				_ghostSleepStoppedFeatures = Features.AllFeatures.Where(f => !GhostSleepNeverStopFeatures.Contains(f)).ToList();
				foreach (var feat in _ghostSleepStoppedFeatures) {
					await StopFeature(feat);
				}
				log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: {_ghostSleepStoppedFeatures.Count} feature(s) stopped, Expeditions and Defender keep running.");

				// 3) Expeditions.WaitForAllExpeditions temporaer erzwingen
				try {
					_ghostSleepPrevWaitForAllExpeditions = (bool) InstanceSettings.Expeditions.WaitForAllExpeditions;
					InstanceSettings.Expeditions.WaitForAllExpeditions = true;
				} catch (Exception e) {
					_ghostSleepPrevWaitForAllExpeditions = null;
					log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not set Expeditions.WaitForAllExpeditions: {e.Message}");
				}

				// 4) Defender auf PASSIV, erster zufaelliger Anker aus aktuell laufenden Expeditionsrueckkehrern
				_ghostSleepEscalatedActive = false;
				SetDefenderGhostSleepPassive(true);
				RearmGhostSleepDefenderAnchor();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: error while falling asleep: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}
		}

		// WakeUp-Uebergang: wartet bis das aktuelle Expeditions-Batch komplett zurueck ist PLUS 10-15min
		// Puffer, dann alle zuvor gestoppten Features wieder starten und Expeditions/Defender-Zustand
		// zuruecksetzen. Aus dem regulaeren Schedule-Check heraus (Zeitfenster vorbei) oder von aussen
		// (Settings-Reload/Profilwechsel wuerde GhostSleep sonst inkonsistent stehen lassen - siehe Aufruf
		// in HandleGhostSleepScheduleAsync).
		private async Task GhostSleepWakeUpAsync(DateTime? nextGoToSleepEta) {
			try {
				log(LogLevel.Information, LogSender.Tbot, "GhostSleep: Waking up...");

				try {
					userData.fleets = await _fleetScheduler.UpdateFleets();
					var runningExpeditions = userData.fleets.Where(f => f.Mission == Missions.Expedition && !f.ReturnFlight).ToList();
					var returningExpeditions = userData.fleets.Where(f => f.Mission == Missions.Expedition && f.ReturnFlight).ToList();
					if (runningExpeditions.Count > 0 || returningExpeditions.Count > 0) {
						double maxBackIn = returningExpeditions.Count > 0 ? returningExpeditions.Max(f => f.BackIn ?? 0) : 0;
						// ArriveIn ist (anders als BackIn) nicht nullable in diesem
						// Codebase (siehe z.B. GoToSleepAsync oben: "f.ArriveIn <= timeToWakeup" ohne ??) -
						// "?? 0" darauf war ein Compile-Error (CS0019).
						double maxArriveThenBack = runningExpeditions.Count > 0 ? runningExpeditions.Max(f => (double) f.ArriveIn + 3600) : 0; // grobe Schaetzung falls noch keine Rueckflugdaten
						// IntervalType kennt kein "SomeMinutes" (CS0117) - AMinuteOrTwo
						// ist der naechstliegende bereits vorhandene Wert fuer diesen Zweck.
						long waitMs = (long) (Math.Max(maxBackIn, maxArriveThenBack) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo) + 10 * 60 * 1000;
						if (waitMs > 0) {
							log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: still waiting {TimeSpan.FromMilliseconds(waitMs)} for the current expedition batch to return (+ buffer) before features are restarted.");
							await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(waitMs, TimeSpan.FromMinutes(20).TotalMilliseconds)), cts.Token);
						}
					}
				} catch (Exception e) {
					log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: waiting for the expeditions to return before waking up failed, continuing anyway: {e.Message}");
				}

				// Defender wieder in den normalen Takt, Expeditions-Setting zuruecksetzen
				SetDefenderGhostSleepPassive(false);
				_ghostSleepEscalatedActive = false;
				if (_ghostSleepPrevWaitForAllExpeditions.HasValue) {
					try {
						InstanceSettings.Expeditions.WaitForAllExpeditions = _ghostSleepPrevWaitForAllExpeditions.Value;
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not reset Expeditions.WaitForAllExpeditions: {e.Message}");
					}
					_ghostSleepPrevWaitForAllExpeditions = null;
				}

				// Alle zuvor gestoppten Features wieder starten
				foreach (var feat in _ghostSleepStoppedFeatures) {
					await InitializeFeature(feat);
				}
				_ghostSleepStoppedFeatures.Clear();

				_ghostSleeping = false;
				if (nextGoToSleepEta.HasValue)
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next time to fall asleep at {nextGoToSleepEta.Value.ToString()}");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: error while waking up: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}
		}

		// Waehlt aus den aktuell laufenden/rueckkehrenden Expeditionsflotten EINE zufaellige als neuen
		// Weck-Anker fuer Defender und plant den Defender-Worker per ChangeWorkerPeriod exakt dorthin um
		// (bewusst NICHT ChangeWorkerDueTime - siehe ghost-recall-changeworkerduetime-bug.md; der Worker
		// bleibt dieselbe laufende Instanz, es wird nur seine naechste Ausfuehrung verschoben).
		private void RearmGhostSleepDefenderAnchor() {
			try {
				if (!workers.TryGetValue(Feature.Defender, out var defenderWorkerRaw) || defenderWorkerRaw is not DefenderWorker defenderWorker) {
					log(LogLevel.Warning, LogSender.Tbot, "GhostSleep: Defender worker not found, cannot set a wake anchor.");
					return;
				}

				var candidates = userData.fleets
					.Where(f => f.Mission == Missions.Expedition && f.ReturnFlight && (f.BackIn ?? 0) > 0)
					.ToList();

				DateTime anchorUtc;
				if (candidates.Count > 0) {
					var chosen = candidates[new Random().Next(candidates.Count)];
					// kleiner Vorlauf, damit Defender VOR der Landung schon einen Check macht, nicht erst danach
					anchorUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, (chosen.BackIn ?? 0) - 60));
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next Defender PASSIVE wake anchor on fleet {chosen.ID} (return in {chosen.BackIn}s) - check at {anchorUtc.ToLocalTime()}.");
				} else {
					// Keine rueckkehrende Expedition bekannt (z.B. gerade erst verschickt) - Standard-Fallback,
					// verhindert dauerhaftes Schlafen ohne jeden Check.
					anchorUtc = DateTime.UtcNow.AddMinutes(30);
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: no returning expedition known, fallback check at {anchorUtc.ToLocalTime()}.");
				}

				defenderWorker.GhostSleepNextAnchorUtc = anchorUtc;
				var due = anchorUtc - DateTime.UtcNow;
				if (due < TimeSpan.FromSeconds(1))
					due = TimeSpan.FromSeconds(1);
				defenderWorker.ChangeWorkerPeriod(due);
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: error while setting the Defender anchor: {e.Message}");
			}
		}

		// oeffentlicher Wrapper um die private
		// RearmGhostSleepDefenderAnchor(), damit DefenderWorker (ueber ITBotMain) selbst um einen frischen
		// Anker bitten kann, sobald der zuletzt gesetzte GhostSleepNextAnchorUtc erreicht/ueberschritten ist.
		// Ohne diesen Aufruf blieb der Anker fuer immer auf dem alten, jetzt vergangenen Wert stehen - jeder
		// weitere Execute()-Zyklus in DefenderWorker berechnete daraus ein negatives, auf 1000ms gedecktes
		// Intervall und der Worker feuerte praktisch im Sekundentakt weiter (Log-Spam "GhostSleep PASSIV:
		// naechster kurzer Wach-Check um <alter, laengst verstrichener Zeitpunkt>", siehe
		// ghostsleep-plan-adr.md). No-Op, wenn GhostSleep gerade gar nicht laeuft (RearmGhostSleepDefenderAnchor
		// selbst faengt "kein Defender-Worker gefunden" bereits ab).
		public void GhostSleepRearmDefenderAnchor() {
			RearmGhostSleepDefenderAnchor();
		}

		private void SetDefenderGhostSleepPassive(bool passive) {
			if (workers.TryGetValue(Feature.Defender, out var defenderWorkerRaw) && defenderWorkerRaw is DefenderWorker defenderWorker) {
				defenderWorker.GhostSleepPassiveMode = passive;
				if (!passive)
					defenderWorker.GhostSleepNextAnchorUtc = null;
			}
		}

		// Von DefenderWorker aufgerufen (HandleAttack), wenn waehrend GhostSleep-PASSIV ein echter Angriff
		// erkannt wird (active=true, Eskalation - siehe Plan) bzw. wenn keine Angriffswelle mehr aussteht
		// (active=false, Deeskalation zurueck auf PASSIV mit neuem Anker).
		public async void GhostSleepEscalate(bool active) {
			if (!_ghostSleeping)
				return;
			try {
				if (active) {
					if (_ghostSleepEscalatedActive)
						return;
					_ghostSleepEscalatedActive = true;
					log(LogLevel.Warning, LogSender.Tbot, "GhostSleep: attack detected - Defender ACTIVE, Expeditions.WaitForAllExpeditions temporarily disabled (variant B).");
					try {
						InstanceSettings.Expeditions.WaitForAllExpeditions = false;
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not disable Expeditions.WaitForAllExpeditions: {e.Message}");
					}
				} else {
					_ghostSleepEscalatedActive = false;
					log(LogLevel.Information, LogSender.Tbot, "GhostSleep: no further attack wave - Defender back to PASSIVE.");
					if (_ghostSleepPrevWaitForAllExpeditions.HasValue) {
						try {
							InstanceSettings.Expeditions.WaitForAllExpeditions = true;
						} catch (Exception e) {
							log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not reset Expeditions.WaitForAllExpeditions: {e.Message}");
						}
					}
					RearmGhostSleepDefenderAnchor();
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: error during escalation (active={active}): {e.Message}");
			}
		}
		// =================== Angriffs-Lockdown ===================
		// Sobald der Defender einen Angriff erkannt und ein Fleetsave registriert hat, laufen ALLE anderen
		// Worker nicht mehr (auch Expeditions): der Fleetsave hat hoechste Prioritaet - keine konkurrierenden
		// Flottenslots, kein AutoFarm/Expedition, das Flotten vom angegriffenen Himmelskoerper abzieht. Der
		// Defender selbst bleibt IMMER an (wird bei Bedarf sogar neu gestartet). Nach Ende der Angriffswelle
		// (DefenderWorker: keine offenen Fleetsaves mehr) werden genau die Worker wieder gestartet, die zuvor liefen.
		private readonly SemaphoreSlim _attackLockdownLock = new SemaphoreSlim(1, 1);
		private bool _attackLockdownActive = false;
		private List<Feature> _attackLockdownStoppedFeatures = new();

		public async Task AttackLockdown(bool active) {
			await _attackLockdownLock.WaitAsync();
			try {
				if (active) {
					if (_attackLockdownActive)
						return;
					_attackLockdownActive = true;

					// Defender darf nie ausgehen
					if (!IsFeatureRunning(Feature.Defender))
						await InitializeFeature(Feature.Defender);

					_attackLockdownStoppedFeatures = Features.AllFeatures
						.Where(f => f != Feature.Defender && f != Feature.SleepMode && IsFeatureRunning(f))
						.ToList();
					foreach (var feat in _attackLockdownStoppedFeatures) {
						try {
							await StopFeature(feat);
						} catch (Exception e) {
							log(LogLevel.Warning, LogSender.Tbot, $"Attack lockdown: could not stop {feat}: {e.Message}");
						}
					}
					log(LogLevel.Warning, LogSender.Tbot, $"Attack lockdown ACTIVE: {_attackLockdownStoppedFeatures.Count} workers stopped, only Defender is running (fleetsave has priority).");
				} else {
					if (!_attackLockdownActive)
						return;
					_attackLockdownActive = false;

					var toRestart = _attackLockdownStoppedFeatures;
					_attackLockdownStoppedFeatures = new();
					int restarted = 0;
					foreach (var feat in toRestart) {
						// Waehrend GhostSleep bleiben dessen gestoppte Features weiterhin aus
						if (_ghostSleeping && _ghostSleepStoppedFeatures.Contains(feat))
							continue;
						try {
							await InitializeFeature(feat);
							restarted++;
						} catch (Exception e) {
							log(LogLevel.Warning, LogSender.Tbot, $"Attack lockdown: could not restart {feat}: {e.Message}");
						}
					}
					log(LogLevel.Information, LogSender.Tbot, $"Attack lockdown ended: {restarted} workers restarted.");
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Attack lockdown (active={active}) failed: {e.Message}");
			} finally {
				_attackLockdownLock.Release();
			}
		}
		// =================== Ende GhostSleep ===================

		public void RemoveTelegramMessenger() {
			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing TelegramMessenger from current instance...");
				telegramMessenger = null;
			}
		}

		// every Telegram message is also sent to the global Discord webhook
		// (DiscordWebhook.Active). mirrorToDiscord=false is used by callers that already send their own
		// Discord alert (DefenderWorker) to avoid duplicates.
		public async Task SendTelegramMessage(string fmt, bool mirrorToDiscord = true) {
			string finalStr = $"<code>[{userData?.userInfo?.PlayerName ?? "Unknown"}@{userData?.serverData?.Name ?? "Unknown"}]</code>\n" + fmt;
			if (telegramMessenger != null)
				await telegramMessenger.SendMessage(finalStr);
			if (mirrorToDiscord)
				await SendDiscordInternal(finalStr, NotificationKind.Information);
		}

		// Sends only to the global Discord webhook (no Telegram). Returns false if the webhook is
		// not configured/active, so callers can fall back to something else.
		public async Task<bool> SendDiscordMessage(string fmt, NotificationKind kind = NotificationKind.Information) {
			if (discordWebhookMessenger == null)
				return false;
			string finalStr = $"<code>[{userData?.userInfo?.PlayerName ?? "Unknown"}@{userData?.serverData?.Name ?? "Unknown"}]</code>\n" + fmt;
			return await SendDiscordInternal(finalStr, kind);
		}

		private async Task<bool> SendDiscordInternal(string finalStr, NotificationKind kind) {
			if (discordWebhookMessenger == null)
				return false;
			try {
				await discordWebhookMessenger.SendMessage(finalStr, kind);
				return true;
			} catch (Exception exception) {
				log(LogLevel.Warning, LogSender.Tbot, $"Unable to send Discord notification: {exception.Message}");
				return false;
			}
		}

		private void ConfigureDiscordWebhook() {
			discordWebhookMessenger = null;
			try {
				if (!SettingsService.IsSettingSet(InstanceSettings, "DiscordWebhook") ||
					!SettingsService.IsSettingSet(InstanceSettings.DiscordWebhook, "Active") ||
					!(bool) InstanceSettings.DiscordWebhook.Active)
					return;
				string webhookUrl = (string) InstanceSettings.DiscordWebhook.Url;
				if (string.IsNullOrWhiteSpace(webhookUrl)) {
					log(LogLevel.Warning, LogSender.Tbot, "Discord webhook is active but no URL is configured. Discord notifications are disabled.");
					return;
				}
				string username = SettingsService.IsSettingSet(InstanceSettings.DiscordWebhook, "Username")
					? (string) InstanceSettings.DiscordWebhook.Username
					: "TBot";
				discordWebhookMessenger = new DiscordWebhookMessenger(webhookUrl, username);
				log(LogLevel.Information, LogSender.Tbot, "Discord webhook notifications enabled.");
			} catch (Exception exception) {
				log(LogLevel.Warning, LogSender.Tbot, $"Unable to configure Discord webhook. Discord notifications are disabled: {exception.Message}");
			}
		}

		public async Task TelegramGetFleets() {
			userData.fleets = (await FleetScheduler.UpdateFleets()).Where(f => !f.ReturnFlight).ToList();
			string message = "";
			foreach (Fleet fleet in userData.fleets) {
				message += $"{fleet.ID} -> Origin: {fleet.Origin.ToString()}, Dest: {fleet.Destination.ToString()}, Type: {fleet.Mission}, ArrivalTime: {fleet.ArrivalTime.ToString()}\n\n";
			}
			await SendTelegramMessage(message);

		}

		public async Task TelegramBuild(Buildables buildable, decimal num = 0) {
			string results = "";
			decimal MaxNumToBuild = 0;
			Resources cost = _helpersService.CalcPrice(buildable, 1);
			foreach (Celestial celestial in userData.celestials.Where(c => c is Planet).ToList()) {
				List<decimal> MaxNumber = new();
				await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				if ((int) celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory || (int) celestial.Constructions.BuildingID == (int) Buildables.Shipyard) {
					results += $"{celestial.Coordinate.ToString()}: Shipyard or Nanite in construction\n";
					continue;
				}
				await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				Resources resources = celestial.Resources;
				if (num == 0) {
					if (cost.Metal > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Metal / (decimal) cost.Metal));
					if (cost.Crystal > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Crystal / (decimal) cost.Crystal));
					if (cost.Deuterium > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Deuterium / (decimal) cost.Deuterium));

					MaxNumToBuild = MaxNumber.Min();
				} else {
					MaxNumToBuild = num;
				}

				if (MaxNumToBuild > 0) {
					if (buildable == Buildables.RocketLauncher || buildable == Buildables.LightLaser || buildable == Buildables.HeavyLaser || buildable == Buildables.GaussCannon || buildable == Buildables.IonCannon || buildable == Buildables.PlasmaTurret || buildable == Buildables.InterplanetaryMissiles || buildable == Buildables.AntiBallisticMissiles) {
						await _ogameService.BuildDefences(celestial, buildable, (long) MaxNumToBuild);
					} else {
						await _ogameService.BuildShips(celestial, buildable, (long) MaxNumToBuild);
						results += $"{celestial.Coordinate.ToString()}: {MaxNumToBuild} started\n";
					}
				} else {
					results += $"{celestial.Coordinate.ToString()}: Not enough resources\n";
				}
			}
			if (results != "") {
				await SendTelegramMessage(results);
			} else {
				await SendTelegramMessage("Could not start build anywhere.");
			}
			return;
		}


		public async Task TelegramCollect(bool noLimit = false) {
			// this was previously "void" and the async Collect() call was fired-and-forgotten
			// (never awaited), so /collect and /collectall appeared unresponsive: the method returned
			// immediately, before the repatriate cycle - and its Telegram result message - ever ran.
			await SendTelegramMessage("Collecting...");
			await _fleetScheduler.Collect(noLimit);
		}

		public async Task TelegramCancelGhostSleep() {
			if (!timers.TryGetValue("GhostSleepTimer", out Timer value)) {
				await SendTelegramMessage("No GhostSleep configured or too late to cancel (fleets already sent), Do a <code>/wakeup</code>");
				return;
			}

			timers.TryGetValue("GhostSleepTimer", out Timer value2);
			value2.Dispose();
			timers.Remove("GhostSleepTimer");
			telegramUserData.Mission = Missions.None;
			await SendTelegramMessage("Ghostsleep canceled!");

			return;
		}

		public async Task TelegramJumpGate(Celestial origin, Coordinate destination, string mode) {
			if (origin.Coordinate.Type == Celestials.Planet) {
				await SendTelegramMessage($"Current Celestial is not a moon.");
				return;
			}

			Celestial moondest = userData.celestials.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) destination.Galaxy)
				.Where(c => c.Coordinate.System == (int) destination.System)
				.Where(c => c.Coordinate.Position == (int) destination.Position)
				.Where(c => c.Coordinate.Type == Celestials.Moon)
				.SingleOrDefault() ?? new() { ID = 0 };

			if (moondest.ID == 0) {
				await SendTelegramMessage($"{destination.ToString()} -> Moon not found!");
				return;
			}

			if (moondest.Coordinate.ToString().Equals(origin.Coordinate.ToString())) {
				await SendTelegramMessage($"Origin and destination are the same! did you /celestial?");
				return;
			}

			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);

			if (origin.Ships.GetMovableShips().IsEmpty()) {
				await SendTelegramMessage($"No ships on {origin.Coordinate}, did you /celestial?");
				return;
			}

			var payload = origin.Resources;
			Ships ships = origin.Ships;
			if (mode.Equals("auto")) {
				float cargoBonus = 0;
				if (origin.LFBonuses != null && origin.LFBonuses.LfShipBonusesInt != null && origin.LFBonuses.LfShipBonusesInt.Count > 0 && origin.LFBonuses.LfShipBonusesInt.ContainsKey((int) Buildables.SmallCargo)) {
					cargoBonus = origin.LFBonuses.LfShipBonusesInt.GetValueOrDefault((int) Buildables.SmallCargo).CargoCapacity;
				}
				long idealSmallCargo = _helpersService.CalcShipNumberForPayload(payload, Buildables.SmallCargo, userData.researches.HyperspaceTechnology, userData.serverData, cargoBonus, userData.userInfo.Class, userData.serverData.ProbeCargo);

				if (idealSmallCargo <= origin.Ships.GetAmount(Buildables.SmallCargo)) {
					ships.SetAmount(Buildables.SmallCargo, origin.Ships.GetAmount(Buildables.SmallCargo) - (long) idealSmallCargo);
				} else {
					cargoBonus = 0;
					if (origin.LFBonuses != null && origin.LFBonuses.LfShipBonusesInt != null && origin.LFBonuses.LfShipBonusesInt.Count > 0 && origin.LFBonuses.LfShipBonusesInt.ContainsKey((int) Buildables.LargeCargo)) {
						cargoBonus = origin.LFBonuses.LfShipBonusesInt.GetValueOrDefault((int) Buildables.LargeCargo).CargoCapacity;
					}
					long idealLargeCargo = _helpersService.CalcShipNumberForPayload(payload, Buildables.LargeCargo, userData.researches.HyperspaceTechnology, userData.serverData, cargoBonus, userData.userInfo.Class, userData.serverData.ProbeCargo);
					if (idealLargeCargo <= origin.Ships.GetAmount(Buildables.LargeCargo)) {
						ships.SetAmount(Buildables.LargeCargo, origin.Ships.GetAmount(Buildables.LargeCargo) - (long) idealLargeCargo);
					} else {
						ships.SetAmount(Buildables.SmallCargo, 0);
						ships.SetAmount(Buildables.LargeCargo, 0);
					}
				}
			}
			try {
				await _ogameService.JumpGate(origin, moondest, ships);
				await SendTelegramMessage($"JumGate Done!");
			} catch (Exception ex) {
				await SendTelegramMessage($"JumGate Failed! Error: {ex.Message}");
			}
		}

		public async Task TelegramPhalanx(Celestial origin, Coordinate target) {
			try {
				List<Fleet> phalanxed = await _ogameService.Phalanx(origin, target);
				await SendTelegramMessage($"Phalanxed {phalanxed.Count} fleets");
				foreach (Fleet fleetPhalanxed in phalanxed) {
					string currFleetStr =
						$"Mission {fleetPhalanxed.Mission.ToString()}\n" +
						$"Origin {fleetPhalanxed.Origin.ToString()}\n" +
						$"Destination {fleetPhalanxed.Destination.ToString()}\n" +
						$"Ships: {fleetPhalanxed.Ships.ToString()}";
					await SendTelegramMessage($"Phalanxed: {currFleetStr}");

				}
			} catch (Exception ex) {
				await SendTelegramMessage($"No fleet phalanxed. Client returned {ex.Message}");
			}
		}

		public async Task TelegramDeploy(Celestial celestial, Coordinate destination, decimal speed) {
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: No ships!");
				await SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return;
			}
			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: there is no fuel!");
				await SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return;
			}

			FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(celestial.Coordinate, destination, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, celestial.LFBonuses, userData.userInfo.Class, userData.allianceClass);
			int fleetId = await _fleetScheduler.SendFleet(celestial, celestial.Ships, destination, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} deployed from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				await SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return;
			}

			return;
		}

		// Telegram /debrissave <Dauer> [x:y:z[:planet|moon]]: Flotte(n) immer mit 10 % Speed zum naechsten Truemmerfeld,
		// Rueckruf nach der halben Dauer (Rueckkehr ca. nach <Dauer>). Ohne Koordinate: alle Himmelskoerper aus
		// DebrisSave.Celestials der Config (Array von { Galaxy, System, Position, Type? }).
		public async Task TelegramDebrisSave(long durationSeconds, Coordinate specific) {
			var all = userData.celestials.Unique().ToList();
			List<Celestial> targets = new();

			void AddMatch(int g, int sys, int pos, Celestials? type) {
				var match = all
					.Where(c => c.Coordinate.Galaxy == g && c.Coordinate.System == sys && c.Coordinate.Position == pos)
					.Where(c => c.Coordinate.Type == Celestials.Moon || c.Coordinate.Type == Celestials.Planet)
					.Where(c => type == null || c.Coordinate.Type == type)
					.OrderBy(c => c.Coordinate.Type == Celestials.Moon ? 0 : 1) // Mond zuerst
					.FirstOrDefault();
				if (match == null)
					log(LogLevel.Warning, LogSender.Tbot, $"[DebrisSave] no celestial found at {g}:{sys}:{pos}.");
				else if (!targets.Contains(match))
					targets.Add(match);
			}

			if (specific != null) {
				AddMatch(specific.Galaxy, specific.System, specific.Position, specific.Type == Celestials.Moon || specific.Type == Celestials.Planet ? specific.Type : (Celestials?) null);
			} else {
				try {
					var list = InstanceSettings.DebrisSave.Celestials;
					foreach (dynamic entry in (System.Collections.IEnumerable) list) {
						try {
							Celestials? type = null;
							try {
								string t = (string) entry.Type;
								if (!string.IsNullOrEmpty(t) && Enum.TryParse(t, true, out Celestials parsed)) type = parsed;
							} catch { }
							AddMatch((int) entry.Galaxy, (int) entry.System, (int) entry.Position, type);
						} catch (Exception e) {
							log(LogLevel.Warning, LogSender.Tbot, $"[DebrisSave] invalid entry in DebrisSave.Celestials skipped: {e.Message}");
						}
					}
				} catch {
					await SendTelegramMessage("DebrisSave.Celestials is not configured. Use /debrissave <duration> <x:y:z> or add the list to the config.");
					return;
				}
			}

			if (targets.Count == 0) {
				await SendTelegramMessage("DebrisSave: no matching celestial found.");
				return;
			}
			int sent = 0;
			foreach (var c in targets) {
				try {
					if (await _fleetScheduler.DebrisSave(c, null, true, durationSeconds, Speeds.TenPercent))
						sent++;
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.Tbot, $"[DebrisSave] {c.Coordinate}: {e.Message}");
					await SendTelegramMessage($"DebrisSave {c.Coordinate} failed: {e.Message}");
				}
			}
			await SendTelegramMessage($"DebrisSave: {sent}/{targets.Count} fleet(s) on their way.");
		}

		public async Task<bool> TelegramSwitch(decimal speed, Celestial attacked = null, bool fromTelegram = false) {
			Celestial celestial;

			if (attacked == null) {
				celestial = await TelegramGetCurrentCelestial();
			} else {
				celestial = attacked; //for autofleetsave func when under attack, last option if no other destination found.
			}

			if (celestial.Coordinate.Type == Celestials.Planet) {
				bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
				if (!hasMoon) {
					if (fromTelegram)
						await SendTelegramMessage($"This planet does not have a Moon! Switch impossible.");
					return false;
				}
			}

			Coordinate dest = new();
			dest.Galaxy = celestial.Coordinate.Galaxy;
			dest.System = celestial.Coordinate.System;
			dest.Position = celestial.Coordinate.Position;

			if (celestial.Coordinate.Type == Celestials.Planet) {
				dest.Type = Celestials.Moon;
			} else {
				dest.Type = Celestials.Planet;
			}

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: No ships!");
				if (fromTelegram)
					await SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return false;
			}

			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel!");
				if (fromTelegram)
					await SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return false;
			}

			FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(celestial.Coordinate, dest, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, celestial.LFBonuses, userData.userInfo.Class, userData.allianceClass);
			int fleetId = await _fleetScheduler.SendFleet(celestial, celestial.Ships, dest, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			// De Morgan bug - "!=" combined with "||" was always true (fleetId can never equal
			// more than one of these codes at once, so at least one "!=" is always true), meaning even a
			// failed SendFleet was reported as a successful switch. This matters here in particular because
			// TelegramSwitch is also used as the AutoFleetSave fallback in FleetScheduler.cs (~line 395),
			// where a false-positive AlreadySent=true could suppress the "you gonna get hit" warning.
			if (fleetId != (int) SendFleetCode.GenericError &&
				fleetId != (int) SendFleetCode.AfterSleepTime &&
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				if (fromTelegram)
					await SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return true;
			}

			return false;
		}

		public async void TelegramSetCurrentCelestial(Coordinate coord, string celestialType, Feature updateType = Feature.Null, bool editsettings = false) {
			userData.celestials = await _tbotOgameBridge.UpdateCelestials();

			//check if no error in submitted celestial (belongs to the current player)
			telegramUserData.CurrentCelestial = userData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) coord.Galaxy)
				.Where(c => c.Coordinate.System == (int) coord.System)
				.Where(c => c.Coordinate.Position == (int) coord.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>(celestialType))
				.SingleOrDefault() ?? new() { ID = 0 };

			if (telegramUserData.CurrentCelestial.ID == 0) {
				await SendTelegramMessage("Error! Wrong information. Verify coordinate.\n");
				return;
			}
			if (editsettings) {
				await EditSettings(telegramUserData.CurrentCelestial, updateType);
				await SendTelegramMessage($"JSON settings updated to: {telegramUserData.CurrentCelestial.Coordinate.ToString()}\nWait few seconds for Bot to reload before sending commands.");
			} else {
				await SendTelegramMessage($"Main celestial successfuly updated to {telegramUserData.CurrentCelestial.Coordinate.ToString()}");
			}
			return;
		}

		public async Task<Celestial> TelegramGetCurrentCelestial() {
			if (telegramUserData.CurrentCelestial == null) {
				Celestial celestial;
				celestial = userData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == (int) InstanceSettings.Brain.Transports.Origin.Galaxy)
					.Where(c => c.Coordinate.System == (int) InstanceSettings.Brain.Transports.Origin.System)
					.Where(c => c.Coordinate.Position == (int) InstanceSettings.Brain.Transports.Origin.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) InstanceSettings.Brain.Transports.Origin.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

				if (celestial.ID == 0) {
					await SendTelegramMessage("Error! Could not parse Celestial from JSON InstanceSettings. Need <code>/editsettings</code>");
					return new Celestial();
				}

				telegramUserData.CurrentCelestial = celestial;
			}

			return telegramUserData.CurrentCelestial;
		}

		public async Task TelegramGetInfo(Celestial celestial) {

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			string result = "";
			string resources = $"{celestial.Resources.Metal.ToString("#,#", CultureInfo.InvariantCulture)} Metal\n" +
								$"{celestial.Resources.Crystal.ToString("#,#", CultureInfo.InvariantCulture)} Crystal\n" +
								$"{celestial.Resources.Deuterium.ToString("#,#", CultureInfo.InvariantCulture)} Deuterium\n\n";
			string ships = celestial.Ships.GetMovableShips().ToString();

			if (celestial.Resources.TotalResources == 0)
				result += "No Resources." ?? resources;
			if (celestial.Ships.GetMovableShips().IsEmpty())
				result += "No ships." ?? ships;

			await SendTelegramMessage($"{celestial.Coordinate.ToString()}\n\n" +
				"Resources:\n" +
				$"{resources}" +
				"Ships:\n" +
				$"{ships}");

			return;
		}

		public async Task TelegramCollectDeut(long MinAmount = 0) {
			await _fleetScheduler.CollectDeut(MinAmount);
		}

		// /trans <speed> - bulk version of /switch. From every Planet that has its own Moon,
		// deploy Brain.AutoCargo.CargoType ships plus Deuterium to that Moon, topping each Moon up to
		// two target values: Brain.AutoCargo.MaxCargosToKeep ships and Brain.Transports.DeutToLeaveOnMoons
		// Deuterium. Only the missing difference is sent (capped by what's available on the Planet); a
		// Moon already at both targets is skipped, and a Moon with enough ships but not enough Deuterium
		// is also skipped (no ships available to carry the top-up, and we don't send extra ships purely
		// to ferry Deuterium).
		public async Task TelegramTransportToMoon(decimal speed) {
			Buildables cargoType = Buildables.LargeCargo;
			if (!Enum.TryParse<Buildables>((string) InstanceSettings.Brain.AutoCargo.CargoType, true, out cargoType)) {
				cargoType = Buildables.LargeCargo;
			}
			long targetShips = (long) InstanceSettings.Brain.AutoCargo.MaxCargosToKeep;
			long targetDeut = (long) InstanceSettings.Brain.Transports.DeutToLeaveOnMoons;

			userData.celestials = await _tbotOgameBridge.UpdateCelestials();

			int sentCount = 0;
			int skippedCount = 0;
			List<string> summaryLines = new();

			foreach (Celestial planet in userData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).ToList()) {
				Celestial moon = userData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == planet.Coordinate.Galaxy)
					.Where(c => c.Coordinate.System == planet.Coordinate.System)
					.Where(c => c.Coordinate.Position == planet.Coordinate.Position)
					.Where(c => c.Coordinate.Type == Celestials.Moon)
					.SingleOrDefault();

				if (moon == null) {
					continue;
				}

				Celestial tempPlanet = await _tbotOgameBridge.UpdatePlanet(planet, UpdateTypes.Resources);
				tempPlanet = await _tbotOgameBridge.UpdatePlanet(tempPlanet, UpdateTypes.Ships);
				tempPlanet = await _tbotOgameBridge.UpdatePlanet(tempPlanet, UpdateTypes.LFBonuses);
				Celestial tempMoon = await _tbotOgameBridge.UpdatePlanet(moon, UpdateTypes.Resources);
				tempMoon = await _tbotOgameBridge.UpdatePlanet(tempMoon, UpdateTypes.Ships);

				long moonShips = tempMoon.Ships.GetAmount(cargoType);
				long moonDeut = (long) tempMoon.Resources.Deuterium;

				long shipsNeeded = Math.Max(0, targetShips - moonShips);
				long deutNeeded = Math.Max(0, targetDeut - moonDeut);

				if (shipsNeeded == 0 && deutNeeded == 0) {
					summaryLines.Add($"Skip {tempPlanet.Coordinate}: Moon already at target.");
					skippedCount++;
					continue;
				}
				if (shipsNeeded == 0 && deutNeeded > 0) {
					summaryLines.Add($"Skip {tempPlanet.Coordinate}: Moon needs {deutNeeded} Deuterium but already has enough ships (no ships to carry it).");
					skippedCount++;
					continue;
				}

				long planetShips = tempPlanet.Ships.GetAmount(cargoType);
				long shipsToSend = Math.Min(shipsNeeded, planetShips);

				if (shipsToSend == 0) {
					summaryLines.Add($"Skip {tempPlanet.Coordinate}: no {cargoType} available to send (needs {shipsNeeded}).");
					skippedCount++;
					continue;
				}

				Ships ships = new();
				ships.Add(cargoType, shipsToSend);

				Resources payload = new();
				if (deutNeeded > 0) {
					Resources wantedDeut = new(0, 0, Math.Min(deutNeeded, (long) tempPlanet.Resources.Deuterium));
					Resources maxTransportable = _helpersService.CalcMaxTransportableResources(ships, wantedDeut, userData.researches.HyperspaceTechnology, userData.serverData, tempPlanet.LFBonuses, userData.userInfo.Class, 0, userData.serverData.ProbeCargo);
					payload.Deuterium = maxTransportable.Deuterium;
				}

				int fleetId = await _fleetScheduler.SendFleet(tempPlanet, ships, moon.Coordinate, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

				if (fleetId == (int) SendFleetCode.AfterSleepTime || fleetId == (int) SendFleetCode.NotEnoughSlots || fleetId == (int) SendFleetCode.GenericError) {
					summaryLines.Add($"Failed {tempPlanet.Coordinate} -> Moon: fleetId {fleetId}.");
					skippedCount++;
					continue;
				}

				summaryLines.Add($"Sent {tempPlanet.Coordinate} -> Moon: {shipsToSend} {cargoType}, {payload.Deuterium} Deuterium.");
				sentCount++;
			}

			string summary = $"Transport to Moon done: {sentCount} sent, {skippedCount} skipped.\n" + string.Join("\n", summaryLines);
			await SendTelegramMessage(summary);
		}

		public async Task SleepNow(DateTime WakeUpTime) {
			long interval;

			DateTime time = await _tbotOgameBridge.GetDateTime();
			interval = (long) WakeUpTime.Subtract(time).TotalMilliseconds;
			timers.Add("TelegramSleepModeTimer", new Timer(WakeUpNow, null, interval, Timeout.Infinite));
			await SendTelegramMessage($"Going to sleep, Waking Up at {WakeUpTime.ToString()}");
			log(LogLevel.Information, LogSender.SleepMode, $"Going to sleep..., Waking Up at {WakeUpTime.ToString()}");
			if (userData.isSleeping == false) {
				/* remove?
				if (
					SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
					SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
					(bool) InstanceSettings.SleepMode.LogoutOnSleep
				) {
					loggedIn = false;
					await _ogameService.Logout();
					log(LogLevel.Information, LogSender.SleepMode, $"Logged out from ogamed.");
				}
				*/
				userData.isSleeping = true;
			}
		}
		// ===== PATCH Sleep-Statuskachel (03.10.2026) =====
		// Schreibt alle 30 s /home/tbot/tbot/sleepstatus.json (neben TBot.dll). ogamed liefert die Datei unter
		// /bot/sleepstatus aus, die Kachel unter dem "Manual mode"-Button im Browser liest sie.
		// Alle Zeiten sind Spielzeit (Server-Zeit); Restsekunden werden beim Schreiben berechnet, der Browser
		// zaehlt anhand updatedMs weiter - so gibt es keine Zeitzonen-Probleme.
		private const int SleepStatusTickMs = 30 * 1000;

		private void InitializeSleepStatusWriter() {
			if (timers.TryGetValue("SleepStatusTimer", out Timer old)) {
				old.Dispose();
				timers.Remove("SleepStatusTimer");
			}
			timers.Add("SleepStatusTimer", new Timer(HandleSleepStatusWrite, null, 5000, SleepStatusTickMs));

			// alle 3 s nach Befehlsdateien (slotcmd_*.json) von ogamed suchen
			if (timers.TryGetValue("SlotCmdTimer", out Timer oldCmd)) {
				oldCmd.Dispose();
				timers.Remove("SlotCmdTimer");
			}
			timers.Add("SlotCmdTimer", new Timer(HandleSlotCmd, null, 7000, 3000));
		}

		// ===== PATCH Slot-Kachel (08.10.2026) =====
		// Die Kachel im Browser zeigt belegte/freie Slots und erlaubt, Slot-Limits live zu aendern.
		// ogamed schreibt dafuer eine Datei slotcmd_<zeit>.json ({"key":..,"value":..}) neben die Binary;
		// hier wird sie ausgewertet, im laufenden Speicher UND in instance_settings.json gesetzt.
		// Der Datei-Watcher wuerde sonst einen kompletten Worker-Neustart ausloesen -> kurzes Sperrfenster.
		private DateTime _suppressReloadUntilUtc = DateTime.MinValue;
		private int _slotCmdBusy = 0;

		// erlaubte Schluessel (Whitelist) -> Pfad in der Config
		private static readonly Dictionary<string, string> SlotCmdPaths = new() {
			{ "leaveFree", "General.SlotsToLeaveFree" },
			{ "autofarm", "AutoFarm.MaxSlots" },
			{ "brain", "Brain.Transports.MaxSlots" },
			{ "harvest", "AutoHarvest.MaxSlots" },
			{ "discovery", "AutoDiscovery.MaxSlots" },
			{ "exp", "Expeditions.MaxExpeditionsPerOrigin" },
			{ "p_autofarm", "General.SlotPriorityLevel.AutoFarm" },
			{ "p_expeditions", "General.SlotPriorityLevel.Expeditions" },
			{ "p_harvest", "General.SlotPriorityLevel.AutoHarvest" },
			{ "p_discovery", "General.SlotPriorityLevel.AutoDiscovery" },
			{ "p_brain", "General.SlotPriorityLevel.Brain" }
		};

		// Worker-Schalter der Slot-Kachel: Schluessel -> Config-Pfad (Active-Flag)
		private static readonly Dictionary<string, string> WorkerTogglePaths = new() {
			{ "w_autofarm", "AutoFarm.Active" },
			{ "w_expeditions", "Expeditions.Active" },
			{ "w_harvest", "AutoHarvest.Active" },
			{ "w_discovery", "AutoDiscovery.Active" },
			{ "w_brain", "Brain.Active" }
		};

		// Zu einem Schalter gehoerende Features (Brain = alle Brain*-Features)
		private static List<Feature> FeaturesForToggle(string key) {
			switch (key) {
				case "w_autofarm": return new List<Feature> { Feature.AutoFarm };
				case "w_expeditions": return new List<Feature> { Feature.Expeditions };
				case "w_harvest": return new List<Feature> { Feature.Harvest };
				case "w_discovery": return new List<Feature> { Feature.AutoDiscovery };
				case "w_brain": return Features.AllFeatures.Where(f => f.ToString().StartsWith("Brain")).ToList();
				default: return new List<Feature>();
			}
		}

		// Liest einen Bool-Wert aus den laufenden Settings (null, wenn nicht lesbar).
		// InstanceSettings ist kein JToken, daher ueber dynamic (wie im Rest des Codes).
		private bool? ReadBoolSetting(string path) {
			try {
				switch (path) {
					case "AutoFarm.Active": return (bool) InstanceSettings.AutoFarm.Active;
					case "Expeditions.Active": return (bool) InstanceSettings.Expeditions.Active;
					case "AutoHarvest.Active": return (bool) InstanceSettings.AutoHarvest.Active;
					case "AutoDiscovery.Active": return (bool) InstanceSettings.AutoDiscovery.Active;
					case "Brain.Active": return (bool) InstanceSettings.Brain.Active;
					default: return null;
				}
			} catch {
				return null;
			}
		}

		// Setzt einen Bool-Wert im laufenden Speicher (true bei Erfolg)
		private bool SetBoolSettingInMemory(string path, bool v) {
			try {
				switch (path) {
					case "AutoFarm.Active": InstanceSettings.AutoFarm.Active = v; return true;
					case "Expeditions.Active": InstanceSettings.Expeditions.Active = v; return true;
					case "AutoHarvest.Active": InstanceSettings.AutoHarvest.Active = v; return true;
					case "AutoDiscovery.Active": InstanceSettings.AutoDiscovery.Active = v; return true;
					case "Brain.Active": InstanceSettings.Brain.Active = v; return true;
					default: return false;
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {path} cannot be set in memory: {e.Message}");
				return false;
			}
		}

		// Setzt einen Int-Wert im laufenden Speicher (true bei Erfolg)
		private bool SetIntSettingInMemory(string path, int v) {
			try {
				switch (path) {
					case "General.SlotsToLeaveFree": InstanceSettings.General.SlotsToLeaveFree = v; return true;
					case "AutoFarm.MaxSlots": InstanceSettings.AutoFarm.MaxSlots = v; return true;
					case "Brain.Transports.MaxSlots": InstanceSettings.Brain.Transports.MaxSlots = v; return true;
					case "AutoHarvest.MaxSlots": InstanceSettings.AutoHarvest.MaxSlots = v; return true;
					case "AutoDiscovery.MaxSlots": InstanceSettings.AutoDiscovery.MaxSlots = v; return true;
					case "Expeditions.MaxExpeditionsPerOrigin": InstanceSettings.Expeditions.MaxExpeditionsPerOrigin = v; return true;
					case "General.SlotPriorityLevel.AutoFarm": InstanceSettings.General.SlotPriorityLevel.AutoFarm = v; return true;
					case "General.SlotPriorityLevel.Expeditions": InstanceSettings.General.SlotPriorityLevel.Expeditions = v; return true;
					case "General.SlotPriorityLevel.AutoHarvest": InstanceSettings.General.SlotPriorityLevel.AutoHarvest = v; return true;
					case "General.SlotPriorityLevel.AutoDiscovery": InstanceSettings.General.SlotPriorityLevel.AutoDiscovery = v; return true;
					case "General.SlotPriorityLevel.Brain": InstanceSettings.General.SlotPriorityLevel.Brain = v; return true;
					default: return false;
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {path} cannot be set in memory: {e.Message}");
				return false;
			}
		}

		// Liest einen Int-Wert aus den laufenden Settings (null, wenn nicht lesbar)
		private int? ReadIntSetting(string path) {
			try {
				switch (path) {
					case "General.SlotsToLeaveFree": return (int) InstanceSettings.General.SlotsToLeaveFree;
					case "AutoFarm.MaxSlots": return (int) InstanceSettings.AutoFarm.MaxSlots;
					case "Brain.Transports.MaxSlots": return (int) InstanceSettings.Brain.Transports.MaxSlots;
					case "AutoHarvest.MaxSlots": return (int) InstanceSettings.AutoHarvest.MaxSlots;
					case "AutoDiscovery.MaxSlots": return (int) InstanceSettings.AutoDiscovery.MaxSlots;
					case "Expeditions.MaxExpeditionsPerOrigin": return (int) InstanceSettings.Expeditions.MaxExpeditionsPerOrigin;
					case "General.SlotPriorityLevel.AutoFarm": return (int) InstanceSettings.General.SlotPriorityLevel.AutoFarm;
					case "General.SlotPriorityLevel.Expeditions": return (int) InstanceSettings.General.SlotPriorityLevel.Expeditions;
					case "General.SlotPriorityLevel.AutoHarvest": return (int) InstanceSettings.General.SlotPriorityLevel.AutoHarvest;
					case "General.SlotPriorityLevel.AutoDiscovery": return (int) InstanceSettings.General.SlotPriorityLevel.AutoDiscovery;
					case "General.SlotPriorityLevel.Brain": return (int) InstanceSettings.General.SlotPriorityLevel.Brain;
					default: return null;
				}
			} catch {
				return null;
			}
		}

		private async void HandleSlotCmd(object state) {
			if (Interlocked.Exchange(ref _slotCmdBusy, 1) == 1)
				return;
			try {
				string dir = AppContext.BaseDirectory;
				foreach (string file in Directory.GetFiles(dir, "slotcmd_*.json").OrderBy(f => f)) {
					try {
						JObject cmd = JObject.Parse(File.ReadAllText(file));
						File.Delete(file);
						string key = (string) cmd["key"] ?? "";
						int value = (int) cmd["value"];
						if (WorkerTogglePaths.ContainsKey(key)) {
							await ApplyWorkerToggleAsync(key, value != 0);
							continue;
						}
						if (!SlotCmdPaths.TryGetValue(key, out string path)) {
							log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: unknown key '{key}' ignored.");
							continue;
						}
						value = Math.Max(0, Math.Min(40, value));
						await ApplySlotSettingAsync(path, value);
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: command {Path.GetFileName(file)} failed: {e.Message}");
						try { File.Delete(file); } catch { }
					}
				}
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Slot tile: command directory not readable: {e.Message}");
			} finally {
				Interlocked.Exchange(ref _slotCmdBusy, 0);
			}
		}

		private async Task ApplySlotSettingAsync(string path, int value) {
			int? old = ReadIntSetting(path);
			bool inMemory = SetIntSettingInMemory(path, value);

			// In die Config-Datei schreiben; bei erfolgreichem Speicher-Update den Reload (alle Worker neu) unterdruecken
			if (inMemory)
				_suppressReloadUntilUtc = DateTime.UtcNow.AddSeconds(10);
			try {
				await SettingsService.SetValuesAsync(InstanceSettingsPath, new[] { new KeyValuePair<string, object>(path, (long) value) });
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {path} could not be written to the config file ({e.Message}), only the in-memory value was set.");
				return;
			}
			log(LogLevel.Information, LogSender.Tbot, $"Slot-Kachel: {path} {(old.HasValue ? old.Value.ToString() : "?")} -> {value} set ({(inMemory ? "live, without restarting the workers" : "via config reload")}).");

			try { await WriteSleepStatusAsync(); } catch { }
		}
		// Schaltet einen Worker live an/aus: Active-Flag in Speicher + Config setzen (ohne Reload) und das
		// Feature sofort starten bzw. stoppen. Waehrend Sleep/GhostSleep/Angriffs-Lockdown wird nur das Flag
		// gesetzt - die jeweilige Wiederaufnahme startet den Worker dann gemaess Config.
		private async Task ApplyWorkerToggleAsync(string key, bool on) {
			string path = WorkerTogglePaths[key];
			bool? old = ReadBoolSetting(path);
			bool inMemory = SetBoolSettingInMemory(path, on);

			if (inMemory)
				_suppressReloadUntilUtc = DateTime.UtcNow.AddSeconds(10);
			try {
				await SettingsService.SetValuesAsync(InstanceSettingsPath, new[] { new KeyValuePair<string, object>(path, on) });
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {path} could not be written to the config file ({e.Message}), only the in-memory value was set.");
				return;
			}

			string hinweis = "";
			if (inMemory) {
				bool pausiert = _attackLockdownActive || (userData != null && userData.isSleeping);
				foreach (Feature feat in FeaturesForToggle(key)) {
					if (on) {
						if (_attackLockdownActive && !_attackLockdownStoppedFeatures.Contains(feat))
							_attackLockdownStoppedFeatures.Add(feat); // nach dem Lockdown mitstarten
						if (pausiert || (_ghostSleeping && _ghostSleepStoppedFeatures.Contains(feat))) {
							hinweis = " (starts only after sleep/lockdown)";
							continue;
						}
						try { await InitializeFeature(feat); } catch (Exception e) { log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {feat} could not be started: {e.Message}"); }
					} else {
						// Stoppen im Hintergrund: ein laufender Zyklus soll die Kachel nicht blockieren
						Feature f2 = feat;
						_ = Task.Run(async () => {
							try { await StopFeature(f2); } catch (Exception e) { log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {f2} could not be stopped: {e.Message}"); }
						});
					}
				}
			}
			log(LogLevel.Information, LogSender.Tbot, $"Slot tile: worker {path} {(old.HasValue ? old.Value.ToString() : "?")} -> {on} ({(inMemory ? "live" : "via config reload")}){hinweis}.");

			try { await WriteSleepStatusAsync(); } catch { }
		}
		// ===== Ende PATCH Slot-Kachel =====

		private async void HandleSleepStatusWrite(object state) {
			try {
				await WriteSleepStatusAsync();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Sleep status could not be written: {e.Message}");
			}
		}

		// Liest GoToSleep/WakeUp des aktiven Schlafmodus (ghost=true -> GhostSleep, sonst SleepMode).
		private bool ReadSleepSchedule(bool ghost, out DateTime goToSleep, out DateTime wakeUp) {
			goToSleep = default;
			wakeUp = default;
			try {
				dynamic s = ghost ? InstanceSettings.GhostSleep : InstanceSettings.SleepMode;
				if (!(bool) s.Active)
					return false;
				return DateTime.TryParse((string) s.GoToSleep, out goToSleep) && DateTime.TryParse((string) s.WakeUp, out wakeUp);
			} catch {
				return false;
			}
		}

		// Sekunden bis zur naechsten Uhrzeit (heute oder morgen) relativ zur Spielzeit.
		private static double SecondsUntilNext(DateTime nowGame, DateTime timeOfDay) {
			DateTime next = nowGame.Date.Add(timeOfDay.TimeOfDay);
			if (next <= nowGame)
				next = next.AddDays(1);
			return (next - nowGame).TotalSeconds;
		}

		// --- Angriffs-Snapshot fuer die Statuskachel (vom DefenderWorker nach jedem Check veroeffentlicht) ---
		private readonly object _attackSnapshotLock = new();
		private List<AttackerFleet> _attackSnapshot = new();
		private DateTime _attackSnapshotFetchedUtc = DateTime.MinValue;

		public void PublishAttackSnapshot(List<AttackerFleet> attacks, DateTime fetchedUtc) {
			lock (_attackSnapshotLock) {
				_attackSnapshot = attacks?.Where(a => a != null).ToList() ?? new List<AttackerFleet>();
				_attackSnapshotFetchedUtc = fetchedUtc;
			}
			// Datei sofort aktualisieren, damit die Kachel nicht bis zum naechsten 30-s-Tick wartet
			_ = Task.Run(async () => {
				try { await WriteSleepStatusAsync(); } catch { }
			});
		}

		private async Task WriteSleepStatusAsync() {
			DateTime gameNow;
			try {
				gameNow = await _tbotOgameBridge.GetDateTime();
			} catch {
				gameNow = DateTime.Now;
			}

			bool ghost = _ghostSleeping;
			bool normal = userData != null && userData.isSleeping;
			bool sleeping = ghost || normal;

			// Offene Recalls aus der Pending-Recall-Datei (gleicher Pfad wie FleetScheduler). RecallAtUtc enthaelt
			// in Wahrheit Spielzeit - deshalb nur die ersten 19 Zeichen (ohne Offset/Z) parsen.
			var recalls = new List<object>();
			int more = 0;
			try {
				string playerTag = UserData?.userInfo?.PlayerName ?? "unknown";
				string universeTag = UserData?.serverData?.Name ?? "unknown";
				string path = Path.Combine(AppContext.BaseDirectory, "data", $"pending_recalls_{playerTag}-{universeTag}.json");
				if (File.Exists(path)) {
					var settings = new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
					var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>(File.ReadAllText(path), settings) ?? new JArray();
					var list = new List<(DateTime at, string coord)>();
					foreach (var e in arr) {
						string raw = (string) e["RecallAtUtc"];
						if (string.IsNullOrEmpty(raw) || raw.Length < 19)
							continue;
						if (!DateTime.TryParse(raw.Substring(0, 19), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at))
							continue;
						if (at < gameNow.AddMinutes(-10))
							continue; // lange ueberfaellig/veraltet -> nicht anzeigen
						list.Add((at, (string) e["OriginCoordinate"] ?? "?"));
					}
					list = list.OrderBy(x => x.at).ToList();
					more = Math.Max(0, list.Count - 6);
					foreach (var x in list.Take(6))
						recalls.Add(new { coord = x.coord, at = x.at.ToString("HH:mm"), inSec = (long) Math.Max(0, (x.at - gameNow).TotalSeconds) });
				}
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Pending recalls for the status tile not readable: {e.Message}");
			}

			// Eingehende ECHTE Angriffe (Attack, ACS-Angriff, Mondzerstoerung, Raketen) - Spionage wird bewusst ausgelassen.
			// Restzeit = ArriveIn zum Abrufzeitpunkt minus seither vergangene Zeit; Eintraege nach Einschlag fallen raus.
			var attacks = new List<object>();
			int attacksMore = 0;
			try {
				List<AttackerFleet> snap;
				DateTime snapUtc;
				lock (_attackSnapshotLock) {
					snap = _attackSnapshot.ToList();
					snapUtc = _attackSnapshotFetchedUtc;
				}
				double elapsed = (DateTime.UtcNow - snapUtc).TotalSeconds;
				var real = new List<(double left, string dest, string origin, string mission)>();
				foreach (var a in snap) {
					string mission = a.MissionType.ToString();
					if (mission != "Attack" && mission != "GroupedAttack" && mission != "Destroy" && mission != "MissileAttack")
						continue;
					double left = a.ArriveIn - elapsed;
					if (left < -30)
						continue;
					real.Add((left, a.Destination?.ToString() ?? "?", a.Origin?.ToString() ?? "?", mission));
				}
				real = real.OrderBy(x => x.left).ToList();
				attacksMore = Math.Max(0, real.Count - 4);
				foreach (var x in real.Take(4))
					attacks.Add(new { dest = x.dest, origin = x.origin, mission = x.mission, inSec = (long) Math.Max(0, x.left), at = gameNow.AddSeconds(Math.Max(0, x.left)).ToString("HH:mm:ss") });
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Attacks for the status tile not readable: {e.Message}");
			}

			// Aufwachzeit (wenn schlafend) bzw. Zeit bis zum naechsten Schlafengehen (wenn wach)
			long? wakeInSec = null;
			long? sleepInSec = null;
			if (sleeping) {
				if (ReadSleepSchedule(ghost, out _, out DateTime wake))
					wakeInSec = (long) SecondsUntilNext(gameNow, wake);
			} else {
				double best = double.MaxValue;
				if (ReadSleepSchedule(true, out DateTime gGo, out _))
					best = Math.Min(best, SecondsUntilNext(gameNow, gGo));
				if (ReadSleepSchedule(false, out DateTime sGo, out _))
					best = Math.Min(best, SecondsUntilNext(gameNow, sGo));
				if (best < double.MaxValue)
					sleepInSec = (long) best;
			}

			// Slot-Belegung und Limits fuer die Slot-Kachel
			object slotsInfo = null;
			try {
				var sl = userData?.slots;
				if (sl != null) {
					int total = (int) sl.Total, free = (int) sl.Free;
					slotsInfo = new { total, free, inUse = Math.Max(0, total - free), expTotal = (int) sl.ExpTotal, expInUse = (int) sl.ExpInUse };
				}
			} catch { slotsInfo = null; }
			object slotUse = null;
			try {
				var fl = userData?.fleets;
				if (fl != null) {
					slotUse = new {
						autofarm = fl.Count(f => f.Mission == Missions.Attack),
						brain = fl.Count(f => f.Mission == Missions.Transport),
						harvest = fl.Count(f => f.Mission == Missions.Harvest),
						discovery = fl.Count(f => f.Mission == Missions.Discovery),
						exp = fl.Count(f => f.Mission == Missions.Expedition)
					};
				}
			} catch { slotUse = null; }
			var slotLimits = new {
				leaveFree = ReadIntSetting("General.SlotsToLeaveFree"),
				autofarm = ReadIntSetting("AutoFarm.MaxSlots"),
				brain = ReadIntSetting("Brain.Transports.MaxSlots"),
				harvest = ReadIntSetting("AutoHarvest.MaxSlots"),
				discovery = ReadIntSetting("AutoDiscovery.MaxSlots"),
				exp = ReadIntSetting("Expeditions.MaxExpeditionsPerOrigin")
			};

			// Slot-Prioritaeten (General.SlotPriorityLevel; kleiner = wichtiger) fuer die Worker-Zeilen der Kachel
			var priorities = new {
				autofarm = ReadIntSetting("General.SlotPriorityLevel.AutoFarm"),
				expeditions = ReadIntSetting("General.SlotPriorityLevel.Expeditions"),
				harvest = ReadIntSetting("General.SlotPriorityLevel.AutoHarvest"),
				discovery = ReadIntSetting("General.SlotPriorityLevel.AutoDiscovery"),
				brain = ReadIntSetting("General.SlotPriorityLevel.Brain")
			};

			var workerStates = new {
				autofarm = ReadBoolSetting("AutoFarm.Active"),
				expeditions = ReadBoolSetting("Expeditions.Active"),
				harvest = ReadBoolSetting("AutoHarvest.Active"),
				discovery = ReadBoolSetting("AutoDiscovery.Active"),
				brain = ReadBoolSetting("Brain.Active")
			};

			var payload = new {
				sleeping,
				mode = ghost ? "GhostSleep" : (normal ? "Sleep" : ""),
				wakeInSec,
				sleepInSec,
				recalls,
				more,
				attacks,
				attacksMore,
				slots = slotsInfo,
				slotUse,
				slotLimits,
				workers = workerStates,
				priorities,
				updatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};

			// atomar schreiben (tmp + Move), damit ogamed nie eine halbe Datei liest
			string target = Path.Combine(AppContext.BaseDirectory, "sleepstatus.json");
			string tmp = target + ".tmp";
			File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(payload));
			File.Move(tmp, target, true);
		}
		// ===== Ende PATCH Sleep-Statuskachel =====

		private async void HandleSleepMode(object state) {
			await HandleSleepModeAsync(state);
		}
		private async Task HandleSleepModeAsync(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value)) {
				return;
			}

			try {

				DateTime time = await _tbotOgameBridge.GetDateTime();

				if (!(bool) InstanceSettings.SleepMode.Active) {
					log(LogLevel.Warning, LogSender.SleepMode, "Sleep mode is disabled");
					await WakeUpAsync(null);
				} else if (!DateTime.TryParse((string) InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
					await WakeUpAsync(null);
				} else if (!DateTime.TryParse((string) InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
					await WakeUpAsync(null);
				} else if (goToSleep == wakeUp) {
					log(LogLevel.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
					await WakeUpAsync(null);
				} else {
					long interval;

					if (time >= goToSleep) {
						if (time >= wakeUp) {
							if (goToSleep >= wakeUp) {
								// YES YES YES
								// ASLEEP
								// WAKE UP NEXT DAY
								interval = (long) wakeUp.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await GoToSleepAsync(newTime);
							} else {
								// YES YES NO
								// AWAKE
								// GO TO SLEEP NEXT DAY
								interval = (long) goToSleep.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							}
						} else {
							if (goToSleep >= wakeUp) {
								// YES NO YES
								// THIS SHOULDNT HAPPEN
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							} else {
								// YES NO NO
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await GoToSleepAsync(newTime);
							}
						}
					} else {
						if (time >= wakeUp) {
							if (goToSleep >= wakeUp) {
								// NO YES YES
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							} else {
								// NO YES NO
								// THIS SHOULDNT HAPPEN
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							}
						} else {
							if (goToSleep >= wakeUp) {
								// NO NO YES
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await GoToSleepAsync(newTime);
							} else {
								// NO NO NO
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							}
						}
					}
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while handling sleep mode: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} finally {
			}
		}

		private async void GoToSleep(object state) {
			await GoToSleepAsync(state);
		}
		private async Task GoToSleepAsync(object state) {
			try {
				userData.fleets = await _fleetScheduler.UpdateFleets();
				bool delayed = false;
				if ((bool) InstanceSettings.SleepMode.PreventIfThereAreFleets && userData.fleets.Count() > 0) {
					if (DateTime.TryParse((string) InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp) && DateTime.TryParse((string) InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep)) {
						DateTime time = await _tbotOgameBridge.GetDateTime();
						if (time >= goToSleep && time >= wakeUp && goToSleep < wakeUp)
							goToSleep = goToSleep.AddDays(1);
						if (time >= goToSleep && time >= wakeUp && goToSleep >= wakeUp)
							wakeUp = wakeUp.AddDays(1);

						List<Fleet> tempFleets = new();
						var timeToWakeup = wakeUp.Subtract(time).TotalSeconds;
						// All Deployment Missions that will arrive during sleep
						tempFleets.AddRange(userData.fleets
							.Where(f => f.Mission == Missions.Deploy)
							.Where(f => f.ArriveIn <= timeToWakeup)
						);
						// All other Fleets.Mission
						tempFleets.AddRange(userData.fleets
							.Where(f => f.Mission != Missions.Discovery)
							.Where(f => f.BackIn <= timeToWakeup)
						);
						if (tempFleets.Count() > 0) {
							log(LogLevel.Information, LogSender.SleepMode, "There are fleets that would come back during sleep time. Delaying sleep mode.");
							long interval = 0;
							foreach (Fleet tempFleet in tempFleets) {
								if (tempFleet.Mission == Missions.Discovery)
									continue;
								if (tempFleet.Mission == Missions.Deploy) {
									if (tempFleet.ArriveIn > interval)
										interval = (long) tempFleet.ArriveIn;
								} else {
									if (tempFleet.BackIn > interval)
										interval = (long) tempFleet.BackIn;
								}
							}
							interval *= (long) 1000;
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							DateTime newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
							delayed = true;
							log(LogLevel.Information, LogSender.SleepMode, $"Fleets active, Next check at {newTime.ToString()}");
							if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active) {
								await SendTelegramMessage($"Fleets active, Next check at {newTime.ToString()}");
							}
						}
					} else {
						log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse WakeUp or GoToSleep time.");
					}
				}
				if (!delayed) {
					log(LogLevel.Information, LogSender.SleepMode, "Going to sleep...");
					log(LogLevel.Information, LogSender.SleepMode, $"Waking Up at {state.ToString()}");

					// Optional extra expedition wave with its own speed, before the fleetsave takes the remaining ships
					await SleepWaveBeforeSleepAsync();

					if ((bool) InstanceSettings.SleepMode.AutoFleetSave.Active) {
						var celestialsToFleetsave = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
						if ((bool) InstanceSettings.SleepMode.AutoFleetSave.OnlyMoons)
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
						foreach (Celestial celestial in celestialsToFleetsave.OrderByDescending(c => c.Ships.GetFleetPoints())) {
							try {
								await _fleetScheduler.AutoFleetSave(celestial, true);
							} catch (Exception e) {
								_logger.WriteLog(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while fleetsaving: {e.Message}");
							}
						}
					}

					if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active && state != null) {
						await SendTelegramMessage($"Going to sleep, Waking Up at {state.ToString()}");
					}
					if (userData.isSleeping == false) {
						/* remove?
						if (
							SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
							SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
							(bool) InstanceSettings.SleepMode.LogoutOnSleep
						) {
							loggedIn = false;
							await _ogameService.Logout();
							log(LogLevel.Information, LogSender.SleepMode, $"Logged out from ogamed.");
						}
						*/
						userData.isSleeping = true;
					}
				}
				InitializeFeatures();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while going to sleep: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			}
		}


		public async Task<bool> TelegramIsUnderAttack() {
			bool result = await _ogameService.IsUnderAttack();

			return result;
		}


		public async void WakeUpNow(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("TelegramSleepModeTimer");
			await SendTelegramMessage($"Bot woke up!");

			log(LogLevel.Information, LogSender.SleepMode, "Bot woke up!");

			if (userData.isSleeping) {
				userData.isSleeping = false;
				/* remove?
				if (
					SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
					SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
					(bool) InstanceSettings.SleepMode.LogoutOnSleep
				) {
					await _ogameService.Login();
					loggedIn = true;
					log(LogLevel.Information, LogSender.SleepMode, "Ogamed logged in again!");
				}
				*/
			}
			InitializeFeatures();
		}

		private async void WakeUp(object state) {
			await WakeUpAsync(state);
		}

		private async Task WakeUpAsync(object state) {
			try {
				log(LogLevel.Information, LogSender.SleepMode, "Waking Up...");
				if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active && state != null) {
					await SendTelegramMessage($"Waking up");
					await SendTelegramMessage($"Going to sleep at {state.ToString()}");
				}
				if (userData.isSleeping) {
					userData.isSleeping = false;
					await _ogameService.Login();
					log(LogLevel.Information, LogSender.SleepMode, "Ogamed logged in again!");
				}
				InitializeFeatures();

			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while waking up: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			}
		}

		public async Task TelegramRetireFleet(int fleetId) {
			userData.fleets = await _fleetScheduler.UpdateFleets();
			Fleet ToRecallFleet = userData.fleets.SingleOrDefault(f => f.ID == fleetId) ?? new() { ID = (int) SendFleetCode.GenericError };
			if (ToRecallFleet.ID == (int) SendFleetCode.GenericError) {
				await SendTelegramMessage($"Unable to recall fleet! Already recalled?");
				return;
			}
			_fleetScheduler.RetireFleet(ToRecallFleet);
		}
		public async Task TelegramRetireFleetM(Missions mission) {
			userData.fleets = await _fleetScheduler.UpdateFleets();
			List<Fleet> ToRecallFleet = userData.fleets.Where(f => f.Mission == mission).ToList();
			if (ToRecallFleet.Count == 0) {
				await SendTelegramMessage($"Unable to recall fleet ! No mission ?");
				return;
			}
			foreach (var fleet in ToRecallFleet) {
				_fleetScheduler.RetireFleet(fleet);
			}
		}

		public async Task TelegramMesgAttacker(string message) {
			userData.attacks = await _ogameService.GetAttacks();
			List<int> playerid = new List<int>();

			foreach (AttackerFleet attack in userData.attacks) {
				if (attack.AttackerID != 0 && !playerid.Any(s => s == attack.AttackerID)) {
					try {
						await _ogameService.SendMessage(attack.AttackerID, message);
						playerid.Add(attack.AttackerID);

						await SendTelegramMessage($"Message succesfully sent to {attack.AttackerName}.");
					} catch {
						await SendTelegramMessage($"Unable to send message.");
					}
				} else {
					await SendTelegramMessage($"Unable send message, AttackerID error.");
				}
			}
		}
	}
}