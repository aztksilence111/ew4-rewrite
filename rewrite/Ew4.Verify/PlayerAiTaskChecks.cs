using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class PlayerAiTaskChecks
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("AI tasks: " + label); }
        void Reject(Action action, string code) {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected " + code);
        }
        var original = WorldChecks.Example();
        var player = original.Units.Single(u => u.Owner == "winner");
        player = player with { Cell = 12, RemainingMovement = 6,
            Combatant = player.Combatant with { Profile = player.Combatant.Profile with { Grade = "0", Movement = 6, AttackBands = [new(-1, 1)] } } };
        var world = original with { Width = 9, Height = 5,
            Cells = Enumerable.Range(0, 45).Select(_ => new CellState { Owner = "winner" }).ToArray(),
            Units = [original.Units.Single(u => u.Owner == "loser") with { Cell = 0 }, player,
                player with { Id = "task.extra", Cell = 13 }, player with { Id = "task.third", Cell = 14 }] };
        var ai = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var battle = new WorldBattleRules { ResolveIgnition = false, RecalculateLocalMorale = false, KillDialogueProbability = 0 };
        var turns = new WorldTurnRules { PlayerCamp = "b" };
        WorldState Config(WorldState w, string kind, string[] ids, int goal, int[]? line = null, int radius = 2) {
            w = PlayerAi.Configure(w, w.Revision, "mission", kind: kind).State;
            w = PlayerAi.Configure(w, w.Revision, "policy", policy: "free").State;
            w = PlayerAi.Configure(w, w.Revision, "units", units: ids).State;
            w = PlayerAi.Configure(w, w.Revision, "radius", radius: radius).State;
            if (line != null) w = PlayerAi.Configure(w, w.Revision, "line", line: line).State;
            if (kind != "line_defend") w = PlayerAi.Configure(w, w.Revision, "goal", goal: goal, ai: ai).State;
            return w;
        }
        WorldState Change(WorldState w, string step, int? id = null) => PlayerAi.Configure(w, w.Revision, step, taskId: id).State;
        WorldState Tick(WorldState w) => PlayerAi.ExecuteStep(w, PlayerAi.PlanStep(w, ai), ai, battle, turns).State;
        var one = Config(world, "attack", [player.Id], 21);
        one = PlayerAi.Start(one, one.Revision, ai).State;
        var two = Change(one, "task_add");
        Check(PlayerAi.Control(two).ActiveTask == 2 && PlayerAi.Control(two).Enabled && PlayerAi.Control(two).Draft.Units.Length == 0,
            "new numbered task preserves running first task");
        two = Config(two, "defend", ["task.extra"], 30, radius: 1);
        two = PlayerAi.Start(two, two.Revision, ai).State;
        var tasks = PlayerAi.Control(two).Tasks!;
        Check(tasks.Length == 2 && tasks.All(t => t.Enabled) && tasks.Select(t => t.ColorIndex).Distinct().Count() == 2, "two independently enabled tasks use distinct colors");
        Check(PlayerAi.Order(two.Units.Single(u => u.Id == "task.extra")).TaskId == 2, "unit stores task identity");
        var pending = PlayerAi.BeginEnd(two, two.Revision, turns).State;
        var stopped = PlayerAi.Stop(pending, pending.Revision).State;
        Check(PlayerAi.Pending(stopped) && PlayerAi.Control(stopped).Tasks![0].Enabled && !PlayerAi.Control(stopped).Tasks![1].Enabled,
            "stopping second task keeps first pending");
        Check(PlayerAi.Control(stopped).Queue.Contains(player.Id) && !PlayerAi.Control(stopped).Queue.Contains("task.extra"), "stop filters only selected task queue");
        var switchedPending = Change(pending, "task_select", 1);
        Check(PlayerAi.Pending(switchedPending) && PlayerAi.Control(switchedPending).Draft.Units.SequenceEqual([player.Id]), "can inspect another task at paused queue boundary");
        var selected = Change(two, "task_select", 1);
        Check(PlayerAi.Control(selected).Draft.Kind == "attack" && PlayerAi.Control(selected).Draft.Goal == 21, "switch restores independent draft");
        var stoppedFirst = PlayerAi.Stop(selected, selected.Revision).State;
        Check(!PlayerAi.Managed(stoppedFirst, stoppedFirst.Units.Single(u => u.Id == player.Id)) && PlayerAi.Managed(stoppedFirst, stoppedFirst.Units.Single(u => u.Id == "task.extra")), "stop isolates managed state");
        stoppedFirst = PlayerAi.Configure(stoppedFirst, stoppedFirst.Revision, "auto", autoBattle: true).State;
        Check(!PlayerAi.Managed(stoppedFirst, stoppedFirst.Units.Single(u => u.Id == player.Id)) && PlayerAi.Managed(stoppedFirst, stoppedFirst.Units.Single(u => u.Id == "task.third")), "global auto respects stopped task membership");
        var transferred = PlayerAi.Configure(stoppedFirst, stoppedFirst.Revision, "units", units: [player.Id, "task.extra"]).State;
        Check(PlayerAi.Control(transferred).Tasks![1].Draft.Units.Length == 0 && !PlayerAi.Control(transferred).Enabled, "transfer removes donor membership and stops empty donor");
        Check(PlayerAi.Order(transferred.Units.Single(u => u.Id == "task.extra")).TaskId == 1, "transfer updates unit task");
        var removed = PlayerAi.Configure(transferred, transferred.Revision, "units", units: [player.Id]).State;
        Check(PlayerAi.Order(removed.Units.Single(u => u.Id == "task.extra")).TaskId == 0, "deselected member returns to player command");
        var deleted = Change(transferred, "task_delete");
        Check(PlayerAi.Control(deleted).ActiveTask == 2 && PlayerAi.Order(deleted.Units.Single(u => u.Id == player.Id)).Policy == "manual", "deletion releases assigned units");
        var addedAgain = Change(deleted, "task_add");
        Check(PlayerAi.Control(addedAgain).ActiveTask == 3 && PlayerAi.Control(addedAgain).Tasks!.Select(t => t.ColorIndex).Distinct().Count() == 2, "new task reuses only available color");
        var full = world;
        for (int i = 1; i < PlayerAi.MaximumTasks; i++) full = Change(full, "task_add");
        Reject(() => Change(full, "task_add"), "player_ai.task_limit");
        Reject(() => PlayerAi.Configure(world, world.Revision, "radius", radius: 7), "player_ai.radius");
        Reject(() => Change(world, "task_select", 88), "player_ai.task");

        var guard = Config(world, "defend", [player.Id, "task.extra"], 30, radius: 1);
        Reject(() => PlayerAi.Configure(guard, guard.Revision, "goal", goals: [30, 31]), "player_ai.goal");
        guard = PlayerAi.Start(guard, guard.Revision, ai).State;
        var assignment = PlayerAi.Control(guard).Tasks![0].Stations;
        var map = new HexMap(guard.Width, guard.Height);
        Check(assignment.Count == 2 && assignment.Values.Distinct().Count() == 2 && assignment.Values.All(c => map.Distance(c, 30) <= 1), "defenders receive unique legal stations in zone");
        var centerGuard = assignment.Single(p => p.Value == 30).Key;
        var casualty = PlayerAi.Reconcile(guard with { Units = guard.Units.Where(u => u.Id != centerGuard).ToArray() });
        var filled = PlayerAi.BeginEnd(casualty, casualty.Revision, turns, ai).State;
        Check(PlayerAi.Control(filled).Tasks![0].Stations.Values.Contains(30), "remaining defender fills vacated center without reassigning whole group");
        guard = guard with { Units = guard.Units.Select(u => assignment.TryGetValue(u.Id, out int c) ? u with { Cell = c } : u).ToArray() };
        var guardPending = PlayerAi.BeginEnd(guard, guard.Revision, turns).State;
        var guardPlan = PlayerAi.PlanStep(guardPending, ai);
        Check(guardPlan.Plan == null || guardPlan.Plan.Candidates.All(c => c.Action is not AiMoveCommand m || map.Distance(m.Destination, 30) <= 1), "stationed guards cannot leave zone");
        var guardAfter = Tick(guardPending);
        Check(PlayerAi.Order(guardAfter.Units.Single(u => u.Id == player.Id)).Goal == 30, "guard objective persists on arrival");
        Check(WorldSave.Encode(guardAfter).SequenceEqual(WorldSave.Encode(WorldSave.Decode(WorldSave.Encode(guardAfter), guardAfter.ContentRevision))), "missions stations and queue round trip");
        var outsideEnemy = guardPending with { Units = guardPending.Units.Select(u => u.Owner == "loser" ? u with { Cell = 44 } : u).ToArray() };
        Check(PlayerAi.PlanStep(outsideEnemy, ai).Plan == null, "distant enemy does not pull guards out of zone");

        var lineDraft = Config(world, "line_defend", [player.Id, "task.extra"], 0, [20, 24], 1);
        Check(PlayerAi.Control(lineDraft).Draft.GoalConfirmed && PlayerAi.Control(lineDraft).Draft.Goal == null, "line defense confirms without attack target");
        var line = PlayerAi.DeploymentLine(lineDraft, [20, 24]);
        Check(line.Length == 5 && line.Zip(line.Skip(1), (a, b) => map.Distance(a, b) == 1).All(v => v), "fold points expand to adjacent hexes");
        Reject(() => PlayerAi.Configure(lineDraft, lineDraft.Revision, "line", line: [20]), "player_ai.line");
        var lineGuard = PlayerAi.Start(lineDraft, lineDraft.Revision, ai).State;
        Check(PlayerAi.Control(lineGuard).Tasks![0].Stations.Count == 2, "line defense deploys multiple units");
        var sea = lineDraft with { Cells = lineDraft.Cells.Select((c, i) => i == 20 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        var landOnly = PlayerAi.Start(sea, sea.Revision, ai).State;
        Check(PlayerAi.Control(landOnly).Tasks![0].Stations.Values.All(c => !landOnly.Cells[c].Terrain.Sea), "land units do not get water stations");
        var attackLine = Config(world, "line_attack", [player.Id], 35, [20, 24], 1);
        attackLine = PlayerAi.Start(attackLine, attackLine.Revision, ai).State;
        int station = PlayerAi.Control(attackLine).Tasks![0].Stations[player.Id];
        attackLine = attackLine with { Units = attackLine.Units.Select(u => u.Id == player.Id ? u with { Cell = station } : u).ToArray() };
        var gather = Tick(PlayerAi.BeginEnd(attackLine, attackLine.Revision, turns).State);
        Check(PlayerAi.Control(gather).Tasks![0].Deployed && PlayerAi.Order(gather.Units.Single(u => u.Id == player.Id)).Goal == 35, "deployment latches without consuming attack objective");
        var immediate = PlayerAi.Stop(attackLine, attackLine.Revision).State;
        immediate = PlayerAi.Configure(immediate, immediate.Revision, "deploy_first", deployFirst: false).State;
        immediate = PlayerAi.Start(immediate, immediate.Revision, ai).State;
        var advance = PlayerAi.PlanStep(PlayerAi.BeginEnd(immediate, immediate.Revision, turns).State, ai);
        Check(advance.Plan?.Candidates.Any(c => c.Action is AiMoveCommand) == true, "immediate line attack advances toward target");

        var envelope = JsonNode.Parse(WorldSave.Encode(one))!.AsObject();
        envelope["version"] = 7;
        foreach (var faction in envelope["payload"]!["factions"]!.AsArray()) {
            var control = faction!["playerAi"]?.AsObject();
            if (control == null) continue;
            control.Remove("tasks"); control.Remove("activeTask"); control.Remove("nextTaskId");
        }
        byte[] payload = System.Text.Encoding.UTF8.GetBytes(envelope["payload"]!.ToJsonString());
        envelope["sha256"] = Convert.ToHexStringLower(SHA256.HashData(payload));
        var migrated = WorldSave.Decode(System.Text.Encoding.UTF8.GetBytes(envelope.ToJsonString()), one.ContentRevision);
        Check(PlayerAi.Control(migrated).Tasks?.Length == 1 && PlayerAi.Managed(migrated, migrated.Units.Single(u => u.Id == player.Id)), "v7 migration preserves active legacy order");
        var legacyGroups = one with { Factions = one.Factions.Select(f => f.PlayerAi == null ? f : f with { PlayerAi = f.PlayerAi with { Tasks = null } }).ToArray(),
            Units = one.Units.Select(u => u.Id == "task.extra" ? u with { PlayerControl = new(u.Owner, "free", 25, Goals: [25, 26]) }
                : u.Id == "task.third" ? u with { PlayerControl = new(u.Owner, "free", 34, Goals: [34]) } : u).ToArray() };
        var grouped = PlayerAi.MigrateTasks(legacyGroups);
        grouped.Validate();
        Check(PlayerAi.Control(grouped).Tasks!.Length == 2 && grouped.Units.Where(u => u.Id is "task.extra" or "task.third").All(u => PlayerAi.Managed(grouped, u)),
            "legacy distinct routes share a policy batch without dropping commands");
        Check(PlayerAi.Targets(PlayerAi.Order(grouped.Units.Single(u => u.Id == "task.extra"))).SequenceEqual([25, 26])
            && PlayerAi.Targets(PlayerAi.Order(grouped.Units.Single(u => u.Id == "task.third"))).SequenceEqual([34]), "migration retains each unit remaining route");
        var invalidColors = new AiControlPresentation { TaskColors = Enumerable.Repeat("#ffffff", 16).ToArray() };
        Reject(() => AiControlPresentation.Validate(invalidColors, _ => []), "extension.ai_control");
        return checks;
    }
}
