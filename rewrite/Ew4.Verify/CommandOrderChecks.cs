using Ew4.Core;
using Ew4.Persistence;

internal static class CommandOrderChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool yes, string message) { checks++; if (!yes) throw new Exception("Command orders: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (RuleViolation) { checks++; return; }
            throw new Exception("Accepted invalid order: " + message);
        }
        var fixture = WorldChecks.Example();
        var profile = fixture.Units[0].Combatant.Profile with { MaxHealth = 200, Movement = 6, MaximumRange = 2,
            Category = "infantry", MinimumAttack = 10, MaximumAttack = 10 };
        UnitState U(string id, int cell, int hp = 200, string owner = "winner") => new() {
            Id = id, Owner = owner, Cell = cell, RemainingMovement = 6, Combatant = new() { Profile = profile, Health = hp } };
        var heal = new ExtensionSkill { Id = "test.heal", Kind = "active", EndsAction = false, Cooldown = 4, Order = new() {
            Mode = "heal", Recipients = new() { Radius = 2, Count = 2, Conditions = new[] { "damaged", "mobile" } }, Bonus = new() { Heal = 45, HealFraction = .3 } } };
        var swap = new ExtensionSkill { Id = "test.swap", Kind = "active", EndsAction = false, Cooldown = 3, Order = new() {
            Mode = "swap", Recipients = new() { Radius = 1, Count = 1, Other = true }, IncludesSelf = true } };
        var again = new ExtensionSkill { Id = "test.again", Kind = "active", EndsAction = false, Cooldown = 3, Order = new() {
            Mode = "reactivate", Recipients = new() { Radius = 2 } } };
        var recovery = new ExtensionSkill { Id = "test.recovery", Order = new() { Event = "start", Recipients = new() {
            Radius = 2, Count = 1, Other = true, Conditions = new[] { "damaged" } }, Bonus = new() { Heal = 8 } } };
        var definitions = new[] { heal, swap, again, recovery };
        foreach (var skill in definitions) CommandOrders.Validate(skill, 3);
        Reject(() => CommandOrders.Validate(heal, 2), "API 2");
        Reject(() => CommandOrders.Validate(heal with { Order = heal.Order! with { Bonus = new() { HealFraction = double.NaN } } }, 3), "nonfinite fraction");
        Reject(() => CommandOrders.Validate(heal with { Order = heal.Order! with { Mode = "not_a_mechanic" } }, 3), "unknown mode");
        var bus = new RuleEventBus(definitions.ToDictionary(s => s.Id));
        var source = U("source", 40) with { Combatant = U("source", 40).Combatant with {
            SkillSlots = definitions.Select((s, i) => new SkillSlot(s.Id, "slot." + i)).ToArray() } };
        var world = fixture with { Width = 9, Height = 9, ActiveFaction = "winner", Round = 1,
            Cells = Enumerable.Range(0, 81).Select(_ => new CellState { Owner = "winner" }).ToArray(),
            Units = new[] { source, U("one", 39, 50), U("two", 31, 190), U("enemy", 42, 100, "loser") },
            Factions = fixture.Factions.Select(f => f with { Resources = new(0, 0, 0) }).ToArray() };
        var movement = new WorldMoveRules { Extensions = bus };
        UnitState Get(WorldState w, string id) => w.Units.Single(u => u.Id == id);
        WorldState Cast(WorldState w, int slot, params string[] selected) => WorldSkillActions.Execute(w, w.Revision,
            source.Id, "slot." + slot, null, bus, movement, targets: selected).State;
        var bytes = WorldSave.Encode(world);
        var healed = Cast(world, 0, "one", "two");
        Check(Get(healed, "one").Combatant.Health == 110 && Get(healed, "two").Combatant.Health == 200, "larger of flat/fraction, cap at missing HP");
        Check(healed.Factions.All(f => f.Resources == new Resources(0, 0, 0)), "nonsummon zero-resource activation");
        Check(!Get(healed, "source").Acted && healed.ExtensionState["source/slot.0/ready"] == 5, "no ordinary action spent; N+C cooldown");
        Reject(() => Cast(healed, 0, "one"), "cooldown");
        Reject(() => Cast(world, 0, "one", "one"), "duplicate selection");
        Reject(() => Cast(world, 0, "enemy"), "enemy recovery");
        Reject(() => Cast(world, 0), "empty recovery");
        Check(WorldSave.Encode(world).SequenceEqual(bytes), "failed actions are atomic and input remains immutable");
        var spent = world with { Units = world.Units.Select(u => u.Id == "source" ? u with {
            Acted = true, RemainingMovement = 0, MovedThisTurn = true, AttackedThisTurn = true } : u).ToArray() };
        var swapped = Cast(spent, 1, "one");
        Check(Get(swapped, "source").Cell == 39 && Get(swapped, "one").Cell == 40, "atomic adjacent swap after all actions");
        Check(Get(swapped, "source").Acted && Get(swapped, "source").RemainingMovement == 0
            && Get(swapped, "source").MovedThisTurn && Get(swapped, "source").AttackedThisTurn, "swap preserves each action history");
        Reject(() => Cast(world with { Units = world.Units.Select(u => u.Id == "two" ? u with { Cell = 0 } : u).ToArray() }, 1, "two"), "not adjacent to source");
        var barrier = world with { Cells = world.Cells.Select((c, i) => i == 39 ? c with { Terrain = c.Terrain with { Barrier = TerrainBarrier.Mountain } } : c).ToArray() };
        Reject(() => Cast(barrier, 1, "one"), "swap validates both destination terrains");
        Reject(() => Cast(world, 2, "one"), "fresh unit cannot receive full reactivation");
        var recipientSpent = world with { Units = world.Units.Select(u => u.Id == "one" ? u with { MovedThisTurn = true, Acted = true, RemainingMovement = 0 } : u).ToArray() };
        var reactivated = Cast(recipientSpent, 2, "one");
        Check(!Get(reactivated, "one").Acted && Get(reactivated, "one").RemainingMovement == 6 && Get(reactivated, "one").MovedThisTurn,
            "full movement and attack restored without erasing history");
        var otherAgain = again with { Id = "test.again.other" };
        var otherBus = new RuleEventBus(new Dictionary<string, ExtensionSkill> { [otherAgain.Id] = otherAgain });
        var caster = Get(reactivated, "source") with { Combatant = source.Combatant with { SkillSlots = new[] { new SkillSlot(otherAgain.Id, "other") } } };
        var second = reactivated with { Units = reactivated.Units.Select(u => u.Id == caster.Id ? caster : u).ToArray() };
        Reject(() => WorldSkillActions.Execute(second, second.Revision, caster.Id, "other", null, otherBus, movement,
            targets: new[] { "one" }), "different skill cannot grant a second full reactivation");
        var restored = WorldSave.Decode(WorldSave.Encode(reactivated), reactivated.ContentRevision);
        Check(WorldSave.Encode(restored).SequenceEqual(WorldSave.Encode(reactivated)), "orders/cooldown/history roundtrip");
        var started = CommandOrders.StartTurn(world, "winner", bus);
        Check(Get(started, "one").Combatant.Health == 58 && Get(started, "two").Combatant.Health == 190, "weakest-recipient selection from common snapshot");
        Check(WorldSave.Encode(CommandOrders.StartTurn(started, "winner", bus)).SequenceEqual(WorldSave.Encode(started)), "turn-start reload cannot heal twice");
        var noBus = new RuleEventBus(new Dictionary<string, ExtensionSkill>());
        Check(ReferenceEquals(world, CommandOrders.StartTurn(world, "winner", noBus))
            && ReferenceEquals(world, CommandOrders.EndTurn(world, "winner", noBus)), "no configuration leaves vanilla state unchanged");
        return checks;
    }
}
