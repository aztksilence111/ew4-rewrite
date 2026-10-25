using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Legacy;

namespace Ew4.Mods;

internal sealed class LegacyModImporter
{
    private readonly ModBase baseline;
    private readonly ImportSource source;
    private readonly string stage;
    private readonly Action<ImportProgress> report;
    private readonly LegacyResourceResolver resources;
    private readonly HashSet<string> names;
    private readonly JsonObject manifest;
    private readonly List<string> warnings = ["仅导入资源和已实现的规则；原APK的SO/DEX不会执行。原生代码修改的数值、技能和脚本行为不能自动移植。"];
    private long generatedBytes;
    private int generatedFiles;
    internal LegacyModImporter(ModBase baseline, ImportSource source, string stage, Action<ImportProgress> report)
    {
        this.baseline = baseline; this.source = source; this.stage = stage; this.report = report;
        resources = new(baseline, source, warnings);
        names = resources.Names;
        manifest = ModFiles.Object(baseline.Read("client/manifest.json"));
    }
    private bool Changed(string name) => resources.Changed(name);
    private byte[] Read(string name) => resources.Read(name);
    private XElement Xml(string name)
    {
        try { return ModFiles.Xml(Read(name)).Root!; }
        catch (System.Xml.XmlException e) { throw new InvalidDataException(name + ": " + e.Message, e); }
    }
    private void Write(string name, byte[] bytes)
    {
        generatedBytes += bytes.Length; generatedFiles++;
        if (bytes.Length > ModFiles.MaximumFile || generatedBytes > ModFiles.MaximumTotal || generatedFiles > 25000) throw new InvalidDataException("转换结果超过资源包预算");
        ModFiles.Write(stage, "client/" + name, bytes);
    }
    private void Json(string name, object value) => ModFiles.WriteJson(stage, "client/" + name, value);
    private static double N(XElement e, string key, double fallback = 0) => e.Attribute(key) is { } a ? double.Parse(a.Value, CultureInfo.InvariantCulture) : fallback;
    private XElement Atlas(string name) => ModFiles.Xml(Encoding.UTF8.GetBytes("<atlas>" + ModFiles.Text(Read(name + ".xml")) + "</atlas>")).Root!;
    internal (string[] Scenarios, string[] Warnings) Convert()
    {
        if (!names.Any(n => n.StartsWith("def_", StringComparison.Ordinal) || new[] { ".btl", ".png", ".pkm", ".pvr", ".bin", ".fnt", ".ini", ".mp3", ".wav", ".ogg" }.Contains(Path.GetExtension(n))))
            throw new InvalidDataException("未找到原版资源。请选择APK、assets目录ZIP或带mod.json的内容包。");
        report(new("working", "迁移剧本与将领数据"));
        var extensionPaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string originalPath in source.Names.Where(p => p.StartsWith("rewrite/", StringComparison.Ordinal) || p.StartsWith("assets/rewrite/", StringComparison.Ordinal)))
        {
            string path = originalPath.StartsWith("assets/", StringComparison.Ordinal) ? originalPath[7..] : originalPath;
            if (!extensionPaths.Add(path)) throw new InvalidDataException("Duplicate extension file: " + path);
            if (!ModFiles.RuntimePath(path)) throw new InvalidDataException("Unsupported extension file: " + path);
            ModFiles.Write(stage, path, source.Read(originalPath));
        }
        resources.CompleteCityLabels();
        resources.CheckMapReferences();
        foreach (string name in names.Where(n => !n.Contains('/') && (n.EndsWith(".btl", StringComparison.Ordinal) || n.EndsWith(".xml", StringComparison.Ordinal) || n.EndsWith(".ini", StringComparison.Ordinal) || n.EndsWith(".bin", StringComparison.Ordinal))))
        {
            if (baseline.Files.Contains("original/" + name) || name.EndsWith(".btl", StringComparison.Ordinal) || Xml("def_map.xml").Elements().Any(e => (string?)e.Attribute("file") == name))
                ModFiles.Write(stage, "original/" + name, Read(name));
        }
        foreach (string path in baseline.Files.Where(p => p.StartsWith("original/", StringComparison.Ordinal)
            && (p.EndsWith(".xml", StringComparison.Ordinal) || p.EndsWith(".ini", StringComparison.Ordinal))))
            if (!names.Contains(path[9..])) ModFiles.Write(stage, path, Read(path[9..]));
        if (LegacyCatalogConverter.Sources.Any(Changed))
        {
            var bundle = LegacyCatalogFiles.Load(p => baseline.Read(p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p));
            var catalog = LegacyCatalogConverter.Convert(Read, bundle.Catalog);
            foreach (var (file, bytes) in LegacyCatalogFiles.Split(catalog, bundle.RuntimeRules, bundle.MetagameRules)) ModFiles.Write(stage, "legacy/" + file, bytes);
        }
        var strings = LegacyCatalogConverter.Strings(Read("stringtable_cn.ini"));
        manifest["strings"] = JsonSerializer.SerializeToNode(strings);
        manifest["commanders"] = new JsonObject(Xml("def_commander.xml").Elements().Select(e => KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("id")!, JsonValue.Create((string)e.Attribute("name")!))));
        manifest["buildings"] = new JsonObject(Xml("def_construction.xml").Elements().Select(e => KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("type")!, new JsonArray(e.Elements().Select(x => (JsonNode)LegacyCatalogConverter.Attr(x)).ToArray()))));
        manifest["capitals"] = new JsonObject(Xml("def_capital.xml").Elements().Select(e => KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("id")!, LegacyCatalogConverter.Attr(e))));
        manifest["strategic"] = new JsonObject(Xml("def_battlelist.xml").Elements().Select(e => KeyValuePair.Create<string, JsonNode?>(Path.GetFileNameWithoutExtension((string)e.Attribute("file")!), LegacyCatalogConverter.Attr(e))));
        if (names.Contains("layout-568h.xml")) manifest["layouts"] = new JsonObject(Xml("layout-568h.xml").Elements().GroupBy(e => (string)e.Attribute("id")!).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, LegacyCatalogConverter.Layout(g.Last()))));
        foreach (string name in names.Where(n => n.StartsWith("tutorials_script", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)))
            manifest["tutorials"]![Path.GetFileNameWithoutExtension(name).Replace("_script", "")] = new JsonObject { ["sha256"] = ModFiles.Hash(Read(name)), ["commands"] = new JsonArray(Xml(name).Elements().Select(e => (JsonNode)LegacyCatalogConverter.Attr(e)).ToArray()) };
        var textureNames = names.Where(n => !n.Contains('/') && (n.EndsWith(".png", StringComparison.Ordinal) || n.EndsWith(".pkm", StringComparison.Ordinal)))
            .Where(n => !n.StartsWith("font_", StringComparison.Ordinal))
            .Select(n => Path.GetFileNameWithoutExtension(n)).Distinct().ToArray();
        for (int i = 0; i < textureNames.Length; i++)
        {
            string stem = textureNames[i], file = names.Contains(stem + ".png") ? stem + ".png" : stem + ".pkm";
            bool changed = Changed(file), atlasChanged = Changed(stem + ".xml");
            if (!changed && !atlasChanged) continue;
            report(new("working", "转换贴图: " + stem, i, textureNames.Length));
            var image = Raster.Decode(Read(file)); Write("images/" + stem + ".png", image.Png());
            JsonObject? sprites = null;
            if (names.Contains(stem + ".xml") && ModFiles.Text(Read(stem + ".xml")).TrimStart().StartsWith("<Texture", StringComparison.Ordinal))
            {
                sprites = new JsonObject();
                foreach (var e in Atlas(stem).Descendants("Image"))
                    sprites[(string)e.Attribute("name")!] = new JsonObject(e.Attributes().Where(a => a.Name.LocalName != "name").Select(a => KeyValuePair.Create<string, JsonNode?>(a.Name.LocalName, JsonValue.Create(double.Parse(a.Value, CultureInfo.InvariantCulture)))));
                manifest["atlases"]![stem] = sprites;
            }
            else sprites = manifest["atlases"]?[stem] as JsonObject;
            if (sprites != null && new[] { "image_recruit_hd", "image_recruit_hd2", "image_recruit_hd3", "image_ui_hd", "image_menu_hd", "image_item_hd", "image_newgame_hd", "princesses_hd", "princesses_sketch_hd", "commanders1_hd", "commanders2_hd", "image_flag_hd", "image_ui" }.Contains(stem))
                foreach (var (sprite, value) in sprites)
                {
                    ModFiles.PathName(sprite);
                    int V(string key) => checked((int)value![key]!.GetValue<double>());
                    int w = V("w"), h = V("h");
                    if (w == 0 || h == 0)
                    {
                        Write("ui/" + stem + "/" + sprite, new Raster(1, 1, new byte[4]).Png());
                        warnings.Add("空图集区域保留透明占位: " + stem + "/" + sprite);
                    }
                    else
                    {
                        try { Write("ui/" + stem + "/" + sprite, image.Crop(V("x"), V("y"), w, h).Png()); }
                        catch (InvalidDataException e) { throw new InvalidDataException($"{stem}/{sprite} ({V("x")},{V("y")},{w},{h}) in {image.Width}x{image.Height}: {e.Message}", e); }
                    }
                }
            if (stem == "tex_mainmenu_back_ip5@2x") Write("ui/main.png", image.Crop(0, 0, image.Width, 776).Png());
        }
        ConvertMenuAssets();
        foreach (string font in new[] { "font_text_1_cn_hd", "font_text_2_cn_hd", "font_text_3_cn_hd", "font_text_4_cn_hd",
            "font_char_1_hd", "font_char_2_hd", "font_char_3_hd", "font_char_4_hd", "font_ascii", "font_city", "font_city_hd",
            "font_city_labels_hd", "font_fight_num", "font_fight_num_hd" })
        {
            if (names.Contains(font + ".fnt")) manifest["fonts"]![font] = LegacyBinary.Font(Read(font + ".fnt"));
            if (names.Contains(font + ".png")) Write("images/" + font + ".png", Read(font + ".png"));
        }
        if (Changed("zh_CN.lproj/title_hd.png")) Write("ui/title.png", Raster.Decode(Read("zh_CN.lproj/title_hd.png")).Crop(0, 0, 759, 192).Png());
        manifest["textRendering"] = new JsonObject { ["typeface"] = "EW4 Extended", ["cityFill"] = "#ffffff", ["cityOutline"] = "#000000" };
        manifest["ui"]!["labels"] = new JsonObject();
        ConvertProfiles(strings);
        ConvertAnimations();
        ConvertMaps();
        ConvertEffects();
        foreach (string f in names.Where(n => n.EndsWith(".mp3", StringComparison.Ordinal) || n.EndsWith(".wav", StringComparison.Ordinal) || n.EndsWith(".ogg", StringComparison.Ordinal)))
            if (Changed(f)) Write((f.EndsWith(".mp3", StringComparison.Ordinal) ? "music/" : "sounds/") + Path.GetFileName(f), Read(f));
        if (names.Contains("global_data.xml"))
        {
            string? menuMusic = (string?)ModFiles.Xml(Read("global_data.xml")).Descendants("data")
                .FirstOrDefault(e => (string?)e.Attribute("name") == "BGMusic")?.Attribute("value");
            if (!string.IsNullOrWhiteSpace(menuMusic))
            {
                ModFiles.PathName(menuMusic);
                if (menuMusic.Contains('/') || !new[] { ".mp3", ".ogg", ".wav" }.Contains(Path.GetExtension(menuMusic)))
                    throw new InvalidDataException("Invalid BGMusic filename");
                if (names.Contains(menuMusic)) Write("music/" + menuMusic, Read(menuMusic));
                var music = ModFiles.Object(baseline.Read("client/music.json"));music["menu"] = menuMusic;Json("music.json", music);
            }
        }
        Json("manifest.json", manifest);
        string[] scenarios = names.Where(n => !n.Contains('/') && n.EndsWith(".btl", StringComparison.Ordinal)).Select(Path.GetFileNameWithoutExtension).Select(n => n!).Order(StringComparer.Ordinal).ToArray();
        bool full = resources.FullPackage;
        if (!full) scenarios = baseline.Scenarios.Union(scenarios).Order(StringComparer.Ordinal).ToArray();
        ConvertCountries(strings, scenarios);
        JsonObject pages;
        try { pages = ModFiles.Object(baseline.Read("frontend/conquest-pages.json")); }
        catch (IOException) { pages = new JsonObject { ["pages"] = new JsonArray(new JsonObject { ["id"] = "original", ["slots"] = new JsonArray(Enumerable.Range(1, 6).Select(i => (JsonNode)new JsonObject { ["scenario"] = "conquest" + i }).ToArray()) }) }; }
        var registered = pages["pages"]!.AsArray().SelectMany(p => p!["slots"]!.AsArray()).Where(s => s != null).Select(s => (string)s!["scenario"]!).ToHashSet();
        var additional = scenarios.Where(s => s.StartsWith("conquest", StringComparison.Ordinal) && !registered.Contains(s)).ToArray();
        for (int offset = 0; offset < additional.Length; offset += 6)
            pages["pages"]!.AsArray().Add(new JsonObject { ["id"] = "imported-" + offset / 6, ["slots"] = new JsonArray(Enumerable.Range(0, 6).Select(i => offset + i < additional.Length ? (JsonNode)new JsonObject { ["scenario"] = additional[offset + i] } : null).ToArray()) });
        ModFiles.WriteJson(stage, "frontend/conquest-pages.json", pages);
        string[] libraries = source.Names.Where(n => n.EndsWith(".so", StringComparison.Ordinal)).ToArray();
        ModFiles.WriteJson(stage, "import-report.json", new { converterVersion = 3, fullPackage = full, sourceFiles = source.Names.Count,
            sources = resources.Resolutions.OrderBy(r => r.Resource),
            nativeLibraries = libraries.Select(n => new { file = n, sha256 = ModFiles.Hash(source.Read(n)), executed = false }), warnings });
        return (scenarios, warnings.ToArray());
    }
    private void ConvertMenuAssets()
    {
        var references = manifest["layouts"]!["form_mainmenu"]!["children"]!.AsArray()
            .Select(n => (string?)n!["attributes"]!["texture"]).Where(n => n != null && n.StartsWith("tex_", StringComparison.Ordinal)).Select(n => n!)
            .Concat(new[] { "form_selcampaign", "form_selconquest" }.Select(id => (string)manifest["layouts"]![id]!["attributes"]!["back"]!))
            .Append("tex_campaign_back.pkm").Distinct();
        var textures = new JsonObject();
        var decoded = new Dictionary<string, (Raster Image, int Ratio)>();
        foreach (string reference in references)
        {
            ModFiles.PathName(reference);
            string stem = Path.GetFileNameWithoutExtension(reference), hd = stem.EndsWith("@2x", StringComparison.Ordinal) ? stem : stem + "@2x";
            var candidates = new[] { hd + ".png", hd + ".pkm", stem + ".png", stem + ".pkm" }.Distinct().ToArray();
            string? file = candidates.FirstOrDefault(names.Contains);
            int ratio;
            Raster image;
            string imageFile;
            if (file != null)
            {
                image = Raster.Decode(Read(file)); imageFile = "images/" + Path.GetFileNameWithoutExtension(file) + ".png";
                ratio = Path.GetFileNameWithoutExtension(file).EndsWith("@2x", StringComparison.Ordinal) ? 2 : 1;
                if (!File.Exists(ModFiles.Under(stage, "client/" + imageFile))) Write(imageFile, image.Png());
            }
            else
            {
                imageFile = new[] { "images/" + hd + ".png", "images/" + stem + ".png" }.FirstOrDefault(n => baseline.Files.Contains("client/" + n))
                    ?? throw new InvalidDataException("菜单缺少布局引用的背景: " + reference);
                image = Raster.Decode(baseline.Read("client/" + imageFile)); ratio = imageFile.Contains("@2x", StringComparison.Ordinal) ? 2 : 1;
            }
            textures[reference] = new JsonObject { ["file"] = imageFile, ["ratio"] = ratio };
            decoded[reference] = (image, ratio);
        }
        manifest["menuTextures"] = textures;
        foreach (var (id, output) in new[] { ("form_selcampaign", "campaign_background"), ("form_selconquest", "conquest_background") })
        {
            var a = manifest["layouts"]![id]!["attributes"]!.AsObject();
            var (image, ratio) = decoded[(string)a["back"]!];
            int V(string key) => checked((int)(double.Parse((string?)a[key] ?? "0", CultureInfo.InvariantCulture) * ratio));
            Write("ui/" + output + ".png", image.Crop(V("tx"), V("ty"), V("tw"), V("th")).Png());
        }
        Write("ui/campaign.png", decoded["tex_campaign_back.pkm"].Image.Png());
    }
    private void ConvertProfiles(Dictionary<string, string> strings)
    {
        if (!names.Contains("def_motion.xml"))
        {
            foreach (var (id, p) in manifest["profiles"]!.AsObject())
            {
                string kind = (string)p!["kind"]!; p["name"] = strings.GetValueOrDefault("name_" + kind, kind); p["description"] = strings.GetValueOrDefault("desc_" + kind, "");
            }
            return;
        }
        var motions = Xml("def_motion.xml").Elements().GroupBy(e => (string)e.Attribute("name")!).ToDictionary(g => g.Key, g => g.Last()); var profiles = new JsonObject();
        var armies = Xml("def_army.xml");
        var indices = manifest["profiles"]!.AsObject().Select(p => p.Value!).GroupBy(p => (string)p["kind"]!).ToDictionary(g => g.Key, g => g.First()["unitIndex"]!.GetValue<int>());
        string[] cards = ["militia", "line", "light", "grenadier", "guard", "machinegun", "lightcavalry", "heavycavalry", "musketcavalry", "armoredcar", "lightartillery", "heavyartillery", "fortressartillery", "rocket", "cruiser", "frigate", "battleship", "ironclads", "smallfortress", "mediumfortress", "largefortress", "coastalartillery"];
        foreach (var country in armies.Elements()) foreach (var army in country.Elements("army"))
        {
            string kind = (string)army.Attribute("name")!, variant = (string)country.Attribute("name")!; int grade = (int)army.Attribute("grade")!;
            if (!indices.TryGetValue(kind, out int index) || index >= cards.Length) throw new InvalidDataException("原格式新增兵种需要rewrite规则定义: " + kind);
            string id = $"ew4.unit.{variant}.{LegacyCatalogConverter.Slug(kind)}.{grade}";
            string motionKey = new[] { $"{kind} {variant} {grade + 1}", $"{kind} {grade + 1}", kind }.FirstOrDefault(motions.ContainsKey) ?? throw new InvalidDataException("缺少兵种动画: " + id);
            var m = motions[motionKey];
            profiles[id] = new JsonObject { ["name"] = strings.GetValueOrDefault("name_" + kind, kind), ["kind"] = kind, ["unitIndex"] = index, ["grade"] = grade,
                ["scale"] = N(m, "scale", 1), ["card"] = (index >= 18 ? "build_" : "recruit_") + cards[index] + ".png", ["cardMarker"] = (index >= 18 ? "buildmarker_" : "recruitmarker_") + cards[index] + ".png",
                ["stats"] = LegacyCatalogConverter.Attr(army), ["description"] = strings.GetValueOrDefault("desc_" + kind, ""), ["library"] = (string)m.Attribute("res")!, ["x"] = N(m, "x"), ["y"] = N(m, "y"),
                ["motions"] = new JsonObject(m.Elements().Where(e => new[] { "all", "1", "right" }.Contains((string?)e.Attribute("dir") ?? "all")).GroupBy(e => (string)e.Attribute("type")!).Select(g => KeyValuePair.Create<string, JsonNode?>(g.Key, JsonValue.Create((string)g.Last().Attribute("name")!)))) };
        }
        manifest["profiles"] = profiles;
    }
    private void ConvertAnimations()
    {
        var visualLibraries = names.Contains("def_motion.xml") ? Xml("def_motion.xml").Elements().Select(e => (string)e.Attribute("res")!) : [];
        var libraries = manifest["profiles"]!.AsObject().Select(p => (string)p.Value!["library"]!).Concat(visualLibraries).Concat(["choose", "strategicmap", "maptext", "anim_fire", "anim_upgrade", "anim_smoke"]).Distinct();
        foreach (string lib in libraries)
        {
            string preferred = names.Contains(lib + "_hd.bin") ? lib + "_hd" : lib;
            if (!Changed(preferred + ".bin") && !Changed(preferred + ".xml")) continue;
            if (!names.Contains(preferred + ".xml")) throw new InvalidDataException("动画需要配套XML图集: " + preferred);
            JsonObject animation;
            try { animation = LegacyBinary.Animation(Read(preferred + ".bin")); }
            catch (InvalidDataException hdError) when (preferred != lib && names.Contains(lib + ".bin") && names.Contains(lib + ".xml"))
            {
                try { animation = LegacyBinary.Animation(Read(lib + ".bin")); }
                catch (InvalidDataException sdError) { throw new InvalidDataException($"{preferred}.bin: {hdError.Message}; {lib}.bin: {sdError.Message}", sdError); }
                warnings.Add($"{preferred}.bin 无效 ({hdError.Message})，使用同包 {lib}.bin 和 {lib}.xml");
                preferred = lib;
            }
            catch (InvalidDataException e) { throw new InvalidDataException(preferred + ".bin: " + e.Message, e); }
            var atlas = Atlas(preferred);
            animation["atlas"] = new JsonObject { ["textures"] = new JsonArray(atlas.Elements("Texture").Select(e => { var a = LegacyCatalogConverter.Attr(e); a["name"] = Path.ChangeExtension((string)a["name"]!, ".png"); return (JsonNode)a; }).ToArray()),
                ["sprites"] = new JsonArray(atlas.Descendants("Image").Select(e => (JsonNode)LegacyCatalogConverter.Attr(e)).ToArray()) };
            animation["pixelRatio"] = preferred.EndsWith("_hd", StringComparison.Ordinal) ? 2 : 1;
            Json("animations/" + lib + ".json", animation);
        }
    }
    private void ConvertMaps()
    {
        var terrainDefs = Xml("def_mapterrain.xml").Elements().ToArray();
        foreach (var map in Xml("def_map.xml").Elements())
        {
            string name = (string)map.Attribute("name")!, terrainFile = (string)map.Attribute("file")!, tile = (string)map.Attribute("tile")!, stem = Path.GetFileNameWithoutExtension(tile), text = (string)map.Attribute("textpos")!;
            ModFiles.PathName(name); string indexFile = names.Contains(stem + "_hd.bin") ? stem + "_hd.bin" : tile; int ratio = indexFile == tile ? 1 : 2;
            bool regenerate = Changed("def_map.xml") || Changed(indexFile) || names.Any(n => n.StartsWith("map/" + stem + "_", StringComparison.Ordinal) && Changed(n));
            JsonObject result = manifest["maps"]?[name] is JsonObject found ? (JsonObject)found.DeepClone() : new JsonObject();
            if (regenerate || result.Count == 0)
            {
                if (!names.Contains(indexFile)) throw new InvalidDataException("地图缺少贴图索引: " + indexFile);
                report(new("working", "重建地图: " + name));
                byte[] index = Read(indexFile); int w = BitConverter.ToInt32(index, 0), h = BitConverter.ToInt32(index, 4), step = 62 * ratio;
                if (w < 1 || h < 1 || w > 20000 || h > 20000 || (long)w * h > 150_000_000) throw new InvalidDataException("地图尺寸超出导入预算");
                int cols = (w + step - 1) / step, rows = (h + step - 1) / step;
                if (index.Length != 8L + cols * rows * 4L) throw new InvalidDataException("地图贴图索引尺寸不符");
                var cache = new Dictionary<int, Raster>(); var tiles = new JsonArray();
                for (int top = 0; top < h; top += 512 * ratio) for (int left = 0; left < w; left += 512 * ratio)
                {
                    int tw = Math.Min(512 * ratio, w - left), th = Math.Min(512 * ratio, h - top); var image = new Raster(tw, th, new byte[tw * th * 4]);
                    for (int row = top / step; row <= (top + th - 1) / step; row++) for (int col = left / step; col <= (left + tw - 1) / step; col++)
                    {
                        int value = BitConverter.ToInt32(index, 8 + (row * cols + col) * 4); if (value == -1) continue; if (value < 0) throw new InvalidDataException("地图贴图索引为负数");
                        int number = value / 16 + 1;
                        if (!cache.TryGetValue(number, out var atlas))
                        {
                            string file = $"map/{stem}_{number}{(ratio == 2 ? "@2x" : "")}.pvr";
                            if (!names.Contains(file)) throw new InvalidDataException("缺少地图图集: " + file);
                            atlas = Raster.Decode(Read(file)); if (atlas.Width != 256 * ratio || atlas.Height != 256 * ratio) throw new InvalidDataException("地图图集尺寸不符");
                            if (cache.Count >= 16) cache.Clear(); cache[number] = atlas;
                        }
                        int x = Math.Max(left, col * step), y = Math.Max(top, row * step), cw = Math.Min(left + tw, (col + 1) * step) - x, ch = Math.Min(top + th, (row + 1) * step) - y;
                        image.Copy(atlas, value % 4 * 64 * ratio + ratio + x - col * step, value % 16 / 4 * 64 * ratio + ratio + y - row * step, x - left, y - top, cw, ch);
                    }
                    string fileName = $"maps/{name}/{left / ratio}_{top / ratio}.png"; Write(fileName, image.Png());
                    tiles.Add(new JsonObject { ["x"] = (double)left / ratio, ["y"] = (double)top / ratio, ["width"] = (double)tw / ratio, ["height"] = (double)th / ratio, ["file"] = fileName });
                }
                result["width"] = (double)w / ratio; result["height"] = (double)h / ratio; result["pixelRatio"] = ratio; result["tiles"] = tiles;
            }
            if (regenerate || Changed(terrainFile) || Changed("def_mapterrain.xml"))
            {
                byte[] terrain = Read(terrainFile); int width = BitConverter.ToInt32(terrain, 8), height = BitConverter.ToInt32(terrain, 12);
                // The original terrain header is four little-endian words followed by 12-byte cells.
                if (width < 1 || height < 1 || terrain.Length != 16L + width * (long)height * 12) throw new InvalidDataException("地图地形记录尺寸不符");
                var overlays = new JsonArray();
                for (int cell = 0; cell < width * height; cell++) for (int layer = 0; layer < 2; layer++)
                {
                    int o = 16 + cell * 12 + layer * 4, kind = terrain[o], sprite = terrain[o + 1];
                    if (kind is 0 or 11 || layer == 0 && kind == 1 || sprite == 255) continue;
                    if (kind >= terrainDefs.Length) throw new InvalidDataException("未知地图地形贴图分类");
                    var entry = terrainDefs[kind].Elements().SingleOrDefault(e => (int?)e.Attribute("idx") == sprite) ?? throw new InvalidDataException("缺少地形贴图索引");
                    overlays.Add(new JsonObject { ["cell"] = cell, ["layer"] = layer, ["seaForeground"] = kind == 1, ["image"] = (string)entry.Attribute("image")!,
                        ["x"] = cell % width * 64 + cell / width % 2 * 32 + (sbyte)terrain[o + 2], ["y"] = cell / width * 54 - 18 + (sbyte)terrain[o + 3] });
                }
                result["overlays"] = overlays;
            }
            if (names.Contains(text) && (Changed(text) || regenerate)) result["labels"] = new JsonArray(Xml(text).Elements().Select(e => (JsonNode)LegacyCatalogConverter.Attr(e)).ToArray());
            manifest["maps"]![name] = result;
        }
    }
    private void ConvertCountries(Dictionary<string, string> strings, string[] scenarios)
    {
        var variants = Xml("def_army.xml").Elements().Select(e => (string)e.Attribute("name")!).Where(n => n != "others")
            .ToDictionary(n => n, n => new HashSet<string> { n });
        foreach (string scenario in scenarios)
            foreach (var faction in Ew4.Legacy.LegacyBattle.Read(Read(scenario + ".btl")).Factions)
            {
                if (!variants.TryGetValue(faction.CountryId, out var choices)) variants[faction.CountryId] = choices = [];
                choices.Add(faction.Variant);
            }
        var catalog = LegacyCatalogFiles.Load(p => {
            string name = p.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + p[5..] : p;
            string file = ModFiles.Under(stage, name);
            return File.Exists(file) ? File.ReadAllBytes(file) : baseline.Read(name);
        }).Catalog;
        var countries = new JsonObject();
        foreach (var (id, choices) in variants.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            string? variant = choices.Count == 1 ? choices.Single() : null;
            countries[id] = new JsonObject { ["name"] = strings.GetValueOrDefault(id, strings.GetValueOrDefault("name_" + id, id)),
                ["flag"] = variant, ["unitVariant"] = variant == null ? null : catalog["variants"]!.AsObject().ContainsKey(variant) ? variant : "others", ["visualVariant"] = variant };
        }
        Json("countries.json", new JsonObject { ["schema"] = 1, ["countries"] = countries,
            ["sourceSha256"] = new JsonObject(new[] { "def_army.xml", "stringtable_cn.ini" }.Select(name => KeyValuePair.Create<string, JsonNode?>(name, JsonValue.Create(ModFiles.Hash(Read(name)))))) });
    }
    private void ConvertEffects()
    {
        if (!names.Any(n => n is "def_motion.xml" or "def_elementanim.xml" or "def_effectsanim.xml"
            || n.StartsWith("effect", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)
            || n.EndsWith(".wav", StringComparison.Ordinal) || n.EndsWith(".ogg", StringComparison.Ordinal) || n.EndsWith(".mp3", StringComparison.Ordinal))) return;
        var fx = ModFiles.Object(baseline.Read("client/effects.json")); var motions = new JsonObject(); var bindings = new JsonObject();
        if (names.Contains("def_motion.xml"))
        {
            foreach (var e in Xml("def_motion.xml").Elements()) { var m = LegacyCatalogConverter.Attr(e); m["motions"] = new JsonArray(e.Elements().Select(x => (JsonNode)LegacyCatalogConverter.Attr(x)).ToArray()); motions[(string)e.Attribute("name")!] = m; }
            foreach (var (id, p) in manifest["profiles"]!.AsObject())
            {
                string kind = (string)p!["kind"]!, variant = id.Split('.')[2]; int grade = p["grade"]!.GetValue<int>() + 1;
                bindings[id] = new[] { $"{kind} {variant} {grade}", $"{kind} {grade}", kind }.First(motions.ContainsKey);
            }
            fx["motions"] = motions; fx["bindings"] = bindings;
            Json("unit-visuals.json", UnitVisualConfiguration.FromLegacy(Xml("def_motion.xml"), manifest["profiles"]!.AsObject()));
        }
        if (names.Contains("def_elementanim.xml"))
            fx["elements"] = new JsonObject(Xml("def_elementanim.xml").Elements().Select(e => KeyValuePair.Create<string, JsonNode?>(
                (string)e.Attribute("name")!, new JsonArray(e.Elements().Select(x => (JsonNode)LegacyCatalogConverter.Attr(x)).ToArray()))));
        if (names.Contains("def_effectsanim.xml"))
            fx["timelines"] = new JsonObject(Xml("def_effectsanim.xml").Elements().GroupBy(e => (string)e.Attribute("name")!).Select(g => g.Last()).Select(e => KeyValuePair.Create<string, JsonNode?>((string)e.Attribute("name")!,
                new JsonArray(e.Elements().SelectMany(g => g.Elements().Select(x => { var a = LegacyCatalogConverter.Attr(x); a["type"] = (string)g.Attribute("type")!; return (JsonNode)a; })).ToArray()))));
        foreach (string file in names.Where(n => n.StartsWith("effect", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal)))
        {
            var root = Xml(file); if (root.Name.LocalName != "effect") continue;
            fx["definitions"]![file] = new JsonArray(root.Elements().Select(e => (JsonNode)new JsonObject { ["life"] = (string)e.Attribute("life")!,
                ["parameters"] = new JsonObject(e.Elements().Select(p => KeyValuePair.Create<string, JsonNode?>((string)p.Attribute("name")!, p.HasElements ? new JsonArray(p.Elements().Select(t => (JsonNode)LegacyCatalogConverter.Attr(t)).ToArray()) : new JsonObject(p.Attributes().Where(a => a.Name.LocalName != "name").Select(a => KeyValuePair.Create<string, JsonNode?>(a.Name.LocalName, JsonValue.Create(a.Value))))))) }).ToArray());
        }
        var requestedSounds = fx["timelines"]!.AsObject().SelectMany(p => p.Value!.AsArray()).Select(e => (string?)e!["sound"])
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToHashSet(StringComparer.Ordinal);
        var soundFiles = names.Where(n => n.EndsWith(".wav", StringComparison.Ordinal) || n.EndsWith(".ogg", StringComparison.Ordinal)
            || n.EndsWith(".mp3", StringComparison.Ordinal) && requestedSounds.Contains(Path.GetFileName(n))).ToArray();
        foreach (string file in soundFiles.Where(n => n.EndsWith(".mp3", StringComparison.Ordinal)))
            Write("sounds/" + Path.GetFileName(file), Read(file));
        fx["sounds"] = new JsonArray(fx["sounds"]!.AsArray().Select(n => n!.GetValue<string>()).Concat(soundFiles.Select(Path.GetFileName))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(n => JsonValue.Create(n)).ToArray());
        Json("effects.json", fx);
    }
}
