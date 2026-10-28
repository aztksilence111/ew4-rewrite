using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record FactionAppearance(string? Flag = null, string? FlagImage = null, string? Color = null);

public static class ScenarioFactions
{
    public static IEnumerable<ScenarioEffect> Creations(IEnumerable<ScriptedScenarioEvent> events)
        => events.SelectMany(ScenarioRules.Effects).Where(e => e.Type == "faction_create");

    public static string? Template(WorldState world, ScenarioEffect creation)
        => creation.TemplateFaction == "@player" ? world.ScenarioPlayerFaction! : creation.TemplateFaction;

    public static ScriptedScenarioEvent BindTemplates(ScriptedScenarioEvent ev, string player)
    {
        ScenarioEffect Bind(ScenarioEffect e) => e.TemplateFaction == "@player" ? e with { TemplateFaction = player } : e;
        return ev with { Effects = ev.Effects.Select(Bind).ToArray(),
            InsufficientResourcesEffects = ev.InsufficientResourcesEffects.Select(Bind).ToArray(),
            Popup = ev.Popup == null ? null : ev.Popup with { Choices = ev.Popup.Choices.Select(c => c with { Effects = c.Effects.Select(Bind).ToArray() }).ToArray() } };
    }

    public static bool Code(string value) => value.Length is > 0 and <= 128
        && value.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-');

    // Country IDs are data keys, not native three-letter codes. Legacy BTL fields
    // remain fixed-width; CountryCatalog aliases those values to this unrestricted ID.
    public static bool CountryId(string value) => !string.IsNullOrWhiteSpace(value) && value.All(c => !char.IsControl(c));

    public static void ValidateAppearance(FactionAppearance? appearance)
    {
        if (appearance == null) return;
        if (appearance.Flag != null && !Code(appearance.Flag)
            || appearance.Flag != null && appearance.FlagImage != null
            || appearance.Color != null && (appearance.Color.Length is not (7 or 9) || appearance.Color[0] != '#'
                || !appearance.Color.Skip(1).All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F'))
            || appearance.FlagImage is string image && (!image.StartsWith("rewrite/", StringComparison.Ordinal)
                || !image.EndsWith(".png", StringComparison.Ordinal) || image.Length >= 240 || image.Contains('\\') || image.Contains(':')
                || image.Any(char.IsControl) || image.Split('/').Any(p => p.Length == 0 || p is "." or "..")))
            throw new RuleViolation("scenario.appearance", "Invalid faction flag, package PNG path or #RRGGBB[AA] color");
    }

    public static int PackedColor(string color)
    {
        int r = Convert.ToInt32(color.Substring(1, 2), 16), g = Convert.ToInt32(color.Substring(3, 2), 16),
            b = Convert.ToInt32(color.Substring(5, 2), 16), a = color.Length == 9 ? Convert.ToInt32(color.Substring(7, 2), 16) : 255;
        return unchecked(r | g << 8 | b << 16 | a << 24);
    }

    private static FactionAppearance Appearance(FactionAppearance? old, ScenarioEffect effect) => new(
        effect.Flag ?? (effect.FlagImage != null ? null : old?.Flag),
        effect.FlagImage ?? (effect.Flag != null ? null : old?.FlagImage), effect.Color ?? old?.Color);

    internal static WorldState Identity(WorldState world, string owner, ScenarioEffect effect)
        => world with { Factions = world.Factions.Select(f => f.Id == owner
            ? f with { Name = effect.Name ?? f.Name, Appearance = Appearance(f.Appearance, effect) } : f).ToArray() };

    internal static WorldState Create(WorldState world, ScenarioEffect effect)
    {
        if (world.Factions.Any(f => f.Id == effect.Faction))
            throw new RuleViolation("scenario.faction_exists", "Faction already exists: " + effect.Faction);
        string? templateId = Template(world, effect);
        var template = templateId == null ? new FactionState() : world.Factions.SingleOrDefault(f => f.Id == templateId)
            ?? throw new RuleViolation("scenario.faction_missing", "Unknown faction template");
        var created = template with { Id = effect.Faction, Name = effect.Name!, Camp = effect.Camp!, Human = false, Defeated = false,
            HomelandBuildings = null,
            Capital = effect.Capital is int capital ? new(capital) : null,
            Appearance = Appearance(template.Appearance, effect),
            Resources = effect.InitialResources ?? new(0, 0, 0), Shortage = 0, Kills = new Dictionary<string, long>(), TurnUses = new Dictionary<string, int>(),
            Recruitment = (effect.Recruitment ?? template.Recruitment).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            Survival = template.Survival with { ExcludedBuildings = template.Survival.ExcludedBuildings.ToArray() } };
        return world with { Factions = world.Factions.Append(created).ToArray() };
    }

    internal static WorldState Revive(WorldState world, string owner)
    {
        var faction = world.Factions.Single(f => f.Id == owner);
        if (!faction.Defeated) throw new RuleViolation("scenario.faction_active", "Faction is already active: " + owner);
        return world with { Factions = world.Factions.Select(f => f.Id == owner
            ? f with { Defeated = false, Shortage = 0, TurnUses = new Dictionary<string, int>() } : f).ToArray() };
    }

    internal static WorldState Control(WorldState world, string owner)
    {
        if (world.LocalMultiplayer != null) return world;
        if (world.Phase == TurnPhase.Finished || world.Factions.Single(f => f.Id == owner).Defeated)
            throw new RuleViolation("scenario.player_control", "Player control requires a live faction in an unfinished battle");
        return world with { ScenarioPlayerFaction = owner,
            Factions = world.Factions.Select(f => f with { Human = f.Id == owner }).ToArray() };
    }
}
