using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Ew4.Mods;

internal static class UiPresentation
{
    internal const string Path = "rewrite/ui-presentation.json";
    internal static readonly Dictionary<string, string[]> Controls = new()
    {
        ["main"] = ["entries", "campaign", "conquest", "tutorial", "headquarters", "settings", "title"],
        ["scenario"] = ["list", "pagination", "load", "title"],
        ["country"] = ["confirm"], ["campaign"] = ["confirm", "intro"],
        ["battle"] = ["pause", "endTurn", "undo", "nextUnit", "skipAi", "aiCountry", "resources", "capacity", "mapMode"],
        ["pause"] = ["save", "settings", "restart", "exit", "turn"],
        ["settings"] = []
    };
    private static readonly string[] StyleKeys = ["x", "y", "right", "bottom", "w", "h", "image", "pressedImage", "color", "background", "fontSize"];
    internal static JsonObject Read(byte[] bytes)
    {
        if (bytes.Length > 256 * 1024) throw new InvalidDataException("UI配置超过256KiB");
        return ModFiles.Object(bytes);
    }
    internal static void Check(JsonObject config, JsonObject manifest, HashSet<string> files, Func<string, byte[]> read)
    {
        void Fail(string message) => throw new InvalidDataException("UI配置: " + message);
        void Keys(JsonObject obj, IEnumerable<string> allowed)
        { if (obj.Any(p => !allowed.Contains(p.Key))) Fail("未知字段"); }
        var images = new HashSet<string>(); long imageBytes = 0;
        void Image(string name)
        {
            if (name.StartsWith("rewrite/", StringComparison.Ordinal))
            {
                ModFiles.PathName(name);
                if (!name.EndsWith(".png", StringComparison.Ordinal) || !files.Contains(name)) Fail("图片不存在: " + name);
                var png = read(name);
                if (images.Add(name)) imageBytes += png.Length;
                if (imageBytes > 16 * 1024 * 1024) Fail("UI图片合计超过16MiB");
                if (png.Length > 4 * 1024 * 1024) Fail("图片超过4MiB: " + name);
                var raster = Raster.Decode(png);
                if (raster.Width > 4096 || raster.Height > 4096) Fail("图片尺寸超过4096: " + name);
            }
            else if (!new[] { "image_ui_hd", "image_item_hd", "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3", "image_menu_hd", "image_newgame_hd", "commanders1_hd", "commanders2_hd", "princesses_hd", "princesses_sketch_hd", "image_ui" }
                .Any(a => manifest["atlases"]?[a]?[name] != null)) Fail("未知图集图片: " + name);
        }
        void Style(JsonObject style)
        {
            Keys(style, StyleKeys);
            foreach (var (key, value) in style)
            {
                if (key is "image" or "pressedImage") { Image(value!.GetValue<string>()); continue; }
                if (key is "color" or "background")
                { if (!Regex.IsMatch(value!.GetValue<string>(), "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$")) Fail("颜色须为#RRGGBB或#RRGGBBAA"); continue; }
                if (value?.GetValueKind() != System.Text.Json.JsonValueKind.Number) { Fail("尺寸与坐标须为JSON数字"); continue; }
                double n = double.Parse(value.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
                if (!double.IsFinite(n) || n < (key is "w" or "h" or "fontSize" ? 1 : 0) || n > (key == "fontSize" ? 128 : 2048)) Fail("尺寸或坐标越界");
            }
            if (style.ContainsKey("x") && style.ContainsKey("right") || style.ContainsKey("y") && style.ContainsKey("bottom")) Fail("同一轴不能同时使用两端坐标");
        }
        Keys(config, ["schema", "screens", "native"]);
        if ((int?)config["schema"] != 1) Fail("schema须为1");
        if (config["screens"] is JsonObject screens)
        {
            foreach (var (screen, value) in screens)
            {
                if (!Controls.ContainsKey(screen)) Fail("未知屏幕: " + screen);
                var spec = value!.AsObject(); Keys(spec, ["panel", "controls", "decorations"]);
                foreach (string field in new[] { "panel", "controls" })
                    if (spec.ContainsKey(field) && spec[field] is not JsonObject) Fail(field + "须为对象");
                if (spec.ContainsKey("decorations") && spec["decorations"] is not JsonArray) Fail("decorations须为数组");
                if (spec["panel"] is JsonObject panel)
                {
                    Style(panel);
                    if (panel.Any(p => p.Key is not ("image" or "background" or "color"))) Fail("面板只允许背景与颜色，保留响应式尺寸与退出入口");
                }
                if (spec["controls"] is JsonObject controls)
                    foreach (var (id, style) in controls)
                    { if (!Controls[screen].Contains(id)) Fail("未知或受保护控件: " + screen + "/" + id); Style(style!.AsObject()); }
                if (spec["decorations"] is JsonArray decorations)
                {
                    if (decorations.Count > 32) Fail("每屏装饰超过32个");
                    foreach (var decoration in decorations) Style(decoration!.AsObject());
                }
            }
        }
        else if (config.ContainsKey("screens")) Fail("screens须为对象");
        if (config["native"] is JsonObject native)
        {
            int count = 0;
            foreach (var (form, patches) in native)
            {
                if (manifest["layouts"]?[form] is not JsonObject definition) { Fail("未知原版弹窗: " + form); continue; }
                foreach (var (path, patch) in patches!.AsObject())
                {
                    if (++count > 256) Fail("原版控件补丁超过256个");
                    Find(definition, path); Style(patch!.AsObject());
                    if (patch.AsObject().ContainsKey("right") || patch.AsObject().ContainsKey("bottom")) Fail("原版控件只支持x/y坐标");
                }
            }
        }
        else if (config.ContainsKey("native")) Fail("native须为对象");
    }
    private static JsonObject Find(JsonObject definition, string path)
    {
        if (path.Length is < 1 or > 512) throw new InvalidDataException("UI控件路径无效");
        foreach (string segment in path.Split('/'))
        {
            var matches = definition["children"]!.AsArray().Where(n => (string?)n?["attributes"]?["id"] == segment).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("UI控件路径不存在或不唯一: " + path);
            definition = matches[0]!.AsObject();
        }
        return definition["attributes"]!.AsObject();
    }
    internal static void Apply(JsonObject manifest, JsonObject config)
    {
        manifest["uiPresentation"] = config.DeepClone();
        if (config["native"] is not JsonObject native) return;
        foreach (var (form, patches) in native)
            foreach (var (path, patch) in patches!.AsObject())
            {
                var attrs = Find(manifest["layouts"]![form]!.AsObject(), path);
                foreach (var (key, value) in patch!.AsObject())
                {
                    string target = key switch { "image" => attrs["type"]!.ToString().Contains("button", StringComparison.Ordinal) ? "frm1" : "name", "pressedImage" => "frm2", "fontSize" => "size", _ => key };
                    if (key is "right" or "bottom") throw new InvalidDataException("原版控件只支持x/y坐标");
                    string text = value!.ToString();
                    if (key == "color") text = string.Join(',', Enumerable.Range(0, text.Length == 9 ? 4 : 3).Select(i => Convert.ToInt32(text.Substring(1 + i * 2, 2), 16)).Concat(text.Length == 9 ? [] : new[] { 255 }));
                    attrs[target] = text;
                }
            }
    }
}
