using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	/// <summary>
	/// Celestial under consideration to be targetted for farming.
	/// </summary>
	public class FarmTarget {
		public FarmTarget(Celestial target, FarmState farmState = FarmState.Idle, EspionageReport report = null) {
			Celestial = target;
			State = farmState;
			Report = report;
		}
		public Celestial Celestial { get; set; }
		public FarmState State { get; set; }
		public EspionageReport Report { get; set; }
		public int? ConsumedReportId { get; set; }

		// PATCH 86: Zeitpunkt (Server-Zeit), zu dem State zuletzt auf AttackSent gesetzt wurde.
		// PruneOldReports() fragt bei jedem Interleaving-Pass (PATCH 68) die Flottenliste neu ab
		// und prueft per AutoFarmAttackPolicy.IsAttackInProgress(), ob die Angriffsflotte noch
		// unterwegs ist. Direkt nach dem Versand kann die frisch abgefragte Flottenliste die
		// gerade erst losgeschickte Flotte aber noch nicht enthalten (API-/ogamed-Verzoegerung) -
		// IsAttackInProgress liefert dann faelschlich "nicht unterwegs", der State wird verfrueht
		// auf ProbesPending zurueckgesetzt, und derselbe Ziel-Zyklus (Sonde -> Bericht -> Angriff)
		// startet erneut, WAEHREND die erste Flotte noch fliegt - Ergebnis: mehrere echte
		// Angriffsflotten kurz hintereinander auf dasselbe Ziel. Mit AttackSentAt kann eine
		// Mindest-Schonfrist erzwungen werden, unabhaengig davon, was die (evtl. noch veraltete)
		// Flottenliste gerade zeigt.
		public DateTime? AttackSentAt { get; set; }

		// PATCH 117: siehe AutoFarmWorker.PATCHNOTES.md, Abschnitt 137
		// Markiert Ziele, die schon beim ALLERERSTEN Check (AutoFarm.MaxDefensePoints ueberschritten,
		// noch bevor irgendeine Eskorten-Simulation laeuft) als "zu stark verteidigt" erkannt wurden.
		// KEINE Blacklist: das Ziel/der Spionagebericht bleibt normal in der Zielliste sichtbar
		// (Kampfrechner direkt vom Bericht aus aufrufbar) und wird von AutoFarm.KeepReportFor NICHT
		// automatisch geloescht - der Nutzer prueft/entfernt es manuell selbst.
		public bool HeldForManualReview { get; set; }

		public bool HasCoords(Coordinate coords) {
			return coords != null && Celestial?.Coordinate != null
				&& coords.Galaxy == Celestial.Coordinate.Galaxy
				&& coords.System == Celestial.Coordinate.System
				&& coords.Position == Celestial.Coordinate.Position
				&& coords.Type == Celestial.Coordinate.Type;
		}
		private string GetCelestialCode() {
			return Celestial.Coordinate.Type switch {
				Celestials.Planet => "P",
				Celestials.Debris => "DF",
				Celestials.Moon => "M",
				Celestials.DeepSpace => "DS",
				_ => "",
			};
		}
		public override string ToString() {
			return $"[{GetCelestialCode()}:{Celestial.Coordinate.Galaxy}:{Celestial.Coordinate.System}:{Celestial.Coordinate.Position}]";
		}
	}

}