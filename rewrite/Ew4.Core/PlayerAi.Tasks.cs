using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record PlayerAiTask
{
    public int Id { get; init; } = 1;
    public int ColorIndex { get; init; }
    public bool Enabled { get; init; }
    public PlayerAiDraft Draft { get; init; } = new();
    public Dictionary<string, int> Stations { get; init; } = new(StringComparer.Ordinal);
    public int[] Vacancies { get; init; } = Array.Empty<int>();
    public bool Deployed { get; init; }
    public PlayerAiTask Copy() => this with { Draft = Draft.Copy(), Stations = new(Stations, StringComparer.Ordinal), Vacancies = Vacancies.ToArray() };
}

public static partial class PlayerAi
{
    public const int MaximumTasks = 16;
    public static readonly IReadOnlyList<string> TaskKinds = Array.AsReadOnly(new[] { "attack", "defend", "line_attack", "line_defend" });
    public static PlayerAiTask[] Tasks(PlayerAiControl control) => control.Tasks ?? new[] { new PlayerAiTask { Draft = control.Draft, Enabled = control.Enabled } };
    private static PlayerAiControl EnsureTasks(PlayerAiControl control) => control.Tasks == null
        ? control with { Tasks = Tasks(control), ActiveTask = 1, NextTaskId = 2 } : control;
    private static PlayerAiTask CurrentTask(PlayerAiControl control) => Tasks(control).Single(t => t.Id == control.ActiveTask);
    private static bool HasObjective(PlayerAiDraft draft) => draft.Kind switch {
        "line_defend" => draft.Line.Length >= 2,
        "line_attack" => draft.Line.Length >= 2 && draft.Goal.HasValue,
        _ => draft.Goal.HasValue
    };
    private static PlayerAiControl StoreTask(PlayerAiControl control, PlayerAiTask task)
    {
        var tasks = Tasks(control).Select(t => t.Id == task.Id ? task : t).ToArray();
        return control with { Tasks = tasks, Draft = tasks.Single(t => t.Id == control.ActiveTask).Draft, Enabled = tasks.Any(t => t.Enabled) };
    }
    private static void ValidateTasks(WorldState world, PlayerAiControl control)
    {
        bool ValidDraft(PlayerAiDraft d) => d != null && d.Units != null && d.Line != null && TaskKinds.Contains(d.Kind)
            && d.Radius is >= 0 and <= 6 && d.Line.Length <= MaximumGoals
            && d.Line.All(c => c >= 0 && c < world.Cells.Count)
            && d.Units.Length <= 100_000 && d.Units.All(id => !string.IsNullOrWhiteSpace(id))
            && d.Units.Distinct(StringComparer.Ordinal).Count() == d.Units.Length
            && (d.Policy == null || Policies.Contains(d.Policy)) && (!d.PolicyConfirmed || d.Policy != null)
            && (!d.UnitsConfirmed || d.Units.Length > 0) && (!d.GoalConfirmed || HasObjective(d))
            && (!d.Goal.HasValue || d.Goal >= 0 && d.Goal < world.Cells.Count) && !InvalidTargets(d.Goals, d.Goal, world)
            && (d.Kind != "defend" || Targets(d).Length <= 1);
        if (!ValidDraft(control.Draft)) throw new RuleViolation("player_ai.task", "Invalid mission draft");
        if (control.Tasks is not { } tasks) return;
        if (tasks.Length is < 1 or > MaximumTasks || tasks.Any(t => t == null || t.Id < 1 || t.Id >= control.NextTaskId || t.ColorIndex is < 0 or >= MaximumTasks
                || !ValidDraft(t.Draft) || t.Stations == null || t.Stations.Any(p => !t.Draft.Units.Contains(p.Key) || p.Value < 0 || p.Value >= world.Cells.Count)
                || t.Vacancies == null || t.Vacancies.Length > 100_000 || t.Vacancies.Any(c => c < 0 || c >= world.Cells.Count)
                || t.Stations.Values.Distinct().Count() != t.Stations.Count
                || t.Enabled && (!t.Draft.PolicyConfirmed || !t.Draft.UnitsConfirmed || !t.Draft.GoalConfirmed))
            || tasks.Select(t => t.Id).Distinct().Count() != tasks.Length || !tasks.Any(t => t.Id == control.ActiveTask)
            || tasks.Select(t => t.ColorIndex).Distinct().Count() != tasks.Length
            || tasks.SelectMany(t => t.Draft.Units).Distinct(StringComparer.Ordinal).Count() != tasks.Sum(t => t.Draft.Units.Length)
            || control.Enabled != tasks.Any(t => t.Enabled))
            throw new RuleViolation("player_ai.task", "Invalid player AI tasks");
    }
    private static PlayerAiControl TransferUnits(PlayerAiControl control, string[] ids)
    {
        var tasks = Tasks(control).Select(t => {
            if (t.Id == control.ActiveTask) return t;
            var units = t.Draft.Units.Except(ids, StringComparer.Ordinal).ToArray();
            return t with { Enabled = t.Enabled && units.Length > 0,
                Draft = t.Draft with { Units = units, UnitsConfirmed = t.Draft.UnitsConfirmed && units.Length > 0 },
                Vacancies = t.Vacancies.Concat(t.Stations.Where(p => !units.Contains(p.Key)).Select(p => p.Value)).Distinct().ToArray(),
                Stations = t.Stations.Where(p => units.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value) };
        }).ToArray();
        return control with { Tasks = tasks, Enabled = tasks.Any(t => t.Enabled) };
    }
    private static WorldChange TaskAction(WorldState world, PlayerAiControl control, string step, int? id)
    {
        if (control.EndingTurn && step != "task_select") throw new RuleViolation("player_ai.ending", "请等待本回合托管结束");
        var tasks = Tasks(control);
        IReadOnlyList<UnitState>? units = null;
        if (step == "task_add") {
            if (tasks.Length >= MaximumTasks) throw new RuleViolation("player_ai.task_limit", "最多创建16个任务");
            var task = new PlayerAiTask { Id = control.NextTaskId, ColorIndex = Enumerable.Range(0, MaximumTasks).First(i => tasks.All(t => t.ColorIndex != i)) };
            control = control with { ActiveTask = task.Id, NextTaskId = checked(task.Id + 1), Draft = task.Draft, Tasks = tasks.Append(task).ToArray() };
        } else if (step == "task_select") {
            var task = tasks.SingleOrDefault(t => t.Id == id) ?? throw new RuleViolation("player_ai.task", "任务不存在");
            control = control with { ActiveTask = task.Id, Draft = task.Draft };
        } else {
            var removed = CurrentTask(control);
            if (removed.Enabled) throw new RuleViolation("player_ai.running", "请先停止当前任务");
            units = world.Units.Select(u => u.Owner == world.ActiveFaction && removed.Draft.Units.Contains(u.Id)
                ? u with { PlayerControl = new(u.Owner, ActionRound: Order(u).ActionRound) } : u).ToArray();
            tasks = tasks.Where(t => t.Id != removed.Id).ToArray();
            if (tasks.Length == 0) tasks = new[] { new PlayerAiTask { Id = 1 } };
            var active = tasks.LastOrDefault(t => t.Id < removed.Id) ?? tasks[0];
            control = control with { Tasks = tasks, ActiveTask = active.Id, Draft = active.Draft, Enabled = tasks.Any(t => t.Enabled) };
        }
        return Change(world, control, units);
    }

