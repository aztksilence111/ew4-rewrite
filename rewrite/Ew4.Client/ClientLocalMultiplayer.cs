using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private object LocalPlayers(ClientCommand command)
    {
        var scenario = content.Scenarios.Single(s => s.Id == command.Scenario);
        if (scenario.Mode is not (1 or 2)) throw new RuleViolation("local.mode", "Local multiplayer requires a campaign or conquest");
        if (!CampaignProgress.Accessible(content, Account(), scenario.Id)) throw new RuleViolation("campaign.locked", "Campaign is locked");
        var battle = content.Battle(scenario.Id);
        int primary = scenario.Mode == 2 ? battle.Factions.ToList().FindIndex(f => content.Countries.Canonical(f.CountryId) == (command.Country == null ? null : content.Countries.Canonical(command.Country)))
            : battle.Factions.ToList().FindIndex(f => !f.Ai);
        if (primary < 0 || scenario.Mode == 2 && battle.Factions[primary].Camp == 4)
            throw new RuleViolation("local.selection", "Choose a playable initial country first");
        var order = new[] { primary }.Concat(Enumerable.Range(0, battle.Factions.Count).Where(i => i != primary));
        return order.Where(i => scenario.Mode != 2 || battle.Factions[i].Camp != 4)
            .Select(i => new { id = LegacyScenarioInitialization.FactionId(scenario.Id, i, battle.Factions[i]),
            name = content.CountryName(battle.Factions[i].CountryId), country = content.Countries.Canonical(battle.Factions[i].CountryId),
            flag = content.Country(battle.Factions[i].CountryId)?.Flag ?? battle.Factions[i].Variant,
            flagImage = content.Country(battle.Factions[i].CountryId)?.FlagImage, locked = i == primary }).ToArray();
    }

    private void ValidateLocalSelection(ClientCommand command)
    {
        if (command.LocalPlayers == null || content.Scenarios.Single(s => s.Id == command.Scenario).Mode != 2) return;
        var battle = content.Battle(command.Scenario!);
        if (battle.Factions.Select((f, i) => (f, i)).Any(p => p.f.Camp == 4
            && command.LocalPlayers.Contains(LegacyScenarioInitialization.FactionId(command.Scenario!, p.i, p.f))))
            throw new RuleViolation("local.selection", "Neutral conquest countries cannot be selected as local players");
    }

    private object? LocalMultiplayerView(GameSession session)
    {
        var local = session.World.LocalMultiplayer;
        if (local == null) return null;
        return new { local.MainFaction, local.HumanFactions, local.WinnerFaction, local.ShareAlliedVictory,
            results = local.WinnerFaction == null ? null : session.World.Factions.Select(f => new {
                f.Id, f.Name, outcome = LocalMultiplayerGame.Won(session.World, f.Id, local.WinnerFaction) ? "Victory" : "Defeat" }).ToArray() };
    }
}
