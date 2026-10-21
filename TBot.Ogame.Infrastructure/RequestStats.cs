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
		private static readonly ConcurrentDictionary<string, long> _counts = new();

		public static void Count() {
			string name = Current.Value ?? "(sonst)";
			_counts.AddOrUpdate(name, 1, (_, v) => v + 1);
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
			return base.SendAsync(request, cancellationToken);
		}
	}
}
