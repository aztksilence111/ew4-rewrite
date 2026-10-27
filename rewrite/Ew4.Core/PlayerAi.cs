using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record PlayerUnitControl(string Owner, string Policy = "manual", int? Goal = null, int ActionRound = 0, int[]? Goals = null, int TaskId = 0);
public sealed record PlayerAiDraft
{
    public string? Policy { get; init; }
    public string[] Units { get; init; } = Array.Empty<string>();
    public int? Goal { get; init; }
    public int[]? Goals { get; init; }
    public bool PolicyConfirmed { get; init; }
    public bool UnitsConfirmed { get; init; }
    public bool GoalConfirmed { get; init; }
    public string Kind { get; init; } = "attack";
    public int Radius { get; init; } = 2;
    public int[] Line { get; init; } = Array.Empty<int>();
    public bool DeployFirst { get; init; } = true;
    public PlayerAiDraft Copy() => this with { Units = Units.ToArray(), Goals = Goals?.ToArray(), Line = Line.ToArray() };
}
public sealed record PlayerAiControl
{
    public bool Enabled { get; init; }
    public bool AutoBattle { get; init; }
    public PlayerAiDraft Draft { get; init; } = new();
    public bool EndingTurn { get; init; }
    public string[] Queue { get; init; } = Array.Empty<string>();
    public int Steps { get; init; }
    public int ActiveTask { get; init; } = 1;
    public int NextTaskId { get; init; } = 2;
    public PlayerAiTask[]? Tasks { get; init; }
    public PlayerAiControl Copy() => this with { Queue = Queue.ToArray(), Draft = Draft.Copy(), Tasks = Tasks?.Select(t => t.Copy()).ToArray() };
}
public sealed record PlayerAiStep(AiUnitPlan? Plan, string? Unit, bool Reached = false);

