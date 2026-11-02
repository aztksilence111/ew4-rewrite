using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record CommanderAbilityDefinition(string Id, string NameKey, string Icon, int DefaultValue = 0);
public sealed record CommanderAbilityPresentation(string? NameKey = null, string? Icon = null);
public sealed record CommanderAbilityConfiguration
{
    public int Schema { get; init; }
    public CommanderAbilityDefinition[] Abilities { get; init; } = [];
    public Dictionary<string, Dictionary<string, int>> Commanders { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> Units { get; init; } = new(StringComparer.Ordinal);
    public InheritedAbilityRule[] Inheritance { get; init; } = [];
    public string[] Order { get; init; } = [];
    public Dictionary<string, CommanderAbilityPresentation> Presentation { get; init; } = new(StringComparer.Ordinal);

    public void Validate()
    {
        string[] reserved = ["infantry", "cavalry", "artillery", "naval", "fort", "commerce", "movement", "training"];
        bool Id(string? id) => id is { Length: > 0 and <= 128 } && id.Contains('.')
            && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        bool Icon(string? icon) => icon != null && icon.EndsWith(".png", StringComparison.Ordinal)
            && (ExtensionLoader.PathValid(icon) || icon.Length < 128 && icon.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'));
        if (Schema != 1 || Abilities == null || Commanders == null || Units == null || Inheritance == null || Order == null || Presentation == null
            || Abilities.Length > 32 || Abilities.Any(a => a == null || !Id(a.Id) || reserved.Contains(a.Id)
                || string.IsNullOrWhiteSpace(a.NameKey) || a.NameKey.Length > 128 || !Icon(a.Icon) || a.DefaultValue < 0)
            || Abilities.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != Abilities.Length)
            throw new RuleViolation("extension.commander_abilities", "Invalid ability definitions (at most 32 unique custom abilities)");
        var known = Abilities.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var visible = known.Concat(reserved).ToHashSet(StringComparer.Ordinal);
        if (Order.Any(id => !visible.Contains(id)) || Order.Distinct(StringComparer.Ordinal).Count() != Order.Length
            || Presentation.Any(p => !visible.Contains(p.Key) || p.Value == null
                || p.Value.NameKey != null && (string.IsNullOrWhiteSpace(p.Value.NameKey) || p.Value.NameKey.Length > 128)
                || p.Value.Icon != null && !Icon(p.Value.Icon)))
            throw new RuleViolation("extension.commander_abilities", "Invalid ability order or presentation override");
        if (Commanders.Any(p => !Id(p.Key) || p.Value == null || p.Value.Any(v => !known.Contains(v.Key) || v.Value < 0))
            || Units.Any(p => !Id(p.Key) || !known.Contains(p.Value))
            || Inheritance.Any(r => r == null || !Id(r.Skill) || !known.Contains(r.Ability) || r.Gain < 0 || r.Maximum < 0)
            || Inheritance.GroupBy(r => (r.Skill, r.Ability)).Any(g => g.Count() > 1))
            throw new RuleViolation("extension.commander_abilities", "Invalid ability values, unit bindings or inheritance");
    }

    internal void Patch(JsonObject catalog, IEnumerable<string> extensionSkills)
    {
        var commanders = catalog["commanders"]!.AsArray().ToDictionary(c => (string)c!["id"]!, StringComparer.Ordinal);
        var units = catalog["profiles"]!.AsArray().ToDictionary(c => (string)c!["id"]!, StringComparer.Ordinal);
        var skills = catalog["skills"]!.AsArray().Select(s => (string)s!["id"]!).Concat(extensionSkills).ToHashSet(StringComparer.Ordinal);
        if (Commanders.Keys.Any(id => !commanders.ContainsKey(id)) || Units.Keys.Any(id => !units.ContainsKey(id))
            || Inheritance.Any(r => !skills.Contains(r.Skill)))
            throw new RuleViolation("extension.commander_abilities", "Unknown commander, unit or teaching skill");
        foreach (var (id, commander) in commanders)
            commander!["commander_abilities"] = JsonSerializer.SerializeToNode(Abilities.ToDictionary(a => a.Id,
                a => Commanders.GetValueOrDefault(id)?.GetValueOrDefault(a.Id, a.DefaultValue) ?? a.DefaultValue), ExtensionLoader.Json);
        foreach (var (id, ability) in Units) units[id]!["commander_ability"] = ability;
    }
}
