using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace TBot.Common.Logging {
	public static class LogAnonymizer {
		private static readonly string _playerName = Environment.GetEnvironmentVariable("TBOT_LOG_PLAYERNAME") ?? "";
		private static readonly string _universeName = Environment.GetEnvironmentVariable("TBOT_LOG_UNIVERSENAME") ?? "";
		private static readonly ConcurrentDictionary<string, string> _terms = new();

		public static bool Enabled { get; set; } = false;
		private static readonly Regex _doppelKlammer = new(@"\[(\[(?:Email|Player|Universe)\])\]", RegexOptions.Compiled);
		private static readonly Regex _emailRegex = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

		public static void Register(string term, string replacement) {
			if (!string.IsNullOrWhiteSpace(term) && term.Length >= 3)
				_terms[term] = replacement;
		}

		private static string GanzesWort(string term) => @"(?<![A-Za-z0-9_])" + Regex.Escape(term) + @"(?![A-Za-z0-9_])";

		public static string Apply(string message) {
			if (!Enabled || string.IsNullOrEmpty(message))
				return message;
			if (!string.IsNullOrEmpty(_playerName))
				message = Regex.Replace(message, GanzesWort(_playerName), "[Player]", RegexOptions.IgnoreCase);
			if (!string.IsNullOrEmpty(_universeName))
				message = Regex.Replace(message, GanzesWort(_universeName), "[Universe]", RegexOptions.IgnoreCase);
			foreach (var term in _terms)
				message = Regex.Replace(message, GanzesWort(term.Key), term.Value, RegexOptions.IgnoreCase);
			message = _emailRegex.Replace(message, "[Email]");
			return _doppelKlammer.Replace(message, "$1");
		}
	}
}
