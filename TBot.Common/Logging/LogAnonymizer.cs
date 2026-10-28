using System;
using System.Collections.Concurrent;
using System.Linq;
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

		private static readonly ConcurrentDictionary<string, byte> _celestialNames = new();
		public static void RegisterCelestial(string name) {
			if (!string.IsNullOrWhiteSpace(name) && name.Length >= 3)
				_celestialNames[name] = 0;
		}

		private static readonly Regex _koordinate = new(@"\b([1-9]):(\d{1,3}):([1-9]\d?)\b", RegexOptions.Compiled);
		private static readonly Regex _ip = new(@"\b\d{1,3}(\.\d{1,3}){3}\b", RegexOptions.Compiled);
		private static readonly Regex _token = new(@"\b[A-Za-z0-9_\-:]{28,}\b", RegexOptions.Compiled);
		private static readonly Regex _homePfad = new(@"/home/[^/\s]+/", RegexOptions.Compiled);

		public static string ApplyForSave(string message) {
			if (string.IsNullOrEmpty(message))
				return message;
			message = Core(message);
			foreach (var name in _celestialNames.Keys.OrderByDescending(n => n.Length))
				message = message.Replace(name + " [", "Celestial [");
			message = _koordinate.Replace(message, "x:x:$3");
			message = _ip.Replace(message, "<ip>");
			message = _token.Replace(message, "<token>");
			return _homePfad.Replace(message, "/home/<user>/");
		}

		public static string Apply(string message) {
			if (!Enabled || string.IsNullOrEmpty(message))
				return message;
			return Core(message);
		}

		private static string Core(string message) {
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
