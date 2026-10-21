using System;
using System.Collections.Generic;

namespace TBot.Ogame.Infrastructure.Models {
	public class ExpeditionMessage {
		public long ID { get; set; }
		public string Content { get; set; } = "";
		public Resources Resources { get; set; }
		public Dictionary<string, long> Ships { get; set; } = new();
		public DateTime CreatedAt { get; set; }
	}
}
