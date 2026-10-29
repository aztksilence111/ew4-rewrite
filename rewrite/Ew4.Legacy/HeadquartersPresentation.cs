using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record HeadquartersAction(string Command, int? Section = null, string? Page = null, string? EditingText = null);
public sealed record HeadquartersRoster(int Rows = 2, double CardScale = 1, int Gap = 6, bool ShowEmpty = true,
    bool ShowInfo = true, bool ShowPromotion = true);
public sealed record HeadquartersScreen
{
    public VictoryLayoutNode Layout { get; init; } = new();
    public Dictionary<string, string> Bindings { get; init; } = new();
    public Dictionary<string, HeadquartersAction> Actions { get; init; } = new();
    public HeadquartersRoster Roster { get; init; } = new();
    public int? InitialSection { get; init; }
    public string Background { get; init; } = "#ebe6e1";
    public string? BackgroundImage { get; init; }
}

public sealed record HeadquartersPresentation
{
    public int Schema { get; init; } = 1;
    public Dictionary<string, HeadquartersScreen> Screens { get; init; } = new();

    [DoesNotReturn] static void Fail(string detail) => throw new RuleViolation("extension.headquarters_presentation", detail);
    public void Validate(byte[] manifestBytes, Func<string, byte[]> read)
    {
        if (Schema != 1 || Screens == null || Screens.Count is < 1 or > 32) Fail("Expected schema 1 and 1..32 screens");
        using var manifest = JsonDocument.Parse(manifestBytes);
        string[] atlases = ["image_ui_hd", "image_item_hd", "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3",
            "image_menu_hd", "image_newgame_hd", "commanders1_hd", "commanders2_hd", "princesses_hd", "princesses_sketch_hd", "image_ui"];
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string atlas in atlases)
            if (manifest.RootElement.GetProperty("atlases").TryGetProperty(atlas, out var entries))
                foreach (var image in entries.EnumerateObject()) images.TryAdd(image.Name, "client/ui/" + atlas + "/" + image.Name);
        var checkedImages = new HashSet<string>(); long totalBytes = 0;
        void Image(string name)
        {
            string path;
            if (ExtensionLoader.PathValid(name) && name.EndsWith(".png", StringComparison.Ordinal)) path = name;
            else if (!images.TryGetValue(name, out path!)) { Fail("Unknown image: " + name); return; }
            if (!checkedImages.Add(path)) return;
            byte[] data;
            try { data = read(path); } catch (Exception e) when (e is IOException or KeyNotFoundException) { Fail("Missing image: " + path); return; }
            if (data.Length is < 24 or > 4 * 1024 * 1024 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                || !data.AsSpan(12, 4).SequenceEqual("IHDR"u8)) Fail("Invalid PNG: " + path);
            uint w = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(16)), h = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(20));
            if (w is 0 or > 4096 || h is 0 or > 4096 || (long)w * h > 8_388_608 || (totalBytes += data.Length) > 16 * 1024 * 1024)
                Fail("Image budget exceeded");
        }
        bool Color(string text) => Regex.IsMatch(text, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$");
        foreach (var (key, screen) in Screens!)
        {
            if (!Regex.IsMatch(key, "^[a-zA-Z][a-zA-Z0-9_-]{0,63}$") || screen == null || screen.Layout == null
                || screen.Bindings == null || screen.Actions == null || screen.Roster == null) Fail("Invalid screen: " + key);
            if (screen!.Background == null || !Color(screen.Background) || screen.InitialSection is < 0 or > 2
                || screen.Roster.Rows is < 1 or > 16 || !double.IsFinite(screen.Roster.CardScale) || screen.Roster.CardScale is < .25 or > 3
                || screen.Roster.Gap is < 0 or > 64) Fail("Invalid screen or roster style: " + key);
            if (screen.BackgroundImage != null) Image(screen.BackgroundImage);
            var nodes = new Dictionary<string, VictoryLayoutNode>(); int count = 0;
            void Visit(VictoryLayoutNode node, string path, int depth)
            {
                if (node == null || node.Attributes == null || node.Children == null || depth > 12 || ++count > 256) Fail("Invalid layout tree");
                var a = node!.Attributes;
                if (a.Any(p => p.Value == null || p.Value.Length > 16384)) Fail("Invalid layout attribute");
                foreach (var (field, text) in a)
                {
                    if (field is not ("id" or "type" or "x" or "y" or "w" or "h" or "scale" or "hscale" or "vscale"
                        or "text" or "string" or "title" or "font" or "size" or "color" or "textcolor" or "align" or "valign" or "breakwords"
                        or "name" or "frm1" or "frm2" or "frame" or "background" or "opacity" or "rotation" or "radius")) Fail("Unknown layout attribute: " + field);
                    if (field is "x" or "y" or "w" or "h" or "scale" or "hscale" or "vscale" or "size" or "opacity" or "rotation" or "radius")
                    {
                        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n)
                            || Math.Abs(n) > 4096 || field is "w" or "h" or "scale" or "hscale" or "vscale" or "size" && n <= 0
                            || field == "opacity" && n is < 0 or > 1 || field == "radius" && n < 0) Fail("Invalid number: " + field);
                    }
                    if (field is "name" or "frm1" or "frm2" or "frame") Image(text);
                    if (field == "background" && !Color(text)) Fail("Invalid background");
                    if (field is "color" or "textcolor" && (!Regex.IsMatch(text, "^\\d{1,3},\\d{1,3},\\d{1,3},\\d{1,3}$")
                        || text.Split(',').Any(c => int.Parse(c, CultureInfo.InvariantCulture) > 255))) Fail("Expected RGBA bytes");
                }
                if (depth == 0)
                {
                    foreach (string size in new[] { "w", "h" })
                        if (!a.TryGetValue(size, out var value) || !int.TryParse(value, out int n) || n is < 80 or > 2048) Fail("Canvas size must be 80..2048");
                }
                else
                {
                    string id = a.GetValueOrDefault("id") ?? "", type = a.GetValueOrDefault("type") ?? "";
                    if (!Regex.IsMatch(id, "^[a-zA-Z][a-zA-Z0-9_-]{0,63}$") || type is not ("group" or "groupbox" or "text" or "image" or "listbox" or "grid" or "button" or "tmp_button" or "tmp_intitle")) Fail("Invalid control");
                    path = path.Length == 0 ? id : path + "/" + id;
                    if (!nodes.TryAdd(path, node)) Fail("Duplicate control: " + path);
                }
                foreach (var child in node.Children) Visit(child, path, depth + 1);
            }
            Visit(screen.Layout, "", 0);
            var used = new HashSet<string>();
            string Resolve(string target, bool button, bool container = false)
            {
                if (string.IsNullOrWhiteSpace(target)) Fail("Empty binding target");
                var matches = nodes.Where(p => p.Key == target || !target.Contains('/') && p.Key.EndsWith('/' + target, StringComparison.Ordinal)).ToArray();
                if (matches.Length != 1) Fail("Missing or ambiguous control: " + target);
                string path = matches[0].Key, type = matches[0].Value.Attributes["type"];
                if (!used.Add(path) || !(button ? type is "button" or "tmp_button" : container ? type is "group" or "groupbox" or "listbox" or "grid" : type is "text" or "tmp_intitle")) Fail("Invalid binding: " + target);
                return path;
            }
            foreach (var (binding, target) in screen.Bindings)
            {
                if (binding is not ("roster" or "title" or "medals" or "badges" or "count" or "capacity" or "refreshRemaining" or "offerName" or "medalPrice" or "badgePrice")) Fail("Unknown data: " + binding);
                Resolve(target, false, binding == "roster");
            }
            foreach (var (target, action) in screen.Actions)
            {
                Resolve(target, true);
                if (action == null || action.Command is not ("back" or "main" or "roster" or "academy" or "filter" or "shop" or "princesses" or "tags"
                    or "tasks" or "achievements" or "refresh" or "recruitMedals" or "recruitBadges" or "page" or "info")) Fail("Unknown command");
                if (action!.Section is < 0 or > 2 || action.Section != null && action.Command is not ("academy" or "filter")
                    || action.Command == "page" && (action.Page == null || !Screens.ContainsKey(action.Page) || action.Page is "roster" or "academy" or "deployment")
                    || action.Page != null && action.Command != "page" || action.EditingText != null && (action.Command != "tags" || action.EditingText.Length > 256)) Fail("Invalid command arguments");
                if (key == "deployment" && action.Command is not ("back" or "info")) Fail("Deployment only supports back and info commands");
            }
            if (!screen.Actions.Values.Any(a => a.Command == "back")) Fail("A back command is required");
            if (used.Any(p => used.Any(q => q != p && q.StartsWith(p + "/", StringComparison.Ordinal)))) Fail("Bindings cannot contain other bindings");
        }
    }
}
