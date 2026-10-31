using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tbot.Common.IO;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using ResModel = TBot.Ogame.Infrastructure.Models.Resources;
using ShipsModel = TBot.Ogame.Infrastructure.Models.Ships;

namespace Tbot.Workers {
	public static class StartDataFreshness {
		public const string Resources = "res", Ships = "ships", Production = "prod";

		public class Entry {
			public ResModel Res; public DateTime? ResAt;
			public ShipsModel Ships; public DateTime? ShipsAt;
			public ResourcesProduction Prod; public DateTime? ProdAt;
		}

		private static readonly ConcurrentDictionary<int, Entry> Data = new();
		private static volatile bool _dirty;
		private static DateTime _lastSave = DateTime.MinValue;

		public static void Mark(Celestial c, UpdateTypes type) {
			if (c == null)
				return;
			var e = Data.GetOrAdd(c.ID, _ => new Entry());
			var now = DateTime.UtcNow;
			lock (e) {
				switch (type) {
					case UpdateTypes.Resources: e.Res = c.Resources; e.ResAt = now; break;
					case UpdateTypes.Ships: e.Ships = c.Ships; e.ShipsAt = now; break;
					case UpdateTypes.Techs: e.Ships = c.Ships; e.ShipsAt = now; break;
					case UpdateTypes.ResourcesProduction: e.Prod = c.ResourcesProduction; e.ProdAt = now; break;
					case UpdateTypes.Full:
						e.Res = c.Resources; e.ResAt = now;
						e.Ships = c.Ships; e.ShipsAt = now;
						e.Prod = c.ResourcesProduction; e.ProdAt = now;
						break;
					default: return;
				}
			}
			_dirty = true;
		}

		public static ResModel GetResources(Celestial c) => (c != null && Data.TryGetValue(c.ID, out var e) && e.Res != null) ? e.Res : c?.Resources;
		public static ShipsModel GetShips(Celestial c) => (c != null && Data.TryGetValue(c.ID, out var e) && e.Ships != null) ? e.Ships : c?.Ships;
		public static ResourcesProduction GetProduction(Celestial c) => (c != null && Data.TryGetValue(c.ID, out var e) && e.Prod != null) ? e.Prod : c?.ResourcesProduction;

		public static double AgeSeconds(Celestial c, string kind) {
			if (c != null && Data.TryGetValue(c.ID, out var e)) {
				DateTime? at = kind == Resources ? e.ResAt : kind == Ships ? e.ShipsAt : e.ProdAt;
				if (at.HasValue)
					return Math.Max(0, (DateTime.UtcNow - at.Value).TotalSeconds);
			}
			return double.MaxValue;
		}

		public class AgeSpan { public long newest, oldest; }

		public static AgeSpan AgeRange(IEnumerable<Celestial> list, string kind) {
			var ages = list.Select(c => AgeSeconds(c, kind)).Where(a => a < double.MaxValue).ToList();
			if (ages.Count == 0)
				return null;
			return new AgeSpan { newest = (long) ages.Min(), oldest = (long) ages.Max() };
		}

		public static void Load(string path) {
			try {
				if (!File.Exists(path))
					return;
				var d = JsonConvert.DeserializeObject<Dictionary<int, Entry>>(File.ReadAllText(path));
				if (d == null)
					return;
				foreach (var kv in d)
					Data[kv.Key] = kv.Value;
			} catch (Exception) {  }
		}

		public static void SaveIfDue(string path) {
			try {
				if (!_dirty || (DateTime.UtcNow - _lastSave).TotalMinutes < 2)
					return;
				_dirty = false;
				_lastSave = DateTime.UtcNow;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				AtomicFile.WriteAllText(path, JsonConvert.SerializeObject(Data.ToDictionary(kv => kv.Key, kv => kv.Value)));
			} catch (Exception) {  }
		}
	}
}
