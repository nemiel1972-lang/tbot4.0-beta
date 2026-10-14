using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Text;
using Tbot.Common.Extensions;

namespace Tbot.Common.Settings {
	public static class SettingsService {
		public static string GlobalSettingsPath { get; set; }
		public static string LogsPath { get; set; }

		private static SettingsSemaphoreManager _settingsSemaphoreManager = new SettingsSemaphoreManager();

		public static async Task WriteSettings(string settingsPath, string content) {
			var semaphore = _settingsSemaphoreManager.GetSemaphore(settingsPath);
			using (await semaphore.WriteLockAsync()) {
				await File.WriteAllTextAsync(settingsPath, content, Encoding.Default);
			}
		}

		// Unterordner (relativ zum Config-Ordner), in dem "Load": "name" ohne Pfad gesucht wird
		public const string DefaultLoadDirectory = "conf.d";
		// Ordner fuer Zugangsdaten (Passwort, Proxy, Webhooks, Tokens), per Load eingebunden
		public const string SecretsDirectory = "secrets";

		public static async Task<dynamic> GetSettings(string settingsPath) {
			string file = await GetSettingsFileContents(settingsPath);
			return FinalizeSettings(ParseAndResolve(file, settingsPath));
		}

		// Synchrone Variante fuer Leser, die den rohen JSON-Baum brauchen (z. B. WebUI-Dashboards): Load ist aufgeloest.
		public static JObject ReadResolvedObject(string settingsPath) {
			if (!File.Exists(settingsPath))
				throw new Exception($"{settingsPath} does not exist.");
			return ParseAndResolve(File.ReadAllText(settingsPath), settingsPath);
		}

		private static JObject ParseAndResolve(string json, string settingsPath) {
			var obj = ParseObject(json);
			ResolveLoadReferences(obj, Path.GetDirectoryName(Path.GetFullPath(settingsPath)));
			return obj;
		}

		// Datums-Strings bleiben Strings (sonst wuerde JObject sie beim Zurueckschreiben umformatieren)
		private static JObject ParseObject(string json) {
			using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
			return JObject.Load(reader);
		}

		private static dynamic FinalizeSettings(JObject settingsObj) {
			string json = settingsObj.ToString(Formatting.None);
			dynamic settings = JsonConvert.DeserializeObject<ExpandoObject>(json, new ExpandoObjectConverter());
			settings = ConfigObject.FromExpando(JsonNetAdapter.Transform(settings));
			return settings;
		}

		private static readonly JsonMergeSettings LoadMerge = new JsonMergeSettings {
			MergeArrayHandling = MergeArrayHandling.Replace,
			MergeNullValueHandling = MergeNullValueHandling.Ignore
		};

		// Loest Bloecke der Form { "Active": true, "Load": "name" } auf: Inhalt der Datei wird eingemischt,
		// Schluessel direkt neben "Load" (z. B. "Active") haben Vorrang. Fehlende Datei = Exception.
		private static void ResolveLoadReferences(JObject obj, string baseDir, int depth = 0) {
			if (obj == null)
				return;
			if (depth > 8)
				throw new Exception("Load references nested too deep (circular reference?).");

			foreach (var prop in obj.Properties().ToList()) {
				if (prop.Value is JObject child) {
					var references = GetLoadReferences(child);
					if (references != null) {
						// Mehrere Dateien werden der Reihe nach gemischt (spaetere gewinnen), danach gewinnen die Schluessel im Block selbst
						var loaded = new JObject();
						foreach (var reference in references) {
							string loadedPath = ResolveLoadPath(baseDir, reference);
							if (!File.Exists(loadedPath))
								throw new Exception($"Setting '{prop.Name}': Load reference '{reference}' (resolved to '{loadedPath}') was not found.");
							var part = ParseObject(File.ReadAllText(loadedPath));
							ResolveLoadReferences(part, baseDir, depth + 1);
							loaded.Merge(part, LoadMerge);
						}

						var overlay = (JObject) child.DeepClone();
						overlay.Remove("Load");
						ResolveLoadReferences(overlay, baseDir, depth + 1);
						loaded.Merge(overlay, LoadMerge);
						obj[prop.Name] = loaded;
					} else {
						ResolveLoadReferences(child, baseDir, depth + 1);
					}
				} else if (prop.Value is JArray arr) {
					foreach (var item in arr.OfType<JObject>().ToList())
						ResolveLoadReferences(item, baseDir, depth + 1);
				}
			}
		}

