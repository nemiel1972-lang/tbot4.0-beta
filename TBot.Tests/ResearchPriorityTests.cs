using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Serilog.Events;
using Tbot.Includes;
using Tbot.Workers.Brain;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class ResearchPriorityTests {
		private class NoOpLogger : ILoggerService<CalculationService> {
			public void WriteLog(LogLevel level, LogSender source, string message) { }
			public void ConfigureLogging(string logPath) { }
			public void AddTelegramLogger(string botToken, string chatId) { }
			public void RemoveTelegramLogger() { }
			public bool IsTelegramLoggerEnabled() => false;
			public void SetTelegramLoggerLogLevel(LogEventLevel logLevel) { }
			public LogEventLevel GetTelegramLoggerLevel() => LogEventLevel.Information;
		}

		private static ResearchPriorityInput Input() => new ResearchPriorityInput {
			PrioritizePlasma = true, PrioritizeAstrophysics = true,
			Plasma = 19, MaxPlasma = 65, Astrophysics = 20, MaxAstrophysics = 23,
			Energy = 12, Laser = 12, Ion = 6, Espionage = 8, Impulse = 10, ResearchLab = 8,
			PlasmaDOIR = 5000, AstroDOIR = 10, LastDOIR = 100, MaxDaysOfInvestmentReturn = 9999,
		};

		[Fact]
		public void PlasmaIsAHardPriorityOverAstrophysics() {
			var d = ResearchPriorityLogic.Decide(Input());
			Assert.Equal(PriorityResearchPick.Plasma, d.Pick);
		}

		[Fact]
		public void PlasmaHardPriorityIgnoresPaybackEvenWhenLastDoirIsUnknown() {
			var i = Input();
			i.LastDOIR = 0;
			Assert.Equal(PriorityResearchPick.Plasma, ResearchPriorityLogic.Decide(i).Pick);
		}

		[Fact]
		public void PlasmaAtConfiguredCapIsNotChosenAgain() {
			var i = Input();
			i.Plasma = 65;
			var d = ResearchPriorityLogic.Decide(i);
			Assert.NotEqual(PriorityResearchPick.Plasma, d.Pick);
			Assert.Contains("cap 65", d.ToString());
			Assert.Equal(PriorityResearchPick.Astrophysics, d.Pick);
		}

		[Fact]
		public void BothFlagsActiveAstrophysicsDoesNotDisplacePlasma() {
			var i = Input();
			i.Astrophysics = 20; i.Plasma = 40;
			Assert.Equal(PriorityResearchPick.Plasma, ResearchPriorityLogic.Decide(i).Pick);
		}

		[Fact]
		public void PlasmaDisabledAstrophysicsFollowsItsOwnRules() {
			var i = Input();
			i.PrioritizePlasma = false;
			Assert.Equal(PriorityResearchPick.Astrophysics, ResearchPriorityLogic.Decide(i).Pick);
		}

		[Fact]
		public void PlasmaNotUnlockedLogsTheReasonAndFallsBack() {
			var i = Input();
			i.ResearchLab = 3;
			var d = ResearchPriorityLogic.Decide(i);
			Assert.Contains("requirements missing", d.ToString());
			Assert.Equal(PriorityResearchPick.Astrophysics, d.Pick);
		}

		[Fact]
		public void PlasmaNotUnlockedAndNothingElseLeavesFallbackToCheapestResearch() {
			var i = Input();
			i.Laser = 5; i.PrioritizeAstrophysics = false;
			var d = ResearchPriorityLogic.Decide(i);
			Assert.Equal(PriorityResearchPick.None, d.Pick);
			Assert.Contains("falling back to the cheapest research", d.ToString());
		}

		[Fact]
		public void PaybackCheckIsOptIn() {
			var i = Input();
			i.PlasmaChecksPayback = true;
			var d = ResearchPriorityLogic.Decide(i);
			Assert.Equal(PriorityResearchPick.Astrophysics, d.Pick);
			Assert.Contains("payback", d.ToString());
			i.PlasmaDOIR = 50;
			Assert.Equal(PriorityResearchPick.Plasma, ResearchPriorityLogic.Decide(i).Pick);
		}

		[Fact]
		public void AstrophysicsUsesTwoStepCapRule() {
			var i = Input();
			i.PrioritizePlasma = false;
			i.Astrophysics = 21; i.MaxAstrophysics = 22;
			var d = ResearchPriorityLogic.Decide(i);
			Assert.Equal(PriorityResearchPick.None, d.Pick);
			Assert.Contains("needs cap >= 23", d.ToString());
		}

		[Fact]
		public void EnergyPriorityComesAfterPlasmaBeforeAstrophysics() {
			var i = Input();
			i.PrioritizePlasma = false; i.PrioritizeEnergy = true; i.EnergyWanted = true;
			Assert.Equal(PriorityResearchPick.Energy, ResearchPriorityLogic.Decide(i).Pick);
			i.PrioritizePlasma = true;
			Assert.Equal(PriorityResearchPick.Plasma, ResearchPriorityLogic.Decide(i).Pick);
		}


		[Fact]
		public void ConfiguredMaxPlasma65SurvivesJsonRoundTrip() {
			var json = JObject.Parse("{\"Brain\":{\"AutoResearch\":{\"MaxPlasmaTechnology\":65,\"PrioritizePlasmaTechnology\":true}}}");
			var reloaded = JObject.Parse(json.ToString());
			dynamic settings = reloaded;
			Assert.Equal(65, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology);
		}

		[Fact]
		public void GetNextResearchRespectsConfiguredPlasmaCap() {
			var svc = new CalculationService(new NoOpLogger(), ogameService: null);
			var planet = new Planet { Facilities = new Facilities { ResearchLab = 11 } };
			var researches = new Researches { PlasmaTechnology = 20, EnergyTechnology = 12, LaserTechnology = 12, IonTechnology = 6 };

			Buildables Next(int maxPlasma) => svc.GetNextResearchToBuild(planet, researches, false, null,
				maxEnergyTechnology: 0, maxLaserTechnology: 0, maxIonTechnology: 0, maxHyperspaceTechnology: 0, maxPlasmaTechnology: maxPlasma,
				maxCombustionDrive: 0, maxImpulseDrive: 0, maxHyperspaceDrive: 0, maxEspionageTechnology: 0, maxComputerTechnology: 0,
				maxAstrophysics: 0, maxIntergalacticResearchNetwork: 0, maxWeaponsTechnology: 0, maxShieldingTechnology: 0, maxArmourTechnology: 0,
				optimizeForStart: false, ensureExpoSlots: false, ForceResearchWhateverTheLabLevel: true);

			Assert.Equal(Buildables.PlasmaTechnology, Next(65));
			Assert.Equal(Buildables.Null, Next(20));
		}
	}
}
