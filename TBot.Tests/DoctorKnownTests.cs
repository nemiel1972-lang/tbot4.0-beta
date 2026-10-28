using System;
using System.IO;
using Tbot.Common.Settings;
using Xunit;

namespace TBot.Tests {
	public class DoctorKnownTests : IDisposable {
		private readonly string _alias = "knowntest_" + Guid.NewGuid().ToString("N");
		public void Dispose() { try { File.Delete(Path.Combine(AppContext.BaseDirectory, "data", $"doctor_known_{_alias}.json")); } catch (Exception) { } }

		[Fact]
		public void MarkAndUnmarkAndClear() {
			Assert.Empty(DoctorKnown.Read(_alias));
			DoctorKnown.Set(_alias, "Slots.AutoFarm", true);
			DoctorKnown.Set(_alias, "Slots.Brain.Limit", true);
			Assert.Equal(2, DoctorKnown.Read(_alias).Count);
			DoctorKnown.Set(_alias, "Slots.AutoFarm", false);
			Assert.Contains("Slots.Brain.Limit", DoctorKnown.Read(_alias));
			Assert.DoesNotContain("Slots.AutoFarm", DoctorKnown.Read(_alias));
			DoctorKnown.Clear(_alias);
			Assert.Empty(DoctorKnown.Read(_alias));
		}

		[Fact]
		public void EmptyKeyIsIgnored() {
			DoctorKnown.Set(_alias, "  ", true);
			Assert.Empty(DoctorKnown.Read(_alias));
		}
	}
}
