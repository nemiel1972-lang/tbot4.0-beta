using System;
using System.Collections.Generic;
using System.Linq;

namespace TBot.Model {
	public static class AutoFarmScanPlanner {
		// PATCH 125: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 145
		// resumeSystem == null -> komplette Range von vorne (startSystem..endSystem).
		// resumeSystem gesetzt -> NUR der Rest ab resumeSystem bis endSystem, KEIN Wraparound mehr.
		// Vorher wurde ab resumeSystem "rotiert" und danach wieder von startSystem bis resumeSystem-1
		// weitergescannt. Zusammen mit AutoFarm.ScanBlockSize hiess das: jeder Zyklus resumt, bekommt
		// wieder die KOMPLETTE Range (nur verschoben), wird nach ScanBlockSize abgebrochen - die Range
		// wurde innerhalb eines Durchlaufs also nie "fertig" und der Cursor kam nie zum naechsten
		// AutoFarm.ScanRange-Eintrag (Galaxie 1 wurde nie gescannt).
		public static IReadOnlyList<int> EnumerateSystems(int startSystem, int endSystem, int? resumeSystem) {
			if (endSystem < startSystem)
				return Array.Empty<int>();

			int from = resumeSystem.HasValue ? Math.Clamp(resumeSystem.Value, startSystem, endSystem) : startSystem;
			return Enumerable.Range(from, endSystem - from + 1).ToArray();
		}

		// PATCH 104: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 65
		// Wie EnumerateSystems, aber die Systeme einer Range werden nach ihrem KREISFOERMIGEN Abstand
		// zu homeSystem sortiert (Heimatwelt-/Mond-System) - naeheste Systeme zuerst.
		// totalSystems (ServerData.Systems) wird fuer den Ring-Wraparound benoetigt (z.B. bei 499
		// Systemen ist System 499 nur 1 System von System 1 entfernt, nicht 498).
		// PATCH 125: resumeSystem == null -> komplette distanzsortierte Liste ab Index 0 (also ab der
		// Heimatwelt selbst). Vorher wurde auch beim Neustart einer Range "ab startSystem rotiert" -
		// bei Range 1-499 und Heimat 100 begann ein frischer Scan dadurch bei System 1 (Distanz 99)
		// statt bei 100. resumeSystem gesetzt -> nur der Rest der distanzsortierten Liste ab dessen
		// Position, ohne Wraparound (siehe Kommentar bei EnumerateSystems).
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