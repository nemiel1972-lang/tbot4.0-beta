using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Events;
using Serilog.Formatting;

namespace TBot.Common.Logging.TextFormatters {
	public class SerilogCSVTextFormatter : ITextFormatter {
		public void Format(LogEvent logEvent, TextWriter output) {
			string message;
			if (logEvent.Exception != null) {
				message = $"EXCEPTION:{logEvent.Exception.ToString()}. {logEvent.MessageTemplate.ToString()}";
			} else {
				message = $"{logEvent.MessageTemplate.ToString()}";
			}
			output.Write("{0},{1},{2},{3}{4}",
				EscapeForCSV(logEvent.Level.ToString()),
				EscapeForCSV(logEvent.Properties["LogSender"].ToString()),
				EscapeForCSV(DateTime.Now.ToString(CultureInfo.InvariantCulture)),
				EscapeForCSV(message),
				output.NewLine);
		}

		public static string EscapeForCSV(string str) {
			bool mustQuote = (str.Contains(",") || str.Contains("\"") || str.Contains("\r") || str.Contains("\n"));
			if (mustQuote) {
				StringBuilder sb = new StringBuilder();
				sb.Append("\"");
				foreach (char nextChar in str) {
					sb.Append(nextChar);
					if (nextChar == '"')
						sb.Append("\"");
				}
				sb.Append("\"");
				return sb.ToString();
			}

			return str;
		}
	}
}
