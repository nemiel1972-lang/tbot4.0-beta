using System;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Helpers {
	public static class AutoMineOverrideHelper {

		public static bool IsColonyConfigured(dynamic autoMineSettings) {
			if (!SettingsService.IsSettingSet(autoMineSettings, "Colony"))
				return false;
			try {
				var colony = Newtonsoft.Json.Linq.JObject.FromObject((object) autoMineSettings.Colony);
				foreach (var p in colony.Properties()) {
					if (p.Name.StartsWith("Max", StringComparison.OrdinalIgnoreCase) &&
						(p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Integer || p.Value.Type == Newtonsoft.Json.Linq.JTokenType.Float) &&
						(double) p.Value > 0)
						return true;
				}
				return false;
			} catch {
				return true;
			}
		}

		public static Facilities ResolveLunar(Celestial cel, dynamic autoMineSettings) {
			int Global(string name) => SettingsService.IsSettingSet(autoMineSettings, name) ? SettingsService.GetSetting<int>(autoMineSettings, name, 0) : 0;
			int lunarBase = Global("MaxLunarBase"), robotics = Global("MaxLunarRoboticsFactory"), phalanx = Global("MaxSensorPhalanx"), jump = Global("MaxJumpGate"), shipyard = Global("MaxLunarShipyard");
			try {
				bool isHome = cel.Coordinate.Galaxy == (int) autoMineSettings.HomeWorld.Galaxy &&
					cel.Coordinate.System == (int) autoMineSettings.HomeWorld.System &&
					cel.Coordinate.Position == (int) autoMineSettings.HomeWorld.Position;
				if (!isHome) {
					if (IsColonyConfigured((object) autoMineSettings)) {
						dynamic col = autoMineSettings.Colony;
						int C(string name, int cur) => SettingsService.IsSettingSet(col, name) ? SettingsService.GetSetting<int>(col, name, cur) : cur;
						lunarBase = C("MaxLunarBase", lunarBase); robotics = C("MaxLunarRoboticsFactory", robotics); phalanx = C("MaxSensorPhalanx", phalanx);
						jump = C("MaxJumpGate", jump); shipyard = C("MaxLunarShipyard", shipyard);
					}
					dynamic entry = FindOverride(cel, autoMineSettings);
					if (entry != null) {
						int O(string name, int cur) => SettingsService.IsSettingSet(entry, name) ? SettingsService.GetSetting<int>(entry, name, cur) : cur;
						lunarBase = O("MaxLunarBase", lunarBase); robotics = O("MaxLunarRoboticsFactory", robotics); phalanx = O("MaxSensorPhalanx", phalanx);
						jump = O("MaxJumpGate", jump); shipyard = O("MaxLunarShipyard", shipyard);
					}
				}
			} catch {
			}
			return new Facilities { LunarBase = lunarBase, RoboticsFactory = robotics, SensorPhalanx = phalanx, JumpGate = jump, Shipyard = shipyard };
		}

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
			if (AutoMineOverrideHelper.IsColonyConfigured(autoMineSettings) &&
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
