using Newtonsoft.Json.Linq;

namespace TBot.WebUI.Services {
	public static class SecretMasker {
		public const string Mask = "********";

		private static readonly HashSet<string> AlwaysSecret = new(StringComparer.OrdinalIgnoreCase) {
			"Password", "CaptchaAPIKey", "WebhookUrl", "API", "ChatId", "Email"
		};
		private static readonly HashSet<string> SecretInsideAuthOrProxy = new(StringComparer.OrdinalIgnoreCase) {
			"Username", "Address"
		};

		private static IEnumerable<JProperty> Properties(JToken root) {
			return root is JContainer container ? container.DescendantsAndSelf().OfType<JProperty>() : Enumerable.Empty<JProperty>();
		}

		private static bool IsSecret(JProperty prop, bool allStrings) {
			if (prop.Value.Type != JTokenType.String)
				return false;
			if (allStrings)
				return true;
			var value = prop.Value.Value<string>() ?? "";
			var name = prop.Name;
			if (AlwaysSecret.Contains(name))
				return true;
			if (name.Equals("Url", StringComparison.OrdinalIgnoreCase) && value.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase))
				return true;
			if (SecretInsideAuthOrProxy.Contains(name) && prop.Parent is JObject obj && obj.Parent is JProperty parentProp
				&& (parentProp.Name.Equals("BasicAuth", StringComparison.OrdinalIgnoreCase) || parentProp.Name.Equals("Proxy", StringComparison.OrdinalIgnoreCase)))
				return true;
			return false;
		}

		public static string MaskJson(string json, bool allStrings = false) {
			var root = JToken.Parse(json);
			foreach (var prop in Properties(root).ToList()) {
				if (IsSecret(prop, allStrings) && !string.IsNullOrEmpty(prop.Value.Value<string>()))
					prop.Value = Mask;
			}
			return root.ToString(Newtonsoft.Json.Formatting.Indented);
		}

		public static string RestoreSecrets(string newJson, string originalJson, bool allStrings = false) {
			var newRoot = JToken.Parse(newJson);
			JToken originalRoot = null;
			try { originalRoot = JToken.Parse(originalJson); } catch { }

			foreach (var prop in Properties(newRoot).ToList()) {
				if (!IsSecret(prop, allStrings))
					continue;
				var original = originalRoot?.SelectToken(prop.Path);
				if (original != null && original.Type == JTokenType.String)
					prop.Value = original.DeepClone();
				else
					prop.Value = prop.Value.Value<string>() == Mask ? "" : prop.Value;
			}
			return newRoot.ToString(Newtonsoft.Json.Formatting.Indented);
		}
	}
}