		// "Load": "name" oder "Load": ["name1", "name2"]; null, wenn der Block keinen Load hat
		private static List<string> GetLoadReferences(JObject block) {
			if (!block.TryGetValue("Load", StringComparison.OrdinalIgnoreCase, out var token))
				return null;
			if (token.Type == JTokenType.String)
				return new List<string> { token.Value<string>() };
			if (token is JArray arr && arr.All(t => t.Type == JTokenType.String))
				return arr.Select(t => t.Value<string>()).ToList();
			return null;
		}

		// "AutoMine" -> <baseDir>/conf.d/AutoMine.json; "secrets/credentials" -> <baseDir>/secrets/credentials.json.
		// Absolute Pfade und Ausbrueche aus dem Config-Ordner (..) sind verboten.
		public static string ResolveLoadPath(string baseDir, string reference) {
			string path = (reference ?? "").Trim().Replace('\\', '/');
			if (path.Length == 0)
				throw new Exception("Empty Load reference.");
			if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				path += ".json";
			if (Path.IsPathRooted(path))
				throw new Exception($"Load reference '{reference}': absolute paths are not allowed.");
			bool hasDirectory = path.Contains('/');
			string full = Path.GetFullPath(Path.Combine(baseDir, hasDirectory ? path : Path.Combine(DefaultLoadDirectory, path)));
			string root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
			if (!full.StartsWith(root, StringComparison.Ordinal))
				throw new Exception($"Load reference '{reference}' points outside the config directory.");
			return full;
		}

		// Alle Dateien, die per Load (rekursiv) in diese Datei eingebunden sind (fuer Anzeige/Watcher/Prueflogik)
		public static List<string> GetLoadedFiles(string settingsPath) {
			var result = new List<string>();
			if (!File.Exists(settingsPath))
				return result;
			string baseDir = Path.GetDirectoryName(Path.GetFullPath(settingsPath));
			CollectLoadedFiles(ParseObject(File.ReadAllText(settingsPath)), baseDir, result, 0);
			return result;
		}

		private static void CollectLoadedFiles(JContainer token, string baseDir, List<string> result, int depth) {
			if (depth > 8)
				return;
			foreach (var obj in token.DescendantsAndSelf().OfType<JObject>().ToList()) {
				var references = GetLoadReferences(obj);
				if (references == null)
					continue;
				foreach (var reference in references) {
					string path = ResolveLoadPath(baseDir, reference);
					if (!result.Contains(path) && File.Exists(path)) {
						result.Add(path);
						CollectLoadedFiles(ParseObject(File.ReadAllText(path)), baseDir, result, depth + 1);
					}
				}
			}
		}

		public static async Task<string> GetSettingsFileContents(string settingsPath) {
			if (File.Exists(settingsPath) == false) {
				throw new Exception($"{settingsPath} does not exist.");
			}
			var semaphore = _settingsSemaphoreManager.GetSemaphore(settingsPath);
			using (await semaphore.ReadLockAsync()) {
				string file = await File.ReadAllTextAsync(settingsPath);
				return file;
			}
		}

		public static async Task<dynamic> GetMergedSettings(string baseSettingsPath, List<string> overlaySettingsPath) {
            string baseJson = await GetSettingsFileContents(baseSettingsPath);
			List<string> overlayJson = new();
			foreach (var overlay in overlaySettingsPath) {
				overlayJson.Add(await GetSettingsFileContents(overlay));
			}
			var merged = MergeToJObject(baseJson, overlayJson);
			ResolveLoadReferences(merged, Path.GetDirectoryName(Path.GetFullPath(baseSettingsPath)));
			return FinalizeSettings(merged);
        }

        // Hinweis: ohne Dateipfad (nur Inhalte, z. B. Tests) wird Load bewusst nicht aufgeloest.
        public static dynamic GetMergedSettingsFromContents(string baseJson, List<string> overlayJson) {
            return FinalizeSettings(MergeToJObject(baseJson, overlayJson));
        }

