using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

internal static class ValidationReuseChecks
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
        var fixture = WorldChecks.Example();
        var bands = new[] { new AttackBand(0, 5) };
        var slots = new[] { new SkillSlot("skill", "template") };
        var traits = new[] { "trait" };
        var overrides = new[] { "override" };
        var multipliers = new Dictionary<string, float> { ["infantry"] = 1 };
        var sourceProfile = fixture.Units[0].Combatant.Profile with
        { Grade = "0", AttackBands = bands, SkillSlots = slots, Traits = traits, LegacySkillOverrides = overrides, DamageMultipliers = multipliers };
        var source = fixture with { Units = fixture.Units.Select(u => u with
        { Combatant = u.Combatant with { Profile = sourceProfile } }).ToArray() };
        source.Validate();
        var snapshot = source.Copy();
        snapshot.Validate();
        byte[] before = WorldSave.Encode(snapshot);
        var frozen = snapshot.Units[0].Combatant.Profile;
        Check(JsonSerializer.Serialize(source) == JsonSerializer.Serialize(snapshot), "Freezing changed serialized content");
        Check(ReferenceEquals(frozen, snapshot.Copy().Units[0].Combatant.Profile), "Frozen template was copied again");
        bands[0] = new(0, 0); slots[0] = new("skill", "invalid/slot");
        traits[0] = "changed"; overrides[0] = "changed"; multipliers["infantry"] = -1;
        Check(before.SequenceEqual(WorldSave.Encode(snapshot)), "Caller arrays or dictionary leaked into snapshot");
        Reject(source.Validate, "extension.slot");
        Reject(() => (frozen with { MaxHealth = 0 }).Validate(), "profile.invalid");
        Reject(() => (frozen with { AttackBands = bands }).Validate(), "profile.invalid");
        Reject(() => (frozen with { SkillSlots = slots }).Validate(), "extension.slot");
        Reject(() => (frozen with { DamageMultipliers = multipliers }).Validate(), "profile.multiplier");
        Check(((IList<AttackBand>)frozen.AttackBands).IsReadOnly
            && ((IList<SkillSlot>)frozen.SkillSlots).IsReadOnly
            && ((IList<string>)frozen.Traits).IsReadOnly
            && ((IList<string>)frozen.LegacySkillOverrides).IsReadOnly
            && ((IDictionary<string, float>)frozen.DamageMultipliers).IsReadOnly, "Frozen collections expose writes");
        var loaded = WorldSave.Decode(before, snapshot.ContentRevision);
        loaded.Validate();
        ((IList<AttackBand>)loaded.Units[0].Combatant.Profile.AttackBands)[0] = new(0, 0);
        Reject(loaded.Validate, "profile.invalid");
        var mutableBands = new[] { new AttackBand(0, 5) };
        var mutable = sourceProfile with { SkillSlots = [], AttackBands = mutableBands, DamageMultipliers = new Dictionary<string, float>() };
        mutable.Validate(); mutableBands[0] = new(0, 0);
        Reject(mutable.Validate, "profile.invalid");

        var rules = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        WorldAi.PlanUnit(snapshot, "unit.2", rules);
        var units = (UnitState[])snapshot.Units;
        var valid = units[1];
        units[1] = valid with { Cell = 100 };
        Reject(() => WorldAi.PlanUnit(snapshot, valid.Id, rules), "world.unit");
        units[1] = valid with { Id = units[0].Id };
        Reject(() => WorldAi.PlanUnit(snapshot, valid.Id, rules), "world.identity");
        units[1] = valid with { Cell = units[0].Cell };
        Reject(() => WorldAi.PlanUnit(snapshot, valid.Id, rules), "world.capacity");
        units[1] = valid with { CommanderId = "missing" };
        Reject(() => WorldAi.PlanUnit(snapshot, valid.Id, rules), "world.unit");
        units[1] = valid;
        Check(before.SequenceEqual(WorldSave.Encode(snapshot)), "Planning or rejection polluted the snapshot");
        var disabled = rules with { Movement = rules.Movement with { Extensions = new RuleEventBus(
            new Dictionary<string, ExtensionSkill> { ["unused"] = new() { Id = "unused" } }) } };
        for (int throws = 0; throws < 2; throws++)
        {
            using (PlanningScope.Begin(rules))
            {
                snapshot.Validate();
                try
                {
                    using var inner = PlanningScope.Begin(disabled);
                    using var descendant = PlanningScope.Begin(rules);
                    int calls = 0;
                    var token = new object();
                    PlanningScope.Validate(token, () => calls++);
                    PlanningScope.Validate(token, () => calls++);
                    Check(calls == 2, "Disabled scope did not propagate to descendants");
                    units[1] = valid with { Cell = 100 };
                    if (throws != 0) throw new InvalidOperationException("callback");
                }
                catch (InvalidOperationException error) when (error.Message == "callback") { }
                Reject(snapshot.Validate, "world.unit");
                units[1] = valid;
                snapshot.Validate();
                Reject(() => (snapshot with { Revision = -1 }).Validate(), "world.header");
                Check(!ReferenceEquals(snapshot, PlanningScope.WithAiRandomState(snapshot, 1)), "RNG wrapper missing");
                int wrappedChecks = 0;
                PlanningScope.Validate(PlanningScope.WithAiRandomState(snapshot, 1), () => wrappedChecks++);
                Check(wrappedChecks == 0, "Internal RNG-only wrapper lost proof");
                int clonedChecks = 0;
                PlanningScope.Validate(snapshot with { }, () => clonedChecks++);
                Check(clonedChecks == 1, "Ordinary clone inherited proof");
            }
            units[1] = valid with { Cell = 100 };
            Reject(snapshot.Validate, "world.unit");
            units[1] = valid;
        }
        var attackers = new[] { "infantry" };
        var defenders = new[] { "cavalry" };
        var excluded = new[] { "naval" };
        var modifiers = new[] { new AiAttackModifier { AttackerCategories = attackers,
            DefenderCategories = defenders, ExcludedDefenderCategories = excluded } };
        var buildings = new Dictionary<string, AiBuildingValue> { ["city"] = new(10, 2) };
        var installations = new Dictionary<string, long> { ["trench"] = 1 };
        var priorities = new Dictionary<int, long> { [1] = 10 };
        var grades = new Dictionary<string, float> { ["0"] = 1 };
        var mutableRules = new AiTacticalRules { Buildings = buildings, Installations = installations,
            Priorities = priorities, Grades = grades, Modifiers = modifiers };
        var frozenRules = FrozenAiTacticalRules.Copy(mutableRules);
        string encodedRules = JsonSerializer.Serialize(frozenRules);
        Check(encodedRules == JsonSerializer.Serialize(mutableRules), "Frozen scoring content changed");
        Check(ReferenceEquals(frozenRules, FrozenAiTacticalRules.Copy(frozenRules)), "Frozen scoring copied twice");
        Check(!FrozenAiTacticalRules.IsValidated(frozenRules with { }), "Scoring clone inherited certificate");
        Check(((IDictionary<string, AiBuildingValue>)frozenRules.Buildings).IsReadOnly
            && ((IDictionary<string, long>)frozenRules.Installations).IsReadOnly
            && ((IDictionary<int, long>)frozenRules.Priorities).IsReadOnly
            && ((IDictionary<string, float>)frozenRules.Grades).IsReadOnly
            && ((IList<AiAttackModifier>)frozenRules.Modifiers).IsReadOnly
            && ((IList<string>)frozenRules.Modifiers[0].AttackerCategories).IsReadOnly
            && ((IList<string>)frozenRules.Modifiers[0].DefenderCategories).IsReadOnly
            && ((IList<string>)frozenRules.Modifiers[0].ExcludedDefenderCategories).IsReadOnly, "Scoring collections expose writes");
        attackers[0] = ""; defenders[0] = ""; excluded[0] = "";
        buildings["city"] = new(-1, 0); installations["trench"] = -1;
        priorities[1] = -1; grades["0"] = float.NaN; modifiers[0] = new() { TargetInstallation = "" };
        frozenRules.Validate();
        Check(encodedRules == JsonSerializer.Serialize(frozenRules), "Caller mutation changed frozen scoring");
        Reject(mutableRules.Validate, "ai.configuration");
        Reject(() => (frozenRules with { NavalStrengthMultiplier = -1 }).Validate(), "ai.configuration");
        Reject(() => (frozenRules with { Modifiers = modifiers }).Validate(), "ai.configuration");
        Reject(() => FrozenAiTacticalRules.Copy(mutableRules), "ai.configuration");
        var reloadedRules = JsonSerializer.Deserialize<AiTacticalRules>(encodedRules)!;
        Check(!FrozenAiTacticalRules.IsValidated(reloadedRules), "Deserialization inherited certificate");
        ((IDictionary<string, float>)reloadedRules.Grades)["0"] = -1;
        Reject(reloadedRules.Validate, "ai.configuration");
        var nextRules = FrozenAiTacticalRules.Copy(frozenRules with { Grades = new Dictionary<string, float> { ["0"] = 3 } });
        Check(nextRules.Grades["0"] == 3 && frozenRules.Grades["0"] == 1, "Rule reload changed an old snapshot");
        return checks;
    }
}
