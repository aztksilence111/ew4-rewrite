using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record PlayerUnitControl(string Owner, string Policy = "manual", int? Goal = null, int ActionRound = 0, int[]? Goals = null);
public sealed record PlayerAiDraft
{
    public string? Policy { get; init; }
    public string[] Units { get; init; } = Array.Empty<string>();
    public int? Goal { get; init; }
    public int[]? Goals { get; init; }
    public bool PolicyConfirmed { get; init; }
    public bool UnitsConfirmed { get; init; }
    public bool GoalConfirmed { get; init; }
}
public sealed record PlayerAiControl
{
    public bool Enabled { get; init; }
    public bool AutoBattle { get; init; }
    public PlayerAiDraft Draft { get; init; } = new();
    public bool EndingTurn { get; init; }
    public string[] Queue { get; init; } = Array.Empty<string>();
    public int Steps { get; init; }
    public PlayerAiControl Copy() => this with { Queue = Queue.ToArray(), Draft = Draft with { Units = Draft.Units.ToArray(), Goals = Draft.Goals?.ToArray() } };
}
public sealed record PlayerAiStep(AiUnitPlan? Plan, string? Unit, bool Reached = false);

public static class PlayerAi
{
    public const int MaximumGoals = 32;
    public static int[] Targets(PlayerUnitControl order) => order.Goals ?? (order.Goal.HasValue ? new[] { order.Goal.Value } : Array.Empty<int>());
    public static int[] Targets(PlayerAiDraft draft) => draft.Goals ?? (draft.Goal.HasValue ? new[] { draft.Goal.Value } : Array.Empty<int>());
    private static PlayerUnitControl WithTargets(PlayerUnitControl order, int[] goals) => order with { Goal = goals.Length == 0 ? null : goals[0], Goals = goals.ToArray() };
    private static PlayerAiDraft WithTargets(PlayerAiDraft draft, int[] goals) => draft with { Goal = goals.Length == 0 ? null : goals[0], Goals = goals.ToArray() };
    private static bool AtGoal(WorldState world, UnitState unit, int goal) => unit.Cell == goal
        || world.Units.Any(u => u.Id != unit.Id && u.Owner == unit.Owner && u.Cell == goal)
            && new HexMap(world.Width, world.Height).Distance(unit.Cell, goal) == 1;
    private static bool InvalidTargets(int[]? goals, int? first, WorldState world) => goals != null && (goals.Length > MaximumGoals
        || goals.Distinct().Count() != goals.Length || goals.Any(g => g < 0 || g >= world.Cells.Count)
        || (goals.Length == 0 ? first.HasValue : first != goals[0]));
    public static WorldState Reconcile(WorldState world)
    {
        if (!world.Factions.Any(f => f.PlayerAi != null)) return world;
        var owners = world.Units.ToDictionary(u => u.Id, u => u.Owner, StringComparer.Ordinal);
        bool changed = false;
        var factions = world.Factions.Select(f => {
            if (f.PlayerAi is not { } c) return f;
            var units = c.Draft.Units.Where(id => owners.TryGetValue(id, out var owner) && owner == f.Id).ToArray();
            var queue = c.Queue.Where(id => owners.TryGetValue(id, out var owner) && owner == f.Id).ToArray();
            if (units.Length == c.Draft.Units.Length && queue.Length == c.Queue.Length) return f;
            changed = true;
            return f with { PlayerAi = c with { Draft = c.Draft with { Units = units, UnitsConfirmed = units.Length > 0 && c.Draft.UnitsConfirmed },
                Queue = queue, Steps = queue.FirstOrDefault() == c.Queue.FirstOrDefault() ? c.Steps : 0 } };
        }).ToArray();
        return changed ? world with { Factions = factions } : world;
    }
    public static readonly IReadOnlyList<string> Policies = Array.AsReadOnly(new[] { "manual", "free", "aggressive", "cautious", "hold", "retreat" });
    public static PlayerAiControl Control(WorldState world) => world.Factions.Single(f => f.Id == world.ActiveFaction).PlayerAi ?? new();
    public static PlayerUnitControl Order(UnitState unit) => unit.PlayerControl?.Owner == unit.Owner ? unit.PlayerControl : new(unit.Owner);
    public static bool Managed(WorldState world, UnitState unit)
    {
        var control = world.Factions.Single(f => f.Id == unit.Owner).PlayerAi;
        var order = Order(unit);
        return control?.Enabled == true && (order.Goal.HasValue || order.Policy != "manual" || control.AutoBattle);
    }
    public static bool Pending(WorldState world) => world.Factions.Any(f => f.Id == world.ActiveFaction && f.Human && f.PlayerAi is { Enabled: true, EndingTurn: true });
    public static bool Automatic(WorldState world) => world.Factions.Any(f => f.Id == world.ActiveFaction && f.Human && f.PlayerAi is { Enabled: true, AutoBattle: true });

