using System.Collections.Generic;

namespace Tbot.Workers.Brain {
	public enum PriorityResearchPick { None, Plasma, Energy, Astrophysics }

	public class ResearchPriorityInput {
		public bool PrioritizePlasma { get; set; }
		public bool PrioritizeEnergy { get; set; }
		public bool PrioritizeAstrophysics { get; set; }
		public bool PlasmaChecksPayback { get; set; }

		public int Plasma { get; set; }
		public int MaxPlasma { get; set; }
		public int Astrophysics { get; set; }
		public int MaxAstrophysics { get; set; }
		public int Energy { get; set; }
		public int Laser { get; set; }
		public int Ion { get; set; }
		public int Espionage { get; set; }
		public int Impulse { get; set; }
		public int ResearchLab { get; set; }

		public float PlasmaDOIR { get; set; }
		public float AstroDOIR { get; set; }
		public float LastDOIR { get; set; }
		public float MaxDaysOfInvestmentReturn { get; set; }

		public bool EnergyWanted { get; set; }
	}

	public class ResearchPriorityDecision {
		public PriorityResearchPick Pick { get; set; } = PriorityResearchPick.None;
		public List<string> Reasons { get; } = new List<string>();
		public override string ToString() => string.Join("; ", Reasons);
	}

	public static class ResearchPriorityLogic {
		public static ResearchPriorityDecision Decide(ResearchPriorityInput i) {
			var d = new ResearchPriorityDecision();

			if (i.PrioritizePlasma) {
				if (i.MaxPlasma < i.Plasma + 1) {
					d.Reasons.Add($"Plasma technology skipped: level {i.Plasma} reached the configured cap {i.MaxPlasma}");
				} else if (i.ResearchLab < 4 || i.Energy < 8 || i.Laser < 10 || i.Ion < 5) {
					d.Reasons.Add($"Plasma technology skipped: requirements missing (research lab {i.ResearchLab}/4, energy {i.Energy}/8, laser {i.Laser}/10, ion {i.Ion}/5)");
				} else if (i.PlasmaChecksPayback && !(i.LastDOIR > 0 && i.PlasmaDOIR <= i.LastDOIR && i.PlasmaDOIR <= i.MaxDaysOfInvestmentReturn)) {
					d.Reasons.Add($"Plasma technology skipped: payback {i.PlasmaDOIR:F1} d is above the limit (last mine payback {i.LastDOIR:F1} d, max {i.MaxDaysOfInvestmentReturn:F0} d; PlasmaPriorityChecksPayback is on)");
				} else {
					d.Pick = PriorityResearchPick.Plasma;
					d.Reasons.Add($"Plasma technology chosen: priority on, level {i.Plasma} -> {i.Plasma + 1} of cap {i.MaxPlasma} (payback {i.PlasmaDOIR:F1} d is not compared)");
					return d;
				}
			}

			if (i.PrioritizeEnergy && i.EnergyWanted) {
				d.Pick = PriorityResearchPick.Energy;
				d.Reasons.Add("Energy technology chosen: priority on and it saves energy sources");
				return d;
			}

			if (i.PrioritizeAstrophysics) {
				int needed = i.Astrophysics % 2 == 0 ? i.Astrophysics + 1 : i.Astrophysics + 2;
				if (i.MaxAstrophysics < needed) {
					d.Reasons.Add($"Astrophysics skipped: level {i.Astrophysics} needs cap >= {needed}, configured cap is {i.MaxAstrophysics}");
				} else if (i.LastDOIR <= 0) {
					d.Reasons.Add("Astrophysics skipped: no mine payback measured yet");
				} else if (i.AstroDOIR > i.MaxDaysOfInvestmentReturn || i.AstroDOIR > i.LastDOIR) {
					d.Reasons.Add($"Astrophysics skipped: payback {i.AstroDOIR:F1} d is above the limit (last mine payback {i.LastDOIR:F1} d, max {i.MaxDaysOfInvestmentReturn:F0} d)");
				} else if (i.ResearchLab < 3 || i.Espionage < 4 || i.Impulse < 3) {
					d.Reasons.Add($"Astrophysics skipped: requirements missing (research lab {i.ResearchLab}/3, espionage {i.Espionage}/4, impulse {i.Impulse}/3)");
				} else {
					d.Pick = PriorityResearchPick.Astrophysics;
					d.Reasons.Add($"Astrophysics chosen: priority on, payback {i.AstroDOIR:F1} d within the limits");
					return d;
				}
			}

			d.Reasons.Add("no priority research applies, falling back to the cheapest research");
			return d;
		}
	}
}
