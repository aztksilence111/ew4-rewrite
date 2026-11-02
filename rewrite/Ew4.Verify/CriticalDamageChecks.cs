using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class CriticalDamageChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Critical damage: " + label); }
        string output = Path.Combine(root, "build/critical-audit-20261102");
        using var native = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "native.json")));
        var profile = new UnitProfile { Id = "critical.probe", Category = "infantry", MaxHealth = 100,
            MinimumAttack = 2, MaximumAttack = 9, AttackBands = [new(5, 2), new(15, 3), new(30, 4), new(50, 5)],
            DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1 } };
        foreach (var vector in native.RootElement.GetProperty("vectors").EnumerateArray())
        {
            var attacker = new Combatant { Profile = profile, Health = 100, Skills = vector.GetProperty("offense").GetBoolean() ? [Ew4Combat.Skill(31)] : [] };
            var defender = new Combatant { Profile = profile, Health = 100, Skills = vector.GetProperty("defense").GetBoolean() ? [Ew4Combat.Skill(32)] : [] };
            var random = new Ew4Random(vector.GetProperty("seed").GetUInt64());
            var actual = Ew4Combat.Calculate(attacker, defender, 1, random);
            var expected = vector.GetProperty("expected");
            Check(actual.DefenderDamage == expected.GetProperty("defender_damage").GetInt32()
                && actual.AttackerDamage == expected.GetProperty("attacker_damage").GetInt32()
                && actual.OffensiveProc == expected.GetProperty("offensive_proc").GetBoolean()
                && actual.DefensiveProc == expected.GetProperty("defensive_proc").GetBoolean()
                && actual.Counterattack == expected.GetProperty("counterattack").GetBoolean()
                && actual.RandomState == expected.GetProperty("rng_state").GetUInt64() && random.State == actual.RandomState,
                "native damage, proc and RNG " + vector.GetRawText());
            if (actual.OffensiveProc && !actual.DefensiveProc) Check(actual.DefenderDamage == 45, "offensive proc uses maximum rolls");
            if (actual.DefensiveProc) Check(actual.DefenderDamage == 1, "defensive proc overrides damage");
        }
        Check(LegacySkillParameters.Default.Percent(31) == 10, "original offense probability 10 percent");
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.GetFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var start = content.Start("campaign1_02b", null);
        var shooter = start.Session.World.Units.Single(u => u.Cell == 2544);
        var map = new HexMap(start.Session.World.Width, start.Session.World.Height);
        var victim = start.Session.World.Units.First(u => u.Owner != shooter.Owner
            && start.Session.World.Factions.Single(f => f.Id == u.Owner).Camp != start.Session.World.Factions.Single(f => f.Id == shooter.Owner).Camp
            && map.Distance(shooter.Cell, u.Cell) >= shooter.Combatant.Profile.MinimumRange && map.Distance(shooter.Cell, u.Cell) <= shooter.Combatant.Profile.MaximumRange);
        string profileRoot = Path.Combine(output, "profile");
        Directory.CreateDirectory(Path.Combine(profileRoot, "saves/mode-1"));
        var fixtures = new List<object>();
        foreach (var (kind, slot) in new[] { ("normal", 1), ("critical", 2), ("blocked", 3) })
        {
            var world = start.Session.World with { Units = start.Session.World.Units.Select(u =>
                u.Id == shooter.Id ? u with { Combatant = u.Combatant with { Skills = u.Combatant.Skills.Append(Ew4Combat.Skill(31)).Distinct().ToArray() } }
                : u.Id == victim.Id && kind == "blocked" ? u with { Combatant = u.Combatant with { Skills = u.Combatant.Skills.Append(Ew4Combat.Skill(32)).Distinct().ToArray() } } : u).ToArray() };
            BattlePreview preview = null!;
            for (ulong seed = 1; seed < 100000; seed++)
            {
                world = world with { RandomState = seed };
                preview = WorldBattle.Preview(world, world.Revision, shooter.Id, victim.Id, start.Rules.Battle);
                if (kind == "normal" && !preview.Main.OffensiveProc || kind == "critical" && preview.Main.OffensiveProc && !preview.Main.DefensiveProc
                    || kind == "blocked" && preview.Main.OffensiveProc && preview.Main.DefensiveProc) break;
            }
            Check(kind == "normal" ? !preview.Main.OffensiveProc : preview.Main.OffensiveProc, kind + " seed found");
            string slotPath = Path.Combine(profileRoot, $"saves/mode-1/slot-{slot}.json");
            File.WriteAllBytes(slotPath, JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("campaign1_02b", null,
                SessionSave.Encode(start.Session with { World = world })), ClientGame.Json));
            var host = new ClientGame(content, Path.Combine(output, "backend-" + kind));
            Directory.CreateDirectory(Path.Combine(output, "backend-" + kind, "saves/mode-1"));
            File.Copy(slotPath, Path.Combine(output, "backend-" + kind, $"saves/mode-1/slot-{slot}.json"), true);
            JsonElement Call(string route, object command)
            {
                using var result = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
                Check(result.RootElement.GetProperty("ok").GetBoolean(), route + " succeeds");
                return result.RootElement.GetProperty("data").Clone();
            }
            var before = Call("load", new { slot, mode = 1 });
            var after = Call("action", new { op = "attack", revision = before.GetProperty("revision").GetInt64(), unit = shooter.Id, target = victim.Id });
            var combat = after.GetProperty("combat");
            bool critical = kind == "critical";
            Check(combat.GetProperty("critical").GetBoolean() == critical && !combat.GetProperty("counterCritical").GetBoolean(), kind + " client proc flags");
            Check(combat.GetProperty("damage").GetInt32() == preview.Main.DefenderDamage, kind + " authoritative damage");
            var victimAfter = after.GetProperty("units").EnumerateArray().FirstOrDefault(u => u.GetProperty("id").GetString() == victim.Id);
            int healthAfter = victimAfter.ValueKind == JsonValueKind.Undefined ? 0 : victimAfter.GetProperty("health").GetInt32();
            Check(healthAfter == Math.Max(0, victim.Combatant.Health - preview.Main.DefenderDamage), kind + " actual health commit");
            fixtures.Add(new { kind, slot, attacker = shooter.Id, defender = victim.Id, healthBefore = victim.Combatant.Health,
                healthAfter, damage = preview.Main.DefenderDamage, critical, seed = world.RandomState });
        }
        File.WriteAllText(Path.Combine(output, "fixtures.json"), JsonSerializer.Serialize(new { checks, errors = 0, fixtures }, ClientGame.Json));
        return checks;
    }
}
