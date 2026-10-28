using System;
using System.Collections.Generic;
using TBot.Common.Logging;
using Xunit;

namespace TBot.Tests {
	public class TelegramRepeatLimitTests {
		private static readonly DateTime T0 = new DateTime(2026, 10, 25, 10, 0, 0, DateTimeKind.Utc);

		private static IEnumerable<KeyValuePair<string, string>> Map(params (string Key, string Value)[] items) {
			var list = new List<KeyValuePair<string, string>>();
			foreach (var i in items)
				list.Add(new KeyValuePair<string, string>(i.Key, i.Value));
			return list;
		}

		[Fact]
		public void OffByDefaultSendsEverything() {
			var l = TelegramRepeatLimit.Off();
			Assert.True(l.ShouldSend("Expeditions", "Next check at 10:27", T0));
			Assert.True(l.ShouldSend("Expeditions", "Next check at 10:27", T0));
		}

		[Fact]
		public void SameMessageIsHeldBackUntilTheTimeIsUp() {
			var l = TelegramRepeatLimit.Parse(60, null);
			Assert.True(l.ShouldSend("Expeditions", "Next check at 10:27", T0));
			Assert.False(l.ShouldSend("Expeditions", "Next check at 10:27", T0.AddMinutes(30)));
			Assert.True(l.ShouldSend("Expeditions", "Next check at 10:27", T0.AddMinutes(60)));
		}

		[Fact]
		public void NumbersAreIgnoredWhenComparing() {
			var l = TelegramRepeatLimit.Parse(60, null);
			Assert.True(l.ShouldSend("Expeditions", "Expedition origin not found: 2:250:8 (Planet) at 10:27", T0));
			Assert.False(l.ShouldSend("Expeditions", "Expedition origin not found: 2:250:8 (Planet) at 10:33", T0.AddMinutes(6)));
			Assert.True(l.ShouldSend("Expeditions", "Expedition origin not found: Moon", T0.AddMinutes(6)));
		}

		[Fact]
		public void OtherSenderAndOtherTextAreIndependent() {
			var l = TelegramRepeatLimit.Parse(60, null);
			Assert.True(l.ShouldSend("Expeditions", "Hello", T0));
			Assert.True(l.ShouldSend("AutoFarm", "Hello", T0));
			Assert.True(l.ShouldSend("Expeditions", "Other", T0));
		}

		[Fact]
		public void PerWorkerValueOverridesTheDefaultIncludingZero() {
			var l = TelegramRepeatLimit.Parse(60, Map(("AutoFarm", "0"), ("Harvest", "5"), ("X", "abc"), ("Y", "-3"), ("", "5")));
			Assert.True(l.ShouldSend("AutoFarm", "Hello", T0));
			Assert.True(l.ShouldSend("AutoFarm", "Hello", T0));
			Assert.True(l.ShouldSend("Harvest", "Hello", T0));
			Assert.False(l.ShouldSend("Harvest", "Hello", T0.AddMinutes(4)));
			Assert.True(l.ShouldSend("Harvest", "Hello", T0.AddMinutes(5)));
			Assert.Equal(60, l.MinutesFor("X"));
			Assert.Equal(60, l.MinutesFor("Y"));
			Assert.Equal(60, l.MinutesFor(null));
		}

		[Fact]
		public void SenderNamesIgnoreCase() {
			var l = TelegramRepeatLimit.Parse(0, Map(("expeditions", "30")));
			Assert.True(l.ShouldSend("Expeditions", "Hello", T0));
			Assert.False(l.ShouldSend("EXPEDITIONS", "Hello", T0.AddMinutes(1)));
		}

		[Fact]
		public void NegativeDefaultCountsAsOff() {
			Assert.Equal(0, TelegramRepeatLimit.Parse(-5, null).DefaultMinutes);
		}

		[Fact]
		public void ManyDifferentMessagesDoNotGrowWithoutLimit() {
			var l = TelegramRepeatLimit.Parse(60, null);
			for (int i = 0; i < 5000; i++) {
				string text = "";
				for (int n = i + 1; n > 0; n /= 26)
					text += (char) ('a' + n % 26);
				Assert.True(l.ShouldSend("S", text, T0.AddSeconds(i)));
			}
		}

		[Fact]
		public void DescribeShowsDefaultAndWorkers() {
			Assert.Equal("repeat limit off", TelegramRepeatLimit.Off().Describe());
			Assert.Equal("repeat limit 60 min, AutoFarm=0, Expeditions=30", TelegramRepeatLimit.Parse(60, Map(("Expeditions", "30"), ("AutoFarm", "0"))).Describe());
		}
	}
}
