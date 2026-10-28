#if FASTFARM
using Newtonsoft.Json.Linq;
using Tbot.Common.Settings;
using TBot.WebUI.Models;

namespace TBot.WebUI.Services {
	public static class FastFarmReader {
		public static FastFarmDashboardModel Read(string alias, IReadOnlyList<string> instances) {
			var model = new FastFarmDashboardModel { SelectedInstance = alias, Instances = instances };
			try {
				var block = ReadBlock(alias);
				if (block != null) {
					model.Available = true;
					model.Active = block.Value<bool?>("Active") ?? false;
					model.OnlyWorker = block.Value<bool?>("OnlyWorker") ?? true;
					model.Config = new FastFarmConfigSummary {
						CargoType = block.Value<string>("CargoType") ?? "SmallCargo",
						ProbesPerTarget = block.Value<int?>("ProbesPerTarget") ?? 20,
						SlotsToLeaveFree = block.Value<int?>("SlotsToLeaveFree") ?? 1,
						WeightMetal = block.Value<double?>("WeightMetal") ?? 3,
						WeightCrystal = block.Value<double?>("WeightCrystal") ?? 2,
						WeightDeuterium = block.Value<double?>("WeightDeuterium") ?? 1,
						MinLoot = block.Value<long?>("MinLoot") ?? 100000,
						MinDeuterium = block.Value<long?>("MinDeuterium") ?? 0,
						TopTargets = block.Value<int?>("TopTargets") ?? 36,
						ReportMaxAgeMinutes = block.Value<int?>("ReportMaxAgeMinutes") ?? 30,
						ShrinkDiscardPercent = block.Value<double?>("ShrinkDiscardPercent") ?? 10
					};
				}
			} catch {
			}
			try {
				string path = StatePath(alias);
				if (File.Exists(path))
					model.State = JObject.Parse(File.ReadAllText(path)).ToObject<FastFarmStateDto>();
			} catch {
			}
			return model;
		}

		public static bool IsActive(string alias) {
			try {
				return ReadBlock(alias)?.Value<bool?>("Active") ?? false;
			} catch {
				return false;
			}
		}

		public static (bool Overrides, bool KeepExpeditions) GetOverride(string alias) {
			try {
				var block = ReadBlock(alias);
				if (block == null || !(block.Value<bool?>("Active") ?? false) || !(block.Value<bool?>("OnlyWorker") ?? true))
					return (false, false);
				return (true, block.Value<bool?>("KeepExpeditions") ?? false);
			} catch {
				return (false, false);
			}
		}

		public static bool GetKeepAuction(string alias) {
			try {
				return ReadBlock(alias)?.Value<bool?>("KeepAuction") ?? false;
			} catch {
				return false;
			}
		}

		private static JObject? ReadBlock(string alias) {
			var globalPath = SettingsService.GlobalSettingsPath;
			if (string.IsNullOrWhiteSpace(globalPath) || !File.Exists(globalPath))
				globalPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
			var global = JObject.Parse(File.ReadAllText(globalPath));
			string instancePath = Path.Combine(AppContext.BaseDirectory, "instance_settings.json");
			foreach (var instance in global["Instances"]?.OfType<JObject>() ?? Enumerable.Empty<JObject>()) {
				if (!string.Equals(instance.Value<string>("Alias"), alias, StringComparison.OrdinalIgnoreCase))
					continue;
				var configured = instance.Value<string>("Settings");
				if (!string.IsNullOrWhiteSpace(configured))
					instancePath = Path.IsPathRooted(configured) ? configured : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(globalPath) ?? AppContext.BaseDirectory, configured));
				break;
			}
			if (!File.Exists(instancePath))
				return null;
			var resolved = SettingsService.ReadResolvedObject(instancePath);
			return resolved["FastFarm"] as JObject;
		}

		private static string StatePath(string alias) {
			return Path.Combine(AppContext.BaseDirectory, "data", Path.GetFileName($"fastfarm_{alias}.json"));
		}
	}
}
#endif
