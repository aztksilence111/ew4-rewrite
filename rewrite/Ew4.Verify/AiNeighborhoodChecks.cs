using System.Text.Json;
using Ew4.Core;

internal static class AiNeighborhoodChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        var fixture = WorldChecks.Example();
        var map = new HexMap(17, 11);
        var units = Enumerable.Range(0, 180).Reverse().Select(i => fixture.Units[i % 2] with {
            Id = "neighborhood." + i, Cell = i / 2,
            Owner = fixture.Units[i / 2 % 2].Owner,
            Combatant = fixture.Units[i % 2].Combatant with { Profile = fixture.Units[i % 2].Combatant.Profile with { Grade = "0" } } }).ToArray();
        var world = fixture with { Width = map.Width, Height = map.Height, Units = units,
            Limits = new() { UnitsPerCell = 2 }, Cells = Enumerable.Range(0, map.Count).Select(_ => new CellState()).ToArray() };
        world.Validate();
        var queries = new PlanningQueries(world);
        foreach (int center in Enumerable.Range(0, map.Count))
        foreach (int radius in new[] { 0, 1, 2, 4, 16, int.MaxValue })
        foreach (bool includeCenter in new[] { false, true })
            Check(queries.Nearby(map, center, radius, includeCenter).Select(u => u.Id).SequenceEqual(units
                .Where(u => (includeCenter || u.Cell != center) && map.Distance(center, u.Cell) <= radius).Select(u => u.Id)),
                "Indexed neighborhood changed membership or army order");
        var rules = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        foreach (int radius in new[] { 0, 1, 2, 20 })
        {
            var sample = world with { Units = units.Select(u => u with { Combatant = u.Combatant with {
                Profile = u.Combatant.Profile with { MinimumRange = 0, MaximumRange = radius } } }).ToArray() };
            foreach (var actor in sample.Units.Where(u => u.Owner == sample.ActiveFaction).Take(12))
            {
                string reference = JsonSerializer.Serialize(WorldAi.StrikeCandidates(sample, actor.Id, rules));
                using var scope = PlanningScope.Begin(rules);
                sample.Validate();
                Check(reference == JsonSerializer.Serialize(WorldAi.StrikeCandidates(sample, actor.Id, rules)),
                    "Indexed strike changed candidates, scores or tie order");
            }
        }
        return checks;
    }
}
