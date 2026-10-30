using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Model {
	public sealed class AutoFarmSystemSnapshot {
		public int Galaxy { get; init; }
		public int System { get; init; }
		public DateTime ObservedAtUtc { get; init; }
		public bool IsEmpty { get; init; }
		public List<Planet> Planets { get; init; } = new();
	}

	public sealed class AutoFarmAttackRecord {
		public int ReportId { get; init; }
		public string TargetName { get; init; }
		public Coordinate Coordinate { get; init; }
		public Coordinate OriginCoordinate { get; init; }
		public string Mission { get; init; } = "Attack";
		public DateTime DispatchedAtUtc { get; init; }
		public Resources Loot { get; init; } = new();
	}

	public sealed record AutoFarmScanCursor(int RangeIndex, int Galaxy, int System);

	public sealed class AutoFarmFleetLossRecord {
		public int FleetId { get; init; }
		public Coordinate Target { get; init; }
		public Coordinate Origin { get; init; }
		public DateTime DispatchedAtUtc { get; init; }
		public DateTime ExpectedArrivalUtc { get; set; }
		public DateTime ExpectedReturnUtc { get; set; }
		public Dictionary<string, long> Sent { get; init; } = new();
		public Dictionary<string, long> Returned { get; init; }
		public string Status { get; init; } = "pending";
		public int MissCount { get; init; }
		public Resources LossValue { get; init; } = new();
		public Resources PlannedLoot { get; init; } = new();
		public bool GotKnown { get; init; }
		public Resources ActualLoot { get; init; } = new();
		public long FuelDeuterium { get; init; }
	}

	public sealed class AutoFarmStateStore : IDisposable {
		private readonly string _connectionString;
		private bool _enabled = true;
		private bool _lossTrackingEnabled = true;

		static AutoFarmStateStore() {
			SQLitePCL.Batteries_V2.Init();
		}

		public AutoFarmStateStore(string fileName, string dataFolder = null) {
			var folder = dataFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
			Directory.CreateDirectory(folder);

			var safeFileName = Path.GetFileName(fileName);
			if (string.IsNullOrWhiteSpace(safeFileName))
				safeFileName = "autofarm.db";
			if (!safeFileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
				safeFileName += ".db";

			var path = Path.Combine(folder, safeFileName);
			_connectionString = new SqliteConnectionStringBuilder {
				DataSource = path,
				Pooling = false
			}.ToString();
			Initialize();
		}

		public AutoFarmSystemSnapshot GetFreshSystemSnapshot(int galaxy, int system, DateTime nowUtc, TimeSpan ttl) {
			if (!_enabled || ttl <= TimeSpan.Zero)
				return null;

			try {
				var snapshot = GetSystemSnapshot(galaxy, system);
				if (snapshot == null || nowUtc - snapshot.ObservedAtUtc >= ttl)
					return null;

				return snapshot;
			} catch {
				return null;
			}
		}

		public AutoFarmSystemSnapshot GetSystemSnapshot(int galaxy, int system) {
			if (!_enabled)
				return null;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
SELECT observed_at_utc, is_empty, planets_json
FROM systems
WHERE galaxy = $galaxy AND system = $system;";
				command.Parameters.AddWithValue("$galaxy", galaxy);
				command.Parameters.AddWithValue("$system", system);

				using var reader = command.ExecuteReader();
				if (!reader.Read())
					return null;

				return new AutoFarmSystemSnapshot {
					Galaxy = galaxy,
					System = system,
					ObservedAtUtc = ParseUtc(reader.GetString(0)),
					IsEmpty = reader.GetInt64(1) != 0,
					Planets = JsonConvert.DeserializeObject<List<Planet>>(reader.GetString(2)) ?? new List<Planet>()
				};
			} catch {
				return null;
			}
		}

		public void SaveSystemSnapshot(int galaxy, int system, DateTime observedAtUtc, bool isEmpty, IEnumerable<Planet> planets) {
			if (!_enabled)
				return;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
INSERT INTO systems (galaxy, system, observed_at_utc, is_empty, planets_json)
VALUES ($galaxy, $system, $observed_at_utc, $is_empty, $planets_json)
ON CONFLICT(galaxy, system) DO UPDATE SET
  observed_at_utc = excluded.observed_at_utc,
  is_empty = excluded.is_empty,
  planets_json = excluded.planets_json;";
				command.Parameters.AddWithValue("$galaxy", galaxy);
				command.Parameters.AddWithValue("$system", system);
				command.Parameters.AddWithValue("$observed_at_utc", FormatUtc(observedAtUtc));
				command.Parameters.AddWithValue("$is_empty", isEmpty ? 1 : 0);
				command.Parameters.AddWithValue("$planets_json",
					JsonConvert.SerializeObject(planets ?? Array.Empty<Planet>()));
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public void UpsertTarget(FarmTarget target, DateTime updatedAtUtc) {
			if (!_enabled || target?.Celestial?.Coordinate == null)
				return;

			try {
				var coordinate = target.Celestial.Coordinate;
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
INSERT INTO targets (
  galaxy, system, position, celestial_type, name, state, report_json, consumed_report_id, updated_at_utc, held_for_manual_review)
VALUES ($galaxy, $system, $position, $celestial_type, $name, $state, $report_json, $consumed_report_id, $updated_at_utc, $held_for_manual_review)
ON CONFLICT(galaxy, system, position, celestial_type) DO UPDATE SET
  name = excluded.name,
  state = excluded.state,
  report_json = excluded.report_json,
  consumed_report_id = excluded.consumed_report_id,
  updated_at_utc = excluded.updated_at_utc,
  held_for_manual_review = excluded.held_for_manual_review;";
				command.Parameters.AddWithValue("$galaxy", coordinate.Galaxy);
				command.Parameters.AddWithValue("$system", coordinate.System);
				command.Parameters.AddWithValue("$position", coordinate.Position);
				command.Parameters.AddWithValue("$celestial_type", (int) coordinate.Type);
				command.Parameters.AddWithValue("$name", target.Celestial.Name ?? string.Empty);
				command.Parameters.AddWithValue("$state", (int) target.State);
				command.Parameters.AddWithValue("$report_json", target.Report == null
					? DBNull.Value
					: JsonConvert.SerializeObject(target.Report));
				command.Parameters.AddWithValue("$consumed_report_id", target.ConsumedReportId.HasValue
					? target.ConsumedReportId.Value
					: DBNull.Value);
				command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(updatedAtUtc));
				command.Parameters.AddWithValue("$held_for_manual_review", target.HeldForManualReview ? 1 : 0);
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public List<FarmTarget> LoadTargets() {
			var targets = new List<FarmTarget>();
			if (!_enabled)
				return targets;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
SELECT galaxy, system, position, celestial_type, name, state, report_json, consumed_report_id, held_for_manual_review
FROM targets;";

				using var reader = command.ExecuteReader();
				while (reader.Read()) {
					var coordinate = new Coordinate(
						reader.GetInt32(0),
						reader.GetInt32(1),
						reader.GetInt32(2),
						(Celestials) reader.GetInt32(3));
					var celestial = new Planet {
						Name = reader.GetString(4),
						Coordinate = coordinate
					};
					var report = reader.IsDBNull(6)
						? null
						: JsonConvert.DeserializeObject<EspionageReport>(reader.GetString(6));
					targets.Add(new FarmTarget(celestial, (FarmState) reader.GetInt32(5), report) {
						ConsumedReportId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
						HeldForManualReview = !reader.IsDBNull(8) && reader.GetInt32(8) != 0
					});
				}
			} catch {
				return new List<FarmTarget>();
			}

			return targets;
		}

		public void DeleteTarget(int galaxy, int system, int position) {
			if (!_enabled)
				return;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = "DELETE FROM targets WHERE galaxy = $g AND system = $s AND position = $p;";
				command.Parameters.AddWithValue("$g", galaxy);
				command.Parameters.AddWithValue("$s", system);
				command.Parameters.AddWithValue("$p", position);
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public void ClearSystems() {
			ExecuteMaintenance("DELETE FROM systems; DELETE FROM scan_cursor;");
		}

		public void ResetAll() {
			ExecuteMaintenance("DELETE FROM systems; DELETE FROM targets; DELETE FROM consumed_reports; DELETE FROM attacks; DELETE FROM scan_cursor; DELETE FROM main_scan_state;");
			ExecuteMaintenance("DELETE FROM fleet_losses;");
		}

		private void ExecuteMaintenance(string sql) {
			if (!_enabled)
				return;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = sql;
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public bool HasConsumedReport(int reportId) {
			if (!_enabled || reportId <= 0)
				return false;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT EXISTS(SELECT 1 FROM consumed_reports WHERE report_id = $report_id);";
				command.Parameters.AddWithValue("$report_id", reportId);
				return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 1;
			} catch {
				return false;
			}
		}

		public bool RecordAttack(FarmTarget target, Resources loot, DateTime dispatchedAtUtc, Coordinate origin = null, string mission = "Attack") {
			if (!_enabled || target?.Celestial?.Coordinate == null || target.Report?.ID <= 0)
				return false;

			try {
				var coordinate = target.Celestial.Coordinate;
				using var connection = OpenConnection();
				using var transaction = connection.BeginTransaction();

				using (var consumed = connection.CreateCommand()) {
					consumed.Transaction = transaction;
					consumed.CommandText = @"
INSERT OR IGNORE INTO consumed_reports (
  report_id, galaxy, system, position, celestial_type, consumed_at_utc)
VALUES ($report_id, $galaxy, $system, $position, $celestial_type, $consumed_at_utc);";
					consumed.Parameters.AddWithValue("$report_id", target.Report.ID);
					consumed.Parameters.AddWithValue("$galaxy", coordinate.Galaxy);
					consumed.Parameters.AddWithValue("$system", coordinate.System);
					consumed.Parameters.AddWithValue("$position", coordinate.Position);
					consumed.Parameters.AddWithValue("$celestial_type", (int) coordinate.Type);
					consumed.Parameters.AddWithValue("$consumed_at_utc", FormatUtc(dispatchedAtUtc));
					consumed.ExecuteNonQuery();
				}

				using (var attack = connection.CreateCommand()) {
					attack.Transaction = transaction;
					attack.CommandText = @"
INSERT OR IGNORE INTO attacks (
  report_id, galaxy, system, position, celestial_type, target_name,
  dispatched_at_utc, metal, crystal, deuterium,
  origin_galaxy, origin_system, origin_position, origin_celestial_type, mission)
VALUES ($report_id, $galaxy, $system, $position, $celestial_type, $target_name,
  $dispatched_at_utc, $metal, $crystal, $deuterium,
  $origin_galaxy, $origin_system, $origin_position, $origin_celestial_type, $mission);";
					attack.Parameters.AddWithValue("$report_id", target.Report.ID);
					attack.Parameters.AddWithValue("$galaxy", coordinate.Galaxy);
					attack.Parameters.AddWithValue("$system", coordinate.System);
					attack.Parameters.AddWithValue("$position", coordinate.Position);
					attack.Parameters.AddWithValue("$celestial_type", (int) coordinate.Type);
					attack.Parameters.AddWithValue("$target_name", target.Celestial.Name ?? string.Empty);
					attack.Parameters.AddWithValue("$dispatched_at_utc", FormatUtc(dispatchedAtUtc));
					attack.Parameters.AddWithValue("$metal", loot?.Metal ?? 0);
					attack.Parameters.AddWithValue("$crystal", loot?.Crystal ?? 0);
					attack.Parameters.AddWithValue("$deuterium", loot?.Deuterium ?? 0);
					attack.Parameters.AddWithValue("$origin_galaxy", origin?.Galaxy ?? (object) DBNull.Value);
					attack.Parameters.AddWithValue("$origin_system", origin?.System ?? (object) DBNull.Value);
					attack.Parameters.AddWithValue("$origin_position", origin?.Position ?? (object) DBNull.Value);
					attack.Parameters.AddWithValue("$origin_celestial_type", origin == null ? (object) DBNull.Value : (int) origin.Type);
					attack.Parameters.AddWithValue("$mission", string.IsNullOrWhiteSpace(mission) ? "Attack" : mission);
					attack.ExecuteNonQuery();
				}

				transaction.Commit();
				return true;
			} catch {
				return false;
			}
		}

		public List<AutoFarmAttackRecord> LoadAttackHistory(int limit = 100) {
			var attacks = new List<AutoFarmAttackRecord>();
			if (!_enabled)
				return attacks;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
SELECT report_id, galaxy, system, position, celestial_type, target_name,
  dispatched_at_utc, metal, crystal, deuterium,
  origin_galaxy, origin_system, origin_position, origin_celestial_type, mission
FROM attacks
ORDER BY dispatched_at_utc DESC
LIMIT $limit;";
				command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 1000));

				using var reader = command.ExecuteReader();
				while (reader.Read()) {
					Coordinate origin = null;
					if (!reader.IsDBNull(10) && !reader.IsDBNull(11) && !reader.IsDBNull(12) && !reader.IsDBNull(13))
						origin = new Coordinate(reader.GetInt32(10), reader.GetInt32(11), reader.GetInt32(12), (Celestials) reader.GetInt32(13));

					attacks.Add(new AutoFarmAttackRecord {
						ReportId = reader.GetInt32(0),
						Coordinate = new Coordinate(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), (Celestials) reader.GetInt32(4)),
						TargetName = reader.GetString(5),
						DispatchedAtUtc = ParseUtc(reader.GetString(6)),
						Loot = new Resources(reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9)),
						OriginCoordinate = origin,
						Mission = reader.IsDBNull(14) ? "Attack" : reader.GetString(14)
					});
				}
			} catch {
				return new List<AutoFarmAttackRecord>();
			}

			return attacks;
		}

		public bool LoadMainScanCompleted() {
			if (!_enabled)
				return false;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
SELECT completed
FROM main_scan_state
WHERE id = 1;";

				using var reader = command.ExecuteReader();
				if (!reader.Read())
					return false;

				return reader.GetInt64(0) != 0;
			} catch {
				return false;
			}
		}

		public void SaveMainScanCompleted(bool completed) {
			if (!_enabled)
				return;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
INSERT INTO main_scan_state (id, completed)
VALUES (1, $completed)
ON CONFLICT(id) DO UPDATE SET
  completed = excluded.completed;";
				command.Parameters.AddWithValue("$completed", completed ? 1 : 0);
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public AutoFarmScanCursor LoadScanCursor() {
			if (!_enabled)
				return null;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
SELECT range_index, galaxy, system
FROM scan_cursor
WHERE id = 1;";

				using var reader = command.ExecuteReader();
				if (!reader.Read())
					return null;

				return new AutoFarmScanCursor(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
			} catch {
				return null;
			}
		}

		public DateTime? LoadScanCursorUpdatedAtUtc() {
			if (!_enabled)
				return null;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = "SELECT updated_at_utc FROM scan_cursor WHERE id = 1;";
				var value = command.ExecuteScalar() as string;
				return string.IsNullOrEmpty(value) ? null : ParseUtc(value);
			} catch {
				return null;
			}
		}

		public void SaveScanCursor(int rangeIndex, int galaxy, int system, DateTime updatedAtUtc) {
			if (!_enabled)
				return;

			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
INSERT INTO scan_cursor (id, range_index, galaxy, system, updated_at_utc)
VALUES (1, $range_index, $galaxy, $system, $updated_at_utc)
ON CONFLICT(id) DO UPDATE SET
  range_index = excluded.range_index,
  galaxy = excluded.galaxy,
  system = excluded.system,
  updated_at_utc = excluded.updated_at_utc;";
				command.Parameters.AddWithValue("$range_index", rangeIndex);
				command.Parameters.AddWithValue("$galaxy", galaxy);
				command.Parameters.AddWithValue("$system", system);
				command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(updatedAtUtc));
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public void ReplaceTargets(IEnumerable<FarmTarget> targets, DateTime updatedAtUtc) {
			if (!_enabled)
				return;

			try {
				using var connection = OpenConnection();
				using var transaction = connection.BeginTransaction();
				using (var delete = connection.CreateCommand()) {
					delete.Transaction = transaction;
					delete.CommandText = "DELETE FROM targets;";
					delete.ExecuteNonQuery();
				}

				var uniqueTargets = (targets ?? Array.Empty<FarmTarget>())
					.Where(target => target?.Celestial?.Coordinate != null)
					.GroupBy(target => target.Celestial.Coordinate.ToString())
					.Select(group => group.Last());

				foreach (var target in uniqueTargets) {

					using var insert = connection.CreateCommand();
					insert.Transaction = transaction;
					var coordinate = target.Celestial.Coordinate;
					insert.CommandText = @"
INSERT INTO targets (
  galaxy, system, position, celestial_type, name, state, report_json, consumed_report_id, updated_at_utc, held_for_manual_review)
VALUES ($galaxy, $system, $position, $celestial_type, $name, $state, $report_json, $consumed_report_id, $updated_at_utc, $held_for_manual_review);";
					insert.Parameters.AddWithValue("$galaxy", coordinate.Galaxy);
					insert.Parameters.AddWithValue("$system", coordinate.System);
					insert.Parameters.AddWithValue("$position", coordinate.Position);
					insert.Parameters.AddWithValue("$celestial_type", (int) coordinate.Type);
					insert.Parameters.AddWithValue("$name", target.Celestial.Name ?? string.Empty);
					insert.Parameters.AddWithValue("$state", (int) target.State);
					insert.Parameters.AddWithValue("$report_json", target.Report == null
						? DBNull.Value
						: JsonConvert.SerializeObject(target.Report));
					insert.Parameters.AddWithValue("$consumed_report_id", target.ConsumedReportId.HasValue
						? target.ConsumedReportId.Value
						: DBNull.Value);
					insert.Parameters.AddWithValue("$updated_at_utc", FormatUtc(updatedAtUtc));
					insert.Parameters.AddWithValue("$held_for_manual_review", target.HeldForManualReview ? 1 : 0);
					insert.ExecuteNonQuery();
				}

				transaction.Commit();
			} catch {
			}
		}

		public bool RecordFleetDispatch(int fleetId, Coordinate target, Coordinate origin, DateTime dispatchedAtUtc, DateTime expectedArrivalUtc, DateTime expectedReturnUtc, Dictionary<string, long> sent, Resources plannedLoot, long fuelDeuterium = 0) {
			if (!_enabled || !_lossTrackingEnabled || fleetId <= 0 || target == null || origin == null)
				return false;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
		INSERT OR REPLACE INTO fleet_losses (
		fleet_id, galaxy, system, position, celestial_type,
		origin_galaxy, origin_system, origin_position, origin_celestial_type,
		dispatched_at_utc, expected_arrival_utc, expected_return_utc, sent_json, status, miss_count,
		loot_metal, loot_crystal, loot_deuterium, fuel_deuterium)
		VALUES ($fleet_id, $galaxy, $system, $position, $celestial_type,
		$og, $os, $op, $ot,
		$dispatched, $arrival, $return, $sent, 'pending', 0,
		$lm, $lc, $ld, $fuel);";
				command.Parameters.AddWithValue("$fleet_id", fleetId);
				command.Parameters.AddWithValue("$galaxy", target.Galaxy);
				command.Parameters.AddWithValue("$system", target.System);
				command.Parameters.AddWithValue("$position", target.Position);
				command.Parameters.AddWithValue("$celestial_type", (int) target.Type);
				command.Parameters.AddWithValue("$og", origin.Galaxy);
				command.Parameters.AddWithValue("$os", origin.System);
				command.Parameters.AddWithValue("$op", origin.Position);
				command.Parameters.AddWithValue("$ot", (int) origin.Type);
				command.Parameters.AddWithValue("$dispatched", FormatUtc(dispatchedAtUtc));
				command.Parameters.AddWithValue("$arrival", FormatUtc(expectedArrivalUtc));
				command.Parameters.AddWithValue("$return", FormatUtc(expectedReturnUtc));
				command.Parameters.AddWithValue("$sent", JsonConvert.SerializeObject(sent ?? new Dictionary<string, long>()));
				command.Parameters.AddWithValue("$lm", plannedLoot?.Metal ?? 0);
				command.Parameters.AddWithValue("$lc", plannedLoot?.Crystal ?? 0);
				command.Parameters.AddWithValue("$ld", plannedLoot?.Deuterium ?? 0);
				command.Parameters.AddWithValue("$fuel", fuelDeuterium);
				command.ExecuteNonQuery();
				return true;
			} catch {
				return false;
			}
		}

		public List<AutoFarmFleetLossRecord> LoadFleetLosses(string status = null, DateTime? dispatchedSinceUtc = null) {
			var list = new List<AutoFarmFleetLossRecord>();
			if (!_enabled || !_lossTrackingEnabled)
				return list;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
		SELECT fleet_id, galaxy, system, position, celestial_type,
		origin_galaxy, origin_system, origin_position, origin_celestial_type,
		dispatched_at_utc, expected_arrival_utc, expected_return_utc, sent_json, returned_json, status, miss_count,
		loss_metal, loss_crystal, loss_deuterium, loot_metal, loot_crystal, loot_deuterium,
		got_known, got_metal, got_crystal, got_deuterium, fuel_deuterium
		FROM fleet_losses
		WHERE ($status IS NULL OR status = $status) AND ($since IS NULL OR dispatched_at_utc >= $since);";
				command.Parameters.AddWithValue("$status", (object) status ?? DBNull.Value);
				command.Parameters.AddWithValue("$since", dispatchedSinceUtc.HasValue ? FormatUtc(dispatchedSinceUtc.Value) : DBNull.Value);
				using var reader = command.ExecuteReader();
				while (reader.Read()) {
					list.Add(new AutoFarmFleetLossRecord {
						FleetId = reader.GetInt32(0),
						Target = new Coordinate(reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), (Celestials) reader.GetInt32(4)),
						Origin = new Coordinate(reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), (Celestials) reader.GetInt32(8)),
						DispatchedAtUtc = ParseUtc(reader.GetString(9)),
						ExpectedArrivalUtc = ParseUtc(reader.GetString(10)),
						ExpectedReturnUtc = ParseUtc(reader.GetString(11)),
						Sent = JsonConvert.DeserializeObject<Dictionary<string, long>>(reader.GetString(12)) ?? new Dictionary<string, long>(),
						Returned = reader.IsDBNull(13) ? null : JsonConvert.DeserializeObject<Dictionary<string, long>>(reader.GetString(13)),
						Status = reader.GetString(14),
						MissCount = reader.GetInt32(15),
						LossValue = new Resources(reader.GetInt64(16), reader.GetInt64(17), reader.GetInt64(18)),
						PlannedLoot = new Resources(reader.GetInt64(19), reader.GetInt64(20), reader.GetInt64(21)),
						GotKnown = reader.GetInt32(22) != 0,
						ActualLoot = new Resources(reader.GetInt64(23), reader.GetInt64(24), reader.GetInt64(25)),
						FuelDeuterium = reader.GetInt64(26)
					});
				}
			} catch {
				return new List<AutoFarmFleetLossRecord>();
			}
			return list;
		}

		public void UpdateFleetLossObservation(int fleetId, int missCount, DateTime? expectedArrivalUtc, DateTime? expectedReturnUtc) {
			if (!_enabled || !_lossTrackingEnabled)
				return;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
		UPDATE fleet_losses SET
		miss_count = $miss,
		expected_arrival_utc = COALESCE($arrival, expected_arrival_utc),
		expected_return_utc = COALESCE($return, expected_return_utc)
		WHERE fleet_id = $fleet_id AND status = 'pending';";
				command.Parameters.AddWithValue("$fleet_id", fleetId);
				command.Parameters.AddWithValue("$miss", missCount);
				command.Parameters.AddWithValue("$arrival", expectedArrivalUtc.HasValue ? FormatUtc(expectedArrivalUtc.Value) : DBNull.Value);
				command.Parameters.AddWithValue("$return", expectedReturnUtc.HasValue ? FormatUtc(expectedReturnUtc.Value) : DBNull.Value);
				command.ExecuteNonQuery();
			} catch {
			}
		}

		public void ResolveFleetLoss(int fleetId, string status, Dictionary<string, long> returned, Resources lossValue, DateTime resolvedAtUtc, Resources actualLoot = null) {
			if (!_enabled || !_lossTrackingEnabled)
				return;
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
		UPDATE fleet_losses SET
		status = $status, returned_json = $returned, resolved_at_utc = $resolved,
		loss_metal = $lm, loss_crystal = $lc, loss_deuterium = $ld,
		got_known = $gk, got_metal = $gm, got_crystal = $gc, got_deuterium = $gd
		WHERE fleet_id = $fleet_id AND status = 'pending';";
				command.Parameters.AddWithValue("$fleet_id", fleetId);
				command.Parameters.AddWithValue("$status", status);
				command.Parameters.AddWithValue("$returned", returned == null ? DBNull.Value : JsonConvert.SerializeObject(returned));
				command.Parameters.AddWithValue("$resolved", FormatUtc(resolvedAtUtc));
				command.Parameters.AddWithValue("$lm", lossValue?.Metal ?? 0);
				command.Parameters.AddWithValue("$lc", lossValue?.Crystal ?? 0);
				command.Parameters.AddWithValue("$ld", lossValue?.Deuterium ?? 0);
				command.Parameters.AddWithValue("$gk", actualLoot == null ? 0 : 1);
				command.Parameters.AddWithValue("$gm", actualLoot?.Metal ?? 0);
				command.Parameters.AddWithValue("$gc", actualLoot?.Crystal ?? 0);
				command.Parameters.AddWithValue("$gd", actualLoot?.Deuterium ?? 0);
				command.ExecuteNonQuery();
			} catch {
			}
		}		private void Initialize() {
			try {
				using var connection = OpenConnection();
				using var command = connection.CreateCommand();
				command.CommandText = @"
CREATE TABLE IF NOT EXISTS systems (
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  observed_at_utc TEXT NOT NULL,
  is_empty INTEGER NOT NULL,
  planets_json TEXT NOT NULL,
  PRIMARY KEY (galaxy, system)
);
CREATE TABLE IF NOT EXISTS targets (
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  position INTEGER NOT NULL,
  celestial_type INTEGER NOT NULL,
  name TEXT NOT NULL,
  state INTEGER NOT NULL,
  report_json TEXT NULL,
  consumed_report_id INTEGER NULL,
  updated_at_utc TEXT NOT NULL,
  held_for_manual_review INTEGER NOT NULL DEFAULT 0,
  PRIMARY KEY (galaxy, system, position, celestial_type)
);
CREATE TABLE IF NOT EXISTS consumed_reports (
  report_id INTEGER NOT NULL PRIMARY KEY,
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  position INTEGER NOT NULL,
  celestial_type INTEGER NOT NULL,
  consumed_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS attacks (
  report_id INTEGER NOT NULL PRIMARY KEY,
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  position INTEGER NOT NULL,
  celestial_type INTEGER NOT NULL,
  target_name TEXT NOT NULL,
  dispatched_at_utc TEXT NOT NULL,
  metal INTEGER NOT NULL,
  crystal INTEGER NOT NULL,
  deuterium INTEGER NOT NULL,
  origin_galaxy INTEGER NULL,
  origin_system INTEGER NULL,
  origin_position INTEGER NULL,
  origin_celestial_type INTEGER NULL,
  mission TEXT NULL
);
CREATE TABLE IF NOT EXISTS scan_cursor (
  id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
  range_index INTEGER NOT NULL,
  galaxy INTEGER NOT NULL,
  system INTEGER NOT NULL,
  updated_at_utc TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS main_scan_state (
  id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
  completed INTEGER NOT NULL
);";
				command.ExecuteNonQuery();

				try {
					using var migration = connection.CreateCommand();
					migration.CommandText = "ALTER TABLE targets ADD COLUMN consumed_report_id INTEGER NULL;";
					migration.ExecuteNonQuery();
				} catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) {
				}

				try {
					using var migration = connection.CreateCommand();
					migration.CommandText = "ALTER TABLE targets ADD COLUMN held_for_manual_review INTEGER NOT NULL DEFAULT 0;";
					migration.ExecuteNonQuery();
				} catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) {
				}

				foreach (var column in new[] {
					"origin_galaxy INTEGER NULL",
					"origin_system INTEGER NULL",
					"origin_position INTEGER NULL",
					"origin_celestial_type INTEGER NULL",
					"mission TEXT NULL"
				}) {
					try {
						using var migration = connection.CreateCommand();
						migration.CommandText = $"ALTER TABLE attacks ADD COLUMN {column};";
						migration.ExecuteNonQuery();
					} catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) {
					}
				}

				using var backfill = connection.CreateCommand();
				backfill.CommandText = @"
INSERT OR IGNORE INTO consumed_reports (
  report_id, galaxy, system, position, celestial_type, consumed_at_utc)
SELECT consumed_report_id, galaxy, system, position, celestial_type, updated_at_utc
FROM targets
WHERE consumed_report_id IS NOT NULL AND consumed_report_id > 0;";
				backfill.ExecuteNonQuery();

				try {
					using var lossTable = connection.CreateCommand();
					lossTable.CommandText = @"
			CREATE TABLE IF NOT EXISTS fleet_losses (
			fleet_id INTEGER NOT NULL PRIMARY KEY,
			galaxy INTEGER NOT NULL,
			system INTEGER NOT NULL,
			position INTEGER NOT NULL,
			celestial_type INTEGER NOT NULL,
			origin_galaxy INTEGER NOT NULL,
			origin_system INTEGER NOT NULL,
			origin_position INTEGER NOT NULL,
			origin_celestial_type INTEGER NOT NULL,
			dispatched_at_utc TEXT NOT NULL,
			expected_arrival_utc TEXT NOT NULL,
			expected_return_utc TEXT NOT NULL,
			sent_json TEXT NOT NULL,
			returned_json TEXT NULL,
			status TEXT NOT NULL,
			miss_count INTEGER NOT NULL DEFAULT 0,
			resolved_at_utc TEXT NULL,
			loss_metal INTEGER NOT NULL DEFAULT 0,
			loss_crystal INTEGER NOT NULL DEFAULT 0,
			loss_deuterium INTEGER NOT NULL DEFAULT 0,
			loot_metal INTEGER NOT NULL DEFAULT 0,
			loot_crystal INTEGER NOT NULL DEFAULT 0,
			loot_deuterium INTEGER NOT NULL DEFAULT 0
			);
			CREATE INDEX IF NOT EXISTS ix_fleet_losses_status ON fleet_losses(status);
			CREATE INDEX IF NOT EXISTS ix_fleet_losses_dispatched ON fleet_losses(dispatched_at_utc);";
					lossTable.ExecuteNonQuery();
					foreach (var col in new[] { "got_known INTEGER NOT NULL DEFAULT 0", "got_metal INTEGER NOT NULL DEFAULT 0", "got_crystal INTEGER NOT NULL DEFAULT 0", "got_deuterium INTEGER NOT NULL DEFAULT 0", "fuel_deuterium INTEGER NOT NULL DEFAULT 0" }) {
						try {
							using var alter = connection.CreateCommand();
							alter.CommandText = "ALTER TABLE fleet_losses ADD COLUMN " + col + ";";
							alter.ExecuteNonQuery();
						} catch { }
					}
				} catch {
					_lossTrackingEnabled = false;
				}
			} catch {
				_enabled = false;
			}
		}

		private SqliteConnection OpenConnection() {
			var connection = new SqliteConnection(_connectionString);
			connection.Open();
			return connection;
		}

		private static string FormatUtc(DateTime value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

		private static DateTime ParseUtc(string value) => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

		public void Dispose() {
		}
	}
}