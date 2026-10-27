using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ArmyCapacityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        checks += AiDisbandChecks.Run();
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Army capacity: " + label); }
        void Reject(Action action, string? code = null)
        {
            try { action(); }
            catch (RuleViolation e) { Check(code == null || e.Code == code, "rejected " + e.Code); return; }
            catch (JsonException) when (code == null) { checks++; return; }
            throw new InvalidOperationException("Army capacity: expected rejection " + code);
        }
        var fixture = WorldChecks.Example();
        var policy = new ArmyCapacityRules();
        var world = fixture with { ArmyCapacity = policy, Cells = fixture.Cells.Select((c, i) => i switch
        {
            0 => c with { BuildingKind = "city", BuildingLevel = 7 },
            5 => c with { BuildingKind = "city", BuildingLevel = 1 },
            6 => c with { BuildingKind = "stable", BuildingLevel = 2 },
            7 => c with { BuildingKind = "city", BuildingLevel = 3 },
            8 => c with { BuildingKind = "industry", BuildingLevel = 1 },
            _ => c
        }).ToArray() };
        Check(WorldArmyCapacity.Limit(world, "winner") == 17, "base plus owned buildings only");
        Check(WorldArmyCapacity.Limit(world, "loser") == 16, "enemy territory has its own capacity");
        Check(WorldArmyCapacity.Limit(world with { Limits = new() { UnitsPerFaction = 12 } }, "winner") == 12, "fixed scenario cap still applies");
        Check(WorldArmyCapacity.Limit(fixture, "winner") == null, "unconfigured standalone core worlds remain compatible");
        Check(WorldArmyCapacity.Limit(world with { ArmyCapacity = policy with { FactionBases = new Dictionary<string, int> { ["winner"] = 12 } } }, "winner") == 21,
            "per faction base replaces default");
        foreach (string kind in new[] { "city", "industry", "stable", "port", "farmland" })
        for (int level = 0; level < policy.Buildings[kind].Length; level++)
            Check(WorldArmyCapacity.BuildingCapacity(world, new() { BuildingKind = kind, BuildingLevel = level }) == policy.Buildings[kind][level], "every configured level");
        Check(WorldArmyCapacity.BuildingCapacity(world, new() { BuildingKind = "city", BuildingLevel = 7, Terrain = new() { Active = false } }) == 0,
            "inactive cells do not grant capacity");
        Reject(() => WorldArmyCapacity.BuildingCapacity(world, new() { BuildingKind = "city", BuildingLevel = 8 }), "army.capacity_level");
        var maxPolicy = policy with { BaseCapacity = int.MaxValue };
        Check(WorldArmyCapacity.Limit(world with { ArmyCapacity = maxPolicy }, "winner") == int.MaxValue, "large totals saturate without overflow");

        var actor = fixture.Units.Single(u => u.Owner == "winner");
        var enemy = fixture.Units.Single(u => u.Owner == "loser");
        var offer = new RecruitmentOffer { Id = "capacity", Building = BuildingRequirement.Any, Template = actor.Combatant, Cost = new(1, 0, 0) };
        var recruitmentWorld = fixture with { ArmyCapacity = policy with { BaseCapacity = 0 },
            Factions = fixture.Factions.Select(f => f with { Resources = new(100, 100, 100),
                Recruitment = new Dictionary<string, RecruitmentGrant> { [offer.Id] = new() } }).ToArray(),
            Cells = fixture.Cells.Select((c, i) => i == 7 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
        Check(WorldActions.CanRecruit(recruitmentWorld, 7, offer), "one last slot is available");
        var full = WorldActions.Recruit(recruitmentWorld, recruitmentWorld.Revision, 7, "last.slot", offer).State;
        Check(full.Units.Count(u => u.Owner == "winner") == 2 && WorldArmyCapacity.Limit(full, "winner") == 2, "recruit consumes last slot");
        Check(WorldActions.CanRecruit(full, 6, offer), "full capacity still permits normal recruitment");
        byte[] before = WorldSave.Encode(full);
        var overflow = WorldActions.Recruit(full, full.Revision, 6, "overflow", offer).State;
        Check(overflow.Units.Count(u => u.Owner == "winner") == 3 && WorldArmyCapacity.IsOverLimit(overflow, "winner"),
            "recruitment can cross capacity and activates the penalty");
        Check(overflow.Factions.Single(f => f.Id == "winner").Resources.Money == full.Factions.Single(f => f.Id == "winner").Resources.Money - 1,
            "over-capacity recruitment pays normal cost");
        Check(before.SequenceEqual(WorldSave.Encode(full)), "successful recruitment preserves prior snapshot");
        Check(!WorldArmyCapacity.IsOverLimit(full, "winner") && !WorldArmyCapacity.IsOverLimit(fixture, "winner"),
            "exact capacity and unlimited worlds have no penalty");
        var lost = full with { Cells = full.Cells.Select((c, i) => i == 7 ? c with { Owner = "loser" } : c).ToArray() };
        lost.Validate();
        Check(lost.Units.Count(u => u.Owner == "winner") == 2 && WorldArmyCapacity.Limit(lost, "winner") == 0, "lost cities lower cap without removing armies");
        Check(WorldSave.Decode(WorldSave.Encode(lost), lost.ContentRevision).Units.Count == lost.Units.Count, "over-capacity armies remain loadable");
        var reinforced = full with { Units = full.Units.Append(actor with { Id = "scripted.reinforcement", Cell = 6 }).ToArray() };
        reinforced.Validate();
        Check(!WorldArmyCapacity.CanAdd(reinforced, "winner") && reinforced.Units.Count == full.Units.Count + 1,
            "forced scenario reinforcements exceed capacity and stop AI expansion");
        var expanded = full with { Cells = full.Cells.Select((c, i) => i == 0 ? c with { Owner = "winner", BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
        Check(WorldArmyCapacity.Limit(expanded, "winner") == 4 && WorldActions.CanRecruit(expanded, 6, offer), "capturing a city immediately opens slots");
        var upgraded = WorldImprovements.UpgradeBuilding(full, full.Revision, 7, new() { Steps = [new("city", 1, 2, new(1, 0, 0))] }).State;
        Check(WorldArmyCapacity.Limit(upgraded, "winner") == 3 && WorldActions.CanRecruit(upgraded, 6, offer), "public upgrade immediately opens one slot");
        var casualty = full with { Units = full.Units.Where(u => u.Id != "last.slot").ToArray() };
        Check(WorldActions.CanRecruit(casualty, 6, offer), "losing or removing a unit releases a slot");
        Check(WorldArmyCapacity.Limit(full with { Units = [enemy, actor with { Transport = true }] }, "winner") == 2, "transport does not change building capacity");
        Check(!WorldArmyCapacity.CanAdd(full with { Units = full.Units.Select(u => u.Owner == "winner" ? u with {
            Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = "naval" } } } : u).ToArray() }, "winner"), "navies consume shared slots");
        var summon = new ExtensionSkill { Id = "capacity.summon", Kind = "active", Target = "cell", Range = 9,
            Effects = [new() { Event = "skill.activate", Operation = "summon", Value = 1, Profile = actor.Combatant.Profile.Id }] };
        var bus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [summon.Id] = summon });
        bus.RegisterUnitTemplates([new(actor.Combatant.Profile, actor.Support)]);
        WorldState Caster(WorldState source) => source with { Units = source.Units.Select(u => u.Id == actor.Id
            ? u with { Combatant = u.Combatant with { SkillSlots = [new(summon.Id, "capacity.summon.slot")] } } : u).ToArray() };
        Check(!bus.ValidCellTarget(full, actor, summon, 6), "full capacity excludes summon target");
        Reject(() => WorldSkillActions.Execute(Caster(full), full.Revision, actor.Id, "capacity.summon.slot", null, bus, new(), 6), "skill.target");
        var summoned = WorldSkillActions.Execute(Caster(casualty), casualty.Revision, actor.Id, "capacity.summon.slot", null, bus, new(), 6).State;
        Check(summoned.Units.Count(u => u.Owner == "winner") == 2 && !WorldArmyCapacity.CanAdd(summoned, "winner"),
            "controllable summon consumes the remaining army slot");
        var copied = full.Copy();
        full.ArmyCapacity!.Buildings["city"][1] = 99;
        Check(WorldArmyCapacity.Limit(copied, "winner") == 2, "policy tables are isolated by snapshot copy");
        full.ArmyCapacity.Buildings["city"][1] = 2;

        var ai = new WorldAiRules { Tactics = new() { Grades = new Dictionary<string, float> { ["0"] = 1 } },
            ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var turns = new WorldTurnRules { Buildings = [new("city", 1, new(0, 0, 10))] };
        foreach (bool human in new[] { false, true })
        {
            var economyRules = turns with { Buildings = [new("city", 1, new(11, 7, 31))] };
            WorldState EconomyWorld(WorldState source, long food = 100) => source with {
                Round = 2, ActiveFaction = "loser",
                Factions = source.Factions.Select(f => f.Id == "winner" ? f with {
                    Human = human, Resources = new(100, 100, food), IncomeBonus = new(2, 2, 2)
                } : f).ToArray(),
                Units = source.Units.Select(u => u.Owner == "winner" ? u with { Support = new() { FoodConsumption = 5 } } : u).ToArray()
            };
            FactionState Settle(WorldState source) => WorldTurns.EndTurn(source, source.Revision, economyRules).State.Factions.Single(f => f.Id == "winner");
            Check(Settle(EconomyWorld(full)).Resources == new Resources(113, 109, 123), "exact capacity has full output and normal upkeep: " + human);
            Check(Settle(EconomyWorld(overflow)).Resources == new Resources(106, 104, 86), "all output including bonuses halves, rounds down; total upkeep doubles: " + human);
            var exempt = EconomyWorld(overflow) with { Units = EconomyWorld(overflow).Units.Select(u => u.Id == "overflow"
                ? u with { Support = u.Support with { Logistics = true } } : u).ToArray() };
            Check(Settle(exempt).Resources.Food == 96, "logistics exemption survives doubled upkeep: " + human);
            Check(Settle(EconomyWorld(overflow with { Units = overflow.Units.Where(u => u.Id != "overflow").ToArray() })).Resources == new Resources(113, 109, 123),
                "returning to capacity immediately clears penalty: " + human);
            var restoredCapacity = overflow with { Cells = overflow.Cells.Select((c, i) => i == 5 ? c with { BuildingKind = "city", BuildingLevel = 1 } : c).ToArray() };
            Check(!WorldArmyCapacity.IsOverLimit(restoredCapacity, "winner") && Settle(EconomyWorld(restoredCapacity)).Resources == new Resources(124, 116, 149),
                "gaining building capacity restores production and upkeep: " + human);
            var hungry = WorldTurns.EndTurn(EconomyWorld(overflow, 0), overflow.Revision, economyRules).State;
            Check(hungry.Factions.Single(f => f.Id == "winner").Shortage == 2
                && hungry.Units.Where(u => u.Owner == "winner").All(u => u.Morale.Effective == -1),
                "over-capacity food shortage penalizes human and AI morale equally: " + human);
            Check(Settle(EconomyWorld(overflow) with { Round = 1 }).Resources == new Resources(100, 100, 100),
                "opening round keeps existing no-settlement behavior: " + human);
        }
        foreach (string task in new[] { "recruit", "naval", "fortress" })
        {
            var production = new WorldProductionRules { Offers = new Dictionary<string, RecruitmentOffer> { [offer.Id] = offer },
                Groups = [new("capacity", [offer.Id], task == "naval")], Tasks = [new(task, 1)],
                Fortresses = new Dictionary<string, RecruitmentOffer> { [offer.Id] = offer },
                FortressScoring = new() { Recipes = [new(offer.Id, 1, false)] } };
            for (ulong seed = 0; seed < 12; seed++)
            {
                var plan = WorldAi.PlanProduction(full with { AiRandomState = seed }, ai, production, turns);
                Check(plan is { AttemptedTask: null, Command: null } && plan.FinalRandomState == seed, task + " AI skips full capacity without random draws");
            }
            var forged = new AiProductionPlan(full.Revision, full.AiRandomState, full.AiRandomState, task,
                new AiRecruitCommand(full.Revision, 6, offer.Id, "forged", task == "fortress"));
            Reject(() => WorldAi.ExecuteProduction(full, forged, production, ai), "recruit.capacity");
            var overPlan = WorldAi.PlanProduction(overflow, ai, production, turns);
            Check(overPlan is { AttemptedTask: null, Command: null }, task + " AI also skips recruitment after passive overflow");
            var lastSlot = new AiProductionPlan(casualty.Revision, casualty.AiRandomState, casualty.AiRandomState, task,
                new AiRecruitCommand(casualty.Revision, 6, offer.Id, "ai.last.slot", task == "fortress"));
            var aiFull = WorldAi.ExecuteProduction(casualty, lastSlot, production, ai).State;
            Check(aiFull.Units.Count(u => u.Owner == "winner") == 2 && !WorldArmyCapacity.IsOverLimit(aiFull, "winner"),
                task + " AI can fill its last slot without triggering penalties");
        }
        var configuration = new ArmyCapacityConfiguration { FactionBases = new()
        {
            ["*"] = new() { ["fra"] = 10 }, ["conquest1"] = new() { ["fra"] = 12, ["exact.faction"] = 14 }
        } };
        var mapped = configuration.Resolve("conquest1", new Dictionary<string, string> { ["exact.faction"] = "fra", ["other.faction"] = "fra" });
        Check(mapped.FactionBases["exact.faction"] == 14 && mapped.FactionBases["other.faction"] == 12, "scenario and exact faction precedence");
        foreach (string json in new[] { "{\"schema\":2}", "{\"baseCapacity\":-1}", "{\"baseCapacity\":1.5}", "{\"baseCapacity\":\"8\"}",
            "{\"baseCapacity\":8,\"baseCapacity\":9}", "{\"buildings\":null}", "{\"buildings\":{\"city\":[0,-1]}}", "{\"buildings\":{\"city\":[1,2]}}",
            "{\"buildings\":{\"city\":[0,\"2\"]}}", "{\"factionBases\":{\"*\":{\"fra\":-1}}}", "{\"typo\":8}" })
            Reject(() => ArmyCapacityConfiguration.Read(_ => Encoding.UTF8.GetBytes(json)));

        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var started = content.Start("conquest1", "fra");
        Check(started.Session.World.Units.Count(u => u.Owner == started.Session.PlayerFaction) == 32
            && WorldArmyCapacity.Limit(started.Session.World, started.Session.PlayerFaction) == 49, "actual France begins at 32/49");
        int starts = 0;
        foreach (var choice in content.Scenarios.Where(s => s.Mode != 3))
        {
            var battle = content.Battle(choice.Id);
            var scenario = content.Start(choice.Id, choice.Mode == 2 ? battle.Factions.First(f => f.Camp != 4).CountryId : null);
            var sample = scenario.Session;
            sample.Validate();
            Check(scenario.Rules.Production.Values.All(p => p.EmergencyDisband == (choice.Mode == 2)), "only conquest enables AI emergency disband: " + choice.Id);
            Check(sample.World.ArmyCapacity != null && WorldArmyCapacity.Limit(sample.World, sample.PlayerFaction) >= 0, "scenario capacity initializes: " + choice.Id);
            starts++;
        }
        Check(starts >= 90, "all built-in scenarios covered");
        string folder = Path.Combine(root, "build/army-capacity-20261026/check-profile");
        Directory.CreateDirectory(folder);
        var oldWorld = started.Session.World with { ArmyCapacity = null };
        var oldSession = started.Session with { World = oldWorld };
        var slot = new ClientSlot("conquest1", "fra", SessionSave.Encode(oldSession));
        File.WriteAllBytes(Path.Combine(folder, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(slot, ClientGame.Json));
        var host = new ClientGame(content, folder);
        using var loaded = JsonDocument.Parse(host.Dispatch("/api/load", "{\"slot\":1}"));
        Check(loaded.RootElement.GetProperty("ok").GetBoolean(), "legacy client slot loads: " + loaded.RootElement.GetRawText()[..Math.Min(160, loaded.RootElement.GetRawText().Length)]);
        var playerView = loaded.RootElement.GetProperty("data").GetProperty("factions").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == started.Session.PlayerFaction);
        Check(playerView.GetProperty("unitCount").GetInt32() == 32 && playerView.GetProperty("unitLimit").GetInt32() == 49,
            "old client saves acquire policy and display current count/limit");
        byte[] ReconfiguredRead(string path) => path == ArmyCapacityConfiguration.Path
            ? Encoding.UTF8.GetBytes("{\"baseCapacity\":10}") : Read(path);
        var reconfigured = new ClientContent(ReconfiguredRead, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        Check(reconfigured.Start("conquest1", "fra").Session.World.ContentRevision == started.Session.World.ContentRevision,
            "capacity tuning preserves existing source content binding");
        var changedHost = new ClientGame(reconfigured, folder);
        using var changed = JsonDocument.Parse(changedHost.Dispatch("/api/load", "{\"slot\":1}"));
        Check(changed.RootElement.GetProperty("ok").GetBoolean(), "old slot loads after capacity tuning");
        var changedPlayer = changed.RootElement.GetProperty("data").GetProperty("factions").EnumerateArray()
            .Single(f => f.GetProperty("id").GetString() == started.Session.PlayerFaction);
        Check(changedPlayer.GetProperty("unitCount").GetInt32() == 32 && changedPlayer.GetProperty("unitLimit").GetInt32() == 51,
            "loading applies installed capacity changes without altering armies");
        var envelope = JsonNode.Parse(WorldSave.Encode(oldWorld))!.AsObject();
        envelope["payload"]!.AsObject().Remove("armyCapacity");
        envelope["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(envelope["payload"]!.ToJsonString())));
        Check(WorldSave.Decode(Encoding.UTF8.GetBytes(envelope.ToJsonString()), oldWorld.ContentRevision).ArmyCapacity == null,
            "pre-feature world saves without policy field still decode");
        return checks;
    }
}
