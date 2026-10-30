using Tbot.Common.IO;
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
			TBot.Ogame.Infrastructure.RequestStats.DevLog = (thema, text) => Tbot.Workers.ExtraLog.Write(this, thema, text);

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

		private void ConfigureCollectLogs() {
			try {
				bool aktiv = false, anonym = true;
				double stunden = 12;
				if (SettingsService.IsSettingSet(InstanceSettings, "Experimental") && SettingsService.IsSettingSet(InstanceSettings.Experimental, "CollectLogs")) {
					var c = InstanceSettings.Experimental.CollectLogs;
					aktiv = SettingsService.IsSettingSet(c, "Active") && (bool) c.Active;
					if (SettingsService.IsSettingSet(c, "Hours"))
						stunden = (double) c.Hours;
					if (SettingsService.IsSettingSet(c, "Anonymize"))
						anonym = (bool) c.Anonymize;
				}
				TBot.Common.Logging.CollectLogs.Configure(aktiv, stunden, anonym);
			} catch (Exception) {
			}
		}

		private Credentials GetCredentialsFromSettings() {
			ConfigureCollectLogs();
			TBot.Common.Logging.LogAnonymizer.Enabled = SettingsService.IsSettingSet(InstanceSettings.General, "AnonymizeLogs") && (bool) InstanceSettings.General.AnonymizeLogs;
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

		private void LogCaptchaHint() {
			try {
				log(LogLevel.Warning, LogSender.Tbot, $"Captcha required. If the automatic solver fails, solve it by hand: {CaptchaPageAddress()}. The bot logs in afterwards.");
			} catch (Exception) {
			}
		}

		private DateTime _letzteCaptchaMeldung = DateTime.MinValue;
		private void MeldeLoginPause(string text) {
			try {
				if ((DateTime.UtcNow - _letzteCaptchaMeldung).TotalMinutes < 30)
					return;
				_letzteCaptchaMeldung = DateTime.UtcNow;
				_ = SendTelegramMessage(text);
			} catch (Exception) {
			}
		}

		private LoginPauseConfig ReadLoginPauseConfig() {
			LoginPauseConfig c = new();
			try {
				var g = InstanceSettings.General;
				if (!SettingsService.IsSettingSet(g, "LoginPause"))
					return c;
				var lp = g.LoginPause;
				if (SettingsService.IsSettingSet(lp, "Active"))
					c.Active = (bool) lp.Active;
				if (SettingsService.IsSettingSet(lp, "FailuresBeforePause"))
					c.FailuresBeforePause = Math.Max(1, (int) lp.FailuresBeforePause);
				if (SettingsService.IsSettingSet(lp, "PauseMinutes"))
					c.PauseMinutes = Math.Max(1, (int) lp.PauseMinutes);
				if (SettingsService.IsSettingSet(lp, "MaxPauseMinutes"))
					c.MaxPauseMinutes = Math.Max(c.PauseMinutes, (int) lp.MaxPauseMinutes);
			} catch (Exception) {
			}
			return c;
		}

		private async Task WaitLoginPauseAsync() {
			LoginPauseConfig c = ReadLoginPauseConfig();
			if (!c.Active)
				return;
			var st = LoginPause.Read(InstanceAlias);
			if (st.UntilUtc <= DateTime.UtcNow)
				return;
			try {
				await _ogameService.SetLoginPause(new DateTimeOffset(st.UntilUtc, TimeSpan.Zero).ToUnixTimeSeconds());
			} catch (Exception) {
			}
			DateTime nextLog = DateTime.MinValue;
			while (DateTime.UtcNow < st.UntilUtc) {
				if (DateTime.UtcNow >= nextLog) {
					log(LogLevel.Warning, LogSender.Tbot, $"Login paused for another {Math.Ceiling((st.UntilUtc - DateTime.UtcNow).TotalMinutes):N0} min after {st.Failures} failed login(s) - this protects the IP. To skip the pause, solve the captcha by hand: {CaptchaPageAddress()}");
					nextLog = DateTime.UtcNow.AddMinutes(5);
				}
				await Task.Delay(TimeSpan.FromSeconds(15));
				var status = await _ogameService.GetLoginPauseStatus();
				if (status != null && status.LoggedIn) {
					log(LogLevel.Information, LogSender.Tbot, "Logged in by hand during the login pause - pause ended.");
					LoginPause.Clear(InstanceAlias);
					return;
				}
			}
			try {
				await _ogameService.SetLoginPause(0);
			} catch (Exception) {
			}
			log(LogLevel.Information, LogSender.Tbot, "Login pause over - trying to log in again.");
		}

		private void RegisterLoginFailure() {
			try {
				LoginPauseConfig c = ReadLoginPauseConfig();
				if (!c.Active)
					return;
				DateTime? until = LoginPause.RegisterFailure(InstanceAlias, c);
				if (until.HasValue) {
					_ = _ogameService.SetLoginPause(new DateTimeOffset(until.Value, TimeSpan.Zero).ToUnixTimeSeconds());
					log(LogLevel.Warning, LogSender.Tbot, $"Login failed repeatedly - no further login attempts until {until.Value.ToLocalTime():HH:mm} (pause). Manual captcha: {CaptchaPageAddress()}");
					MeldeLoginPause($"Login failed repeatedly - paused until {until.Value.ToLocalTime():HH:mm}. Solve the captcha by hand to skip the pause: {CaptchaPageAddress()}");
				}
			} catch (Exception) {
			}
		}

		private string CaptchaPageAddress() {
			string host = "localhost";
			string port = "8080";
			try {
				host = (string) InstanceSettings.General.Host ?? "localhost";
				if (string.IsNullOrWhiteSpace(host) || host == "0.0.0.0")
					host = "localhost";
				port = (string) InstanceSettings.General.Port ?? "8080";
			} catch (Exception) {
			}
			return $"http://{host}:{port}/bot/captcha (enter the number 0-3 of the requested symbol)";
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
				LogCaptchaHint();
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

			double zeitAbweichung = serverTime.Year < 2000 ? 0 : (serverTime - DateTime.Now).TotalMinutes;
			log(LogLevel.Information, LogSender.Tbot, $"Server time: {serverTime.ToString()} (system clock {DateTime.Now.ToString()}, difference {zeitAbweichung:F1} min, time zone {TimeZoneInfo.Local.Id})");
			if (Math.Abs(zeitAbweichung) > 3)
				log(LogLevel.Warning, LogSender.Tbot, "Server time and system clock differ by more than 3 minutes - check the time zone and clock of this machine (sleep mode and schedules use the game time).");
			TBot.Common.Logging.LogAnonymizer.Register(userData.userInfo.PlayerName, "[Player]");
			log(LogLevel.Information, LogSender.Tbot, $"Player name: {userData.userInfo.PlayerName}");
			log(LogLevel.Information, LogSender.Tbot, $"Player class: {userData.userInfo.Class.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Alliance class: {userData.allianceClass.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player rank: {userData.userInfo.Rank}");
			log(LogLevel.Information, LogSender.Tbot, $"Player points: {userData.userInfo.Points}");
			log(LogLevel.Information, LogSender.Tbot, $"Player honour points: {userData.userInfo.HonourPoints}");
			ZeigeUpdateMeldung();
		}

		private void ZeigeUpdateMeldung() {
			try {
				if (SettingsService.IsSettingSet(InstanceSettings.General, "ShowUpdateNotes") && !(bool) InstanceSettings.General.ShowUpdateNotes)
					return;
				var notiz = Tbot.Common.Settings.WhatsNew.ClaimBotNotice(Tbot.Common.Settings.UiLanguage.Read());
				if (notiz == null)
					return;
				string kurz = notiz.AsShortText(Tbot.Common.Settings.UiLanguage.Read());
				log(LogLevel.Information, LogSender.Main, kurz);
				_ = SendTelegramMessage(kurz);
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Update notice failed: {e.Message}");
			}
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

			await WaitLoginPauseAsync();
			try {
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
						LogCaptchaHint();
						throw new UnableToLoginException("Unable to login", ex);
					}
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.Tbot, $"Unable to login. (\"{e.Message}\")");
					throw new UnableToLoginException("Unable to login", e);
				}
			} catch (Exception) {
				RegisterLoginFailure();
				throw;
			}
			LoginPause.Clear(InstanceAlias);

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
			_ = Task.Run(() => Tbot.Workers.RewardsCapture.Loop(this, _ogameService, userData.celestials, cts.Token));
			_ = Task.Run(() => Tbot.Workers.LogistikPlaner.Loop(this, _tbotOgameBridge, _helpersService, cts.Token));
			await _fleetScheduler.ReconcilePendingRecalls();
			userData.scheduledFleets = new();
			userData.farmTargets = new();

			if (userData.celestials.Count == 1) {
				await EditSettings(userData.celestials.First());
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);
			}

			await FillTemplateCoordinatesAsync();

			RunConfigDoctor(true);

			ConfigureDiscordWebhook();

			InitializeSleepMode();

			InitializeGhostSleepSchedule();

			InitializeSleepStatusWriter();

			_defenderTestTimer = new Timer(DefenderTestPoll, null, 5000, 3000);

			settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, InstanceSettingsPath,
				new[] { SettingsService.DefaultLoadDirectory + "/**/*.json", SettingsService.SecretsDirectory + "/**/*.json" });
		}

		private Timer _defenderTestTimer;
		private int _defenderTestBusy;
		private async void DefenderTestPoll(object state) {
			if (System.Threading.Interlocked.Exchange(ref _defenderTestBusy, 1) == 1)
				return;
			try {
				string req = Tbot.Workers.DefenderTest.RequestPath(InstanceAlias);
				if (!System.IO.File.Exists(req))
					return;
				string text = System.IO.File.ReadAllText(req);
				System.IO.File.Delete(req);
				var lines = await Tbot.Workers.DefenderTest.Run(this, Newtonsoft.Json.Linq.JObject.Parse(text));
				var result = new { updatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), lines };
				string target = Tbot.Workers.DefenderTest.ResultPath(InstanceAlias);
				System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target));
				AtomicFile.WriteAllText(target, Newtonsoft.Json.JsonConvert.SerializeObject(result));
				log(LogLevel.Information, LogSender.Defender, $"Defender test run ({lines.Count} lines, nothing was sent).");
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Defender, $"Defender test failed: {e.Message}");
			} finally {
				System.Threading.Interlocked.Exchange(ref _defenderTestBusy, 0);
			}
		}

		public async ValueTask DisposeAsync() {
			log(LogLevel.Information, LogSender.Tbot, "Deinitializing instance...");
			try { _defenderTestTimer?.Dispose(); } catch { }

			if (settingsWatcher != null) {
				settingsWatcher.deinitWatch();
				settingsWatcher = null;
			}

			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing instance from telegram...");
				await telegramMessenger.RemoveTBotInstance(this);
			}
			discordWebhookMessenger = null;

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

		private async void InitializeFeatures() { try {
			foreach(var feat in Features.AllFeatures) {
				await InitializeFeature(feat);
			}
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in InitializeFeatures: {asyncVoidEx}"); }
		}

		private bool _legacyConfigLogged;

		private bool IsLegacyConfigBlocked() {
			try {
				if (SettingsService.IsSettingSet(InstanceSettings, "ConfigVersion") && (int) InstanceSettings.ConfigVersion >= 4)
					return false;
				if (!SettingsService.IsSettingSet(InstanceSettings, "Brain") || !SettingsService.IsSettingSet(InstanceSettings.Brain, "LifeformAutoMine"))
					return false;
				var lf = InstanceSettings.Brain.LifeformAutoMine;
				return SettingsService.IsSettingSet(lf, "MaxT2Building") || SettingsService.IsSettingSet(lf, "MaxT3Building")
					|| SettingsService.IsSettingSet(lf, "MaxBuilding6") || SettingsService.IsSettingSet(lf, "MaxBuilding12");
			} catch {
				return false;
			}
		}

		public async Task InitializeFeature(Feature feat) {
			if (feat != Feature.Defender && feat != Feature.SleepMode && IsLegacyConfigBlocked()) {
				if (!_legacyConfigLogged) {
					_legacyConfigLogged = true;
					log(LogLevel.Error, LogSender.Tbot, "OLD CONFIG DETECTED (no \"ConfigVersion\": 4, old lifeform fields present). Only the Defender runs. " +
						"Stop the bot, run MigrateConfig (it is in the main folder), check the config and restart. " +
						"Already migrated by hand? Then add \"ConfigVersion\": 4 at the top of instance_settings.json.");
				}
				return;
			}
			if (feat == Feature.SleepMode) {
				return;
			}
			if (_pauseActive && !_pauseGateMode && (feat != Feature.Defender || _pauseStopsDefender)) {
				lock (_pauseSync) {
					_pauseStoppedFeatures.Add(feat);
				}
				return;
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
				Feature.BrainKeepBoosters => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.BrainAutoResearch => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoFarm => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
#if FASTFARM
				Feature.FastFarm => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
#endif
				Feature.Expeditions => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Harvest => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Colonize => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoDiscovery => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				_ => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds)
			};

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
				return true;
			} else {
				return false;
			}
		}

		public async Task<bool> EditSettings(Celestial celestial = null, Feature feature = Feature.Null, string recall = "", int cargo = 0) {
			await Task.Delay(500, cts.Token);
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

		public async Task<List<SemaphoreSlim>> WaitFeature(TimeSpan? timeout = null) {
			TimeSpan wait = timeout ?? TimeSpan.FromSeconds(20);
			var semaphores = new List<SemaphoreSlim>();
			foreach (var feat in Features.AllFeatures) {
				ITBotWorker worker = _workerFactory.GetWorker(feat);
				if (worker != null) {
					var semaphore = worker.GetSemaphore();
					if (!semaphores.Contains(semaphore))
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
				dynamic newSettings;
				try {
					newSettings = await SettingsService.GetSettings(InstanceSettingsPath);
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.Tbot, $"Settings reload aborted, keeping the running settings: {e.Message}");
					return;
				}
				log(LogLevel.Information, LogSender.Tbot, "Settings file change detected! Waiting workers to complete ongoing activities...");

				cts.Cancel();
				foreach (var worker in workers) {
					log(LogLevel.Information, LogSender.Tbot, $"Stopping feature {worker.Key.ToString()}...");
					await worker.Value.StopWorker();
					log(LogLevel.Information, LogSender.Tbot, $"Feature {worker.Key.ToString()} stopped for settings reload!");
				}

				log(LogLevel.Information, LogSender.Tbot, "Reloading Settings file");
				InstanceSettings = newSettings;
				ConfigureCollectLogs();
				ConfigureDiscordWebhook();

				cts = new();
				await HandleSleepModeAsync(null);
				_lastReloadFinished = DateTime.Now;
			}
			finally {
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

		private bool _ghostSleeping = false;
		private bool? _ghostSleepPrevWaitForAllExpeditions = null;
		private List<Feature> _ghostSleepStoppedFeatures = new();
		private bool _ghostSleepEscalatedActive = false;

		private bool _preSleepExpoActive = false;
		private bool? _preSleepPrevWaitForAll = null;
		private bool? _preSleepPrevWaitForMajority = null;

		private int GhostSleepPreSleepMinutes() {
			try {
				return Math.Max(0, (int) InstanceSettings.GhostSleep.PreSleepExpeditionMinutes);
			} catch {
				return 30;
			}
		}

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

		private readonly object _ghostSleepDrainLock = new();
		private DateTime? _ghostNextSleepUtc = null;
		private DateTime _ghostNextSleepStampUtc = DateTime.MinValue;
		private DateTime? _ghostDrainWaitStartUtc = null;
		private DateTime _ghostDrainLastWaitLogUtc = DateTime.MinValue;

		private dynamic GhostSleepDrainSection() {
			try {
				if (SettingsService.IsSettingSet(InstanceSettings.GhostSleep, "ExpeditionDrain"))
					return InstanceSettings.GhostSleep.ExpeditionDrain;
			} catch {  }
			return null;
		}

		private bool GhostSleepDrainActive() {
			try {
				var section = GhostSleepDrainSection();
				if (section == null || !SettingsService.IsSettingSet(section, "Active"))
					return false;
				return (bool) section["Active"];
			} catch {
				return false;
			}
		}

		private int GhostSleepDrainInt(string key, int defaultValue) {
			try {
				var section = GhostSleepDrainSection();
				if (section != null && SettingsService.IsSettingSet(section, key))
					return Math.Max(0, Convert.ToInt32((object) section[key]));
			} catch {  }
			return defaultValue;
		}

		private bool GhostSleepAnchorRandom() {
			try {
				if (SettingsService.IsSettingSet(InstanceSettings.GhostSleep, "AnchorMode"))
					return string.Equals(Convert.ToString((object) InstanceSettings.GhostSleep.AnchorMode), "Random", StringComparison.OrdinalIgnoreCase);
			} catch {  }
			return false;
		}

		public bool GhostSleepAllowsNewExpedition(int? learnedDurationSeconds) {
			try {
				if (!GhostSleepDrainActive())
					return true;
				if (_ghostSleeping || SleepWaveActive)
					return true;
				DateTime? nextSleep;
				DateTime stamp;
				lock (_ghostSleepDrainLock) {
					nextSleep = _ghostNextSleepUtc;
					stamp = _ghostNextSleepStampUtc;
				}
				if (!nextSleep.HasValue || (DateTime.UtcNow - stamp).TotalMinutes > 5)
					return true;
				double remainingMin = (nextSleep.Value - DateTime.UtcNow).TotalMinutes;
				double durationMin = (learnedDurationSeconds ?? GhostSleepDrainInt("DefaultDurationMinutes", 90) * 60) / 60.0;
				int bufferMin = GhostSleepDrainInt("BufferMinutes", 10);
				return durationMin <= remainingMin + bufferMin;
			} catch {
				return true;
			}
		}

		private async Task<bool> GhostSleepDrainShouldWaitAsync() {
			try {
				if (!_ghostDrainWaitStartUtc.HasValue)
					_ghostDrainWaitStartUtc = DateTime.UtcNow;
				int maxWaitMin = GhostSleepDrainInt("StragglerWaitMinutes", 20);
				double waitedMin = (DateTime.UtcNow - _ghostDrainWaitStartUtc.Value).TotalMinutes;
				if (waitedMin >= maxWaitMin) {
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep drain: waited {maxWaitMin} min for stragglers, going to sleep anyway.");
					return false;
				}
				userData.fleets = await _fleetScheduler.UpdateFleets();
				int away = userData.fleets.Count(f => f.Mission == Missions.Expedition);
				if (away == 0) {
					log(LogLevel.Information, LogSender.Tbot, "GhostSleep drain: all expeditions are home, going to sleep.");
					return false;
				}
				if ((DateTime.UtcNow - _ghostDrainLastWaitLogUtc).TotalMinutes >= 5) {
					_ghostDrainLastWaitLogUtc = DateTime.UtcNow;
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep drain: waiting for {away} expedition(s) to come home (max. {maxWaitMin} min, {waitedMin:F0} min so far).");
				}
				return true;
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep drain: waiting check failed, going to sleep: {e.Message}");
				return false;
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
					SleepWaveActive = true;
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

		private async void HandleGhostSleepSchedule(object state) { try {
			await HandleGhostSleepScheduleAsync(state);
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in HandleGhostSleepSchedule: {asyncVoidEx}"); }
		}

		private static bool ComputeShouldBeAsleep(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			if (goToSleep < wakeUp) {
				return time >= goToSleep && time < wakeUp;
			} else {
				return time >= goToSleep || time < wakeUp;
			}
		}

		private async Task HandleGhostSleepScheduleAsync(object state) {
			if (!await _ghostSleepScheduleLock.WaitAsync(0))
				return;
			try {
				bool active = false;
				try {
					active = (bool) InstanceSettings.GhostSleep.Active;
				} catch (Exception ex) {
					object rawActive = null;
					try { rawActive = InstanceSettings.GhostSleep.Active; } catch {  }
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

				DateTime time = await GameNowQuiet();
				bool shouldBeAsleep = ComputeShouldBeAsleep(time, goToSleep, wakeUp);

				int preMinutes = GhostSleepPreSleepMinutes();
				double minutesUntilSleep = (goToSleep.TimeOfDay - time.TimeOfDay).TotalMinutes;
				if (minutesUntilSleep < 0)
					minutesUntilSleep += 24 * 60;
				bool drain = GhostSleepDrainActive();
				lock (_ghostSleepDrainLock) {
					_ghostNextSleepUtc = (shouldBeAsleep || _ghostSleeping) ? null : DateTime.UtcNow.AddMinutes(minutesUntilSleep);
					_ghostNextSleepStampUtc = DateTime.UtcNow;
				}
				if (!shouldBeAsleep)
					_ghostDrainWaitStartUtc = null;
				bool inPreWindow = !drain && preMinutes > 0 && !shouldBeAsleep && !_ghostSleeping && minutesUntilSleep <= preMinutes;
				if (inPreWindow) {
					if (!_preSleepExpoActive)
						GhostSleepPreSleepBegin();
				} else if (_preSleepExpoActive && !shouldBeAsleep) {
					GhostSleepPreSleepRestore("Zeitfenster verlassen");
				}

				if (shouldBeAsleep && !_ghostSleeping) {
					if (drain && await GhostSleepDrainShouldWaitAsync())
						return;
					await GhostSleepGoToSleepAsync(wakeUp);
				} else if (!shouldBeAsleep && _ghostSleeping) {
					await GhostSleepWakeUpAsync(goToSleep);
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: An error has occurred while handling the schedule: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			} finally {
				_ghostSleepScheduleLock.Release();
			}
		}

		private async Task GhostSleepGoToSleepAsync(DateTime? wakeUpEta) {
			try {
				_ghostSleeping = true;
				log(LogLevel.Information, LogSender.Tbot, "GhostSleep: Going to sleep...");
				if (wakeUpEta.HasValue)
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next schedule check/wake-up window at {wakeUpEta.Value.ToString()}");

				_ghostDrainWaitStartUtc = null;
				if (_preSleepExpoActive || GhostSleepDrainActive()) {
					if (!_preSleepExpoActive)
						GhostSleepPreSleepBegin();
					await GhostSleepExpeditionTopUpAsync();
					GhostSleepPreSleepRestore("falling asleep");
				}

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
				}

				var allCelestials = userData.celestials.Unique().ToList();
				List<Celestial> priorityCelestials = new();
				foreach (var (g, s, p) in priorityCoords) {
					var atCoord = allCelestials
						.Where(c => c.Coordinate.Galaxy == g && c.Coordinate.System == s && c.Coordinate.Position == p)
						.Where(c => c.Coordinate.Type == Celestials.Moon || c.Coordinate.Type == Celestials.Planet)
						.OrderBy(c => c.Coordinate.Type == Celestials.Moon ? 0 : 1)
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

				_ghostSleepStoppedFeatures = Features.AllFeatures.Where(f => !GhostSleepNeverStopFeatures.Contains(f)).ToList();
				foreach (var feat in _ghostSleepStoppedFeatures) {
					await StopFeature(feat);
				}
				log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: {_ghostSleepStoppedFeatures.Count} feature(s) stopped, Expeditions and Defender keep running.");

				try {
					_ghostSleepPrevWaitForAllExpeditions = (bool) InstanceSettings.Expeditions.WaitForAllExpeditions;
					InstanceSettings.Expeditions.WaitForAllExpeditions = true;
				} catch (Exception e) {
					_ghostSleepPrevWaitForAllExpeditions = null;
					log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: could not set Expeditions.WaitForAllExpeditions: {e.Message}");
				}

				_ghostSleepEscalatedActive = false;
				SetDefenderGhostSleepPassive(true);
				RearmGhostSleepDefenderAnchor();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: error while falling asleep: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}
		}

		private async Task GhostSleepWakeUpAsync(DateTime? nextGoToSleepEta) {
			try {
				log(LogLevel.Information, LogSender.Tbot, "GhostSleep: Waking up...");

				try {
					userData.fleets = await _fleetScheduler.UpdateFleets();
					var runningExpeditions = userData.fleets.Where(f => f.Mission == Missions.Expedition && !f.ReturnFlight).ToList();
					var returningExpeditions = userData.fleets.Where(f => f.Mission == Missions.Expedition && f.ReturnFlight).ToList();
					if (runningExpeditions.Count > 0 || returningExpeditions.Count > 0) {
						double maxBackIn = returningExpeditions.Count > 0 ? returningExpeditions.Max(f => f.BackIn ?? 0) : 0;
						double maxArriveThenBack = runningExpeditions.Count > 0 ? runningExpeditions.Max(f => (double) f.ArriveIn + 3600) : 0;
						long waitMs = (long) (Math.Max(maxBackIn, maxArriveThenBack) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo) + 10 * 60 * 1000;
						if (waitMs > 0) {
							log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: still waiting {TimeSpan.FromMilliseconds(waitMs)} for the current expedition batch to return (+ buffer) before features are restarted.");
							await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(waitMs, TimeSpan.FromMinutes(20).TotalMilliseconds)), cts.Token);
						}
					}
				} catch (Exception e) {
					log(LogLevel.Warning, LogSender.Tbot, $"GhostSleep: waiting for the expeditions to return before waking up failed, continuing anyway: {e.Message}");
				}

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

		private void RearmGhostSleepDefenderAnchor() {
			try {
				if (!workers.TryGetValue(Feature.Defender, out var defenderWorkerRaw) || defenderWorkerRaw is not DefenderWorker defenderWorker) {
					log(LogLevel.Warning, LogSender.Tbot, "GhostSleep: Defender worker not found, cannot set a wake anchor.");
					return;
				}

				bool randomMode = GhostSleepAnchorRandom();
				if (!randomMode) {
					try {
						var refresh = Task.Run(async () => await _fleetScheduler.UpdateFleets());
						if (refresh.Wait(TimeSpan.FromSeconds(25)))
							userData.fleets = refresh.Result;
					} catch (Exception refreshEx) {
						log(LogLevel.Debug, LogSender.Tbot, $"GhostSleep: could not refresh the fleet list for the anchor, using the cached one: {refreshEx.Message}");
					}
				}

				var candidates = userData.fleets
					.Where(f => f.Mission == Missions.Expedition && (randomMode ? f.ReturnFlight : true) && (f.BackIn ?? 0) > 0)
					.ToList();

				DateTime anchorUtc;
				if (!randomMode) {
					const int minBackInSeconds = 150;
					var last = candidates.Where(f => (f.BackIn ?? 0) > minBackInSeconds).OrderByDescending(f => f.BackIn).FirstOrDefault();
					if (last != null) {
						int leadSeconds = new Random().Next(45, 121);
						anchorUtc = DateTime.UtcNow.AddSeconds((last.BackIn ?? 0) - leadSeconds);
						log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next Defender PASSIVE wake anchor on the last returning expedition {last.ID} (return in {last.BackIn}s) - check at {anchorUtc.ToLocalTime()}.");
					} else if (candidates.Count > 0) {
						int maxBack = candidates.Max(f => f.BackIn ?? 0);
						int restartWait = 240;
						try {
							if (SettingsService.IsSettingSet(InstanceSettings.Expeditions, "MaxWaitNextRound"))
								restartWait = Convert.ToInt32((object) InstanceSettings.Expeditions.MaxWaitNextRound) + 90;
						} catch {  }
						anchorUtc = DateTime.UtcNow.AddSeconds(maxBack + restartWait);
						log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: all expeditions land within {maxBack}s - re-evaluating the anchor after the next wave has started, check at {anchorUtc.ToLocalTime()}.");
					} else {
						anchorUtc = DateTime.UtcNow.AddMinutes(30);
						log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: no returning expedition known, fallback check at {anchorUtc.ToLocalTime()}.");
					}
				} else if (candidates.Count > 0) {
					var chosen = candidates[new Random().Next(candidates.Count)];
					anchorUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, (chosen.BackIn ?? 0) - 60));
					log(LogLevel.Information, LogSender.Tbot, $"GhostSleep: next Defender PASSIVE wake anchor on fleet {chosen.ID} (return in {chosen.BackIn}s) - check at {anchorUtc.ToLocalTime()}.");
				} else {
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

		public async void GhostSleepEscalate(bool active) { try {
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
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in GhostSleepEscalate: {asyncVoidEx}"); }
		}
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
		private readonly SemaphoreSlim _pauseLock = new SemaphoreSlim(1, 1);
		private readonly object _pauseSync = new();
		private volatile bool _pauseActive = false;
		private volatile bool _pauseStopsDefender = false;
		private volatile bool _pauseGateMode = false;
		public bool IsBotPaused => _pauseActive;
		public bool IsAsleep => _ghostSleeping || (userData != null && userData.isSleeping);

		public bool FastFarmBlocksNewTransports {
			get {
				try {
					var s = InstanceSettings;
					if (!SettingsService.IsSettingSet(s, "FastFarm"))
						return false;
					var f = s.FastFarm;
					if (!SettingsService.IsSettingSet(f, "Active") || !(bool) f.Active)
						return false;
					return !SettingsService.IsSettingSet(f, "OnlyWorker") || (bool) f.OnlyWorker;
				} catch (Exception) {
					return false;
				}
			}
		}

		public TimeSpan? PauseGateDelay(Feature feature) {
			if (!_pauseActive || !_pauseGateMode)
				return null;
			if (feature == Feature.SleepMode || (feature == Feature.Defender && !_pauseStopsDefender))
				return null;
			TimeSpan rest = _pauseUntilUtc - DateTime.UtcNow;
			if (rest <= TimeSpan.Zero)
				return null;
			return rest + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 60000));
		}
		private DateTime _pauseUntilUtc = DateTime.MinValue;
		private readonly HashSet<Feature> _pauseStoppedFeatures = new();
		private string _pauseRejectText = null;
		private DateTime _pauseRejectUtc = DateTime.MinValue;

		private BotPauseConfig ReadBotPauseConfig() {
			BotPauseConfig c = new();
			try {
				var g = InstanceSettings;
				if (!SettingsService.IsSettingSet(g, "Experimental") || !SettingsService.IsSettingSet(g.Experimental, "BotPause"))
					return c;
				var p = g.Experimental.BotPause;
				if (SettingsService.IsSettingSet(p, "Active"))
					c.Active = (bool) p.Active;
				if (SettingsService.IsSettingSet(p, "DurationMinutes"))
					c.DurationMinutes = Math.Max(1, (int) p.DurationMinutes);
				if (SettingsService.IsSettingSet(p, "RecallMarginMinutes"))
					c.RecallMarginMinutes = Math.Max(0, (int) p.RecallMarginMinutes);
				if (SettingsService.IsSettingSet(p, "MaxDurationMinutes"))
					c.MaxDurationMinutes = Math.Max(c.DurationMinutes, (int) p.MaxDurationMinutes);
				if (SettingsService.IsSettingSet(p, "StopDefender"))
					c.StopDefender = (bool) p.StopDefender;
				if (SettingsService.IsSettingSet(p, "AttackMarginMinutes"))
					c.AttackMarginMinutes = Math.Max(0, (int) p.AttackMarginMinutes);
				if (SettingsService.IsSettingSet(p, "Mode"))
					c.Mode = (string) p.Mode;
			} catch (Exception) {
			}
			if (c.MaxDurationMinutes < c.DurationMinutes)
				c.MaxDurationMinutes = c.DurationMinutes;
			return c;
		}

		private List<PendingRecall> ReadPendingRecalls() {
			var list = new List<PendingRecall>();
			try {
				string playerTag = UserData?.userInfo?.PlayerName ?? "unknown";
				string universeTag = UserData?.serverData?.Name ?? "unknown";
				string path = Path.Combine(AppContext.BaseDirectory, "data", $"pending_recalls_{playerTag}-{universeTag}.json");
				if (!File.Exists(path))
					return list;
				var settings = new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
				var arr = Newtonsoft.Json.JsonConvert.DeserializeObject<JArray>(File.ReadAllText(path), settings) ?? new JArray();
				foreach (var e in arr) {
					string raw = (string) e["RecallAtUtc"];
					if (string.IsNullOrEmpty(raw) || raw.Length < 19)
						continue;
					if (!DateTime.TryParse(raw.Substring(0, 19), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime at))
						continue;
					list.Add(new PendingRecall { At = at, Coordinate = (string) e["OriginCoordinate"] ?? "?" });
				}
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Pending recalls not readable: {e.Message}");
			}
			return list;
		}

		private void PublishStatusSoon() {
			_ = Task.Run(async () => {
				try { await WriteSleepStatusAsync(); } catch { }
			});
		}

		private (bool Ok, string Text) PauseRejected(string text) {
			Tbot.Workers.ExtraLog.Write(this, "BotPause", () => "Pause rejected: " + text);
			_pauseRejectText = text;
			_pauseRejectUtc = DateTime.UtcNow;
			log(LogLevel.Warning, LogSender.Tbot, text);
			PublishStatusSoon();
			return (false, text);
		}

		private void ArmPauseTimer(int minutes) {
			if (timers.TryGetValue("BotPauseTimer", out Timer old)) {
				old.Dispose();
				timers.Remove("BotPauseTimer");
			}
			timers.Add("BotPauseTimer", new Timer(HandlePauseTimer, null, (long) minutes * 60 * 1000, Timeout.Infinite));
		}

		private async void HandlePauseTimer(object state) { try {
			await ResumeBot("time is up");
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in HandlePauseTimer: {asyncVoidEx}"); }
		}

		public async Task<(bool Ok, string Text)> PauseBot(int? minutes = null) {
			await _pauseLock.WaitAsync();
			try {
				BotPauseConfig cfg = ReadBotPauseConfig();
				int min = BotPause.ClampMinutes(minutes, cfg);

				if (!cfg.Active && !_pauseActive)
					return PauseRejected("Pause rejected: the pause function is switched off (Experimental.BotPause.Active).");

				if (_pauseActive) {
					_pauseUntilUtc = DateTime.UtcNow.AddMinutes(min);
					ArmPauseTimer(min);
					log(LogLevel.Information, LogSender.Tbot, $"Pause prolonged: {min} min from now.");
					PublishStatusSoon();
					return (true, $"Pause prolonged: {min} min from now.");
				}
				if (_attackLockdownActive)
					return PauseRejected("Pause rejected: an attack lockdown is active.");
				if (_ghostSleeping || (userData != null && userData.isSleeping))
					return PauseRejected("Pause rejected: the bot is sleeping.");

				DateTime gameNow;
				try {
					gameNow = await _tbotOgameBridge.GetDateTime();
				} catch (Exception) {
					gameNow = DateTime.Now;
				}
				List<PendingRecall> recalls = ReadPendingRecalls();
				PendingRecall blocking = BotPause.FindBlockingRecall(recalls, gameNow, min, cfg.RecallMarginMinutes);
				if (blocking != null) {
					string text = BotPause.RejectionText(blocking, gameNow);
					int possible = BotPause.MaxPossibleMinutes(recalls, gameNow, cfg.RecallMarginMinutes);
					if (possible >= 1 && possible < min)
						text += $" A pause of up to {possible} min would fit.";
					return PauseRejected(text);
				}

				try {
					List<AttackerFleet> angriffe;
					try {
						angriffe = await _ogameService.GetAttacks() ?? new List<AttackerFleet>();
					} catch (Exception) {
						angriffe = GetAttackSnapshot(out DateTime _);
					}
					int? naechster = BotPause.FindBlockingAttackSeconds(angriffe.Select(a => a.ArriveIn), min, cfg.AttackMarginMinutes, cfg.StopDefender);
					if (naechster != null)
						return PauseRejected($"Pause rejected: an attack arrives in {Math.Max(1, naechster.Value / 60)} min (limit {cfg.AttackMarginMinutes} min" + (cfg.StopDefender ? " plus the pause length, the Defender would be stopped" : "") + ").");
				} catch (Exception) {
				}

				var toStop = Features.AllFeatures
					.Where(f => (f != Feature.Defender || cfg.StopDefender) && f != Feature.SleepMode && IsFeatureRunning(f))
					.ToList();
				_pauseStopsDefender = cfg.StopDefender;
				if (cfg.GateMode) {
					lock (_pauseSync) {
						_pauseStoppedFeatures.Clear();
					}
					_pauseGateMode = true;
					_pauseActive = true;
					_pauseUntilUtc = DateTime.UtcNow.AddMinutes(min);
					_pauseRejectText = null;
					Tbot.Workers.ExtraLog.Write(this, "BotPause", () => $"Pause started (gate): {min} min, stopDefender={cfg.StopDefender}, attackMargin={cfg.AttackMarginMinutes} min, holding [{string.Join(", ", toStop)}].");
					ArmPauseTimer(min);
					log(LogLevel.Warning, LogSender.Tbot, $"Pause started: {toStop.Count} workers hold their cycles for {min} min ({(cfg.StopDefender ? "SleepMode and recalls" : "Defender, SleepMode and recalls")} keep running; scouting, attack and expedition missions are blocked). Ends on its own or with Resume.");
					PublishStatusSoon();
					return (true, $"Pause started for {min} min: {toStop.Count} workers hold their cycles. {(cfg.StopDefender ? "Recalls keep running" : "Defender and recalls keep running")}.");
				}
				_pauseGateMode = false;
				lock (_pauseSync) {
					_pauseStoppedFeatures.Clear();
					foreach (var f in toStop)
						_pauseStoppedFeatures.Add(f);
				}
				_pauseActive = true;
				_pauseUntilUtc = DateTime.UtcNow.AddMinutes(min);
				_pauseRejectText = null;
				Tbot.Workers.ExtraLog.Write(this, "BotPause", () => $"Pause started: {min} min, stopDefender={cfg.StopDefender}, attackMargin={cfg.AttackMarginMinutes} min, stopping [{string.Join(", ", toStop)}].");
				foreach (var feat in toStop) {
					try {
						await StopFeature(feat);
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"Pause: could not stop {feat}: {e.Message}");
					}
				}
				ArmPauseTimer(min);
				log(LogLevel.Warning, LogSender.Tbot, $"Pause started: {toStop.Count} workers stopped for {min} min ({(cfg.StopDefender ? "SleepMode and recalls" : "Defender, SleepMode and recalls")} keep running; scouting, attack and expedition missions are blocked). Ends on its own or with Resume.");
				PublishStatusSoon();
				return (true, $"Pause started for {min} min: {toStop.Count} workers stopped. {(cfg.StopDefender ? "Recalls keep running" : "Defender and recalls keep running")}.");
			} finally {
				_pauseLock.Release();
			}
		}

		public async Task<(bool Ok, string Text)> ResumeBot(string reason = "manual") {
			await _pauseLock.WaitAsync();
			try {
				if (!_pauseActive)
					return (false, "The bot is not paused.");
				if (timers.TryGetValue("BotPauseTimer", out Timer t)) {
					t.Dispose();
					timers.Remove("BotPauseTimer");
				}
				bool warTor = _pauseGateMode;
				_pauseActive = false;
				_pauseGateMode = false;
				_pauseStopsDefender = false;
				Tbot.Workers.ExtraLog.Write(this, "BotPause", () => $"Pause ended ({reason}).");
				if (warTor) {
					log(LogLevel.Information, LogSender.Tbot, $"Pause ended ({reason}): the workers continue on their own schedule.");
					PublishStatusSoon();
					return (true, $"Pause ended ({reason}): the workers continue on their own schedule.");
				}
				List<Feature> toStart;
				lock (_pauseSync) {
					toStart = _pauseStoppedFeatures.ToList();
					_pauseStoppedFeatures.Clear();
				}

				await _attackLockdownLock.WaitAsync();
				try {
					if (_attackLockdownActive) {
						foreach (var f in toStart)
							if (!_attackLockdownStoppedFeatures.Contains(f))
								_attackLockdownStoppedFeatures.Add(f);
						log(LogLevel.Information, LogSender.Tbot, $"Pause ended ({reason}); the attack lockdown keeps {toStart.Count} workers stopped until the attack wave is over.");
						PublishStatusSoon();
						return (true, "Pause ended; the attack lockdown keeps the workers stopped until the attack wave is over.");
					}
				} finally {
					_attackLockdownLock.Release();
				}

				if (_ghostSleeping) {
					foreach (var f in toStart)
						if (!_ghostSleepStoppedFeatures.Contains(f))
							_ghostSleepStoppedFeatures.Add(f);
					log(LogLevel.Information, LogSender.Tbot, $"Pause ended ({reason}); the bot is in GhostSleep, the workers start at wake-up.");
					PublishStatusSoon();
					return (true, "Pause ended; the workers start at wake-up (GhostSleep).");
				}
				if (userData != null && userData.isSleeping) {
					log(LogLevel.Information, LogSender.Tbot, $"Pause ended ({reason}); the bot is sleeping, the workers start at wake-up.");
					PublishStatusSoon();
					return (true, "Pause ended; the workers start at wake-up.");
				}

				int started = 0;
				foreach (var feat in toStart) {
					if (IsFeatureRunning(feat))
						continue;
					try {
						await InitializeFeature(feat);
						started++;
					} catch (Exception e) {
						log(LogLevel.Warning, LogSender.Tbot, $"Pause: could not restart {feat}: {e.Message}");
					}
				}
				log(LogLevel.Information, LogSender.Tbot, $"Pause ended ({reason}): {started} workers restarted.");
				PublishStatusSoon();
				return (true, $"Pause ended ({reason}): {started} workers restarted.");
			} finally {
				_pauseLock.Release();
			}
		}


		public void RemoveTelegramMessenger() {
			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing TelegramMessenger from current instance...");
				telegramMessenger = null;
			}
		}

		public async Task SendTelegramMessage(string fmt, bool mirrorToDiscord = true) {
			string finalStr = $"<code>[{userData?.userInfo?.PlayerName ?? "Unknown"}@{userData?.serverData?.Name ?? "Unknown"}]</code>\n" + fmt;
			if (telegramMessenger != null)
				await telegramMessenger.SendMessage(finalStr);
			if (mirrorToDiscord)
				await SendDiscordInternal(finalStr, NotificationKind.Information);
		}

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


		public string PreviewCollect(bool noLimit = false, string typFilter = null, bool skipIncoming = false) {
			return _fleetScheduler.PreviewCollect(noLimit, typFilter, skipIncoming);
		}

		public async Task TelegramCollect(bool noLimit = false, string typFilter = null, bool skipIncoming = false) {
			await SendTelegramMessage(typFilter == null ? "Collecting..." : $"Collecting ({typFilter}s only)...");
			await _fleetScheduler.Collect(noLimit, typFilter, skipIncoming);
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

		public async Task TelegramDebrisSave(long durationSeconds, Coordinate specific) {
			var all = userData.celestials.Unique().ToList();
			List<Celestial> targets = new();

			void AddMatch(int g, int sys, int pos, Celestials? type) {
				var match = all
					.Where(c => c.Coordinate.Galaxy == g && c.Coordinate.System == sys && c.Coordinate.Position == pos)
					.Where(c => c.Coordinate.Type == Celestials.Moon || c.Coordinate.Type == Celestials.Planet)
					.Where(c => type == null || c.Coordinate.Type == type)
					.OrderBy(c => c.Coordinate.Type == Celestials.Moon ? 0 : 1)
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
				celestial = attacked;
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

		public async void TelegramSetCurrentCelestial(Coordinate coord, string celestialType, Feature updateType = Feature.Null, bool editsettings = false) { try {
			userData.celestials = await _tbotOgameBridge.UpdateCelestials();

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
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in TelegramSetCurrentCelestial: {asyncVoidEx}"); }
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
				userData.isSleeping = true;
			}
		}
		private const int SleepStatusTickMs = 30 * 1000;

		private void InitializeSleepStatusWriter() {
			if (timers.TryGetValue("SleepStatusTimer", out Timer old)) {
				old.Dispose();
				timers.Remove("SleepStatusTimer");
			}
			timers.Add("SleepStatusTimer", new Timer(HandleSleepStatusWrite, null, 5000, SleepStatusTickMs));

			if (timers.TryGetValue("StartDataTimer", out Timer oldSd)) {
				oldSd.Dispose();
				timers.Remove("StartDataTimer");
			}
			timers.Add("StartDataTimer", new Timer(HandleStartDataRefresh, null, 12000, 60 * 1000));

			if (timers.TryGetValue("SlotCmdTimer", out Timer oldCmd)) {
				oldCmd.Dispose();
				timers.Remove("SlotCmdTimer");
			}
			timers.Add("SlotCmdTimer", new Timer(HandleSlotCmd, null, 7000, 3000));
		}

		private DateTime _suppressReloadUntilUtc = DateTime.MinValue;
		private int _slotCmdBusy = 0;

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

		private static readonly Dictionary<string, string> WorkerTogglePaths = new() {
			{ "w_autofarm", "AutoFarm.Active" },
			{ "w_expeditions", "Expeditions.Active" },
			{ "w_harvest", "AutoHarvest.Active" },
			{ "w_discovery", "AutoDiscovery.Active" },
			{ "w_brain", "Brain.Active" }
			,{ "w_defender", "Defender.Active" }
			,{ "w_transport", "Brain.Transports.Active" }
			,{ "w_repatriate", "Brain.AutoRepatriate.Active" }
			,{ "w_cargo", "Brain.AutoCargo.Active" }
			,{ "w_mine", "Brain.AutoMine.Active" }
			,{ "w_research", "Brain.AutoResearch.Active" }
			,{ "w_lfmine", "Brain.LifeformAutoMine.Active" }
			,{ "w_lfresearch", "Brain.LifeformAutoResearch.Active" }
			,{ "w_boosters", "Brain.KeepBoosters.Active" }
			,{ "w_offer", "Brain.BuyOfferOfTheDay.Active" }
			,{ "w_jumpgate", "Brain.AutoFleetJumpGate.Active" }
			,{ "w_colonize", "AutoColonize.Active" }
		};

		private static List<Feature> FeaturesForToggle(string key) {
			switch (key) {
				case "w_autofarm": return new List<Feature> { Feature.AutoFarm };
				case "w_expeditions": return new List<Feature> { Feature.Expeditions };
				case "w_harvest": return new List<Feature> { Feature.Harvest };
				case "w_discovery": return new List<Feature> { Feature.AutoDiscovery };
				case "w_defender": return new List<Feature> { Feature.Defender };
				case "w_repatriate": return new List<Feature> { Feature.BrainAutoRepatriate };
				case "w_cargo": return new List<Feature> { Feature.BrainAutobuildCargo };
				case "w_mine": return new List<Feature> { Feature.BrainAutoMine, Feature.BrainCelestialAutoMine };
				case "w_research": return new List<Feature> { Feature.BrainAutoResearch };
				case "w_lfmine": return new List<Feature> { Feature.BrainLifeformAutoMine, Feature.BrainCelestialLifeformAutoMine };
				case "w_lfresearch": return new List<Feature> { Feature.BrainLifeformAutoResearch, Feature.BrainCelestialLifeformAutoResearch };
				case "w_boosters": return new List<Feature> { Feature.BrainKeepBoosters };
				case "w_offer": return new List<Feature> { Feature.BrainOfferOfTheDay };
				case "w_jumpgate": return new List<Feature> { Feature.BrainAutoFleetJumpGate };
				case "w_colonize": return new List<Feature> { Feature.Colonize };
				case "w_transport": return new List<Feature>();
				case "w_brain": return Features.AllFeatures.Where(f => f.ToString().StartsWith("Brain")).ToList();
				default: return new List<Feature>();
			}
		}

		private bool? ReadBoolSetting(string path) {
			try {
				switch (path) {
					case "AutoFarm.Active": return (bool) InstanceSettings.AutoFarm.Active;
					case "Expeditions.Active": return (bool) InstanceSettings.Expeditions.Active;
					case "AutoHarvest.Active": return (bool) InstanceSettings.AutoHarvest.Active;
					case "AutoDiscovery.Active": return (bool) InstanceSettings.AutoDiscovery.Active;
					case "Defender.Active": return (bool) InstanceSettings.Defender.Active;
					case "Brain.Active": return (bool) InstanceSettings.Brain.Active;
					case "Brain.Transports.Active": return (bool) InstanceSettings.Brain.Transports.Active;
					case "Brain.AutoRepatriate.Active": return (bool) InstanceSettings.Brain.AutoRepatriate.Active;
					case "Brain.AutoCargo.Active": return (bool) InstanceSettings.Brain.AutoCargo.Active;
					case "Brain.AutoMine.Active": return (bool) InstanceSettings.Brain.AutoMine.Active;
					case "Brain.AutoResearch.Active": return (bool) InstanceSettings.Brain.AutoResearch.Active;
					case "Brain.LifeformAutoMine.Active": return (bool) InstanceSettings.Brain.LifeformAutoMine.Active;
					case "Brain.LifeformAutoResearch.Active": return (bool) InstanceSettings.Brain.LifeformAutoResearch.Active;
					case "Brain.KeepBoosters.Active": return (bool) InstanceSettings.Brain.KeepBoosters.Active;
					case "Brain.BuyOfferOfTheDay.Active": return (bool) InstanceSettings.Brain.BuyOfferOfTheDay.Active;
					case "Brain.AutoFleetJumpGate.Active": return (bool) InstanceSettings.Brain.AutoFleetJumpGate.Active;
					case "AutoColonize.Active": return (bool) InstanceSettings.AutoColonize.Active;
					default: return null;
				}
			} catch {
				return null;
			}
		}

		private bool SetBoolSettingInMemory(string path, bool v) {
			try {
				switch (path) {
					case "AutoFarm.Active": InstanceSettings.AutoFarm.Active = v; return true;
					case "Expeditions.Active": InstanceSettings.Expeditions.Active = v; return true;
					case "AutoHarvest.Active": InstanceSettings.AutoHarvest.Active = v; return true;
					case "AutoDiscovery.Active": InstanceSettings.AutoDiscovery.Active = v; return true;
					case "Defender.Active": InstanceSettings.Defender.Active = v; return true;
					case "Brain.Active": InstanceSettings.Brain.Active = v; return true;
					case "Brain.Transports.Active": InstanceSettings.Brain.Transports.Active = v; return true;
					case "Brain.AutoRepatriate.Active": InstanceSettings.Brain.AutoRepatriate.Active = v; return true;
					case "Brain.AutoCargo.Active": InstanceSettings.Brain.AutoCargo.Active = v; return true;
					case "Brain.AutoMine.Active": InstanceSettings.Brain.AutoMine.Active = v; return true;
					case "Brain.AutoResearch.Active": InstanceSettings.Brain.AutoResearch.Active = v; return true;
					case "Brain.LifeformAutoMine.Active": InstanceSettings.Brain.LifeformAutoMine.Active = v; return true;
					case "Brain.LifeformAutoResearch.Active": InstanceSettings.Brain.LifeformAutoResearch.Active = v; return true;
					case "Brain.KeepBoosters.Active": InstanceSettings.Brain.KeepBoosters.Active = v; return true;
					case "Brain.BuyOfferOfTheDay.Active": InstanceSettings.Brain.BuyOfferOfTheDay.Active = v; return true;
					case "Brain.AutoFleetJumpGate.Active": InstanceSettings.Brain.AutoFleetJumpGate.Active = v; return true;
					case "AutoColonize.Active": InstanceSettings.AutoColonize.Active = v; return true;
					default: return false;
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {path} cannot be set in memory: {e.Message}");
				return false;
			}
		}

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

		private async void HandleSlotCmd(object state) { try {
			if (Interlocked.Exchange(ref _slotCmdBusy, 1) == 1)
				return;
			try {
				string dir = AppContext.BaseDirectory;
				HandlePreviewRequests(dir);
				foreach (string file in Directory.GetFiles(dir, "slotcmd_*.json").OrderBy(f => f)) {
					try {
						JObject cmd = JObject.Parse(File.ReadAllText(file));
						File.Delete(file);
						string key = (string) cmd["key"] ?? "";
						int value = (int) cmd["value"];
						if (key == "pause") {
							if (value != 0)
								await PauseBot(null);
							else
								await ResumeBot("button");
							continue;
						}
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
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in HandleSlotCmd: {asyncVoidEx}"); }
		}

		private void HandlePreviewRequests(string dir) {
			try {
				foreach (string file in Directory.GetFiles(dir, "cmdpreview_*.json")) {
					try {
						JObject req = JObject.Parse(File.ReadAllText(file));
						string alias = (string) req["instance"] ?? "";
						if (!string.Equals(alias, InstanceAlias ?? "", StringComparison.OrdinalIgnoreCase)) {
							if ((DateTime.UtcNow - File.GetLastWriteTimeUtc(file)).TotalSeconds > 60)
								File.Delete(file);
							continue;
						}
						string id = (string) req["id"] ?? "";
						string text = PreviewForLine((string) req["line"] ?? "");
						if (id.Length == 0 || id.Any(ch => !char.IsLetterOrDigit(ch)))
							continue;
						string ziel = Path.Combine(dir, $"cmdresult_{id}.json");
						File.WriteAllText(ziel + ".tmp", new JObject { ["id"] = id, ["text"] = text }.ToString(Newtonsoft.Json.Formatting.None));
						File.Move(ziel + ".tmp", ziel, true);
						File.Delete(file);
					} catch (Exception e) {
						log(LogLevel.Debug, LogSender.Tbot, $"Command preview request {Path.GetFileName(file)} failed: {e.Message}");
					}
				}
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Command preview: directory not readable: {e.Message}");
			}
		}

		private string PreviewForLine(string line) {
			string[] t = line.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (t.Length == 0)
				return "";
			string cmd = t[0].Split('@')[0];
			if (cmd is "/collect" or "/collectall") {
				string filter = t.Skip(1).FirstOrDefault(x => x is "planet" or "moon");
				return PreviewCollect(cmd == "/collectall", filter, t.Skip(1).Contains("skip"));
			}
			return "";
		}

		private async Task ApplySlotSettingAsync(string path, int value) {
			int? old = ReadIntSetting(path);
			bool inMemory = SetIntSettingInMemory(path, value);

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
						if ((int) feat >= 100)
							continue;
						if (_attackLockdownActive && !_attackLockdownStoppedFeatures.Contains(feat))
							_attackLockdownStoppedFeatures.Add(feat);
						if (pausiert || (_ghostSleeping && _ghostSleepStoppedFeatures.Contains(feat))) {
							hinweis = " (starts only after sleep/lockdown)";
							continue;
						}
						try { await InitializeFeature(feat); } catch (Exception e) { log(LogLevel.Warning, LogSender.Tbot, $"Slot tile: {feat} could not be started: {e.Message}"); }
					} else {
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

		private int _startDataBusy = 0;

		private object _globalItems = null;
		private DateTime _globalItemsAt = DateTime.MinValue;
		private async Task RefreshGlobalItems(List<Celestial> list) {
			if ((DateTime.UtcNow - _globalItemsAt).TotalMinutes < 10)
				return;
			_globalItemsAt = DateTime.UtcNow;
			try {
				var planets = list.OfType<Planet>().Take(2).ToList();
				if (planets.Count == 0)
					return;
				var lists = new List<List<ActiveItem>>();
				foreach (var p in planets) {
					lists.Add(await _ogameService.GetActiveItems(p) ?? new List<ActiveItem>());
					await Task.Delay(1500);
				}
				var resourceBound = new System.Text.RegularExpressions.Regex("metal|metall|crystal|kristall|deuterium|energy|energie|crawler|raupe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				string KeyOf(ActiveItem i) => string.IsNullOrEmpty(i.Ref) ? i.Name : i.Ref;
				var items = lists[0]
					.Where(i => !resourceBound.IsMatch(i.Name ?? ""))
					.Where(i => lists.Skip(1).All(l => l.Any(x => KeyOf(x) == KeyOf(i))))
					.Select(i => new { name = i.Name, remainingSec = i.TimeRemaining, totalSec = i.TotalDuration, img = i.ImgSmall })
					.ToList();
				_globalItems = new { fetchedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), items };
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Global items not loaded: {e.Message}");
			}
		}

		private async Task FillTemplateCoordinatesAsync() {
			try {
				var planets = (userData?.celestials ?? new List<Celestial>()).Where(c => c.Coordinate.Type == Celestials.Planet).ToList();
				if (planets.Count == 0) return;
				var cfg = SettingsService.ReadResolvedObject(InstanceSettingsPath);
				var open = ConfigDoctor.FindTemplateCoordinates(cfg);
				if (open.Count == 0) return;
				Celestial home = planets.First();
				try {
					var hw = cfg["Brain"]?["AutoMine"]?["HomeWorld"];
					if (hw != null) {
						var match = planets.FirstOrDefault(c => (int) c.Coordinate.Galaxy == (int) hw["Galaxy"] && (int) c.Coordinate.System == (int) hw["System"] && (int) c.Coordinate.Position == (int) hw["Position"]);
						if (match != null) home = match;
					}
				} catch { }
				var changes = new List<KeyValuePair<string, object>>();
				foreach (var (path, hasType) in open) {
					changes.Add(new KeyValuePair<string, object>(path + ".Galaxy", (int) home.Coordinate.Galaxy));
					changes.Add(new KeyValuePair<string, object>(path + ".System", (int) home.Coordinate.System));
					changes.Add(new KeyValuePair<string, object>(path + ".Position", (int) home.Coordinate.Position));
					if (hasType) changes.Add(new KeyValuePair<string, object>(path + ".Type", "Planet"));
				}
				await SettingsService.SetValuesAsync(InstanceSettingsPath, changes);
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);
				log(LogLevel.Information, LogSender.Tbot, $"Config: template coordinates 1:1:1 replaced by the home planet {home.Coordinate} in: {string.Join(", ", open.Select(x => x.Path))}.");
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Tbot, $"Config: could not replace template coordinates 1:1:1: {e.Message}");
			}
		}

		private DateTime _configCheckAt = DateTime.MinValue;
		private string _configCheckHash = "";
		private void RunConfigDoctor(bool force) {
			try {
				if (!force && DateTime.UtcNow - _configCheckAt < TimeSpan.FromHours(1))
					return;
				_configCheckAt = DateTime.UtcNow;
				var cfg = SettingsService.ReadResolvedObject(InstanceSettingsPath);
				var owned = (userData?.celestials ?? new List<Celestial>())
					.Select(c => ConfigDoctor.CoordKey((int) c.Coordinate.Galaxy, (int) c.Coordinate.System, (int) c.Coordinate.Position, c.Coordinate.Type == Celestials.Moon ? "Moon" : "Planet")).ToList();
				var findings = ConfigDoctor.Check(cfg, owned);
				try {
					var ex = cfg["Expeditions"] as Newtonsoft.Json.Linq.JObject;
					var ms = ex?["ManualShips"] as Newtonsoft.Json.Linq.JObject;
					var msh = ms?["Ships"] as Newtonsoft.Json.Linq.JObject;
					long needed = userData?.ExpoLargeCargoNeeded ?? 0;
					if (needed > 0 && msh != null && ((bool?) ex?["Active"] ?? false) && ((bool?) ms?["Active"] ?? false)) {
						long gt = (long?) msh["LargeCargo"] ?? 0;
						bool otherCargo = new[] { "SmallCargo", "Pathfinder", "Recycler" }.Any(s => ((long?) msh[s] ?? 0) > 0);
						if (gt < needed && !otherCargo)
							findings.Add(new ConfigFinding { Severity = "warn", Path = "Expeditions.ManualShips.Ships.LargeCargo", Message = $"Manual expedition fleet has {gt:N0} large cargo, but one expedition needs {needed:N0} to carry the maximum find (lifeform find bonus included). Part of a big find stays behind." });
					}
				} catch (Exception e) {
					log(LogLevel.Debug, LogSender.Tbot, $"Config check (manual ships): {e.Message}");
				}
				string hash = string.Join("|", findings.Select(x => x.ToString()));
				if (hash != _configCheckHash) {
					_configCheckHash = hash;
					if (findings.Count == 0)
						log(LogLevel.Information, LogSender.Tbot, "Config check: no problems found.");
					foreach (var x in findings)
						log(x.Severity == "info" ? LogLevel.Information : LogLevel.Warning, LogSender.Tbot, $"Config check ({x.Severity}) {x.Path}: {x.Message}");
				}
				string dir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dir);
				var tips = new List<ConfigFinding>();
				var known = DoctorKnown.Read(InstanceAlias);
				tips = tips.Where(t => !known.Contains(t.Key)).ToList();
				var all = findings.Concat(tips).ToList();
				long startMs = new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime).ToUnixTimeMilliseconds();
				var file = new { updatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), startMs, findings = all.Select(x => new { severity = x.Severity, path = x.Path, message = x.Message, key = x.Key }).ToList() };
				AtomicFile.WriteAllText(Path.Combine(dir, Path.GetFileName($"config_check_{InstanceAlias}.json")), Newtonsoft.Json.JsonConvert.SerializeObject(file));
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Config check failed: {e.Message}");
			}
		}

		private async void HandleStartDataRefresh(object state) { try {
			if (Interlocked.Exchange(ref _startDataBusy, 1) == 1)
				return;
			try {
				if (_ghostSleeping || _pauseActive || userData == null || userData.isSleeping || userData.celestials == null)
					return;
				var list = userData.celestials.Unique().ToList();
				bool Stale(Celestial c, string kind, bool hasData) => !hasData || StartDataFreshness.AgeMinutes(c, kind) >= 10;
				var todo = list
					.Select(c => new {
						c,
						res = Stale(c, StartDataFreshness.Resources, c.Resources != null),
						ships = Stale(c, StartDataFreshness.Ships, c.Ships != null),
						prod = c is Planet && Stale(c, StartDataFreshness.Production, c.ResourcesProduction != null),
						age = Math.Max(StartDataFreshness.AgeMinutes(c, StartDataFreshness.Resources), StartDataFreshness.AgeMinutes(c, StartDataFreshness.Ships))
					})
					.Where(x => x.res || x.ships || x.prod)
					.OrderByDescending(x => x.age)
					.ToList();
				int done = 0;
				foreach (var x in todo) {
					var c = x.c;
					string key = c.Coordinate.ToString();
					try {
						if (x.res) await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Resources);
						if (x.ships) await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
						if (x.prod) await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.ResourcesProduction);
					} catch (Exception e) {
						log(LogLevel.Debug, LogSender.Tbot, $"Start page data for {key} not loaded: {e.Message}");
					}
					if (++done % 4 == 0 && done < todo.Count)
						await WriteSleepStatusAsync();
					await Task.Delay(1500);
				}
				RunConfigDoctor(false);
				await RefreshGlobalItems(list);
				await WriteSleepStatusAsync();
			} finally {
				Interlocked.Exchange(ref _startDataBusy, 0);
			}
		} catch (Exception ex) { log(LogLevel.Debug, LogSender.Tbot, $"Start page refresh: {ex.Message}"); }
		}

		private async void HandleSleepStatusWrite(object state) { try {
			try {
				await WriteSleepStatusAsync();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Sleep status could not be written: {e.Message}");
			}
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in HandleSleepStatusWrite: {asyncVoidEx}"); }
		}

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

		private static double SecondsUntilNext(DateTime nowGame, DateTime timeOfDay) {
			DateTime next = nowGame.Date.Add(timeOfDay.TimeOfDay);
			if (next <= nowGame)
				next = next.AddDays(1);
			return (next - nowGame).TotalSeconds;
		}

		private readonly object _attackSnapshotLock = new();
		private List<AttackerFleet> _attackSnapshot = new();
		private DateTime _attackSnapshotFetchedUtc = DateTime.MinValue;

		public List<AttackerFleet> GetAttackSnapshot(out DateTime fetchedUtc) {
			lock (_attackSnapshotLock) {
				fetchedUtc = _attackSnapshotFetchedUtc;
				return _attackSnapshot?.ToList() ?? new List<AttackerFleet>();
			}
		}

		public void PublishAttackSnapshot(List<AttackerFleet> attacks, DateTime fetchedUtc) {
			lock (_attackSnapshotLock) {
				_attackSnapshot = attacks?.Where(a => a != null).ToList() ?? new List<AttackerFleet>();
				_attackSnapshotFetchedUtc = fetchedUtc;
			}
			_ = Task.Run(async () => {
				try { await WriteSleepStatusAsync(); } catch { }
			});
		}

		private TimeSpan? _gameClockOffset;

		private async Task<DateTime> GameNowQuiet() {
			if (IsAsleep && _gameClockOffset.HasValue)
				return DateTime.Now + _gameClockOffset.Value;
			DateTime gameNow = await _tbotOgameBridge.GetDateTime();
			_gameClockOffset = gameNow - DateTime.Now;
			return gameNow;
		}

		private async Task WriteSleepStatusAsync() {
			DateTime gameNow;
			try {
				gameNow = await GameNowQuiet();
			} catch {
				gameNow = DateTime.Now;
			}

			bool ghost = _ghostSleeping;
			bool normal = userData != null && userData.isSleeping;
			bool sleeping = ghost || normal;

			var recalls = new List<object>();
			int more = 0;
			try {
				var list = ReadPendingRecalls()
					.Where(r => r.At >= gameNow.AddMinutes(-10))
					.OrderBy(r => r.At)
					.ToList();
				more = Math.Max(0, list.Count - 6);
				foreach (var x in list.Take(6))
					recalls.Add(new { coord = x.Coordinate, at = x.At.ToString("HH:mm"), inSec = (long) Math.Max(0, (x.At - gameNow).TotalSeconds) });
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Pending recalls for the status tile not readable: {e.Message}");
			}

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
				defender = ReadBoolSetting("Defender.Active"),
				brain = ReadBoolSetting("Brain.Active"),
				transport = ReadBoolSetting("Brain.Transports.Active"),
				repatriate = ReadBoolSetting("Brain.AutoRepatriate.Active"),
				cargo = ReadBoolSetting("Brain.AutoCargo.Active"),
				mine = ReadBoolSetting("Brain.AutoMine.Active"),
				research = ReadBoolSetting("Brain.AutoResearch.Active"),
				lfmine = ReadBoolSetting("Brain.LifeformAutoMine.Active"),
				lfresearch = ReadBoolSetting("Brain.LifeformAutoResearch.Active"),
				boosters = ReadBoolSetting("Brain.KeepBoosters.Active"),
				offer = ReadBoolSetting("Brain.BuyOfferOfTheDay.Active"),
				jumpgate = ReadBoolSetting("Brain.AutoFleetJumpGate.Active"),
				colonize = ReadBoolSetting("AutoColonize.Active")
			};

			object modeOverride = null;
#if FASTFARM
			try {
				if (SettingsService.IsSettingSet(InstanceSettings, "FastFarm") && SettingsService.IsSettingSet(InstanceSettings.FastFarm, "Active") && (bool) InstanceSettings.FastFarm.Active
					&& !(SettingsService.IsSettingSet(InstanceSettings.FastFarm, "OnlyWorker") && !(bool) InstanceSettings.FastFarm.OnlyWorker))
					modeOverride = new { name = "FastFarm" };
			} catch { modeOverride = null; }
#endif

			object account = null;
			try {
				var ui = userData?.userInfo;
				var cel = userData?.celestials;
				if (ui != null) {
					long m = 0, k = 0, d = 0, dm = 0;
					long pm = 0, pk = 0, pd = 0;
					int planets = 0, moons = 0, withResources = 0, withProd = 0;
					if (cel != null) {
						foreach (var c in cel) {
							if (c?.Coordinate?.Type == Celestials.Moon) moons++; else planets++;
							if (c?.ResourcesProduction != null) {
								withProd++;
								pm += c.ResourcesProduction.Metal?.CurrentProduction ?? 0;
								pk += c.ResourcesProduction.Crystal?.CurrentProduction ?? 0;
								pd += c.ResourcesProduction.Deuterium?.CurrentProduction ?? 0;
							}
							if (c?.Resources != null) {
								withResources++;
								m += c.Resources.Metal; k += c.Resources.Crystal; d += c.Resources.Deuterium;
								dm = Math.Max(dm, c.Resources.Darkmatter);
							}
						}
					}
					account = new {
						name = ui.PlayerName,
						universe = userData?.serverData?.Name,
						playerClass = ui.Class.ToString(),
						rank = ui.Rank, total = ui.Total, points = ui.Points,
						planets, moons,
						prodPerHour = withProd > 0 ? new { metal = pm, crystal = pk, deuterium = pd, planets = withProd } : null,
						metal = withResources > 0 ? (long?) m : null, crystal = withResources > 0 ? (long?) k : null,
						deuterium = withResources > 0 ? (long?) d : null, darkmatter = withResources > 0 ? (long?) dm : null,
						artifacts = userData.artifactsCollected >= 0 ? (long?) userData.artifactsCollected : null,
						artifactsLimit = userData.artifactsCollected >= 0 ? (long?) userData.artifactsLimit : null
					};
				}
			} catch { account = null; }

			object fleetStock = null;
			try {
				var celestials = userData?.celestials;
				if (celestials != null) {
					var totals = new Dictionary<string, long>();
					int known = 0;
					long points = 0;
					foreach (var c in celestials) {
						if (c?.Ships == null) continue;
						known++;
						points += c.Ships.GetFleetPoints();
						foreach (var prop in typeof(Ships).GetProperties()) {
							if (prop.PropertyType != typeof(long) || prop.Name == "SolarSatellite" || prop.Name == "Crawler") continue;
							long n = (long) prop.GetValue(c.Ships);
							if (n > 0) totals[prop.Name] = (totals.TryGetValue(prop.Name, out long old) ? old : 0) + n;
						}
					}
					if (known > 0)
						fleetStock = new { ships = totals.OrderByDescending(t => t.Value).Select(t => new { type = t.Key, count = t.Value }).ToList(), celestials = known, of = celestials.Count, points };
				}
			} catch { fleetStock = null; }

			object fleetsRunning = null;
			try {
				var fl = userData?.fleets;
				if (fl != null) {
					var byMission = fl.GroupBy(f => f.Mission.ToString())
						.Select(g => new { mission = g.Key, outbound = g.Count(f => !f.ReturnFlight), back = g.Count(f => f.ReturnFlight) })
						.OrderByDescending(g => g.outbound + g.back).ToList();
					var next = fl.OrderBy(f => f.ArriveIn).Take(6)
						.Select(f => new {
							mission = f.Mission.ToString(), back = f.ReturnFlight,
							origin = f.Origin?.ToString(), dest = f.Destination?.ToString(),
							inSec = (long) Math.Max(0, f.ArriveIn),
							ships = f.Ships?.GetFleetPoints() ?? 0
						}).ToList();
					fleetsRunning = new { total = fl.Count, byMission, next, more = Math.Max(0, fl.Count - 6) };
				}
			} catch { fleetsRunning = null; }

			object fleetsAnim = null;
			try {
				var fa = userData?.fleets;
				if (fa != null) {
					fleetsAnim = fa.Take(40).Select(f => {
						DateTime? endTime = f.ReturnFlight ? f.BackTime : f.ArrivalTime;
						double legSec = (f.ArrivalTime - f.StartTime).TotalSeconds;
						bool legOk = f.StartTime.Year > 2000 && legSec >= 5 && legSec <= 3 * 86400;
						long endMs = endTime.HasValue && endTime.Value.Year > 2000 ? new DateTimeOffset(endTime.Value).ToUnixTimeMilliseconds() : 0L;
						long rem = f.ReturnFlight ? (f.BackIn ?? 0) : f.ArriveIn;
						return (object) new {
							id = f.ID,
							m = f.Mission.ToString(),
							b = f.ReturnFlight,
							a = f.Mission == Missions.Attack || f.Mission == Missions.FederalAttack || f.Mission == Missions.Destroy || f.Mission == Missions.MissileAttack,
							end = endMs,
							tot = legOk ? (long) Math.Round(legSec) : Math.Max(5L, rem),
							rem = Math.Max(0L, rem)
						};
					}).ToList();
				}
			} catch { fleetsAnim = null; }

			var pauseInfo = new {
				active = _pauseActive,
				untilInSec = _pauseActive ? (long) Math.Max(0, (_pauseUntilUtc - DateTime.UtcNow).TotalSeconds) : 0L,
				reject = (!_pauseActive && _pauseRejectText != null && (DateTime.UtcNow - _pauseRejectUtc).TotalSeconds < 90) ? _pauseRejectText : null,
				defaultMinutes = ReadBotPauseConfig().DurationMinutes
			};

			object clock = null;
			try {
				if (gameNow.Year >= 2000) {
					int offsetMin = (int) (Math.Round((gameNow - DateTime.UtcNow).TotalMinutes / 15.0) * 15);
					clock = new {
						game = gameNow.ToString("yyyy-MM-dd HH:mm:ss"),
						offsetMin,
						systemZone = TimeZoneInfo.Local.Id,
						systemOffsetMin = (int) TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes
					};
				}
			} catch { clock = null; }

			object workerRuns = null;
			try {
				workerRuns = workers.Select(kv => {
					var wb = kv.Value as WorkerBase;
					if (wb == null || !wb.IsWorkerRunning()) return null;
					var next = wb.NextRunUtc; var last = wb.LastRunUtc;
					return new {
						name = kv.Key.ToString(),
						busy = wb.IsBusy,
						nextInSec = next.HasValue ? (long?) Math.Max(0, (long) (next.Value - DateTime.UtcNow).TotalSeconds) : null,
						lastAgoSec = last.HasValue ? (long?) Math.Max(0, (long) (DateTime.UtcNow - last.Value).TotalSeconds) : null,
						periodSec = wb.Period == System.Threading.Timeout.InfiniteTimeSpan ? null : (long?) wb.Period.TotalSeconds
					};
				}).Where(x => x != null).OrderBy(x => x.name).ToList();
			} catch { workerRuns = null; }

			var payload = new {
				sleeping,
				clock,
				mode = ghost ? "GhostSleep" : (normal ? "Sleep" : ""),
				pause = pauseInfo,
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
				workerRuns,
				globalItems = _globalItems,
				priorities,
				modeOverride,
				account,
				fleetStock,
				fleetsRunning,
				fleetsAnim,
				lang = UiLanguage.Read(),
				updatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
			};

			string target = Path.Combine(AppContext.BaseDirectory, "sleepstatus.json");
			AtomicFile.WriteAllText(target, Newtonsoft.Json.JsonConvert.SerializeObject(payload));

			try {
				string dataDir = Path.Combine(AppContext.BaseDirectory, "data");
				Directory.CreateDirectory(dataDir);
				string perInstance = Path.Combine(dataDir, Path.GetFileName($"status_{InstanceAlias}.json"));
				File.Copy(target, perInstance, true);
			} catch {  }
		}

		private async void HandleSleepMode(object state) { try {
			await HandleSleepModeAsync(state);
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in HandleSleepMode: {asyncVoidEx}"); }
		}
		private async Task HandleSleepModeAsync(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value)) {
				return;
			}

			try {

				DateTime time = await _tbotOgameBridge.GetDateTime();

				if (time.Year < 2000) {
					long retryMs = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					log(LogLevel.Warning, LogSender.SleepMode, "Server time not available (ogamed not reachable or logged out) - checking sleep mode again shortly.");
					timers.GetValueOrDefault("SleepModeTimer").Change(retryMs, Timeout.Infinite);
					return;
				}

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
								interval = (long) wakeUp.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await GoToSleepAsync(newTime);
							} else {
								interval = (long) goToSleep.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							}
						} else {
							if (goToSleep >= wakeUp) {
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							} else {
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
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							} else {
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							}
						} else {
							if (goToSleep >= wakeUp) {
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await GoToSleepAsync(newTime);
							} else {
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
				if (time.Year < 2000)
					time = DateTime.Now;
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} finally {
			}
		}

		private async void GoToSleep(object state) { try {
			await GoToSleepAsync(state);
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in GoToSleep: {asyncVoidEx}"); }
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
						tempFleets.AddRange(userData.fleets
							.Where(f => f.Mission == Missions.Deploy)
							.Where(f => f.ArriveIn <= timeToWakeup)
						);
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


		public async void WakeUpNow(object state) { try {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("TelegramSleepModeTimer");
			await SendTelegramMessage($"Bot woke up!");

			log(LogLevel.Information, LogSender.SleepMode, "Bot woke up!");

			if (userData.isSleeping) {
				userData.isSleeping = false;
			}
			InitializeFeatures();
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in WakeUpNow: {asyncVoidEx}"); }
		}

		private async void WakeUp(object state) { try {
			await WakeUpAsync(state);
		} catch (Exception asyncVoidEx) { log(LogLevel.Error, LogSender.Tbot, $"Unhandled error in WakeUp: {asyncVoidEx}"); }
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