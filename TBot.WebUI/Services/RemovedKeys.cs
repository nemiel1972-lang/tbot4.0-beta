using Newtonsoft.Json.Linq;
using System.Reflection;

namespace TBot.WebUI.Services {
	public static class RemovedKeys {
		private static readonly Lazy<List<string[]>> Pfade = new(Laden);

		private static List<string[]> Laden() {
			var liste = new List<string[]>();
			try {
				var asm = typeof(RemovedKeys).Assembly;
				using var s = asm.GetManifestResourceStream("removed_keys.txt");
				if (s == null)
					return liste;
				using var r = new StreamReader(s);
				string? zeile;
				while ((zeile = r.ReadLine()) != null) {
					zeile = zeile.Trim();
					if (zeile.Length == 0 || zeile.StartsWith("#"))
						continue;
					int hash = zeile.IndexOf('#');
					string pfad = (hash >= 0 ? zeile.Substring(0, hash) : zeile).Trim();
					if (pfad.Length > 0)
						liste.Add(pfad.Split('.'));
				}
			} catch {
			}
			return liste;
		}

		public static int Entfernen(string block, JObject data) {
			int anzahl = 0;
			foreach (var teile in Pfade.Value) {
				if (teile.Length < 2 || !string.Equals(teile[0], block, StringComparison.OrdinalIgnoreCase))
					continue;
				JToken? knoten = data;
				for (int i = 1; i < teile.Length - 1 && knoten != null; i++)
					knoten = (knoten as JObject)?.GetValue(teile[i], StringComparison.OrdinalIgnoreCase);
				if (knoten is JObject o && o.Property(teile[^1], StringComparison.OrdinalIgnoreCase) is JProperty p) {
					p.Remove();
					anzahl++;
				}
			}
			return anzahl;
		}
	}
}
