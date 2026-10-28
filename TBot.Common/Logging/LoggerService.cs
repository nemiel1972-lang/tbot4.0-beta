using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks;
using TBot.Common.Logging;
using TBot.Common.Logging.Enrichers;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.Sinks;
using TBot.Common.Logging.TextFormatters;
using System.Reflection;
using System.Runtime.InteropServices;
using Serilog.Context;
using Serilog.Core;
using Serilog.Filters;
using Microsoft.AspNetCore.SignalR;
using TBot.Common.Logging.Hubs;
using Serilog.Sinks.AspNetCore.SignalR.Extensions;
using System.Globalization;
using Microsoft.AspNetCore.Routing.Template;

namespace TBot.Common.Logging {
	public class LoggerService<T> : ILoggerService<T> {

		private readonly IHubContext<WebHub, IWebHub> _hub;
		private readonly IServiceProvider _serviceProvider;

		private static readonly string _playerName = Environment.GetEnvironmentVariable("TBOT_LOG_PLAYERNAME") ?? "";
		private static readonly string _universeName = Environment.GetEnvironmentVariable("TBOT_LOG_UNIVERSENAME") ?? "";

		private static string Anonymize(string message) {
			return LogAnonymizer.Apply(message);
		}

		public LoggerService(IHubContext<WebHub, IWebHub> hub,
			IServiceProvider serviceProvider) {
			_hub = hub;
			_serviceProvider = serviceProvider;
		}




		public void WriteLog(LogLevel level, LogSender sender, string message) {
			IDisposable? telegram = null;

			LogSave.Write(level, sender.ToString(), message);
			CollectLogs.Write(level, sender.ToString(), message);
			message = Anonymize(message);

			if (TelegramLogState.Added == true) {
				telegram = LogContext.PushProperty("TelegramEnabled", true);
			}
			using (LogContext.PushProperty("LogSender", sender))
			using (LogContext.PushProperty("LogLevel", level))
			using (LogContext.PushProperty("LogSenderEmoji", EmojiFormatter.GetEmoji(sender.ToString())))
			using (LogContext.PushProperty("LogLevelEmoji", EmojiFormatter.GetEmoji(level.ToString())))
			{
				switch (level) {
					case LogLevel.Trace:
						Log.Logger.Verbose(message);
						break;
					case LogLevel.Debug:
						Log.Logger.Debug(message);
						break;
					case LogLevel.Information:
						Log.Logger.Information(message);
						break;
					case LogLevel.Warning:
						Log.Logger.Warning(message);
						break;
					case LogLevel.Error:
						Log.Logger.Error(message);
						break;
					case LogLevel.Critical:
						Log.Logger.Fatal(message);
						break;
				}
			}

			if (telegram != null) {
				telegram.Dispose();
			}
		}

		private LoggerConfiguration GetDefaultConfiguration() {
			string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
			long maxFileSize = 1 * 1024 * 1024 * 10;

			var logConfig = new LoggerConfiguration()
				.Enrich.With(new ThreadIdEnricher())
				.Enrich.FromLogContext()
				.WriteTo.TBotColoredConsole(
					outputTemplate: outTemplate
				)
				.WriteTo.File(
					path: Path.Combine(TelegramLogState.LogPath, "TBot.log"),
					buffered: false,
					restrictedToMinimumLevel: LogEventLevel.Debug,
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					retainedFileCountLimit: 10,
					rollingInterval: RollingInterval.Day)
				.WriteTo.File(
					path: Path.Combine(TelegramLogState.LogPath, "TBot.csv"),
					buffered: false,
					restrictedToMinimumLevel: LogEventLevel.Debug,
					hooks: new SerilogCSVHeaderHooks(),
					formatter: new SerilogCSVTextFormatter(),
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					rollingInterval: RollingInterval.Day)
				.WriteTo.SignalRTBotSink<WebHub, IWebHub>(
					LogEventLevel.Verbose,
					_serviceProvider,
					CultureInfo.InvariantCulture,
					new string[] { },
					new string[] { },
					new string[] { },
					false)
				.MinimumLevel.Verbose();

			return logConfig;
		}

