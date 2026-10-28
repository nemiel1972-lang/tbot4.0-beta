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
		public int Rank { get; set; }
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

	public class GalaxyPlayer {
		public int Id { get; set; }
		public string Name { get; set; }
		public int Rank { get; set; }
		public int AllianceId { get; set; }
		public int HomeGalaxy { get; set; }
		public int HomeSystem { get; set; }
		public int HomePosition { get; set; }
		public bool Inactive { get; set; }
		public bool LongInactive { get; set; }
		public bool Banned { get; set; }
		public bool Vacation { get; set; }
		public bool Newbie { get; set; }
		public bool StrongPlayer { get; set; }
		public DateTime SeenUtc { get; set; }
		public bool StatusFromApi { get; set; }
	}

	public class GalaxyScanInfo {
		public int Galaxies { get; set; }
		public int Systems { get; set; }
		public int IntervalHours { get; set; }
		public double MeanPauseSec { get; set; }
		public bool ScanAll { get; set; }
		public int LastGalaxy { get; set; }
		public int LastSystem { get; set; }
		public DateTime LastUtc { get; set; }
		public string LastKind { get; set; }
	}

	public class GalaxyCacheData {
		public GalaxyScanInfo ScanInfo { get; set; }
		public Dictionary<string, GalaxyCacheSystem> Systems { get; set; } = new();
		public Dictionary<int, GalaxyPlayer> Players { get; set; } = new();
		public DateTime HighscoreUtc { get; set; } = DateTime.MinValue;
		public bool HighscoreHasStatus { get; set; }
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
					d.Players ??= new();
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
				Rank = p.Player?.Rank ?? 0,
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
					Planets = s.Entries.Select(e => {
						_data.Players.TryGetValue(e.PlayerId, out var hp);
						bool neuer = _data.HighscoreHasStatus && hp != null && !hp.StatusFromApi && hp.SeenUtc > s.SeenUtc;
						return new Planet {
							Name = e.Name,
							Coordinate = new Coordinate(galaxy, system, e.Position, Celestials.Planet),
							Inactive = neuer ? hp.Inactive : e.Inactive,
							Vacation = neuer ? hp.Vacation : e.Vacation,
							Newbie = neuer ? hp.Newbie : e.Newbie,
							Banned = neuer ? hp.Banned : e.Banned,
							Administrator = e.Administrator,
							StrongPlayer = neuer ? hp.StrongPlayer : e.StrongPlayer,
							Player = new Player { ID = e.PlayerId, Name = e.PlayerName, Rank = hp != null && hp.Rank > 0 ? hp.Rank : e.Rank },
							Moon = e.HasMoon ? new Moon() : null
						};
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

		public (int Galaxy, int System)? NextToScan(IEnumerable<(int Galaxy, int System)> candidates, TimeSpan maxAge, DateTime nowUtc, TimeSpan? emptyMaxAge = null) {
			lock (_lock) {
				(int Galaxy, int System)? best = null;
				DateTime bestSeen = DateTime.MaxValue;
				foreach (var c in candidates) {
					bool known = _data.Systems.TryGetValue(Key(c.Galaxy, c.System), out var s);
					DateTime seen = known ? s.SeenUtc : DateTime.MinValue;
					TimeSpan limit = known && s.Entries.Count == 0 && emptyMaxAge.HasValue && emptyMaxAge.Value > maxAge ? emptyMaxAge.Value : maxAge;
					if (nowUtc - seen <= limit)
						continue;
					if (seen < bestSeen) {
						bestSeen = seen;
						best = c;
					}
				}
				return best;
			}
		}


		public void SetScanInfo(GalaxyScanInfo info) {
			lock (_lock) {
				_data.ScanInfo = info;
				_dirty = true;
			}
		}

		public int PlayerCount {
			get { lock (_lock) return _data.Players.Count; }
		}

		public DateTime HighscoreUtc {
			get { lock (_lock) return _data.HighscoreUtc; }
		}

		public void RecordHighscore(IEnumerable<HighscorePlayer> players, DateTime nowUtc, bool statusFromApi = false) {
			if (players == null)
				return;
			lock (_lock) {
				foreach (var p in players) {
					if (p == null || p.ID <= 0)
						continue;
					var st = p.Status ?? new List<string>();
					_data.Players.TryGetValue(p.ID, out var alt);
					bool hatHome = p.Homeworld != null && p.Homeworld.Galaxy > 0;
					_data.Players[p.ID] = new GalaxyPlayer {
						Id = p.ID, Name = p.Name, Rank = p.Position, AllianceId = p.AllianceID, StatusFromApi = statusFromApi,
						HomeGalaxy = hatHome ? p.Homeworld.Galaxy : alt?.HomeGalaxy ?? 0, HomeSystem = hatHome ? p.Homeworld.System : alt?.HomeSystem ?? 0, HomePosition = hatHome ? p.Homeworld.Position : alt?.HomePosition ?? 0,
						Inactive = st.Contains("inactive") || st.Contains("longinactive"),
						LongInactive = st.Contains("longinactive"),
						Banned = st.Contains("banned"), Vacation = st.Contains("vacation"),
						Newbie = st.Contains("noob"), StrongPlayer = st.Contains("strong"),
						SeenUtc = nowUtc
					};
				}
				_dirty = true;
			}
		}

		public void FinishHighscore(DateTime startedUtc, DateTime nowUtc) {
			lock (_lock) {
				foreach (var id in _data.Players.Where(kv => kv.Value.SeenUtc < startedUtc).Select(kv => kv.Key).ToList())
					_data.Players.Remove(id);
				_data.HighscoreUtc = nowUtc;
				_data.HighscoreHasStatus = _data.Players.Values.Any(p => p.Inactive || p.Banned || p.Vacation);
				_dirty = true;
			}
		}

		public (int Galaxy, int System)? NextInactiveSystem(TimeSpan maxAge, DateTime nowUtc) {
			lock (_lock) {
				var ids = new HashSet<int>(_data.Players.Values.Where(p => p.Inactive && !p.Vacation && !p.Banned).Select(p => p.Id));
				foreach (var sys in _data.Systems.Values)
					foreach (var e in sys.Entries)
						if (e.Inactive && !e.Vacation && !e.Banned && e.PlayerId > 0)
							ids.Add(e.PlayerId);
				if (ids.Count == 0)
					return null;
				var systems = new HashSet<(int, int)>();
				foreach (var p in _data.Players.Values)
					if (ids.Contains(p.Id) && p.HomeGalaxy > 0 && p.HomeSystem > 0)
						systems.Add((p.HomeGalaxy, p.HomeSystem));
				foreach (var kv in _data.Systems) {
					if (kv.Value.Entries.Any(e => ids.Contains(e.PlayerId))) {
						var parts = kv.Key.Split(':');
						if (parts.Length == 2 && int.TryParse(parts[0], out int gg) && int.TryParse(parts[1], out int ss))
							systems.Add((gg, ss));
					}
				}
				(int Galaxy, int System)? best = null;
				DateTime bestSeen = DateTime.MaxValue;
				foreach (var c in systems) {
					DateTime seen = _data.Systems.TryGetValue(Key(c.Item1, c.Item2), out var s) ? s.SeenUtc : DateTime.MinValue;
					if (nowUtc - seen <= maxAge)
						continue;
					if (seen < bestSeen) {
						bestSeen = seen;
						best = (c.Item1, c.Item2);
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
