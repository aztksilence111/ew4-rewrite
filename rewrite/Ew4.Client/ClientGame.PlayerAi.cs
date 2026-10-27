using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private object PlayerAiAction(ClientCommand command)
    {
        var current = Require(); var world = current.Session.World;
        if (command.Op != "player_ai_stop" && (world.ActiveFaction != current.Session.PlayerFaction || !world.Factions.Single(f => f.Id == world.ActiveFaction).Human))
            throw new RuleViolation("client.player_turn", "Player does not own the active turn");
        var change = command.Op switch
        {
            "player_ai_start" => PlayerAi.Start(world, world.Revision, current.Rules.Ai),
            "player_ai_stop" => PlayerAi.Stop(world, world.Revision, current.Session.PlayerFaction),
            "player_ai_config" => PlayerAi.Configure(world, world.Revision, command.Offer ?? "", command.Policy, command.Units, command.Cell, command.AutoBattle, command.Goals,
                command.TaskId, command.Kind, command.Radius, command.Line, command.DeployFirst, current.Rules.Ai),
            _ => throw new RuleViolation("client.command", "Unknown player AI command")
        };
        Commit(change);
        if (command.Op is "player_ai_start" or "player_ai_stop") AutoSave();
        return View();
    }

    private object? AdvancePlayerAi(bool presentation = true)
    {
        var current = Require(); var world = current.Session.World; var rules = current.Rules;
        if (world.ActiveFaction != current.Session.PlayerFaction || world.PendingDialogue != null || world.Phase != TurnPhase.Active)
            throw new RuleViolation("player_ai.owner", "Player AI cannot act at this boundary");
        if (!PlayerAi.Pending(world))
        {
            if (!PlayerAi.Automatic(world)) throw new RuleViolation("player_ai.inactive", "Player AI is not running");
            Commit(PlayerAi.BeginEnd(world, world.Revision, rules.Turns, rules.Ai));
            return null;
        }
        var step = PlayerAi.PlanStep(world, rules.Ai);
        var combat = presentation && step.Plan != null ? AiCombat(world, step.Plan) : null;
        Commit(PlayerAi.ExecuteStep(world, step, rules.Ai, rules.Battle, rules.Turns));
        movementHistory.Invalidate(Require().Session.World);
        return combat;
    }
}
