using TBot.Common.Logging;
using Xunit;

namespace TBot.Tests {
	public class LogSaveAnonymizeTests {
		[Fact]
		public void CleansEverythingEvenWhenAnonymizeLogsIsOff() {
			LogAnonymizer.Enabled = false;
			LogAnonymizer.Register("TestSpielerXY", "[Player]");
			LogAnonymizer.RegisterCelestial("Heimatwelt");
			string s = LogAnonymizer.ApplyForSave("TestSpielerXY: Heimatwelt [P:2:357:8] mail a.b@example.com ip 10.1.2.3 at /home/tbot/TBot4.0-livetest/x.cs token abcdefghijklmnopqrstuvwxyz0123456789");
			Assert.DoesNotContain("TestSpielerXY", s);
			Assert.DoesNotContain("Heimatwelt", s);
			Assert.Contains("Celestial [P:x:x:8]", s);
			Assert.DoesNotContain("example.com", s);
			Assert.DoesNotContain("10.1.2.3", s);
			Assert.Contains("/home/<user>/", s);
			Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", s);
		}

		[Fact]
		public void NormalApplyIsUnchangedWhenOff() {
			LogAnonymizer.Enabled = false;
			Assert.Equal("Heimatwelt [P:2:357:8]", LogAnonymizer.Apply("Heimatwelt [P:2:357:8]"));
		}
	}
}
