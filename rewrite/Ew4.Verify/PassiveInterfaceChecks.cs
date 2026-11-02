using System.Text;
using System.Text.Json;
using Ew4.Core;
using Ew4.Client;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class PassiveInterfaceChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Passive interfaces: " + label); }
        void Reject(Action action, string label)
        {
            try { action(); } catch (RuleViolation) { checks++; return; }
            throw new Exception("Passive interfaces accepted " + label);
        }
        string example = Path.Combine(root, "rewrite/mod-kit/examples/passive-auras");
        var files = Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(example, p).Replace('\\', '/'), File.ReadAllBytes);
        var loaded = ExtensionLoader.Load(p => files.TryGetValue(p, out var data) ? data : throw new FileNotFoundException(p));
        var bus = loaded.Bus;
        Check(loaded.ApiVersion == 3 && bus.Skills.Count == 4 && bus.Statuses.Count == 1, "author example imports");
        Check(bus.Scripts == null, "common conditions need no Lua runtime");
        byte[] FullRead(string path) => files.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path.StartsWith("data/")
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(FullRead, ["conquest1.btl"]);
        var actual = content.Start("conquest1", "fra");
        var bound = actual.Session.World.Units.Where(u => u.Combatant.Profile.Id == "ew4.unit.fra.heavy_artillery.1").ToArray();
        Check(bound.Length > 0 && bound.All(u => u.Combatant.MaximumRange == u.Combatant.Profile.MaximumRange + 1
            && u.Combatant.MaximumHealth >= u.Combatant.Profile.MaxHealth + 100 && u.Combatant.FoodExempt), "real scenario binds passive attributes");
        Check(bound.Where(u => u.Owner == actual.Session.World.ActiveFaction).All(u => u.Combatant.Statuses.Count > 0), "opening active faction receives turn-start aura");
        var fixture = WorldChecks.Example();
        var actor = fixture.Units[1] with { Cell = 24, Support = new() { FoodConsumption = 10 },
            Combatant = fixture.Units[1].Combatant with { Health = 70, SkillSlots = [new("example.aura", "aura")] } };
        var ally = actor with { Id = "ally", Cell = 25, Combatant = actor.Combatant with { SkillSlots = [] } };
        var enemy = fixture.Units[0] with { Cell = 23 };
        var distant = ally with { Id = "distant", Cell = 48 };
        var world = fixture with { Width = 7, Height = 7, Units = [actor, ally, enemy, distant],
            Cells = Enumerable.Range(0, 49).Select(i => new CellState { Owner = i == 23 ? enemy.Owner : actor.Owner }).ToArray() };
        world.Validate();
        UnitState Unit(WorldState w, string id) => w.Units.Single(u => u.Id == id);
        var aura = bus.Dispatch(world, "turn.start", actor.Id, new(world.RandomState));
        Check(Unit(aura, actor.Id).Combatant.Statuses.Count == 1 && Unit(aura, ally.Id).Combatant.Statuses.Count == 1, "aura includes source and adjacent ally");
        Check(Unit(aura, enemy.Id).Combatant.Statuses.Count == 0 && Unit(aura, distant.Id).Combatant.Statuses.Count == 0, "aura excludes hostile and distant units");
        var infantryOnly = bus.Skills["example.aura"] with { Effects = [bus.Skills["example.aura"].Effects[0] with {
            When = new() { TargetCategories = ["infantry"], MinDistance = 1 } }] };
        var filteredBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [infantryOnly.Id] = infantryOnly }, statuses: bus.Statuses);
        var filtered = filteredBus.Dispatch(world, "turn.start", actor.Id, new(world.RandomState));
        Check(Unit(filtered, actor.Id).Combatant.Statuses.Count == 0 && Unit(filtered, ally.Id).Combatant.Statuses.Count == 1, "category and distance conditions filter aura targets directly");
        var noInfantry = world with { Units = world.Units.Select(u => u.Id == ally.Id ? u with { Combatant = u.Combatant with {
            Profile = u.Combatant.Profile with { Category = "cavalry" } } } : u).ToArray() };
        Check(Unit(filteredBus.Dispatch(noInfantry, "turn.start", actor.Id, new(world.RandomState)), ally.Id).Combatant.Statuses.Count == 0, "expert aura excludes another category");
        Check(Unit(aura, ally.Id).Combatant is { Health: 170, MaximumHealth: 200 }, "HP aura preserves missing health");
        var repeated = bus.Dispatch(aura, "turn.start", actor.Id, new(aura.RandomState));
        Check(Unit(repeated, ally.Id).Combatant is { Health: 170, MaximumHealth: 200 }, "refresh cannot accumulate HP");
        var expired = bus.ExpireStatuses(bus.ExpireStatuses(repeated, actor.Owner), actor.Owner);
        Check(Unit(expired, ally.Id).Combatant is { Health: 70, MaximumHealth: 100 } && Unit(expired, ally.Id).Combatant.AttributeModifiers == null, "expiration restores base without changing profile");
        var loadedWorld = WorldSave.Decode(WorldSave.Encode(aura), world.ContentRevision);
        Check(WorldSave.Encode(loadedWorld).SequenceEqual(WorldSave.Encode(aura)), "aura exact save roundtrip");
        Check(bus.RefreshAttributes(Unit(loadedWorld, ally.Id).Combatant).Health == 170, "reload refresh does not duplicate HP");
        var transition = WorldTurns.Resume(world with { Phase = TurnPhase.StartFaction }, world.Revision, new() { Extensions = bus }).State;
        Check(Unit(transition, ally.Id).Combatant.Statuses.Count == 1, "real faction turn dispatches aura");

        var capacity = bus.RefreshAttributes(actor.Combatant with { SkillSlots = [new("example.capacity", "capacity")] });
        Check(capacity is { MaximumHealth: 200, MaximumRange: 2, FoodExempt: true, IgnoreTerrain: true, Health: 170 }, "passive attributes resolve");
        Check(capacity.Profile.MaxHealth == 100 && capacity.Profile.MaximumRange == 1, "profile remains immutable base");
        capacity.Validate();
        var durable = world with { Units = world.Units.Select(u => u.Id == actor.Id ? u with { Combatant = capacity } : u).ToArray() };
        var reloaded = WorldSave.Decode(WorldSave.Encode(durable), world.ContentRevision);
        Check(Unit(reloaded, actor.Id).Combatant.AttributeModifiers == capacity.AttributeModifiers
            && Unit(reloaded, actor.Id).Combatant.Health == capacity.Health, "attributes survive save without source profile mutation");
        var reverted = bus.RefreshAttributes(capacity with { SkillSlots = [] });
        Check(reverted is { Health: 70, MaximumHealth: 100, MaximumRange: 1, FoodExempt: false, IgnoreTerrain: false }, "skill removal restores all attributes");

        var cavalry = capacity with { Profile = capacity.Profile with { Category = "cavalry" } };
        var moving = durable with { Units = durable.Units.Select(u => u.Id == actor.Id ? u with { Combatant = cavalry, RemainingMovement = 6 } : u).ToArray(),
            Cells = durable.Cells.Select((c, i) => i == 17 ? c with { Terrain = c.Terrain with { TerrainCost = 5 } } : c).ToArray() };
        var reach = WorldActions.Reachable(moving, actor.Id, new());
        Check(reach[17].Remaining == 3, "terrain immunity reaches real movement search");
        var pass = moving with { Cells = moving.Cells.Select((c, i) => i == 17 ? c with { Terrain = c.Terrain with { MinimumEntryCost = 5 } } : c).ToArray() };
        Check(WorldActions.Reachable(pass, actor.Id, new())[17].Remaining == 1, "mountain pass minimum retained");
        var blocked = moving with { Cells = moving.Cells.Select((c, i) => i == 17 ? c with { Owner = null, Terrain = c.Terrain with { Barrier = TerrainBarrier.Mountain } } : c).ToArray() };
        Check(!WorldActions.Reachable(blocked, actor.Id, new()).ContainsKey(17), "barrier cannot be bypassed");

        var upkeepWorld = durable with { Round = 2, Phase = TurnPhase.StartFaction,
            Factions = durable.Factions.Select(f => f with { Resources = new(100, 100, 100) }).ToArray(),
            Units = durable.Units.Select(u => u.Id == actor.Id ? u : u with { Support = new() }).ToArray() };
        var supplied = WorldTurns.Resume(upkeepWorld, upkeepWorld.Revision, new() { Extensions = bus }).State;
        Check(supplied.Factions.Single(f => f.Id == actor.Owner).Resources.Food == 100, "food exemption reaches actual settlement");
        var paying = upkeepWorld with { Units = upkeepWorld.Units.Select(u => u.Id == actor.Id ? u with { Combatant = reverted } : u).ToArray() };
        Check(WorldTurns.Resume(paying, paying.Revision, new() { Extensions = bus }).State.Factions.Single(f => f.Id == actor.Owner).Resources.Food == 90, "removing exemption restores upkeep");

        var morale = world with { Units = world.Units.Select(u => u.Id == actor.Id ? u with { Combatant = u.Combatant with { SkillSlots = [new("example.morale", "morale")] } } : u).ToArray() };
        morale = bus.Dispatch(morale, "turn.start", actor.Id, new(morale.RandomState));
        Check(Unit(morale, ally.Id).Morale.Baseline == 1 && Unit(morale, ally.Id).Combatant.Morale == 1, "morale updates runtime and combat values");
        Check(Unit(morale, enemy.Id).Combatant.Morale == 0, "morale obeys relation filter");
        Check(WorldSave.Decode(WorldSave.Encode(morale), morale.ContentRevision).Units.Single(u => u.Id == ally.Id).Morale == Unit(morale, ally.Id).Morale, "morale persists");
        var moraleTick = new ExtensionStatus { Id = "test.morale_tick", MaxStacks = 2, Effects = [new() { Event = "turn.start", Operation = "morale", Value = 1 }] };
        var moraleBus = new RuleEventBus(new Dictionary<string, ExtensionSkill>(), statuses: new Dictionary<string, ExtensionStatus> { [moraleTick.Id] = moraleTick });
        var stackedMorale = world with { Units = world.Units.Select(u => u.Id == actor.Id ? u with { Combatant = u.Combatant with {
            SkillSlots = [], Statuses = [new(moraleTick.Id, 2, Stacks: 2)] } } : u).ToArray() };
        Check(Unit(moraleBus.Dispatch(stackedMorale, "turn.start", actor.Id, new(world.RandomState)), actor.Id).Combatant.Morale == 1,
            "morale setter is independent of status stack count");

        var condition = capacity with { Health = 200, SkillSlots = [new("example.conditions", "condition")],
            Profile = capacity.Profile with { MinimumAttack = 4, MaximumAttack = 4 } };
        var battle = world with { Units = [actor with { Combatant = condition }, enemy with { Cell = 26 }],
            Cells = world.Cells.Select((c, i) => i == 26 ? c with { Owner = enemy.Owner, BuildingKind = "city", BuildingLevel = 1 }
                : c).ToArray() };
        var rules = new WorldBattleRules { Combat = new() { Extensions = bus } };
        var preview = WorldBattle.Preview(battle, battle.Revision, actor.Id, enemy.Id, rules);
        Check(preview.Main.DefenderDamage == 25, "JSON conditions use actual distance, source terrain and target city");
        var noCity = battle with { Cells = battle.Cells.Select((c, i) => i == 26 ? c with { BuildingKind = null, BuildingLevel = 0 } : c).ToArray() };
        Check(WorldBattle.Preview(noCity, noCity.Revision, actor.Id, enemy.Id, rules).Main.DefenderDamage == 20, "noncity target does not satisfy condition");
        var forest = battle with { Cells = battle.Cells.Select((c, i) => i == 24 ? c with { DefenseTerrain = "forest" } : c).ToArray() };
        Check(WorldBattle.Preview(forest, forest.Revision, actor.Id, enemy.Id, rules).Main.DefenderDamage == 20, "source terrain condition rejects forest");
        var near = battle with { Units = battle.Units.Select(u => u.Id == enemy.Id ? u with { Cell = 25 } : u).ToArray(),
            Cells = battle.Cells.Select((c, i) => i == 25 ? c with { Owner = enemy.Owner, BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
        Check(WorldBattle.Preview(near, near.Revision, actor.Id, enemy.Id, rules).Main.DefenderDamage == 20, "distance condition rejects adjacent target");
        var luaSkill = bus.Skills["example.conditions"] with { Effects = [bus.Skills["example.conditions"].Effects[0] with {
            When = null, Script = "rewrite/scripts/conditions.lua", Function = "conditional_damage" }] };
        var luaBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [luaSkill.Id] = luaSkill },
            new ExtensionScriptRuntime(new Dictionary<string, string> { ["rewrite/scripts/conditions.lua"] = Encoding.UTF8.GetString(files["rewrite/scripts/conditions.lua"]) }));
        Check(WorldBattle.Preview(battle, battle.Revision, actor.Id, enemy.Id, rules with { Combat = new() { Extensions = luaBus } }).Main == preview.Main,
            "optional Lua and JSON conditions produce identical combat and RNG");
        var shortRange = battle with { Units = battle.Units.Select(u => u.Id == actor.Id ? u with { Combatant = u.Combatant with { AttributeModifiers = null, Health = 100 } } : u).ToArray() };
        Reject(() => WorldBattle.Preview(shortRange, shortRange.Revision, actor.Id, enemy.Id, rules), "out of range attack");
        var defender = capacity with { Health = 200, SkillSlots = [] };
        Check(Ew4Combat.Calculate(enemy.Combatant, defender, 2, new(1)).Counterattack, "effective range enables counterattack");
        Check(!Ew4Combat.Calculate(enemy.Combatant, reverted, 2, new(1)).Counterattack, "base range cannot counter at distance two");

        var definitions = new Dictionary<string, Commander> { ["leader"] = new() { Id = "leader", SkillSlots = [new("example.capacity", "capacity")] },
            ["other"] = new() { Id = "other" } };
        var commanders = new WorldCommanderRules { Extensions = bus, Definitions = definitions, AllowReplacement = true };
        var deployment = world with { Commanders = [new() { Id = "leader", Owner = actor.Owner, RankHealthBonus = 50 }, new() { Id = "other", Owner = actor.Owner }] };
        var assigned = WorldCommanders.Assign(deployment, deployment.Revision, actor.Id, "leader", commanders).State;
        Check(Unit(assigned, actor.Id).Combatant.MaximumHealth == 250, "rank and passive HP combine once");
        var detached = WorldCommanders.Detach(assigned, assigned.Revision, actor.Id, commanders).State;
        Check(Unit(detached, actor.Id).Combatant.MaximumHealth == 100 && !Unit(detached, actor.Id).Combatant.FoodExempt, "detachment removes commander attributes");
        var replaced = WorldCommanders.Assign(assigned, assigned.Revision, actor.Id, "other", commanders).State;
        Check(Unit(replaced, actor.Id).Combatant.MaximumHealth == 100, "replacement removes previous passive HP");
        var eventWorld = assigned with { ScenarioPlayerFaction = actor.Owner, ScenarioEvents = [new() { Id = "detach", Effects = [new() {
            Type = "commander_detach", Unit = actor.Id, Commander = "leader", Faction = actor.Owner, Disposition = "available",
            CommanderTemplate = new() { Definition = definitions["leader"], Extensions = bus } }] }] };
        Check(Unit(ScenarioRules.Evaluate(eventWorld).State, actor.Id).Combatant.MaximumHealth == 100, "event detachment recomputes attributes");

        var manifest = files["rewrite/extension.json"];
        files["rewrite/extension.json"] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("\"apiVersion\": 3", "\"apiVersion\": 2"));
        Reject(() => ExtensionLoader.Load(p => files[p]), "API 2 cannot advertise API 3 operations");
        files["rewrite/extension.json"] = manifest;
        var statuses = files["rewrite/definitions/statuses.json"];
        files["rewrite/definitions/statuses.json"] = Encoding.UTF8.GetBytes("[{\"id\":\"example.inspired\",\"effects\":[{\"event\":\"turn.start\",\"operation\":\"apply_status\",\"status\":\"example.inspired\",\"duration\":1}]}]");
        Reject(() => ExtensionLoader.Load(p => files[p]), "recursive status application");
        files["rewrite/definitions/statuses.json"] = statuses;
        var skills = files["rewrite/definitions/skills.json"];
        files["rewrite/definitions/skills.json"] = Encoding.UTF8.GetBytes("[{\"id\":\"example.invalid\",\"effects\":[{\"event\":\"unit.attributes\",\"operation\":\"range_add\",\"value\":1,\"when\":{\"minDistance\":1}}]}]");
        Reject(() => ExtensionLoader.Load(p => files[p]), "location condition on cached attributes");
        files["rewrite/definitions/skills.json"] = skills;
        Reject(() => new ExtensionEffectCondition { MinDistance = 3, MaxDistance = 1 }.Validate(), "inverted distance interval");
        Reject(() => new ExtensionEffectCondition { TargetCategories = ["infantry", "infantry"] }.Validate(), "duplicate category condition");
        Reject(() => new ExtensionEffectCondition { TargetBuildings = null! }.Validate(), "null condition array");
        var resolvedSource = CombatContext.Resolve(battle, battle.Units[0], "infantry", new());
        var resolvedTarget = CombatContext.Resolve(battle, battle.Units[1], "infantry", new());
        Check(new ExtensionEffectCondition { SourceInBuilding = false, TargetInBuilding = true,
            TargetTerrains = ["land", "forest"] }.Matches(resolvedSource, resolvedTarget, 2), "building presence and OR array semantics");
        Check(!new ExtensionEffectCondition { TargetInBuilding = false }.Matches(resolvedSource, resolvedTarget, 2), "building absence rejects a city");
        Check(JsonSerializer.Serialize(capacity.Profile) == JsonSerializer.Serialize(actor.Combatant.Profile), "shared base profile unchanged throughout");
        return checks;
    }
}
