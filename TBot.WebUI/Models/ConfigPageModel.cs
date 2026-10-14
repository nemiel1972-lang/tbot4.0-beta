namespace TBot.WebUI.Models {
	public class ConfigBlockInfo {
		public string Name { get; set; } = "";
		public bool? Active { get; set; }
		// custom = eigene Formularseite, generic = automatisch erzeugtes Formular, placeholder = noch nicht freigeschaltet, secret = nur ueber den JSON-Editor
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
