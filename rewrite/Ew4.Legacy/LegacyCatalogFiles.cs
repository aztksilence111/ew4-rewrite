using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Core;

namespace Ew4.Legacy;

// File boundaries are for authors. Existing rule readers receive one consistent in-memory catalog.
public sealed class LegacyCatalogFiles
{
    public static readonly string[] PartNames = ["units.json", "unit-variants.json", "commanders.json", "skills.json",
        "items.json", "recruitment.json", "buildings.json", "installations.json", "terrain-rules.json", "conquest-list.json", "combat-rules.json",
        "unit-progression.json", "commander-progression.json", "economy-rules.json"];
    private static readonly (string File, string Key, string Source, string Root, string Element)[] XmlParts = [
        ("items.json", "items", "def_item.xml", "items", "item"),
        ("recruitment.json", "cards", "def_card.xml", "cards", "card"),
        ("buildings.json", "buildings", "def_construction.xml", "constructions", "construction"),
        ("installations.json", "installations", "def_installation.xml", "installations", "installation"),
        ("terrain-rules.json", "terrain", "def_terraintype.xml", "terraintypes", "terraintype"),
        ("conquest-list.json", "entries", "def_conquestlist.xml", "battlelist", "battle")];
    private static readonly string[] ProgressionKeys = ["commander_rank_xp", "commander_nobility_xp",
        "commander_rank_health_bonus", "commander_nobility_healing"];
    private static readonly string[] Weapons = ["gun", "guns", "mgun", "cannon", "rocket", "cold"];
    private static readonly string[] Categories = ["infantry", "cavalry", "artillery", "naval", "fort"];
    private static readonly JsonSerializerOptions Output = new() { WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public JsonObject Catalog { get; }
    public bool IsSplit { get; }
    public JsonObject RuntimeRules { get; private set; } = new();
    public JsonObject MetagameRules { get; private set; } = new();
    public IReadOnlyDictionary<string, string> Xml => xml;
    private readonly Dictionary<string, string> xml = new(StringComparer.Ordinal);

    private LegacyCatalogFiles(JsonObject catalog, bool split) { Catalog = catalog; IsSplit = split; }
    private static JsonObject Object(byte[] bytes) => JsonNode.Parse(bytes)?.AsObject()
        ?? throw new RuleViolation("legacy.catalog_file", "Expected a JSON object");
    public static byte[] Encode(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString(Output).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");

    public static LegacyCatalogFiles Load(Func<string, byte[]> read)
    {
        var index = Object(read("data/catalog.json"));
        int version = (int?)index["schema_version"] ?? 0;
        if (version == 2) return new(index, false) { RuntimeRules = Rules(read, "runtime_rule_tables.json"), MetagameRules = Rules(read, "metagame_rule_tables.json") };
        if (version != 3) throw new RuleViolation("legacy.catalog_version", "Unknown catalog version");
        var files = index["files"]?.AsArray().Select(v => (string)v!).ToArray() ?? [];
        if (!files.SequenceEqual(PartNames)) throw new RuleViolation("legacy.catalog_file", "Unexpected catalog file list");
        var parts = PartNames.ToDictionary(name => name, name => {
            var part = Object(read("data/" + name));
            if ((int?)part["schema"] != 1) throw new RuleViolation("legacy.catalog_file", "Unknown section version: " + name);
            return part;
        }, StringComparer.Ordinal);
        var catalog = new JsonObject { ["schema_version"] = 2, ["origin"] = index["origin"]?.DeepClone(),
            ["source_sha256"] = index["source_sha256"]!.DeepClone(),
            ["profiles"] = parts["units.json"]["units"]!.DeepClone(),
            ["variants"] = parts["unit-variants.json"]["variants"]!.DeepClone(),
            ["skills"] = parts["skills.json"]["skills"]!.DeepClone(),
            ["commanders"] = new JsonArray(parts["commanders.json"]["commanders"]!.AsArray().Select(ImportCommander).ToArray()) };
        var result = new LegacyCatalogFiles(catalog, true) { RuntimeRules = Rules(read, "runtime_rule_tables.json"), MetagameRules = Rules(read, "metagame_rule_tables.json") };
        var trees = new JsonObject();
        foreach (var part in XmlParts)
        {
            var root = new XElement(part.Root, parts[part.File][part.Key]!.AsArray().Select(r => Element(part.Element, r!.AsObject())));
            result.xml.Add(part.Source, root.ToString()); trees[part.Source] = Tree(root);
        }
        var commanderRoot = new XElement("commanders", catalog["commanders"]!.AsArray().Select(c =>
            Element("commander", c!["legacy_attributes"]!.AsObject())));
        result.xml.Add("def_commander.xml", commanderRoot.ToString()); trees["def_commander.xml"] = Tree(commanderRoot);
        // Preserve source-only army metadata for old research readers; gameplay uses units.json.
        trees["def_army.xml"] = Tree(XDocument.Parse(Encoding.UTF8.GetString(read("original/def_army.xml"))).Root!);
        catalog["source_trees"] = trees;
        var native = parts["combat-rules.json"]["tables"]!.DeepClone().AsObject();
        var progression = result.RuntimeRules;
        foreach (string key in ProgressionKeys) native[key] = progression[key]!.DeepClone();
        catalog["native_tables"] = native;
        foreach (var profile in catalog["profiles"]!.AsArray()) ResolveCombat(profile!.AsObject(), native);
        ValidateSkills(catalog["skills"]!.AsArray());
        return result;
    }

    public static string ReadCatalogFile(string path) => Load(name => File.ReadAllBytes(name.StartsWith("data/", StringComparison.Ordinal)
        ? Path.Combine(Path.GetDirectoryName(path)!, name[5..])
        : Path.Combine(Path.GetDirectoryName(path)!, "..", name))).Catalog.ToJsonString();

    public static byte[] ReadCatalogBytes(string path) => Encoding.UTF8.GetBytes(ReadCatalogFile(path));

    public bool ItemsMatchSource(Func<string, byte[]> read)
    {
        if (!IsSplit) return true;
        var original = XDocument.Parse(Encoding.UTF8.GetString(read("original/def_item.xml"))).Root!;
        return JsonNode.DeepEquals(Tree(original), Catalog["source_trees"]!["def_item.xml"]);
    }

    public static JsonObject Rules(Func<string, byte[]> read, string file)
    {
        var data = Object(read("data/" + file));
        if ((int?)data["schema_version"] == 1) return data;
        string[] expected = file == "runtime_rule_tables.json" ? ["unit-progression.json", "commander-progression.json"] : ["economy-rules.json"];
        if ((int?)data["schema_version"] != 2 || data["files"] is not JsonArray paths
            || !paths.Select(p => (string)p!).SequenceEqual(expected)) throw new RuleViolation("legacy.catalog_file", "Invalid rule table index: " + file);
        var result = new JsonObject { ["schema_version"] = 1, ["source_sha256"] = data["source_sha256"]!.DeepClone() };
        foreach (string name in expected)
        {
            var part = Object(read("data/" + name));
            if ((int?)part["schema"] != 1) throw new RuleViolation("legacy.catalog_file", "Unknown rule section: " + name);
            string[] keys = name switch {
                "unit-progression.json" => ["unit_veterancy_rows", "training_money"],
                "commander-progression.json" => ProgressionKeys,
                _ => ["title_commerce", "rank_percent", "title_percent"] };
            if (!keys.ToHashSet(StringComparer.Ordinal).SetEquals(part["rules"]!.AsObject().Select(p => p.Key)))
                throw new RuleViolation("legacy.catalog_file", "Missing or unknown rule fields: " + name);
            foreach (var pair in part["rules"]!.AsObject()) result[pair.Key] = pair.Value!.DeepClone();
        }
        return result;
    }

    public static Dictionary<string, byte[]> Split(JsonObject catalog, JsonObject runtimeRules, JsonObject metagameRules)
    {
        if ((int?)catalog["schema_version"] != 2) throw new RuleViolation("legacy.catalog_version", "Expected a legacy catalog to split");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        void Part(string name, string key, JsonNode value) => files.Add(name, Encode(new JsonObject { ["schema"] = 1, [key] = value }));
        var trees = catalog["source_trees"]!.AsObject();
        var native = catalog["native_tables"]!.DeepClone().AsObject();
        foreach (string key in ProgressionKeys) native.Remove(key);
        var units = catalog["profiles"]!.DeepClone().AsArray();
        var army = FromTree(trees["def_army.xml"]!.AsObject());
        var weapons = army.Elements().SelectMany(country => country.Elements("army").Select(unit => (
            Id: "ew4.unit." + (string)country.Attribute("name")! + "." + System.Text.RegularExpressions.Regex.Replace(
                ((string)unit.Attribute("name")!).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_') + "." + (string)unit.Attribute("grade")!,
            Weapon: (string)unit.Attribute("weapon")!))).ToDictionary(p => p.Id, p => p.Weapon, StringComparer.Ordinal);
        foreach (var node in units)
        {
            var unit = node!.AsObject();
            if (!weapons.TryGetValue((string)unit["id"]!, out string? weapon)) continue;
            unit["weapon"] ??= weapon;
            var generated = (JsonObject)unit.DeepClone(); generated.Remove("damage_multipliers");
            if (int.TryParse((string?)unit["grade"], out int grade) && grade is >= 0 and <= 2) generated.Remove("attack_bands");
            ResolveCombat(generated, native);
            foreach (string key in new[] { "attack_bands", "damage_multipliers" })
                if (JsonNode.DeepEquals(unit[key], generated[key])) unit.Remove(key);
        }
        Part("units.json", "units", units);
        Part("unit-variants.json", "variants", catalog["variants"]!.DeepClone());
        Part("skills.json", "skills", catalog["skills"]!.DeepClone());
        Part("commanders.json", "commanders", new JsonArray(catalog["commanders"]!.AsArray().Select(ExportCommander).ToArray()));
        foreach (var part in XmlParts)
            Part(part.File, part.Key, new JsonArray(FromTree(trees[part.Source]!.AsObject()).Elements().Select(e => (JsonNode)Record(e)).ToArray()));
        Part("combat-rules.json", "tables", native);
        Part("commander-progression.json", "rules", new JsonObject(ProgressionKeys.Select(key => KeyValuePair.Create<string, JsonNode?>(key, runtimeRules[key]!.DeepClone()))));
        Part("unit-progression.json", "rules", new JsonObject(runtimeRules.Where(p => p.Key is not ("schema_version" or "source_sha256") && !ProgressionKeys.Contains(p.Key))
            .Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))));
        Part("economy-rules.json", "rules", new JsonObject(metagameRules.Where(p => p.Key is not ("schema_version" or "source_sha256"))
            .Select(p => KeyValuePair.Create(p.Key, p.Value?.DeepClone()))));
        foreach (var (file, rules, names) in new[] {
            ("runtime_rule_tables.json", runtimeRules, new[] { "unit-progression.json", "commander-progression.json" }),
            ("metagame_rule_tables.json", metagameRules, new[] { "economy-rules.json" }) })
            files.Add(file, Encode(new JsonObject { ["schema_version"] = 2, ["source_sha256"] = rules["source_sha256"]!.DeepClone(), ["files"] = JsonSerializer.SerializeToNode(names) }));
        files.Add("catalog.json", Encode(new JsonObject { ["schema_version"] = 3, ["origin"] = catalog["origin"]?.DeepClone(),
            ["source_sha256"] = catalog["source_sha256"]!.DeepClone(), ["files"] = JsonSerializer.SerializeToNode(PartNames) }));
        return files;
    }

    private static void ResolveCombat(JsonObject unit, JsonObject tables)
    {
        if (unit["attack_bands"] == null)
        {
            if (!int.TryParse((string?)unit["grade"], out int grade) || grade is < 0 or > 2)
                throw new RuleViolation("legacy.catalog_profile", "Custom grades require explicit attack_bands");
            var thresholds = tables["attack_count_health_thresholds"]!.AsArray();
            if (thresholds.Count != 6) throw new RuleViolation("legacy.catalog_profile", "Expected six attack thresholds");
            unit["attack_bands"] = new JsonArray(Enumerable.Range(0, grade + 4).Select(i => (JsonNode)new JsonObject {
                ["strict_minimum_percent"] = thresholds[i]!.DeepClone(), ["rolls"] = i + 2 }).ToArray());
        }
        if (unit["damage_multipliers"] == null)
        {
            int weapon = Array.IndexOf(Weapons, (string?)unit["weapon"]);
            var values = tables["weapon_category_multipliers"]!.AsArray();
            if (weapon < 0 || values.Count != 30) throw new RuleViolation("legacy.catalog_profile", "Unknown weapon or invalid weapon multipliers");
            unit["damage_multipliers"] = new JsonObject(Categories.Select((category, i) =>
                KeyValuePair.Create<string, JsonNode?>(category, values[weapon * 5 + i]!.DeepClone())));
        }
    }

    private static JsonNode ExportCommander(JsonNode? node)
    {
        var c = node!.AsObject(); var attrs = c["legacy_attributes"]!.AsObject();
        var result = new JsonObject { ["id"] = c["id"]!.DeepClone(), ["legacy_id"] = int.Parse((string)attrs["id"]!, CultureInfo.InvariantCulture) };
        foreach (var attr in attrs.Where(p => p.Key != "id" && !new[] { "skill1", "skill2", "skill3", "skill4" }.Contains(p.Key)))
            result[attr.Key] = Scalar((string)attr.Value!);
        result["skills"] = c["skills"]!.DeepClone();
        if (c["skill_slots"] != null) result["skill_slots"] = c["skill_slots"]!.DeepClone();
        return result;
    }

    private static JsonNode ImportCommander(JsonNode? node)
    {
        var c = node!.AsObject();
        var attrs = new JsonObject { ["id"] = Text(c["legacy_id"]!) };
        foreach (var attr in c.Where(p => p.Key is not ("id" or "legacy_id" or "skills" or "skill_slots"))) attrs[attr.Key] = Text(attr.Value!);
        var skills = c["skills"]!.DeepClone();
        if (c["skill_slots"] is JsonArray slots && !slots.Select(s => (string)s!["id"]!).SequenceEqual(skills.AsArray().Select(s => (string)s!)))
            throw new RuleViolation("legacy.commander_definition", "skills and skill_slots disagree");
        for (int i = 0; i < 4; i++)
        {
            string? id = i < skills.AsArray().Count ? (string?)skills[i] : null;
            attrs["skill" + (i + 1)] = id?.StartsWith("ew4.skill.", StringComparison.Ordinal) == true ? id[10..] : "-1";
        }
        if ((string?)c["id"] != "ew4.commander." + Text(c["legacy_id"]!))
            throw new RuleViolation("legacy.commander_definition", "Commander ID disagrees with legacy_id");
        var result = new JsonObject { ["id"] = c["id"]!.DeepClone(), ["legacy_attributes"] = attrs, ["skills"] = skills };
        if (c["skill_slots"] != null) result["skill_slots"] = c["skill_slots"]!.DeepClone();
        return result;
    }

    private static void ValidateSkills(JsonArray skills)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skill in skills)
        {
            if (string.IsNullOrWhiteSpace((string?)skill!["id"]) || !ids.Add((string)skill["id"]!))
                throw new RuleViolation("legacy.catalog_skill", "Missing or duplicate skill ID");
            if (skill["legacy_id"] is { } legacy && ((int)legacy is < 0 or > 39 || (string?)skill["id"] != "ew4.skill." + (int)legacy))
                throw new RuleViolation("legacy.catalog_skill", "Skill ID disagrees with legacy_id");
        }
    }

    private static JsonNode Scalar(string value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number)
        ? JsonValue.Create(number)! : JsonValue.Create(value)!;
    private static string Text(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out string? text) ? text : node.ToJsonString();
    private static JsonObject Record(XElement e)
    {
        var result = new JsonObject(e.Attributes().Select(a => KeyValuePair.Create(a.Name.LocalName, (JsonNode?)Scalar(a.Value))));
        if (e.HasElements) result["children"] = new JsonArray(e.Elements().Select(child => {
            var row = Record(child); row["tag"] = child.Name.LocalName; return (JsonNode)row;
        }).ToArray());
        return result;
    }
    private static XElement Element(string tag, JsonObject row)
    {
        var e = new XElement(tag, row.Where(p => p.Key is not ("children" or "tag"))
            .Select(p => new XAttribute(p.Key, Text(p.Value!))));
        if (row["children"] is JsonArray children)
            foreach (var child in children) e.Add(Element((string)child!["tag"]!, child.AsObject()));
        return e;
    }
    private static JsonObject Tree(XElement e) => new() { ["tag"] = e.Name.LocalName,
        ["attributes"] = new JsonObject(e.Attributes().Select(a => KeyValuePair.Create<string, JsonNode?>(a.Name.LocalName, JsonValue.Create(a.Value)))),
        ["text"] = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim(),
        ["children"] = new JsonArray(e.Elements().Select(c => (JsonNode)Tree(c)).ToArray()) };
    private static XElement FromTree(JsonObject tree)
    {
        var e = new XElement((string)tree["tag"]!, tree["attributes"]!.AsObject().Select(p => new XAttribute(p.Key, (string)p.Value!)));
        if (!string.IsNullOrEmpty((string?)tree["text"])) e.Add((string)tree["text"]!);
        foreach (var child in tree["children"]!.AsArray()) e.Add(FromTree(child!.AsObject()));
        return e;
    }
}
