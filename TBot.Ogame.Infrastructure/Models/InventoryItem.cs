namespace TBot.Ogame.Infrastructure.Models {
	public class InventoryItem {
		public string Ref { get; set; } = "";
		public string Name { get; set; } = "";
		public string Title { get; set; } = "";
		public long Amount { get; set; }
		public long AmountFree { get; set; }
	}
}
