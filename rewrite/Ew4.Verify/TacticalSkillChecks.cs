using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;
using Ew4.Client;

internal static class TacticalSkillChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool yes, string label) { checks++; if (!yes) throw new Exception("Tactical skills: " + label); }
        void Reject(Action action, string label) { try { action(); } catch (RuleViolation) { checks++; return; } throw new Exception("Tactical skill accepted: " + label); }
        var skills = ExtensionLoader.ParseJson<ExtensionSkill[]>(File.ReadAllBytes(Path.Combine(root, "颓垣饿鸠/source/skills/mao.json")));
        foreach (var skill in skills) TacticalSkills.Validate(skill, 3);
        var bus = new RuleEventBus(skills.ToDictionary(s => s.Id));
        var active = skills[0];
        string slot = "commander.mao.4";
        var map = new HexMap(9, 9);
        var fixture = WorldChecks.Example();
        var profile = fixture.Units[0].Combatant.Profile with { Grade = "0", MaxHealth = 1000, MinimumAttack = 20, MaximumAttack = 20,
            MaximumRange = 3, DamageMultipliers = new Dictionary<string, float> { ["infantry"] = 1, ["cavalry"] = 1, ["artillery"] = 1, ["fort"] = 1, ["naval"] = 1 } };
        UnitState Unit(string id, int cell, string owner = "winner", int hp = 1000, string category = "infantry") => new() {
            Id = id, Cell = cell, Owner = owner, RemainingMovement = 6,
            Combatant = new() { Profile = profile with { Category = category }, Health = hp } };
        var source = Unit("mao", 40) with { Combatant = Unit("mao", 40).Combatant with {
            SkillSlots = skills.Select((s, i) => new SkillSlot(s.Id, i == 0 ? slot : "commander.mao." + (i + 4))).ToArray() } };
        var world = fixture with { Width = 9, Height = 9, ActiveFaction = "winner",
            Cells = Enumerable.Range(0, 81).Select(_ => new CellState { Owner = "winner" }).ToArray(),
            Units = [source, Unit("one", 39), Unit("two", 31), Unit("three", 41), Unit("four", 49), Unit("enemy", 42, "loser")],
            Factions = fixture.Factions.Select(f => f with { Resources = new(0, 0, 0) }).ToArray() };
        var move = new WorldMoveRules { Extensions = bus, SpendAllMovement = false };
        var turns = new WorldTurnRules { Extensions = bus };
        UnitState Get(WorldState w, string id) => w.Units.Single(u => u.Id == id);
        WorldState Cast(WorldState w, params string[] targets) => WorldSkillActions.Execute(w, w.Revision, source.Id, slot, null, bus, move, targets: targets).State;
        var original = WorldSave.Encode(world);
        var cast = Cast(world, "one", "two", "three");
        Check(cast.Units.Count(u => u.Maneuvers.Count == 1) == 4 && Get(cast, "mao").RemainingMovement == 8 && !Get(cast, "mao").Acted, "self plus three, movement without consuming attack");
        Check(cast.Factions.All(f => f.Resources == new Resources(0, 0, 0)) && cast.ExtensionState["mao/" + slot + "/ready"] == 7, "zero resources, C6");
        Check(Cast(world).Units.Count(u => u.Maneuvers.Count > 0) == 1, "self-only activation");
        Check(Cast(world with { Factions = world.Factions.Select(f => f with { Resources = new(-1, -1, -1) }).ToArray() }).Factions.All(f => f.Resources == new Resources(-1, -1, -1)), "no holding threshold even in debt");
        Reject(() => Cast(world, "one", "two", "three", "four"), "four extras");
        Reject(() => Cast(world, "one", "one"), "duplicate targets");
        Reject(() => Cast(world, "mao"), "self not an extra target");
        Reject(() => Cast(world, "enemy"), "enemy target");
        Reject(() => Cast(cast), "cooldown");
        var ineligible = world with { Units = world.Units.Select(u => u.Id == "one" ? u with { MovedThisTurn = true } : u).ToArray() };
        Reject(() => Cast(ineligible, "one"), "already moved recipient");
        Reject(() => Cast(world with { Units = world.Units.Select(u => u.Id == "mao" ? u with { AttackedThisTurn = true } : u).ToArray() }), "retained attack action cannot activate afterward");
        var movedCaster = WorldActions.Move(world, world.Revision, "mao", 58, move).State;
        Reject(() => Cast(movedCaster), "moved caster");
        foreach (string category in new[] { "artillery", "fort", "naval" })
            Reject(() => Cast(world with { Units = world.Units.Select(u => u.Id == "one" ? u with { Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = category } } } : u).ToArray() }, "one"), category + " extra");
        Check(original.SequenceEqual(WorldSave.Encode(world)), "failures and activation preserve original snapshot and RNG");
        var distant = world with { Units = world.Units.Select(u => u.Id == "one" ? u with { Cell = 0 } : u).ToArray() };
        Reject(() => Cast(distant, "one"), "out-of-range recipient");
        var transported = world with { Units = world.Units.Select(u => u.Id == "one" ? u with { Transport = true } : u).ToArray() };
        Reject(() => Cast(transported, "one"), "transported recipient");
        var restored = WorldSave.Decode(WorldSave.Encode(cast), cast.ContentRevision);
        Check(Get(restored, "one").Maneuvers.Single().StartCell == 39 && Get(restored, "one").RemainingMovement == 8, "save grants and movement");
        var moved = WorldActions.Move(restored, restored.Revision, "mao", 58, move).State;
        var enemyTurn = WorldTurns.EndTurn(moved, moved.Revision, turns).State;
        Check(Get(enemyTurn, "mao").Maneuvers.Single().EndCell == 58 && TacticalSkills.Resolve(enemyTurn, Get(enemyTurn, "mao"), bus)!.Evasion == .15, "end-position snapshot, protection through first enemy turn");
        var next = WorldTurns.EndTurn(enemyTurn, enemyTurn.Revision, turns).State;
        Check(Get(next, "mao").Maneuvers.Single().CounterReady && !Get(next, "one").Maneuvers.Single().CounterReady, "net movement qualifies only the moving unit");
        Check(Get(next, "mao").RemainingMovement == 6 && TacticalSkills.Resolve(next, Get(next, "mao"), bus)!.MainAttackPenalty == 0, "movement and attack penalty expire before counteroffensive");
        var afterReload = WorldSave.Decode(WorldSave.Encode(next), next.ContentRevision);
        Check(Get(afterReload, "mao").Maneuvers.Single().CounterReady, "counteroffensive survives reload");
        var separated = afterReload with { Units = afterReload.Units.Select(u => u.Id == "mao" ? u with { Cell = 80 } : u).ToArray() };
        Check(TacticalSkills.Resolve(separated, Get(separated, "one"), bus) == null, "protection suspended outside source radius");
        var battleRules = new WorldBattleRules { Combat = new() { Extensions = bus } };
        var previewBytes = WorldSave.Encode(afterReload);
        WorldBattle.Preview(afterReload, afterReload.Revision, "mao", "enemy", battleRules);
        Check(previewBytes.SequenceEqual(WorldSave.Encode(afterReload)), "preview does not consume first attack or RNG");
        var fought = WorldBattle.Attack(afterReload, afterReload.Revision, "mao", "enemy", battleRules).State;
        Check(Get(fought, "mao").Maneuvers.Single().Used && TacticalSkills.Resolve(fought, Get(fought, "mao"), bus)!.FirstAttack == 0, "commit consumes first attack");
        Check(Get(fought, "mao").AttackedThisTurn && Get(fought, "mao").RemainingMovement == 0, "main attack cannot refund movement or first-action eligibility");
        var secondEnemy = WorldTurns.EndTurn(next, next.Revision, turns).State;
        Check(Get(secondEnemy, "mao").Maneuvers.Count == 1, "protection survives second enemy turn");
        var expired = WorldTurns.EndTurn(secondEnemy, secondEnemy.Revision, turns).State;
        Check(expired.Units.All(u => u.Maneuvers.Count == 0), "protection expires at third own turn");
        var loop = Cast(world);
        int loopCell = Enumerable.Range(0, 6).Select(d => map.Neighbor(40, d)).First(c => world.Units.All(u => u.Cell != c));
        loop = WorldActions.Move(loop, loop.Revision, "mao", loopCell, move).State;
        loop = WorldActions.Move(loop, loop.Revision, "mao", 40, move).State;
        loop = WorldTurns.EndTurn(loop, loop.Revision, turns).State;
        loop = WorldTurns.EndTurn(loop, loop.Revision, turns).State;
        Check(!Get(loop, "mao").Maneuvers.Single().CounterReady, "walking out and back does not grant counteroffensive");
        var removedSource = cast with { Units = cast.Units.Where(u => u.Id != "mao").ToArray() };
        Check(TacticalSkills.Resolve(removedSource, Get(removedSource, "one"), bus) == null, "source death revokes protection");
        var unbound = cast with { Units = cast.Units.Select(u => u.Id == "mao" ? u with { Combatant = u.Combatant with { SkillSlots = [] } } : u).ToArray() };
        Check(TacticalSkills.Prune(unbound, bus).Units.All(u => u.Maneuvers.Count == 0), "unbinding revokes fixed roster");
        var changed = cast with { Units = cast.Units.Select(u => u.Id == "one" ? u with { CommanderId = "different" } : u).ToArray() };
        Check(TacticalSkills.Prune(changed, bus).Units.Single(u => u.Id == "one").Maneuvers.Count == 0, "recipient replacement revokes state");

        var attacker = Unit("a", 0).Combatant with { GroundMobile = true };
        var protectedUnit = attacker with { Evasion = .2f, EvasionIgnoringBuilding = .1f,
            Tactical = new(Evasion: .15) };
        int Hit(Combatant a, Combatant d) => Ew4Combat.Calculate(a, d, 1, new(7)).DefenderDamage;
        Check(Hit(attacker, protectedUnit) == 65, "add 15 points to ordinary 20-percent evasion");
        Check(Hit(attacker with { Skills = [Ew4Combat.Skill(14)] }, protectedUnit) == 92, "ignore evasion retains precisely half new protection");
        Check(Hit(attacker with { GroundMobile = false }, protectedUnit) == Hit(attacker, protectedUnit with { Tactical = null }), "fixed/air/naval attack excluded");
        Check(Hit(attacker, protectedUnit with { Evasion = .6f }) == Hit(attacker, protectedUnit with { Evasion = .6f, Tactical = null }), "never lower existing evasion above 50 percent");
        Check(Math.Abs(Hit(attacker with { Skills = [Ew4Combat.Skill(29)] }, protectedUnit with { BuildingKind = "city", Evasion = .4f }) - 80) <= 1, "building-ignore retains non-building extra evasion");
        Check(Ew4Combat.Calculate(protectedUnit, attacker, 1, new(7)).AttackerDamage == Ew4Combat.Calculate(protectedUnit with { Tactical = null }, attacker, 1, new(7)).AttackerDamage, "own attack retaliation does not get maneuver evasion");
        Check(Hit(attacker with { Tactical = new(Attack: .15, FirstAttack: .2) }, attacker) == 135, "35 percent combined counteroffensive and territory aura");
        Check(Hit(attacker with { Tactical = new(MainAttackPenalty: .15) }, attacker) == 85, "first-round main-attack cost");
        var genericSkill = new ExtensionSkill { Id = "verify.generic", Effects = [new() { Operation = "damage_multiplier", Value = 1.3 }] };
        var genericBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [genericSkill.Id] = genericSkill });
        var capped = attacker with { SkillSlots = [new(genericSkill.Id, "generic")], Tactical = new(Attack: .15, FirstAttack: .2) };
        Check(Ew4Combat.Calculate(capped, attacker, 1, new(7), new() { Extensions = genericBus }).DefenderDamage == 150, "generic and original attack capped at fifty percent");
        var zeroSkill = genericSkill with { Effects = [new() { Operation = "damage_multiplier", Value = 0 }] };
        var zeroBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [zeroSkill.Id] = zeroSkill });
        Check(Ew4Combat.Calculate(capped, attacker, 1, new(7), new() { Extensions = zeroBus }).DefenderDamage == 1, "tactical bonus cannot restore damage suppressed by zero multiplier");
        var combinedShield = protectedUnit with { Tactical = new(Defense: .15, Evasion: .15) };
        Check(Hit(attacker, combinedShield) == 65, "protection and territory defense use highest, not multiplication");
        var reduction = genericSkill with { Id = "verify.reduction", Effects = [new() { Event = "combat.modify_incoming_damage", Operation = "damage_multiplier", Value = .7 }] };
        var reductionBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [reduction.Id] = reduction });
        var allDefense = combinedShield with { SkillSlots = [new(reduction.Id, "generic.defense")] };
        Check(Ew4Combat.Calculate(attacker, allDefense, 1, new(7), new() { Extensions = reductionBus }).DefenderDamage == 48, "generic and original defense share forty-percent budget");

        var foreign = world with { Cells = world.Cells.Select((c, i) => i == 40 ? c with { Owner = "loser" } : c).ToArray() };
        Check(TacticalSkills.Resolve(foreign, Get(foreign, "mao"), bus)?.Attack == .15 && TacticalSkills.Resolve(foreign, Get(foreign, "one"), bus)?.Defense == .15, "foreign source protects self and neighbors");
        Check(TacticalSkills.Resolve(world, Get(world, "mao"), bus) == null, "own territory not eligible");
        var allyTerritory = foreign with { Factions = foreign.Factions.Select(f => f.Id == "loser" ? f with { Camp = "b" } : f).ToArray() };
        Check(TacticalSkills.Resolve(allyTerritory, Get(allyTerritory, "mao"), bus) == null, "allied territory not eligible");
        var unknown = foreign with { Cells = foreign.Cells.Select((c, i) => i == 40 ? c with { Owner = null } : c).ToArray() };
        Check(TacticalSkills.Resolve(unknown, Get(unknown, "mao"), bus) == null, "missing ownership is not foreign territory");
        var actualAttack = WorldBattle.Preview(foreign, foreign.Revision, "mao", "enemy", battleRules).Main;
        Check(actualAttack.DefenderDamage == 115 && actualAttack.AttackerDamage == 85, "territory attack and defense apply in actual world battle: " + actualAttack);
        var far = foreign with { Units = foreign.Units.Select(u => u.Id == "one" ? u with { Cell = 60 } : u).ToArray() };
        Check(TacticalSkills.Resolve(far, Get(far, "one"), bus) == null, "aura never carried across map");

        var healWorld = world with { Units = [source with { Combatant = source.Combatant with { Health = 50 } }, Unit("local", 31, hp: 10),
                Unit("west1", 0, hp: 10), Unit("west2", 1, hp: 20), Unit("west3", 9, hp: 30),
                Unit("east1", 80, hp: 10), Unit("east2", 79, hp: 20), Unit("enemy", 42, "loser", 1)],
            Cells = world.Cells.Select((c, i) => i is 0 or 80 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
        var healed = TacticalSkills.Heal(healWorld, "winner", bus);
        Check(Get(healed, "west1").Combatant.Health == 14 && Get(healed, "west2").Combatant.Health == 24
            && Get(healed, "east1").Combatant.Health == 14 && Get(healed, "east2").Combatant.Health == 24, "each national facility independently heals two");
        Check(Get(healed, "west3").Combatant.Health == 30 && Get(healed, "local").Combatant.Health == 16
            && Get(healed, "mao").Combatant.Health == 50 && Get(healed, "enemy").Combatant.Health == 1, "weakest selection, adjacent recovery excludes self and enemy");
        var overlap = healWorld with { Cells = healWorld.Cells.Select((c, i) => i is 9 or 31 ? c with { BuildingKind = "farmland", BuildingLevel = 1 } : c).ToArray() };
        var overlapHealed = TacticalSkills.Heal(overlap, "winner", bus);
        Check(Get(overlapHealed, "west1").Combatant.Health == 14, "overlapping facilities do not stack");
        Check(Get(overlapHealed, "local").Combatant.Health == 20, "facility four plus adjacent six stack");
        var replayed = TacticalSkills.Heal(WorldSave.Decode(WorldSave.Encode(overlapHealed), overlapHealed.ContentRevision), "winner", bus);
        Check(WorldSave.Encode(replayed).SequenceEqual(WorldSave.Encode(overlapHealed)), "reload cannot repeat this turn healing");
        var secondSource = source with { Id = "second.source", Cell = 32 };
        var doubled = overlap with { Units = overlap.Units.Append(secondSource).ToArray() };
        Check(Get(TacticalSkills.Heal(doubled, "winner", bus), "local").Combatant.Health == 20, "same adjacent effect from two sources remains six");
        var reseated = overlapHealed with { Units = overlapHealed.Units.Where(u => u.Id != "mao").Append(secondSource).ToArray() };
        Check(Get(TacticalSkills.Heal(reseated, "winner", bus), "local").Combatant.Health == 20, "new source cannot repeat faction healing this turn");
        var nextHealing = overlapHealed with { Round = overlapHealed.Round + 1 };
        Check(Get(TacticalSkills.Heal(nextHealing, "winner", bus), "local").Combatant.Health == 30, "healing renews next faction turn");
        var independent = new ExtensionSkill { Id = "verify.heal", Effects = [new() { Event = "turn.start", Operation = "heal", Value = 20 }] };
        var independentBus = new RuleEventBus(skills.Append(independent).ToDictionary(s => s.Id));
        var withOther = overlap with { Units = overlap.Units.Select(u => u.Id == "local" ? u with { Combatant = u.Combatant with { SkillSlots = [new(independent.Id, "independent")] } } : u).ToArray() };
        withOther = independentBus.DispatchFactionTurn(withOther, "winner", "turn.start");
        Check(Get(TacticalSkills.Heal(withOther, "winner", independentBus), "local").Combatant.Health == 40, "independent healing stacks without per-turn cap");
        var baseBus = new RuleEventBus(new Dictionary<string, ExtensionSkill>());
        Check(!baseBus.TacticalEnabled && TacticalSkills.Resolve(foreign, Get(foreign, "mao"), baseBus) == null, "no Mod configuration leaves base combat intact");
        Check(WorldSave.Encode(TacticalSkills.StartTurn(healWorld, "winner", baseBus)).SequenceEqual(WorldSave.Encode(healWorld)), "no Mod configuration leaves healing intact");
        var ai = new WorldAiRules { Movement = move, ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var production = new WorldProductionRules();
        var aiChange = WorldAi.ExecuteStep(world, new(new(world.Revision, world.AiRandomState, world.AiRandomState, null, null), null), ai, production, turns, battleRules);
        Check(Get(aiChange.State, "mao").Maneuvers.Count == 1 && aiChange.Notices.Any(n => n.Kind == "skill.used"), "AI uses configured maneuver before normal action");
        Reject(() => TacticalSkills.Validate(active with { Maneuver = active.Maneuver! with { MaximumOthers = 17 } }, 3), "unbounded targets");
        Reject(() => TacticalSkills.Validate(active, 2), "old API configuration");
        Reject(() => TacticalSkills.Validate(skills[2] with { RecoveryNetwork = skills[2].RecoveryNetwork! with { FacilityEffect = "" } }, 3), "missing healing category");
        return checks + ClientChecks(root, skills);
    }
    private static int ClientChecks(string root, ExtensionSkill[] skills)
    {
        int checks = 0;
        void Check(bool yes, string label) { checks++; if (!yes) throw new Exception("Tactical client: " + label); }
        byte[] Read(string path)
        {
            if (path == "rewrite/extension.json") return JsonSerializer.SerializeToUtf8Bytes(new {
                schema = 1, apiVersion = 3, skills = new[] { "rewrite/definitions/mao-skills.json" } });
            if (path == "rewrite/definitions/mao-skills.json") return JsonSerializer.SerializeToUtf8Bytes(skills, ClientGame.Json);
            string overlay = Path.Combine(root, "颓垣饿鸠/portraits/game-preview/overlay", path);
            return File.ReadAllBytes(File.Exists(overlay) && path.StartsWith("rewrite/presentation/") ? overlay :
                Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        }
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var start = content.Start("conquest1", "fra");
        var w = start.Session.World;
        var source = w.Units.First(u => u.Owner == w.ActiveFaction && u.Combatant.Profile.Category == "infantry" && TacticalSkills.Mobile(w, u));
        source = source with { Acted = false, MovedThisTurn = false, RemainingMovement = source.Combatant.Profile.Movement,
            Combatant = source.Combatant with { SkillSlots = skills.Select((s, i) => new SkillSlot(s.Id, "mao.client." + i)).ToArray(), Skills = [] } };
        var map = new HexMap(w.Width, w.Height);
        var free = Enumerable.Range(0, w.Cells.Count).Where(c => w.Cells[c].Terrain.Active && !w.Cells[c].Terrain.Sea
            && map.Distance(c, source.Cell) <= 3 && w.Units.All(u => u.Cell != c)).Take(4).ToArray();
        Check(free.Length == 4, "four recipient fixture sites");
        var recipients = free.Select((c, i) => source with { Id = "tactical.client." + i, Cell = c, CommanderId = null, CommanderBase = null,
            Combatant = source.Combatant with { SkillSlots = [], Skills = [] } }).ToArray();
        w = w with { Units = w.Units.Select(u => u.Id == source.Id ? source : u).Concat(recipients).ToArray() };
        w.Validate();
        string folder = Path.Combine(root, "build/mao-tactical-20261104/client-qa", Guid.NewGuid().ToString("N"));
        string file = Path.Combine(folder, "saves/mode-2/slot-1.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(start.Session with { World = w })), ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object body)
        {
            using var doc = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(body, ClientGame.Json)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(), route + ": " + doc.RootElement);
            return doc.RootElement.GetProperty("data").Clone();
        }
        var loaded = Call("load", new { mode = 2, slot = 1 });
        var selection = Call("action", new { op = "select", unit = source.Id, cell = source.Cell, revision = loaded.GetProperty("revision").GetInt64() });
        var entry = selection.GetProperty("skills").EnumerateArray().Single(e => e.GetProperty("skill").GetProperty("id").GetString() == skills[0].Id);
        Check(entry.GetProperty("available").GetBoolean() && entry.GetProperty("multiple").GetBoolean()
            && entry.GetProperty("maximumTargets").GetInt32() == 3 && entry.GetProperty("includesSelf").GetBoolean(), "multi-select contract");
        Check(!entry.GetProperty("targets").EnumerateArray().Any(t => t.GetString() == source.Id), "caster cannot consume a target slot");
        File.WriteAllBytes(Path.Combine(root, "build/mao-tactical-20261104/battle-ui.json"), JsonSerializer.SerializeToUtf8Bytes(new { state = loaded, entry, unit = source.Id }, ClientGame.Json));
        using var rejected = JsonDocument.Parse(host.Dispatch("/api/action", JsonSerializer.Serialize(new { op = "skill", unit = source.Id,
            offer = "mao.client.0", units = recipients.Select(u => u.Id).ToArray(), revision = loaded.GetProperty("revision").GetInt64() }, ClientGame.Json)));
        Check(!rejected.RootElement.GetProperty("ok").GetBoolean(), "host rejects four targets");
        var activated = Call("action", new { op = "skill", unit = source.Id, offer = "mao.client.0",
            units = recipients.Take(3).Select(u => u.Id).ToArray(), revision = loaded.GetProperty("revision").GetInt64() });
        Check(activated.GetProperty("units").EnumerateArray().Count(u => u.GetProperty("statuses").EnumerateArray()
            .Any(s => s.GetProperty("id").GetString() == skills[0].Id)) == 4, "host exposes four actual maneuver states");
        var cooldown = Call("action", new { op = "select", unit = source.Id, cell = source.Cell, revision = activated.GetProperty("revision").GetInt64() });
        var cooling = cooldown.GetProperty("skills").EnumerateArray().Single();
        Check(!cooling.GetProperty("available").GetBoolean() && cooling.GetProperty("remainingCooldown").GetInt64() == 6, "host cooldown");
        Call("save", new { mode = 2, slot = 2 });
        host = new ClientGame(content, folder);
        var reloaded = Call("load", new { mode = 2, slot = 2 });
        Check(reloaded.GetProperty("units").EnumerateArray().Count(u => u.GetProperty("statuses").EnumerateArray()
            .Any(s => s.GetProperty("id").GetString() == skills[0].Id)) == 4, "host restart preserves actual state");
        return checks;
    }
}
