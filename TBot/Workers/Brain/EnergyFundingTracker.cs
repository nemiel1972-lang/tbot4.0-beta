using System.Collections.Concurrent;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers.Brain {
	// AutoMine, LifeformAutoMine und LifeformAutoResearch laufen als drei komplett unabhaengige
	// Worker pro Celestial und wissen nichts voneinander. Wenn AutoMine gezielt einen Transport losschickt,
	// um ein Energiegebaeude (Solarkraftwerk/Fusionskraftwerk) zu finanzieren (siehe Patch 13:
	// _pendingFundedBuildable), koennen LifeformAutoMine oder LifeformAutoResearch in der Zwischenzeit auf
	// demselben Celestial zuschlagen und die frisch angekommenen Ressourcen fuer ein LF-Gebaeude bzw. eine
	// LF-Forschung verbrauchen - LifeformAutoMine kennt das Konzept "Energie zuerst" ueberhaupt nicht.
	// Diese Klasse ist ein einfacher, prozessweiter Lock pro Celestial-ID: AutoMine traegt sich ein, sobald
	// es eine Energiegebaeude-Finanzierung per Transport anstoesst, und meldet sich ab, sobald der Bau
	// gestartet wurde oder das Memo verworfen wird (Patch 13/35-Logik). LifeformAutoMine und
	// LifeformAutoResearch pruefen das Flag und ueberspringen den Celestial, solange es gesetzt ist.
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
