using System;
using System.Collections.Generic;
using System.Linq;

namespace TBot.Model {
	public static class AutoFarmScanPlanner {
		public static IReadOnlyList<int> EnumerateSystems(int startSystem, int endSystem, int? resumeSystem) {
			if (endSystem < startSystem)
				return Array.Empty<int>();

			int from = resumeSystem.HasValue ? Math.Clamp(resumeSystem.Value, startSystem, endSystem) : startSystem;
			return Enumerable.Range(from, endSystem - from + 1).ToArray();
		}

		public static IReadOnlyList<int> EnumerateSystemsByDistance(int startSystem, int endSystem, int? resumeSystem, int homeSystem, int totalSystems) {
			int lo = Math.Min(startSystem, endSystem);
			int hi = Math.Max(startSystem, endSystem);

			if (totalSystems <= 0)
				return EnumerateSystems(lo, hi, resumeSystem);

			int CircularDistance(int system) {
				int diff = Math.Abs(system - homeSystem);
				return Math.Min(diff, totalSystems - diff);
			}

			var sortedByDistance = Enumerable.Range(lo, hi - lo + 1)
				.OrderBy(CircularDistance)
				.ThenBy(system => system)
				.ToArray();

			int resumeIndex = 0;
			if (resumeSystem.HasValue) {
				resumeIndex = Array.IndexOf(sortedByDistance, Math.Clamp(resumeSystem.Value, lo, hi));
				if (resumeIndex < 0)
					resumeIndex = 0;
			}

			return sortedByDistance.Skip(resumeIndex).ToArray();
		}
	}
}