    public static void Validate(WorldState world)
    {
        foreach (var faction in world.Factions)
        {
            if (faction.PlayerAi is not { } c) continue;
            var d = c.Draft;
            if (!faction.Human || d == null || d.Units == null || c.Queue == null || c.Steps < 0 || c.Steps > 8
                || c.Queue.Length > 100_000 || d.Units.Length > 100_000
                || c.Queue.Any(string.IsNullOrWhiteSpace) || d.Units.Any(string.IsNullOrWhiteSpace)
                || c.Queue.Distinct(StringComparer.Ordinal).Count() != c.Queue.Length
                || d.Units.Distinct(StringComparer.Ordinal).Count() != d.Units.Length
                || d.Policy != null && !Policies.Contains(d.Policy) || d.PolicyConfirmed && d.Policy == null
                || d.UnitsConfirmed && d.Units.Length == 0 || d.GoalConfirmed && !d.Goal.HasValue
                || d.Goal.HasValue && (d.Goal < 0 || d.Goal >= world.Cells.Count)
                || InvalidTargets(d.Goals, d.Goal, world)
                || c.EndingTurn && !c.Enabled || !c.EndingTurn && (c.Queue.Length > 0 || c.Steps != 0))
                throw new RuleViolation("player_ai.state", "Invalid player AI state");
        }
        foreach (var unit in world.Units)
            if (unit.PlayerControl is { } c && (string.IsNullOrWhiteSpace(c.Owner) || !world.Factions.Any(f => f.Id == c.Owner)
                || !Policies.Contains(c.Policy) || c.ActionRound < 0 || c.ActionRound > world.Round
                || c.Goal.HasValue && (c.Goal < 0 || c.Goal >= world.Cells.Count)))
                throw new RuleViolation("player_ai.unit", "Invalid player unit order");
        foreach (var unit in world.Units)
            if (unit.PlayerControl is { } order && InvalidTargets(order.Goals, order.Goal, world))
                throw new RuleViolation("player_ai.unit", "Invalid ordered player targets");
    }