public static partial class PlayerAi
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
            var tasks = c.Tasks?.Select(t => {
                var taskDraft = t.Id == c.ActiveTask ? c.Draft : t.Draft;
                var ids = taskDraft.Units.Where(id => owners.TryGetValue(id, out var owner) && owner == f.Id).ToArray();
                return t with { Enabled = ids.Length > 0 && t.Enabled,
                    Draft = taskDraft with { Units = ids, UnitsConfirmed = ids.Length > 0 && taskDraft.UnitsConfirmed },
                    Vacancies = t.Vacancies.Concat(t.Stations.Where(p => !ids.Contains(p.Key)).Select(p => p.Value)).Distinct().ToArray(),
                    Stations = t.Stations.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value) };
            }).ToArray();
            var units = c.Draft.Units.Where(id => owners.TryGetValue(id, out var owner) && owner == f.Id).ToArray();
            var queue = c.Queue.Where(id => owners.TryGetValue(id, out var owner) && owner == f.Id).ToArray();
            if (units.Length == c.Draft.Units.Length && queue.Length == c.Queue.Length
                && (tasks == null || tasks.Zip(c.Tasks!, (a, b) => a.Draft.Units.Length == b.Draft.Units.Length).All(same => same))) return f;
            changed = true;
            var enabled = tasks?.Any(t => t.Enabled) ?? c.Enabled;
            return f with { PlayerAi = c with { Tasks = tasks, Enabled = enabled, EndingTurn = enabled && c.EndingTurn,
                Draft = tasks?.Single(t => t.Id == c.ActiveTask).Draft ?? c.Draft with { Units = units, UnitsConfirmed = units.Length > 0 && c.Draft.UnitsConfirmed },
                Queue = enabled ? queue : Array.Empty<string>(), Steps = enabled && queue.FirstOrDefault() == c.Queue.FirstOrDefault() ? c.Steps : 0 } };
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
        if (control?.Enabled != true) return false;
        if (control.Tasks != null) {
            var task = control.Tasks.SingleOrDefault(t => t.Draft.Units.Contains(unit.Id));
            if (task != null) return task.Enabled;
            return control.AutoBattle;
        }
        return order.Goal.HasValue || order.Policy != "manual" || control.AutoBattle;
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
                || d.UnitsConfirmed && d.Units.Length == 0 || d.GoalConfirmed && !HasObjective(d)
                || d.Goal.HasValue && (d.Goal < 0 || d.Goal >= world.Cells.Count)
                || InvalidTargets(d.Goals, d.Goal, world)
                || c.EndingTurn && !c.Enabled || !c.EndingTurn && (c.Queue.Length > 0 || c.Steps != 0))
                throw new RuleViolation("player_ai.state", "Invalid player AI state");
            ValidateTasks(world, c);
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
        string[]? units = null, int goal = -1, bool? autoBattle = null, int[]? goals = null,
        int? taskId = null, string? kind = null, int? radius = null, int[]? line = null, bool? deployFirst = null, WorldAiRules? ai = null)
    {
        Ready(world, revision);
        world = Reconcile(world);
        var control = EnsureTasks(Control(world));
        if (step is "task_add" or "task_select" or "task_delete")
            return TaskAction(world, control, step, taskId);
        if (control.EndingTurn || CurrentTask(control).Enabled) throw new RuleViolation("player_ai.running", "请先停止当前任务再修改设定");
        var draft = control.Draft;
        IReadOnlyList<UnitState>? updatedUnits = null;
        switch (step)
        {
            case "mission":
                if (kind == null || !TaskKinds.Contains(kind)) throw new RuleViolation("player_ai.mission", "请选择任务类型");
                draft = WithTargets(draft, Array.Empty<int>()) with { Kind = kind, Radius = kind.StartsWith("line_", StringComparison.Ordinal) ? 1 : 2,
                    Line = Array.Empty<int>(), GoalConfirmed = false }; break;
            case "radius":
                if (radius is null or < 0 or > 6) throw new RuleViolation("player_ai.radius", "防守范围须为0至6格");
                draft = draft with { Radius = radius.Value }; break;
            case "deploy_first": draft = draft with { DeployFirst = deployFirst ?? true }; break;
            case "line":
                if (!draft.Kind.StartsWith("line_", StringComparison.Ordinal) || line == null || line.Length < 2 || line.Length > MaximumGoals
                    || line.Any(c => c < 0 || c >= world.Cells.Count || !world.Cells[c].Terrain.Active))
                    throw new RuleViolation("player_ai.line", "请至少选择两个有效部署线折点");
                var expanded = DeploymentLine(world, line);
                if (expanded.Any(c => !world.Cells[c].Terrain.Active)) throw new RuleViolation("player_ai.line", "部署线不能穿过地图外区域");
                draft = draft with { Line = line.ToArray(), GoalConfirmed = draft.Kind == "line_defend" || draft.Goal.HasValue }; break;
            case "policy":
                if (policy == null || !Policies.Contains(policy)) throw new RuleViolation("player_ai.policy", "请选择战斗方针");
                draft = (policy == "manual" ? WithTargets(draft, Array.Empty<int>()) : draft) with { Policy = policy,
                    PolicyConfirmed = true, GoalConfirmed = policy != "manual" && draft.GoalConfirmed };
                break;
            case "units":
                CheckUnits(world, units ?? Array.Empty<string>());
                bool sameSelection = new HashSet<string>(draft.Units, StringComparer.Ordinal).SetEquals(units!);
                draft = draft with { Units = units!.ToArray(), UnitsConfirmed = true, GoalConfirmed = sameSelection && draft.GoalConfirmed };
                control = TransferUnits(control, units!);
                updatedUnits = world.Units.Select(u => units!.Contains(u.Id)
                    ? u with { PlayerControl = new(u.Owner, ActionRound: Order(u).ActionRound, TaskId: control.ActiveTask) }
                    : draft.Units.Contains(u.Id) ? u : u.Owner == world.ActiveFaction && Order(u).TaskId == control.ActiveTask
                        ? u with { PlayerControl = new(u.Owner, ActionRound: Order(u).ActionRound) }
                    : u).ToArray();
                break;
            case "goal":
                CheckUnits(world, draft.Units);
                var targets = goals ?? new[] { goal };
                if (targets.Length == 0 || targets.Length > MaximumGoals || targets.Distinct().Count() != targets.Length
                    || targets.Any(g => g < 0 || g >= world.Cells.Count || !world.Cells[g].Terrain.Active))
                    throw new RuleViolation("player_ai.goal", "请选择有效地图地块");
                if (draft.Kind == "defend" && targets.Length != 1) throw new RuleViolation("player_ai.goal", "防守任务只能指定一个中心");
                draft = WithTargets(draft, targets) with { GoalConfirmed = !draft.Kind.StartsWith("line_", StringComparison.Ordinal) || draft.Line.Length >= 2 }; break;
            case "clear_goal":
                var selected = new HashSet<string>(draft.Units, StringComparer.Ordinal);
                updatedUnits = world.Units.Select(u => u.Owner == world.ActiveFaction && selected.Contains(u.Id)
                    ? u with { PlayerControl = WithTargets(Order(u), Array.Empty<int>()) } : u).ToArray();
                draft = WithTargets(draft, Array.Empty<int>()) with { GoalConfirmed = false };
                break;
            case "auto": control = control with { AutoBattle = autoBattle ?? false }; break;
            case "cancel":
                updatedUnits = world.Units.Select(u => u.Owner == world.ActiveFaction && draft.Units.Contains(u.Id)
                    ? u with { PlayerControl = new(u.Owner, ActionRound: Order(u).ActionRound) } : u).ToArray();
                draft = new(); break;
            default: throw new RuleViolation("player_ai.step", "Unknown player AI configuration step");
        }
        control = StoreTask(control, CurrentTask(control) with { Draft = draft, Stations = new Dictionary<string, int>(), Vacancies = Array.Empty<int>(), Deployed = false });
        if (ai != null && draft.UnitsConfirmed && HasObjective(draft) && draft.Kind != "attack")
            control = StoreTask(control, CurrentTask(control) with { Stations = AssignStations(world with { Units = updatedUnits ?? world.Units }, draft, ai) });
        return Change(world, control, updatedUnits);
    }
    public static WorldChange Start(WorldState world, long revision, WorldAiRules? ai = null)
    {
        Ready(world, revision);
        world = Reconcile(world);
        var control = EnsureTasks(Control(world)); var draft = control.Draft;
        if (control.EndingTurn) throw new RuleViolation("player_ai.ending", "请等待本回合托管结束");
        if (CurrentTask(control).Enabled) throw new RuleViolation("player_ai.running", "当前任务已经开始执行");
        if (!draft.PolicyConfirmed || !draft.UnitsConfirmed || !draft.GoalConfirmed || draft.Policy == null
            || !HasObjective(draft) || Targets(draft).Any(g => !world.Cells[g].Terrain.Active))
            throw new RuleViolation("player_ai.incomplete", "请先确认战斗方针、框选单位和行动目标");
        CheckUnits(world, draft.Units);
        var selected = new HashSet<string>(draft.Units, StringComparer.Ordinal);
        control = StoreTask(control, CurrentTask(control) with { Enabled = true,
            Stations = draft.Kind == "attack" ? new Dictionary<string, int>() : AssignStations(world, draft, ai ?? new()), Vacancies = Array.Empty<int>(), Deployed = false });
        return Change(world, control, world.Units.Select(u => selected.Contains(u.Id)
            ? u with { PlayerControl = WithTargets(Order(u) with { Policy = draft.Policy, TaskId = control.ActiveTask }, Targets(draft)) } : u).ToArray());
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
        var control = EnsureTasks(faction.PlayerAi ?? new());
        var remaining = world.Units.Where(u => u.Owner == factionId && control.Draft.Units.Contains(u.Id))
            .Select(u => Targets(Order(u))).Where(g => g.Length > 0).OrderByDescending(g => g.Length).FirstOrDefault() ?? Array.Empty<int>();
        var draft = control.Draft.Kind == "attack" ? WithTargets(control.Draft, remaining) with { GoalConfirmed = remaining.Length > 0 && control.Draft.GoalConfirmed } : control.Draft;
        var stopped = StoreTask(control, CurrentTask(control) with { Enabled = false, Draft = draft });
        var remainingQueue = stopped.Enabled ? control.Queue.Where(id => !control.Draft.Units.Contains(id)).ToArray() : Array.Empty<string>();
        stopped = stopped with { EndingTurn = stopped.Enabled && control.EndingTurn, Queue = remainingQueue,
            Steps = remainingQueue.FirstOrDefault() == control.Queue.FirstOrDefault() ? control.Steps : 0 };
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
    public static WorldChange BeginEnd(WorldState world, long revision, WorldTurnRules turns, WorldAiRules? ai = null)
    {
        Ready(world, revision);
        var control = Control(world);
        if (!control.Enabled) return WorldTurns.EndTurn(world, revision, turns);
        if (control.EndingTurn) throw new RuleViolation("player_ai.ending", "正在执行本回合托管");
        if (ai != null) { world = RepairStations(world, ai); control = Control(world); }
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
        var task = control.Tasks?.SingleOrDefault(t => t.Enabled && t.Draft.Units.Contains(unit.Id));
        if (task != null && task.Draft.Kind != "attack") return PlanTask(world, unit, task, ai);
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
            if (u.Id != step.Unit || control.Tasks?.Any(t => t.Draft.Units.Contains(u.Id)
                && (t.Draft.Kind is "defend" or "line_defend" || t.Draft.Kind == "line_attack" && t.Draft.DeployFirst && !t.Deployed)) == true) return u;
            var order = Order(u); var remaining = Targets(order);
            if (step.Reached) remaining = remaining.Skip(1).ToArray();
            if (remaining.Length > 0 && AtGoal(next, u, remaining[0])) remaining = remaining.Skip(1).ToArray();
            return u with { PlayerControl = WithTargets(order, remaining) };
        }).ToArray(),
            Factions = next.Factions.Select(f => f.Id == world.ActiveFaction ? f with { PlayerAi = control with {
                Queue = done ? control.Queue.Skip(1).ToArray() : control.Queue.ToArray(), Steps = done ? 0 : control.Steps + 1 } } : f).ToArray() };
        next = UpdateDeployment(next, world.ActiveFaction);
        next.Validate();
        return change with { State = next };
    }

    private static WorldAiCommand? GoalMove(WorldState world, UnitState unit, int goal, WorldAiRules ai, HashSet<int>? allowed = null)
    {
        if (unit.RemainingMovement < 1 || unit.Combatant.Profile.Category == "fort" || !world.Cells[goal].Terrain.Active) return null;
        var map = new HexMap(world.Width, world.Height);
        var context = WorldActions.MovementContext(world, unit, ai.Movement);
        var occupied = new HashSet<int>(world.Units.Where(u => u.Id != unit.Id).Select(u => u.Cell));
        var range = WorldActions.MovementRange(world, unit, ai.Movement);
        var endpoints = occupied.Contains(goal) ? Enumerable.Range(0, 6).Select(d => map.Neighbor(goal, d)).Where(c => c >= 0 && !occupied.Contains(c)).ToArray() : new[] { goal };
        var distances = Movement.DistancesForAdvance(map, context.Cells, endpoints, context.Actor, ai.Movement.Search, unit.Cell, range.Keys.ToArray());
        if (!distances.TryGetValue(unit.Cell, out long origin)) return null;
        int destination = range.Keys.Where(c => c != unit.Cell && !occupied.Contains(c) && (allowed == null || allowed.Contains(c))
                && (allowed == null || range[c].Path.All(allowed.Contains)) && distances.TryGetValue(c, out long cost) && cost < origin)
            .OrderBy(c => distances[c]).ThenBy(c => c).DefaultIfEmpty(-1).First();
        return destination < 0 ? null : new AiMoveCommand(world.Revision, unit.Id, destination);
    }
    private static WorldAiCommand? Retreat(WorldState world, UnitState unit, WorldAiRules ai, HashSet<int>? allowed = null)
    {
        if (unit.RemainingMovement < 1 || unit.Combatant.Profile.Category == "fort") return null;
        var map = new HexMap(world.Width, world.Height);
        var enemies = world.Units.Where(u => WorldDiplomacy.Hostile(world, unit.Owner, u.Owner, ai.NeutralCamps)).ToArray();
        long Threat(int cell) => enemies.Sum(u => (long)u.Combatant.Health * 100 / Math.Max(1, map.Distance(cell, u.Cell)));
        long original = Threat(unit.Cell);
        var occupied = new HashSet<int>(world.Units.Select(u => u.Cell));
        var range = WorldActions.MovementRange(world, unit, ai.Movement);
        int destination = range.Keys
            .Where(c => c != unit.Cell && !occupied.Contains(c) && (allowed == null || allowed.Contains(c) && range[c].Path.All(allowed.Contains)) && Threat(c) < original)
            .OrderBy(Threat).ThenBy(c => world.Cells[c].Environment).ThenBy(c => world.Cells[c].Owner == unit.Owner ? 0 : 1)
            .ThenBy(c => c).DefaultIfEmpty(-1).First();
        return destination < 0 ? null : new AiMoveCommand(world.Revision, unit.Id, destination);
    }
}
