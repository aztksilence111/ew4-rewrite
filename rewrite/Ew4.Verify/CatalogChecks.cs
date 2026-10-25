using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;

internal static class CatalogChecks
{
    public static int Run(string root)
    {
        using var data = JsonDocument.Parse(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var document = data.RootElement;
        if (document.GetProperty("schema_version").GetInt32() != 2) throw new InvalidOperationException("Unknown catalog schema");
        int count = 1;
        foreach (var source in document.GetProperty("source_sha256").EnumerateObject())
        {
            string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "apk_work/original/assets", source.Name)))).ToLowerInvariant();
            if (digest != source.Value.GetString()) throw new InvalidOperationException("Stale imported source: " + source.Name);
            count++;
        }
        var profiles = new Registry<UnitProfile>();
        var runtime = LegacyRuntimeCatalog.FromJson(document.GetRawText());
        foreach (var profile in document.GetProperty("profiles").EnumerateArray())
        {
            var parsed = runtime.Profiles[profile.GetProperty("id").GetString()!].Profile;
            profiles.Add(parsed.Id, parsed);
            count++;
        }
        foreach (var variant in document.GetProperty("variants").EnumerateObject())
            foreach (var reference in variant.Value.EnumerateObject()) { profiles.Get(reference.Value.GetString()!); count++; }
        if (profiles.Entries.Where(p => p.Key.Contains(".rocket.", StringComparison.Ordinal)).Any(p => !p.Value.HasTrait("line_splash") || !p.Value.HasTrait("ai.rocket")))
            throw new InvalidOperationException("Imported rocket is missing splash/AI traits");
        count++;
        var skills = new Registry<string>();
        foreach (var skill in document.GetProperty("skills").EnumerateArray())
        {
            skills.Add(skill.GetProperty("id").GetString()!, skill.GetProperty("names").GetProperty("zh_CN").GetString()!);
            count++;
        }
        var commanders = new Registry<string>();
        foreach (var general in document.GetProperty("commanders").EnumerateArray())
        {
            commanders.Add(general.GetProperty("id").GetString()!, general.GetProperty("legacy_attributes").GetProperty("name").GetString()!);
            foreach (var skill in general.GetProperty("skills").EnumerateArray()) skills.Get(skill.GetString()!);
            count++;
        }
        var extension = profiles.Entries.First().Value with
        {
            Id = "new.unit.test", Grade = "rank_12", Category = "custom_category",
            AttackBands = Enumerable.Range(0, 12).Select(i => new AttackBand(i * 8, i + 1)).ToArray()
        };
        extension.Validate();
        if (Ew4Combat.AttackCount(new Combatant { Profile = extension, Health = extension.MaxHealth }) != 12)
            throw new InvalidOperationException("Hidden grade/attack-band capacity");
        count++;
        return count;
    }
}
