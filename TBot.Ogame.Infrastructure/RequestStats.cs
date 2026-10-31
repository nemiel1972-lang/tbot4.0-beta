using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure {
	public static class RequestStats {
		public static readonly AsyncLocal<string> Current = new();

		public static Action<string, string> DevLog;
		private static readonly ConcurrentDictionary<string, long> _counts = new();

		public static void Count() {
			string name = Current.Value ?? "(sonst)";
			_counts.AddOrUpdate(name, 1, (_, v) => v + 1);
		}

		private static readonly ConcurrentDictionary<string, long> _celestialSwitches = new();
		private static readonly object _celestialLock = new();
		private static string _lastCelestial;
		private static readonly System.Text.RegularExpressions.Regex _celestialRx = new(@"/bot/(?:planets|moons|celestials)/(\d+)", System.Text.RegularExpressions.RegexOptions.Compiled);

		public static void CountCelestial(Uri uri) {
			var m = uri == null ? null : _celestialRx.Match(uri.AbsolutePath);
			if (m == null || !m.Success) return;
			string id = m.Groups[1].Value;
			bool changed; string prev;
			lock (_celestialLock) { prev = _lastCelestial; changed = prev != null && prev != id; _lastCelestial = id; }
			if (changed) {
				_celestialSwitches.AddOrUpdate(Current.Value ?? "(sonst)", 1, (_, v) => v + 1);
				try { DevLog?.Invoke("CeleSwitch", $"{prev} -> {id} by {Current.Value ?? "(sonst)"} ({uri.AbsolutePath})"); } catch { }
			}
		}

		public static List<KeyValuePair<string, long>> SnapshotCelestialSwitches() {
			var list = new List<KeyValuePair<string, long>>();
			foreach (var key in _celestialSwitches.Keys.ToList())
				if (_celestialSwitches.TryRemove(key, out long v) && v > 0)
					list.Add(new KeyValuePair<string, long>(key, v));
			return list.OrderByDescending(kv => kv.Value).ToList();
		}

		private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, long>> _detail = new();
		private static readonly System.Text.RegularExpressions.Regex _zahlRx = new(@"/\d+", System.Text.RegularExpressions.RegexOptions.Compiled);

		public static void CountDetail(Uri uri) {
			if (uri == null) return;
			string endpoint = _zahlRx.Replace(uri.AbsolutePath, "/#");
			var d = _detail.GetOrAdd(Current.Value ?? "(sonst)", _ => new ConcurrentDictionary<string, long>());
			d.AddOrUpdate(endpoint, 1, (_, v) => v + 1);
		}

		public static List<KeyValuePair<string, List<KeyValuePair<string, long>>>> SnapshotDetail() {
			var result = new List<KeyValuePair<string, List<KeyValuePair<string, long>>>>();
			foreach (var worker in _detail.Keys.ToList()) {
				if (!_detail.TryRemove(worker, out var d)) continue;
				var list = d.OrderByDescending(kv => kv.Value).ToList();
				if (list.Count > 0) result.Add(new(worker, list));
			}
			return result.OrderByDescending(w => w.Value.Sum(kv => kv.Value)).ToList();
		}

		public static List<KeyValuePair<string, long>> Snapshot() {
			var list = new List<KeyValuePair<string, long>>();
			foreach (var key in _counts.Keys.ToList()) {
				if (_counts.TryRemove(key, out long v) && v > 0)
					list.Add(new KeyValuePair<string, long>(key, v));
			}
			return list.OrderByDescending(kv => kv.Value).ToList();
		}
	}

	internal sealed class CountingHandler : DelegatingHandler {
		public CountingHandler(HttpMessageHandler inner) : base(inner) { }
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
			RequestStats.Count();
			RequestStats.CountCelestial(request.RequestUri);
			RequestStats.CountDetail(request.RequestUri);
			return base.SendAsync(request, cancellationToken);
		}
	}
}
