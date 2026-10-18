using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Tbot.Includes;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using TBot.Ogame.Infrastructure;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Exceptions;
using Tbot.Models;
using System.Collections;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Common.Settings;

namespace Tbot.Services {
	internal class InitInstanceData {
		public string Alias;
		public string SettingsPath;

		public InitInstanceData(string alias, string setPath) {
			Alias = alias;
			SettingsPath = setPath;
		}
	}
	internal class InstanceManager : IInstanceManager {
		public string SettingsAbsoluteFilepath { get; set; } = "";
		static dynamic _mainSettings;

		private readonly IOgameService _ogameService;
		private readonly ILoggerService<InstanceManager> _logger;
		private readonly ILoggerService<TelegramMessenger> _telegramLogger;
		private readonly IServiceScopeFactory _scopeFactory;
		private readonly ICalculationService _helpersService;

		static ITelegramMessenger telegramMessenger = null;
		static SemaphoreSlim instancesSem = new SemaphoreSlim(1, 1);
		static List<TbotInstanceData> instances = new();
		static SettingsFileWatcher settingsWatcher = null;
		public InstanceManager(ILoggerService<InstanceManager> logger,
			IOgameService ogameService,
			IServiceScopeFactory serviceScopeFactory,
			ILoggerService<TelegramMessenger> telegramLogger,
			ICalculationService helpersService) {
			_logger = logger;
			_ogameService = ogameService;
			_scopeFactory = serviceScopeFactory;
			_telegramLogger = telegramLogger;
			_helpersService = helpersService;
		}

		public async void OnSettingsChanged() {
			await instancesSem.WaitAsync();
			try {
				await OnSettingsChangedLocked();
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"Error while applying settings change: {e}");
			} finally {
				instancesSem.Release();
			}
		}

		private async Task OnSettingsChangedLocked() {
			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Reading settings \"{SettingsAbsoluteFilepath}\"");

			_mainSettings = await SettingsService.GetSettings(SettingsAbsoluteFilepath);

			await InitializeTelegramMessenger();

			SettingsVersion settingVersion = SettingsVersion.Invalid;

			List<TbotInstanceData> newInstances = new();
			List<Task<TbotInstanceData>> awaitingInstances = new();
			List<Task> deinitingInstances = new();
			List<InitInstanceData> instancesToBeInited = new();

			if (SettingsService.IsSettingSet(_mainSettings, "Instances") == false) {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "Single instance settings detected");
				settingVersion = SettingsVersion.AllInOne;

