using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public enum BoosterType {
		Metal,
		Crystal,
		Deuterium,
		Energy
	}

	public static class BoosterMatcher {
		private static readonly Dictionary<BoosterType, string[]> TypeKeywords = new() {
			[BoosterType.Metal] = new[] { "metal", "metall" },
			[BoosterType.Crystal] = new[] { "crystal", "kristall" },
			[BoosterType.Deuterium] = new[] { "deuterium" },
			[BoosterType.Energy] = new[] { "energy", "energie" },
		};

		private static readonly Dictionary<string, string[]> TierKeywords = new(StringComparer.OrdinalIgnoreCase) {
			["bronze"] = new[] { "bronze" },
			["silver"] = new[] { "silver", "silber" },
			["gold"] = new[] { "gold" },
			["platinum"] = new[] { "platinum", "platin" },
		};

		private static readonly string[] NotABooster = { "kraken", "detroid", "newtron", "newton", "crawler", "commander", "admiral", "engineer", "geologist", "technocrat",
			"ressourcen-booster", "ressourcenbooster", "resource booster", "resource-booster", "expedition" };

		public static string Normalize(string name) => (name ?? "").Trim().ToLowerInvariant();

		public static bool IsBoosterOfType(string name, BoosterType type) {
			var n = Normalize(name);
			if (n.Length == 0 || NotABooster.Any(n.Contains))
				return false;
			return TypeKeywords[type].Any(n.Contains);
		}

		public static string TierOf(string name) {
			var n = Normalize(name);
			foreach (var kv in TierKeywords)
				if (kv.Value.Any(n.Contains))
					return kv.Key;
			return null;
		}

		public static readonly string[] TierOrder = { "bronze", "silver", "gold", "platinum" };

		public static BoosterType? TypeOf(string name) {
			foreach (BoosterType t in Enum.GetValues(typeof(BoosterType)))
				if (IsBoosterOfType(name, t))
					return t;
			return null;
		}

		public static Dictionary<string, Dictionary<string, long>> StockByTier(IEnumerable<InventoryItem> inventory) {
			var res = new Dictionary<string, Dictionary<string, long>>();
			foreach (var tier in TierOrder)
				res[tier] = new Dictionary<string, long> { ["Metal"] = 0, ["Crystal"] = 0, ["Deuterium"] = 0, ["Energy"] = 0 };
			foreach (var i in inventory ?? Enumerable.Empty<InventoryItem>()) {
				var type = TypeOf(i.Name);
				var tier = TierOf(i.Name);
				if (type != null && tier != null)
					res[tier][type.Value.ToString()] += i.Amount;
			}
			return res;
		}

		public static bool IsActive(IEnumerable<ActiveItem> active, BoosterType type) =>
			ActiveItemOf(active, type) != null;

		public static ActiveItem ActiveItemOf(IEnumerable<ActiveItem> active, BoosterType type) =>
			(active ?? Enumerable.Empty<ActiveItem>()).Where(a => a.TimeRemaining > 0 && IsBoosterOfType(a.Name, type)).OrderByDescending(a => a.TimeRemaining).FirstOrDefault();

		public static InventoryItem Pick(IEnumerable<InventoryItem> inventory, BoosterType type, IList<string> tiersInPreferenceOrder, int reserve = 0) =>
			Pick(inventory, type, tiersInPreferenceOrder, _ => reserve);

		public static InventoryItem Pick(IEnumerable<InventoryItem> inventory, BoosterType type, IList<string> tiersInPreferenceOrder, Func<string, int> reserveOfTier) {
			if (inventory == null || tiersInPreferenceOrder == null)
				return null;
			var candidates = inventory.Where(i => !string.IsNullOrEmpty(i.Ref) && IsBoosterOfType(i.Name, type)).ToList();
			foreach (var tier in tiersInPreferenceOrder) {
				var t = Normalize(tier);
				int keep = Math.Max(0, reserveOfTier?.Invoke(t) ?? 0);
				var hit = candidates.FirstOrDefault(c => TierOf(c.Name) == t && c.Amount > keep);
				if (hit != null)
					return hit;
			}
			return null;
		}
	}
}
