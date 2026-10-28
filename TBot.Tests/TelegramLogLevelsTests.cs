using System.Collections.Generic;
using Serilog.Events;
using TBot.Common.Logging;
using Xunit;

namespace TBot.Tests {
	public class TelegramLogLevelsTests {
		private static IEnumerable<KeyValuePair<string, string>> Map(params (string Key, string Value)[] items) {
			var list = new List<KeyValuePair<string, string>>();
			foreach (var i in items)
				list.Add(new KeyValuePair<string, string>(i.Key, i.Value));
			return list;
		}

		[Fact]
		public void DefaultIsWarningWhenNothingIsConfigured() {
			var l = TelegramLogLevels.Parse(null, null);
			Assert.Equal(LogEventLevel.Warning, l.Default);
			Assert.True(l.Allows("Expeditions", LogEventLevel.Warning));
			Assert.True(l.Allows("Expeditions", LogEventLevel.Error));
			Assert.False(l.Allows("Expeditions", LogEventLevel.Information));
		}

		[Fact]
		public void WorkerCanBeSetHigherThanTheDefault() {
			var l = TelegramLogLevels.Parse("Warning", Map(("Expeditions", "Information"), ("AutoMine", "Debug")));
			Assert.True(l.Allows("Expeditions", LogEventLevel.Information));
			Assert.False(l.Allows("Expeditions", LogEventLevel.Debug));
			Assert.True(l.Allows("AutoMine", LogEventLevel.Debug));
			Assert.False(l.Allows("Defender", LogEventLevel.Information));
			Assert.True(l.Allows("Defender", LogEventLevel.Warning));
		}

		[Fact]
		public void WorkerCanBeSetLowerOrSwitchedOff() {
			var l = TelegramLogLevels.Parse("Information", Map(("AutoFarm", "Error"), ("Harvest", "Off")));
			Assert.False(l.Allows("AutoFarm", LogEventLevel.Warning));
			Assert.True(l.Allows("AutoFarm", LogEventLevel.Error));
			Assert.False(l.Allows("Harvest", LogEventLevel.Fatal));
			Assert.True(l.Allows("Expeditions", LogEventLevel.Information));
		}

		[Fact]
		public void DefaultEmptyAndUnknownEntriesFallBackToTheDefault() {
			var l = TelegramLogLevels.Parse("Warning", Map(("A", "Default"), ("B", ""), ("C", "  "), ("D", "Nonsense"), ("", "Debug"), (" ", "Debug")));
			foreach (var s in new[] { "A", "B", "C", "D" }) {
				Assert.False(l.Allows(s, LogEventLevel.Information));
				Assert.True(l.Allows(s, LogEventLevel.Warning));
			}
		}

		[Fact]
		public void SenderNamesAndLevelNamesIgnoreCase() {
			var l = TelegramLogLevels.Parse("warning", Map(("expeditions", "INFORMATION")));
			Assert.True(l.Allows("Expeditions", LogEventLevel.Information));
			Assert.True(l.Allows("EXPEDITIONS", LogEventLevel.Information));
		}

		[Fact]
		public void MissingSenderUsesTheDefault() {
			var l = TelegramLogLevels.Parse("Error", Map(("Expeditions", "Debug")));
			Assert.False(l.Allows(null, LogEventLevel.Warning));
			Assert.True(l.Allows(null, LogEventLevel.Error));
			Assert.False(l.Allows("", LogEventLevel.Warning));
		}

		[Fact]
		public void InvalidOrOffDefaultBecomesWarning() {
			Assert.Equal(LogEventLevel.Warning, TelegramLogLevels.Parse("Nonsense", null).Default);
			Assert.Equal(LogEventLevel.Warning, TelegramLogLevels.Parse("", null).Default);
			Assert.Equal(LogEventLevel.Warning, TelegramLogLevels.Parse("Off", null).Default);
		}

		[Fact]
		public void MicrosoftLevelNamesAreUnderstood() {
			var l = TelegramLogLevels.Parse("Trace", Map(("X", "Critical")));
			Assert.Equal(LogEventLevel.Verbose, l.Default);
			Assert.True(l.Allows("X", LogEventLevel.Fatal));
			Assert.False(l.Allows("X", LogEventLevel.Error));
		}

		[Fact]
		public void LowestIsTheLowestNeededLevelAndOffDoesNotLowerIt() {
			Assert.Equal(LogEventLevel.Warning, TelegramLogLevels.Parse("Warning", null).Lowest);
			Assert.Equal(LogEventLevel.Information, TelegramLogLevels.Parse("Warning", Map(("Expeditions", "Information"), ("AutoFarm", "Error"))).Lowest);
			Assert.Equal(LogEventLevel.Warning, TelegramLogLevels.Parse("Warning", Map(("AutoFarm", "Off"))).Lowest);
			Assert.Equal(LogEventLevel.Debug, TelegramLogLevels.Parse("Debug", Map(("AutoFarm", "Error"))).Lowest);
		}

		[Fact]
		public void WithDefaultKeepsTheWorkerEntries() {
			var l = TelegramLogLevels.Parse("Warning", Map(("Expeditions", "Information"), ("Harvest", "Off")));
			var m = l.WithDefault(LogEventLevel.Debug);
			Assert.Equal(LogEventLevel.Debug, m.Default);
			Assert.True(m.Allows("Defender", LogEventLevel.Debug));
			Assert.False(m.Allows("Harvest", LogEventLevel.Fatal));
			Assert.False(m.Allows("Expeditions", LogEventLevel.Debug));
			Assert.Equal(LogEventLevel.Warning, l.Default);
		}

		[Fact]
		public void DescribeListsOnlyTheDifferingWorkers() {
			Assert.Equal("default Warning", TelegramLogLevels.OnlyDefault(LogEventLevel.Warning).Describe());
			Assert.Equal("default Warning, AutoFarm=Off, Expeditions=Information",
				TelegramLogLevels.Parse("Warning", Map(("Expeditions", "Information"), ("AutoFarm", "Off"))).Describe());
		}
	}
}