				if (instances.Any(c => c._botSettingsPath == SettingsAbsoluteFilepath) == true) {
					var foundInstance = instances.First(c => c._botSettingsPath == SettingsAbsoluteFilepath);

					newInstances.Add(foundInstance);
				} else {
					instancesToBeInited.Add(new InitInstanceData("MAIN", SettingsAbsoluteFilepath));
				}
			} else {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "Multiples instances settings detected");
				settingVersion = SettingsVersion.MultipleInstances;

				ICollection json_instances = _mainSettings.Instances;
				List<InitInstanceData> uniqueInstances = new();

				foreach (var instance in _mainSettings.Instances) {
					string cInstanceSettingPath = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(SettingsAbsoluteFilepath), instance.Settings)).FullName;
					string alias = instance.Alias;

					if(uniqueInstances.Any(c => c.SettingsPath == cInstanceSettingPath) == false) {
						uniqueInstances.Add(new InitInstanceData(alias, cInstanceSettingPath));
					}
				}

				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Read {json_instances.Count} instances. Unique instaces #{uniqueInstances.Count}");

				foreach (var instance in uniqueInstances) {
					string cInstanceSettingPath = instance.SettingsPath;
					string alias = instance.Alias;

					if (instances.Any(c => c._botSettingsPath == cInstanceSettingPath) == true) {
						_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Instance \"{alias}\" \"{cInstanceSettingPath}\" already inited.");
						var foundInstance = instances.First(c => c._botSettingsPath == cInstanceSettingPath);
						foundInstance._alias = alias;

						newInstances.Add(foundInstance);
					} else {
						_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Enqueueing initialization of instance \"{alias}\" \"{cInstanceSettingPath}\"");
						instancesToBeInited.Add(instance);
					}
				}
			}

			foreach (var deInstance in instances) {
				if (newInstances.Any(c => string.Compare(c._botSettingsPath, deInstance._botSettingsPath) == 0) == false) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Deinitializing instance \"{deInstance._alias}\" \"{deInstance._botSettingsPath}\"");

					deinitingInstances.Add(deInstance._botMain.DisposeAsync().AsTask());
				}
			}
			await Task.WhenAll(deinitingInstances);

			for (int initIndex = 0; initIndex < instancesToBeInited.Count; initIndex++) {
				var instanceToBeInited = instancesToBeInited[initIndex];
				string cInstanceSettingPath = instanceToBeInited.SettingsPath;
				string alias = instanceToBeInited.Alias;
				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Asynchronously initializing instance \"{alias}\" \"{cInstanceSettingPath}\"");
				awaitingInstances.Add(StartTBotMain(cInstanceSettingPath, alias));
				if (initIndex < instancesToBeInited.Count - 1) {
					int millisecondsTimeout = Random.Shared.Next(30, 60) * 1000;
					await Task.Delay(millisecondsTimeout);
				}
			}

			foreach (Task<TbotInstanceData> awaitingInstance in awaitingInstances) {
				try {
					TbotInstanceData uniqueInstance = await awaitingInstance;
					if (uniqueInstance != null) {
						newInstances.Add(uniqueInstance);
					}
				} catch (MissingConfigurationException ) {
					continue;
				} catch (AccountInVacationModeException) {
					continue;
				} catch (UnableToLoginException) {
					continue;
				}
			}

			instances.Clear();
			instances = newInstances.ToList();

			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Instances stats: Initialized {instances.Count} - Deinitialized {deinitingInstances.Count}");

			if (settingsWatcher == null)
				settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, SettingsAbsoluteFilepath);
		}

		public async ValueTask DisposeAsync() {
			List<Task> deinitTasks = new();
			foreach (var instance in instances) {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Deinitializing instance \"{instance._alias}\" \"{instance._botSettingsPath}\"");
				deinitTasks.Add(instance.Deinitialize());
			}
			await Task.WhenAll(deinitTasks);
			instances.Clear();
		}

		private async Task InitializeTelegramMessenger() {
			if (
				SettingsService.IsSettingSet(_mainSettings, "TelegramMessenger") &&
				SettingsService.IsSettingSet(_mainSettings.TelegramMessenger, "Active") &&
				(bool) _mainSettings.TelegramMessenger.Active
			) {
				if (telegramMessenger == null) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Activating Telegram Messenger");
					var logEnabled = SettingsService.IsSettingSet(_mainSettings.TelegramMessenger, "Logging") && (bool) _mainSettings.TelegramMessenger.Logging;

					telegramMessenger = new TelegramMessenger(_telegramLogger, _helpersService,
						(string) _mainSettings.TelegramMessenger.API, (string) _mainSettings.TelegramMessenger.ChatId, logEnabled);
					await telegramMessenger.TelegramBot();
				}

				if (
					SettingsService.IsSettingSet(_mainSettings.TelegramMessenger, "TelegramAutoPing") &&
					SettingsService.IsSettingSet(_mainSettings.TelegramMessenger.TelegramAutoPing, "Active") &&
					SettingsService.IsSettingSet(_mainSettings.TelegramMessenger.TelegramAutoPing, "EveryHours") &&
					(bool) _mainSettings.TelegramMessenger.TelegramAutoPing.Active
				) {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing is enabled!");
					long everyHours = (long) _mainSettings.TelegramMessenger.TelegramAutoPing.EveryHours;
					if (everyHours == 0) {
						_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing EveryHours is 0. Setting to 1 hour.");
						everyHours = 1;
					}

					telegramMessenger.StartAutoPing(everyHours);
				} else {
					_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger AutoPing disabled.");
				}
			} else {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "Telegram Messenger disabled");

				if (telegramMessenger != null) {
					await telegramMessenger.TelegramBotDisable();
					telegramMessenger = null;
				}
			}
		}


		private async Task<TbotInstanceData> StartTBotMain(string settingsPath, string alias) {
			_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Initializing instance \"{alias}\" \"{settingsPath}\"");

			try {
				if (File.Exists(settingsPath) == false) {
					_logger.WriteLog(LogLevel.Warning, LogSender.Main, $"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
					throw new MissingConfigurationException($"Instance \"{alias}\" cannot be initialized. \"{settingsPath}\" does not exist");
				} else {

					var scope = _scopeFactory.CreateScope();
					var tBotInstance = scope.ServiceProvider.GetRequiredService<ITBotMain>();

					await tBotInstance.Init(settingsPath, alias, telegramMessenger);

					_logger.WriteLog(LogLevel.Information, LogSender.Main, $"Instance \"{alias}\" initialized successfully!");
					tBotInstance.OnError += TBotInstance_OnError;

					var instance = new TbotInstanceData(tBotInstance, scope, settingsPath, alias);
					return instance;
				}
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"Error initializing instance \"{alias}\": {e.Message}");
				throw;
			}
		}

		private async void TBotInstance_OnError(object sender, EventArgs e) { try {
			ITBotMain tbotInstance = (ITBotMain) sender;
			var alias = tbotInstance.InstanceAlias;
			_logger.WriteLog(LogLevel.Error, LogSender.Main, $"Error on tboInstance instance \"{alias}\"");
			try {
				TbotInstanceData fInstance = instances.First(c => c._botMain.InstanceAlias == alias);
				ITBotMain tbotToDispose = fInstance._botMain;

				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"Removing TBot instance \"{alias}\"");

				instances.Remove(fInstance);
				await tbotToDispose.DisposeAsync();
			} catch(InvalidOperationException) {
			}
		} catch (Exception asyncVoidEx) { _logger.WriteLog(LogLevel.Error, LogSender.Main, $"Unhandled error in TBotInstance_OnError: {asyncVoidEx}"); }
		}
	}
}
