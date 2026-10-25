using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;
using Ew4.Legacy;

internal static class BattleChainChecks
{
    public static int Run(string root, bool importedCatalog = false)
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException(label);
        }
        string[] categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
        string[] terrain = ["open", "medium", "dense"];
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports", importedCatalog ? "catalog_battle_vectors.json" : "battle_chain_vectors.json")));
        var runtime = importedCatalog ? LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json"))) : null;
        string assets = Path.Combine(root, "apk_work/original/assets");
        var actualCatalog = !importedCatalog ? null : LegacyCombatCatalog.Read(File.ReadAllText(Path.Combine(root, "reverse/reports/runtime_rule_tables.json")),
            File.ReadAllText(Path.Combine(assets, "def_terraintype.xml")), File.ReadAllText(Path.Combine(assets, "def_installation.xml")),
            LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml"))));
        ProgressionRules Track(string key) => new() { Levels = data.RootElement.GetProperty(key).EnumerateArray()
            .Select((v, i) => new ProgressionLevel(i.ToString(), v.GetInt64())).Append(new("cap", null)).ToArray() };
        var rankTrack = Track("rank_costs");
        var titleTrack = Track("title_costs");
        int[] bonuses = data.RootElement.GetProperty("rank_bonuses").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        int[] titleHealing = data.RootElement.GetProperty("title_healing").EnumerateArray().Select(v => v.GetInt32()).ToArray();
        var fixture = WorldChecks.Example();
        var profile = fixture.Units[0].Combatant.Profile;
        UnitProfile Profile(JsonElement f, int index) => importedCatalog ? runtime!.Profiles[f.GetProperty("profile_id").GetString()!].Profile with
            { MaxHealth = f.GetProperty("maximum").GetInt32() } : profile with
            { Id = "unit.type." + index, MaxHealth = f.GetProperty("maximum").GetInt32(), Category = categories[f.GetProperty("category").GetInt32()],
                KillExperience = f.GetProperty("grade").GetInt32() + 1, IgnitionNumerator = f.GetProperty("weapon").GetInt32() switch { 3 => 1, 4 => 5, _ => 0 },
                IgnitionDenominator = f.GetProperty("weapon").GetInt32() == 4 ? 2 : 1 };
        int sample = 0;
        foreach (var vector in data.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var facts = vector.GetProperty("units").EnumerateArray().ToArray();
            var stages = vector.GetProperty("stages").EnumerateArray().ToArray();
            var track = new ProgressionRules
            {
                Levels = stages.Select((s, i) => new ProgressionLevel(i.ToString(), i == stages.Length - 1 ? null : stages[i + 1].GetProperty("cost").GetInt64(), s.GetProperty("heal").GetInt32())).ToArray(),
                SingleAdvancePerAward = true, AccumulateAtCap = true, DiscardExperienceOnReachingCap = false
            };
            var units = facts.Select((f, i) => new UnitState
            {
                Id = "unit." + i, Owner = i == 0 ? "winner" : "loser", Cell = i, RemainingMovement = importedCatalog ? f.GetProperty("movement").GetInt32() : 6,
                Veterancy = f.GetProperty("level").GetInt32().ToString(), Experience = f.GetProperty("experience").GetInt64(),
                Transport = f.GetProperty("transport").GetBoolean(), Burning = f.GetProperty("burning").GetInt32(),
                CommanderId = f.GetProperty("commander").ValueKind == JsonValueKind.Null ? null : "commander." + i,
                Support = new() { TitleHealing = f.GetProperty("commander").ValueKind == JsonValueKind.Null ? 0 : titleHealing[f.GetProperty("commander").GetProperty("title").GetInt32()] },
                Morale = new(f.GetProperty("morale").GetInt32(), 0, f.GetProperty("morale").GetInt32()),
                Combatant = new() { Health = f.GetProperty("health").GetInt32(), Morale = f.GetProperty("morale").GetInt32(),
                    Skills = f.GetProperty("commander").ValueKind == JsonValueKind.Null ? [] : f.GetProperty("commander").GetProperty("skills").EnumerateArray().Select(v => Ew4Combat.Skill(v.GetInt32())).ToArray(),
                    Profile = Profile(f, i) }
            }).ToArray();
            var commanders = facts.Select((f, i) => (f, i)).Where(p => p.f.GetProperty("commander").ValueKind != JsonValueKind.Null).Select(p =>
            {
                var c = p.f.GetProperty("commander");
                return new CommanderRuntime { Id = "commander." + p.i, Owner = units[p.i].Owner, Rank = new(c.GetProperty("rank").GetInt32(), c.GetProperty("rank_xp").GetInt64()),
                    Title = new(c.GetProperty("title").GetInt32(), c.GetProperty("title_xp").GetInt64()), RankHealthBonus = bonuses[c.GetProperty("rank").GetInt32()] };
            }).ToArray();
            var state = fixture with { Width = 3, Height = 1, Units = units, Commanders = commanders, Cells = facts.Select((f, i) => new CellState
                { Owner = units[i].Owner, Terrain = new() { Sea = f.GetProperty("sea").GetBoolean() },
                    BuildingKind = f.GetProperty("building").GetBoolean() ? "city" : null, FireTerrain = terrain[f.GetProperty("terrain").GetInt32()],
                    Environment = f.GetProperty("environment").GetInt32() }).ToArray(), RandomState = vector.GetProperty("seed").GetUInt64() };
            var rules = new WorldBattleRules { CheckFactionSurvival = false, RecalculateLocalMorale = false, UnitProgression = track,
                RankProgression = rankTrack, TitleProgression = titleTrack,
                RankHealthBonuses = bonuses.Select((value, i) => (value, id: rankTrack.Levels[i].Id)).ToDictionary(p => p.id, p => p.value),
                TitleHealing = titleHealing.Select((value, i) => (value, id: titleTrack.Levels[i].Id)).ToDictionary(p => p.id, p => p.value) };
            if (actualCatalog != null) rules = actualCatalog.BattleRules() with { CheckFactionSurvival = false, RecalculateLocalMorale = false };
            int splash = vector.GetProperty("splash").GetInt32();
            var actual = WorldBattle.CommitResolved(state, new("unit.0", "unit.1", vector.GetProperty("taken").GetInt32(), vector.GetProperty("dealt").GetInt32(),
                vector.GetProperty("counter").GetBoolean(), state.RandomState, splash > 0 ? "unit.2" : null, splash), rules).State;
            var expected = vector.GetProperty("expected");
            var wanted = expected.GetProperty("units").EnumerateArray().ToArray();
            for (int i = 0; i < wanted.Length; i++)
            {
                var unit = actual.Units.SingleOrDefault(u => u.Id == "unit." + i);
                var target = wanted[i];
                Check((unit?.Combatant.Health ?? 0) == target.GetProperty("health").GetInt32() && (unit != null) == target.GetProperty("occupied").GetBoolean(), "native combined HP/death " + sample);
                Check(actual.Cells[i].Environment == target.GetProperty("environment").GetInt32()
                    && (actual.Cells[i].Owner != null) == target.GetProperty("owner").GetBoolean(), "native combined environment/sea ownership " + sample);
                if (target.GetProperty("commander").ValueKind != JsonValueKind.Null)
                {
                    var commander = actual.Commanders.Single(c => c.Id == "commander." + i);
                    var expectedCommander = target.GetProperty("commander");
                    Check(commander.Rank == new ProgressionState(expectedCommander[0].GetInt32(), expectedCommander[2].GetInt64())
                        && commander.Title == new ProgressionState(expectedCommander[1].GetInt32(), expectedCommander[3].GetInt64()), "native combined commander XP " + sample);
                }
                if (unit == null) continue;
                var morale = target.GetProperty("morale");
                Check(unit.Veterancy == target.GetProperty("level").GetInt32().ToString() && unit.Experience == target.GetProperty("experience").GetInt64()
                    && unit.Combatant.Profile.MaxHealth == target.GetProperty("maximum").GetInt32()
                    && unit.Burning == target.GetProperty("burning").GetInt32(), "native combined XP/advance/ignition " + sample);
                Check(unit.Morale == new MoraleState(morale[0].GetInt32(), morale[1].GetInt32(), morale[2].GetInt32()) && unit.RemainingMovement == target.GetProperty("movement").GetInt32()
                    && unit.Acted == target.GetProperty("acted").GetBoolean(), "native combined action/morale " + sample);
            }
            Check(actual.RandomState == expected.GetProperty("random_state").GetUInt64(), "native combined random state " + sample);
            Check(WorldSave.Encode(actual).SequenceEqual(WorldSave.Encode(WorldSave.Decode(WorldSave.Encode(actual), "fixture.1"))), "combined battle save roundtrip");
            sample++;
        }
        return checks;
    }
}
