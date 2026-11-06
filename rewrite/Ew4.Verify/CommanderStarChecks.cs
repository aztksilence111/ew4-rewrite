using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;

internal static class CommanderStarChecks
{
    public static int Run()
    {
        int count = 0;
        var original = new CommanderAbilityConfiguration { Schema = 1 };
        original.Validate();
        if (original.Stars != null) throw new InvalidOperationException("Default content enabled Mod star colors");
        count++;
        var stars = new CommanderStarPresentation { DefaultColor = "red", Palette = new() { ["red"] = "rewrite/icons/red.png" },
            GlowColors = new() { ["red"] = "#ff932d" },
            Countries = new() { ["J19"] = "red" }, Commanders = new() { ["ew4.commander.1112"] = "red" } };
        var valid = original with { Stars = stars };
        valid.Validate(); count++;
        var roundTrip = JsonSerializer.Deserialize<CommanderAbilityConfiguration>(JsonSerializer.Serialize(valid, ExtensionLoader.Json), ExtensionLoader.Json)!;
        roundTrip.Validate();
        if (roundTrip.Stars?.Palette["red"] != "rewrite/icons/red.png") throw new InvalidOperationException("Mod palette round trip");
        if (roundTrip.Stars.GlowColors["red"] != "#ff932d") throw new InvalidOperationException("Mod glow color round trip");
        count++;
        foreach (var invalid in new[] {
            stars with { Mode = "automatic" }, stars with { DefaultColor = "blue" },
            stars with { Palette = new() { ["red"] = "rewrite/../red.png" } },
            stars with { Countries = new() { ["J19"] = "blue" } },
            stars with { Commanders = new() { ["1112"] = "red" } },
            stars with { GlowColors = new() { ["red"] = "orange" } },
            stars with { GlowColors = new() { ["red"] = "#gg932d" } },
            stars with { GlowColors = new() { ["blue"] = "#9edbff" } },
            stars with { Palette = new() }, stars with { Palette = Enumerable.Range(0, 17).ToDictionary(i => "c" + i, _ => "red.png") }
        })
        {
            try { (original with { Stars = invalid }).Validate(); }
            catch (RuleViolation error) when (error.Code == "extension.commander_stars") { count++; continue; }
            throw new InvalidOperationException("Invalid star presentation was accepted");
        }
        return count;
    }
}
