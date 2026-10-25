using System.Text.Json;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;

internal static class LegacyTechnologyChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Legacy technology: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation error) { Check(error.Code == code, code + " actual=" + error.Code); return; }
            throw new InvalidOperationException("Expected technology rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        string xml = File.ReadAllText(Path.Combine(assets, "def_warzonetech.xml"));
        var technology = LegacyWarzoneTechnology.Read(xml);
        var cards = LegacyRecruitmentCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_card.xml")));
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var sample = LegacyBattle.Read(File.ReadAllBytes(Path.Combine(assets, "campaign1_01.btl"))).Factions[0];
        Check(technology.Defaults.Count == 6 && technology.Defaults.Values.All(v => v.Count == 26) && technology.SourceXml == xml, "complete original XML defaults retained");
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/technology_vectors.json")));
        foreach (var vector in vectors.RootElement.GetProperty("technology").EnumerateArray())
        {
            int zone = vector.GetProperty("warzone").GetInt32();
            var levels = vector.GetProperty("levels").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var expected = vector.GetProperty("expected").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            var source = sample with { Variant = "fra" };
            var applied = technology.Apply(source, 1, true, zone, levels);
            Check(applied.Values68.SequenceEqual(expected), "native account getter to faction copy matches every slot");
            Check(source.Values68.SequenceEqual(sample.Values68) && applied.Resources == source.Resources && applied.CountryId == source.CountryId
                && applied.Count172 == source.Count172, "technology changes no unrelated scenario fields");
            levels[0] = 999;
            Check(applied.Values68[0] == expected[0], "account snapshot copied rather than aliased");
            for (int mode = 1; mode <= 4; mode++)
                foreach (bool player in new[] { false, true })
                    if (mode != 1 || !player) Check(ReferenceEquals(technology.Apply(source, mode, player, zone, expected), source), "non-target modes and factions retain scenario technology");
            var faction = LegacyFactionRules.Import(applied, "winner", "test", "b", cards, runtime);
            foreach (var card in cards.Cards.Values.Where(c => c.Recruits))
            {
                Check(faction.Recruitment.ContainsKey(card.Id) == (expected[card.Unit] >= 0), "overridden unit availability reaches recruitment grants");
                if (!faction.Recruitment.TryGetValue(card.Id, out var grant)) continue;
                string category = runtime.Unit("fra", card.Unit, card.Grade).Profile.Category;
                int requirement = category switch { "infantry" => 1, "cavalry" => 2, "artillery" => 3, _ => int.MaxValue };
                Check(grant.Veterancy == expected[card.Unit].ToString(System.Globalization.CultureInfo.InvariantCulture)
                    && grant.Transport == (card.Type != "fortress" && expected[25] >= requirement), "overridden veterancy and transport eligibility reach actual grants");
            }
            long Bonus(int index, int step) => expected[index] is >= 1 and <= 3 ? expected[index] * step : 0;
            Check(faction.IncomeBonus == new Resources(Bonus(23, 20), Bonus(24, 10), Bonus(22, 10)), "overridden economy slots reach modern income bonuses");
        }
        foreach (var pair in technology.Defaults)
            Check(technology.Apply(sample, 1, true, pair.Key).Values68.SequenceEqual(pair.Value), "new account uses XML default snapshot");
        var extended = XDocument.Parse(xml);
        var extra = new XElement(extended.Root!.Element("warzone")!); extra.SetAttributeValue("id", 100); extended.Root.Add(extra);
        Check(LegacyWarzoneTechnology.Read(extended.ToString()).Defaults.ContainsKey(100), "warzone registry is not a six-entry array");
        var duplicate = XDocument.Parse(xml); duplicate.Root!.Add(new XElement(duplicate.Root.Element("warzone")!));
        Reject(() => LegacyWarzoneTechnology.Read(duplicate.ToString()), "legacy.technology");
        var duplicateTech = XDocument.Parse(xml); var zone0 = duplicateTech.Root!.Element("warzone")!; zone0.Add(new XElement(zone0.Element("tech")!));
        Reject(() => LegacyWarzoneTechnology.Read(duplicateTech.ToString()), "legacy.technology");
        var missing = XDocument.Parse(xml); missing.Root!.Element("warzone")!.Element("tech")!.Remove();
        Reject(() => LegacyWarzoneTechnology.Read(missing.ToString()), "legacy.technology");
        Reject(() => technology.Apply(sample, 0, true, 0), "legacy.technology_mode");
        Reject(() => technology.Apply(sample, 1, true, 999), "legacy.technology");
        Reject(() => technology.Apply(sample, 1, true, 0, new int[25]), "legacy.technology");
        Reject(() => technology.Apply(sample, 1, true, 0, Enumerable.Repeat(-2, 26).ToArray()), "legacy.technology");
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_technology.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            warzones = technology.Defaults.Count, nativeSnapshots = 200, limitation = "Explicit import policy and component grants/economy tested; complete scenario Start and saved account technology profile migration remain pending." }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
