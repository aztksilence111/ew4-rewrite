using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private bool ConquestDiplomacy() => content.Scenarios.Single(s => s.Id == Require().Session.World.ScenarioId).Mode == 2;

    private object DiplomacyView(ClientCommand command)
    {
        var current = Require();
        if (!ConquestDiplomacy()) throw new RuleViolation("diplomacy.mode", "外交仅在征服模式开放");
        var player = current.Session.World.Factions.Single(f => f.Id == current.Session.PlayerFaction);
        return new { current.Session.Revision, player = current.Session.PlayerFaction, presentation = content.Diplomacy.Presentation,
            playerCapital = player.Capital, playerCapitalName = player.Capital is { } ownCapital ? current.Session.World.Cells[ownCapital.Cell].Name ?? (ownCapital.Cell + 1).ToString() : null,
            factions = current.Session.World.Factions.Where(f => f.Id != current.Session.PlayerFaction)
                .Select(f => new { f.Id, quote = DemandDiplomacy.Quote(current.Session.World, current.Session.PlayerFaction, f.Id),
                    capital = f.Capital, capitalName = f.Capital is { } capital ? current.Session.World.Cells[capital.Cell].Name ?? (capital.Cell + 1).ToString() : null }).ToArray() };
    }

    private object DiplomacyAction(ClientCommand command)
    {
        if (!ConquestDiplomacy()) throw new RuleViolation("diplomacy.mode", "外交仅在征服模式开放");
        var current = Require();
        var change = command.Op == "declare_war"
            ? DemandDiplomacy.DeclareWar(current.Session.World, current.Session.World.Revision, current.Session.PlayerFaction, command.Target!)
            : DemandDiplomacy.Demand(current.Session.World, current.Session.World.Revision, current.Session.PlayerFaction, command.Target!);
        Commit(change, autoSave: false);
        movementHistory.Reset(game!.Session.World);
        AutoSave();
        return View();
    }
}
