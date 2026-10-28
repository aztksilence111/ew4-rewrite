using Ew4.Core;
using Ew4.Persistence;

internal static class CombatantSnapshotChecks
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        void Reject(Action action, string code)
        {
            try { action(); }
            catch (RuleViolation error) { Check(error.Code == code, "Wrong rejection: " + error.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        void ReadOnly(Action action)
        {
            try { action(); } catch (NotSupportedException) { checks++; return; }
            throw new InvalidOperationException("Frozen collection allowed mutation");
        }
        var fixture = WorldChecks.Example();
        var skills = new[] { "custom" };
        var slots = new[] { new SkillSlot("custom", "template") };
        var statuses = new[] { new SkillStatus("custom", 2, SourceOwner: fixture.Factions[0].Id) };
        var goals = new[] { 1, 2 };
        var actor = fixture.Units[0] with { Combatant = fixture.Units[0].Combatant with
            { Skills = skills, SkillSlots = slots, Statuses = statuses },
            PlayerControl = new(fixture.Units[0].Owner, Goal: 1, Goals: goals) };
        var source = fixture with { Units = [actor, fixture.Units[1]] };
        source.Validate();
        var snapshot = source.Copy(); snapshot.Validate();
        var frozen = snapshot.Units[0].Combatant;
        byte[] before = WorldSave.Encode(snapshot);
        Check(before.SequenceEqual(WorldSave.Encode(source)), "Snapshot serialization changed");
        var second = snapshot.Copy();
        Check(ReferenceEquals(frozen, second.Units[0].Combatant), "Combatant copied twice");
        Check(ReferenceEquals(snapshot.Units[1], second.Units[1]), "Immutable unit copied twice");
        Check(!ReferenceEquals(snapshot.Units, second.Units), "Unit list shared");
        skills[0] = "changed"; slots[0] = new("custom", "invalid/slot");
        statuses[0] = new("custom", 0); goals[0] = 7;
        Check(before.SequenceEqual(WorldSave.Encode(snapshot)), "Caller mutation leaked into snapshot");
        second.Units[0].PlayerControl!.Goals![0] = 6;
        Check(snapshot.Units[0].PlayerControl!.Goals![0] == 1, "Control goals shared between copies");
        ReadOnly(() => ((IList<string>)frozen.Skills)[0] = "changed");
        ReadOnly(() => ((IList<SkillSlot>)frozen.SkillSlots)[0] = slots[0]);
        ReadOnly(() => ((IList<SkillStatus>)frozen.Statuses)[0] = statuses[0]);
        Reject(() => (frozen with { Health = 0 }).Validate(), "combatant.invalid");
        Reject(() => (frozen with { Stars = -1 }).Validate(), "combatant.invalid");
        Reject(() => (frozen with { Statuses = statuses }).Validate(), "combatant.status");
        Reject(() => (frozen with { SkillSlots = slots }).Validate(), "extension.slot");
        var changed = FrozenCombatants.Copy(frozen with { Health = frozen.Health - 1 }); changed.Validate();
        Check(changed.Health == frozen.Health - 1 && frozen.Health == actor.Combatant.Health,
            "Changed combatant polluted previous snapshot");
        var loaded = WorldSave.Decode(before, snapshot.ContentRevision); loaded.Validate();
        ((IList<SkillStatus>)loaded.Units[0].Combatant.Statuses)[0] = statuses[0];
        Reject(loaded.Validate, "combatant.status");
        loaded = WorldSave.Decode(before, snapshot.ContentRevision); loaded.Validate();
        ((IList<SkillSlot>)loaded.Units[0].Combatant.SkillSlots)[0] = slots[0];
        Reject(loaded.Validate, "extension.slot");
        var invalidOwner = FrozenCombatants.Copy(frozen with { Statuses = [new("custom", 2, SourceOwner: "missing")] });
        invalidOwner.Validate();
        Reject(() => (snapshot with { Units = [snapshot.Units[0] with { Combatant = invalidOwner }, snapshot.Units[1]] }).Validate(), "world.unit");
        Reject(() => (snapshot with { Units = [snapshot.Units[0] with { Owner = "missing" }, snapshot.Units[1]] }).Validate(), "world.unit");
        Reject(() => (snapshot with { Units = [snapshot.Units[0] with { Cell = 100 }, snapshot.Units[1]] }).Validate(), "world.unit");
        var baseSkills = new[] { "base" };
        var commanderSource = fixture with { Units = [fixture.Units[0] with {
            CommanderBase = new(fixture.Units[0].Combatant with { Skills = baseSkills }, new()) }, fixture.Units[1]] };
        var commanderCopy = commanderSource.Copy();
        baseSkills[0] = "changed";
        Check(commanderCopy.Units[0].CommanderBase!.Combatant.Skills.Single() == "base", "Commander base shared caller array");
        Check(ReferenceEquals(commanderCopy.Units[0].CommanderBase, commanderCopy.Copy().Units[0].CommanderBase), "Commander base copied twice");
        Check(before.SequenceEqual(WorldSave.Encode(WorldSave.Decode(before, snapshot.ContentRevision).Copy())), "Save roundtrip changed bytes");
        return checks;
    }
}
