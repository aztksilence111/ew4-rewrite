using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LegacyFactionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Legacy faction: " + label); }
        void Reject(Action action, string code)
        {
            try { action(); } catch (RuleViolation e) { Check(e.Code == code, code + ": " + e.Code); return; }
            throw new InvalidOperationException("Expected faction rejection: " + code);
        }
        string assets = Path.Combine(root, "apk_work/original/assets");
        string xml = File.ReadAllText(Path.Combine(assets, "def_card.xml"));
        var cards = LegacyRecruitmentCatalog.Read(xml);
        var buildings = LegacyBuildingCatalog.Read(File.ReadAllText(Path.Combine(assets, "def_construction.xml")), File.ReadAllText(Path.Combine(assets, "def_capital.xml")));
        var runtime = LegacyRuntimeCatalog.FromJson(Ew4.Legacy.LegacyCatalogFiles.ReadCatalogFile(Path.Combine(root, "rewrite/data/legacy/catalog.json")));
        var sample = LegacyBattle.Read(File.ReadAllBytes(Path.Combine(assets, "campaign1_01.btl"))).Factions[0];
        var offers = cards.Offers("fra", runtime, buildings);
        using var vectors = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reverse/reports/faction_recruitment_vectors.json")));
        foreach (var v in vectors.RootElement.GetProperty("cards").EnumerateArray())
        {
            var c = cards.Cards[v.GetProperty("id").GetInt32()];
            int[] actual = [c.LegacyId, Array.IndexOf(new[] { "recruit", "fortress", "installation", "special" }, c.Type), c.Grade,
                (int)c.Cost.Money, (int)c.Cost.Industry, c.Cooldown, c.ConstructionTurns, c.Unit];
            Check(actual.SequenceEqual(v.GetProperty("expected").EnumerateArray().Select(e => e.GetInt32())), "native card loader mapping");
        }
        foreach (var v in vectors.RootElement.GetProperty("tables").EnumerateArray())
        {
            int index = v.GetProperty("index").GetInt32(), value = v.GetProperty("value").GetInt32();
            int[] table = new int[26]; table[index] = value;
            var source = sample with { Values68 = table, State176 = 3, Variant = "fra" };
            var rules = LegacyFactionRules.Read(source);
            var faction = LegacyFactionRules.Import(source, "winner", "sample", "b", cards, runtime);
            Check(faction.Shortage == 3, "native shortage field at source176");
            for (int unit = 0; unit < 22; unit++)
                Check(rules.RecruitmentVeterancy.GetValueOrDefault(unit, -1) == (table[unit] < 0 ? -1 : table[unit]), "all 22 source entries map as availability plus veterancy");
        }
        foreach (var v in vectors.RootElement.GetProperty("income").EnumerateArray())
        {
            int[] table = new int[26]; table[v.GetProperty("index").GetInt32()] = v.GetProperty("value").GetInt32();
            var source = sample with { Values68 = table, State176 = 0, IncomeMultiplier = 1.25f, Variant = "fra" };
            var faction = LegacyFactionRules.Import(source, "winner", "sample", "b", cards, runtime);
            var basis = WorldChecks.Example();
            var state = basis with { Units = [], Factions = basis.Factions.Select(f => f.Id == "winner" ? faction : f).ToArray(),
                Cells = basis.Cells.Select((c, i) => c with { Owner = i == 0 ? "winner" : null, IncomeOverride = new(31, 17, 13) }).ToArray() };
            var actual = WorldTurns.FactionIncome(state, faction, new());
            Check(new[] { actual.Money, actual.Industry, actual.Food }.SequenceEqual(v.GetProperty("expected").EnumerateArray().Select(e => e.GetInt64())), "real income uses correct three source slots and ignores unrelated fields");
        }
        int succeeded = 0;
        foreach (var v in vectors.RootElement.GetProperty("recruitment").EnumerateArray())
        {
            int Int(string key) => v.GetProperty(key).GetInt32();
            bool Flag(string key) => v.GetProperty(key).GetBoolean();
            var card = cards.Cards[Int("card")]; var offer = offers[card.Id];
            int[] table = new int[26]; table[card.Unit] = Int("veteran"); table[25] = Int("transport");
            var source = sample with { Values68 = table, State176 = 0, Resources = new(Int("money"), Int("industry"), 0), Variant = "fra" };
            var actor = LegacyFactionRules.Import(source, "winner", "sample", "b", cards, runtime) with
            { TurnUses = new Dictionary<string, int> { ["ew4.fortress"] = Flag("fort_used") ? 1 : 0 } };
            var example = WorldChecks.Example();
            var state = example with { Factions = example.Factions.Select(f => f.Id == actor.Id ? actor : f).ToArray(),
                Units = Flag("occupied") ? [example.Units[1] with { Cell = 0 }] : [],
                Cells = example.Cells.Select((c, i) => c with { Owner = i == 0 ? Flag("own") ? "winner" : "loser" : c.Owner,
                    Terrain = c.Terrain with { Active = i != 0 || Flag("active"), Sea = i == 0 && Flag("sea") },
                    BuildingKind = i == 0 && Flag("building") ? LegacyBuildingCatalog.Kinds[Int("kind")] : null,
                    BuildingLevel = i == 0 && Flag("building") ? Int("level") : 0 }).ToArray() };
            var expected = v.GetProperty("expected");
            if (Flag("occupied") && !Flag("active"))
            {
                Reject(() => WorldActions.CanRecruit(state, 0, offer), "world.unit");
                continue;
            }
            Check(WorldActions.CanRecruit(state, 0, offer) == expected[0].GetBoolean(), "native combined faction and building eligibility");
            byte[] original = WorldSave.Encode(state);
            if (!expected[1].GetBoolean())
            {
                Reject(() => WorldActions.Recruit(state, state.Revision, 0, "new.unit", offer), expected[0].GetBoolean() ? "recruit.resources" : "recruit.site");
                Check(original.SequenceEqual(WorldSave.Encode(state)), "failed imported recruitment is atomic");
                continue;
            }
            var next = WorldActions.Recruit(state, state.Revision, 0, "new.unit", offer).State;
            var result = next.Units.Single(u => u.Id == "new.unit"); var faction = next.Factions.Single(f => f.Id == "winner");
            Check(faction.Resources.Money == expected[2].GetInt64() && faction.Resources.Industry == expected[3].GetInt64(), "native loaded card costs spent");
            Check(result.Veterancy == expected[4].GetInt32().ToString(CultureInfo.InvariantCulture)
                && result.Transport == expected[5].GetBoolean() && (faction.TurnUses.GetValueOrDefault("ew4.fortress") > 0) == expected[6].GetBoolean(), "native initial veterancy/transport/fort turn usage");
            Check(result.Combatant.ConstructionTurns == expected[7].GetInt32() && result.RemainingMovement == 0 && result.Acted
                && result.Support.FoodConsumption == runtime.Unit("fra", card.Unit, card.Grade).FoodConsumption, "native construction and ready flags plus upkeep");
            Check(original.SequenceEqual(WorldSave.Encode(state)), "successful recruit preserves original snapshot");
            var restored = WorldSave.Decode(WorldSave.Encode(next), next.ContentRevision);
            Check(restored.Units.Single(u => u.Id == "new.unit").Veterancy == result.Veterancy
                && restored.Factions.Single(f => f.Id == "winner").Recruitment[card.Id] == faction.Recruitment[card.Id], "imported grants and recruited state survive save");
            succeeded++;
        }
        Check(succeeded == 317, "successful joint native samples exercised");

        // Exact row qualification can represent upgrades that remove a recruitment option.
        var fixture = WorldChecks.Example() with { Units = [] };
        var selective = offers["ew4.card.0"] with { AllowedBuildingLevels = new Dictionary<string, IReadOnlyCollection<int>> { ["city"] = new[] { 1, 3, 8 } } };
        fixture = fixture with { Factions = fixture.Factions.Select(f => f.Id != "winner" ? f : f with
            { Recruitment = new Dictionary<string, RecruitmentGrant> { [selective.Id] = new() } }).ToArray() };
        foreach (int level in new[] { 0, 1, 2, 3, 6, 8, 20 })
        {
            var world = fixture with { Cells = fixture.Cells.Select((c, i) => i == 8 ? c with { BuildingKind = "city", BuildingLevel = level } : c).ToArray() };
            Check(WorldActions.CanRecruit(world, 8, selective) == new[] { 1, 3, 8 }.Contains(level), "configuration permits disjoint levels beyond old six-level limit");
            Check(!WorldActions.CanRecruit(world, 8, selective with { AllowedBuildingLevels = new Dictionary<string, IReadOnlyCollection<int>>() }), "explicit empty allowed rows disables recruitment");
        }
        Reject(() => (selective with { AllowedBuildingLevels = new Dictionary<string, IReadOnlyCollection<int>> { ["city"] = [-1] } }).Validate(), "recruit.configuration");
        Reject(() => LegacyFactionRules.Read(sample with { Values68 = [1] }), "legacy.faction_table");
        Check(LegacyFactionRules.Import(sample with { State176 = 6 }, "x", "x", "b", cards, runtime).Shortage == 6,
            "serialized shortage above four survives import until economy recomputes it");
        var debt = LegacyFactionRules.Import(sample with { Resources = new(-3000, -1500, 8600) }, "x", "x", "b", cards, runtime);
        Check(debt.Resources.Money == -3000 && debt.Resources.Industry == -1500, "negative opening balance is explicitly preserved");
        var firstDebt = TurnEconomy.Start(1, debt.Resources, new(100, 1600, 0), 0, legacyResourceDebt: true);
        var settledDebt = TurnEconomy.Start(2, debt.Resources, new(100, 1600, 0), 0, legacyResourceDebt: true);
        Check(firstDebt.Resources == debt.Resources && settledDebt.Resources == new Resources(0, 100, 8600),
            "native opening debt retained in first round and clamped after next income update");
        Check(AiProduction.AffordableCopies(debt.Resources, new(1, 0, 0)) == 0
            && AiProduction.AffordableCopies(debt.Resources, new(0, 0, 0)) == 0, "opening debt cannot create negative affordability or free purchases");
        Reject(() => LegacyRecruitmentCatalog.Read("<wrong/>"), "legacy.card_definition");
        Reject(() => LegacyRecruitmentCatalog.Read("<cards><card id='0' name='a' type='unknown'/></cards>"), "legacy.card_definition");
        Reject(() => LegacyRecruitmentCatalog.Read("<cards><card id='0' name='a' type='special'/><card id='0' name='b' type='special'/></cards>"), "legacy.card_definition");
        var extra = XDocument.Parse(xml); var extraCard = new XElement(extra.Root!.Elements().First()); extraCard.SetAttributeValue("id", 1000); extra.Root.Add(extraCard);
        Check(LegacyRecruitmentCatalog.Read(extra.ToString()).Offers("fra", runtime, buildings).Count == 42, "import adapter does not copy native 47-card storage limit");
        Check(cards.SourceRevision == LegacyRecruitmentCatalog.Read(xml).SourceRevision
            && cards.SourceRevision != LegacyRecruitmentCatalog.Read(xml.Replace("price=\"60\"", "price=\"61\"")).SourceRevision, "card definitions contribute deterministic digest");

        int files = 0, factions = 0, grants = 0;
        var columns = Enumerable.Range(0, 26).Select(_ => new SortedSet<int>()).ToArray();
        var shortages = new SortedSet<int>(); var deploymentCounts = new SortedSet<int>();
        foreach (var file in Directory.GetFiles(assets, "*.btl").Order(StringComparer.Ordinal))
        {
            var battle = LegacyBattle.Read(File.ReadAllBytes(file));
            for (int i = 0; i < battle.Factions.Count; i++)
            {
                var source = battle.Factions[i]; var rules = LegacyFactionRules.Read(source);
                var faction = LegacyFactionRules.Import(source, "faction." + i, source.CountryId, "camp." + source.Camp, cards, runtime);
                var choices = cards.Offers(source.Variant, runtime, buildings);
                Check(faction.Human == !source.Ai && faction.Shortage == source.State176 && faction.IncomeBonus == rules.IncomeBonus, "real faction state import");
                foreach (var card in cards.Cards.Values.Where(c => c.Recruits))
                {
                    bool available = source.Values68[card.Unit] >= 0;
                    Check(faction.Recruitment.ContainsKey(card.Id) == available, "every real faction recruit authorization");
                    if (!available) continue;
                    Check(choices[card.Id].Template!.Profile.Id == runtime.Unit(source.Variant, card.Unit, card.Grade).Profile.Id, "country variant resolved for recruit");
                    grants++;
                }
                for (int column = 0; column < 26; column++) columns[column].Add(source.Values68[column]);
                shortages.Add(source.State176);
                deploymentCounts.Add(source.Count172);
                factions++;
            }
            files++;
        }
        Check(files == 95 && factions == 543, "all original faction tables imported");
        var report = new { files, factions, cards = cards.Cards.Count, recruitmentCards = offers.Count, grants,
            nativeSuccesses = succeeded, tableValues = columns, shortageValues = shortages, deploymentCountsStillToBind = deploymentCounts, checks,
            limitations = new[] { "Faction/recruitment components; no complete Start, client or Android APK.",
                "Count172 deployment capacity, metagame technology overrides, cards41..46 execution adapters, full combat/progression rules and shared rule fingerprint remain to bind." } };
        File.WriteAllText(Path.Combine(root, "rewrite/reports/legacy_factions.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
