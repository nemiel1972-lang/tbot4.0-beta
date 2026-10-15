namespace TBot.WebUI.Models {
	public class ConfigBlockInfo {
		public string Name { get; set; } = "";
		public bool? Active { get; set; }
		public string Label { get; set; } = "";
		public string Mode { get; set; } = "placeholder";
	}

	public class ConfigPageModel {
		public List<string> Instances { get; set; } = new();
		public string Instance { get; set; } = "";
		public List<ConfigBlockInfo> Blocks { get; set; } = new();
		public string Selected { get; set; } = "";
		public string SelectedMode { get; set; } = "placeholder";
		public string? Error { get; set; }
	}
}
