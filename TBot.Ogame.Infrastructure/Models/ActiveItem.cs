namespace TBot.Ogame.Infrastructure.Models {
	public class ActiveItem {
		public long ID { get; set; }
		public string Ref { get; set; } = "";
		public string Name { get; set; } = "";
		public long TimeRemaining { get; set; }
		public long TotalDuration { get; set; }
		public string ImgSmall { get; set; } = "";
	}
}
