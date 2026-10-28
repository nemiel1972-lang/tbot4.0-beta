using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers {
	public static class LithiumClient {
		const string OrionQuery = "page=ingame&component=orion&asJson=1&action=";

		static KeyValuePair<string, string> F(string k, string v) => new KeyValuePair<string, string>(k, v);

		public static (bool Ok, string Message) Check(string json) {
			try {
				var o = JObject.Parse(json);
				bool ok = (bool?) o["success"] ?? string.Equals((string) o["status"], "success", StringComparison.OrdinalIgnoreCase);
				string msg = (string) o["message"] ?? (string) o["error"]?["message"] ?? "";
				return (ok, msg);
			} catch (Exception e) {
				return (false, "unreadable answer: " + e.Message);
			}
		}

		public static Task<string> Start(IOgameService o, string resource = "metal", int workload = 100) =>
			o.PostPageForm(OrionQuery + "startLithiumConversion", F("lithiumConvertingResource", resource), F("lithiumConvertingWorkload", workload.ToString()), F("token", "auto"));

		public static Task<string> Stop(IOgameService o, long spaceObjectId) =>
			o.PostPageForm(OrionQuery + "stopLithiumConversion", F("spaceObjectId", spaceObjectId.ToString()), F("token", "auto"));

		public static Task<string> FetchProduction(IOgameService o) =>
			o.PostPageForm("page=ingame&component=orion&ajax=1&action=fetchLithiumProduction", F("token", "auto"));

		public static List<long> ParseActiveConversions(string json) {
			var ids = new List<long>();
			try {
				string html = (string) JObject.Parse(json)["content"]?["orion"] ?? "";
				foreach (Match m in Regex.Matches(html, @"stopLithiumConversion\((\d+)\)"))
					if (long.TryParse(m.Groups[1].Value, out long id) && !ids.Contains(id)) ids.Add(id);
			} catch (Exception) { }
			return ids;
		}
	}
}
