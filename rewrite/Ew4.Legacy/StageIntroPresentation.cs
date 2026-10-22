using System.Globalization;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record StageIntroContent
{
    public string Type { get; init; } = "text";
    public string? Text { get; init; }
    public string? Image { get; init; }
    public string? Commander { get; init; }
    public int? Width { get; init; }
}

public sealed record StageIntroPresentation
{
    public VictoryLayoutNode? Layout { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Commander { get; init; }
    public string? Objectives { get; init; }
    public string? VictoryText { get; init; }
    public string? BestVictoryText { get; init; }
    public StageIntroContent[] Content { get; init; } = [];
    public Dictionary<string, string?> Bindings { get; init; } = new()
    {
        ["description"] = "text_desc", ["commander"] = "tcmder",
        ["minimumRatingRound"] = "text_victory", ["highestRatingRound"] = "text_bestvic"
    };
    public static void Validate(IReadOnlyDictionary<string, StageIntroPresentation> screens, byte[] manifestBytes, Func<string, byte[]> read)
    {
        using var manifest = JsonDocument.Parse(manifestBytes);
        var root = manifest.RootElement;
        var defaults = root.GetProperty("layouts").GetProperty("form_stageintro").Deserialize<VictoryLayoutNode>(ExtensionLoader.Json)!;
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var atlas in root.GetProperty("atlases").EnumerateObject())
            foreach (var image in atlas.Value.EnumerateObject()) images.TryAdd(image.Name, atlas.Name);
        [DoesNotReturn] static void Fail(string message) => throw new RuleViolation("extension.stage_intro", message);
        var checkedImages = new HashSet<string>();long imageBytes = 0;
        void Png(string path)
        {
            if (!checkedImages.Add(path)) return;
            byte[] data;
            try { data = read(path); } catch (IOException) { Fail("Missing stage intro image: " + path); return; }
            if (data.Length is < 24 or > 4 * 1024 * 1024 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8)) Fail("Invalid stage intro PNG: " + path);
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)), h = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20));
            if (w is 0 or > 4096 || h is 0 or > 4096 || (long)w * h > 8_388_608 || (imageBytes += data.Length) > 16 * 1024 * 1024)
                Fail("Stage intro images exceed size budget");
        }
        void Image(string name)
        {
            if (name.Contains('/') || name.Contains('\\') || !images.TryGetValue(name, out var atlas)) Fail("Unknown stage intro image: " + name);
            else Png("client/ui/" + atlas + "/" + name);
        }
        void Commander(string id)
        {
            string name = id + ".png";
            string? atlas = new[] { "princesses_hd", "commanders1_hd", "commanders2_hd" }
                .FirstOrDefault(a => root.GetProperty("atlases").TryGetProperty(a, out var entries) && entries.TryGetProperty(name, out _));
            if (atlas == null) Fail("Unknown stage intro commander: " + id);
            Png("client/ui/" + atlas + "/" + name);
        }
        foreach (var (scenario, screen) in screens)
        {
            if (string.IsNullOrWhiteSpace(scenario) || screen == null || screen.Bindings == null) Fail("Invalid stage intro screen");
            if (screen!.Commander is { Length: > 0 } commander) Commander(commander);
            if (new[] { screen.Title, screen.Description, screen.Objectives, screen.VictoryText, screen.BestVictoryText }.Any(s => s?.Length > 16384))
                Fail("Stage intro text exceeds 16384 characters");
            if (screen.Content == null || screen.Content.Length > 64) Fail("Stage intro content exceeds 64 blocks");
            foreach (var block in screen.Content!)
            {
                if (block == null || block.Width is < 1 or > 2048 || block.Text?.Length > 16384) Fail("Invalid stage intro content");
                switch (block!.Type)
                {
                    case "text":
                        if (block.Text == null || block.Image != null || block.Commander != null || block.Width != null) Fail("Invalid stage intro text block");
                        break;
                    case "image":
                        if (string.IsNullOrWhiteSpace(block.Image) || block.Commander != null) Fail("Invalid stage intro image block");
                        if (block.Image!.Contains('/'))
                        {
                            if (!ExtensionLoader.PathValid(block.Image) || !block.Image.EndsWith(".png", StringComparison.Ordinal)) Fail("Invalid stage intro image path");
                            Png(block.Image);
                        }
                        else Image(block.Image);
                        break;
                    case "commander":
                        if (string.IsNullOrWhiteSpace(block.Commander) || block.Image != null) Fail("Invalid stage intro commander block");
                        Commander(block.Commander!);break;
                    default: Fail("Unknown stage intro content type");break;
                }
            }
            if (screen.Content.Sum(b => b.Text?.Length ?? 0) > 65536) Fail("Stage intro content text exceeds 65536 characters");
            var nodes = new Dictionary<string, VictoryLayoutNode>();int count = 0;
            void Visit(VictoryLayoutNode node, string path, int depth)
            {
                if (node == null || node.Attributes == null || node.Children == null || depth > 12 || ++count > 256) Fail("Invalid stage intro layout tree");
                var a = node!.Attributes;
                if (a.Any(p => p.Value == null)) Fail("Null layout attribute");
                foreach (string field in new[] { "x", "y", "w", "h", "scale", "hscale", "vscale" })
                    if (a.TryGetValue(field, out var text) && (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                        || !double.IsFinite(n) || Math.Abs(n) > 100000 || field is "w" or "h" or "scale" or "hscale" or "vscale" && n <= 0))
                        Fail("Invalid stage intro layout number: " + field);
                if (depth == 0)
                {
                    foreach (string field in new[] { "w", "h" })
                        if (!a.TryGetValue(field, out string? text) || !int.TryParse(text, out int n) || n is < 80 or > 2048) Fail("Invalid stage intro window size");
                }
                else
                {
                    string id = a.GetValueOrDefault("id") ?? "", type = a.GetValueOrDefault("type") ?? "";
                    if (string.IsNullOrWhiteSpace(id) || id.Contains('/') || type is not ("group" or "groupbox" or "text" or "image" or "listbox" or "grid" or "button" or "tmp_button" or "tmp_intitle" or "tmp_commander"))
                        Fail("Invalid stage intro control");
                    path = path.Length == 0 ? id : path + "/" + id;
                    if (!nodes.TryAdd(path, node)) Fail("Duplicate stage intro control: " + path);
                    foreach (string field in new[] { "name", "frm1", "frm2", "frame" }) if (a.TryGetValue(field, out var image)) Image(image);
                }
                foreach (var child in node.Children) Visit(child, path, depth + 1);
            }
            Visit(screen.Layout ?? defaults, "", 0);
            var resolved = new Dictionary<string, string>();
            foreach (var (binding, target) in screen.Bindings!)
            {
                if (binding is not ("description" or "commander" or "objectives" or "minimumRatingRound" or "highestRatingRound" or "victoryLabel" or "bestVictoryLabel" or "close")) Fail("Unknown stage intro binding: " + binding);
                if (target == null) continue;
                var matches = nodes.Where(p => p.Key == target || !target.Contains('/') && p.Key.EndsWith('/' + target, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) Fail("Missing or ambiguous stage intro binding: " + target);
                string type = matches[0].Value.Attributes["type"];
                if (!(binding == "close" ? type.Contains("button") : binding == "commander" ? type is "group" or "groupbox" or "tmp_commander" : type == "text")) Fail("Invalid stage intro binding type: " + binding);
                resolved[binding] = matches[0].Key;
            }
            if (resolved.Values.Distinct().Count() != resolved.Count || resolved.Any(p => resolved.Any(q => q.Key != p.Key && q.Value.StartsWith(p.Value + "/", StringComparison.Ordinal))))
                Fail("Stage intro bindings must not overwrite another bound control");
            if (screen.Content.Length > 0 && !resolved.ContainsKey("description")) Fail("Stage intro content requires a description binding");
        }
    }
}
