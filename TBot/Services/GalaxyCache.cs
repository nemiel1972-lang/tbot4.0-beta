using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Tbot.Common.IO;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {

	public class GalaxyCacheEntry {
		public int Position { get; set; }
		public int PlayerId { get; set; }
		public string PlayerName { get; set; }
		public string Name { get; set; }
		public bool Inactive { get; set; }
		public bool Vacation { get; set; }
		public bool Newbie { get; set; }
		public bool Banned { get; set; }
		public bool Administrator { get; set; }
		public bool StrongPlayer { get; set; }
		public bool HasMoon { get; set; }
	}

	public class GalaxyCacheSystem {
		public DateTime SeenUtc { get; set; }
		public List<GalaxyCacheEntry> Entries { get; set; } = new();
	}

	public enum GalaxyEventType {
		New,
		Gone,
		Moon,
		Status,
		Move
	}

	public class GalaxyEvent {
		public DateTime TimeUtc { get; set; }
		public GalaxyEventType Type { get; set; }
		public int Galaxy { get; set; }
		public int System { get; set; }
		public int Position { get; set; }
		public int PlayerId { get; set; }
		public string PlayerName { get; set; }
		public string Detail { get; set; }
		public bool Matched { get; set; }

		public string Coord => $"{Galaxy}:{System}:{Position}";
	}

	public class GalaxyCacheData {
		public Dictionary<string, GalaxyCacheSystem> Systems { get; set; } = new();
		public List<GalaxyEvent> Events { get; set; } = new();
	}

	public class GalaxyCache {
		public const int MaxEvents = 500;
		public static readonly TimeSpan MoveWindow = TimeSpan.FromHours(72);

		private readonly object _lock = new();
		private GalaxyCacheData _data = new();
		private readonly string _path;
		private DateTime _lastSaveUtc = DateTime.MinValue;
		private bool _dirty;

		public int OwnPlayerId { get; set; }

		public GalaxyCache(string path) {
			_path = path;
		}

		private static string Key(int galaxy, int system) => $"{galaxy}:{system}";

		public int SystemCount {
			get { lock (_lock) return _data.Systems.Count; }
		}

		public void Load() {
			if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
				return;
			try {
				var d = JsonConvert.DeserializeObject<GalaxyCacheData>(File.ReadAllText(_path));
				if (d != null) {
					d.Systems ??= new();
					d.Events ??= new();
					lock (_lock)
						_data = d;
				}
			} catch (Exception) {
			}
		}

		public void SaveIfDue(DateTime nowUtc, bool force = false) {
			if (string.IsNullOrEmpty(_path))
				return;
			string json;
			lock (_lock) {
				if (!_dirty || (!force && (nowUtc - _lastSaveUtc).TotalSeconds < 60))
					return;
				json = JsonConvert.SerializeObject(_data);
				_dirty = false;
				_lastSaveUtc = nowUtc;
			}
			try {
				Directory.CreateDirectory(Path.GetDirectoryName(_path));
				AtomicFile.WriteAllText(_path, json);
			} catch (Exception) {
				lock (_lock)
					_dirty = true;
			}
		}

		private static GalaxyCacheEntry ToEntry(Planet p) {
			return new GalaxyCacheEntry {
				Position = p.Coordinate?.Position ?? 0,
				PlayerId = p.Player?.ID ?? 0,
				PlayerName = p.Player?.Name,
				Name = p.Name,
				Inactive = p.Inactive,
				Vacation = p.Vacation,
				Newbie = p.Newbie,
				Banned = p.Banned,
				Administrator = p.Administrator,
				StrongPlayer = p.StrongPlayer,
				HasMoon = p.Moon != null
			};
		}

		public List<GalaxyEvent> Record(GalaxyInfo info, DateTime nowUtc) {
			var events = new List<GalaxyEvent>();
			if (info?.Planets == null || info.Galaxy <= 0 || info.System <= 0)
				return events;
			var neu = info.Planets.Where(p => p != null && p.Coordinate != null && p.Coordinate.Position > 0).Select(ToEntry)
				.GroupBy(e => e.Position).Select(g => g.First()).OrderBy(e => e.Position).ToList();
			lock (_lock) {
				string key = Key(info.Galaxy, info.System);
				_data.Systems.TryGetValue(key, out var alt);
				if (alt != null) {
					var altMap = alt.Entries.ToDictionary(e => e.Position);
					var neuMap = neu.ToDictionary(e => e.Position);
					foreach (var n in neu) {
						if (!altMap.TryGetValue(n.Position, out var o)) {
							if (n.PlayerId != OwnPlayerId)
								events.Add(Make(GalaxyEventType.New, info, n, nowUtc, n.Name));
							continue;
						}
						if (o.PlayerId != n.PlayerId) {
							if (o.PlayerId != OwnPlayerId)
								events.Add(Make(GalaxyEventType.Gone, info, o, nowUtc, "owner changed"));
							if (n.PlayerId != OwnPlayerId)
								events.Add(Make(GalaxyEventType.New, info, n, nowUtc, n.Name));
							continue;
						}
						if (n.PlayerId == OwnPlayerId)
							continue;
						if (n.HasMoon && !o.HasMoon)
							events.Add(Make(GalaxyEventType.Moon, info, n, nowUtc, "new moon"));
						string statusAlt = Status(o), statusNeu = Status(n);
						if (statusAlt != statusNeu)
							events.Add(Make(GalaxyEventType.Status, info, n, nowUtc, $"{statusAlt} -> {statusNeu}"));
					}
					foreach (var o in alt.Entries) {
						if (!neuMap.ContainsKey(o.Position) && o.PlayerId != OwnPlayerId)
							events.Add(Make(GalaxyEventType.Gone, info, o, nowUtc, o.Name));
					}
					events.AddRange(DetectMoves(events, nowUtc));
				}
				_data.Systems[key] = new GalaxyCacheSystem { SeenUtc = nowUtc, Entries = neu };
				if (events.Count > 0) {
					_data.Events.AddRange(events);
					if (_data.Events.Count > MaxEvents)
						_data.Events = _data.Events.Skip(_data.Events.Count - MaxEvents).ToList();
				}
				_dirty = true;
			}
			return events;
		}

		private static string Status(GalaxyCacheEntry e) =>
			e.Banned ? "banned" : e.Vacation ? "vacation" : e.Inactive ? "inactive" : e.Newbie ? "newbie" : "active";

		private static GalaxyEvent Make(GalaxyEventType type, GalaxyInfo info, GalaxyCacheEntry e, DateTime nowUtc, string detail) {
			return new GalaxyEvent {
				TimeUtc = nowUtc, Type = type, Galaxy = info.Galaxy, System = info.System, Position = e.Position,
				PlayerId = e.PlayerId, PlayerName = e.PlayerName, Detail = detail
			};
		}

		private List<GalaxyEvent> DetectMoves(List<GalaxyEvent> frisch, DateTime nowUtc) {
			var result = new List<GalaxyEvent>();
			foreach (var n in frisch.Where(e => e.Type == GalaxyEventType.New && e.PlayerId > 0).ToList()) {
				var gone = _data.Events.Concat(frisch)
					.Where(e => e.Type == GalaxyEventType.Gone && !e.Matched && e.PlayerId == n.PlayerId
						&& (nowUtc - e.TimeUtc) <= MoveWindow && e.Coord != n.Coord)
					.OrderByDescending(e => e.TimeUtc).FirstOrDefault();
				if (gone == null)
					continue;
				gone.Matched = true;
				result.Add(new GalaxyEvent {
					TimeUtc = nowUtc, Type = GalaxyEventType.Move, Galaxy = n.Galaxy, System = n.System, Position = n.Position,
					PlayerId = n.PlayerId, PlayerName = n.PlayerName, Detail = $"from {gone.Coord}"
				});
			}
			return result;
		}

		public TimeSpan? SystemAge(int galaxy, int system, DateTime nowUtc) {
			lock (_lock) {
				return _data.Systems.TryGetValue(Key(galaxy, system), out var s) ? nowUtc - s.SeenUtc : null;
			}
		}

		public GalaxyInfo TryGet(int galaxy, int system, TimeSpan maxAge, DateTime nowUtc) {
			lock (_lock) {
				if (!_data.Systems.TryGetValue(Key(galaxy, system), out var s) || nowUtc - s.SeenUtc > maxAge)
					return null;
				return new GalaxyInfo {
					Galaxy = galaxy,
					System = system,
					Planets = s.Entries.Select(e => new Planet {
						Name = e.Name,
						Coordinate = new Coordinate(galaxy, system, e.Position, Celestials.Planet),
						Inactive = e.Inactive,
						Vacation = e.Vacation,
						Newbie = e.Newbie,
						Banned = e.Banned,
						Administrator = e.Administrator,
						StrongPlayer = e.StrongPlayer,
						Player = new Player { ID = e.PlayerId, Name = e.PlayerName },
						Moon = e.HasMoon ? new Moon() : null
					}).ToList()
				};
			}
		}

		public bool IsOccupiedFresh(Coordinate c, TimeSpan maxAge, DateTime nowUtc) {
			if (c == null)
				return false;
			lock (_lock) {
				if (!_data.Systems.TryGetValue(Key(c.Galaxy, c.System), out var s) || nowUtc - s.SeenUtc > maxAge)
					return false;
				return s.Entries.Any(e => e.Position == c.Position);
			}
		}

		public (int Galaxy, int System)? NextToScan(IEnumerable<(int Galaxy, int System)> candidates, TimeSpan maxAge, DateTime nowUtc) {
			lock (_lock) {
				(int Galaxy, int System)? best = null;
				DateTime bestSeen = DateTime.MaxValue;
				foreach (var c in candidates) {
					DateTime seen = _data.Systems.TryGetValue(Key(c.Galaxy, c.System), out var s) ? s.SeenUtc : DateTime.MinValue;
					if (nowUtc - seen <= maxAge)
						continue;
					if (seen < bestSeen) {
						bestSeen = seen;
						best = c;
					}
				}
				return best;
			}
		}

		public List<GalaxyEvent> RecentEvents(int max) {
			lock (_lock)
				return _data.Events.OrderByDescending(e => e.TimeUtc).Take(max).ToList();
		}
	}
}
