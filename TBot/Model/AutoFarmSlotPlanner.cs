using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public sealed record AutoFarmSlotBudget(int MaxSlots, int OwnedSlots, int AvailableSlots);

	/// <summary>
	/// Calculates the AutoFarm worker's fleet-slot budget without confusing
	/// AutoFarm fleets with unrelated mixed fleets.
	/// </summary>
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

			// Vorher wurde per IsOnlyShip() verlangt, dass die Flotte AUSSCHLIESSLICH
			// aus dem konfigurierten CargoType besteht. Echte AutoFarm-Angriffe auf verteidigte
			// Ziele kombinieren aber Cargo-Schiffe MIT Eskorte (Cruiser/Battlecruiser/Pathfinder
			// je nach EscortShipTypes/EscortFallbackShipTypes/SpeedFleetCargoTypes) in DERSELBEN
			// Flotte. Dadurch wurden eskortierte Angriffe faelschlich als "nicht AutoFarm-eigen"
			// gezaehlt -> OwnedSlots wurde unterschaetzt und AutoFarm.MaxSlots war kein
			// verlaesslicher Deckel mehr (siehe Dashboard-Anomalie "Used 0" trotz laufender
			// Angriffe auf System 2:271). Es reicht jetzt, dass der konfigurierte CargoType in
			// der Angriffsflotte ueberhaupt in nennenswerter Anzahl enthalten ist.
			return fleet.Mission == Missions.Attack
				&& cargoType != Buildables.Null
				&& fleet.Ships.GetAmount(cargoType) > 0;
		}
	}
}