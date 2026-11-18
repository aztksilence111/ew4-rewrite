using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record OrderSummonOrigin(string Skill, string Identity);

public sealed partial class RuleEventBus
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, IReadOnlyDictionary<string, RecruitmentOffer>>> orderRecruitment = new(StringComparer.Ordinal);
    public void RegisterOrderRecruitment(string scenario, IReadOnlyDictionary<string, IReadOnlyDictionary<string, RecruitmentOffer>> offers)
        => orderRecruitment[scenario] = offers;
    internal IEnumerable<RecruitmentOffer> OrderRecruitment(WorldState w, string owner) => orderRecruitment.TryGetValue(w.ScenarioId, out var factions)
        && factions.TryGetValue(owner, out var offers) ? offers.Values : Array.Empty<RecruitmentOffer>();
}

public static partial class CommandOrders
{
    private static RecruitmentOffer[]? SummonOffers(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus bus)
    {
        var faction = w.Factions.Single(f => f.Id == source.Owner);
        var offers = bus.OrderRecruitment(w, source.Owner).Where(o => faction.Recruitment.ContainsKey(o.Id) && o.Template != null
            && o.Template.Profile.Grade is "0" or "" && (o.UnlockAny.Count == 0 || o.UnlockAny.Any(w.TriggeredScenarioEvents.Contains))).ToArray();
        var result = new List<RecruitmentOffer>();
        foreach (var profile in skill.Order!.Profiles)
        {
            var offer = offers.Where(o => o.Template!.Profile.Id == profile || o.Template.Profile.Traits.Contains(profile))
                .OrderBy(o => o.Id, StringComparer.Ordinal).FirstOrDefault();
            if (offer == null) return null;
            result.Add(offer);
        }
        return result.ToArray();
    }
    public static Resources? SummonCost(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus bus)
    {
        if (skill.Order?.Mode != "summon" || SummonOffers(w, source, skill, bus) is not { } offers) return null;
        long Price(Func<Resources, long> get) => checked((long)Math.Ceiling(offers.Sum(t => (decimal)get(t.Cost)) * (decimal)skill.Order.Price));
        return new(Price(c => c.Money), Price(c => c.Industry), Price(c => c.Food));
    }
    private static bool SummonAvailable(WorldState w, UnitState source, ExtensionSkill skill, RuleEventBus bus)
    {
        var r = skill.Order!; var money = w.Factions.Single(f => f.Id == source.Owner).Resources;
        if (SummonOffers(w, source, skill, bus) is not { } offers || SummonCost(w, source, skill, bus) is not { } cost
            || money.Money < cost.Money || money.Industry < cost.Industry || money.Food < cost.Food
            || r.SourceLimit > 0 && w.Units.Count(u => u.OrderOrigin == new OrderSummonOrigin(skill.Id, Identity(source))) + offers.Length > r.SourceLimit
            || !WorldArmyCapacity.CanAdd(w, source.Owner, w.Units.Count(u => u.Owner == source.Owner) + offers.Length - 1)) return false;
        var cells = SummonCells(w, source, skill);
        bool Assign(int index, HashSet<int> used)
        {
            if (index == offers.Length) return true;
            foreach (int c in cells)
                if (!used.Contains(c) && LegalDestination(w, source with { Combatant = offers[index].Template! }, c))
                { used.Add(c); if (Assign(index + 1, used)) return true; used.Remove(c); }
            return false;
        }
        return Assign(0, new());
    }
}
