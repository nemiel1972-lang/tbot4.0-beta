using System;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Helpers {
	public static class AutoMineOverrideHelper {

		public static dynamic FindOverride(Celestial celestial, dynamic autoMineSettings) {
			if (!SettingsService.IsSettingSet(autoMineSettings, "ColonyOverrides"))
				return null;
			try {
				foreach (var entry in autoMineSettings.ColonyOverrides) {
					Coordinate entryCoords = new(
						(int) entry.Galaxy,
						(int) entry.System,
						(int) entry.Position,
						Enum.Parse<Celestials>(entry.Type.ToString())
					);
					if (celestial.HasCoords(entryCoords))
						return entry;
				}
			} catch {
			}
			return null;
		}

		public static int Resolve(dynamic entry, dynamic autoMineSettings, string propName, int current) {
			int value = current;
			if (SettingsService.IsSettingSet(autoMineSettings, "Colony") &&
				SettingsService.IsSettingSet(autoMineSettings.Colony, propName)) {
				value = SettingsService.GetSetting<int>(autoMineSettings.Colony, propName, current);
			} else if (SettingsService.IsSettingSet(autoMineSettings, propName)) {
				value = SettingsService.GetSetting<int>(autoMineSettings, propName, current);
			}
			if (SettingsService.IsSettingSet(entry, propName))
				value = SettingsService.GetSetting<int>(entry, propName, value);
			return value;
		}
	}
}
