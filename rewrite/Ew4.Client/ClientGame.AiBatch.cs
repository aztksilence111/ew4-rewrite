using System.Diagnostics;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private WorldAi.StepWork? pendingAiStep;
    private object AiBatch(bool compact = false)
    {
        var timer = Stopwatch.StartNew();
        var before = Require().Session;
        int steps = 0;
        object? error = null;
        try
        {
            do
            {
                var current = Require();
                var world = current.Session.World;
                var active = world.Factions.Single(f => f.Id == world.ActiveFaction);
                if (world.PendingDialogue != null || world.Phase == TurnPhase.Finished) break;
                if (active.Human)
                {
                    if (active.Id != current.Session.PlayerFaction || !(PlayerAi.Pending(world) || PlayerAi.Automatic(world))) break;
                    AdvancePlayerAi(presentation: false);
                    steps++;
                    if (Require().Session.World.ActiveFaction != before.World.ActiveFaction) break;
                    continue;
                }
                var rules = current.Rules;
                var production = rules.Production[active.Id];
                bool trusted = content.Extensions.Bus.Scripts == null && content.Extensions.Bus.Skills.Count == 0
                    && content.Extensions.Bus.Statuses.Count == 0 && PlanningScope.CanTrustStep(rules.Ai, production, rules.Turns, rules.Battle);
                using var transaction = trusted ? PlanningScope.Begin(rules.Ai, production, rules.Turns, rules.Battle, snapshots: true)
                    : PlanningScope.Suspend();
                if (compact && trusted)
                {
                    if (pendingAiStep == null || !pendingAiStep.Matches(world, rules.Ai, production, rules.Turns))
                        pendingAiStep = new(world, rules.Ai, production, rules.Turns);
                    bool complete;
                    do { complete = pendingAiStep.Advance(); }
                    while (!complete && timer.ElapsedMilliseconds < 250);
                    if (!complete) break;
                    var plan = pendingAiStep.Result!;
                    pendingAiStep = null;
                    Commit(WorldAi.ExecuteStep(world, plan, rules.Ai, production, rules.Turns, rules.Battle));
                }
                else
                {
                    pendingAiStep = null;
                    // Keep the ordinary commit, event, settlement and autosave boundaries for every step.
                    Commit(WorldAi.ExecuteNextStep(world, rules.Ai, production, rules.Turns, rules.Battle));
                }
                steps++;
                if (Require().Session.World.ActiveFaction != before.World.ActiveFaction) break;
            } while (steps < 32 && timer.ElapsedMilliseconds < 250);
        }
        catch (Exception e) when (e is RuleViolation or IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or OverflowException)
        {
            pendingAiStep = null;
            // Earlier steps may already be committed; always return their latest state with the error.
            error = new { code = e is RuleViolation rule ? rule.Code : "client.failure", message = e.Message };
        }
        var next = Require().Session;
        // Only opt-in fast playback may omit intermediate pictures. Player control,
        // event and error boundaries still return the full state.
        if (compact && error == null && (steps > 0 || pendingAiStep != null) && next.World.Phase == TurnPhase.Active
            && next.World.Round == before.World.Round
            && next.PlayerFaction == before.PlayerFaction && next.World.PendingDialogue == null
            && !next.World.Factions.Single(f => f.Id == next.World.ActiveFaction).Human
            && ScenarioRules.Pending(next.World) == null && StoryPlayback.Current(next.World) == null
            && next.World.Factions.Select(f => (f.Id, f.Name, f.Human, f.Appearance))
                .SequenceEqual(before.World.Factions.Select(f => (f.Id, f.Name, f.Human, f.Appearance))))
            return new { progressOnly = true, planning = pendingAiStep != null, revision = next.Revision, activeFaction = next.World.ActiveFaction, baseRevision = before.Revision, steps };
        pendingAiStep = null;
        return new { state = View(cellDeltaFrom: compact || steps == 0 ? null : before.World), baseRevision = before.Revision, steps, error };
    }
}
