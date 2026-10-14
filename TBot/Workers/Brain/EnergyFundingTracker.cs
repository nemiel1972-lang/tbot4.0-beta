using System.Collections.Concurrent;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers.Brain {
	public static class EnergyFundingTracker {
		private static readonly ConcurrentDictionary<int, Buildables> _pendingEnergyFunding = new();

		public static void SetPending(int celestialId, Buildables buildable) {
			if (buildable == Buildables.SolarPlant || buildable == Buildables.FusionReactor) {
				_pendingEnergyFunding[celestialId] = buildable;
			}
		}

		public static void Clear(int celestialId) {
			_pendingEnergyFunding.TryRemove(celestialId, out _);
		}

		public static bool IsEnergyFundingPending(int celestialId) {
			return _pendingEnergyFunding.ContainsKey(celestialId);
		}
	}
}
