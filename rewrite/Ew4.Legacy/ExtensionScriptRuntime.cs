using Ew4.Core;
using MoonSharp.Interpreter;

namespace Ew4.Legacy;

// Each invocation gets a fresh interpreter; persistent state is returned explicitly.
public sealed class ExtensionScriptRuntime : IRuleScriptRuntime
{
    private readonly IReadOnlyDictionary<string, string> sources;
    public ExtensionScriptRuntime(IReadOnlyDictionary<string, string> sources)
    {
        this.sources = sources;
        foreach (var pair in sources)
        {
            try { NewScript().LoadString(pair.Value, null, pair.Key); }
            catch (InterpreterException e) { throw new RuleViolation("extension.lua_syntax", pair.Key + ": " + e.DecoratedMessage); }
        }
    }
    private static Script NewScript() => new(CoreModules.Basic | CoreModules.Table | CoreModules.String | CoreModules.Math)
    { Options = { DebugPrint = _ => { } } };

    public void ValidateHandler(string file, string function) => Execute(file, function,
        new("validation", null, null, 1, 1, 0, new Dictionary<string, long>()), new(1), true);
    public RuleScriptResult Run(string file, string function, RuleScriptContext context, Ew4Random random) => Execute(file, function, context, random, false);
    private RuleScriptResult Execute(string file, string function, RuleScriptContext context, Ew4Random random, bool validateOnly)
    {
        if (!sources.TryGetValue(file, out var source)) throw new RuleViolation("extension.lua_missing", file);
        var script = NewScript();
        foreach (string name in new[] { "load", "loadstring", "loadfile", "dofile", "require" }) script.Globals.Set(name, DynValue.Nil);
        var math = script.Globals.Get("math").Table;
        math.Set("random", DynValue.Nil); math.Set("randomseed", DynValue.Nil);
        // Remove native bulk allocation helpers that bypass the VM instruction counter.
        var strings = script.Globals.Get("string").Table;
        foreach (string name in new[] { "rep", "format", "gsub" }) strings.Set(name, DynValue.Nil);
        script.Globals.Get("table").Table.Set("concat", DynValue.Nil);
        var ctx = new Table(script);
        ctx.Set("event", DynValue.NewString(context.Event)); ctx.Set("level", DynValue.NewNumber(context.Level));
        ctx.Set("round", DynValue.NewNumber(context.Round)); ctx.Set("value", DynValue.NewNumber(context.Value));
        ctx.Set("distance", context.Distance.HasValue ? DynValue.NewNumber(context.Distance.Value) : DynValue.Nil);
        ctx.Set("targetCell", context.TargetCell.HasValue ? DynValue.NewNumber(context.TargetCell.Value) : DynValue.Nil);
        ctx.Set("stacks", DynValue.NewNumber(context.Stacks)); ctx.Set("remainingTurns", DynValue.NewNumber(context.RemainingTurns));
        Table Combatant(Combatant? unit)
        {
            var t = new Table(script);
            if (unit == null) return t;
            t.Set("health", DynValue.NewNumber(unit.Health)); t.Set("maxHealth", DynValue.NewNumber(unit.MaximumHealth));
            t.Set("maximumRange", DynValue.NewNumber(unit.MaximumRange));
            t.Set("category", DynValue.NewString(unit.Profile.Category)); t.Set("profile", DynValue.NewString(unit.Profile.Id));
            t.Set("atSea", DynValue.NewBoolean(unit.AtSea));
            t.Set("morale", DynValue.NewNumber(unit.Morale));
            t.Set("building", unit.BuildingKind == null ? DynValue.Nil : DynValue.NewString(unit.BuildingKind));
            if (unit.RuleLocation is { } site)
            {
                t.Set("cell", DynValue.NewNumber(site.Cell));
                t.Set("terrain", DynValue.NewString(site.Terrain));
                t.Set("building", site.Building == null ? DynValue.Nil : DynValue.NewString(site.Building));
                t.Set("buildingLevel", DynValue.NewNumber(site.BuildingLevel));
                t.Set("installation", site.Installation == null ? DynValue.Nil : DynValue.NewString(site.Installation));
            }
            var statuses = new Table(script);
            foreach (var status in unit.Statuses)
            {
                var saved = new Table(script);
                saved.Set("turns", DynValue.NewNumber(status.RemainingTurns)); saved.Set("stacks", DynValue.NewNumber(status.Stacks));
                saved.Set("level", DynValue.NewNumber(status.Level)); statuses.Set(status.Id, DynValue.NewTable(saved));
            }
            t.Set("statuses", DynValue.NewTable(statuses));
            t.Set("has_tag", DynValue.NewCallback((_, args) => DynValue.NewBoolean(unit.Profile.Traits.Contains(args[args.Count - 1].String))));
            return t;
        }
        ctx.Set("source", DynValue.NewTable(Combatant(context.Source))); ctx.Set("target", DynValue.NewTable(Combatant(context.Target)));
        ctx.Set("playerCamp", context.PlayerCamp == null ? DynValue.Nil : DynValue.NewString(context.PlayerCamp));
        ctx.Set("sourceId", context.SourceId == null ? DynValue.Nil : DynValue.NewString(context.SourceId));
        ctx.Set("targetId", context.TargetId == null ? DynValue.Nil : DynValue.NewString(context.TargetId));
        ctx.Set("skillId", context.SkillId == null ? DynValue.Nil : DynValue.NewString(context.SkillId));
        ctx.Set("instanceId", context.InstanceId == null ? DynValue.Nil : DynValue.NewString(context.InstanceId));
        ctx.Set("itemId", context.ItemId == null ? DynValue.Nil : DynValue.NewString(context.ItemId));
        if (context.World is WorldState world)
        {
            ctx.Set("width", DynValue.NewNumber(world.Width)); ctx.Set("height", DynValue.NewNumber(world.Height));
            ctx.Set("activeFaction", DynValue.NewString(world.ActiveFaction));
            ctx.Set("playerFaction", world.ScenarioPlayerFaction == null ? DynValue.Nil : DynValue.NewString(world.ScenarioPlayerFaction));
            Table Ids(IEnumerable<string> ids)
            { var t = new Table(script); int i = 1; foreach (var id in ids) t.Set(i++, DynValue.NewString(id)); return t; }
            ctx.Set("unitIds", DynValue.NewTable(Ids(world.Units.Select(u => u.Id))));
            ctx.Set("factionIds", DynValue.NewTable(Ids(world.Factions.Select(f => f.Id))));
            ctx.Set("commanderIds", DynValue.NewTable(Ids(world.Commanders.Select(c => c.Id))));
            ctx.Set("relation", DynValue.NewCallback((_, args) =>
            {
                string first = args[args.Count - 2].String, second = args[args.Count - 1].String;
                return world.Factions.Any(f => f.Id == first) && world.Factions.Any(f => f.Id == second)
                    ? DynValue.NewString(WorldDiplomacy.Relation(world, first, second)) : DynValue.Nil;
            }));
            ctx.Set("event_count", DynValue.NewCallback((_, args) =>
            {
                string id = args[args.Count - 1].String;
                return DynValue.NewNumber(world.ScenarioEventProgress.TryGetValue(id, out var progress) ? progress.Occurrences
                    : world.TriggeredScenarioEvents.Contains(id) ? 1 : 0);
            }));
            ctx.Set("unit_destroyed", DynValue.NewCallback((_, args) => DynValue.NewBoolean(world.DestroyedScenarioUnits.Contains(args[args.Count - 1].String))));
            ctx.Set("commander_dead", DynValue.NewCallback((_, args) => DynValue.NewBoolean(world.DeadScenarioCommanders.Contains(args[args.Count - 1].String))));
            ctx.Set("commander", DynValue.NewCallback((_, args) =>
            {
                var commander = world.Commanders.SingleOrDefault(c => c.Id == args[args.Count - 1].String);
                if (commander == null) return DynValue.Nil;
                var t = new Table(script); t.Set("id", DynValue.NewString(commander.Id)); t.Set("owner", DynValue.NewString(commander.Owner));
                t.Set("withdrawn", DynValue.NewBoolean(commander.Withdrawn)); t.Set("rank", DynValue.NewNumber(commander.Rank.Level));
                t.Set("title", DynValue.NewNumber(commander.Title.Level)); return DynValue.NewTable(t);
            }));
            ctx.Set("cell", DynValue.NewCallback((_, args) =>
            {
                double raw = args[args.Count - 1].Number;
                if (!double.IsFinite(raw) || raw != Math.Truncate(raw) || raw < 0 || raw >= world.Cells.Count) throw new ScriptRuntimeException("Invalid cell index");
                int index = (int)raw;var cell = world.Cells[index];var t = new Table(script);
                t.Set("id", DynValue.NewNumber(index)); t.Set("owner", cell.Owner == null ? DynValue.Nil : DynValue.NewString(cell.Owner));
                t.Set("camp", cell.Owner == null ? DynValue.Nil : DynValue.NewString(world.Factions.Single(f => f.Id == cell.Owner).Camp));
                t.Set("kind", cell.BuildingKind == null ? DynValue.Nil : DynValue.NewString(cell.BuildingKind));
                t.Set("level", DynValue.NewNumber(cell.BuildingLevel));t.Set("objective", DynValue.NewBoolean(cell.Objective));
                t.Set("name", cell.Name == null ? DynValue.Nil : DynValue.NewString(cell.Name));
                t.Set("environment", DynValue.NewNumber(cell.Environment));
                t.Set("installation", cell.InstallationKind == null ? DynValue.Nil : DynValue.NewString(cell.InstallationKind));
                t.Set("active", DynValue.NewBoolean(cell.Terrain.Active));t.Set("sea", DynValue.NewBoolean(cell.Terrain.Sea));return DynValue.NewTable(t);
            }));
            ctx.Set("unit", DynValue.NewCallback((_, args) =>
            {
                string id = args[args.Count - 1].String;var unit = world.Units.SingleOrDefault(u => u.Id == id);
                if (unit == null) return DynValue.Nil;
                var t = Combatant(unit.Combatant);t.Set("id", DynValue.NewString(unit.Id));t.Set("owner", DynValue.NewString(unit.Owner));
                t.Set("commander", unit.CommanderId == null ? DynValue.Nil : DynValue.NewString(unit.CommanderId));
                t.Set("morale", DynValue.NewNumber(unit.Morale.Effective)); t.Set("movement", DynValue.NewNumber(unit.RemainingMovement));
                t.Set("acted", DynValue.NewBoolean(unit.Acted)); t.Set("objective", DynValue.NewBoolean(unit.Objective));
                t.Set("transport", DynValue.NewBoolean(unit.Transport)); t.Set("experience", DynValue.NewNumber(unit.Experience));
                t.Set("burning", DynValue.NewNumber(unit.Burning));
                t.Set("cell", DynValue.NewNumber(unit.Cell));t.Set("camp", DynValue.NewString(world.Factions.Single(f => f.Id == unit.Owner).Camp));return DynValue.NewTable(t);
            }));
            ctx.Set("faction", DynValue.NewCallback((_, args) =>
            {
                string id = args[args.Count - 1].String;var f = world.Factions.SingleOrDefault(f => f.Id == id);
                if (f == null) return DynValue.Nil;
                var t = new Table(script);t.Set("id", DynValue.NewString(f.Id));t.Set("camp", DynValue.NewString(f.Camp));
                t.Set("defeated", DynValue.NewBoolean(f.Defeated));t.Set("human", DynValue.NewBoolean(f.Human));
                t.Set("money", DynValue.NewNumber(f.Resources.Money));t.Set("industry", DynValue.NewNumber(f.Resources.Industry));t.Set("food", DynValue.NewNumber(f.Resources.Food));return DynValue.NewTable(t);
            }));
        }
        var state = new Table(script);
        foreach (var pair in context.State) state.Set(pair.Key, DynValue.NewNumber(pair.Value));
        ctx.Set("state", DynValue.NewTable(state));
        ctx.Set("random", DynValue.NewCallback((_, args) =>
        {
            double n = args[args.Count - 1].Number;
            if (!double.IsFinite(n) || n < 1 || n > int.MaxValue || n != Math.Truncate(n))
                throw new ScriptRuntimeException("random(bound) requires a positive integer");
            return DynValue.NewNumber(random.Next((int)n));
        }));
        try
        {
            // Source initialization and handler execution share a deterministic instruction budget.
            int budget = 200_000;
            DynValue Bounded(DynValue callable, params DynValue[] args)
            {
                var coroutine = script.CreateCoroutine(callable).Coroutine;
                coroutine.AutoYieldCounter = 1000;
                DynValue result;
                do
                {
                    if ((budget -= 1000) < 0) throw new RuleViolation("extension.lua_budget", file + ": instruction budget exceeded");
                    result = coroutine.Resume(args); args = [];
                } while (coroutine.State != CoroutineState.Dead);
                return result;
            }
            Bounded(script.LoadString(source, null, file));
            var handler = script.Globals.Get(function);
            if (handler.Type != DataType.Function) throw new RuleViolation("extension.lua_function", file + ": " + function);
            if (validateOnly) return new(0);
            var answer = Bounded(handler, DynValue.NewTable(ctx));
            var value = answer.Type == DataType.Table ? answer.Table.Get("value") : answer;
            if (value.Type == DataType.Boolean) value = DynValue.NewNumber(value.Boolean ? 1 : 0);
            if (value.Type != DataType.Number || !double.IsFinite(value.Number)) throw new RuleViolation("extension.lua_result", file + ": return a number or {value=number,state=table}");
            var saved = answer.Type == DataType.Table ? answer.Table.Get("state") : DynValue.Nil;
            Dictionary<string, long>? counters = null;
            if (!saved.IsNil())
            {
                if (saved.Type != DataType.Table) throw new RuleViolation("extension.lua_state", "Script state must be a table");
                counters = new(StringComparer.Ordinal);
                foreach (var pair in saved.Table.Pairs)
                {
                    if (counters.Count >= 128 || pair.Key.Type != DataType.String || pair.Value.Type != DataType.Number
                        || !double.IsFinite(pair.Value.Number) || Math.Abs(pair.Value.Number) > 9_007_199_254_740_991d || Math.Truncate(pair.Value.Number) != pair.Value.Number)
                        throw new RuleViolation("extension.lua_state", "State requires at most 128 integer values with string keys");
                    counters.Add(pair.Key.String, checked((long)pair.Value.Number));
                }
            }
            return new(value.Number, counters);
        }
        catch (InterpreterException e) { throw new RuleViolation("extension.lua_runtime", file + ": " + e.DecoratedMessage); }
    }
}