		public void ConfigureLogging(string logPath) {
			lock (TelegramLogState.Sync) {
				TelegramLogState.LogPath = logPath;

				var logConfig = GetDefaultConfiguration();

				TelegramLogState.Switch.MinimumLevel = TelegramLogState.Levels.Lowest;
				TelegramLogState.Added = false;

				Log.Logger = logConfig.CreateLogger();

			}
		}

		public void AddTelegramLogger(string botToken, string chatId) {
			lock (TelegramLogState.Sync) {
				if (TelegramLogState.Added == false) {
					var logConfig = GetDefaultConfiguration();
					Log.Logger = logConfig.WriteTo.Logger(
							c => c.MinimumLevel.ControlledBy(TelegramLogState.Switch)
								.Filter.ByIncludingOnly(TelegramAllows)
								.WriteTo.Telegram(botToken: botToken,
									chatId: chatId,
									dateFormat: null,
									outputTemplate: "{LogLevelEmoji:l}{LogSenderEmoji:l} {Message:lj}{NewLine}{Exception}")
							).CreateLogger();

					TelegramLogState.Added = true;
				}
			}
		}

		private bool TelegramAllows(LogEvent e) {
			if (!e.Properties.TryGetValue("TelegramEnabled", out var enabled) || !(enabled is ScalarValue es) || !(es.Value is bool on) || !on)
				return false;
			string sender = null;
			if (e.Properties.TryGetValue("LogSender", out var ls) && ls is ScalarValue lss)
				sender = lss.Value?.ToString();
			if (!TelegramLogState.Levels.Allows(sender, e.Level))
				return false;
			return TelegramLogState.Repeat.ShouldSend(sender, e.RenderMessage(), DateTime.UtcNow);
		}

		public void ConfigureTelegramRepeat(int defaultMinutes, IEnumerable<KeyValuePair<string, string>> perSender) {
			lock (TelegramLogState.Sync) {
				var limit = TelegramRepeatLimit.Parse(defaultMinutes, perSender);
				string before = TelegramLogState.Repeat.Describe();
				string after = limit.Describe();
				TelegramLogState.Repeat = limit;
				if (before != after)
					WriteLog(LogLevel.Information, LogSender.Main, $"Telegram log: {after}");
			}
		}

		public void ConfigureTelegramLevels(string defaultLevel, IEnumerable<KeyValuePair<string, string>> perSender) {
			lock (TelegramLogState.Sync) {
				TelegramLogState.Levels = TelegramLogLevels.Parse(defaultLevel, perSender);
				TelegramLogState.Switch.MinimumLevel = TelegramLogState.Levels.Lowest;
				string text = TelegramLogState.Levels.Describe();
				if (text != TelegramLogState.LevelsText) {
					TelegramLogState.LevelsText = text;
					WriteLog(LogLevel.Information, LogSender.Main, $"Telegram log levels: {text}");
				}
			}
		}

		public void RemoveTelegramLogger() {
			ConfigureLogging(TelegramLogState.LogPath);
		}

		public bool IsTelegramLoggerEnabled() {
			return TelegramLogState.Added;
		}

		public void SetTelegramLoggerLogLevel(LogEventLevel logLevel) {
			lock (TelegramLogState.Sync) {
				if (logLevel != TelegramLogState.Levels.Default) {
					WriteLog(LogLevel.Warning, LogSender.Main, $"Telegram log level changed from {TelegramLogState.Levels.Default.ToString()}" +
						$" into {logLevel.ToString()} (per-worker levels from the config stay as they are)");
				}
				TelegramLogState.Levels = TelegramLogState.Levels.WithDefault(logLevel);
				TelegramLogState.Switch.MinimumLevel = TelegramLogState.Levels.Lowest;
			}
		}

		public LogEventLevel GetTelegramLoggerLevel() {
			return TelegramLogState.Levels.Default;
		}
	}
}