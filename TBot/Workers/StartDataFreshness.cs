using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public static class StartDataFreshness {
		public const string Resources = "res", Ships = "ships", Production = "prod";
		private static readonly ConditionalWeakTable<Celestial, ConcurrentDictionary<string, DateTime>> Stamps = new();

		public static void Mark(Celestial c, UpdateTypes type) {
			if (c == null)
				return;
			var d = Stamps.GetOrCreateValue(c);
			var now = DateTime.UtcNow;
			switch (type) {
				case UpdateTypes.Resources: d[Resources] = now; break;
				case UpdateTypes.Ships: d[Ships] = now; break;
				case UpdateTypes.ResourcesProduction: d[Production] = now; break;
				case UpdateTypes.Techs: d[Ships] = now; break;
				case UpdateTypes.Full:
					d[Resources] = now; d[Ships] = now; d[Production] = now;
					break;
			}
		}

		public static double AgeMinutes(Celestial c, string kind) {
			if (c != null && Stamps.TryGetValue(c, out var d) && d.TryGetValue(kind, out var at))
				return (DateTime.UtcNow - at).TotalMinutes;
			return double.MaxValue;
		}
	}
}
