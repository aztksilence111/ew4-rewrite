using System.Buffers.Binary;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

string root = Path.GetFullPath(args[0]);
string assets = Path.Combine(root, "rewrite/data/original");
var technology = LegacyWarzoneTechnology.Read(File.ReadAllText(Path.Combine(assets, "def_warzonetech.xml")));
int selections = 0, checkedFactions = 0;
var ranges = new Dictionary<string, float[]>();
for (int scenario = 1; scenario <= 6; scenario++)
{
    string name = "conquest" + scenario;
    var battle = LegacyBattle.Read(File.ReadAllBytes(Path.Combine(assets, name + ".btl")));
    ranges[name] = [battle.Factions.Min(f => f.AiEvasionBonus), battle.Factions.Max(f => f.AiEvasionBonus)];
    foreach (var chosen in battle.Factions)
    {
        var initialized = LegacyScenarioInitialization.Prepare(battle, 2, chosen.CountryId, technology);
        for (int index = 0; index < battle.Factions.Count; index++)
        {
            var source = battle.Factions[index];
            var actual = initialized.Records[index];
            float expected = source.Camp == chosen.Camp ? 0 : source.AiEvasionBonus;
            if (actual.AiEvasionBonus != expected || actual.Ai != (index != initialized.PlayerIndex))
                throw new InvalidOperationException(name + ": unexpected initialized faction bonus/control");
            checkedFactions++;
        }
        selections++;
    }
}

int modChecks = 0;
void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException("Mod AI defense: " + label);
    modChecks++;
}
byte[] original = File.ReadAllBytes(Path.Combine(assets, "conquest1.btl"));
var sourceBattle = LegacyBattle.Read(original);
int targetIndex = sourceBattle.Factions.ToList().FindIndex(f => f.CountryId == "gbr");
int factionOffset = sourceBattle.Sections.Single(s => s.Name == "factions").Offset;
int fieldOffset = factionOffset + targetIndex * 180 + 60;
foreach (float bonus in new[] { 0f, .25f, 1f })
{
    byte[] modified = (byte[])original.Clone();
    BinaryPrimitives.WriteSingleLittleEndian(modified.AsSpan(fieldOffset, 4), bonus);
    Check(modified.AsSpan(0, fieldOffset).SequenceEqual(original.AsSpan(0, fieldOffset))
        && modified.AsSpan(fieldOffset + 4).SequenceEqual(original.AsSpan(fieldOffset + 4)), "only author-selected field changes");
    byte[] Read(string path) => path == "original/conquest1.btl" ? modified
        : File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
    var content = new ClientContent(Read, ["conquest1.btl"]);
    var started = content.Start("conquest1", "fra");
    string targetId = LegacyScenarioInitialization.FactionId("conquest1", targetIndex, sourceBattle.Factions[targetIndex]);
    var imported = started.Session.World.Factions.Single(f => f.Id == targetId);
    Check(!imported.Human && imported.AiEvasionBonus == bonus, "BTL override reaches running enemy faction");
    var saved = WorldSave.Decode(WorldSave.Encode(started.Session.World), started.Session.World.ContentRevision);
    Check(saved.Factions.Single(f => f.Id == targetId).AiEvasionBonus == bonus, "override survives save/load");
    var selected = content.Start("conquest1", "gbr");
    Check(selected.Session.World.Factions.Single(f => f.Id == targetId) is { Human: true, AiEvasionBonus: 0 },
        "selecting modified country retains original player normalization");

    var fixture = WorldChecks.Example();
    var attacker = fixture.Units[1] with { Cell = 4, Combatant = fixture.Units[1].Combatant with
        { Profile = fixture.Units[1].Combatant.Profile with { MinimumAttack = 4, MaximumAttack = 4 } } };
    var defender = fixture.Units[0] with { Cell = 5 };
    var scene = fixture with { Units = [attacker, defender], Factions = fixture.Factions.Select(f => f.Id == defender.Owner
        ? imported with { Id = f.Id, Camp = f.Camp } : f).ToArray() };
    var preview = WorldBattle.Preview(scene, scene.Revision, attacker.Id, defender.Id, new());
    Check(preview.Main.DefenderDamage == Math.Max(1, (int)(20 * (1 - bonus))), "public combat applies imported AI reduction");
    var from = new MovementCell();
    var mountain = new MovementCell { TerrainCost = 6 };
    Check(Movement.EntryCost(from, mountain, 0, new() { Category = "infantry", FullBudget = 6, Remaining = 6 }, new()) == 3
        && Movement.EntryCost(from, mountain, 0, new() { Category = "infantry", Geography = true,
            FullBudget = 6, Remaining = 6 }, new()) == 3, "infantry keeps basic land cost with or without geography");
}
Check(File.ReadAllBytes(Path.Combine(assets, "conquest1.btl")).SequenceEqual(original), "installed source remains unchanged");

Console.WriteLine(JsonSerializer.Serialize(new
{
    movementAndOtherContracts = Contracts.Run(root),
    defenseContextChecks = CombatContextChecks.Run(root),
    conquestSelections = selections,
    checkedFactions,
    modChecks,
    rawConquestBonusRanges = ranges,
    errors = 0
}));
