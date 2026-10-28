using System;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Tbot.Includes;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class CalculationServiceTests {
		private class NoOpLogger : ILoggerService<CalculationService> {
			public void WriteLog(LogLevel level, LogSender source, string message) { }
			public void ConfigureLogging(string logPath) { }
			public void AddTelegramLogger(string botToken, string chatId) { }
			public void RemoveTelegramLogger() { }
			public bool IsTelegramLoggerEnabled() => false;
			public void SetTelegramLoggerLogLevel(LogEventLevel logLevel) { }
			public LogEventLevel GetTelegramLoggerLevel() => LogEventLevel.Information;
		}

		private static CalculationService Svc() => new CalculationService(new NoOpLogger(), ogameService: null);

		private static ServerData Server(int hyperMultiplier = 5) => new ServerData { CargoHyperspaceTechMultiplier = hyperMultiplier };


		[Fact]
		public void DeuteriumProductionFollowsSynthesizerNotCrystalMine_Planet() {
			var s = Svc();
			var planet = new Planet {
				Buildings = new Buildings { CrystalMine = 1, DeuteriumSynthesizer = 20 },
				Temperature = new Temperature { Min = -20, Max = 20 },
				Coordinate = new Coordinate(1, 1, 8, Celestials.Planet),
			};
			long expected = s.CalcDeuteriumProduction(20, 0, 1);
			long wrong = s.CalcDeuteriumProduction(1, 0, 1);
			Assert.NotEqual(expected, wrong);
			Assert.Equal(expected, s.CalcDeuteriumProduction(planet, 1));
		}

		[Fact]
		public void DeuteriumProductionFollowsSynthesizerNotCrystalMine_Buildings() {
			var s = Svc();
			var buildings = new Buildings { CrystalMine = 30, DeuteriumSynthesizer = 5 };
			var temp = new Temperature { Min = 0, Max = 0 };
			Assert.Equal(s.CalcDeuteriumProduction(5, 0, 1), s.CalcDeuteriumProduction(buildings, temp, 1));
		}

		[Fact]
		public void DeuteriumProductionZeroSynthesizerIsZeroEvenWithCrystalMine() {
			var s = Svc();
			var buildings = new Buildings { CrystalMine = 30, DeuteriumSynthesizer = 0 };
			Assert.Equal(0, s.CalcDeuteriumProduction(buildings, new Temperature { Min = 0, Max = 0 }, 1));
		}


		[Fact]
		public void DeuteriumLfBonusIsPercentOfBaseProduction() {
			var s = Svc();
			long none = s.CalcDeuteriumProduction(10, 0, 1);
			long with10 = s.CalcDeuteriumProduction(10, 0, 1, 1, 0, 10f);
			Assert.Equal(none + (long) Math.Round(none * 10 / 100.0), with10);
			Assert.True(with10 > none + 10);
		}

		[Fact]
		public void CrystalLfBonusIsPercentOfBaseProduction() {
			var s = Svc();
			long none = s.CalcCrystalProduction(10, 8, 1);
			long with10 = s.CalcCrystalProduction(10, 8, 1, 1, 0, 10f);
			Assert.Equal(none + (long) Math.Round(none * 10 / 100.0), with10);
		}

		[Fact]
		public void DeuteriumAndCrystalLfBonusZeroChangesNothing() {
			var s = Svc();
			Assert.Equal(s.CalcDeuteriumProduction(12, 20, 2), s.CalcDeuteriumProduction(12, 20, 2, 1, 0, 0f));
			Assert.Equal(s.CalcCrystalProduction(12, 8, 2), s.CalcCrystalProduction(12, 8, 2, 1, 0, 0f));
		}


		[Fact]
		public void BomberConsumptionStaysAt700WithHyperspaceDrive8() {
			var s = Svc();
			Assert.Equal(700, s.CalcShipConsumption(Buildables.Bomber, 0, 7, 1.0));
			Assert.Equal(700, s.CalcShipConsumption(Buildables.Bomber, 0, 8, 1.0));
			Assert.Equal(700, s.CalcShipConsumption(Buildables.Bomber, 0, 15, 1.0));
		}

		[Fact]
		public void BomberSpeedRisesWithHyperspaceDrive8() {
			var s = Svc();
			Assert.Equal(8800, s.CalcShipSpeed(Buildables.Bomber, 0, 6, 7));
			Assert.Equal(17000, s.CalcShipSpeed(Buildables.Bomber, 0, 6, 8));
		}

		[Fact]
		public void RecyclerSpeedAndConsumptionSwitchDrivesAtImpulse17AndHyperspace15() {
			var s = Svc();
			Assert.Equal(2000, s.CalcShipSpeed(Buildables.Recycler, 0, 0, 0));
			Assert.Equal(17600, s.CalcShipSpeed(Buildables.Recycler, 0, 17, 0));
			Assert.Equal(33000, s.CalcShipSpeed(Buildables.Recycler, 0, 17, 15));
			Assert.Equal(300, s.CalcShipConsumption(Buildables.Recycler, 0, 0, 1.0));
			Assert.Equal(600, s.CalcShipConsumption(Buildables.Recycler, 17, 0, 1.0));
			Assert.Equal(900, s.CalcShipConsumption(Buildables.Recycler, 17, 15, 1.0));
		}


		[Fact]
		public void ShipSpeedWithoutLfBonusIsUnchanged() {
			var s = Svc();
			Assert.Equal(10000, s.CalcShipSpeed(Buildables.SmallCargo, 10, 0, 0, 0f));
		}

		[Fact]
		public void ShipSpeedLfBonusIsFractionOfBaseSpeed() {
			var s = Svc();
			Assert.Equal(10250, s.CalcShipSpeed(Buildables.SmallCargo, 10, 0, 0, 0.05f));
		}


		[Fact]
		public void CargoCapacityWithoutAnyBonusIsExactBaseValue() {
			var s = Svc();
			Assert.Equal(5000, s.CalcShipCapacity(Buildables.SmallCargo, 0, Server(), 0f));
			Assert.Equal(25000, s.CalcShipCapacity(Buildables.LargeCargo, 0, Server(), 0f));
		}

		[Fact]
		public void CargoCapacityHyperspaceBonusMatchesOverloadWithoutLf() {
			var s = Svc();
			Assert.Equal(43750, s.CalcShipCapacity(Buildables.LargeCargo, 15, Server(), 0f));
			Assert.Equal(s.CalcShipCapacity(Buildables.LargeCargo, 15, Server()), s.CalcShipCapacity(Buildables.LargeCargo, 15, Server(), 0f));
		}

		[Fact]
		public void CargoCapacityLfBonusIsTruncatedNeverOverestimated() {
			var s = Svc();
			Assert.Equal(44750, s.CalcShipCapacity(Buildables.LargeCargo, 15, Server(), 0.0487f));
			Assert.True(s.CalcShipCapacity(Buildables.LargeCargo, 15, Server(), 0.0487f) <= 44967);
		}

		[Fact]
		public void CargoCapacityCollectorBonus() {
			var s = Svc();
			Assert.Equal(6250, s.CalcShipCapacity(Buildables.SmallCargo, 0, Server(), 0f, CharacterClass.Collector));
		}
	}
}
