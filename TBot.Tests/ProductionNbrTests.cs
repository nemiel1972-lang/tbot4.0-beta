using System.Collections.Generic;
using Newtonsoft.Json;
using TBot.Ogame.Infrastructure.Models;
using Xunit;

namespace TBot.Tests {
	public class ProductionNbrTests {
		[Fact]
		public void NormalValueIsKept() {
			var l = JsonConvert.DeserializeObject<List<Production>>("[{\"ID\":212,\"Nbr\":40}]");
			Assert.Equal(40, l[0].Nbr);
		}

		[Fact]
		public void HugeValueIsClampedInsteadOfFailing() {
			var l = JsonConvert.DeserializeObject<List<Production>>("[{\"ID\":212,\"Nbr\":9783497825},{\"ID\":209,\"Nbr\":3}]");
			Assert.Equal(2, l.Count);
			Assert.Equal(int.MaxValue, l[0].Nbr);
			Assert.Equal(3, l[1].Nbr);
		}
	}
}
