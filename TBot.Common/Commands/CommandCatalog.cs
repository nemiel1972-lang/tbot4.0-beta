using System.Text.RegularExpressions;

namespace Tbot.Common.Commands {
	public sealed record CommandSpec(string Name, string Group, string Short, string Syntax, string Example, Func<string[], string?> Check);

	public sealed record CommandCheckResult(bool Known, bool Ok, string Command, string Message, string? Syntax, string? Short);

	public static class CommandCatalog {
		private static readonly Regex Coord = new(@"^\d{1,2}:\d{1,3}:\d{1,2}$", RegexOptions.Compiled);
		private static readonly Regex Duration = new(@"^(?=\d)(\d+h)?(\d+m)?(\d+s)?(\d+)?$", RegexOptions.Compiled);
		private static readonly Regex Word = new(@"^[a-z]+$", RegexOptions.Compiled);

		private static string? None(string[] a) => a.Length == 0 ? null : "this command takes no arguments";
		private static bool IsCoord(string s) => Coord.IsMatch(s);
		private static bool IsDuration(string s) => Duration.IsMatch(s);
		private static bool IsInt(string s, int min, int max) => int.TryParse(s, out int v) && v >= min && v <= max;
		private static bool IsType(string s) => s is "moon" or "planet";

		private static string? Range(string[] a, int min, int max, string expected) =>
			a.Length < min || a.Length > max ? "expected: " + expected : null;

		private static string? DurationMission(string[] a) {
			if (a.Length < 1 || a.Length > 2) return "expected: <duration> [mission], e.g. 4h30m harvest";
			if (!IsDuration(a[0])) return $"'{a[0]}' is not a duration (e.g. 4h30m, 3m50s)";
			if (a.Length == 2 && !Word.IsMatch(a[1])) return $"'{a[1]}' is not a mission name (e.g. harvest)";
			return null;
		}

