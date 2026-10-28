using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ExtensionPlanningChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        var overrides = new Dictionary<string, byte[]>();
        bool absent = false;
        byte[] Read(string path)
        {
            if (absent && path == "rewrite/extension.json") throw new FileNotFoundException(path);
            return overrides.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(Path.Combine(root, "rewrite/data",
                path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        }
        void Put(string path, object value) => overrides[path] = JsonSerializer.SerializeToUtf8Bytes(value, ExtensionLoader.Json);
        void Manifest(object value) { overrides.Clear(); Put("rewrite/extension.json", value); }
        void Scope(string label, bool trusted)
        {
            var content = new ClientContent(Read, ["conquest1.btl"]);
            var started = content.Start("conquest1", "fra");
            var rules = started.Rules;
            var world = started.Session.World;
            Check(PlanningScope.CanTrust(rules.Ai.Movement) == trusted, label + ": movement trust");
            using (PlanningScope.Begin(rules.Ai, rules.Production.Values.First(), rules.Turns, rules.Battle))
            {
                world.Validate();
                var query = PlanningScope.Queries(world);
                Check((query != null) == trusted, label + ": assembled execution scope");
                Check(ReferenceEquals(query, PlanningScope.Queries(world)), label + ": repeated index lookup");
            }
            Check(PlanningScope.Queries(world) == null, label + ": scope leaked");
            if (!absent) Check(content.Extensions.Enabled, label + ": content identity disabled");
        }
        absent = true; Scope("absent", true); absent = false;
        Scope("builtin geography and map colors", true);
        Manifest(new { schema = 1, apiVersion = 3, geography = "rewrite/geography.json" });
        Scope("geography only", true);
        Manifest(new { schema = 1, apiVersion = 3, mapColors = "rewrite/map-colors.json" });
        Scope("presentation only", true);
        Manifest(new { schema = 1, apiVersion = 3, skills = new[] { "rewrite/test-skills.json" } });
        Put("rewrite/test-skills.json", new[] { new ExtensionSkill { Id = "test.passive" } });
        Scope("declarative skill", false);
        Manifest(new { schema = 1, apiVersion = 3, statuses = new[] { "rewrite/test-statuses.json" } });
        Put("rewrite/test-statuses.json", new[] { new ExtensionStatus { Id = "test.status" } });
        Scope("declarative status", false);
        Manifest(new { schema = 1, apiVersion = 3, scripts = new[] { "rewrite/test.lua" } });
        overrides["rewrite/test.lua"] = Encoding.UTF8.GetBytes("function value(ctx) return 1 end");
        Scope("declared script", false);
        Manifest(new { schema = 1, apiVersion = 3, skills = new[] { "rewrite/test-skills.json" } });
        Put("rewrite/test-skills.json", new[] { new ExtensionSkill { Id = "test.script",
            Effects = [new() { Script = "rewrite/test.lua", Function = "value" }] } });
        overrides["rewrite/test.lua"] = Encoding.UTF8.GetBytes("function value(ctx) return 1 end");
        Scope("implicit script dependency", false);
        overrides["rewrite/test.lua"] = Encoding.UTF8.GetBytes("function other(ctx) return 1 end");
        try { ExtensionLoader.Load(Read); throw new InvalidOperationException("Missing handler accepted"); }
        catch (RuleViolation) { checks++; }
        overrides.Remove("rewrite/test.lua");
        try { ExtensionLoader.Load(Read); throw new InvalidOperationException("Missing script accepted"); }
        catch (FileNotFoundException) { checks++; }

        var fixture = WorldChecks.Example().Copy();
        var actor = fixture.Units[0];
        var bus = new RuleEventBus(new Dictionary<string, ExtensionSkill>());
        var ai = new WorldAiRules { Movement = new() { Extensions = bus } };
        byte[] before = WorldSave.Encode(fixture);
        using (PlanningScope.Begin(ai))
        {
            fixture.Validate();
            var ownership = MovementOwnership.Move(fixture, new(fixture.Width, fixture.Height),
                fixture.Units.Where(u => u.Id != actor.Id).Select(u => u.Cell).ToArray(), [actor.Cell, 1], actor.Owner, new());
            var projected = PlanningScope.ProjectMovement(fixture, actor, 1, 0, ownership.Cells, ownership);
            var next = bus.Dispatch(projected, "unit.move_end", actor.Id, new(projected.RandomState));
            Check(ReferenceEquals(next, projected), "Empty dispatch replaced the world");
            Check(PlanningScope.IsMovementProjection(next), "Empty dispatch lost the movement proof");
            Check(PlanningScope.Queries(next) != null, "Empty dispatch lost the validation proof");
        }
        var changedRandom = bus.Dispatch(fixture, "unit.move_end", actor.Id, new(fixture.RandomState + 1));
        Check(!ReferenceEquals(changedRandom, fixture) && changedRandom.RandomState == fixture.RandomState + 1,
            "Empty dispatch lost an incoming random-state change");
        var skill = new ExtensionSkill { Id = "test.money", Effects = [new() { Event = "turn.start", Operation = "money", Value = 7 }] };
        bus = new(new Dictionary<string, ExtensionSkill> { [skill.Id] = skill });
        var bound = fixture with { Units = fixture.Units.Select(u => u.Id != actor.Id ? u : u with {
            Combatant = u.Combatant with { SkillSlots = [new(skill.Id, "money")] } }).ToArray() };
        Check(ReferenceEquals(bus.Dispatch(bound, "unit.move_end", actor.Id, new(bound.RandomState)), bound), "Unmatched effect copied world");
        var effective = bus.Dispatch(bound, "turn.start", actor.Id, new(bound.RandomState));
        Check(!ReferenceEquals(effective, bound) && effective.Factions.Single(f => f.Id == actor.Owner).Resources.Money
            == bound.Factions.Single(f => f.Id == actor.Owner).Resources.Money + 7, "Effective dispatch changed semantics");
        Check(effective.RandomState == bound.RandomState && before.SequenceEqual(WorldSave.Encode(fixture)), "Dispatch mutated source or RNG");
        var granted = bus.Dispatch(fixture, "turn.start", actor.Id, new(fixture.RandomState), grantedSlot: new(skill.Id, "grant"));
        Check(granted.Factions.Single(f => f.Id == actor.Owner).Resources.Money
            == fixture.Factions.Single(f => f.Id == actor.Owner).Resources.Money + 7, "Granted slot dispatch skipped");
        var missingSource = fixture with { Units = fixture.Units.Where(u => u.Id != actor.Id).ToArray() };
        var death = bus.Dispatch(missingSource, "turn.start", actor.Id, new(missingSource.RandomState),
            eventSource: bound.Units.Single(u => u.Id == actor.Id));
        Check(death.Factions.Single(f => f.Id == actor.Owner).Resources.Money
            == missingSource.Factions.Single(f => f.Id == actor.Owner).Resources.Money + 7, "Removed event source dispatch skipped");

        var mutable = fixture.Copy();
        using (PlanningScope.Begin(ai))
        {
            mutable.Validate();
            try
            {
                using var nested = PlanningScope.Begin(ai with { Movement = ai.Movement with { Extensions = bus } });
                ((CellState[])mutable.Cells)[0] = mutable.Cells[0] with { Environment = -1 };
                throw new InvalidOperationException("nested exit");
            }
            catch (InvalidOperationException) { }
            Check(PlanningScope.Queries(mutable) == null, "Untrusted exception retained ancestor queries");
            try { mutable.Validate(); throw new InvalidOperationException("Ancestor proof survived callback"); }
            catch (RuleViolation) { checks++; }
        }
        return checks;
    }
}
