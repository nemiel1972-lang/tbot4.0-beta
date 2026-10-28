using System.Reflection;

namespace TBot.Common {
	public static class BuildInfo {
		private static string Meta(string key) =>
			typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

		public static string BuildTimeUtc => Meta("BuildTimeUtc");
		public static string GitCommit => Meta("GitCommit");

		public static string Display {
			get {
				var parts = new List<string>();
				if (!string.IsNullOrEmpty(GitCommit)) parts.Add(GitCommit);
				if (!string.IsNullOrEmpty(BuildTimeUtc)) parts.Add(BuildTimeUtc + " UTC");
				return string.Join(" - ", parts);
			}
		}
	}
}
