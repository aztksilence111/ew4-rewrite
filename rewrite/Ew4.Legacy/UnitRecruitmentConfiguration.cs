using Ew4.Core;

namespace Ew4.Legacy;

// Profiles live in legacy/units.json; this additive registry bypasses the legacy 22-slot card table.
public sealed record UnitRecruitmentConfiguration(int Schema, ModBuildingRecruit[] Offers)
{
    public void Validate()
    {
        if (Schema != 1 || Offers == null || Offers.Length > 1024)
            throw new RuleViolation("unit_recruitment.configuration", "Expected schema 1 and at most 1024 offers");
        new ModConstructionRules { Recruitment = Offers }.Validate();
        foreach (var offer in Offers)
            if (!Id(offer.Id) || !Id(offer.Profile)
                || offer.Id.StartsWith("ew4.card.", StringComparison.Ordinal)
                || offer.MinimumLevel > 32 || offer.BuildingKinds!.Distinct(StringComparer.Ordinal).Count() != offer.BuildingKinds!.Count
                || offer.Countries.Distinct(StringComparer.Ordinal).Count() != offer.Countries.Count
                || offer.Scenarios.Distinct(StringComparer.Ordinal).Count() != offer.Scenarios.Count)
                throw new RuleViolation("unit_recruitment.configuration", "Invalid or duplicate offer ID, profile, level or scope");
    }
    private static bool Id(string id) => id.Length is > 0 and <= 128 && id.Contains('.')
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
}
