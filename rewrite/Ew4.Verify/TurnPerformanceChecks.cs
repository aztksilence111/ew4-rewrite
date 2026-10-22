using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class TurnPerformanceChecks
{
    public static void Run(string root, string phase)
    {
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data",
            path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var started = content.Start("conquest1", "fra");
        // Session identity is normally random; fix it for byte-for-byte before/after comparison.
        var session = started.Session with { BattleId = "turn-performance-fixed",
            World = started.Session.World with { RandomState = 481960042392081575, AiRandomState = 3493881671538411339 } };
        var refresh = typeof(WorldTurns).Assembly.GetType("Ew4.Core.WorldDeaths")!
            .GetMethod("RefreshMorale", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<WorldState, IReadOnlyCollection<string>, WorldState>>();
        var samples = new List<object>();
        string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
        foreach (var (name, iterations, operation) in new (string, int, Func<WorldState>)[] {
            ("refreshMorale", 200, () => refresh(session.World, started.Rules.Turns.NeutralCamps)),
            ("endFaction", 20, () => WorldTurns.EndTurn(session.World, session.World.Revision, started.Rules.Turns).State) })
        {
            for (int i = 0; i < 10; i++) operation();
            var times = new List<double>(); var allocations = new List<long>(); WorldState final = session.World;
            for (int sample = 0; sample < 7; sample++)
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread(); var clock = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) final = operation();
                times.Add(clock.Elapsed.TotalMilliseconds / iterations);
                allocations.Add((GC.GetAllocatedBytesForCurrentThread() - allocated) / iterations);
            }
            samples.Add(new { name, iterations, medianMs = times.Order().ElementAt(3),
                bytesPerOperation = allocations.Order().ElementAt(3),
                worldSha256 = Hash(WorldSave.Encode(final)), sessionSha256 = Hash(SessionSave.Encode(session with { World = final })),
                final.RandomState, final.AiRandomState, final.Revision, units = final.Units.Count });
        }
        string folder = Path.Combine(root, "build/turn-performance-20261022"); Directory.CreateDirectory(folder);
        string result = JsonSerializer.Serialize(new { phase, scenario = "conquest1/fra", units = session.World.Units.Count,
            scope = "Fixed desktop rule operations, seven samples; not a phone or full AI round benchmark", samples }, new JsonSerializerOptions { WriteIndented = true });
        if (phase == "after")
        {
            using var before = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "before.json")));
            using var after = JsonDocument.Parse(result);
            foreach (var pair in before.RootElement.GetProperty("samples").EnumerateArray().Zip(after.RootElement.GetProperty("samples").EnumerateArray()))
                foreach (string field in new[] { "worldSha256", "sessionSha256", "RandomState", "AiRandomState", "Revision", "units" })
                    if (pair.First.GetProperty(field).GetRawText() != pair.Second.GetProperty(field).GetRawText())
                        throw new InvalidOperationException("Performance result changed " + field);
        }
        File.WriteAllText(Path.Combine(folder, phase + ".json"), result + Environment.NewLine);
        Console.WriteLine(result);
    }
}
