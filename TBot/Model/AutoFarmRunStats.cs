using System;
using System.Collections.Generic;
using System.Linq;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public class AutoFarmRunStats {
		public int SystemsExcluded;
		public int CacheEmptySkip;
		public int CacheSnapshotUsed;
		public int LiveScans;
		public int ScanFailures;
		public int NoData;
		public int SkipRank;
		public int SkipExcludedPlanet;
		public int SkipBlacklist;
		public int SkipNotSuitable;
		public int SkipBusyState;
		public int ProbesSent;
		public string StopReason;

		public void SetStopReason(string reason) {
			StopReason ??= reason;
		}

		public static string DescribeTargets(IEnumerable<FarmTarget> targets) {
			var groups = (targets ?? Enumerable.Empty<FarmTarget>())
				.Where(t => t != null)
				.GroupBy(t => t.State)
				.OrderBy(g => g.Key.ToString())
				.Select(g => $"{g.Key}={g.Count()}");
			string text = string.Join(", ", groups);
			return text.Length == 0 ? "none" : text;
		}

		public string DescribeRun() {
			return $"systems: excluded={SystemsExcluded}, cache-empty-skip={CacheEmptySkip}, cache-snapshot={CacheSnapshotUsed}, live={LiveScans}, no-data={NoData}, failed={ScanFailures}; " +
				$"planets skipped: rank={SkipRank}, excluded={SkipExcludedPlanet}, blacklist={SkipBlacklist}, not-suitable={SkipNotSuitable}, busy-state={SkipBusyState}; " +
				$"probes sent={ProbesSent}; end: {StopReason ?? "scan range finished"}";
		}
	}
}
