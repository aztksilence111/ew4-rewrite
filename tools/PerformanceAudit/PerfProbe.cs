using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Ew4.Core;

// Diagnostic-only: copied into an isolated build, never included in the game project.
public static class PerfProbe
{
    public sealed class Metric
    {
        public string Name { get; set; } = "";
        public long Calls { get; set; }
        public double InclusiveMs { get; set; }
        public double ExclusiveMs { get; set; }
        public long InclusiveBytes { get; set; }
        public long ExclusiveBytes { get; set; }
    }
    private struct Frame
    {
        public Metric Metric;
        public long Started, Bytes, ChildTicks, ChildBytes;
    }
    private static readonly Dictionary<string, Metric> Metrics = new();
    private static readonly Frame[] Frames = new Frame[512];
    private static int depth;
    private static ConditionalWeakTable<WorldState, object> worlds = new();
    public static long WorldValidationCalls, WorldSnapshots, CellValidationVisits, UnitValidationVisits;
    public static void ObserveWorld(WorldState world)
    {
        if (!Active) return;
        WorldValidationCalls++; CellValidationVisits += world.Cells.Count; UnitValidationVisits += world.Units.Count;
        if (!worlds.TryGetValue(world, out _)) { worlds.Add(world, new object()); WorldSnapshots++; }
    }
    public static bool Active;
    public static void Reset() { if (depth != 0) throw new InvalidOperationException("Probe scope open"); Metrics.Clear(); worlds = new(); WorldValidationCalls = WorldSnapshots = CellValidationVisits = UnitValidationVisits = 0; }
    public static Metric[] Snapshot() => Metrics.Values.OrderByDescending(m => m.ExclusiveMs).ToArray();
    public static Scope Enter(string name)
    {
        if (!Active) return new Scope(-1);
        if (!Metrics.TryGetValue(name, out var metric)) Metrics.Add(name, metric = new Metric { Name = name });
        int slot = depth++;
        Frames[slot] = new Frame { Metric = metric, Started = Stopwatch.GetTimestamp(), Bytes = GC.GetAllocatedBytesForCurrentThread() };
        return new Scope(slot);
    }
    public static T Measure<T>(string name, Func<T> operation) { using var scope = Enter(name); return operation(); }
    public readonly struct Scope : IDisposable
    {
        private readonly int slot;
        public Scope(int slot) { this.slot = slot; }
        public void Dispose()
        {
            if (slot < 0) return;
            long now = Stopwatch.GetTimestamp(), allocated = GC.GetAllocatedBytesForCurrentThread();
            var frame = Frames[slot];
            if (--depth != slot) throw new InvalidOperationException("Probe nesting mismatch");
            long ticks = now - frame.Started, bytes = allocated - frame.Bytes;
            frame.Metric.Calls++;
            frame.Metric.InclusiveMs += ticks * 1000.0 / Stopwatch.Frequency;
            frame.Metric.ExclusiveMs += (ticks - frame.ChildTicks) * 1000.0 / Stopwatch.Frequency;
            frame.Metric.InclusiveBytes += bytes;
            frame.Metric.ExclusiveBytes += bytes - frame.ChildBytes;
            if (slot > 0) { Frames[slot - 1].ChildTicks += ticks; Frames[slot - 1].ChildBytes += bytes; }
        }
    }
}
