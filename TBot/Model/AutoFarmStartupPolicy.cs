using System;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public static class AutoFarmStartupPolicy {
		public static bool IsTargetStale(FarmTarget target, DateTime nowServer, int maxReportAgeMinutes) {
			if (target == null || target.HeldForManualReview)
				return false;
			if (target.State != FarmState.ProbesSent && target.State != FarmState.AttackPending)
				return false;
			if (target.Report == null)
				return target.State == FarmState.AttackPending;
			if (maxReportAgeMinutes <= 0)
				return false;
			return (nowServer - target.Report.Date).TotalMinutes > maxReportAgeMinutes;
		}

		public static bool IsCursorStale(DateTime? updatedAtUtc, DateTime nowUtc, int maxReportAgeMinutes) {
			if (updatedAtUtc == null)
				return false;
			var limit = TimeSpan.FromMinutes(Math.Max(60, maxReportAgeMinutes));
			return nowUtc - updatedAtUtc.Value > limit;
		}
	}
}
