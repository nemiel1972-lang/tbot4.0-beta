using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Includes {
	public static class TransportReserve {
		public static long DeutToLeave(dynamic transports, Celestials type) {
			if (type == Celestials.Moon)
				return (long) transports.DeutToLeaveOnMoons;
			return SettingsService.GetSetting<long>(transports, "DeutToLeaveOnPlanets", 0L);
		}
	}
}
