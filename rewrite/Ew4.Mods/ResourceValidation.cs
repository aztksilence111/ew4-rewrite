using System.Text.Json.Nodes;
using Ew4.Client;

namespace Ew4.Mods;

internal static class ResourceValidation
{
    internal static void Check(Func<string, byte[]> read, HashSet<string> files, ClientContent content)
    {
        void Need(string path) { ModFiles.PathName(path); if (!files.Contains(path)) throw new InvalidDataException("缺少依赖资源: " + path); }
        var manifest = ModFiles.Object(read("client/manifest.json"));
        foreach (var type in content.Extensions.Geography.Maps.SelectMany(m => m.TerrainOverrides).Select(o => o.Type).Distinct())
        {
            var visual = content.Extensions.Geography.Visual(type);
            foreach (string sprite in visual.Sprites)
                foreach (string atlas in new[] { "tiles", "tiles_hd" })
                    if (manifest["atlases"]?[atlas]?[sprite] == null)
                        throw new InvalidDataException("Geography sprite missing: " + atlas + "/" + sprite);
        }
        if (files.Contains("client/music.json"))
        {
            var music = ModFiles.Object(read("client/music.json"));
            if ((int?)music["schema"] != 1 || music["battle"] is not JsonArray tracks || tracks.Count is < 1 or > 64
                || music.Any(p => p.Key is not ("schema" or "menu" or "battle" or "defeat")))
                throw new InvalidDataException("Invalid music configuration");
            foreach (var track in tracks.Concat(new[] { music["menu"], music["defeat"] }))
            {
                string name = track?.GetValue<string>() ?? throw new InvalidDataException("Missing music track");
                if (name.Contains('/') || !new[] { ".mp3", ".ogg", ".wav" }.Contains(Path.GetExtension(name)))
                    throw new InvalidDataException("Invalid music filename: " + name);
                Need("client/music/" + name);
            }
        }
        foreach (string id in content.Catalogs.Runtime.Profiles.Keys)
            if (manifest["profiles"]?[id] == null) throw new InvalidDataException("兵种缺少显示配置: " + id);
        if (files.Contains("frontend/conquest-pages.json"))
        {
            var pages = ModFiles.Object(read("frontend/conquest-pages.json"))["pages"] as JsonArray;
            if (pages == null || pages.Count is < 1 or > 200) throw new InvalidDataException("征服页面配置无效");
            foreach (var page in pages)
            {
                if (page?["slots"] is not JsonArray slots || slots.Count != 6) throw new InvalidDataException("征服每页必须配置六个槽位");
                foreach (var slot in slots.Where(s => s != null))
                {
                    if (slot is not JsonObject || slot["scenario"] is not JsonValue) throw new InvalidDataException("征服槽位缺少剧本ID");
                    if (slot["art"] != null) Need("client/" + (string)slot["art"]!);
                }
            }
        }
        foreach (var map in content.MapNames.Values)
        {
            if (manifest["maps"]?[map] is not JsonObject metadata) throw new InvalidDataException("地图缺少显示资源: " + map);
            if (metadata["tiles"] is not JsonArray tiles || tiles.Count == 0) throw new InvalidDataException("地图缺少底图片块: " + map);
            foreach (var tile in tiles) Need("client/" + (string)tile!["file"]!);
            CheckMapLabelPaths(metadata);
        }
        foreach (var (name, font) in manifest["fonts"]!.AsObject())
        {
            Need("client/images/" + name + ".png");
            if (font!["glyphs"] is not JsonObject) throw new InvalidDataException("字库缺少字形表");
            if (font["height"]!.GetValue<double>() is < 1 or > 256) throw new InvalidDataException("字库行高无效");
        }
        var loaded = new Dictionary<string, HashSet<string>>();
        foreach (var (_, profile) in manifest["profiles"]!.AsObject())
        {
            string lib = (string)profile!["library"]!;
            if (!loaded.TryGetValue(lib, out var names))
            {
                string path = "client/animations/" + lib + ".json"; Need(path);
                var animation = ModFiles.Object(read(path)); CheckAnimation(animation);
                names = animation["items"]!.AsArray().Select(i => (string)i!["name"]!).ToHashSet(); loaded[lib] = names;
                foreach (var texture in animation["atlas"]!["textures"]!.AsArray()) Need("client/images/" + (string)texture!["name"]!);
            }
            foreach (var (_, motion) in profile["motions"]!.AsObject()) if (!names.Contains((string)motion!)) throw new InvalidDataException("兵种动作缺失: " + (string)motion!);
        }
        foreach (var (_, label) in manifest["ui"]!["labels"]!.AsObject()) Need("client/" + (string)label!["file"]!);
        if (files.Contains("client/unit-visuals.json"))
            UnitVisualConfiguration.Validate(ModFiles.Object(read("client/unit-visuals.json")), manifest, ModFiles.Object(read("client/effects.json")), read);
        foreach (var (id, country) in content.Countries.Countries)
        {
            if (country.FlagImage != null) { Need(country.FlagImage); continue; }
            if (country.Flag == null) continue;
            Need("client/ui/image_flag_hd/" + country.Flag + "1.png");
            for (int frame = 1; frame <= 4; frame++)
                if (manifest["atlases"]!["buildings"]?[country.Flag + frame + ".png"] == null)
                    throw new InvalidDataException("Country flag atlas entry missing: " + id);
        }
        if (manifest["menuTextures"] is JsonObject backgrounds)
            foreach (var (_, texture) in backgrounds) Need("client/" + (string)texture!["file"]!);
    }
    internal static void CheckMapLabelPaths(JsonObject metadata)
    {
        if (metadata["labels"] is not JsonArray labels) return;
        foreach (var label in labels)
        {
            if (label?["textPath"] == null) continue;
            if (label["textPath"] is not JsonArray path || path.Count is < 3 or > 65 || path.Count % 2 == 0)
                throw new InvalidDataException("地理文字路径须为3至65个点的连续二次曲线");
            var points = new List<(double X, double Y)>();
            foreach (var node in path)
            {
                if (node is not JsonArray point || point.Count != 2 ||
                    point[0] is not JsonValue vx || !vx.TryGetValue<double>(out double x) ||
                    point[1] is not JsonValue vy || !vy.TryGetValue<double>(out double y) ||
                    !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 32768 || Math.Abs(y) > 32768)
                    throw new InvalidDataException("地理文字路径坐标无效");
                points.Add((x, y));
            }
            if (points.All(p => p == points[0]) || points.Max(p => p.X) - points.Min(p => p.X) > 8192 ||
                points.Max(p => p.Y) - points.Min(p => p.Y) > 8192 ||
                label["size"] is not JsonValue size || !size.TryGetValue<double>(out double height) ||
                !double.IsFinite(height) || height is < 1 or > 512)
                throw new InvalidDataException("地理文字路径尺寸无效");
            if (label["text"] is not JsonValue text || !text.TryGetValue<string>(out string? value) ||
                string.IsNullOrEmpty(value) || value.Length > 256 || value.Any(c => c != ' ' && !char.IsAsciiLetter(c)))
                throw new InvalidDataException("地理文字路径英文名称须使用原版字母与空格");
        }
    }

