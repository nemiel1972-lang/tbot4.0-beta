namespace Tbot.Common.Combat;

public static class SimulatorBridge {
    public static Func<string, ManualSimInput, Task<ManualSimOutput>>? ManualSimulation { get; set; }
}

public sealed class ManualSimInput {
    public int[] AttackerTech { get; set; } = new int[3];
    public int[] DefenderTech { get; set; } = new int[3];
    public Dictionary<string, long> AttackerShips { get; set; } = new();
    public Dictionary<string, long> DefenderEntities { get; set; } = new();
    public long[] Resources { get; set; } = new long[3];
    public int Simulations { get; set; } = 500;
    public double FleetToDebrisPercent { get; set; } = 30;
    public bool DefenderIsGeneral { get; set; }
}

public sealed class ManualSimSide {
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public double Ms { get; set; }
    public int Simulations { get; set; }
    public int AttackerWin { get; set; }
    public int DefenderWin { get; set; }
    public int Draw { get; set; }
    public int Rounds { get; set; }
    public long[] AttackerLosses { get; set; } = new long[3];
    public long[] DefenderLosses { get; set; } = new long[3];
    public long[] Debris { get; set; } = new long[3];
    public int Recycler { get; set; }
    public int MoonChance { get; set; }
}

public sealed class ManualSimOutput {
    public ManualSimSide Ogame { get; set; } = new();
    public ManualSimSide Rust { get; set; } = new();
}
