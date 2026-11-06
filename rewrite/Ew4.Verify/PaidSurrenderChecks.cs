using Ew4.Core;
using Ew4.Persistence;

internal static class PaidSurrenderChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code); return; }
            throw new Exception("Expected " + code);
        }
        var world = WorldChecks.Example();
        world = world with { Cells = world.Cells.Select((c, i) => i == 0 ? c with { BuildingKind = "city", BuildingLevel = 2 } : c).ToArray(),
            Units = world.Units.Append(world.Units[1] with { Id = "extra", Cell = 7 }).ToArray(),
            Factions = world.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(451,1024,200) } : f)
                .Concat(Enumerable.Range(1,5).Select(i => new FactionState { Id = "ally"+i, Name = "ally"+i, Camp = "b" })).ToArray(),
            Commanders = [new() { Id = "leader", Owner = "loser", Withdrawn = true }], Demands = new() };
        world = DemandDiplomacy.Initialize(world);
        world = world with { Demands = world.Demands! with { Leaders = new Dictionary<string,string> { ["loser"] = "leader" } } };
        var offer = new RecruitmentOffer { Id = "unit", Template = world.Units[0].Combatant, Cost = new(550,180,50) };
        var quote = PaidSurrender.Quote(world, "winner", "loser", [offer]);
        Check(quote.BaseCost == new Resources(550,180,0) && quote.Cost == new Resources(215,70,0), "reference 550/180 times 39 percent rounds to 215/70");
        Check(quote.TotalPercent == -61 && quote.BuildingLevels == 2 && quote.Blocked == null, "reference modifier breakdown");
        var result = PaidSurrender.Demand(world, world.Revision, "winner", "loser", [offer]).State;
        Check(result.Factions.Single(f => f.Id == "winner").Resources == new Resources(236,954,200), "pay money and industry only");
        Check(result.Factions.Single(f => f.Id == "loser").Defeated && result.Units.All(u => u.Owner != "loser"), "surrender removes all target units");
        Check(result.Cells.All(c => c.Owner != "loser") && result.Cells[0].Owner == "winner" && result.Cells[1].Owner == null, "transfer facilities and neutralize undeveloped land");
        Check(result.Revision == world.Revision + 1 && result.RandomState == world.RandomState, "single revision with no probability draw");
        Reject(() => PaidSurrender.Demand(world, world.Revision - 1, "winner", "loser", [offer]), "world.stale");
        Reject(() => PaidSurrender.Demand(world, world.Revision, "winner", "loser", []), "diplomacy.unpriced");
        var poor = world with { Factions = world.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(0,0,0) } : f).ToArray() };
        Reject(() => PaidSurrender.Demand(poor, poor.Revision, "winner", "loser", [offer]), "diplomacy.resources");
        Check(poor.Factions[0].Defeated == false && poor.Units.Count == 3, "failure leaves input untouched");
        var locked = world with { Demands = world.Demands! with { Rules = new() { LockedFactions = ["loser"] } } };
        Reject(() => PaidSurrender.Demand(locked, locked.Revision, "winner", "loser", [offer]), "diplomacy.protected");
        var free = world with { Demands = world.Demands! with { Rules = new() { Purchase = new() { CommanderReduction = 100 } } } };
        Check(PaidSurrender.Quote(free, "winner", "loser", [offer]).Cost == new Resources(0,0,0), "no negative prices");
        var peace = WorldDiplomacy.Set(world, "winner", "loser", "allied");
        Reject(() => PaidSurrender.Demand(peace, peace.Revision, "winner", "loser", [offer]), "diplomacy.peace");
        Check(quote.LegionLead==5&&quote.LegionPercent==-25,"five additional allied factions gives capped 25 percent reduction");
        var noLeader=world with { Demands=world.Demands! with { Leaders=new Dictionary<string,string>() } };
        Check(PaidSurrender.Quote(noLeader,"winner","loser",[offer]).CommanderPercent==0,"no starting commander is not a commander death");
        var manyBuildings=world with { Cells=world.Cells.Select((c,i)=>i<5?c with { BuildingKind="farmland",BuildingLevel=6 }:c).ToArray() };
        var capped=PaidSurrender.Quote(manyBuildings,"winner","loser",[offer]);
        Check(capped.BuildingLevels==30&&capped.BuildingPercent==50,"all facility kinds count with 50 percent cap");
        var noUnits=world with { Units=world.Units.Where(u=>u.Owner!="loser").ToArray() };
        Reject(()=>PaidSurrender.Demand(noUnits,noUnits.Revision,"winner","loser",[offer]),"diplomacy.no_units");
        var candidates=world with { Demands=new(), Commanders=[new() { Id="junior",Owner="loser",Rank=new(1,0) },new() { Id="senior",Owner="loser",Rank=new(3,0) }],
            Units=world.Units.Select(u=>u.Owner=="loser"?u with { CommanderId="junior" }:u)
                .Append(world.Units[0] with { Id="second",Cell=1,CommanderId="senior" }).ToArray() };
        candidates=DemandDiplomacy.Initialize(candidates);
        Check(candidates.Demands!.Leaders!["loser"]=="senior","highest starting rank chooses the representative commander");
        candidates=candidates with { Units=candidates.Units.Where(u=>u.CommanderId!="senior").ToArray(),
            Commanders=candidates.Commanders.Select(c=>c.Id=="senior"?c with { Withdrawn=true }:c).ToArray() };
        Check(PaidSurrender.Quote(candidates,"winner","loser",[offer]).CommanderPercent==-25,"representative death discounts even while a junior survives");
        var restored=WorldSave.Decode(WorldSave.Encode(candidates),candidates.ContentRevision);
        Check(PaidSurrender.Quote(restored,"winner","loser",[offer]).CommanderPercent==-25,"starting representative remains stable after save and load");
        return checks;
    }
}
