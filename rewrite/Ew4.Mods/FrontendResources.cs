using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace Ew4.Mods;

public sealed partial class ModStore
{
    // CSS image caches can survive a document reload. Give every content set its own URLs.
    public byte[] Frontend(byte[] bytes, string extension)
    {
        if (extension is not (".html" or ".css" or ".js")) return bytes;
        lock (gate)
        {
            string revision = activeKey ?? baseline.Revision;
            string text = Encoding.UTF8.GetString(bytes).Replace("content/", "content/r-" + revision + "/", StringComparison.Ordinal);
            if (extension == ".html")
                text = Regex.Replace(text, "((?:src|href)=\")([^\"]+\\.(?:js|css)(?:\\?[^\"]*)?)(\")",
                    m => m.Groups[1].Value + "compat/" + m.Groups[2].Value + (m.Groups[2].Value.Contains('?') ? "&" : "?") + "content=" + revision + m.Groups[3].Value);
            return Encoding.UTF8.GetBytes(text);
        }
    }

    public byte[] ReadContent(string path)
    {
        lock (gate)
        {
            if (path.StartsWith("r-", StringComparison.Ordinal))
            {
                int end = path.IndexOf('/');
                if (end < 0) throw new FileNotFoundException("Invalid content revision");
                string key = path[2..end], resource = ContentPath(path[(end + 1)..]);
                // In-flight requests from the closing document still resolve their own content set.
                if (key == baseline.Revision) return baseline.Read(resource);
                var selected = key == activeKey ? activeManifest : Manifest(key);
                return Presentation(ReadFrom(key, selected, resource), resource, selected);
            }
            string logical = ContentPath(path);
            return Presentation(ReadFrom(activeKey, activeManifest, logical), logical, activeManifest);
        }
    }
    private static string ContentPath(string path)
    {
        path = ModFiles.PathName(path);
        if (!path.StartsWith("extensions/", StringComparison.Ordinal)) return "client/" + path;
        if (!path.EndsWith(".png", StringComparison.Ordinal)) throw new FileNotFoundException("Only extension images are public");
        return "rewrite/" + path[11..];
    }

    private static byte[] Presentation(byte[] bytes, string path, ModManifest? selected)
    {
        if (path != "client/manifest.json" || selected == null) return bytes;
        var manifest = ModFiles.Object(bytes);
        var overrides = new JsonObject();
        bool RawTexture(string? reference)
        {
            if (reference == null) return false;
            string stem = Path.GetFileNameWithoutExtension(reference);
            return selected.Files.ContainsKey("client/images/" + stem + ".png") || selected.Files.ContainsKey("client/images/" + stem + "@2x.png");
        }
        var main = manifest["layouts"]!["form_mainmenu"]!["children"]!.AsArray();
        if (selected.Files.ContainsKey("client/ui/main.png") && !main.Any(n => RawTexture((string?)n!["attributes"]!["texture"]))) overrides["main"] = "ui/main.png";
        foreach (var (id, output) in new[] { ("form_selcampaign", "campaign_background"), ("form_selconquest", "conquest_background") })
            if (selected.Files.ContainsKey("client/ui/" + output + ".png") && !RawTexture((string?)manifest["layouts"]![id]!["attributes"]!["back"]))
                overrides[id] = "ui/" + output + ".png";
        if (selected.Files.ContainsKey("client/ui/campaign.png") && !RawTexture("tex_campaign_back.pkm")) overrides["strategic"] = "ui/campaign.png";
        manifest["uiOverrides"] = overrides;
        return Encoding.UTF8.GetBytes(manifest.ToJsonString());
    }
}
