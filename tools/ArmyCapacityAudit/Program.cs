using System.Text.Json;
using Ew4.Client;

string root = Path.GetFullPath(args[0]);
byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
    ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
var factions = new List<Sample>();
foreach (var choice in content.Scenarios.Where(s => s.Id.StartsWith("conquest", StringComparison.Ordinal)))
{
    var battle = content.Battle(choice.Id);
    string country = battle.Factions.First(f => f.Camp != 4).CountryId;
    var world = content.Start(choice.Id, country).Session.World;
    foreach (var faction in world.Factions.Where(f => !f.Defeated && f.Camp != "ew4.camp.4"))
    {
        var buildings = world.Cells.Where(c => c.Owner == faction.Id && c.BuildingKind is "city" or "stable" or "industry")
            .Select(c => new Site(c.BuildingKind!, c.BuildingLevel)).ToArray();
        int units = world.Units.Count(u => u.Owner == faction.Id);
        factions.Add(new(choice.Id, faction.Name, units, buildings));
    }
}
int[] Quantiles(IEnumerable<int> values)
{
    var sorted = values.Order().ToArray();
    return sorted.Length == 0 ? [] : new[] { 0d, .25, .5, .75, .9, 1 }.Select(q => sorted[(int)Math.Round(q * (sorted.Length - 1))]).ToArray();
}
int Capacity(Sample faction, int basic, int[] city, int[] facility) => basic + faction.Buildings.Sum(b =>
    b.Level <= 0 ? 0 : (b.Kind == "city" ? city : facility)[Math.Min(b.Level - 1, (b.Kind == "city" ? city : facility).Length - 1)]);
var candidates = new[] {
    new Policy("linear2-base0", 0, [2,4,6,8,10,12,14], [2,4,6,8]),
    new Policy("moderate-base6", 6, [2,3,4,5,6,7,8], [1,2,3,4]),
    new Policy("moderate-base8", 8, [2,3,4,5,6,7,8], [1,2,3,4]),
    new Policy("generous-base8", 8, [3,4,5,6,7,8,9], [2,3,4,5]) };
var summaries = candidates.Select(p => new { p.Id, p.Base, p.City, p.Facility,
    atOrAbove = factions.Count(f => f.Units >= Capacity(f, p.Base, p.City, p.Facility)),
    above = factions.Count(f => f.Units > Capacity(f, p.Base, p.City, p.Facility)),
    headroom = Quantiles(factions.Select(f => Capacity(f, p.Base, p.City, p.Facility) - f.Units)),
    ratioPercent = Quantiles(factions.Where(f => f.Units > 0).Select(f => Capacity(f, p.Base, p.City, p.Facility) * 100 / f.Units)) });
Console.WriteLine(JsonSerializer.Serialize(new { scenarios = factions.Select(f => f.Scenario).Distinct().Count(), factions = factions.Count,
    quantileOrder = "min,p25,p50,p75,p90,max", units = Quantiles(factions.Select(f => f.Units)),
    buildings = Quantiles(factions.Select(f => f.Buildings.Length)),
    levels = factions.SelectMany(f => f.Buildings).GroupBy(b => new { b.Kind, b.Level }).Select(g => new { g.Key.Kind, g.Key.Level, Count = g.Count() }),
    candidates = summaries,
    scenarioTotals = factions.GroupBy(f => f.Scenario).Select(g => new { scenario = g.Key, factions = g.Count(),
        units = g.Sum(f => f.Units), buildings = g.Sum(f => f.Buildings.Length),
        moderateCapacity = g.Sum(f => Capacity(f, 8, [2,3,4,5,6,7,8], [1,2,3,4])) }),
    scales = new[] { 0,1,2,4,8,16,32 }.Select((min, i) => new { min, max = i == 6 ? int.MaxValue : new[] { 0,1,3,7,15,31 }[i],
        data = factions.Where(f => f.Buildings.Length >= min && (i == 6 || f.Buildings.Length <= new[] { 0,1,3,7,15,31 }[i])).ToArray() })
        .Where(g => g.data.Length > 0).Select(g => new { g.min, g.max, count = g.data.Length,
            armies = Quantiles(g.data.Select(f => f.Units)),
            moderateCapacity = Quantiles(g.data.Select(f => Capacity(f, 8, [2,3,4,5,6,7,8], [1,2,3,4]))) }),
    examples = factions.OrderByDescending(f => f.Units).Take(12).Concat(factions.Where(f => f.Buildings.Length is 1 or 2).OrderByDescending(f => f.Units).Take(8))
        .Select(f => new { f.Scenario, f.Name, f.Units, buildings = f.Buildings.Length,
            city = f.Buildings.Count(b => b.Kind == "city"), stable = f.Buildings.Count(b => b.Kind == "stable"), industry = f.Buildings.Count(b => b.Kind == "industry"),
            levelSum = f.Buildings.Sum(b => b.Level), moderateCapacity = Capacity(f, 8, [2,3,4,5,6,7,8], [1,2,3,4]) }) }));

record Site(string Kind, int Level);
record Sample(string Scenario, string Name, int Units, Site[] Buildings);
record Policy(string Id, int Base, int[] City, int[] Facility);
