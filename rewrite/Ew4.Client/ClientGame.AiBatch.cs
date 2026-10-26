using System.Diagnostics;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private object AiBatch()
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
                var plan = WorldAi.PlanStep(world, rules.Ai, production, rules.Turns);
                // Keep the ordinary commit, event, settlement and autosave boundaries for every step.
                Commit(WorldAi.ExecuteStep(world, plan, rules.Ai, production, rules.Turns, rules.Battle));
                steps++;
                if (Require().Session.World.ActiveFaction != before.World.ActiveFaction) break;
            } while (steps < 32 && timer.ElapsedMilliseconds < 250);
        }
        catch (Exception e) when (e is RuleViolation or IOException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException or OverflowException)
        {
            // Earlier steps may already be committed; always return their latest state with the error.
            error = new { code = e is RuleViolation rule ? rule.Code : "client.failure", message = e.Message };
        }
        return new { state = View(cellDeltaFrom: steps == 0 ? null : before.World), baseRevision = before.Revision, steps, error };
    }
}
