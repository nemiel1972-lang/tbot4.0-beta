using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tbot.Common.Combat;

public static class BattleLogStore {
    private static readonly object Sync = new();
    private static DateTime _lastPruneUtc = DateTime.MinValue;
    private static readonly JsonSerializerOptions JsonOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string RootPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "battle", "logs");

    public const double EqualTolerance = 0.02;

    public static int RetentionDays { get; set; } = 30;

    public static void Append(BattleLogRecord record, int? retentionDays = null) {
        ArgumentNullException.ThrowIfNull(record);
        lock (Sync) {
            if (retentionDays.HasValue) RetentionDays = Math.Clamp(retentionDays.Value, 1, 3650);
            Directory.CreateDirectory(RootPath);
            var path = Path.Combine(RootPath, $"battle-{DateTime.UtcNow:yyyyMMdd}.jsonl");
            File.AppendAllText(path, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
            if (DateTime.UtcNow - _lastPruneUtc > TimeSpan.FromHours(1)) {
                _lastPruneUtc = DateTime.UtcNow;
                PruneUnsafe(RetentionDays);
            }
        }
    }

    public static void AppendActual(string battleId, BattleLogActual actual, int? retentionDays = null) {
        if (string.IsNullOrWhiteSpace(battleId)) throw new ArgumentException("A battleId is required.", nameof(battleId));
        ArgumentNullException.ThrowIfNull(actual);
        Append(new BattleLogRecord { BattleId = battleId, Kind = "actual", Actual = actual, TimeUtc = DateTime.UtcNow }, retentionDays);
    }

    public static void AppendLink(string battleId, int fleetId, int? retentionDays = null) {
        if (string.IsNullOrWhiteSpace(battleId)) throw new ArgumentException("A battleId is required.", nameof(battleId));
        Append(new BattleLogRecord { BattleId = battleId, Kind = "link", FleetId = fleetId, TimeUtc = DateTime.UtcNow }, retentionDays);
    }

    public static string? FindBattleIdForFleet(int fleetId, int lookbackDays = 14) {
        lock (Sync) {
            if (!Directory.Exists(RootPath)) return null;
            var oldest = DateTime.UtcNow.Date.AddDays(-Math.Clamp(lookbackDays, 1, 3650));
            foreach (var file in Directory.EnumerateFiles(RootPath, "battle-*.jsonl").OrderByDescending(x => x)) {
                if (File.GetLastWriteTimeUtc(file).Date < oldest) continue;
                foreach (var line in ReadLinesShared(file).Reverse()) {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try {
                        var record = JsonSerializer.Deserialize<BattleLogRecord>(line, JsonOptions);
                        if (record != null && record.Kind == "link" && record.FleetId == fleetId) return record.BattleId;
                    } catch (JsonException) {  }
                }
            }
            return null;
        }
    }

    public static bool TryAppendActualForFleet(int fleetId, BattleLogActual actual, int? retentionDays = null) {
        var battleId = FindBattleIdForFleet(fleetId);
        if (battleId == null) return false;
        AppendActual(battleId, actual, retentionDays);
        return true;
    }

    public static IReadOnlyList<BattleLogRecord> Read(DateTime? fromUtc = null, DateTime? toUtc = null, string? target = null, bool onlyWithActual = false, int limit = 500,
        bool onlyDeviations = false, bool onlyRustErrors = false) {
        lock (Sync) {
            if (!Directory.Exists(RootPath)) return Array.Empty<BattleLogRecord>();
            var raw = new List<BattleLogRecord>();
            foreach (var file in Directory.EnumerateFiles(RootPath, "battle-*.jsonl").OrderByDescending(x => x)) {
                foreach (var line in ReadLinesShared(file)) {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try {
                        var record = JsonSerializer.Deserialize<BattleLogRecord>(line, JsonOptions);
                        if (record != null) raw.Add(record);
                    } catch (JsonException) {  }
                }
            }

            var merged = new List<BattleLogRecord>();
            foreach (var group in raw.GroupBy(x => x.BattleId, StringComparer.Ordinal)) {
                var simulation = group.FirstOrDefault(x => x.Kind == "sim");
                if (simulation == null) continue;
                var actualRecord = group.LastOrDefault(x => x.Kind == "actual" && x.Actual != null);
                var link = group.LastOrDefault(x => x.Kind == "link");
                var record = WithOutcome(simulation, actualRecord?.Actual, link?.FleetId);
                if ((fromUtc.HasValue && record.TimeUtc < fromUtc.Value) || (toUtc.HasValue && record.TimeUtc > toUtc.Value)) continue;
                if (!string.IsNullOrWhiteSpace(target)
                    && !((record.Target ?? "").Contains(target, StringComparison.OrdinalIgnoreCase) || (record.Name ?? "").Contains(target, StringComparison.OrdinalIgnoreCase))) continue;
                if (onlyWithActual && record.Actual == null) continue;
                if (onlyRustErrors && string.IsNullOrEmpty(record.Error)) continue;
                if (onlyDeviations && !HasDeviation(record)) continue;
                merged.Add(record);
            }
            return merged.OrderByDescending(x => x.TimeUtc).Take(Math.Clamp(limit, 1, 5000)).ToList();
        }
    }

    public static bool HasDeviation(BattleLogRecord record) {
        if (record.Go?.AttackerWin == null || record.Rust?.AttackerWin == null) return false;
        if (Math.Abs(record.Go.AttackerWin.Value - record.Rust.AttackerWin.Value) >= 5) return true;
        long go = record.Go.AttackerLosses ?? 0, rust = record.Rust.AttackerLosses ?? 0;
        long baseline = Math.Max(Math.Max(go, rust), 1);
        return Math.Abs(go - rust) / (double) baseline >= 0.10;
    }

    public static string? ComputeBestCalc(BattleLogSide? go, BattleLogSide? rust, BattleLogActual? actual) {
        var actualLoss = SumOrNull(actual?.AttackerLosses);
        if (go?.AttackerLosses == null || rust?.AttackerLosses == null || actualLoss == null) return null;
        double goErr = Math.Abs(go.AttackerLosses.Value - actualLoss.Value);
        double rustErr = Math.Abs(rust.AttackerLosses.Value - actualLoss.Value);
        double scale = Math.Max(Math.Max(Math.Max(actualLoss.Value, go.AttackerLosses.Value), rust.AttackerLosses.Value), 1);
        if (Math.Abs(goErr - rustErr) / scale < EqualTolerance) return "equal";
        return goErr < rustErr ? "ogame" : "rust";
    }

    public static BattleLogSummary Summarize(IEnumerable<BattleLogRecord> records) {
        var list = records.ToList();
        var withActual = list.Where(x => x.Actual != null).ToList();
        var comparable = withActual.Where(x => x.BestCalc != null).ToList();
        double Err(BattleLogSide? side, BattleLogRecord r) {
            var actualLoss = SumOrNull(r.Actual?.AttackerLosses) ?? 0;
            double scale = Math.Max(Math.Max(Math.Max(actualLoss, r.Go?.AttackerLosses ?? 0), r.Rust?.AttackerLosses ?? 0), 1);
            return Math.Abs((side?.AttackerLosses ?? 0) - actualLoss) / scale;
        }
        double? Brier(Func<BattleLogRecord, BattleLogSide?> pick) {
            var items = withActual.Where(x => x.Actual!.Won != null && pick(x)?.AttackerWin != null).ToList();
            if (items.Count == 0) return null;
            return items.Average(x => Math.Pow(pick(x)!.AttackerWin!.Value / 100.0 - (x.Actual!.Won == true ? 1.0 : 0.0), 2));
        }
        return new BattleLogSummary {
            Total = list.Count,
            WithActual = withActual.Count,
            Won = withActual.Count(x => x.Actual!.Won == true),
            Lost = withActual.Count(x => x.Actual!.Won == false),
            RustErrors = list.Count(x => !string.IsNullOrEmpty(x.Error)),
            Deviations = list.Count(HasDeviation),
            BestOgame = comparable.Count(x => x.BestCalc == "ogame"),
            BestRust = comparable.Count(x => x.BestCalc == "rust"),
            BestEqual = comparable.Count(x => x.BestCalc == "equal"),
            MeanLossErrorOgame = comparable.Count == 0 ? null : comparable.Average(x => Err(x.Go, x)),
            MeanLossErrorRust = comparable.Count == 0 ? null : comparable.Average(x => Err(x.Rust, x)),
            BrierOgame = Brier(x => x.Go),
            BrierRust = Brier(x => x.Rust),
            MeanMsOgame = list.Where(x => x.GoMs != null).Select(x => x.GoMs!.Value).DefaultIfEmpty(double.NaN).Average() is var g && !double.IsNaN(g) ? g : null,
            MeanMsRust = list.Where(x => x.RustMs != null && string.IsNullOrEmpty(x.Error)).Select(x => x.RustMs!.Value).DefaultIfEmpty(double.NaN).Average() is var r && !double.IsNaN(r) ? r : null
        };
    }

    private static long? SumOrNull(long[]? values) => values == null || values.Length == 0 ? null : values.Sum();

    private static BattleLogRecord WithOutcome(BattleLogRecord source, BattleLogActual? actual, int? fleetId) => new() {
        Schema = source.Schema, BattleId = source.BattleId, TimeUtc = source.TimeUtc, Mode = source.Mode,
        Target = source.Target, Name = source.Name, Simulations = source.Simulations,
        Go = source.Go, Rust = source.Rust, Actual = actual, Error = source.Error, Kind = source.Kind, Input = source.Input,
        Accepted = source.Accepted, Reason = source.Reason, GoMs = source.GoMs, RustMs = source.RustMs,
        RustVersion = source.RustVersion, BonusMode = source.BonusMode, FleetId = fleetId ?? source.FleetId,
        BestCalc = actual == null ? null : ComputeBestCalc(source.Go, source.Rust, actual)
    };

    private static IEnumerable<string> ReadLinesShared(string path) {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null) yield return line;
    }

    private static void PruneUnsafe(int retentionDays) {
        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(RootPath, "battle-*.jsonl")) {
            if (File.GetLastWriteTimeUtc(file).Date < cutoff) {
                try { File.Delete(file); } catch (IOException) { }
            }
        }
    }
}