    public static int[] DeploymentLine(WorldState world, int[] vertices)
    {
        var map = new HexMap(world.Width, world.Height);
        var cells = new List<int>();
        foreach (int vertex in vertices) {
            map.Check(vertex);
            if (cells.Count == 0) cells.Add(vertex);
            while (cells[^1] != vertex) {
                int origin = cells[^1];
                int next = Enumerable.Range(0, 6).Select(d => map.Neighbor(origin, d)).Where(c => c >= 0)
                    .OrderBy(c => map.Distance(c, vertex)).ThenBy(c => c).First();
                cells.Add(next);
                if (cells.Count > 512) throw new RuleViolation("player_ai.line", "部署线最多覆盖512格");
            }
        }
        return cells.Distinct().ToArray();
    }
    private static int[] MissionArea(WorldState world, PlayerAiDraft draft)
    {
        var map = new HexMap(world.Width, world.Height);
        var centers = draft.Kind == "defend" ? Targets(draft).Take(1).ToArray() : DeploymentLine(world, draft.Line);
        return centers.SelectMany(c => map.Within(c, draft.Radius, true)).Distinct().Where(c => world.Cells[c].Terrain.Active).ToArray();
    }
    private static Dictionary<int, long> StationDistances(WorldState world, UnitState unit, WorldAiRules ai, HashSet<int> requested)
    {
        var context = WorldActions.MovementContext(world, unit, ai.Movement);
        var map = new HexMap(world.Width, world.Height);
        var distances = new Dictionary<int, long> { [unit.Cell] = 0 };
        var result = new Dictionary<int, long>();
        var remaining = new HashSet<int>(requested);
        var queue = new SortedSet<(long Cost, int Cell)> { (0, unit.Cell) };
        while (queue.Count > 0) {
            var priority = queue.Min; queue.Remove(priority); int cell = priority.Cell;
            if (distances[cell] != priority.Cost) continue;
            if (remaining.Remove(cell)) { result[cell] = priority.Cost; if (remaining.Count == 0) break; }
            for (int d = 0; d < 6; d++) {
                int next = map.Neighbor(cell, d);
                if (next < 0) continue;
                int? cost = Movement.EntryCost(context.Cells[cell], context.Cells[next], (d + 3) % 6, context.Actor, ai.Movement.Search);
                if (!cost.HasValue || cost > context.Actor.FullBudget && !ai.Movement.Search.AllowOverdrawnEntry) continue;
                long total = priority.Cost + cost.Value;
                if (distances.TryGetValue(next, out long old) && old <= total) continue;
                distances[next] = total; queue.Add((total, next));
            }
        }
        return result;
    }
    private static Dictionary<string, int> AssignStations(WorldState world, PlayerAiDraft draft, WorldAiRules ai, HashSet<int>? reserved = null)
    {
        var map = new HexMap(world.Width, world.Height);
        var members = world.Units.Where(u => u.Owner == world.ActiveFaction && draft.Units.Contains(u.Id))
            .OrderBy(u => u.Combatant.Profile.MaximumRange > 1 ? 1 : 0).ThenBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal).ToArray();
        var area = MissionArea(world, draft);
        var line = draft.Kind == "defend" ? Targets(draft).Take(1).ToArray() : DeploymentLine(world, draft.Line);
        var outsiders = new HashSet<int>(world.Units.Where(u => !draft.Units.Contains(u.Id)).Select(u => u.Cell));
        var used = reserved == null ? new HashSet<int>() : new HashSet<int>(reserved);
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < members.Length; i++) {
            var unit = members[i];
            if (unit.Combatant.Profile.Category == "fort" || unit.Combatant.Profile.Movement < 1) {
                if (area.Contains(unit.Cell) && used.Add(unit.Cell)) result[unit.Id] = unit.Cell;
                continue;
            }
            var candidates = new HashSet<int>(area.Where(c => !used.Contains(c) && !outsiders.Contains(c)
                && (unit.Combatant.Profile.Category == "naval" ? world.Cells[c].Terrain.Sea : !world.Cells[c].Terrain.Sea)));
            if (candidates.Count == 0) continue;
            var distances = StationDistances(world, unit, ai, candidates);
            int anchor = line[members.Length == 1 ? line.Length / 2 : (int)((long)i * (line.Length - 1) / (members.Length - 1))];
            bool ranged = unit.Combatant.Profile.MaximumRange > 1;
            int destination = candidates.Where(distances.ContainsKey)
                .OrderBy(c => distances[c] + map.Distance(c, anchor) * 6 + (ranged && draft.Radius > 0 && line.Contains(c) ? 14 : 0)
                    + (ranged && draft.Kind == "line_attack" && draft.Goal.HasValue && map.Distance(c, draft.Goal.Value) < map.Distance(anchor, draft.Goal.Value) ? 12 : 0))
                .ThenBy(c => c).DefaultIfEmpty(-1).First();
            if (destination >= 0) { result[unit.Id] = destination; used.Add(destination); }
        }
        return result;
    }
    private static WorldState RepairStations(WorldState world, WorldAiRules ai)
    {
        var control = Control(world);
        if (control.Tasks == null) return world;
        var map = new HexMap(world.Width, world.Height);
        var tasks = control.Tasks.Select(t => {
            if (!t.Enabled || t.Draft.Kind == "attack" || t.Draft.Kind == "line_attack" && t.Deployed) return t;
            var outsiders = new HashSet<int>(world.Units.Where(u => !t.Draft.Units.Contains(u.Id)).Select(u => u.Cell));
            var stations = t.Stations.Where(p => !outsiders.Contains(p.Value)).ToDictionary(p => p.Key, p => p.Value);
            var vacancies = t.Vacancies.Concat(t.Stations.Where(p => outsiders.Contains(p.Value)).Select(p => p.Value)).Distinct().ToList();
            var centers = t.Draft.Kind == "defend" ? Targets(t.Draft).Take(1).ToArray() : DeploymentLine(world, t.Draft.Line);
            int Depth(int c) => centers.Min(center => map.Distance(c, center));
            foreach (int vacant in vacancies.ToArray()) {
                if (outsiders.Contains(vacant) || stations.ContainsValue(vacant)) continue;
                foreach (var unit in world.Units.Where(u => stations.TryGetValue(u.Id, out int current) && Depth(current) > Depth(vacant)
                        && u.Combatant.Profile.Category != "fort" && (u.Combatant.Profile.Category == "naval" ? world.Cells[vacant].Terrain.Sea : !world.Cells[vacant].Terrain.Sea))
                    .OrderBy(u => map.Distance(u.Cell, vacant)).ThenBy(u => u.Id, StringComparer.Ordinal)) {
                    if (!StationDistances(world, unit, ai, new HashSet<int> { vacant }).ContainsKey(vacant)) continue;
                    stations[unit.Id] = vacant; vacancies.Remove(vacant); break;
                }
            }
            var missing = t.Draft.Units.Where(id => !stations.ContainsKey(id)).ToArray();
            if (missing.Length > 0) foreach (var p in AssignStations(world, t.Draft with { Units = missing }, ai, new HashSet<int>(stations.Values))) stations[p.Key] = p.Value;
            return t with { Stations = stations, Vacancies = vacancies.ToArray() };
        }).ToArray();
        return world with { Factions = world.Factions.Select(f => f.Id == world.ActiveFaction ? f with { PlayerAi = control with { Tasks = tasks } } : f).ToArray() };
    }
    private static PlayerAiStep TaskStep(WorldState world, UnitState unit, WorldAiCommand? command, bool reached = false) =>
        new(command == null ? null : new(world.Revision, unit.Id, world.AiRandomState, world.AiRandomState,
            new[] { new AiCandidate<WorldAiCommand>(command, 1) }), unit.Id, reached);

    private static PlayerAiStep PlanTask(WorldState world, UnitState unit, PlayerAiTask task, WorldAiRules ai)
    {
        var draft = task.Draft;
        var order = Order(unit);
        bool advancing = draft.Kind == "line_attack" && (task.Deployed || !draft.DeployFirst);
        if (advancing) {
            var targets = Targets(order);
            bool reached = targets.Length > 0 && AtGoal(world, unit, targets[0]);
            int? target = targets.Skip(reached ? 1 : 0).Select(c => (int?)c).FirstOrDefault();
            var strike = WorldAi.StrikeCandidates(world, unit.Id, ai).OrderByDescending(c => c.Score).FirstOrDefault();
            if (draft.Policy is not ("manual" or "retreat") && strike != null && strike.Score > 0)
                return TaskStep(world, unit, strike.Action, reached);
            if (target.HasValue) return TaskStep(world, unit, GoalMove(world, unit, target.Value, ai), reached);
            return TaskStep(world, unit, null, reached);
        }
        var area = new HashSet<int>(MissionArea(world, draft));
        if (area.Contains(unit.Cell) && draft.Policy == "retreat") {
            var retreat = Retreat(world, unit, ai, area);
            if (retreat != null) return TaskStep(world, unit, retreat);
        }
        if (area.Contains(unit.Cell) && draft.Policy is not ("manual" or "retreat")) {
            var strike = WorldAi.StrikeCandidates(world, unit.Id, ai).OrderByDescending(c => c.Score).FirstOrDefault();
            if (strike != null && strike.Score > 0) return TaskStep(world, unit, strike.Action);
            var threats = world.Units.Where(u => area.Contains(u.Cell) && WorldDiplomacy.Hostile(world, unit.Owner, u.Owner, ai.NeutralCamps)).ToArray();
            if (threats.Length > 0 && draft.Policy != "hold") {
                var map = new HexMap(world.Width, world.Height);
                int original = threats.Min(e => map.Distance(unit.Cell, e.Cell));
                var range = WorldActions.MovementRange(world, unit, ai.Movement);
                var move = WorldAi.TacticalMoveCandidates(world, unit.Id, ai)
                    .Where(c => area.Contains(c.Action.Destination) && range.TryGetValue(c.Action.Destination, out var landing) && landing.Path.All(area.Contains)
                        && threats.Min(e => map.Distance(c.Action.Destination, e.Cell)) < original)
                    .OrderByDescending(c => c.Score).FirstOrDefault();
                if (move != null && move.Score > 0) return TaskStep(world, unit, move.Action);
            }
        }
        if (task.Stations.TryGetValue(unit.Id, out int station) && unit.Cell != station)
            return TaskStep(world, unit, GoalMove(world, unit, station, ai, area.Contains(unit.Cell) ? area : null));
        return TaskStep(world, unit, null);
    }
    private static WorldState UpdateDeployment(WorldState world, string owner)
    {
        return world with { Factions = world.Factions.Select(f => {
            if (f.Id != owner || f.PlayerAi?.Tasks == null) return f;
            var control = f.PlayerAi;
            var tasks = control.Tasks.Select(t => t.Enabled && t.Draft.Kind == "line_attack" && !t.Deployed
                && t.Stations.Count > 0 && t.Stations.All(p => world.Units.Any(u => u.Id == p.Key && u.Cell == p.Value))
                ? t with { Deployed = true } : t).ToArray();
            return f with { PlayerAi = control with { Tasks = tasks } };
        }).ToArray() };
    }

    public static WorldState MigrateTasks(WorldState world)
    {
        var units = world.Units.ToArray();
        var factions = world.Factions.Select(f => {
            if (f.PlayerAi == null || f.PlayerAi.Tasks != null) return f;
            var c = EnsureTasks(f.PlayerAi);
            bool wasEnabled = c.Enabled;
            c = c with { Tasks = c.Tasks!.Select(t => t with { Enabled = wasEnabled && t.Draft.UnitsConfirmed
                && t.Draft.PolicyConfirmed && t.Draft.GoalConfirmed }).ToArray() };
            var selected = new HashSet<string>(c.Draft.Units, StringComparer.Ordinal);
            for (int i = 0; i < units.Length; i++)
                if (units[i].Owner == f.Id && selected.Contains(units[i].Id)) units[i] = units[i] with { PlayerControl = Order(units[i]) with { TaskId = 1 } };
            var legacy = units.Where(u => u.Owner == f.Id && !selected.Contains(u.Id) && (Order(u).Policy != "manual" || Order(u).Goal.HasValue))
                .GroupBy(u => Order(u).Policy).ToArray();
            foreach (var group in legacy) {
                var first = Order(group.First());
                var d = new PlayerAiDraft { Policy = first.Policy, PolicyConfirmed = true, Units = group.Select(u => u.Id).ToArray(),
                    UnitsConfirmed = true, Goal = first.Goal, Goals = Targets(first), GoalConfirmed = first.Goal.HasValue };
                int id = c.NextTaskId;
                // Completed legacy orders retain their policy without requiring a new target.
                if (!d.GoalConfirmed) d = d with { Goal = group.First().Cell, Goals = new[] { group.First().Cell }, GoalConfirmed = true };
                c = c with { Tasks = c.Tasks!.Append(new PlayerAiTask { Id = id, ColorIndex = c.Tasks.Length, Enabled = wasEnabled, Draft = d }).ToArray(), NextTaskId = id + 1 };
                for (int i = 0; i < units.Length; i++) if (d.Units.Contains(units[i].Id)) units[i] = units[i] with { PlayerControl = Order(units[i]) with { TaskId = id } };
            }
            return f with { PlayerAi = c with { Enabled = c.Tasks!.Any(t => t.Enabled) } };
        }).ToArray();
        return world with { Factions = factions, Units = units };
    }
}
