using Tbot.Common.Commands;
using Xunit;

namespace TBot.Tests {
	public class CommandCatalogTests {
		[Theory]
		[InlineData("/collect", true)]
		[InlineData("/collect moon skip", true)]
		[InlineData("/collect skip planet", true)]
		[InlineData("/COLLECTALL Moon", true)]
		[InlineData("/collect moon planet", false)]
		[InlineData("/collect foo", false)]
		[InlineData("/collectdeut 500000", true)]
		[InlineData("/collectdeut", false)]
		[InlineData("/debrissave 1h20", true)]
		[InlineData("/debrissave 1h20 1:100:8", true)]
		[InlineData("/debrissave rnd 2h 5", true)]
		[InlineData("/debrissave 1:1:1 2h 9", false)]
		[InlineData("/deploy 3:41:9 moon 10", true)]
		[InlineData("/deploy 3:41:9 sun 10", false)]
		[InlineData("/jumpgate 2:41:9 auto", true)]
		[InlineData("/jumpgate 2:41:9", false)]
		[InlineData("/sleep 4h3m", true)]
		[InlineData("/sleep abc", false)]
		[InlineData("/ghost 3m50s harvest", true)]
		[InlineData("/pause", true)]
		[InlineData("/pause 10", true)]
		[InlineData("/pause x", false)]
		[InlineData("/ping extra", false)]
		[InlineData("/stopexpe", true)]
		[InlineData("/startautofarm", true)]
		[InlineData("/collect@mybot", true)]
		public void ChecksSyntax(string line, bool ok) {
			Assert.Equal(ok, CommandCatalog.Check(line).Ok);
		}

		[Fact]
		public void UnknownCommandAndMissingSlash() {
			var unknown = CommandCatalog.Check("/colect");
			Assert.False(unknown.Known);
			Assert.Contains("collect", unknown.Message);
			Assert.False(CommandCatalog.Check("collect").Known);
			Assert.False(CommandCatalog.Check("").Ok);
		}

		[Fact]
		public void NamesAreUnique() {
			var names = CommandCatalog.Commands.Select(c => c.Name).ToList();
			Assert.Equal(names.Count, names.Distinct().Count());
		}
	}
}