		private static readonly List<CommandSpec> Raw = new() {
			new("setmain", "Core", "Select the TBot instance to control", "/setmain <number>", "/setmain 0",
				a => a.Length == 1 && IsInt(a[0], 0, 999) ? null : "expected: one instance number, e.g. 0"),
			new("getmain", "Core", "Show the controlled instance", "/getmain", "/getmain", None),
			new("getmainstats", "Core", "Statistics of the controlled instance", "/getmainstats", "/getmainstats", None),
			new("listinstances", "Core", "List all TBot instances", "/listinstances", "/listinstances", None),
			new("loglevel", "Core", "Show the Telegram log level", "/loglevel", "/loglevel", None),
			new("setloglevel", "Core", "Set the Telegram log level", "/setloglevel Debug|Information|Warning|Error", "/setloglevel Warning",
				a => a.Length == 1 && a[0] is "debug" or "information" or "warning" or "error" ? null : "expected: Debug, Information, Warning or Error"),
			new("ping", "Core", "Ping the bot", "/ping", "/ping", None),
			new("stopautoping", "Core", "Stop the automatic ping", "/stopautoping", "/stopautoping", None),
			new("startautoping", "Core", "Start the automatic ping", "/startautoping", "/startautoping", None),
			new("help", "Core", "Full help text", "/help", "/help", None),

			new("getfleets", "Fleets", "Show fleets that are on their way out", "/getfleets", "/getfleets", None),
			new("cancel", "Fleets", "Recall one fleet by id", "/cancel <fleet id>", "/cancel 65656",
				a => a.Length == 1 && IsInt(a[0], 1, int.MaxValue) ? null : "expected: one fleet id (number)"),
			new("cancelmission", "Fleets", "Recall all fleets of one mission", "/cancelmission <mission>", "/cancelmission deploy",
				a => a.Length == 1 && Word.IsMatch(a[0]) ? null : "expected: one mission name, e.g. deploy"),
			new("recall", "Fleets", "Switch automatic recall on or off", "/recall true|false", "/recall true",
				a => a.Length == 1 && a[0] is "true" or "false" ? null : "expected: true or false"),
			new("debrissave", "Fleets", "Harvest debris at low speed and be back after the duration", "/debrissave <duration> [coords]  |  /debrissave <coords|rnd> <duration> <speed 1-5>", "/debrissave 1h20 1:100:8",
				a => {
					if (a.Length == 1) return IsDuration(a[0]) ? null : $"'{a[0]}' is not a duration (e.g. 1h20)";
					if (a.Length == 2 && IsDuration(a[0]) && IsCoord(a[1])) return null;
					if (a.Length == 3 && (IsCoord(a[0]) || a[0] == "rnd") && IsDuration(a[1]) && IsInt(a[2], 1, 5)) return null;
					return "expected: <duration>, <duration> <coords> or <coords|rnd> <duration> <speed 1-5>";
				}),
			new("ghostsleep", "Sleep", "Fleetsave for the current celestial, then sleep", "/ghostsleep <duration> [mission]", "/ghostsleep 4h30m harvest", DurationMission),
			new("ghostsleepall", "Sleep", "Fleetsave for all celestials, then sleep", "/ghostsleepall <duration> [mission]", "/ghostsleepall 4h30m harvest", DurationMission),
			new("ghost", "Sleep", "Ghost flight for the given duration", "/ghost <duration> [mission]", "/ghost 4h3m harvest", DurationMission),
			new("ghostto", "Sleep", "Ghost flight of the current celestial", "/ghostto <duration> [mission]", "/ghostto 4h30m harvest", DurationMission),
			new("ghostmoons", "Sleep", "Ghost flight of the moons", "/ghostmoons <duration> [mission]", "/ghostmoons 4h30m harvest", DurationMission),
			new("cancelghostsleep", "Sleep", "Cancel a planned ghostsleep", "/cancelghostsleep", "/cancelghostsleep", None),
			new("sleep", "Sleep", "Stop the bot for a duration", "/sleep <duration>", "/sleep 4h3m",
				a => a.Length == 1 && IsDuration(a[0]) ? null : "expected: one duration, e.g. 4h3m or 3m50s"),
			new("wakeup", "Sleep", "Wake the bot up", "/wakeup", "/wakeup", None),
			new("pause", "Sleep", "Pause all workers except the Defender for a few minutes", "/pause [minutes]", "/pause 10",
				a => a.Length == 0 || (a.Length == 1 && IsInt(a[0], 1, 1440)) ? null : "expected: optional minutes (number)"),
			new("resume", "Sleep", "End the pause at once", "/resume", "/resume", None),

			new("switch", "Move", "Move resources and fleet to the planet or moon", "/switch <speed 1-10>", "/switch 5",
				a => a.Length == 1 && IsInt(a[0], 1, 10) ? null : "expected: speed 1-10"),
			new("trans", "Move", "Transport the celestial's resources to its moon", "/trans <speed 1-10>", "/trans 5",
				a => a.Length == 1 && IsInt(a[0], 1, 10) ? null : "expected: speed 1-10"),
			new("deploy", "Move", "Deploy ships and resources to a celestial", "/deploy <coords> moon|planet <speed 1-10>", "/deploy 3:41:9 moon 10",
				a => a.Length == 3 && IsCoord(a[0]) && IsType(a[1]) && IsInt(a[2], 1, 10) ? null : "expected: <coords> moon|planet <speed 1-10>, e.g. 3:41:9 moon 10"),
			new("jumpgate", "Move", "Jump with the fleet to another moon", "/jumpgate <coords> auto|full", "/jumpgate 2:41:9 auto",
				a => a.Length == 2 && IsCoord(a[0]) && a[1] is "auto" or "full" ? null : "expected: <coords> auto|full, e.g. 2:41:9 auto"),
			new("fleetjumpgate", "Move", "Run the jump gate worker now", "/fleetjumpgate", "/fleetjumpgate", None),
			new("phalanx", "Move", "Scan a target with the phalanx", "/phalanx <moon coords> <target coords>", "/phalanx 2:241:9 4:100:1",
				a => a.Length == 2 && IsCoord(a[0]) && IsCoord(a[1]) ? null : "expected: two coordinates, e.g. 2:241:9 4:100:1"),
			new("spycrash", "Move", "Crash a probe to create a debris field", "/spycrash <coords|auto>", "/spycrash auto",
				a => a.Length == 1 && (IsCoord(a[0]) || a[0] == "auto") ? null : "expected: coordinates or auto"),

			new("collect", "Collect", "Collect resources at the repatriate target", "/collect [planet|moon] [skip]", "/collect moon skip",
				a => CollectArgs(a)),
			new("collectall", "Collect", "Like /collect without the minimum amount", "/collectall [planet|moon] [skip]", "/collectall planet",
				a => CollectArgs(a)),
			new("collectdeut", "Collect", "Collect only deuterium (from this amount)", "/collectdeut <minimum amount>", "/collectdeut 500000",
				a => a.Length == 1 && IsInt(a[0], 0, int.MaxValue) ? null : "expected: one whole number (minimum deuterium)"),

			new("build", "Build", "Build a unit on every planet", "/build <ship or defence> [number]", "/build LightFighter 100",
				a => a.Length >= 1 && a.Length <= 2 && Regex.IsMatch(a[0], "^[a-z]+$") && (a.Length == 1 || IsInt(a[1], 1, int.MaxValue)) ? null : "expected: <name> [number], e.g. LightFighter 100"),
			new("msg", "Defender", "Send a message to the current attacker", "/msg <text>", "/msg hello",
				a => a.Length >= 1 ? null : "expected: the message text"),
			new("attacked", "Defender", "Check whether you are under attack", "/attacked", "/attacked", None),
			new("getcelestials", "Info", "List your celestials", "/getcelestials", "/getcelestials", None),
			new("celestial", "Info", "Select the current celestial", "/celestial <coords> moon|planet", "/celestial 2:45:8 moon",
				a => a.Length == 2 && IsCoord(a[0]) && IsType(a[1]) ? null : "expected: <coords> moon|planet"),
			new("getinfo", "Info", "Resources and ships of a celestial", "/getinfo [<coords> moon|planet]", "/getinfo 2:45:8 moon",
				a => a.Length == 0 || (a.Length == 2 && IsCoord(a[0]) && IsType(a[1])) ? null : "expected: nothing, or <coords> moon|planet"),
			new("editsettings", "Info", "Change the origin/target celestials in the settings", "/editsettings <coords> moon|planet", "/editsettings 2:425:9 moon",
				a => a.Length == 2 && IsCoord(a[0]) && IsType(a[1]) ? null : "expected: <coords> moon|planet"),
			new("minexpecargo", "Workers", "Change MinPrimaryToSend of the expeditions", "/minexpecargo <number>", "/minexpecargo 100",
				a => a.Length == 1 && IsInt(a[0], 0, int.MaxValue) ? null : "expected: one whole number"),

			Toggle("expe", "expeditions"), Toggle("defender", "the Defender"), Toggle("autoresearch", "AutoResearch"), Toggle("automine", "AutoMine"),
			Toggle("lifeformautomine", "Lifeform AutoMine"), Toggle("lifeformautoresearch", "Lifeform AutoResearch"),
			Toggle("autofarm", "AutoFarm"), Toggle("autodiscovery", "AutoDiscovery"),
		};

