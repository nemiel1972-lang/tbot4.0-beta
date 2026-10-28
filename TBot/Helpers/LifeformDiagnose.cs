using System;
using System.Collections.Generic;
using System.Linq;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Helpers {
	public static class LifeformDiagnose {
		private const long T2Ziel = 11000000;
		private const long T3Ziel = 448000000;

		public static string Erklaere(ICalculationService calc, Celestial cel, LFBuildings max, bool preventIfMoreExpensiveThanNextMine) {
			try {
				var teile = new List<string>();
				var pop = cel.ResourcesProduction?.Population;
				float kosten = calc.CalcLFBuildingsResourcesCostBonus(cel);
				float popRed = calc.CalcLFBuildingsPopulationCostBonus(cel);
				float techRed = calc.CalcLFBuildingPopulationTechCapBonus(cel);
				if (pop != null) {
					double t2Ziel = T2Ziel - T2Ziel * techRed / 100.0, t3Ziel = T3Ziel - T3Ziel * techRed / 100.0;
					teile.Add($"population {Kurz(pop.Available)} of {Kurz(pop.LivingSpace)} living space, food for {Kurz(pop.Satisfied)}, hungry {Kurz(pop.Hungry)}; "
						+ $"T2 {Kurz(pop.T2Lifeforms)}/{Kurz((long) t2Ziel)}, T3 {Kurz(pop.T3Lifeforms)}/{Kurz((long) t3Ziel)}");
				}
				var eintraege = new List<(string Rolle, LFBuildables B)> {
					("population", calc.GetPopulationBuilding(cel.LFtype)),
					("food", calc.GetFoodBuilding(cel.LFtype)),
					("tech", calc.GetTechBuilding(cel.LFtype)),
					("T2", calc.GetT2Building(cel.LFtype)),
					("T3", calc.GetT3Building(cel.LFtype))
				};
				foreach (var b in calc.GetOtherBuildings(cel.LFtype))
					if (!eintraege.Any(e => e.B == b))
						eintraege.Add(("other", b));
				long wohnraum = calc.CalcLivingSpace(cel as Planet);
				long energie = (long) (cel.ResourcesProduction?.Energy?.CurrentProduction ?? 0);
				foreach (var (rolle, b) in eintraege) {
					if (b == LFBuildables.None)
						continue;
					int stufe = cel.GetLevel(b);
					int cap = max.GetLevel(b);
					int naechste = calc.GetNextLevel(cel, b);
					string grund;
					if (naechste > cap) {
						grund = "at the target";
					} else if (!calc.isUnlocked(cel, b)) {
						grund = "locked";
					} else {
						var preis = calc.CalcPrice(b, naechste, kosten, 0, popRed);
						if (preis.Population > wohnraum) grund = $"needs {Kurz(preis.Population)} population, living space {Kurz(wohnraum)}";
						else if (preis.Energy > energie) grund = $"needs {Kurz((long) preis.Energy)} energy, have {Kurz(energie)}";
						else grund = "possible";
					}
					teile.Add($"{rolle} {b} {stufe}/{cap}: {grund}");
				}
				if (preventIfMoreExpensiveThanNextMine)
					teile.Add("PreventIfMoreExpensiveThanNextMine is on (a building dearer than the next mine is held back)");
				return string.Join(" | ", teile);
			} catch (Exception e) {
				return "explanation not possible: " + e.Message;
			}
		}

		private static string Kurz(long v) {
			double a = Math.Abs((double) v);
			if (a >= 1e9) return (v / 1e9).ToString("0.##") + " bn";
			if (a >= 1e6) return (v / 1e6).ToString("0.##") + " M";
			if (a >= 1e3) return (v / 1e3).ToString("0.#") + " k";
			return v.ToString();
		}
	}
}