    private static void Ready(WorldState world, long revision)
    {
        var faction = WorldActions.Ready(world, revision);
        if (!faction.Human) throw new RuleViolation("player_ai.owner", "Only a human faction can configure player AI");
    }
    private static WorldChange Change(WorldState world, PlayerAiControl control, IReadOnlyList<UnitState>? units = null)
    {
        var next = world with { Revision = checked(world.Revision + 1), Units = units ?? world.Units,
            Factions = world.Factions.Select(f => f.Id == world.ActiveFaction ? f with { PlayerAi = control.Copy() } : f).ToArray() };
        next.Validate();
        return new(next, Array.Empty<WorldNotice>());
    }
    private static void CheckUnits(WorldState world, string[] units)
    {
        if (units.Length == 0 || units.Length != units.Distinct(StringComparer.Ordinal).Count()
            || units.Any(id => !world.Units.Any(u => u.Id == id && u.Owner == world.ActiveFaction)))
            throw new RuleViolation("player_ai.selection", "请选择仍然存活的本国单位");
    }
    public static WorldChange Configure(WorldState world, long revision, string step, string? policy = null,
        string[]? units = null, int goal = -1, bool? autoBattle = null, int[]? goals = null)
    {
        Ready(world, revision);
        world = Reconcile(world);
        var control = Control(world);
        if (control.Enabled) throw new RuleViolation("player_ai.running", "请先停止执行再修改设定");
        var draft = control.Draft;
        IReadOnlyList<UnitState>? updatedUnits = null;
        switch (step)
        {
            case "policy":
                if (policy == null || !Policies.Contains(policy)) throw new RuleViolation("player_ai.policy", "请选择战斗方针");
                draft = (policy == "manual" ? WithTargets(draft, Array.Empty<int>()) : draft) with { Policy = policy,
                    PolicyConfirmed = true, GoalConfirmed = policy != "manual" && draft.GoalConfirmed };
                break;
            case "units":
                CheckUnits(world, units ?? Array.Empty<string>());
                bool sameSelection = new HashSet<string>(draft.Units, StringComparer.Ordinal).SetEquals(units!);
                draft = draft with { Units = units!.ToArray(), UnitsConfirmed = true, GoalConfirmed = sameSelection && draft.GoalConfirmed };
                break;
            case "goal":
                CheckUnits(world, draft.Units);
                var targets = goals ?? new[] { goal };
                if (targets.Length == 0 || targets.Length > MaximumGoals || targets.Distinct().Count() != targets.Length
                    || targets.Any(g => g < 0 || g >= world.Cells.Count || !world.Cells[g].Terrain.Active))
                    throw new RuleViolation("player_ai.goal", "请选择有效地图地块");
                draft = WithTargets(draft, targets) with { GoalConfirmed = true }; break;
            case "clear_goal":
                var selected = new HashSet<string>(draft.Units, StringComparer.Ordinal);
                updatedUnits = world.Units.Select(u => u.Owner == world.ActiveFaction && selected.Contains(u.Id)
                    ? u with { PlayerControl = WithTargets(Order(u), Array.Empty<int>()) } : u).ToArray();
                draft = WithTargets(draft, Array.Empty<int>()) with { GoalConfirmed = false };
                break;
            case "auto": control = control with { AutoBattle = autoBattle ?? false }; break;
            case "cancel": draft = new(); break;
            default: throw new RuleViolation("player_ai.step", "Unknown player AI configuration step");
        }
        return Change(world, control with { Draft = draft }, updatedUnits);
    }
    public static WorldChange Start(WorldState world, long revision)
    {
        Ready(world, revision);
        world = Reconcile(world);
        var control = Control(world); var draft = control.Draft;
        if (control.Enabled) throw new RuleViolation("player_ai.running", "AI已经开始执行");
        if (!draft.PolicyConfirmed || !draft.UnitsConfirmed || !draft.GoalConfirmed || draft.Policy == null
            || !draft.Goal.HasValue || Targets(draft).Any(g => !world.Cells[g].Terrain.Active))
            throw new RuleViolation("player_ai.incomplete", "请先确认战斗方针、框选单位和行动目标");
        CheckUnits(world, draft.Units);
        var selected = new HashSet<string>(draft.Units, StringComparer.Ordinal);
        return Change(world, control with { Enabled = true }, world.Units.Select(u => selected.Contains(u.Id)
            ? u with { PlayerControl = WithTargets(Order(u) with { Policy = draft.Policy }, Targets(draft)) } : u).ToArray());
    }
    public static WorldChange Stop(WorldState world, long revision, string? playerFaction = null)
    {
        // Stop is also available at an interrupted dialogue boundary.
        world.Validate();
        world = Reconcile(world);
        if (world.Revision != revision) throw new RuleViolation("action.stale", "State changed before stopping AI");
        string factionId = playerFaction ?? world.ActiveFaction;
        var faction = world.Factions.Single(f => f.Id == factionId);
        if (!faction.Human) throw new RuleViolation("player_ai.owner", "Not a human faction");
        var control = faction.PlayerAi ?? new();
        var remaining = world.Units.Where(u => u.Owner == factionId && control.Draft.Units.Contains(u.Id))
            .Select(u => Targets(Order(u))).Where(g => g.Length > 0).OrderByDescending(g => g.Length).FirstOrDefault() ?? Array.Empty<int>();
        var stopped = control with { Enabled = false, EndingTurn = false, Queue = Array.Empty<string>(), Steps = 0,
            Draft = WithTargets(control.Draft, remaining) with { GoalConfirmed = remaining.Length > 0 && control.Draft.GoalConfirmed } };
        var next = world with { Revision = checked(world.Revision + 1), Factions = world.Factions.Select(f => f.Id == factionId ? f with { PlayerAi = stopped } : f).ToArray() };
        next.Validate();
        return new(next, Array.Empty<WorldNotice>());
    }
    public static WorldState MarkAction(WorldState world, string? unitId)
    {
        if (unitId == null) return world;
        return world with { Units = world.Units.Select(u => u.Id == unitId && u.Owner == world.ActiveFaction
            ? u with { PlayerControl = Order(u) with { ActionRound = world.Round } } : u).ToArray() };
    }
    public static WorldChange BeginEnd(WorldState world, long revision, WorldTurnRules turns)
    {
        Ready(world, revision);
        var control = Control(world);
        if (!control.Enabled) return WorldTurns.EndTurn(world, revision, turns);
        if (control.EndingTurn) throw new RuleViolation("player_ai.ending", "正在执行本回合托管");
        var queue = world.Units.Where(u => u.Owner == world.ActiveFaction && Managed(world, u) && !u.Acted
            && Order(u).ActionRound != world.Round && u.Morale.Effective != -3 && u.Combatant.ConstructionTurns == 0)
            .OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal).Select(u => u.Id).ToArray();
        return Change(world, control with { EndingTurn = true, Queue = queue, Steps = 0 });
    }

    public static PlayerAiStep PlanStep(WorldState world, WorldAiRules ai)
    {
        Ready(world, world.Revision);
        if (!Pending(world)) throw new RuleViolation("player_ai.inactive", "尚未开始回合托管");
        var control = Control(world);
        if (control.Queue.Length == 0) return new(null, null);
        string id = control.Queue[0];
        var unit = world.Units.SingleOrDefault(u => u.Id == id && u.Owner == world.ActiveFaction);
        if (unit == null || !Managed(world, unit) || unit.Combatant.ConstructionTurns > 0 || unit.Morale.Effective == -3 || control.Steps >= 8)
            return new(null, id);
        var order = Order(unit); var targets = Targets(order); bool reached = order.Goal.HasValue && AtGoal(world, unit, order.Goal.Value);
        int? target = reached ? targets.Skip(1).Select(g => (int?)g).FirstOrDefault() : order.Goal;
        string policy = order.Policy == "manual" && control.AutoBattle ? "free" : order.Policy;
        WorldAiCommand? command;
        if (target.HasValue)
        {
            command = GoalMove(world, unit, target.Value, ai);
            if (command == null && policy is not ("manual" or "retreat"))
                command = WorldAi.StrikeCandidates(world, id, ai).Where(c => world.Units.Any(u => u.Id == c.Action.TargetId && u.Cell == target))
                    .OrderByDescending(c => c.Score).Select(c => (WorldAiCommand)c.Action).FirstOrDefault();
        }
        else if (policy == "retreat") command = Retreat(world, unit, ai);
        else if (policy == "manual") command = null;
        else
        {
            string behavior = policy switch { "aggressive" => "1", "cautious" => "2", "hold" => "3", _ => "0" };
            var projected = world with { Units = world.Units.Select(u => u.Id == id ? u with { Behavior = behavior } : u).ToArray() };
            var plan = WorldAi.PlanUnit(projected, id, ai with { Strategy = ai.Strategy with { Transport = null } });
            return new(plan.Candidates.Count == 0 ? null : plan, id, reached);
        }
        return new(command == null ? null : new(world.Revision, id, world.AiRandomState, world.AiRandomState,
            new[] { new AiCandidate<WorldAiCommand>(command, 1) }), id, reached);
    }
    public static WorldChange ExecuteStep(WorldState world, PlayerAiStep step, WorldAiRules ai, WorldBattleRules battle, WorldTurnRules turns)
    {
        Ready(world, world.Revision);
        if (!Pending(world)) throw new RuleViolation("player_ai.inactive", "Player AI is stopped");
        var control = Control(world);
        if (step.Unit == null)
        {
            if (control.Queue.Length != 0) throw new RuleViolation("player_ai.stale", "AI queue changed");
            var cleared = world with { Factions = world.Factions.Select(f => f.Id == world.ActiveFaction ? f with {
                PlayerAi = control with { EndingTurn = false, Queue = Array.Empty<string>(), Steps = 0 } } : f).ToArray() };
            return WorldTurns.EndTurn(cleared, world.Revision, turns);
        }
        if (control.Queue.FirstOrDefault() != step.Unit) throw new RuleViolation("player_ai.stale", "AI queue changed");
        var change = step.Plan == null ? new WorldChange(world with { Revision = checked(world.Revision + 1) }, Array.Empty<WorldNotice>())
            : WorldAi.ExecuteUnit(world, step.Plan, ai, battle);
        bool done = step.Plan == null || step.Plan.Candidates[AiPlanning.Best(step.Plan.Candidates)].Action is AiHoldCommand;
        var next = change.State;
        // Goal completion belongs to the same commit as movement, so saves never replay it.
        next = next with { Units = next.Units.Select(u => {
            if (u.Id != step.Unit) return u;
            var order = Order(u); var remaining = Targets(order);
            if (step.Reached) remaining = remaining.Skip(1).ToArray();
            if (remaining.Length > 0 && AtGoal(next, u, remaining[0])) remaining = remaining.Skip(1).ToArray();
            return u with { PlayerControl = WithTargets(order, remaining) };
        }).ToArray(),
            Factions = next.Factions.Select(f => f.Id == world.ActiveFaction ? f with { PlayerAi = control with {
                Queue = done ? control.Queue.Skip(1).ToArray() : control.Queue.ToArray(), Steps = done ? 0 : control.Steps + 1 } } : f).ToArray() };
        next.Validate();
        return change with { State = next };
    }

    private static WorldAiCommand? GoalMove(WorldState world, UnitState unit, int goal, WorldAiRules ai)
    {
        if (unit.RemainingMovement < 1 || unit.Combatant.Profile.Category == "fort" || !world.Cells[goal].Terrain.Active) return null;
        var map = new HexMap(world.Width, world.Height);
        var context = WorldActions.MovementContext(world, unit, ai.Movement);
        var occupied = new HashSet<int>(world.Units.Where(u => u.Id != unit.Id).Select(u => u.Cell));
        var range = WorldActions.MovementRange(world, unit, ai.Movement);
        var endpoints = occupied.Contains(goal) ? Enumerable.Range(0, 6).Select(d => map.Neighbor(goal, d)).Where(c => c >= 0 && !occupied.Contains(c)).ToArray() : new[] { goal };
        var distances = Movement.DistancesForAdvance(map, context.Cells, endpoints, context.Actor, ai.Movement.Search, unit.Cell, range.Keys.ToArray());
        if (!distances.TryGetValue(unit.Cell, out long origin)) return null;
        int destination = range.Keys.Where(c => c != unit.Cell && !occupied.Contains(c) && distances.TryGetValue(c, out long cost) && cost < origin)
            .OrderBy(c => distances[c]).ThenBy(c => c).DefaultIfEmpty(-1).First();
        return destination < 0 ? null : new AiMoveCommand(world.Revision, unit.Id, destination);
    }
    private static WorldAiCommand? Retreat(WorldState world, UnitState unit, WorldAiRules ai)
    {
        if (unit.RemainingMovement < 1 || unit.Combatant.Profile.Category == "fort") return null;
        var map = new HexMap(world.Width, world.Height);
        var enemies = world.Units.Where(u => WorldDiplomacy.Hostile(world, unit.Owner, u.Owner, ai.NeutralCamps)).ToArray();
        long Threat(int cell) => enemies.Sum(u => (long)u.Combatant.Health * 100 / Math.Max(1, map.Distance(cell, u.Cell)));
        long original = Threat(unit.Cell);
        var occupied = new HashSet<int>(world.Units.Select(u => u.Cell));
        int destination = WorldActions.MovementRange(world, unit, ai.Movement).Keys
            .Where(c => c != unit.Cell && !occupied.Contains(c) && Threat(c) < original)
            .OrderBy(Threat).ThenBy(c => world.Cells[c].Environment).ThenBy(c => world.Cells[c].Owner == unit.Owner ? 0 : 1)
            .ThenBy(c => c).DefaultIfEmpty(-1).First();
        return destination < 0 ? null : new AiMoveCommand(world.Revision, unit.Id, destination);
    }
}
