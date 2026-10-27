using Ew4.Core;
using Ew4.Persistence;

internal static class AiDisbandChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("AI disband: " + label); }
        var fixture = WorldChecks.Example();
        var actor = fixture.Units.Single(u => u.Owner == "winner");
        var offer = new RecruitmentOffer { Id = "emergency", Template = actor.Combatant, Building = BuildingRequirement.Any, Cost = new(101, 31, 7) };
        var production = new WorldProductionRules { EmergencyDisband = true, Tasks = [],
            Offers = new Dictionary<string, RecruitmentOffer> { [offer.Id] = offer } };
        var ai = new WorldAiRules();
        var turns = new WorldTurnRules { Buildings = [new("port", 1, new(0, 0, 0))] };
        var world = fixture with { Width = 30, Height = 3, Round = 2,
            ArmyCapacity = new() { BaseCapacity = 8 },
            Cells = Enumerable.Range(0, 90).Select(i => new CellState { Owner = i == 0 ? "loser" : "winner" }).ToArray(),
            Factions = fixture.Factions.Select(f => f.Id == "winner" ? f with { Human = false, Shortage = 2,
                Resources = new(100, 100, 100), Recruitment = new Dictionary<string, RecruitmentGrant> { [offer.Id] = new() } } : f).ToArray(),
            Units = fixture.Units.Where(u => u.Owner == "loser").Concat(Enumerable.Range(50, 10).Select(i => actor with {
                Id = "rear." + i, Cell = i, Acted = false, Morale = new(0, 0, -1), Support = new() { FoodConsumption = 5 },
                Combatant = actor.Combatant with { Health = i == 59 ? 37 : 100, Morale = -1 }
            })).ToArray() };
        AiProductionPlan Plan(WorldState source, WorldProductionRules? policy = null) => WorldAi.PlanProduction(source, ai, policy ?? production, turns);
        bool Disbands(WorldState source, WorldProductionRules? policy = null) => Plan(source, policy).Command is AiDisbandCommand;
        var before = WorldSave.Encode(world);
        var plan = Plan(world);
        Check(plan.Command is AiDisbandCommand { UnitId: "rear.59" }, "farthest rear unit selected deterministically");
        Check(plan.FinalRandomState == world.AiRandomState, "emergency action uses no random draws");
        var result = WorldAi.ExecuteProduction(world, plan, production, ai).State;
        Check(result.Units.Count(u => u.Owner == "winner") == 9, "only one unit removed");
        Check(result.Factions.Single(f => f.Id == "winner").Resources == new Resources(118, 105, 101), "same wounded refund as player");
        Check(before.SequenceEqual(WorldSave.Encode(world)), "prior snapshot remains unchanged");
        Check(!Disbands(result), "no repeated disband while still over capacity");
        Check(!Disbands(WorldSave.Decode(WorldSave.Encode(result), result.ContentRevision)), "turn limit survives save and load");
        var reset = WorldTurns.EndTurn(result, result.Revision, turns).State;
        reset = reset with { Factions = reset.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(100, 100, 0) } : f).ToArray() };
        reset = WorldTurns.EndTurn(reset, reset.Revision, turns).State;
        Check(Disbands(reset), "next owner turn may remove one more unit if crisis persists");
        var floor = world with { Units = world.Units.Where(u => u.Id is not ("rear.58" or "rear.59")).ToArray(), ArmyCapacity = new() { BaseCapacity = 0 } };
        Check(!Disbands(floor), "at least eight units retained even after all capacity is lost");
        Check(!Disbands(world, production with { EmergencyDisband = false }), "campaign policy prohibits emergency disband");
        Check(!Disbands(world with { ArmyCapacity = new() { BaseCapacity = 10 } }), "no action at exact capacity");
        foreach (int shortage in new[] { 0, 1 })
            Check(!Disbands(world with { Factions = world.Factions.Select(f => f with { Shortage = shortage }).ToArray() }), "no disband before morale shortage threshold");
        Check(!Disbands(world with { Factions = world.Factions.Select(f => f.Id == "winner" ? f with { Human = true } : f).ToArray() }), "AI never disbands player units");
        var commanded = world with { Commanders = [new() { Id = "general", Owner = "winner" }],
            Units = world.Units.Select(u => u.Owner != "winner" ? u : u.Id == "rear.59" ? u with { CommanderId = "general" } : u with { Objective = true }).ToArray() };
        Check(!Disbands(commanded), "commander units are never emergency candidates");
        foreach (string protection in new[] { "morale", "objective", "acted", "fort", "hold", "logistics", "zero-upkeep", "construction", "building", "cell-objective", "front" })
        {
            var guarded = world with { Units = world.Units.Select(u => u.Owner != "winner" ? u : protection switch {
                "morale" => u with { Morale = new(), Combatant = u.Combatant with { Morale = 0 } },
                "objective" => u with { Objective = true }, "acted" => u with { Acted = true },
                "fort" => u with { Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = "fort" } } },
                "hold" => u with { Behavior = "3" }, "logistics" => u with { Support = u.Support with { Logistics = true } },
                "zero-upkeep" => u with { Support = u.Support with { FoodConsumption = 0 } },
                "construction" => u with { Combatant = u.Combatant with { ConstructionTurns = 1 } }, _ => u
            }).ToArray(), Cells = world.Cells.Select((c, i) => protection switch {
                "building" when i >= 50 => c with { BuildingKind = "port", BuildingLevel = 1 },
                "cell-objective" when i >= 50 => c with { Objective = true },
                "front" when i >= 80 => c with { Owner = "loser" }, _ => c
            }).ToArray() };
            Check(!Disbands(guarded), "protected " + protection);
        }
        var recruitPolicy = production with { Groups = [new("emergency", [offer.Id])], Tasks = [new("recruit", 1)] };
        var refilled = result with { ArmyCapacity = new() { BaseCapacity = 10 }, Factions = result.Factions.Select(f => f with { Shortage = 0 }).ToArray() };
        Check(Plan(refilled, recruitPolicy).Command == null, "no recruitment rebound in disband turn");
        var forged = plan with { Command = new AiDisbandCommand(world.Revision, "rear.50") };
        try { WorldAi.ExecuteProduction(world, forged, production, ai); throw new InvalidOperationException("forged disband accepted"); }
        catch (RuleViolation e) { Check(e.Code == "ai.disband_unavailable", "execution revalidates protected selection"); }
        var step = WorldAi.PlanStep(world, ai, production, turns);
        Check(step.Production.Command is AiDisbandCommand && WorldAi.ExecuteStep(world, step, ai, production, turns, new()).State.Units.Count == result.Units.Count,
            "ordinary AI step executes emergency disband");
        return checks;
    }
}
