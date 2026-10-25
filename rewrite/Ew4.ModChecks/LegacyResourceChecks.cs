using System.Text;
using System.Text.Json.Nodes;
using Ew4.Legacy;
using Ew4.Mods;

internal static class LegacyResourceChecks
{
    internal static int Run(ModBase baseline, string run)
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
        void Reject(Action action) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("Missing full-package content accepted"); }
        string folder = Path.Combine(run, "resolution"); Directory.CreateDirectory(folder);
        void Write(string name, byte[] bytes) => ModFiles.Write(folder, name, bytes);
        using (var partial = new ImportSource(folder))
        {
            var resolver = new LegacyResourceResolver(baseline, partial, []);
            Check(resolver.Read("def_map.xml").SequenceEqual(baseline.Read("original/def_map.xml")), "overlay uses builtin shared definition");
            Check(resolver.Resolutions.Single().Source == "builtin:original/def_map.xml", "fallback records actual source");
        }
        Write("AndroidManifest.xml", Encoding.UTF8.GetBytes("<manifest/>"));
        using (var full = new ImportSource(folder))
        {
            var resolver = new LegacyResourceResolver(baseline, full, []);
            Reject(() => resolver.Read("conquest1.btl"));
            Reject(() => resolver.Read("europe.bin"));
        }
        var labels = ModFiles.Xml(baseline.Read("original/def_areaname.xml"));
        var battle = LegacyBattle.Read(baseline.Read("original/campaign1_01.btl"));
        var city = battle.Sites.First(s => s.CityId >= 0 && labels.Root!.Elements().Any(e => (int)e.Attribute("id")! == s.CityId));
        string expected = (string)labels.Root!.Elements().Single(e => (int)e.Attribute("id")! == city.CityId).Attribute("name")!;
        labels.Root.Elements().Single(e => (int)e.Attribute("id")! == city.CityId).Remove();
        Write("def_areaname.xml", Encoding.UTF8.GetBytes(labels.ToString()));
        Write("def_map.xml", baseline.Read("original/def_map.xml"));
        Write("campaign1_01.btl", battle.ToBytes());
        string map = (string)ModFiles.Xml(baseline.Read("original/def_map.xml")).Root!.Elements()
            .Single(e => (int)e.Attribute("id")! == battle.MapId).Attribute("file")!;
        byte[] mapBytes = baseline.Read("original/" + map); Write(map, mapBytes);
        using (var source = new ImportSource(folder))
        {
            var resolver = new LegacyResourceResolver(baseline, source, []); resolver.CompleteCityLabels();
            var resolved = ModFiles.Xml(resolver.Read("def_areaname.xml"));
            Check((string)resolved.Root!.Elements().Single(e => (int)e.Attribute("id")! == city.CityId).Attribute("name")! == expected,
                "matching map/site/id completes missing original city label");
            Check(resolver.Resolutions.Any(r => r.Reason == "matching-map-site-id")
                && resolver.Resolutions.Single(r => r.Resource == "def_areaname.xml").SourceHash != resolver.Resolutions.Single(r => r.Resource == "def_areaname.xml").ResolvedHash,
                "merged labels record raw and resolved hashes");
        }
        mapBytes[18] ^= 1; Write(map, mapBytes);
        using (var source = new ImportSource(folder))
        {
            var resolver = new LegacyResourceResolver(baseline, source, []); resolver.CompleteCityLabels();
            Check(!ModFiles.Xml(resolver.Read("def_areaname.xml")).Root!.Elements().Any(e => (int)e.Attribute("id")! == city.CityId),
                "changed map does not inherit labels solely by reused id");
        }
        Write(map, baseline.Read("original/" + map));
        labels.Root.Add(new System.Xml.Linq.XElement("areaname", new System.Xml.Linq.XAttribute("id", city.CityId), new System.Xml.Linq.XAttribute("name", "Mod City")));
        Write("def_areaname.xml", Encoding.UTF8.GetBytes(labels.ToString()));
        using (var source = new ImportSource(folder))
        {
            var resolver = new LegacyResourceResolver(baseline, source, []); resolver.CompleteCityLabels();
            Check(ModFiles.Text(resolver.Read("def_areaname.xml")).Contains("Mod City") && !resolver.Resolutions.Any(r => r.Reason == "matching-map-site-id"),
                "package label wins over original fallback");
        }
        var army = ModFiles.Xml(baseline.Read("original/def_army.xml"));
        var weapon = army.Root!.Elements().First().Elements("army").First();
        weapon.SetAttributeValue("strength", -999);
        byte[] ReadWeapon(string name) => name == "def_army.xml" ? Encoding.UTF8.GetBytes(army.ToString()) : baseline.Read("original/" + name);
        var converted = LegacyCatalogConverter.Convert(ReadWeapon, baseline.Catalog());
        var singleUse = converted["profiles"]![0]!;
        Check((int)singleUse["max_health"]! == 1 && singleUse["traits"]!.AsArray().Any(t => (string)t! == "consumed_on_attack"),
            "negative 999 strength converts to an explicit single-use trait");
        var runtime = LegacyRuntimeCatalog.FromJson(converted.ToJsonString());
        Check(runtime.Profiles[(string)singleUse["id"]!].Profile.HasTrait("consumed_on_attack"), "single-use catalog loads successfully");
        Check(converted["source_trees"]!["def_army.xml"]!.ToJsonString().Contains("-999"), "source sentinel remains available for audit");
        weapon.SetAttributeValue("strength", -998);
        bool rejected = false;
        try { LegacyRuntimeCatalog.FromJson(LegacyCatalogConverter.Convert(ReadWeapon, baseline.Catalog()).ToJsonString()); }
        catch (Ew4.Core.RuleViolation e) when (e.Code == "profile.invalid") { rejected = true; }
        Check(rejected, "unrecognized negative health still fails validation");
        var cards = ModFiles.Xml(baseline.Read("original/def_card.xml"));
        var unavailable = new System.Xml.Linq.XElement(cards.Root!.Elements().First(e => (string?)e.Attribute("type") == "recruit"));
        unavailable.SetAttributeValue("id", 9999); unavailable.SetAttributeValue("army", "Privateer"); unavailable.SetAttributeValue("grade", 2);
        cards.Root.Add(unavailable);
        var buildings = LegacyBuildingCatalog.Read(ModFiles.Text(baseline.Read("original/def_construction.xml")), ModFiles.Text(baseline.Read("original/def_capital.xml")));
        var definitions = LegacyRecruitmentCatalog.Read(cards.ToString());
        var production = LegacyAiCatalog.Production("fra", runtime, definitions, buildings);
        Check(!production.Offers.ContainsKey("ew4.card.9999") && !runtime.HasUnit("fra", 14, 2),
            "global card without a country profile is unavailable to both human and AI recruitment");
        bool missingRejected = false;
        try { runtime.Unit("fra", 14, 2); }
        catch (Ew4.Core.RuleViolation e) when (e.Code == "legacy.unit_definition") { missingRejected = true; }
        Check(missingRejected, "explicit scenario unit reference to an absent profile still fails");
        return checks;
    }
}