    internal static void CheckAnimation(JsonObject animation)
    {
        var items = animation["items"]!.AsArray(); var elements = animation["elements"]!.AsArray();
        double fps = animation["fps"]!.GetValue<double>();
        if (!double.IsFinite(fps) || fps <= 0 || fps > 1000) throw new InvalidDataException("动画帧率无效");
        if (items.Count > 20000 || elements.Count > 200000) throw new InvalidDataException("动画复杂度超出预算");
        var edges = new List<int>[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            edges[i] = []; int duration = items[i]!["duration"]!.GetValue<int>();
            foreach (var layer in items[i]!["layers"]!.AsArray())
            {
                int previous = -1;
                foreach (var frame in layer!.AsArray())
                {
                    int key = frame!["key"]!.GetValue<int>();
                    if (key <= previous || key < 0 || duration > 0 && key >= duration) throw new InvalidDataException("动画关键帧无效"); previous = key;
                    foreach (var pair in frame["indices"]!.AsArray())
                    {
                        int element = pair![0]!.GetValue<int>(), child = pair[1]!.GetValue<int>();
                        if (element < -1 || element >= elements.Count || child < -1 || child >= items.Count) throw new InvalidDataException("动画节点引用越界");
                        if (child >= 0) edges[i].Add(child);
                    }
                }
            }
        }
        var states = new byte[items.Count]; var depths = new int[items.Count];
        int Visit(int i, int nesting = 0)
        {
            if (nesting > 32) throw new InvalidDataException("动画嵌套超过32层");
            if (states[i] == 1) throw new InvalidDataException("动画引用形成循环");
            if (states[i] == 2) return depths[i];
            states[i] = 1; int depth = 1;
            foreach (int child in edges[i].Distinct()) depth = Math.Max(depth, 1 + Visit(child, nesting + 1));
            if (depth > 32) throw new InvalidDataException("动画嵌套超过32层"); states[i] = 2; return depths[i] = depth;
        }
        for (int i = 0; i < items.Count; i++) Visit(i);
    }
}
