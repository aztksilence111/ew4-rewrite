using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class SupportActionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label)
        {
            checks++;
            if (!value) throw new InvalidOperationException(label);
        }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var fixture = WorldChecks.Example();
        string[] categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
        using var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/support_action_vectors.json")));
        foreach (var v in data.RootElement.EnumerateArray())
        {
            int Int(string name) => v.GetProperty(name).GetInt32();
            bool Flag(string name) => v.GetProperty(name).GetBoolean();
            int identity = Int("identity"), skill = identity switch { 41 => 27, 42 => 28, 43 => 25, _ => 26 };
            var commander = new CommanderRuntime { Id = "builder", Owner = "winner" };
            var unit = fixture.Units[1] with
            {
                CommanderId = Flag("commander") ? commander.Id : null, RemainingMovement = Int("movement"), Acted = Flag("acted"), Transport = Flag("transport"),
                Morale = new(Int("morale"), 0, Int("morale")), Combatant = fixture.Units[1].Combatant with
                {
                    Morale = Int("morale"), ConstructionTurns = Int("construction"), Skills = Flag("skilled") ? [Ew4Combat.Skill(skill)] : [],
                    Profile = fixture.Units[1].Combatant.Profile with { Category = categories[Int("category")] }
                }
            };
            var state = fixture with
            {
                Units = Flag("occupied") ? [unit] : [], Commanders = [commander],
                Factions = fixture.Factions.Select(f => f.Id != "winner" ? f : f with { Resources = new(Int("money"), Int("industry"), 123) }).ToArray(),
                Cells = fixture.Cells.Select((c, i) => i != unit.Cell ? c : c with { Owner = Flag("own") ? "winner" : "loser",
                    Terrain = c.Terrain with { Active = Flag("active") }, BuildingKind = Flag("built") ? "city" : null, BuildingLevel = Flag("built") ? 1 : 0,
                    InstallationKind = Flag("installed") ? "9" : null }).ToArray()
            };
            var price = new UnitPurchaseRules { Cost = new(Int("money_cost"), Int("industry_cost"), 0), DiscountSkill = Ew4Combat.Skill(skill) };
            // Native probes may contain an occupant on an inactive cell; the modern snapshot rejects that before acting.
            byte[] before = JsonSerializer.SerializeToUtf8Bytes(state);
            var expected = v.GetProperty("expected");
            WorldChange? result = null;
            try
            {
                // These vectors document the old purchase predicate, including its fort loophole.
                result = identity == 46 ? WorldSupportActions.BuyTransport(state, state.Revision, unit.Id, new() { Purchase = price, ExcludedCategories = ["naval"] })
                    : WorldSupportActions.BuildInstallation(state, state.Revision, unit.Id, new() { Kind = (identity-41).ToString(), Purchase = price });
            }
            catch (RuleViolation e)
            {
                Check(!expected.GetProperty("success").GetBoolean() && e.Code is "world.unit" or "purchase.unit" or "purchase.site" or "purchase.resources"
                    or "installation.unavailable" or "transport.unavailable", "native support rejection: " + e.Code);
            }
            Check((result != null) == expected.GetProperty("success").GetBoolean(), "native support action success");
            Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(state)), "support action leaves input unchanged");
            if (result == null) continue;
            var changed = result.State.Units.Single();
            Check(changed.RemainingMovement == expected.GetProperty("movement").GetInt32() && changed.Acted == expected.GetProperty("acted").GetBoolean(), "native support action/movement flags");
            Check(changed.Transport == expected.GetProperty("transport").GetBoolean(), "native transport flag");
            var installation = expected.GetProperty("installation");
            Check(result.State.Cells[unit.Cell].InstallationKind == (installation.ValueKind == JsonValueKind.Null ? null : installation.GetInt32().ToString()), "native installation state");
            Check(result.State.Factions.Single(f => f.Id == "winner").Resources == new Resources(expected.GetProperty("money").GetInt64(), expected.GetProperty("industry").GetInt64(), 123), "native support spending/discount");
            Check(result.State.Revision == state.Revision + 1 && result.State.RandomState == state.RandomState && result.State.AiRandomState == state.AiRandomState, "support commits once without RNG");
            Check(WorldSave.Encode(result.State).SequenceEqual(WorldSave.Encode(WorldSave.Decode(WorldSave.Encode(result.State), state.ContentRevision))), "support state survives save/load");
        }
        var actor = fixture.Units[1] with { Cell = 4, RemainingMovement = 6 };
        var sea = fixture with { Units = [actor], Cells = fixture.Cells.Select((c, i) => c with { Owner = "winner", Terrain = c.Terrain with { Sea = i == 5 } }).ToArray(),
            Factions = fixture.Factions.Select(f => f with { Resources = new(1000, 1000, 1000) }).ToArray() };
        Reject(() => WorldActions.Move(sea, sea.Revision, actor.Id, 5), "move.unreachable");
        var transported = WorldSupportActions.BuyTransport(sea, sea.Revision, actor.Id, new() { Purchase = new() { Cost = new(40, 0, 0) } }).State;
        Check(transported.Units.Single().RemainingMovement == 6 && !transported.Units.Single().Acted, "transport purchase preserves action budget");
        var sailed = WorldActions.Move(transported, transported.Revision, actor.Id, 5).State;
        Check(sailed.Units.Single().Cell == 5 && sailed.Units.Single().Combatant.AtSea, "purchased transport enables actual sea entry");
        Reject(() => WorldSupportActions.BuyTransport(transported, transported.Revision, actor.Id, new()), "transport.unavailable");
        Reject(() => WorldSupportActions.BuyTransport(transported, sea.Revision, actor.Id, new()), "action.stale");
        var used = sea with { Units = [actor with { Acted = true, RemainingMovement = 0, Morale = new(-3, 0, -3), Combatant = actor.Combatant with { Morale = -3 } }] };
        var installationRules = new InstallationBuildRules { Kind = "custom.trench", Purchase = new() { Cost = new(30, 10, 0) } };
        var transportRules = new TransportPurchaseRules { Purchase = new() { Cost = new(40, 10, 5) } };
        var foreign = sea with { Cells = sea.Cells.Select((c, i) => i == actor.Cell ? c with { Owner = "loser" } : c).ToArray() };
        var sameCamp = foreign with { Factions = foreign.Factions.Select(f => f with { Camp = "b" }).ToArray() };
        foreach (var friendly in new[] { sameCamp, WorldDiplomacy.Set(foreign, "winner", "loser", "allied") })
        {
            byte[] before = WorldSave.Encode(friendly);
            Check(WorldSupportActions.CanBuyTransport(friendly, actor.Id, transportRules), "transport offered on allied territory");
            Check(WorldSupportActions.TransportCost(friendly, actor.Id, transportRules) == transportRules.Purchase.Cost, "allied transport quote");
            var bought = WorldSupportActions.BuyTransport(friendly, friendly.Revision, actor.Id, transportRules).State;
            Check(bought.Units.Single().Transport && bought.Units.Single().RemainingMovement == actor.RemainingMovement,
                "allied transport purchase preserves movement");
            Check(bought.Cells[actor.Cell].Owner == "loser" && bought.Factions.Single(f => f.Id == "loser").Resources == new Resources(1000, 1000, 1000)
                && bought.Factions.Single(f => f.Id == "winner").Resources == new Resources(960, 990, 995), "allied transport charges only unit owner without capturing land");
            Check(before.SequenceEqual(WorldSave.Encode(friendly)), "allied transport leaves input unchanged");
            Check(WorldSave.Decode(WorldSave.Encode(bought), bought.ContentRevision).Units.Single().Transport, "allied transport survives save/load");
            Check(!WorldSupportActions.CanBuildInstallation(friendly, actor.Id, installationRules), "alliance does not allow installation construction");
            Reject(() => WorldSupportActions.BuildInstallation(friendly, friendly.Revision, actor.Id, installationRules), "purchase.site");
            var poorAlly = friendly with { Factions = friendly.Factions.Select(f => f.Id == "winner" ? f with { Resources = new(39, 10, 5) } : f).ToArray() };
            Check(!WorldSupportActions.CanBuyTransport(poorAlly, actor.Id, transportRules), "allied transport still requires funds");
            Reject(() => WorldSupportActions.BuyTransport(poorAlly, poorAlly.Revision, actor.Id, transportRules), "purchase.resources");
        }
        foreach (var blocked in new[] { foreign, WorldDiplomacy.Set(sameCamp, "winner", "loser", "neutral"),
            WorldDiplomacy.Set(sameCamp, "winner", "loser", "hostile"),
            foreign with { Cells = foreign.Cells.Select((c, i) => i == actor.Cell ? c with { Owner = null } : c).ToArray() } })
        {
            Check(!WorldSupportActions.CanBuyTransport(blocked, actor.Id, transportRules), "transport unavailable outside own or allied territory");
            Reject(() => WorldSupportActions.TransportCost(blocked, actor.Id, transportRules), "purchase.site");
            Reject(() => WorldSupportActions.BuyTransport(blocked, blocked.Revision, actor.Id, transportRules), "purchase.site");
        }
        var built = WorldSupportActions.BuildInstallation(used, used.Revision, actor.Id, installationRules).State;
        Check(built.Cells[4].InstallationKind == "custom.trench" && built.Units.Single().Acted, "native stationary builder branch does not require unused attack or positive morale");
        var context = new CombatContextRules { InstallationDefense = new Dictionary<string, IReadOnlyDictionary<string, int>>
            { ["custom.trench"] = new Dictionary<string, int> { ["infantry"] = 45 } } };
        Check(CombatContext.Resolve(built, built.Units.Single(), "infantry", context).Evasion == .45f, "newly built installation participates in map defense");
        Reject(() => WorldSupportActions.BuildInstallation(built, built.Revision, actor.Id, installationRules), "installation.unavailable");
        var custom = sea with { Units = [actor with { Combatant = actor.Combatant with { Profile = actor.Combatant.Profile with { Category = "engineer" } } }] };
        Check(WorldSupportActions.BuildInstallation(custom, custom.Revision, actor.Id, installationRules with { Builders = ["engineer"] }).State.Cells[4].InstallationKind == "custom.trench",
            "dynamic builder category and installation ID");
        Reject(() => WorldSupportActions.BuildInstallation(custom, custom.Revision, actor.Id, installationRules), "installation.unavailable");
        Check(WorldSupportActions.BuyTransport(custom, custom.Revision, actor.Id, new() { ExcludedCategories = [] }).State.Units.Single().Transport,
            "configurable transport category policy");
        var seaActor = sea with { Units = [actor with { Cell = 5 }] };
        Reject(() => WorldSupportActions.BuyTransport(seaActor, seaActor.Revision, actor.Id, new() { Purchase = new() { AllowSea = false } }), "purchase.site");
        var fort = sea with { Units = [actor with { RemainingMovement = 0, Combatant = actor.Combatant with { Profile = actor.Combatant.Profile with { Category = "fort", Movement = 0 } } }] };
        Check(!WorldSupportActions.CanBuyTransport(fort, actor.Id, new()), "fixed fort has no transport offer");
        Reject(() => WorldSupportActions.BuyTransport(fort, fort.Revision, actor.Id, new()), "transport.unavailable");
        return checks;
    }
}