public sealed class BattleLogRecord {
    public int Schema { get; init; } = 1;
    public string BattleId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime TimeUtc { get; init; } = DateTime.UtcNow;
    public string Mode { get; init; } = "shadow";
    public string? Target { get; init; }
    public string? Name { get; init; }
    public string? BestCalc { get; init; }
    public int? Simulations { get; init; }
    public BattleLogSide? Go { get; init; }
    public BattleLogSide? Rust { get; init; }
    public BattleLogActual? Actual { get; init; }
    public BattleLogInput? Input { get; init; }
    public string? Error { get; init; }
    public string Kind { get; init; } = "sim";
    public int? FleetId { get; init; }
    public bool? Accepted { get; init; }
    public string? Reason { get; init; }
    public double? GoMs { get; init; }
    public double? RustMs { get; init; }
    public string? RustVersion { get; init; }
    public string? BonusMode { get; init; }
}

public sealed class BattleLogSide {
    public int? AttackerWin { get; init; }
    public int? DefenderWin { get; init; }
    public int? Draw { get; init; }
    public long? AttackerLosses { get; init; }
    public long? DefenderLosses { get; init; }
    public long? Debris { get; init; }
    public int? Rounds { get; init; }
    public long[]? AttackerLossesMcd { get; init; }
    public long[]? DefenderLossesMcd { get; init; }
    public long[]? DebrisMcd { get; init; }
}

