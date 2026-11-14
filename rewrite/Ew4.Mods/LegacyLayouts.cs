using System.Globalization;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Legacy;

namespace Ew4.Mods;

// Transfer only author changes between device variants; stock device offsets are not Mod edits.
internal static class LegacyLayouts
{
    private static XElement Parse(byte[] bytes)
    {
        // Native layout reader skips comments, including stock comments containing repeated hyphens.
        string text = System.Text.RegularExpressions.Regex.Replace(ModFiles.Text(bytes), "<!--[\\s\\S]*?-->", "");
        return ModFiles.Xml(LegacyXml.Normalize(System.Text.Encoding.UTF8.GetBytes(text), out _)).Root!;
    }
    internal static void Import(JsonObject manifest, ModBase baseline, LegacyResourceResolver resources, List<string> warnings)
    {
        string[] variants = ["layout-568h.xml", "layout.xml", "layout-640h.xml", "layout-ipad.xml", "layout-ipadhd.xml", "layout-hd.xml"];
        variants = variants.Concat(resources.Names.Where(n => n.StartsWith("layout-", StringComparison.Ordinal) && n.EndsWith(".xml", StringComparison.Ordinal))).Distinct().ToArray();
        var layouts = manifest["layouts"]!.AsObject();
        var references = baseline.Files.Contains("client/import-layouts.json") ? ModFiles.Object(baseline.Read("client/import-layouts.json")) : new JsonObject();
        var used = new JsonObject(); var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in variants.Where(resources.Changed))
        {
            var source = Parse(resources.Read(file));
            XElement? original = references[file] is { } reference ? Parse(System.Text.Encoding.UTF8.GetBytes(reference.GetValue<string>()))
                : baseline.Files.Contains("original/" + file) ? Parse(baseline.Read("original/" + file)) : null;
            foreach (var form in source.Elements().GroupBy(e => (string?)e.Attribute("id")).Where(g => g.Key != null).Select(g => g.Last()))
            {
                string id = (string)form.Attribute("id")!;
                var stock = original?.Elements().LastOrDefault(e => (string?)e.Attribute("id") == id);
                if (stock != null && XNode.DeepEquals(form, stock)) continue;
                if (!claimed.Add(id)) continue; // Prefer the native 568 canvas, then the author's standard layout.
                if (file == "layout-568h.xml" || stock == null || layouts[id] == null) layouts[id] = LegacyCatalogConverter.Layout(form);
                else Merge(layouts[id]!.AsObject(), stock, form);
                used[id] = file;
            }
        }
        if (used.Count > 0)
        {
            manifest["layoutSources"] = used;
            Bridge(manifest, ModFiles.Object(baseline.Read("client/manifest.json")));
            warnings.Add("已按原版差量合并UI布局（优先568h，再标准布局）: " + string.Join(", ", used.Select(p => p.Key + "←" + p.Value)));
        }
    }

    private static void Bridge(JsonObject manifest, JsonObject baseline)
    {
        var screens = new JsonObject();
        JsonNode? Node(JsonObject source, string form, string path)
        {
            JsonNode? node = source["layouts"]?[form];
            foreach (string part in path.Split('/')) node = node?["children"]?.AsArray().LastOrDefault(n => (string?)n?["attributes"]?["id"] == part);
            return node?["attributes"];
        }
        foreach (var (screen, form, bindings) in new (string, string, (string, string)[])[] {
            ("main", "form_mainmenu", [("entries", "menu"), ("campaign", "menu/btn_battle"), ("conquest", "menu/btn_conquest"), ("tutorial", "menu/btn_tutorials"), ("headquarters", "menu/btn_hq"), ("settings", "menu/btn_options")]),
            ("pause", "form_pause", [("save", "btn_save"), ("settings", "btn_option"), ("restart", "btn_restart"), ("exit", "btn_exit")]),
            ("battle", "form_game", [("pause", "btn_pause"), ("endTurn", "btn_next"), ("undo", "btn_undo"), ("skipAi", "btn_skip")]) })
        {
            var controls = new JsonObject();
            foreach (var (control, path) in bindings)
            {
                var current = Node(manifest, form, path); var stock = Node(baseline, form, path);
                if (current == null || stock == null) continue;
                var style = new JsonObject();
                // Battlefield coordinates need responsive anchoring; import its art, retain safe touch geometry.
                foreach (string key in screen == "battle" ? new[] { "frm1", "frm2" } : new[] { "x", "y", "w", "h", "frm1", "frm2" })
                {
                    string? value = (string?)current[key];
                    if (value == null || value == (string?)stock[key]) continue;
                    if (key is "frm1" or "frm2") style[key == "frm1" ? "image" : "pressedImage"] = value;
                    else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)) style[key] = number;
                }
                if (style.Count > 0) controls[control] = style;
            }
            if (controls.Count > 0) screens[screen] = new JsonObject { ["controls"] = controls };
        }
        if (screens.Count > 0) manifest["legacyUiPresentation"] = new JsonObject { ["schema"] = 1, ["screens"] = screens };
    }

    private static void Merge(JsonObject target, XElement stock, XElement edited)
    {
        var attrs = target["attributes"]!.AsObject();
        foreach (var a in edited.Attributes())
        {
            string key = a.Name.LocalName; string? old = (string?)stock.Attribute(a.Name);
            if (old == a.Value) continue;
            if (key is "x" or "y" or "w" or "h" && double.TryParse(old, NumberStyles.Float, CultureInfo.InvariantCulture, out double before)
                && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double after)
                && double.TryParse(attrs[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double current))
                attrs[key] = (current + after - before).ToString(CultureInfo.InvariantCulture);
            else attrs[key] = a.Value;
        }
        foreach (var a in stock.Attributes().Where(a => edited.Attribute(a.Name) == null)) attrs.Remove(a.Name.LocalName);
        var children = target["children"]!.AsArray();
        // Occurrence within an ID handles original XML's repeated decoration IDs.
        var seen = new Dictionary<string, int>();
        foreach (var child in edited.Elements())
        {
            string id = (string?)child.Attribute("id") ?? "";
            int index = seen.GetValueOrDefault(id); seen[id] = index + 1;
            var old = stock.Elements().Where(e => (string?)e.Attribute("id") == id).ElementAtOrDefault(index);
            var destination = children.Where(n => (string?)n?["attributes"]?["id"] == id).ElementAtOrDefault(index);
            if (old == null || destination == null) children.Add(LegacyCatalogConverter.Layout(child));
            else Merge(destination.AsObject(), old, child);
        }
        foreach (var group in stock.Elements().GroupBy(e => (string?)e.Attribute("id") ?? ""))
        {
            int keep = edited.Elements().Count(e => (string?)e.Attribute("id") == group.Key);
            foreach (var node in children.Where(n => (string?)n?["attributes"]?["id"] == group.Key).Skip(keep).ToArray()) children.Remove(node);
        }
    }
}