		private static string? CollectArgs(string[] a) {
			bool typ = false, skip = false;
			foreach (string t in a) {
				if (t is "planet" or "moon" && !typ) typ = true;
				else if (t == "skip" && !skip) skip = true;
				else return $"unknown argument '{t}'; allowed: planet or moon, and skip";
			}
			return null;
		}

		private static CommandSpec Toggle(string name, string label) => new("start|stop" + name, "Workers", $"Start or stop {label}", $"/start{name}  |  /stop{name}", $"/stop{name}", None);

		private static IEnumerable<CommandSpec> Expanded() {
			foreach (CommandSpec s in Raw) {
				if (!s.Name.StartsWith("start|stop")) { yield return s; continue; }
				string n = s.Name.Substring("start|stop".Length);
				yield return s with { Name = "start" + n, Short = s.Short.Replace("Start or stop", "Start"), Syntax = "/start" + n, Example = "/start" + n };
				yield return s with { Name = "stop" + n, Short = s.Short.Replace("Start or stop", "Stop"), Syntax = "/stop" + n, Example = "/stop" + n };
			}
		}

		private static int Distance(string a, string b) {
			int[,] d = new int[a.Length + 1, b.Length + 1];
			for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
			for (int j = 0; j <= b.Length; j++) d[0, j] = j;
			for (int i = 1; i <= a.Length; i++)
				for (int j = 1; j <= b.Length; j++)
					d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
			return d[a.Length, b.Length];
		}

		private static IReadOnlyList<CommandSpec>? _list;
		public static IReadOnlyList<CommandSpec> Commands => _list ??= Expanded().ToList();

		public static CommandCheckResult Check(string? line) {
			string text = (line ?? "").Trim();
			if (text.Length == 0)
				return new(false, false, "", "Enter a command, e.g. /collect moon skip", null, null);
			string[] parts = text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
			string cmd = parts[0];
			if (!cmd.StartsWith('/'))
				return new(false, false, cmd, "A command starts with a slash, e.g. /collect", null, null);
			int at = cmd.IndexOf('@');
			if (at > 0) cmd = cmd.Substring(0, at);
			string name = cmd.TrimStart('/');
			CommandSpec? spec = Commands.FirstOrDefault(s => s.Name == name);
			if (spec == null) {
				string? near = Commands.Select(s => s.Name).Where(n => n.StartsWith(name) || (name.Length > 3 && n.Contains(name)) || Distance(n, name) <= 2)
					.OrderBy(n => Distance(n, name)).FirstOrDefault();
				return new(false, false, cmd, "Unknown command" + (near != null ? $" - did you mean /{near}?" : ""), null, null);
			}
			string? error = spec.Check(parts.Skip(1).ToArray());
			return error == null
				? new(true, true, cmd, "Syntax ok - nothing was sent or executed (check only).", spec.Syntax, spec.Short)
				: new(true, false, cmd, "Syntax error: " + error, spec.Syntax, spec.Short);
		}
	}
}