		private static JObject MergeToJObject(string baseJson, List<string> overlayJson) {
			var baseObj = JsonConvert.DeserializeObject<JObject>(baseJson) ?? new JObject();
			var overlayObj = JsonConvert.DeserializeObject<JObject>(overlayJson.First()) ?? new JObject();
			foreach (var overlay in overlayJson.Skip(1)) {
				var nextOverlayObj = JsonConvert.DeserializeObject<JObject>(overlay) ?? new JObject();
				overlayObj.Merge(nextOverlayObj, LoadMerge);
			}
			baseObj.Merge(overlayObj, LoadMerge);
			return baseObj;
		}

		// Schreibt einzelne Werte dorthin zurueck, wo sie wirklich stehen: bei einem Block mit "Load" in die
		// eingebundene Datei (ausser der Schluessel steht direkt im Block neben "Load", dann dort).
		// Pfade wie "Brain.Transports.Origin.Galaxy" oder "Expeditions.Origin[0].Type". Fehlt ein Zwischenknoten, gibt es eine Exception.
		public static async Task SetValuesAsync(string rootPath, IEnumerable<KeyValuePair<string, object>> values) {
			string rootFull = Path.GetFullPath(rootPath);
			string baseDir = Path.GetDirectoryName(rootFull);
			var docs = new Dictionary<string, JObject>();
			var dirty = new HashSet<string>();

			JObject GetDoc(string full) {
				if (!docs.TryGetValue(full, out var doc)) {
					doc = ParseObject(File.ReadAllText(full));
					docs[full] = doc;
				}
				return doc;
			}

			// Originalinhalt der Hauptdatei ueber den Lock lesen (konsistent mit anderen Schreibern)
			docs[rootFull] = ParseObject(await GetSettingsFileContents(rootFull));

			foreach (var kv in values) {
				var segments = ParsePath(kv.Key);
				string curFile = rootFull;
				JToken cur = docs[rootFull];

				for (int i = 0; i < segments.Count; i++) {
					var seg = segments[i];
					bool last = i == segments.Count - 1;

					if (cur is JObject obj && seg.Name != null) {
						int guard = 0;
						List<string> refs;
						while (obj.Property(seg.Name) == null && (refs = GetLoadReferences(obj)) != null && guard++ < 8) {
							// Schluessel steht nicht im Block selbst: in der Load-Datei suchen, die ihn enthaelt (letzte gewinnt), sonst in der ersten
							string chosen = null;
							foreach (var reference in refs) {
								string lp = ResolveLoadPath(baseDir, reference);
								if (!File.Exists(lp))
									throw new Exception($"Load file for '{kv.Key}' not found: {lp}");
								if (GetDoc(lp).Property(seg.Name) != null)
									chosen = lp;
							}
							chosen ??= ResolveLoadPath(baseDir, refs[0]);
							obj = GetDoc(chosen);
							curFile = chosen;
							cur = obj;
						}
					}

					JToken newValue = last ? JToken.FromObject(kv.Value) : null;
					if (seg.Name != null) {
						if (cur is not JObject target)
							throw new Exception($"Path '{kv.Key}': '{seg.Name}' is not inside an object.");
						if (last) {
							target[seg.Name] = newValue;
							dirty.Add(curFile);
						} else {
							cur = target[seg.Name] ?? throw new Exception($"Path '{kv.Key}': '{seg.Name}' does not exist.");
						}
					} else {
						if (cur is not JArray arr || seg.Index >= arr.Count)
							throw new Exception($"Path '{kv.Key}': index {seg.Index} does not exist.");
						if (last) {
							arr[seg.Index] = newValue;
							dirty.Add(curFile);
						} else {
							cur = arr[seg.Index];
						}
					}
				}
			}

			foreach (var file in dirty)
				await WriteSettings(file, docs[file].ToString(Formatting.Indented));
		}

		private sealed class PathSegment {
			public string Name;
			public int Index;
		}

