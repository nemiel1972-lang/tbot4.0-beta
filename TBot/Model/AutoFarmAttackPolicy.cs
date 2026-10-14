using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public static class AutoFarmAttackPolicy {
		// PATCH 119: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 139
		// excludeFleetId: schliesst eine bestimmte Flotten-ID vom Duplicate-Check aus.
		// Noetig, weil die Truemmerfeld-Trigger-Sonde (siehe AutoFarmWorker.cs) selbst als
		// Missions.Attack zum selben Ziel unterwegs ist - ohne den Ausschluss erkennt dieser
		// Check die eigene, gerade erst verschickte Trigger-Sonde faelschlich als "Angriff
		// bereits unterwegs" und der eigentliche (grosse) Angriff wird nie gesendet.
		public static bool IsAttackInProgress(FarmTarget target, IEnumerable<Fleet> fleets, int? excludeFleetId = null) {
			var destination = target?.Celestial?.Coordinate;
			if (destination == null)
				return false;

			return (fleets ?? Array.Empty<Fleet>())
				.Any(fleet => fleet != null &&
					fleet.Mission == Missions.Attack &&
					fleet.Destination?.IsSame(destination) == true &&
					(!excludeFleetId.HasValue || fleet.ID != excludeFleetId.Value));
		}

		public static bool CanUseReport(FarmTarget target, EspionageReport report) {
			if (target == null || report == null)
				return false;

			return !target.ConsumedReportId.HasValue ||
				target.ConsumedReportId.Value != report.ID;
		}

		public static bool CanProcessReport(FarmTarget target, EspionageReport report) {
			if (target == null || report == null || report.ID <= 0)
				return false;

			if (target.State == FarmState.AttackSent)
				return false;

			return CanUseReport(target, report);
		}

		public static bool IsRepeatedNonActionableReport(FarmTarget target, EspionageReport report) {
			if (target == null || report == null)
				return false;

			var previousReport = target.Report;
			if (previousReport == null || previousReport.ID <= 0 || report.ID <= 0 || previousReport.ID != report.ID)
				return false;

			return target.State == FarmState.NotSuitable
				|| target.State == FarmState.ProbesRequired
				|| target.State == FarmState.FailedProbesRequired;
		}
	}
}