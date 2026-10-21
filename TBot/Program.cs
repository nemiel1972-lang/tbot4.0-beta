using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Serilog;
using Tbot.Common.Settings;
using Tbot.Exceptions;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Hubs;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.WebUI;

namespace Tbot {

	class Program {
		private static ILoggerService<Program> _logger;
		private static IInstanceManager _instanceManager;
		static DateTime startTime = DateTime.UtcNow;

		static void Main(string[] args) {
			MainAsync(args).Wait();
		}
		static async Task MainAsync(string[] args) {

			var serviceCollection = WebApp.GetServiceCollection()
				.AddSingleton(typeof(ILoggerService<>), typeof(LoggerService<>))
				.AddScoped<ICalculationService, CalculationService>()
				.AddScoped<IOgameService, OgameService>()
				.AddScoped<ITBotMain, TBotMain>()
				.AddScoped<ITBotOgamedBridge, TBotOgamedBridge>()
				.AddScoped<IFleetScheduler, FleetScheduler>()
				.AddScoped<IWorkerFactory, WorkerFactory>()
				.AddScoped<ITelegramMessenger, TelegramMessenger>()
				.AddScoped<IInstanceManager, InstanceManager>();

			ConsoleHelpers.SetTitle();

			CmdLineArgsService.DoParse(args);
			if (CmdLineArgsService.printHelp) {
				ColoredConsoleWriter.LogToConsole(LogLevel.Information, LogSender.Tbot, $"{System.AppDomain.CurrentDomain.FriendlyName} {CmdLineArgsService.helpStr}");
				Environment.Exit(0);
			}

			string settingsPath = Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), "settings.json");
			if (CmdLineArgsService.settingsPath.IsPresent) {
				settingsPath = Path.GetFullPath(CmdLineArgsService.settingsPath.Get());
			}

			SettingsService.GlobalSettingsPath = settingsPath;

			var logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
			if (CmdLineArgsService.logPath.IsPresent == true) {
				logPath = Path.GetFullPath(CmdLineArgsService.logPath.Get());
			}

			SettingsService.LogsPath = logPath;

			var serviceProvider = await WebApp.Build();

			_logger = serviceProvider.GetRequiredService<ILoggerService<Program>>();
			_instanceManager = serviceProvider.GetRequiredService<IInstanceManager>();
			var ogameService = serviceProvider.GetRequiredService<IOgameService>();

			_logger.ConfigureLogging(logPath);
			_instanceManager.SettingsAbsoluteFilepath = settingsPath;
			

			if (!ogameService.ValidatePrerequisites()) {
				Environment.Exit(-1);
			} else if (File.Exists(_instanceManager.SettingsAbsoluteFilepath) == false) {
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"\"{_instanceManager.SettingsAbsoluteFilepath}\" not found. Cannot proceed...");
				Environment.Exit(-1);
			}

			_instanceManager.OnSettingsChanged();

			var tcs = new TaskCompletionSource();
			CancellationTokenSource cts = new CancellationTokenSource();

			Console.CancelKeyPress += (sender, e) => {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "CTRL+C pressed!");
				e.Cancel = true;
				cts.Cancel();
				tcs.TrySetResult();
			};

			using var sigtermRegistration = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => {
				_logger.WriteLog(LogLevel.Information, LogSender.Main, "SIGTERM received (systemctl stop/restart)!");
				ctx.Cancel = true;
				cts.Cancel();
				tcs.TrySetResult();
			});

			var settings = await SettingsService.GetSettings(settingsPath);
			if (SettingsService.IsSettingSet(settings, "WebUI")
				&& SettingsService.IsSettingSet(settings.WebUI, "Enable")
				&& (bool) settings.WebUI.Enable) {
				await WebApp.Main(cts.Token);
			}
			await tcs.Task;

			var disposeTask = _instanceManager.DisposeAsync().AsTask();
			var disposeTimeout = Task.Delay(TimeSpan.FromSeconds(20));
			var disposeCompleted = await Task.WhenAny(disposeTask, disposeTimeout);
			if (disposeCompleted != disposeTask) {
				_logger.WriteLog(LogLevel.Warning, LogSender.Main, "Graceful shutdown did not finish within 20s (likely ogamed is wedged) - force-killing ogamed so stuck workers unblock.");
				try {
					ogameService.KillOgamedExecutable();
				} catch (Exception ex) {
					_logger.WriteLog(LogLevel.Error, LogSender.Main, $"KillOgamedExecutable() itself threw: {ex.Message}");
				}
				await Task.WhenAny(disposeTask, Task.Delay(TimeSpan.FromSeconds(5)));
			}

			if (serviceProvider is IAsyncDisposable asyncDisposableProvider) {
				await asyncDisposableProvider.DisposeAsync();
			} else if (serviceProvider is IDisposable disposableProvider) {
				disposableProvider.Dispose();
			}

			_logger.WriteLog(LogLevel.Information, LogSender.Main, "Goodbye!");
		}
	}
}