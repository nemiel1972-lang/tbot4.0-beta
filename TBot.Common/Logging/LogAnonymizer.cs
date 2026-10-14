using System;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace TBot.Common.Logging {
	// Haelt Zugangs-/Kontodaten aus allen Logs heraus (ein gemeinsamer Ort, unabhaengig vom Logger-Typ).
	// Quellen: Umgebungsvariablen TBOT_LOG_PLAYERNAME / TBOT_LOG_UNIVERSENAME, zur Laufzeit registrierte Begriffe
	// (Login-Mail, Universum, Spielername) und ein Muster fuer beliebige E-Mail-Adressen.
	public static class LogAnonymizer {
		private static readonly string _playerName = Environment.GetEnvironmentVariable("TBOT_LOG_PLAYERNAME") ?? "";
		private static readonly string _universeName = Environment.GetEnvironmentVariable("TBOT_LOG_UNIVERSENAME") ?? "";
		private static readonly ConcurrentDictionary<string, string> _terms = new();
		private static readonly Regex _emailRegex = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);

		// Ab sofort wird der Begriff in jeder Logzeile ersetzt (ohne Neustart)
		public static void Register(string term, string replacement) {
			if (!string.IsNullOrWhiteSpace(term) && term.Length >= 3)
				_terms[term] = replacement;
		}

		public static string Apply(string message) {
			if (string.IsNullOrEmpty(message))
				return message;
			if (!string.IsNullOrEmpty(_playerName))
				message = Regex.Replace(message, Regex.Escape(_playerName), "[Player]", RegexOptions.IgnoreCase);
			if (!string.IsNullOrEmpty(_universeName))
				message = Regex.Replace(message, Regex.Escape(_universeName), "[Universe]", RegexOptions.IgnoreCase);
			foreach (var term in _terms)
				message = Regex.Replace(message, Regex.Escape(term.Key), term.Value, RegexOptions.IgnoreCase);
			return _emailRegex.Replace(message, "[Email]");
		}
	}
}
