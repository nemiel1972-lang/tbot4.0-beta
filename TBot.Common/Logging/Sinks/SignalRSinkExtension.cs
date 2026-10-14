using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.AspNetCore.SignalR.Interfaces;
using Serilog;

namespace TBot.Common.Logging.Sinks {
	public static class SignalRSinkExtension {

		public static LoggerConfiguration SignalRTBotSink<THub, T>(
			this LoggerSinkConfiguration loggerConfiguration,
			LogEventLevel logEventLevel,
			IServiceProvider serviceProvider = null,
			IFormatProvider formatProvider = null,
			string[] groups = null,
			string[] userIds = null,
			string[] excludedConnectionIds = null,
			bool sendAsString = false
		) where THub : Hub<T> where T : class, IHub {
			if (loggerConfiguration == null) {
				throw new ArgumentNullException(nameof(loggerConfiguration));
			}
			if (serviceProvider == null) {
				throw new ArgumentNullException(nameof(serviceProvider));

			}
			return loggerConfiguration.Sink(new SignalRTbotSink<THub, T>(formatProvider,
					sendAsString,
					serviceProvider,
					groups,
					userIds,
					excludedConnectionIds),
				logEventLevel);
		}
	}
}