		private static List<PathSegment> ParsePath(string path) {
			var result = new List<PathSegment>();
			foreach (var part in path.Split('.')) {
				int bracket = part.IndexOf('[');
				string name = bracket < 0 ? part : part.Substring(0, bracket);
				if (name.Length > 0)
					result.Add(new PathSegment { Name = name });
				if (bracket >= 0) {
					foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(part.Substring(bracket), @"\[(\d+)\]"))
						result.Add(new PathSegment { Index = int.Parse(m.Groups[1].Value) });
				}
			}
			if (result.Count == 0)
				throw new Exception($"Invalid settings path '{path}'.");
			return result;
		}

		public static bool IsSettingSet(dynamic setting, string property) {
			try {
				var x = setting[property];
				if (x != null)
					return true;
				else
					return false;
			} catch {
				return false;
			}
		}

		public static T GetSetting<T>(dynamic setting, string property, T defValue) {
			if (IsSettingSet(setting, property))
				return (T) setting[property];
			else
				return defValue;
		}

		private class SettingsSemaphoreManager {
			private readonly SemaphoreSlim _dictSemaphore;
			private readonly Dictionary<string, AsyncReaderWriterLock> _settingsSemaphores;

			public SettingsSemaphoreManager() {
				_settingsSemaphores = new Dictionary<string, AsyncReaderWriterLock>();
				_dictSemaphore = new SemaphoreSlim(1, 1);
			}

