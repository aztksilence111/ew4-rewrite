using System.Globalization;
using Ew4.Core;

namespace Ew4.Legacy;

public static class LegacyAiCatalog
{
    private static string Building(string id) => LegacyBuildingCatalog.Kinds[int.Parse(id, CultureInfo.InvariantCulture)];
    private static string Installation(string id) => LegacyRecruitmentCatalog.InstallationKinds[int.Parse(id, CultureInfo.InvariantCulture)];
    private static string Card(string id) => "ew4.card." + id;

    public static WorldAiRules Rules(LegacyCombatCatalog combat, WorldMoveRules? movement = null, TransportPurchaseRules? transport = null)
    {
        var original = LegacyAiTactics.Rules();
        var tactics = original with
        {
            Buildings = original.Buildings.ToDictionary(p => Building(p.Key), p => p.Value, StringComparer.Ordinal),
            Installations = original.Installations.ToDictionary(p => Installation(p.Key), p => p.Value, StringComparer.Ordinal),
            Modifiers = original.Modifiers.Select(m => m with { SourceInstallation = m.SourceInstallation == null ? null : Installation(m.SourceInstallation),
                TargetInstallation = m.TargetInstallation == null ? null : Installation(m.TargetInstallation) }).ToArray()
        };
        var landing = new AiStrategyLandingRules();
        var result = new WorldAiRules { Tactics = FrozenAiTacticalRules.Copy(tactics), Movement = movement ?? new(),
            ExperienceLevels = combat.UnitProgression.Levels.Select((v, i) => (v.Id, i)).ToDictionary(p => p.Id, p => p.i, StringComparer.Ordinal),
            Strategy = new() { Transport = transport, Landing = landing with
                { ReservedAlliedBuildings = landing.ReservedAlliedBuildings.Select(Building).ToArray(),
                    AlliedBuildingChance = landing.AlliedBuildingChance.ToDictionary(p => Building(p.Key), p => p.Value, StringComparer.Ordinal) } } };
        result.Validate(); return result;
    }

    public static WorldProductionRules Production(string variant, LegacyRuntimeCatalog runtime,
        LegacyRecruitmentCatalog cards, LegacyBuildingCatalog buildings)
    {
        var offers = cards.Offers(variant, runtime, buildings);
        var ordinary = cards.Cards.Values.Where(c => c.Type == "recruit" && offers.ContainsKey(c.Id)).ToArray();
        var fortresses = cards.Cards.Values.Where(c => c.Type == "fortress" && offers.ContainsKey(c.Id)).ToArray();
        var pool = LegacyAiOfferPool.Rules(); var installations = LegacyAiConstruction.Installations();
        var forts = LegacyAiConstruction.Fortresses(); var upgrades = new AiUpgradeRules();
        var result = new WorldProductionRules
        {
            Offers = ordinary.ToDictionary(c => c.Id, c => offers[c.Id], StringComparer.Ordinal),
            Fortresses = fortresses.ToDictionary(c => c.Id, c => offers[c.Id], StringComparer.Ordinal),
            Groups = ordinary.GroupBy(c => c.Unit).OrderBy(g => g.Key).Select(g => new ProductionOfferGroup("ew4.unit_type." + g.Key,
                g.OrderByDescending(c => c.Grade).Select(c => c.Id).ToArray(), offers[g.First().Id].Template!.Profile.Category == "naval")).ToArray(),
            Pool = pool with { Admission = pool.Admission.ToDictionary(p => Card(p.Key), p => p.Value, StringComparer.Ordinal) },
            Installations = cards.Installations(runtime.SkillParameters), Transport = cards.Transport(runtime.SkillParameters),
            BuildingUpgrades = buildings.UpgradeRules(runtime.SkillParameters),
            InstallationScoring = installations with { Recipes = installations.Recipes.Select(r => r with { Id = Card(r.Id) }).ToArray(),
                PreferenceOrder = installations.PreferenceOrder.Select(Card).ToArray() },
            FortressScoring = forts with { Recipes = forts.Recipes.Select(r => r with { Id = Card(r.Id) }).ToArray() },
            Upgrade = upgrades with { BuildingWeights = upgrades.BuildingWeights.ToDictionary(p => Building(p.Key), p => p.Value, StringComparer.Ordinal) }
        };
        result.Validate(); return result;
    }
}