public sealed class BattleLogActual {
    public bool? Won { get; init; }
    public long[]? AttackerLosses { get; init; }
    public long[]? DefenderLosses { get; init; }
    public long[]? Debris { get; init; }
    public long[]? Loot { get; init; }
    public string? Source { get; init; }
}

public sealed class BattleLogInput {
    public int[]? AttackerTech { get; init; }
    public int[]? DefenderTech { get; init; }
    public Dictionary<string, long>? AttackerEntities { get; init; }
    public Dictionary<string, long>? DefenderEntities { get; init; }
    public long[]? Resources { get; init; }
    public int? Simulations { get; init; }
    public double? FleetToDebris { get; init; }
    public string? BonusMode { get; init; }
}

public sealed class BattleLogSummary {
    public int Total { get; init; }
    public int WithActual { get; init; }
    public int Won { get; init; }
    public int Lost { get; init; }
    public int RustErrors { get; init; }
    public int Deviations { get; init; }
    public int BestOgame { get; init; }
    public int BestRust { get; init; }
    public int BestEqual { get; init; }
    public double? MeanLossErrorOgame { get; init; }
    public double? MeanLossErrorRust { get; init; }
    public double? BrierOgame { get; init; }
    public double? BrierRust { get; init; }
    public double? MeanMsOgame { get; init; }
    public double? MeanMsRust { get; init; }
}
