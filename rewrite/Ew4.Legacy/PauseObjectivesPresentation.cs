using System.Text.Json;
using System.Text.RegularExpressions;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record PauseObjectivesPresentation
{
    public string Title { get; init; } = "战局目标";
    public int Width { get; init; } = 370;
    public int MaxHeight { get; init; } = 662;
    public int FontSize { get; init; } = 16;
    public int RowGap { get; init; } = 3;
    public int SlideMilliseconds { get; init; } = 280;
    public string Background { get; init; } = "#e9e7e1";
    public string HeaderBackground { get; init; } = "#414442";
    public string TextColor { get; init; } = "#393b39";
    public string AccentColor { get; init; } = "#58735e";
    public string? BackgroundImage { get; init; }
    public string StarImage { get; init; } = "star_middle.png";
    public string EmptyStarImage { get; init; } = "star_campaign_gray.png";
    public string WinTitle { get; init; } = "胜利条件";
    public string LimitsTitle { get; init; } = "失败限制";
    public string RatingsTitle { get; init; } = "胜利评价";
    public string[] Sections { get; init; } = ["win", "limits", "ratings"];
    public Dictionary<string, string> ConditionText { get; init; } = new();

    public static void Validate(IReadOnlyDictionary<string, PauseObjectivesPresentation> screens, byte[] manifestBytes,
        Func<string, byte[]> read)
    {
        using var manifest = JsonDocument.Parse(manifestBytes);
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var atlas in manifest.RootElement.GetProperty("atlases").EnumerateObject())
            foreach (var image in atlas.Value.EnumerateObject()) images.TryAdd(image.Name, atlas.Name);
        foreach (var (scenario, screen) in screens)
        {
            void Fail() => throw new RuleViolation("extension.pause_objectives", "Invalid pause objectives presentation: " + scenario);
            if (string.IsNullOrWhiteSpace(scenario) || screen == null) { Fail(); continue; }
            if (screen.Width is < 240 or > 480 || screen.MaxHeight is < 240 or > 1000 || screen.FontSize is < 14 or > 24
                || screen.RowGap is < 0 or > 16 || screen.SlideMilliseconds is < 0 or > 800
                || screen.Sections == null || screen.Sections.Length != 3 || screen.Sections.Distinct().Count() != 3
                || screen.Sections.Any(s => s is not ("win" or "limits" or "ratings"))
                || new[] { screen.Title, screen.WinTitle, screen.LimitsTitle, screen.RatingsTitle }.Any(s => s == null || s.Length > 256)
                || new[] { screen.Background, screen.HeaderBackground, screen.TextColor, screen.AccentColor }
                    .Any(s => s == null || !Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$"))
                || screen.ConditionText == null || screen.ConditionText.Count > 1024
                || screen.ConditionText.Any(p => !Regex.IsMatch(p.Key, "^(win|lose|limits)(\\.[a-zA-Z0-9_]+)*$")
                    || p.Value == null || p.Value.Length > 2048)) Fail();
            foreach (string? name in new[] { screen.StarImage, screen.EmptyStarImage }.Concat(screen.BackgroundImage == null ? [] : new[] { screen.BackgroundImage }))
            {
                if (name == null || !images.TryGetValue(name, out var atlas)) { Fail(); continue; }
                var bytes = read("client/ui/" + atlas + "/" + name);
                if (bytes.Length > 4 * 1024 * 1024 || bytes.Length < 8 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) Fail();
            }
        }
    }
}
