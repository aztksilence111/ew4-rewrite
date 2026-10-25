using Ew4.Core;

namespace Ew4.Legacy;

public sealed record MapColorsPresentation
{
    public string[]? CampPalette { get; init; }
    public Dictionary<string, string> Camps { get; init; } = new(StringComparer.Ordinal);

    public static void Validate(IReadOnlyDictionary<string, MapColorsPresentation> configurations)
    {
        foreach (var (scenario, config) in configurations)
        {
            if (string.IsNullOrWhiteSpace(scenario) || config == null || config.Camps == null || config.Camps.Count > 256
                || config.Camps.Keys.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
                || config.CampPalette is { Length: < 1 or > 64 })
                throw new RuleViolation("extension.map_colors", "Invalid map colors: " + scenario);
            foreach (string color in (config.CampPalette ?? []).Concat(config.Camps.Values))
            {
                if (color == null) throw new RuleViolation("extension.map_colors", "Missing map color: " + scenario);
                ScenarioFactions.ValidateAppearance(new(Color: color));
            }
        }
    }

    public static MapColorsPresentation Resolve(IReadOnlyDictionary<string, MapColorsPresentation> configurations, string scenario)
    {
        configurations.TryGetValue("*", out var global);
        configurations.TryGetValue(scenario, out var local);
        var camps = new Dictionary<string, string>(global?.Camps ?? new(), StringComparer.Ordinal);
        if (local != null) foreach (var (id, color) in local.Camps) camps[id] = color;
        return new() { CampPalette = local?.CampPalette ?? global?.CampPalette, Camps = camps };
    }
}
