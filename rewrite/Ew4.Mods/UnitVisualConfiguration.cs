using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Ew4.Mods;

internal static class UnitVisualConfiguration
{
    internal static JsonObject FromLegacy(XElement source, JsonObject profiles)
    {
        var variants = new JsonObject(); var defaults = new JsonObject();
        var kinds = profiles.Select(p => (string)p.Value!["kind"]!).Distinct().OrderByDescending(k => k.Length).ToArray();
        foreach (var unit in source.Elements())
        {
            string key = (string)unit.Attribute("name")!;
            foreach (string kind in kinds)
            {
                if (key == kind)
                {
                    for (int grade = 0; grade < 3; grade++) defaults.TryAdd(LegacyCatalogConverter.Slug(kind) + "." + grade, JsonValue.Create(key));
                    break;
                }
                if (!key.StartsWith(kind + " ", StringComparison.Ordinal)) continue;
                string[] tail = key[(kind.Length + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (!int.TryParse(tail[^1], out int formation)) continue;
                string slot = LegacyCatalogConverter.Slug(kind) + "." + (formation - 1);
                if (tail.Length == 1) defaults[slot] = key;
                else if (tail.Length == 2)
                {
                    if (variants[tail[0]] is not JsonObject) variants[tail[0]] = new JsonObject();
                    variants[tail[0]]![slot] = key;
                }
                break;
            }
        }
        return new() { ["schema"] = 1, ["factions"] = new JsonObject(), ["profiles"] = new JsonObject(),
            ["variants"] = variants, ["defaults"] = defaults, ["definitions"] = new JsonObject() };
    }

    internal static void Validate(JsonObject config, JsonObject manifest, JsonObject effects, Func<string, byte[]> read)
    {
        if ((int?)config["schema"] != 1) throw new InvalidDataException("Unsupported unit-visuals schema");
        var bank = (JsonObject)effects["motions"]!.DeepClone();
        foreach (var (key, definition) in config["definitions"]!.AsObject()) bank[key] = definition!.DeepClone();
        var libraries = new Dictionary<string, HashSet<string>>();
        foreach (var (key, value) in bank)
        {
            var definition = value!.AsObject(); string library = (string)definition["res"]!;
            ModFiles.PathName(library);
            foreach (string field in new[] { "x", "y", "scale" })
            {
                double number = definition[field] == null ? field == "scale" ? 1 : 0
                    : double.Parse(definition[field]!.ToString(), CultureInfo.InvariantCulture);
                if (!double.IsFinite(number) || Math.Abs(number) > 10000 || field == "scale" && number == 0)
                    throw new InvalidDataException("Invalid unit visual geometry: " + key);
            }
            if (!libraries.TryGetValue(library, out var items))
            {
                var animation = ModFiles.Object(read("client/animations/" + library + ".json"));
                ResourceValidation.CheckAnimation(animation);
                items = animation["items"]!.AsArray().Select(i => (string)i!["name"]!).ToHashSet(); libraries[library] = items;
                foreach (var texture in animation["atlas"]!["textures"]!.AsArray()) read("client/images/" + (string)texture!["name"]!);
            }
            foreach (var action in definition["motions"]!.AsArray())
            {
                if (!items.Contains((string)action!["name"]!)) throw new InvalidDataException("Unit visual action missing: " + key + "/" + action["name"]);
                if (action["loop"] != null && (action["loop"] is not JsonValue loop || !loop.TryGetValue<bool>(out _)))
                    throw new InvalidDataException("Invalid unit visual loop: " + key);
                if (action["static"] != null && (action["static"] is not JsonValue stationary || !stationary.TryGetValue<bool>(out _)))
                    throw new InvalidDataException("Invalid static unit visual: " + key);
                if (action["speed"] != null && (!double.TryParse(action["speed"]!.ToString(), CultureInfo.InvariantCulture, out double speed) || !double.IsFinite(speed) || speed <= 0))
                    throw new InvalidDataException("Invalid unit visual speed: " + key);
            }
            if (definition["attackVisuals"] is JsonObject attacks)
                foreach (var (layer, target) in attacks)
                    if (layer is not ("air" or "surface") || target == null || !bank.ContainsKey(target.GetValue<string>()))
                        throw new InvalidDataException("Invalid target layer attack visual: " + key);
        }
        if (config["composites"] is JsonObject composites)
            foreach (var (id, value) in composites)
            {
                if (!manifest["profiles"]!.AsObject().ContainsKey(id) || value is not JsonArray parts || parts.Count is < 1 or > 8)
                    throw new InvalidDataException("Invalid composite visual profile: " + id);
                foreach (var part in parts)
                {
                    if (part is not JsonObject p || p["visual"] == null || !bank.ContainsKey(p["visual"]!.GetValue<string>())
                        || (string?)p["layer"] is not ("air" or "surface")) throw new InvalidDataException("Invalid composite visual part: " + id);
                    foreach (string field in new[] { "x", "y" })
                        if (p[field] != null && (!double.TryParse(p[field]!.ToString(), CultureInfo.InvariantCulture, out double n) || !double.IsFinite(n) || Math.Abs(n) > 10000))
                            throw new InvalidDataException("Invalid composite part offset: " + id);
                }
            }
        void Bindings(JsonObject bindings)
        {
            foreach (var (_, value) in bindings)
                if (value == null || !bank.ContainsKey(value.GetValue<string>())) throw new InvalidDataException("Unknown unit visual binding: " + value);
        }
        Bindings(config["defaults"]!.AsObject()); Bindings(config["profiles"]!.AsObject());
        foreach (var (id, _) in config["profiles"]!.AsObject())
            if (!manifest["profiles"]!.AsObject().ContainsKey(id)) throw new InvalidDataException("Unknown visual profile: " + id);
        foreach (var (_, bindings) in config["variants"]!.AsObject()) Bindings(bindings!.AsObject());
        foreach (var (_, variant) in config["factions"]!.AsObject())
            if (variant == null || string.IsNullOrWhiteSpace(variant.GetValue<string>()) || variant.GetValue<string>().Length > 128)
                throw new InvalidDataException("Invalid visual faction variant");
    }
}
