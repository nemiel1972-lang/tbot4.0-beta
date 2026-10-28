using Serilog.Core;
using Serilog.Events;

namespace TBot.Common.Logging {
	internal static class TelegramLogState {
		internal static readonly object Sync = new object();
		internal static readonly LoggingLevelSwitch Switch = new LoggingLevelSwitch(LogEventLevel.Warning);
		internal static volatile bool Added;
		internal static volatile TelegramLogLevels Levels = TelegramLogLevels.OnlyDefault(LogEventLevel.Warning);
		internal static volatile TelegramRepeatLimit Repeat = TelegramRepeatLimit.Off();
		internal static string LevelsText = "";
		internal static string LogPath = "";
	}
}
