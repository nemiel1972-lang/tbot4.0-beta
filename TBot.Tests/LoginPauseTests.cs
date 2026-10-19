using Tbot.Services;
using Xunit;

namespace TBot.Tests {
	public class LoginPauseTests {
		private static LoginPauseConfig Cfg(int vorher = 2, int pause = 30, int max = 120) => new LoginPauseConfig { FailuresBeforePause = vorher, PauseMinutes = pause, MaxPauseMinutes = max };

		[Theory]
		[InlineData(1, 0)]
		[InlineData(2, 30)]
		[InlineData(3, 60)]
		[InlineData(4, 120)]
		[InlineData(5, 120)]
		[InlineData(9, 120)]
		public void PauseMinutes_verdoppelt_sich_bis_zum_Maximum(int fehler, int erwartet) {
			Assert.Equal(erwartet, LoginPause.PauseMinutes(fehler, Cfg()));
		}

		[Fact]
		public void PauseMinutes_Schwelle_1_pausiert_sofort() {
			Assert.Equal(10, LoginPause.PauseMinutes(1, Cfg(1, 10, 60)));
		}

		[Fact]
		public void PauseMinutes_Maximum_kleiner_als_Pause_wird_nicht_unterschritten() {
			Assert.Equal(30, LoginPause.PauseMinutes(5, Cfg(2, 30, 10)));
		}
	}
}
