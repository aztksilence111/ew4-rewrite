using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private GameSession ReadCompatibleSession(ClientSlot slot, LegacyScenarioStart current)
    {
        IEnumerable<ClientContent> Candidates()
        {
            if (content.CorrectOriginalEquipment) yield return content.WithoutEquipmentCorrections();
            var legacy = content.WithLegacyCatalogFormatting();
            yield return legacy;
            if (legacy.CorrectOriginalEquipment) yield return legacy.WithoutEquipmentCorrections();
        }
        foreach (var candidate in Candidates())
        {
            var prior = candidate.Start(slot.Scenario, slot.Country, localPlayers: slot.LocalPlayers, technologySnapshot: slot.TechnologySnapshot);
            GameSession state;
            try
            {
                // Authenticate all three bindings and both checksums before migrating.
                state = SessionSave.Decode(slot.Session, new(prior.Session.ContentRevision, prior.Session.World.ContentRevision, prior.Session.Account.ContentRevision));
            }
            catch (RuleViolation error) when (error.Code == "save.content_revision") { continue; }
            return UpgradeEquipment(state, prior, current);
        }
        using var document = System.Text.Json.JsonDocument.Parse(slot.Session);
        var payload = document.RootElement.GetProperty("payload");
        var world = payload.GetProperty("world").GetProperty("payload");
        var snapshot = SessionSave.Decode(slot.Session, new(payload.GetProperty("contentRevision").GetString()!,
            world.GetProperty("contentRevision").GetString()!, payload.GetProperty("account").GetProperty("contentRevision").GetString()!));
        return ReuseSnapshot(snapshot, current);
    }

    private static GameSession UpgradeEquipment(GameSession state, LegacyScenarioStart prior, LegacyScenarioStart current)
    {
        Dictionary<string, Commander> Definitions(RosterDeploymentRules roster, WorldCommanderRules scenario)
        {
            var definitions = scenario.Definitions.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            foreach (var owned in state.Account.Roster) definitions[owned.Id] = AccountCommanders.Resolve(owned, roster.Account);
            return definitions;
        }
        var oldDefinitions = Definitions(prior.Rules.Roster, prior.Rules.Commanders);
        var definitions = Definitions(current.Rules.Roster, current.Rules.Commanders);
        var units = state.World.Units.Select(unit =>
        {
            if (unit.CommanderId == null) return unit;
            if (!oldDefinitions.TryGetValue(unit.CommanderId, out var oldDefinition)
                || !definitions.TryGetValue(unit.CommanderId, out var definition))
                throw new RuleViolation("client.equipment_migration", "Cannot resolve saved commander's equipment");
            var old = Ew4Commander.Resolve(oldDefinition, unit.Combatant.Profile.Category, prior.Rules.Commanders.LegacySkills);
            var effect = Ew4Commander.Resolve(definition, unit.Combatant.Profile.Category, current.Rules.Commanders.LegacySkills);
            int movement = unit.Combatant.Profile.Category == "fort" ? 0 : effect.MovementBonus - old.MovementBonus;
            return unit with
            {
                Combatant = unit.Combatant with
                {
                    AttackBonus = checked(unit.Combatant.AttackBonus + effect.AttackEquipment - old.AttackEquipment),
                    DefenseBonus = checked(unit.Combatant.DefenseBonus + effect.DefenseEquipment - old.DefenseEquipment),
                    Profile = unit.Combatant.Profile with { Movement = checked(unit.Combatant.Profile.Movement + movement) }
                },
                RemainingMovement = unit.RemainingMovement == 0 ? 0 : Math.Max(0, checked(unit.RemainingMovement + movement)),
                ExtensionMovementLimit = unit.ExtensionMovementLimit.HasValue ? checked(unit.ExtensionMovementLimit.Value + movement) : null,
                Support = unit.Support with
                {
                    FieldHealing = checked(unit.Support.FieldHealing + effect.FieldHealing - old.FieldHealing),
                    AdjacentHealing = checked(unit.Support.AdjacentHealing + effect.AdjacentHealing - old.AdjacentHealing),
                    AdjacentAttack = checked(unit.Support.AdjacentAttack + effect.AdjacentAttack - old.AdjacentAttack),
                    AdjacentDefense = checked(unit.Support.AdjacentDefense + effect.AdjacentDefense - old.AdjacentDefense)
                }
            };
        }).ToArray();
        int? Percent(Commander definition, int effect) => definition.Items.Where(i => i.Effect == Ew4Commander.Effect(effect))
            .Select(i => (int?)i.Value).FirstOrDefault();
        var commanders = state.World.Commanders.Select(commander => !definitions.TryGetValue(commander.Id, out var definition) ? commander
            : commander with { RankExperiencePercent = Percent(definition, 1), TitleExperiencePercent = Percent(definition, 2) }).ToArray();
        var next = state with
        {
            ContentRevision = current.Session.ContentRevision,
            Account = state.Account.ContentRevision == current.Session.Account.ContentRevision ? state.Account
                : state.Account with { ContentRevision = current.Session.Account.ContentRevision, Revision = checked(state.Account.Revision + 1) },
            World = state.World with { ContentRevision = current.Session.World.ContentRevision, Units = units, Commanders = commanders }
        };
        next.Validate();
        return next;
    }
}
