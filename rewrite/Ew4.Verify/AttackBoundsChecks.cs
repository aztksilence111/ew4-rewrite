using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class AttackBoundsChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Attack display: " + label); }
        var profile = new UnitProfile { Id = "attack.display", Category = "infantry", MinimumAttack = 4, MaximumAttack = 6 };
        var plain = new Combatant { Profile = profile };
        foreach (var (category, lower, upper) in new[] { ("infantry", 13, 12), ("cavalry", 11, 10), ("artillery", 7, 8) })
        {
            var unit = plain with { Profile = profile with { Category = category } };
            Combatant Attach(params int[] ids) => Ew4Commander.Attach(unit, new Commander { Skills = ids.Select(Ew4Combat.Skill).ToArray() });
            Check(Ew4Combat.AttackBounds(unit) == (4, 6), category + " without commander");
            Check(Ew4Combat.AttackBounds(Attach(lower)) == (5, 6), category + " lower only");
            Check(Ew4Combat.AttackBounds(Attach(upper)) == (4, 7), category + " upper only");
            var both = Attach(lower, upper);
            Check(Ew4Combat.AttackBounds(both) == (5, 7), category + " both commander skills");
            Check(Ew4Combat.AttackBounds(Attach(7, 8, 10, 11, 12, 13)) == (5, 7), category + " ignores other categories");
            var tuned = LegacySkillParameters.FromOverrides(new Dictionary<string, Dictionary<string, double>>
            {
                [Ew4Combat.Skill(lower)] = new() { ["bonus"] = 4 },
                [Ew4Combat.Skill(upper)] = new() { ["bonus"] = 3 }
            });
            Check(Ew4Combat.AttackBounds(both, tuned) == (8, 9), category + " mod parameters");
            Check(Ew4Combat.AttackBounds(Attach(lower), tuned) == (8, 8), category + " lower exceeds upper matches rolls");
            Check(Ew4Combat.AttackBounds(both with { Profile = both.Profile with { LegacySkillOverrides = [Ew4Combat.Skill(lower)] } }) == (4, 7), category + " overridden legacy skill");
        }
        Check(Ew4Combat.AttackBounds(plain with { Profile = profile with { Category = "naval" }, Skills = [Ew4Combat.Skill(12), Ew4Combat.Skill(13)] }) == (4, 6), "naval ignores infantry skills");
        Check(Ew4Combat.AttackBounds(plain with { Stars = 12, AttackBonus = 20, Morale = 1 }) == (4, 6), "stars equipment morale do not change base roll bounds");

        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var initial = content.Start("conquest1", "fra");
        string folder = Path.Combine(root, "build/attack-bounds-check-20261029/profile");
        var session = initial.Session;
        Directory.CreateDirectory(Path.Combine(folder, "saves/mode-2"));
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "saves/mode-2/slot-0.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        var host = new ClientGame(content, folder);
        using var response = JsonDocument.Parse(host.Dispatch("/api/load", "{\"mode\":2}"));
        Check(response.RootElement.GetProperty("ok").GetBoolean(), "load saved battle");
        var view = response.RootElement.GetProperty("data");
        var originals = session.World.Units.ToDictionary(u => u.Id);
        int boosted = 0;
        foreach (var rendered in view.GetProperty("units").EnumerateArray())
        {
            var unit = originals[rendered.GetProperty("id").GetString()!];
            var expected = Ew4Combat.AttackBounds(unit.Combatant, content.Catalogs.Combat.SkillParameters);
            var attack = rendered.GetProperty("attack");
            Check(attack.GetProperty("minimum").GetInt32() == expected.Minimum && attack.GetProperty("maximum").GetInt32() == expected.Maximum, "saved unit " + unit.Id);
            if (expected.Minimum != unit.Combatant.Profile.MinimumAttack || expected.Maximum != unit.Combatant.Profile.MaximumAttack) boosted++;
        }
        Check(boosted > 0, "scenario commanders expose boosted bounds");
        return checks;
    }
}
