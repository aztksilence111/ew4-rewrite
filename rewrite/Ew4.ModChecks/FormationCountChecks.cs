using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class FormationCountChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException(label); }
        string run = Path.Combine(root, "build/formation-count-checks", Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "rewrite/data"), author = Path.Combine(run, "author");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/")
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        string example = Path.Combine(root, "rewrite/mod-kit/examples/formation-counts");
        foreach (string file in Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(author, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        JsonElement Catalog(ModStore target)
        {
            using var response = JsonDocument.Parse(target.Dispatch("/api/catalog", "{}"));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), "catalog API succeeds");
            return response.RootElement.GetProperty("data").GetProperty("formationCounts").Clone();
        }
        Check(Catalog(store).EnumerateObject().Count() == 0, "builtin formation defaults remain unchanged");
        string configuration = Path.Combine(author, "rewrite/definitions/formation-counts.json");
        string id = "ew4.unit.others.militia.0";
        File.WriteAllText(configuration, JsonSerializer.Serialize(new { schema = 1, profiles = new Dictionary<string, int> { [id] = 5 } }));
        store.Seal(author, "formations", "formations", "1.0", ["conquest1"]);
        var imported = store.Import(author, "ignored", "ignored"); store.Activate(imported.Key);
        Check(Catalog(store).GetProperty(id).GetInt32() == 5, "imported five-formation setting reaches API");
        string archive = Path.Combine(run, "formations.ew4mod"); store.Export(imported.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        offline.Activate(offline.Import(archive, "ignored", "ignored").Key);
        Check(Catalog(offline).GetProperty(id).GetInt32() == 5, "offline package preserves setting");
        store.Activate(null); Check(Catalog(store).EnumerateObject().Count() == 0, "deactivation restores defaults");
        foreach (string invalid in new[] { "0", "-1", "100", "1.5", "\"5\"", "null" })
        {
            File.WriteAllText(configuration, "{\"schema\":1,\"profiles\":{\"" + id + "\":" + invalid + "}}");
            bool rejected = false;
            try { store.Seal(author, "formations", "formations", "bad"); store.Validate(author); }
            catch (Exception e) when (e is InvalidDataException or JsonException or Ew4.Core.RuleViolation) { rejected = true; }
            Check(rejected, "invalid formation count rejected: " + invalid);
        }
        File.WriteAllText(configuration, "{\"schema\":1,\"profiles\":{\"ew4.unit.missing\":4}}");
        bool unknown = false;
        try { store.Seal(author, "formations", "formations", "bad"); store.Validate(author); }
        catch (Exception e) when (e is InvalidDataException or Ew4.Core.RuleViolation) { unknown = true; }
        Check(unknown, "unknown profile rejected");
        return checks;
    }
}
