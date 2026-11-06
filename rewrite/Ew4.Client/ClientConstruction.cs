using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed partial class ClientContent
{
    private LegacyScenarioStart ConfigureConstruction(LegacyScenarioStart start, IReadOnlyDictionary<string, string> countries)
    {
        var config = Extensions.Construction;
        if (config?.Enabled != true) return start;
        var world = start.Session.World; var rules = start.Rules;
        var buildings = config.Buildings.Where(p => WorldConstruction.Available(world, p.Value)).ToArray();
        var ai = rules.Ai with { Tactics = rules.Ai.Tactics with { Buildings = rules.Ai.Tactics.Buildings.Concat(buildings
            .Select(p => new KeyValuePair<string, AiBuildingValue>(p.Key, new(p.Value.AiValue, p.Value.AiValuePerLevel)))).ToDictionary(p => p.Key, p => p.Value) } };
        var upgrades = rules.Upgrades with { Steps = rules.Upgrades.Steps.Concat(buildings.SelectMany(p => p.Value.Levels
            .Skip(1).Select((l, i) => new BuildingUpgradeStep(p.Key, i + 1, i + 2, l.UpgradeCost)))).ToArray() };
        var turns = rules.Turns with { Buildings = rules.Turns.Buildings.Concat(buildings.SelectMany(p => p.Value.Levels
            .Select((l, i) => new BuildingTurnRule(p.Key, i + 1, l.Income, l.Healing)))).ToArray() };
        var battle = rules.Battle with { Context = rules.Battle.Context with { Buildings = rules.Battle.Context.Buildings.Concat(buildings
            .SelectMany(p => p.Value.Levels.Select((l, i) => new BuildingDefenseRule(p.Key, i + 1, l.DefensePercent)))).ToArray() } };
        var recruitment = rules.Recruitment.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var production = rules.Production.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        var additions = new Dictionary<string, Dictionary<string, RecruitmentGrant>>(StringComparer.Ordinal);
        foreach (string faction in recruitment.Keys.ToArray())
        {
            var extra = new Dictionary<string, RecruitmentOffer>(StringComparer.Ordinal);
            var siteOffers = buildings.SelectMany(b => b.Value.Recruitment.Select(r => (Offer: r, Kinds: (IReadOnlyList<string>)new[] { b.Key })))
                .Concat((config.Recruitment ?? Array.Empty<ModBuildingRecruit>()).Select(r => (Offer: r, Kinds: r.BuildingKinds!)));
            foreach (var entry in siteOffers)
            {
                var r = entry.Offer;
                if (r.Scenarios.Count > 0 && !r.Scenarios.Contains(world.ScenarioId)
                    || r.Countries.Count > 0 && (!countries.TryGetValue(faction, out string? country) || !r.Countries.Contains(country))) continue;
                var definition = Catalogs.Runtime.Profiles[r.Profile];
                extra.Add(r.Id, new RecruitmentOffer { Id = r.Id, Template = new Combatant { Profile = definition.Profile, Health = definition.Profile.MaxHealth },
                    Cost = r.Cost, BuildingKinds = entry.Kinds, MinimumBuildingLevel = r.MinimumLevel, AllowSea = false,
                    Support = new UnitSupport { FoodConsumption = definition.FoodConsumption } });
            }
            if (extra.Keys.Any(recruitment[faction].ContainsKey)) throw new RuleViolation("construction.recruitment", "Custom recruitment IDs collide with existing cards");
            recruitment[faction] = recruitment[faction].Concat(extra).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var policy = production[faction];
            production[faction] = policy with { BuildingUpgrades = upgrades, Offers = policy.Offers.Concat(extra).ToDictionary(p => p.Key, p => p.Value),
                Upgrade = policy.Upgrade with { BuildingWeights = policy.Upgrade.BuildingWeights.Concat(buildings
                    .Select(p => new KeyValuePair<string, long>(p.Key, p.Value.AiUpgradeWeight))).ToDictionary(p => p.Key, p => p.Value) },
                Groups = policy.Groups.Concat(extra.Keys.Select(id => new ProductionOfferGroup(id, new[] { id }))).ToArray() };
            additions[faction] = extra.ToDictionary(p => p.Key, _ => new RecruitmentGrant(), StringComparer.Ordinal);
        }
        IReadOnlyDictionary<string, RecruitmentGrant> Grants(string id, IReadOnlyDictionary<string, RecruitmentGrant>? old) =>
            (old ?? new Dictionary<string, RecruitmentGrant>()).Concat(additions.GetValueOrDefault(id) ?? new()).ToDictionary(p => p.Key, p => p.Value);
        ScenarioEffect Effect(ScenarioEffect e) => e.Type == "faction_create" ? e with { Recruitment = Grants(e.Faction, e.Recruitment) } : e;
        world = world with { Factions = world.Factions.Select(f => f with { Recruitment = Grants(f.Id, f.Recruitment) }).ToArray(),
            ScenarioEvents = world.ScenarioEvents.Select(ev => ev with { Effects = ev.Effects.Select(Effect).ToArray(),
                InsufficientResourcesEffects = ev.InsufficientResourcesEffects.Select(Effect).ToArray(),
                Popup = ev.Popup == null ? null : ev.Popup with { Choices = ev.Popup.Choices.Select(c => c with { Effects = c.Effects.Select(Effect).ToArray() }).ToArray() } }).ToArray() };
        world.Validate(); turns.Validate(); upgrades.Validate(); battle.Validate(); ai.Validate();
        foreach (var policy in production.Values) policy.Validate();
        return start with { Session = start.Session with { World = world }, Rules = rules with { Turns = turns, Battle = battle, Upgrades = upgrades,
            Recruitment = recruitment, Production = production, Ai = ai } };
    }
}
