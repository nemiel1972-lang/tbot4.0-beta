using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class SlotReserveTests {
		[Fact]
		public void Kein_Slot_frei_geht_nie() {
			Assert.Equal(SlotDecision.NoSlots, SlotReserve.Decide(0, 1, 1, true, false));
			Assert.Equal(SlotDecision.NoSlots, SlotReserve.Decide(0, 1, 1, true, true));
		}

		[Fact]
		public void Fleetsave_darf_den_letzten_Slot_nehmen() => Assert.Equal(SlotDecision.Allowed, SlotReserve.Decide(1, 1, 1, false, true));

		[Fact]
		public void Normale_Flotte_darf_den_reservierten_Slot_nicht_nehmen() => Assert.Equal(SlotDecision.ReservedForFleetsave, SlotReserve.Decide(1, 0, 1, false, false));

		[Fact]
		public void Auch_force_darf_den_reservierten_Slot_nicht_nehmen() => Assert.Equal(SlotDecision.ReservedForFleetsave, SlotReserve.Decide(1, 1, 1, true, false));

		[Fact]
		public void Ohne_Reserve_gilt_das_alte_Verhalten() {
			Assert.Equal(SlotDecision.NoSlots, SlotReserve.Decide(1, 1, 0, false, false));
			Assert.Equal(SlotDecision.Allowed, SlotReserve.Decide(1, 1, 0, true, false));
			Assert.Equal(SlotDecision.Allowed, SlotReserve.Decide(2, 1, 0, false, false));
		}

		[Fact]
		public void Zwei_Reserve_Slots() {
			Assert.Equal(SlotDecision.ReservedForFleetsave, SlotReserve.Decide(2, 0, 2, false, false));
			Assert.Equal(SlotDecision.Allowed, SlotReserve.Decide(3, 0, 2, false, false));
		}
	}
}
