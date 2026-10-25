using System.Text.Json;
using Ew4.Mods;
using Ew4.ModTool;
using Ew4.Legacy;
using System.Text.Json.Nodes;

try
{
    string? Option(string name) => args.FirstOrDefault(a => a.StartsWith("--" + name + "=", StringComparison.Ordinal))?.Split('=', 2)[1];
    string[] positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
    if (positional.ElementAtOrDefault(0) == "split-catalog")
    {
        if (positional.Length != 3) throw new ArgumentException("split-catalog <schema-2 catalog.json> <output directory>");
        string target = Path.GetFullPath(positional[2]);
        string sourceData = Option("base") ?? Path.GetDirectoryName(Path.GetFullPath(positional[1]))!;
        byte[] RulesRead(string p) => File.ReadAllBytes(Path.Combine(sourceData, p[5..]));
        var files = LegacyCatalogFiles.Split(JsonNode.Parse(File.ReadAllBytes(positional[1]))!.AsObject(),
            LegacyCatalogFiles.Rules(RulesRead, "runtime_rule_tables.json"), LegacyCatalogFiles.Rules(RulesRead, "metagame_rule_tables.json"));
        Directory.CreateDirectory(target);
        foreach (var (name, bytes) in files)
        {
            string path = Path.Combine(target, name);
            if (File.Exists(path) && !args.Contains("--replace")) throw new IOException("Preserved existing author file: " + path);
        }
        foreach (var (name, bytes) in files) File.WriteAllBytes(Path.Combine(target, name), bytes);
        Console.WriteLine(JsonSerializer.Serialize(new { files = files.Count, errors = 0 })); return;
    }
    if (positional.Length == 0) throw new ArgumentException("命令: import | list | activate | export | pack | verify | country-export <源.btl> <输出.json> | country-apply <源.btl> <配置.json> <输出.btl> | country-strip <源.btl> <输出.btl> | jurisdiction-export <源.btl> <输出.json> | jurisdiction-apply <源.btl> <配置.json> <输出.btl> | jurisdiction-strip <源.btl> <输出.btl> | victory-export <源.btl> <输出.json> | victory-apply <源.btl> <规则.json> <输出.btl> | victory-strip <源.btl> <输出.btl>；选项 --id= --name= --version= --base= --mods= --saves= --content=");
    string data = Path.GetFullPath(Option("base") ?? Path.Combine(AppContext.BaseDirectory, "data"));
    byte[] Read(string p) => File.ReadAllBytes(Path.Combine(data, p));
    var baseline = new ModBase(Read, Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
    if (positional[0] == "geography-template")
    {
        if (positional.Length != 3) throw new ArgumentException("geography-template <registered map name> <output.json> --base=<data directory>");
        var content = baseline.Content(baseline.Read, baseline.Scenarios);
        int id = content.MapNames.Single(p => p.Value == positional[1]).Key;
        var map = content.Maps[id];
        var entry = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(Read("original/def_map.xml")))
            .Root!.Elements().Single(e => (int)e.Attribute("id")! == id);
        var config = new GeographyCatalog(1, [new(positional[1], map.Width, map.Height,
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Read("original/" + (string)entry.Attribute("file")!))),
            content.Extensions.Geography.Maps.SingleOrDefault(m => m.Map == positional[1])?.TerrainOverrides ?? [])]);
        string target = Path.GetFullPath(positional[2]);
        if (File.Exists(target) && !args.Contains("--replace")) throw new IOException("Preserved existing author file: " + target);
        File.WriteAllBytes(target, JsonSerializer.SerializeToUtf8Bytes(config, new JsonSerializerOptions(ExtensionLoader.Json) { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { map = positional[1], map.Width, map.Height, files = 1, errors = 0 })); return;
    }
    if (positional[0] == "catalog-export")
    {
        if (positional.Length != 2) throw new ArgumentException("catalog-export <output.json> --base=<data directory>");
        File.WriteAllBytes(positional[1], LegacyCatalogFiles.Encode(baseline.Catalog()));
        Console.WriteLine(JsonSerializer.Serialize(new { files = 1, errors = 0 })); return;
    }
    if (positional[0] is "country-export" or "country-apply" or "country-strip")
    {
        Console.WriteLine(JsonSerializer.Serialize(CountryCommands.Run(positional, baseline, Option("content")), JurisdictionCommands.Json)); return;
    }
    if (positional[0] is "victory-export" or "victory-apply" or "victory-strip")
    {
        Console.WriteLine(JsonSerializer.Serialize(VictoryCommands.Run(positional, baseline, Option("content")), JurisdictionCommands.Json)); return;
    }
    if (positional[0] is "jurisdiction-export" or "jurisdiction-apply" or "jurisdiction-strip")
    {
        Console.WriteLine(JsonSerializer.Serialize(JurisdictionCommands.Run(positional, baseline, Option("content")), JurisdictionCommands.Json));
        return;
    }
    var store = new ModStore(baseline, Option("mods") ?? Path.Combine(AppContext.BaseDirectory, "mods"), Option("saves") ?? Path.Combine(AppContext.BaseDirectory, "saves"));
    string P(int i) => positional.Length > i ? Path.GetFullPath(positional[i]) : throw new ArgumentException("命令缺少参数");
    object result = positional[0] switch
    {
        "import" => store.Import(P(1), Option("id") ?? "my-mod", Option("name") ?? Path.GetFileNameWithoutExtension(P(1)), p => Console.Error.WriteLine(p.Message)),
        "list" => store.List(),
        "verify" => store.Validate(P(1)),
        _ => Run()
    };
    object Run()
    {
        switch (positional[0])
        {
            case "activate": store.Activate(positional[1] == "builtin" ? null : positional[1]); return new { active = positional[1] };
            case "export": store.Export(positional[1], P(2)); return new { exported = P(2) };
            case "pack":
                store.Seal(P(1), Option("id") ?? "my-mod", Option("name") ?? "我的 Mod", Option("version") ?? "1.0");
                return store.Validate(P(1));
            default: throw new ArgumentException("未知命令: " + positional[0]);
        }
    }
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}
catch (Exception e) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; }
