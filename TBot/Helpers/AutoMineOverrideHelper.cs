using System;
using Tbot.Common.Settings;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Helpers {
	/// <summary>
	/// PATCH ColonyOverrides (ported idea from tbot_beta AutoMineRoleHelper): einzelne Planeten koennen
	/// ueber Brain.AutoMine.ColonyOverrides (Liste von {Galaxy, System, Position, Type, Max*...},
	/// Zuordnung per exakter Koordinate) nur die Felder ueberschreiben, die sie wirklich setzen. Alle
	/// anderen Felder kommen vom Colony-Block (Fallback: globale Max*-Werte). Ohne Eintrag
	/// gelten die Colony-Werte.
	/// Liest nur das fertig geladene Settings-Objekt, funktioniert also unabhaengig davon, aus
	/// welcher Datei der Block stammt.
	/// </summary>
	public static class AutoMineOverrideHelper {

		/// <summary>Liefert den passenden ColonyOverrides-Eintrag oder null.</summary>
		public static dynamic FindOverride(Celestial celestial, dynamic autoMineSettings) {
			if (!SettingsService.IsSettingSet(autoMineSettings, "ColonyOverrides"))
				return null;
			try {
				foreach (var entry in autoMineSettings.ColonyOverrides) {
					Coordinate entryCoords = new(
						(int) entry.Galaxy,
						(int) entry.System,
						(int) entry.Position,
						Enum.Parse<Celestials>(entry.Type.ToString())
					);
					if (celestial.HasCoords(entryCoords))
						return entry;
				}
			} catch {
				// Fehlerhafter Eintrag: ignorieren, es gelten dann die bisherigen Gruppenwerte.
			}
			return null;
		}

		/// <summary>
		/// Basis = Colony-Wert (sonst globaler Wert, sonst der bisher berechnete Wert), darueber
		/// der Wert aus dem Override-Eintrag, falls dort gesetzt.
		/// </summary>
		public static int Resolve(dynamic entry, dynamic autoMineSettings, string propName, int current) {
			int value = current;
			if (SettingsService.IsSettingSet(autoMineSettings, "Colony") &&
				SettingsService.IsSettingSet(autoMineSettings.Colony, propName)) {
				value = SettingsService.GetSetting<int>(autoMineSettings.Colony, propName, current);
			} else if (SettingsService.IsSettingSet(autoMineSettings, propName)) {
				value = SettingsService.GetSetting<int>(autoMineSettings, propName, current);
			}
			if (SettingsService.IsSettingSet(entry, propName))
				value = SettingsService.GetSetting<int>(entry, propName, value);
			return value;
		}
	}
}