			public AsyncReaderWriterLock GetSemaphore(string settingsPath) {
				if (!_settingsSemaphores.ContainsKey(settingsPath)) {

					_dictSemaphore.Wait();
					if (!_settingsSemaphores.ContainsKey(settingsPath)) {
						_settingsSemaphores.Add(settingsPath, new AsyncReaderWriterLock());
					}
					_dictSemaphore.Release();
				}
				return _settingsSemaphores[settingsPath];
			}
		}

	}

	public static class JsonNetAdapter {
		public static ExpandoObject Transform(ExpandoObject data) {
			var newExpando = new ExpandoObject();
			var edict = (IDictionary<string, object>) newExpando;

			foreach (var kvp in data)
				edict[kvp.Key.FirstCharToUpper()] = TransformByType(kvp.Value);

			return newExpando;
		}

		private static object TransformByType(object value) {
			return value switch {
				ExpandoObject _ => Transform((ExpandoObject) value),
				List<object> _ => ConvertList((List<object>) value),
				_ => value,
			};
		}

		private static object ConvertList(List<object> list) {
			var hasSingleType = true;
			var tList = new ArrayList(list.Count);

			Type listType = null;

			if (list.Count > 0)
				listType = list.First().GetType();

			foreach (var v in list) {
				hasSingleType = hasSingleType && listType == v.GetType();
				tList.Add(TransformByType(v));
			}

			return tList.ToArray(hasSingleType && listType != null ? listType : typeof(object));
		}
	}

	public class ConfigObject : DynamicObject, IDictionary<string, object> {
		internal Dictionary<string, object> Members = new();

		#region IEnumerable implementation

		public IEnumerator GetEnumerator() {
			return Members.GetEnumerator();
		}

		#endregion

		#region IEnumerable implementation

		IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() {
			return Members.GetEnumerator();
		}

		#endregion

		public static ConfigObject FromExpando(ExpandoObject e) {
			var edict = e as IDictionary<string, object>;
			var c = new ConfigObject();
			var cdict = (IDictionary<string, object>) c;

			// this is not complete. It will, however work for JsonFX ExpandoObjects which consists only of primitive types,
			// ExpandoObject or ExpandoObject [] but won't work for generic ExpandoObjects which might include collections etc.
			foreach (var kvp in edict) // recursively convert and add ExpandoObjects
				switch (kvp.Value) {
					case ExpandoObject o:
						cdict.Add(kvp.Key, FromExpando(o));
						break;
					case ExpandoObject[] _:
						cdict.Add(kvp.Key, (from ex in (ExpandoObject[]) kvp.Value select FromExpando(ex)).ToArray());
						break;
					default:
						cdict.Add(kvp.Key, kvp.Value);
						break;
				}
			return c;
		}

		public override bool TryGetMember(GetMemberBinder binder, out object result) {
			result = Members.ContainsKey(binder.Name) ? Members[binder.Name] : new NullExceptionPreventer();

			return true;
		}

		public override bool TrySetMember(SetMemberBinder binder, object value) {
			if (Members.ContainsKey(binder.Name))
				Members[binder.Name] = value;
			else
				Members.Add(binder.Name, value);
			return true;
		}

		public override string ToString() {
			return JsonConvert.SerializeObject(Members);
		}

		public static implicit operator ConfigObject(ExpandoObject exp) {
			return FromExpando(exp);
		}

		#region casts

		public static implicit operator bool(ConfigObject c) {
			// we want to test for a member:
			// if (config.SomeMember) { ... }
			//
			// instead of:
			// if (config.SomeMember != null) { ... }

			// we return always true, because a NullExceptionPreventer is returned when member
			// does not exist
			return true;
		}

		#endregion

		#region ICollection implementation

		public void Add(KeyValuePair<string, object> item) {
			Members.Add(item.Key, item.Value);
		}

		public void Clear() {
			Members.Clear();
		}

		public bool Contains(KeyValuePair<string, object> item) {
			return Members.ContainsKey(item.Key) && Members[item.Key] == item.Value;
		}

		public void CopyTo(KeyValuePair<string, object>[] array, int arrayIndex) {
			throw new NotImplementedException();
		}

		public bool Remove(KeyValuePair<string, object> item) {
			throw new NotImplementedException();
		}

		public int Count => Members.Count;

		public bool IsReadOnly => throw new NotImplementedException();

		#endregion

		#region IDictionary implementation

		public void Add(string key, object value) {
			Members.Add(key, value);
		}

		public bool ContainsKey(string key) {
			return Members.ContainsKey(key);
		}

		public bool Remove(string key) {
			return Members.Remove(key);
		}

		public object this[string key] {
			get => Members[key];
			set => Members[key] = value;
		}

		public ICollection<string> Keys => Members.Keys;

		public ICollection<object> Values => Members.Values;

		public bool TryGetValue(string key, out object value) {
			return Members.TryGetValue(key, out value);
		}

		#endregion
	}

	/// <summary>
	///     Null exception preventer. This allows for hassle-free usage of configuration values that are not
	///     defined in the config file. I.e. we can do Config.Scope.This.Field.Does.Not.Exist.Ever, and it will
	///     not throw an NullPointer exception, but return te NullExceptionPreventer object instead.
	///     The NullExceptionPreventer can be cast to everything, and will then return default/empty value of
	///     that datatype.
	/// </summary>
	public class NullExceptionPreventer : DynamicObject {
		// all member access to a NullExceptionPreventer will return a new NullExceptionPreventer
		// this allows for infinite nesting levels: var s = Obj1.foo.bar.bla.blubb; is perfectly valid
		public override bool TryGetMember(GetMemberBinder binder, out object result) {
			result = new NullExceptionPreventer();
			return true;
		}

		// Add all kinds of datatypes we can cast it to, and return default values cast to string will be null
		public static implicit operator string(NullExceptionPreventer nep) {
			return null;
		}

		public override string ToString() {
			return null;
		}

		public static implicit operator string[](NullExceptionPreventer nep) {
			return Array.Empty<string>();
		}

		// cast to bool will always be false
		public static implicit operator bool(NullExceptionPreventer nep) {
			return false;
		}

		public static implicit operator bool[](NullExceptionPreventer nep) {
			return Array.Empty<bool>();
		}

		public static implicit operator int[](NullExceptionPreventer nep) {
			return Array.Empty<int>();
		}

		public static implicit operator long[](NullExceptionPreventer nep) {
			return Array.Empty<long>();
		}

		public static implicit operator int(NullExceptionPreventer nep) {
			return 0;
		}

		public static implicit operator long(NullExceptionPreventer nep) {
			return 0;
		}

		// nullable types always return null
		public static implicit operator bool?(NullExceptionPreventer nep) {
			return null;
		}

		public static implicit operator int?(NullExceptionPreventer nep) {
			return null;
		}

		public static implicit operator long?(NullExceptionPreventer nep) {
			return null;
		}
	}
}
