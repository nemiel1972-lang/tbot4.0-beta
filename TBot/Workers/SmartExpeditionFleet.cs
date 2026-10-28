using System;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal sealed class SmartExpeditionInput {
		public Ships Available;
		public Buildables Primary;
		public Buildables Mix;
		public int Pending;
		public long IdealPrimary;
		public long ExtraCapacity;
		public long CapacityPrimary;
		public long CapacityMix;
	}

	internal static class SmartExpeditionFleet {
		private static readonly Buildables[] MilitaryOrder = {
			Buildables.Reaper, Buildables.Destroyer, Buildables.Bomber, Buildables.Battlecruiser,
			Buildables.Battleship, Buildables.Cruiser, Buildables.HeavyFighter, Buildables.LightFighter
		};

		public static Ships Build(SmartExpeditionInput input) {
			var fleet = new Ships();
			if (input == null || input.Available == null || input.Primary == Buildables.Null || input.CapacityPrimary <= 0)
				return fleet;

			var avail = input.Available;
			long pending = Math.Max(1, input.Pending);

			long targetCapacity = Math.Max(0, input.IdealPrimary) * input.CapacityPrimary + Math.Max(0, input.ExtraCapacity);
			long wantPrimary = (targetCapacity + input.CapacityPrimary - 1) / input.CapacityPrimary;
			long primary = Math.Min(wantPrimary, avail.GetAmount(input.Primary) / pending);
			if (primary > 0)
				fleet.SetAmount(input.Primary, primary);

			long missing = targetCapacity - primary * input.CapacityPrimary;
			long mix = 0;
			if (missing > 0 && input.Mix != Buildables.Null && input.Mix != input.Primary && input.CapacityMix > 0) {
				long wantMix = (missing + input.CapacityMix - 1) / input.CapacityMix;
				mix = Math.Min(wantMix, avail.GetAmount(input.Mix) / pending);
				if (mix > 0)
					fleet.SetAmount(input.Mix, mix);
			}

			if (primary + mix <= 0)
				return new Ships();

			if (avail.GetAmount(Buildables.EspionageProbe) >= 1)
				fleet.SetAmount(Buildables.EspionageProbe, 1);
			if (avail.GetAmount(Buildables.Pathfinder) >= 1)
				fleet.SetAmount(Buildables.Pathfinder, 1);
			foreach (var ship in MilitaryOrder) {
				if (avail.GetAmount(ship) >= 1) {
					fleet.SetAmount(ship, 1);
					break;
				}
			}
			return fleet;
		}
	}
}
