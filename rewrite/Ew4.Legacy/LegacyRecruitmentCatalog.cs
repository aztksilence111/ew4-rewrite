using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record LegacyCardDefinition(int LegacyId, string Name, string Type, int Unit, int Grade,
    Resources Cost, int Cooldown, int ConstructionTurns, string Image, string Intro)
{
    public string Id => "ew4.card." + LegacyId;
    public bool Recruits => Type is "recruit" or "fortress";
}

public sealed class LegacyRecruitmentCatalog
{
    public static IReadOnlyDictionary<int, string> InstallationKinds { get; } = new ReadOnlyDictionary<int, string>(
        new Dictionary<int, string> { [0] = "trench", [1] = "fence", [2] = "bunker" });
    public IReadOnlyDictionary<int, LegacyCardDefinition> Cards { get; }
    public string SourceXml { get; }
    public string SourceRevision { get; }
    private LegacyRecruitmentCatalog(Dictionary<int, LegacyCardDefinition> cards, string source)
    {
        Cards = new ReadOnlyDictionary<int, LegacyCardDefinition>(cards); SourceXml = source;
        SourceRevision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
    }

    public static LegacyRecruitmentCatalog Read(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        if (root?.Name != "cards") throw new RuleViolation("legacy.card_definition", "Unexpected card definition root");
        var cards = new Dictionary<int, LegacyCardDefinition>();
        foreach (var row in root.Elements("card"))
        {
            int Integer(string key, int fallback = 0) => (int?)row.Attribute(key) ?? fallback;
            string Text(string key) => (string?)row.Attribute(key) ?? "";
            int id = Integer("id", -1), grade = Integer("grade"), cooldown = Integer("round"), construction = Integer("buildround");
            string type = Text("type");
            int unit = type is "recruit" or "fortress" ? LegacyRuntimeCatalog.UnitIndex(Text("army")) : -1;
            var cost = new Resources(Integer("price"), Integer("industry"), 0); cost.Validate();
            if (id < 0 || string.IsNullOrWhiteSpace(Text("name")) || type is not ("recruit" or "fortress" or "installation" or "special")
                || grade < 0 || grade > 2 || cooldown < 0 || construction < 0
                || !cards.TryAdd(id, new(id, Text("name"), type, unit, grade, cost, cooldown, construction, Text("image"), Text("intro"))))
                throw new RuleViolation("legacy.card_definition", "Invalid or duplicate card definition");
        }
        return new(cards, xml);
    }

    public IReadOnlyDictionary<string, RecruitmentOffer> Offers(string variant, LegacyRuntimeCatalog runtime, LegacyBuildingCatalog buildings)
    {
        var offers = new Dictionary<string, RecruitmentOffer>(StringComparer.Ordinal);
        foreach (var card in Cards.Values.Where(c => c.Recruits))
        {
            // Cards are global; a country's absent unit slot cannot produce an offer.
            if (!runtime.HasUnit(variant, card.Unit, card.Grade)) continue;
            var unit = runtime.Unit(variant, card.Unit, card.Grade);
            bool fort = card.Type == "fortress";
            var levels = buildings.Definitions.Values.Select(b => (b.Kind, Levels: b.Levels
                .Where(l => l.Recruitment.TryGetValue(card.Unit, out int maximum) && card.Grade <= maximum)
                .Select(l => l.Turn.Level).ToArray())).Where(b => b.Levels.Length > 0)
                .ToDictionary(b => b.Kind, b => (IReadOnlyCollection<int>)Array.AsReadOnly(b.Levels), StringComparer.Ordinal);
            var offer = new RecruitmentOffer { Id = card.Id, Template = new() { Profile = unit.Profile, Health = unit.Profile.MaxHealth },
                Cost = card.Cost, Support = new() { FoodConsumption = unit.FoodConsumption },
                Building = fort ? BuildingRequirement.Forbidden : BuildingRequirement.Required,
                AllowedBuildingLevels = fort ? null : new ReadOnlyDictionary<string, IReadOnlyCollection<int>>(levels),
                AllowSea = !fort, TurnGroup = fort ? "ew4.fortress" : card.Id, PerTurnLimit = fort ? 1 : null,
                ConstructionTurns = fort ? card.ConstructionTurns : 0 };
            offer.Validate(); offers.Add(offer.Id, offer);
        }
        return new ReadOnlyDictionary<string, RecruitmentOffer>(offers);
    }

    private LegacyCardDefinition RequiredCard(int id, string type) => Cards.TryGetValue(id, out var card) && card.Type == type
        ? card : throw new RuleViolation("legacy.card_reference", "Required original action card is absent or has another type");

    public IReadOnlyDictionary<string, InstallationBuildRules> Installations(LegacySkillParameters? skillParameters = null)
    {
        var result = new Dictionary<string, InstallationBuildRules>(StringComparer.Ordinal);
        int[] skills = [27, 28, 25];
        foreach (var kind in InstallationKinds)
        {
            var card = RequiredCard(41 + kind.Key, "installation");
            result.Add(card.Id, new() { Kind = kind.Value, Purchase = new() { AllowSea = false, Cost = card.Cost, DiscountSkill = Ew4Combat.Skill(skills[kind.Key]),
                MoneyPercentWithSkill = (skillParameters ?? LegacySkillParameters.Default).Percent(skills[kind.Key]) } });
        }
        return new ReadOnlyDictionary<string, InstallationBuildRules>(result);
    }

    public TransportPurchaseRules Transport(LegacySkillParameters? skillParameters = null) => new() { Purchase = new()
        { Cost = RequiredCard(46, "special").Cost, DiscountSkill = Ew4Combat.Skill(26),
            MoneyPercentWithSkill = (skillParameters ?? LegacySkillParameters.Default).Percent(26) } };
}
