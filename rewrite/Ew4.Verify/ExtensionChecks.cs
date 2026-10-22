using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ExtensionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { if (!pass) throw new Exception("Extension: " + label); checks++; }
        void Reject(Action action, string label)
        {
            try { action(); } catch (Exception e) when (e is RuleViolation or JsonException) { checks++; return; }
            throw new Exception("Extension accepted " + label);
        }
        var assets = Path.Combine(root, "rewrite/data/original");
        var overrides = new Dictionary<string, byte[]>();
        byte[] Read(string p) => overrides.TryGetValue(p, out var data) ? data : File.ReadAllBytes(p.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", p[5..]) : Path.Combine(root, "rewrite/data", p));
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        var builtin = new ClientContent(Read, ["conquest1.btl"]);
        var original = builtin.Start("conquest1", "fra");
        var originalWorld = original.Session.World;
        var source = originalWorld.Units.First(u => u.Owner == originalWorld.ActiveFaction && u.Combatant.Profile.Category != "fort");
        var commander = builtin.Catalogs.Runtime.Commanders.Values.First().Definition;
        Put("rewrite/extension.json", new { schema = 1, apiVersion = 1, skills = new[] { "rewrite/definitions/skills.json" },
            traits = new[] { "rewrite/definitions/traits.json" }, unitBindings = "rewrite/definitions/unit-bindings.json",
            commanderBindings = "rewrite/definitions/commander-bindings.json", victoryRules = "rewrite/definitions/victory-rules.json",
            presentation = "rewrite/presentation/unit-info.json", scripts = new[] { "rewrite/scripts/test.lua" } });
        var boost = new ExtensionSkill { Id = "test.damage", NameKey = "Damage", Effects = [new() { Value = 2 }] };
        var scripted = new ExtensionSkill { Id = "test.lua", NameKey = "Script", Effects = [new() { Script = "rewrite/scripts/test.lua", Function = "boost" }] };
        var healing = new ExtensionSkill { Id = "test.heal", NameKey = "Heal", Kind = "active", Cooldown = 2, EndsAction = false,
            Cost = new(3, 2, 1), Effects = [new() { Event = "skill.activate", Operation = "heal", Script = "rewrite/scripts/test.lua", Function = "heal" }] };
        var income = new ExtensionSkill { Id = "test.income", NameKey = "Income", Effects = [new() { Event = "turn.start", Operation = "money", Value = 7 }] };
        Put("rewrite/definitions/skills.json", new[] { boost, scripted, healing, income });
        Put("rewrite/definitions/traits.json", new[] { new ExtensionSkill { Id = "test.armored", Kind = "tag" } });
        var slots = new[] { new SkillSlot(healing.Id, "heal", 2), new SkillSlot(boost.Id, "boost"), new SkillSlot(scripted.Id, "script"), new SkillSlot(income.Id, "income") };
        Put("rewrite/definitions/unit-bindings.json", new[] { new ExtensionBinding(source.Combatant.Profile.Id, "append", slots, ["test.armored"]) });
        Put("rewrite/definitions/commander-bindings.json", new[] { new ExtensionBinding(commander.Id, "replace",
            Enumerable.Range(0, 7).Select(i => new SkillSlot(boost.Id, "commander." + i, i + 1, 6 - i, 0, i * 24)).ToArray()) });
        Put("rewrite/definitions/victory-rules.json", new Dictionary<string, ExtensionVictoryRules> { ["conquest1"] = new() {
            Win = new() { Type = "round_reached", Round = 3 }, Lose = new() { Type = "round_reached", Round = 4 } } });
        Put("rewrite/presentation/unit-info.json", new Dictionary<string, ExtensionLayout> { ["commander"] = new("positioned"), ["unit"] = new("grid", 2) });
        overrides["rewrite/scripts/test.lua"] = Encoding.UTF8.GetBytes("function boost(ctx) return 1.5 end\nfunction heal(ctx) return {value=9,state={uses=(ctx.state.uses or 0)+1}} end\nfunction spin(ctx) while true do end end\nfunction broken(ctx) error('test') end\nfunction random_value(ctx) return ctx:random(100)+1 end");
        var content = new ClientContent(Read, ["conquest1.btl"]); var started = content.Start("conquest1", "fra"); var world = started.Session.World;
        var unit = world.Units.Single(u => u.Id == source.Id);
        Check(content.Catalogs.Runtime.Commanders.Values.Single(c => c.Definition.Id == commander.Id).Definition.Skills.Count == 7, "unlimited commander skills");
        Check(unit.Combatant.Profile.SkillSlots.Count == 4 && unit.Combatant.Profile.HasTrait("test.armored"), "unit skill and trait binding");
        Check(world.ContentRevision != originalWorld.ContentRevision, "extension fingerprint binds save");
        Check(started.Rules.Ai.Movement.Extensions == content.Extensions.Bus, "AI uses shared extensions");
        var bus = content.Extensions.Bus;
        Check(bus.Modify("combat.modify_damage", "damage_multiplier", 10, unit.Combatant, unit.Combatant, new(1)) == 30, "declarative and Lua multipliers");
        var runtime = bus.Scripts!; var ctx = new RuleScriptContext("test", unit.Combatant, unit.Combatant, 1, 1, 1, new Dictionary<string, long>());
        var aRandom = new Ew4Random(42); var bRandom = new Ew4Random(42);
        Check(runtime.Run("rewrite/scripts/test.lua", "random_value", ctx, aRandom).Value == runtime.Run("rewrite/scripts/test.lua", "random_value", ctx, bRandom).Value
            && aRandom.State == bRandom.State, "deterministic script RNG");
        Reject(() => runtime.Run("rewrite/scripts/test.lua", "spin", ctx, new(1)), "infinite loop");
        Reject(() => runtime.Run("rewrite/scripts/test.lua", "broken", ctx, new(1)), "script exception");
        Reject(() => runtime.Run("rewrite/scripts/test.lua", "missing", ctx, new(1)), "missing function");
        world = world with { Units = world.Units.Select(u => u.Id == unit.Id ? u with { Acted = false, Combatant = u.Combatant with { Health = Math.Max(1, u.Combatant.Health - 15) } } : u).ToArray() };
        var before = world.Units.Single(u => u.Id == unit.Id);
        var funds = world.Factions.Single(f => f.Id == unit.Owner).Resources;
        var change = WorldSkillActions.Execute(world, world.Revision, unit.Id, "heal", null, bus, started.Rules.Movement);
        Check(change.State.Units.Single(u => u.Id == unit.Id).Combatant.Health == Math.Min(before.Combatant.Profile.MaxHealth, before.Combatant.Health + 9), "active skill heals");
        Check(change.Notices.Count(n => n.Kind == "unit.healed" && n.Subject == unit.Id && n.Value == 9) == 1,
            "actual extension healing emits one positive health delta");
        Check(!RuleEventBus.ChangeNotices(change.State, change.State).Any(n => n.Kind == "unit.healed"), "unchanged health emits no healing cue");
        Check(change.State.Factions.Single(f => f.Id == unit.Owner).Resources == new Resources(funds.Money - 3, funds.Industry - 2, funds.Food - 1), "active skill exact cost");
        Check(change.State.ExtensionState[unit.Id + "/heal/uses"] == 1 && change.State.ExtensionState[unit.Id + "/heal/ready"] == world.Round + 2, "instance state and cooldown");
        Reject(() => WorldSkillActions.Execute(change.State, change.State.Revision, unit.Id, "heal", null, bus, started.Rules.Movement), "cooldown repeated action");
        Reject(() => WorldSkillActions.Execute(world, world.Revision + 1, unit.Id, "heal", null, bus, started.Rules.Movement), "stale revision");
        var saved = WorldSave.Decode(WorldSave.Encode(change.State), change.State.ContentRevision);
        Check(saved.ExtensionState[unit.Id + "/heal/uses"] == 1 && saved.Units.Single(u => u.Id == unit.Id).Combatant.Profile.SkillSlots.Count == 4, "save restores skills and state");
        Reject(() => WorldSave.Decode(WorldSave.Encode(change.State), originalWorld.ContentRevision), "save incompatible content");
        var faulty = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [healing.Id] = healing with { Effects = [new() {
            Event = "skill.activate", Operation = "heal", Script = "rewrite/scripts/test.lua", Function = "broken" }] } }, runtime);
        Reject(() => WorldSkillActions.Execute(world, world.Revision, unit.Id, "heal", null, faulty, started.Rules.Movement), "failed active transaction");
        Check(world.Factions.Single(f => f.Id == unit.Owner).Resources == funds && !world.ExtensionState.ContainsKey(unit.Id + "/heal/uses"), "failed script leaves original immutable");
        var camp = world.Factions.Single(f => f.Id == unit.Owner).Camp;
        Check(WorldVictory.Evaluate(world with { Round = 3 }, camp, started.Rules.Turns.Victory, started.Rules.Turns.NeutralCamps) == BattleOutcome.Victory, "custom conquest victory");
        Check(WorldVictory.Evaluate(world with { Round = 4 }, camp, started.Rules.Turns.Victory, started.Rules.Turns.NeutralCamps) == BattleOutcome.Defeat, "simultaneous defeat priority");
        var battle = content.Battle("conquest1"); var v = new ExtensionVictoryRules { Win = new() { Type = "round_reached", Round = 8 } };
        byte[] btl = battle.WithVictory(v); var parsed = LegacyBattle.Read(btl);
        Check(parsed.Victory!.Win.Round == 8 && parsed.ToLegacyBytes().SequenceEqual(battle.ToLegacyBytes()), "VICT body preserved");
        Check(parsed.WithVictory(null).SequenceEqual(battle.ToBytes()), "VICT strip");
        overrides["original/conquest1.btl"] = btl;
        Check(new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra").Rules.Turns.Victory.Custom!.Win.Round == 8, "BTL priority above mod victory");
        overrides.Remove("original/conquest1.btl");
        var p = unit.Combatant.Profile with { MinimumAttack = 20, MaximumAttack = 20, MinimumRange = 1, MaximumRange = 1, AttackBands = [new(0, 1)] };
        var attack = new Combatant { Profile = p, Health = p.MaxHealth };
        var target = attack with { Profile = p with { SkillSlots = [] }, Health = 1 };
        var lethal = Ew4Combat.Calculate(attack, target, 1, new(5), new() { Extensions = bus, LethalStrikePreventsCounterattack = true });
        Check(lethal.DefenderDamage >= 1 && !lethal.Counterattack && lethal.AttackerDamage == 0, "extension damage precedes lethal counter check");
        var originalManifest = overrides["rewrite/extension.json"];
        Put("rewrite/extension.json", new { schema = 1, apiVersion = 99 }); Reject(() => ExtensionLoader.Load(Read), "unknown API");
        overrides["rewrite/extension.json"] = originalManifest;
        var originalSkills = overrides["rewrite/definitions/skills.json"];
        Put("rewrite/definitions/skills.json", new[] { boost with { Effects = [new() { Event = "invented", Operation = "heal" }] } });
        Reject(() => ExtensionLoader.Load(Read), "unknown event"); overrides["rewrite/definitions/skills.json"] = originalSkills;
        var originalBindings = overrides["rewrite/definitions/unit-bindings.json"];
        Put("rewrite/definitions/unit-bindings.json", new[] { new ExtensionBinding(source.Combatant.Profile.Id, "append", [new("missing.skill", "missing")]) });
        Reject(() => ExtensionLoader.Load(Read), "unknown skill"); overrides["rewrite/definitions/unit-bindings.json"] = originalBindings;
        var nativeId = Ew4Combat.Skill(21);
        var overriddenCommander = commander with { Skills = [nativeId], SkillSlots = [], LegacySkillOverrides = [nativeId] };
        var attached = Ew4Commander.Attach(attack, overriddenCommander);
        Check(!attached.Skills.Contains(nativeId) && !attached.Has(nativeId) && RuleEventBus.Slots(attached).Any(s => s.Id == nativeId),
            "native override removed from raw experience skills but retained for extension dispatch");
        Check(Ew4Experience.CommanderGain(100, false, attached.Skills) == 100, "overridden native experience multiplier disabled");
        Reject(() => ExtensionLoader.ValidateVictory(new() { Win = null! }), "null victory root");
        Reject(() => ExtensionLoader.ValidateVictory(new() { Win = new() { Conditions = null! } }), "null condition array");
        Reject(() => ExtensionLoader.ValidateVictory(new() { Win = new() { Type = "all", Conditions = [null!] } }), "null nested condition");
        Reject(() => ExtensionLoader.ValidateVictory(new() { Win = new() { Type = "resources", Resources = new(-1, 0, 0) } }), "negative resource threshold");
        Reject(() => ExtensionLoader.ParseJson<ExtensionVictoryRules>(Encoding.UTF8.GetBytes("{\"win\":{},\"Win\":{}}")), "duplicate victory key");
        Reject(() => ExtensionLoader.ParseJson<ExtensionVictoryRules>(Encoding.UTF8.GetBytes("{\"imaginary\":true}")), "unknown victory member");
        var deep = new VictoryCondition();
        for (int i = 0; i < 14; i++) deep = new() { Type = "all", Conditions = [deep] };
        Reject(() => ExtensionLoader.ValidateVictory(new() { Win = deep }), "deep victory tree");
        var btlScript = new ExtensionVictoryRules { Win = new() { Type = "script", Script = "rewrite/scripts/test.lua", Function = "missing" } };
        overrides["original/conquest1.btl"] = battle.WithVictory(btlScript);
        Reject(() => new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra"), "BTL-only missing handler rejected before gameplay");
        overrides["rewrite/scripts/test.lua"] = Encoding.UTF8.GetBytes("function boost(ctx) return 1.5 end\nfunction heal(ctx) return {value=9,state={uses=(ctx.state.uses or 0)+1}} end\nfunction objective(ctx) local f=ctx:faction('" + world.ActiveFaction + "'); local c=ctx:cell(" + unit.Cell + "); local u=ctx:unit('" + unit.Id + "'); return ctx.playerCamp==f.camp and c.active and u.owner==f.id end");
        overrides["original/conquest1.btl"] = battle.WithVictory(btlScript with { Win = btlScript.Win with { Function = "objective" } });
        var instant = new ClientContent(Read, ["conquest1.btl"]).Start("conquest1", "fra");
        Check(instant.Session.World.Outcome == BattleOutcome.Victory && instant.Session.World.Phase == TurnPhase.Finished,
            "BTL Lua victory reads cell unit faction snapshots and evaluates at start");
        overrides.Remove("original/conquest1.btl");
        var disabledManifest = overrides["rewrite/extension.json"];
        overrides.Remove("rewrite/extension.json");
        Reject(() => builtin.Extensions.ValidateVictoryScripts(btlScript), "BTL script without enabled extension");
        overrides["rewrite/extension.json"] = disabledManifest;
        var validSkills = overrides["rewrite/definitions/skills.json"];
        overrides["rewrite/definitions/skills.json"] = Encoding.UTF8.GetBytes("[null]");
        Reject(() => ExtensionLoader.Load(Read), "null skill");
        overrides["rewrite/definitions/skills.json"] = Encoding.UTF8.GetBytes("[{\"id\":\"test.bad\",\"effects\":null}]");
        Reject(() => ExtensionLoader.Load(Read), "null effects");
        overrides["rewrite/definitions/skills.json"] = validSkills;
        var damageSkill = healing with { Target = "enemy", Range = 1000, Effects = [new() { Event = "skill.activate", Operation = "damage", Value = 100000 }] };
        var damageBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [healing.Id] = damageSkill });
        var victim = world.Units.First(u => world.Factions.Single(f => f.Id == u.Owner).Camp != camp);
        var destroyed = WorldSkillActions.Execute(world, world.Revision, unit.Id, "heal", victim.Id, damageBus, started.Rules.Movement);
        Check(destroyed.State.Units.All(u => u.Id != victim.Id) && destroyed.Notices.Any(n => n.Kind == "unit.destroyed" && n.Subject == victim.Id)
            && destroyed.Notices.Any(n => n.Kind == "battle.damage" && n.Subject == victim.Id), "active damage shares backend death and damage notices");
        Reject(() => WorldSkillActions.Execute(world, world.Revision, unit.Id, "heal", unit.Id, damageBus, started.Rules.Movement), "enemy skill refuses own target");
        var report = Path.Combine(root, "rewrite/reports/extensions-20261020"); Directory.CreateDirectory(report);
        foreach (var pair in overrides.Where(p => p.Key.StartsWith("rewrite/", StringComparison.Ordinal)))
        {
            string file = Path.Combine(report, "example", pair.Key); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllBytes(file, pair.Value);
        }
        File.WriteAllText(Path.Combine(report, "results.json"), JsonSerializer.Serialize(new { checks, errors = 0, profile = source.Combatant.Profile.Id, commander = commander.Id }));
        return checks;
    }
}
