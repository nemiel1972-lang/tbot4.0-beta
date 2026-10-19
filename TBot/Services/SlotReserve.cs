using System;

namespace Tbot.Services {
	public class FleetsaveSlotConfig {
		public bool Active = true;
		public int Slots = 1;
	}

	public enum SlotDecision {
		Allowed,
		NoSlots,
		ReservedForFleetsave
	}

	public static class SlotReserve {
		public static SlotDecision Decide(int free, int softLeaveFree, int hardReserve, bool force, bool isFleetsave) {
			if (free <= 0)
				return SlotDecision.NoSlots;
			if (isFleetsave)
				return SlotDecision.Allowed;
			if (hardReserve > 0 && free <= hardReserve)
				return SlotDecision.ReservedForFleetsave;
			return (free > softLeaveFree || force) ? SlotDecision.Allowed : SlotDecision.NoSlots;
		}
	}
}
