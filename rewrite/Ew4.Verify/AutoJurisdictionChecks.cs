using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class AutoJurisdictionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("Automatic jurisdictions: " + label); checks++; }
        WorldState Land(int width, int height) => WorldChecks.Example() with
        {
            Width = width, Height = height, Units = [], Jurisdictions = null,
            Cells = Enumerable.Range(0, width * height).Select(_ => new CellState { Owner = "winner" }).ToArray()
        };
        WorldState Sites(WorldState world, params (int Cell, string Kind, int Level)[] sites)
        {
            var cells = world.Cells.ToArray();
            foreach (var site in sites) cells[site.Cell] = cells[site.Cell] with { BuildingKind = site.Kind, BuildingLevel = site.Level };
            return world with { Cells = cells };
        }
        foreach (string kind in new[] { "industry", "stable" })
        {
            var world = Sites(Land(13, 9), (58, "city", 4), (59, kind, 1));
            var areas = TerritorialJurisdictions.Create(world);
            int facilitySize = areas.CellCenters.Count(c => c == 59), citySize = areas.CellCenters.Count(c => c == 58);
            Check(facilitySize > 1 && facilitySize < 12, "adjacent " + kind + " retains a compact multi-cell district");
            Check(citySize > facilitySize * 5, "large city owns most surrounding land for " + kind);
            var map = new HexMap(13, 9);
            Check(Enumerable.Range(0, world.Cells.Count).Where(i => areas.CellCenters[i] == 59).All(i =>
                Enumerable.Range(0, 6).All(d => map.Neighbor(i, d) is int n && n >= 0
                    && (areas.CellCenters[n] == 58 || areas.CellCenters[n] == 59))), "city fully encloses a multi-cell " + kind);
            Check(areas.Borders.SequenceEqual(new[] { new JurisdictionBorder(58, 59) }), "enclosed facility still has a land border");
            var far = Sites(Land(25, 1), (1, "city", 1), (20, kind, 1));
            Check(TerritorialJurisdictions.Create(far).CellCenters[24] == 20, "distant " + kind + " serves land without a nearby city");
            var alone = Sites(Land(7, 5), (17, kind, 1));
            Check(TerritorialJurisdictions.Create(alone).CellCenters.All(c => c == 17), kind + " works without any city");
            var separated = Sites(Land(13, 9), (55, "city", 7), (62, kind, 1));
            Check(TerritorialJurisdictions.Create(separated).CellCenters.Count(c => c == 62) > facilitySize * 2,
                "separated " + kind + " has room to grow instead of a fixed size");
        }
        var ranked = Sites(Land(13, 9), (58, "city", 4), (59, "city", 1));
        var rankedAreas = TerritorialJurisdictions.Create(ranked);
        Check(rankedAreas.CellCenters.Count(c => c == 59) > 1, "adjacent lower city retains surrounding land");
        Check(rankedAreas.CellCenters.Count(c => c == 58) > rankedAreas.CellCenters.Count(c => c == 59), "higher city has more influence");
        var spaced = Sites(Land(25, 1), (2, "city", 1), (20, "city", 4));
        var spacedAreas = TerritorialJurisdictions.Create(spaced);
        Check(spacedAreas.CellCenters[11] == 20, "higher city gains land beyond the equal-distance border");
        Check(spacedAreas.CellCenters[0] == 2, "lower city keeps remote land without a nearby higher city");
        var equal = Sites(Land(9, 1), (1, "city", 2), (7, "city", 2));
        Check(TerritorialJurisdictions.Create(equal).CellCenters.SequenceEqual(new[] { 1, 1, 1, 1, 1, 7, 7, 7, 7 }), "equal cities use distance and stable cell order");
        var narrow = Sites(Land(9, 1), (1, "city", 4), (4, "industry", 1));
        Check(TerritorialJurisdictions.Create(narrow).CellCenters[2] == 1, "city has priority in a narrow corridor");
        var blocked = Sites(Land(17, 1), (1, "city", 7), (4, "city", 1));
        var blockedAreas = TerritorialJurisdictions.Create(blocked);
        Check(blockedAreas.CellCenters[16] == 4, "unreachable city fragment goes to the nearer connected smaller city");
        Check(OrphanedCells(blocked, blockedAreas).Length == 0, "narrow corridor leaves no centerless fragment");
        var independent = Sites(Land(21, 13), (126, "city", 7), (140, "city", 2));
        Check(TerritorialJurisdictions.Create(independent).CellCenters.Count(c => c == 140) > 20,
            "remote small city retains a substantial hinterland");

        var random = new Random(20261025);
        for (int sample = 0; sample < 60; sample++)
        {
            var world = Land(8, 6);
            var cells = world.Cells.Select(_ => new CellState
            {
                Owner = random.Next(4) switch { 0 => "loser", 1 => null, _ => "winner" },
                Terrain = new MovementCell { Sea = random.Next(7) == 0,
                    Barrier = random.Next(11) == 0 ? TerrainBarrier.Mountain : TerrainBarrier.None }
            }).ToArray();
            foreach (int site in new[] { 2, 11, 24, 37, 45 })
                cells[site] = new CellState { Owner = site % 2 == 0 ? "winner" : "loser",
                    BuildingKind = site % 3 == 0 ? "stable" : site % 3 == 1 ? "industry" : "city", BuildingLevel = random.Next(1, 5) };
            cells[18] = new CellState { Owner = "winner", BuildingKind = "port", BuildingLevel = 1 };
            world = world with { Cells = cells };
            var actual = TerritorialJurisdictions.Create(world);
            var expected = Reference(world, weighted: true);
            Check(actual.CellCenters.SequenceEqual(expected), "independent per-center path search " + sample);
            Check(OrphanedCells(world, actual).Length == 0, "every non-detached district connects to its center " + sample);
            Check(actual.CellCenters.SequenceEqual(TerritorialJurisdictions.Create(world).CellCenters), "deterministic generation " + sample);
            Check(Enumerable.Range(0, cells.Length).Where(i => !Traversable(cells[i])).All(i => actual.CellCenters[i] == -1), "water ports and barriers stay unassigned " + sample);
            Check(actual.Borders.All(b => Enumerable.Range(0, cells.Length).Any(i => actual.CellCenters[i] == b.First
                && !actual.DetachedCells.Contains(i) && Enumerable.Range(0, 6).Any(d => new HexMap(8, 6).Neighbor(i, d) is int n && n >= 0
                    && actual.CellCenters[n] == b.Second && !actual.DetachedCells.Contains(n)))), "borders correspond to actual adjoining land " + sample);
            actual.Validate(world);
            TerritorialJurisdictions.FromAssignments(world, actual.CellCenters).Validate(world);
        }

        var savedWorld = ranked with { Jurisdictions = rankedAreas };
        savedWorld.Validate();
        var restored = WorldSave.Decode(WorldSave.Encode(savedWorld), savedWorld.ContentRevision);
        Check(restored.Jurisdictions!.CellCenters.SequenceEqual(rankedAreas.CellCenters), "save retains enclosed geometry");
        var upgraded = restored with { Cells = restored.Cells.Select((c, i) => i == 59 ? c with { BuildingLevel = 5 } : c).ToArray() };
        Check(upgraded.Jurisdictions!.CellCenters.SequenceEqual(rankedAreas.CellCenters), "in-game upgrades never repartition an existing save");

        string data = Path.Combine(root, "rewrite/data");
        byte[] Read(string p) => File.ReadAllBytes(Path.Combine(data, p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(data, "original"), "*.btl"));
        var comparisons = new List<object>();
        foreach (string id in new[] { "conquest1", "campaign1_01", "campaign1_08", "campaign1_14" })
        {
            var world = content.Start(id, id == "conquest1" ? "fra" : null).Session.World;
            var areas = world.Jurisdictions!;
            areas.Validate(world);
            Check(areas.CellCenters.SequenceEqual(Reference(world, weighted: true)), "real scenario matches independent distances " + id);
            var old = Reference(world, weighted: false);
            int CountCity(IReadOnlyList<int> assignments) => assignments.Count(c => c >= 0 && world.Cells[c].BuildingKind == "city");
            var districts = areas.CellCenters.Where(c => c >= 0).Distinct().Select(c => new
            {
                center = c, name = world.Cells[c].Name, kind = world.Cells[c].BuildingKind, level = world.Cells[c].BuildingLevel,
                before = old.Count(r => r == c), after = areas.CellCenters.Count(r => r == c)
            }).ToArray();
            Check(CountCity(areas.CellCenters) >= CountCity(old), "cities gain overall district area " + id);
            Check(districts.All(d => areas.CellCenters[d.center] == d.center), "all scenario centers keep their own cell " + id);
            if (id == "conquest1")
            {
                Check(new[] { 3436, 3515, 3593 }.All(i => areas.CellCenters[i] == 3595),
                    "Rome/Naples border fragment joins the neighboring factory instead of detached Naples land");
                Check(districts.Single(d => d.center == 3023).after >= 10, "user image 2 isolated level II city has a real district");
                Check(districts.Single(d => d.center == 2551).after >= 10, "Clermont no longer swallowed by Paris");
                Check(districts.Single(d => d.center == 2713).after >= 8, "Lyon no longer swallowed by Paris");
                Check(districts.Where(d => d.center is 3027 or 3028).All(d => d.after < districts.Single(c => c.center == 3023).after),
                    "user image 3 adjacent Marseille facilities smaller than the separate city");
            }
            comparisons.Add(new { scenario = id, beforeCityCells = CountCity(old), afterCityCells = CountCity(areas.CellCenters),
                changedCells = old.Where((c, i) => c != areas.CellCenters[i]).Count(), districts });
        }
        foreach (var choice in content.Scenarios.Where(s => s.Mode is 1 or 2))
        {
            var battle = content.Battle(choice.Id);
            var world = content.Start(choice.Id, choice.Mode == 2 ? battle.Factions.First(f => f.Camp != 4).CountryId : null).Session.World;
            Check(OrphanedCells(world, world.Jurisdictions!).Length == 0, "all scenario districts connected: " + choice.Id);
        }
        string folder = Path.Combine(root, "build/auto-jurisdictions-connected-20261025");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "backend.json"), JsonSerializer.Serialize(new { checks, errors = 0, comparisons },
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        return checks;
    }

    private static bool Traversable(CellState c) => c.Terrain.CanTraverse() && !c.Terrain.Sea && c.BuildingKind != "port";

    internal static int[] OrphanedCells(WorldState world, TerritorialJurisdictions areas)
    {
        var connected = ConnectedCells(world, areas.CellCenters);
        var detached = areas.DetachedCells.ToHashSet();
        return Enumerable.Range(0, connected.Length).Where(i => areas.CellCenters[i] >= 0 && !connected[i] && !detached.Contains(i)).ToArray();
    }

    private static bool[] ConnectedCells(WorldState world, IReadOnlyList<int> centers)
    {
        var connected = new bool[centers.Count];
        var map = new HexMap(world.Width, world.Height);
        foreach (int root in centers.Where(c => c >= 0).Distinct())
        {
            var queue = new Queue<int>(); queue.Enqueue(root); connected[root] = true;
            while (queue.TryDequeue(out int cell))
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(cell, d);
                    if (next < 0 || connected[next] || centers[next] != root || world.Cells[next].Owner != world.Cells[root].Owner) continue;
                    connected[next] = true; queue.Enqueue(next);
                }
        }
        return connected;
    }

    // Independent full BFS per center: no shared frontier or competitive propagation.
    private static int[] Reference(WorldState world, bool weighted)
    {
        var map = new HexMap(world.Width, world.Height);
        var roots = Enumerable.Range(0, world.Cells.Count).Where(i => Traversable(world.Cells[i])
            && TerritorialJurisdictions.IsCenter(world.Cells[i]) && world.Cells[i].Owner != null).ToArray();
        int[] Distances(int center, bool country)
        {
            var distances = Enumerable.Repeat(-1, world.Cells.Count).ToArray();
            var queue = new Queue<int>(); distances[center] = 0; queue.Enqueue(center);
            while (queue.TryDequeue(out int current))
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(current, d);
                    if (next < 0 || distances[next] >= 0 || !Traversable(world.Cells[next])
                        || country && world.Cells[next].Owner != world.Cells[center].Owner) continue;
                    distances[next] = distances[current] + 1; queue.Enqueue(next);
                }
            return distances;
        }
        long Weight(int center) => !weighted ? 12 : world.Cells[center].BuildingKind == "city"
            ? 16L + Math.Max(1, world.Cells[center].BuildingLevel) : 12;
        var scoreComparer = Comparer<(int Center, int Distance)>.Create((a, b) =>
        {
            int score = (a.Distance * Weight(b.Center)).CompareTo(b.Distance * Weight(a.Center));
            if (score != 0) return score;
            int weight = Weight(b.Center).CompareTo(Weight(a.Center));
            return weight != 0 ? weight : a.Center.CompareTo(b.Center);
        });
        var sameCountry = roots.ToDictionary(c => c, c => Distances(c, true));
        var land = roots.ToDictionary(c => c, c => Distances(c, false));
        var result = Enumerable.Range(0, world.Cells.Count).Select(i =>
        {
            if (!Traversable(world.Cells[i]) || roots.Length == 0) return -1;
            if (sameCountry.ContainsKey(i)) return i;
            var reachable = roots.Where(c => sameCountry[c][i] >= 0).ToArray();
            if (reachable.Length > 0) return reachable.OrderBy(c => (c, sameCountry[c][i]), scoreComparer).First();
            return roots.OrderBy(c => world.Cells[c].Owner == world.Cells[i].Owner ? 0 : 1)
                .ThenBy(c => land[c][i] >= 0 ? 0 : 1)
                .ThenBy(c => (c, land[c][i] >= 0 ? land[c][i] : map.Distance(i, c)), scoreComparer).First();
        }).ToArray();
        if (!weighted) return result;
        // Compute repairs independently with a complete constrained BFS from each root.
        var eligible = Enumerable.Range(0, result.Length).Select(i => roots.Any(c => sameCountry[c][i] >= 0)).ToArray();
        var connected = ConnectedCells(world, result);
        var repairs = Enumerable.Repeat(-1, result.Length).ToArray();
        var repairDistances = Enumerable.Repeat(int.MaxValue, result.Length).ToArray();
        foreach (int center in roots)
        {
            var distances = Enumerable.Repeat(-1, result.Length).ToArray();
            var queue = new Queue<int>(); queue.Enqueue(center); distances[center] = 0;
            while (queue.TryDequeue(out int cell))
                for (int d = 0; d < 6; d++)
                {
                    int next = map.Neighbor(cell, d);
                    if (next < 0 || distances[next] >= 0 || !eligible[next] || world.Cells[next].Owner != world.Cells[center].Owner
                        || connected[next] && result[next] != center) continue;
                    distances[next] = distances[cell] + 1; queue.Enqueue(next);
                }
            for (int i = 0; i < result.Length; i++)
            {
                if (!eligible[i] || connected[i] || distances[i] < 0) continue;
                if (distances[i] < repairDistances[i] || distances[i] == repairDistances[i]
                    && (Weight(center) > Weight(repairs[i]) || Weight(center) == Weight(repairs[i]) && center < repairs[i]))
                { repairs[i] = center; repairDistances[i] = distances[i]; }
            }
        }
        for (int i = 0; i < result.Length; i++) if (repairs[i] >= 0) result[i] = repairs[i];
        return result;
    }
}
