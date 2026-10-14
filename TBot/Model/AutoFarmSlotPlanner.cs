using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public sealed record AutoFarmSlotBudget(int MaxSlots, int OwnedSlots, int AvailableSlots);

	public static class AutoFarmSlotPlanner {
		public static AutoFarmSlotBudget Calculate(
			int maxSlots,
			int totalFreeSlots,
			int slotsToLeaveFree,
			IEnumerable<Fleet> fleets,
			Buildables cargoType) {
			var ownedSlots = (fleets ?? Enumerable.Empty<Fleet>())
				.Count(fleet => IsOwnedFleet(fleet, cargoType));

			var availableByWorker = Math.Max(0, maxSlots - ownedSlots);
			var availableGlobally = Math.Max(0, totalFreeSlots - slotsToLeaveFree);

			return new AutoFarmSlotBudget(
				Math.Max(0, maxSlots),
				ownedSlots,
				Math.Min(availableByWorker, availableGlobally));
		}

		public static bool IsOwnedFleet(Fleet fleet, Buildables cargoType) {
			if (fleet?.Ships == null)
				return false;

			if (fleet.Mission == Missions.Spy)
				return fleet.Ships.IsOnlyProbes();

			return fleet.Mission == Missions.Attack
				&& cargoType != Buildables.Null
				&& fleet.Ships.GetAmount(cargoType) > 0;
		}
	}
}