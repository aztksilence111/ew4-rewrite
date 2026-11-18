using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record OwnedCommander(string Id, string DefinitionId, CommanderLearningState Learning, int Training, IReadOnlyList<string> Items)
{
    public string Tag { get; init; } = "";
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(DefinitionId) || Training < 0
            || Learning.Rank.Level < 0 || Learning.Title.Level < 0 || Learning.Rank.Experience < 0 || Learning.Title.Experience < 0
            || Learning.Abilities.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
            || Items.Any(string.IsNullOrWhiteSpace) || Items.Distinct(StringComparer.Ordinal).Count() != Items.Count
            || Tag == null || Tag.Length > 12 || Tag.Any(char.IsControl))
            throw new RuleViolation("account.commander", "Invalid owned commander");
    }
    public OwnedCommander Copy() => this with { Learning = Learning with { Abilities = Learning.Abilities.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) }, Items = Items.ToArray() };
}

public sealed record AccountCommanderRules
{
    public IReadOnlyDictionary<string, Commander> Definitions { get; init; } = new Dictionary<string, Commander>();
    public IReadOnlyDictionary<string, Equipment> Items { get; init; } = new Dictionary<string, Equipment>();
    public int? MaximumRoster { get; init; }
    public int? EquipmentCapacity { get; init; } = 2;
    public long InheritanceMedals { get; init; }
    public CommanderInheritanceRules Inheritance { get; init; } = new();
    public void Validate()
    {
        if (MaximumRoster < 0 || EquipmentCapacity < 0 || InheritanceMedals < 0
            || Definitions.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key != p.Value.Id)
            || Items.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Key != p.Value.Id))
            throw new RuleViolation("account.commander_rules", "Invalid roster policies or definitions");
        new WorldCommanderRules { Definitions = Definitions }.Validate();
    }
}

public static class AccountCommanders
{
    public static Commander Resolve(OwnedCommander source, AccountCommanderRules rules)
    {
        source.Validate(); rules.Validate();
        if (!rules.Definitions.TryGetValue(source.DefinitionId, out var definition)
            || source.Items.Any(id => !rules.Items.ContainsKey(id)) || source.Items.Count > rules.EquipmentCapacity
            || !source.Learning.Abilities.TryGetValue("movement", out int movement)
            || definition.Stars.Keys.Any(id => !id.Contains('.') && !source.Learning.Abilities.ContainsKey(id)))
            throw new RuleViolation("account.definition", "Roster commander does not match its definition or equipment policy");
        var resolved = definition with { Id = source.Id, Movement = movement, Skills = definition.Skills.ToArray(),
            Stars = definition.Stars.Keys.ToDictionary(id => id, id => source.Learning.Abilities.GetValueOrDefault(id,definition.Stars[id]), StringComparer.Ordinal), Items = Array.Empty<Equipment>() };
        foreach (string item in source.Items) resolved = Ew4Commander.Equip(resolved, rules.Items[item], rules.EquipmentCapacity);
        return resolved;
    }
    private static void Ready(PlayerAccount account, long revision, AccountCommanderRules rules)
    {
        account.Validate(); rules.Validate();
        if (revision != account.Revision) throw new RuleViolation("account.revision", "Stale roster transaction");
        if (account.Roster.Count > rules.MaximumRoster) throw new RuleViolation("account.roster_capacity", "Roster exceeds configured capacity");
        foreach (var commander in account.Roster) Resolve(commander, rules);
    }
    public static PlayerAccount Inherit(PlayerAccount account, long expectedRevision, string donorId, string receiverId,
        IReadOnlyCollection<string> lockedCommanderIds, AccountCommanderRules rules)
    {
        Ready(account, expectedRevision, rules);
        var donor = account.Roster.SingleOrDefault(c => c.Id == donorId);
        var receiver = account.Roster.SingleOrDefault(c => c.Id == receiverId);
        if (donorId == receiverId || donor == null || receiver == null || lockedCommanderIds.Contains(donorId) || lockedCommanderIds.Contains(receiverId))
            throw new RuleViolation("account.inheritance", "Distinct, owned and unlocked commanders are required");
        if (account.Medals < rules.InheritanceMedals) throw new RuleViolation("account.medals", "Insufficient medals");
        var donorDefinition = Resolve(donor, rules);
        var learning = CommanderInheritance.Apply(receiver.Learning, donor.Learning,
            donorDefinition.Skills.Where(s => !donorDefinition.LegacySkillOverrides.Contains(s)).ToArray(), rules.Inheritance);
        var next = account with { Revision = checked(account.Revision + 1), Medals = account.Medals - rules.InheritanceMedals,
            Roster = account.Roster.Where(c => c.Id != donorId).Select(c => (c.Id == receiverId ? c with { Learning = learning } : c).Copy()).ToArray(),
            Inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal) };
        next.Validate();
        return next;
    }
    public static PlayerAccount Equip(PlayerAccount account, long expectedRevision, string commanderId, string itemId, AccountCommanderRules rules)
    {
        Ready(account, expectedRevision, rules);
        var commander = account.Roster.SingleOrDefault(c => c.Id == commanderId);
        if (commander == null || !rules.Items.TryGetValue(itemId, out var item)
            || !account.Inventory.TryGetValue(itemId, out long owned) || account.Roster.Sum(c => c.Items.LongCount(id => id == itemId)) >= owned)
            throw new RuleViolation("account.equipment", "No unassigned owned copy is available");
        Ew4Commander.Equip(Resolve(commander, rules), item, rules.EquipmentCapacity);
        return Replace(account, commander with { Items = commander.Items.Concat(new[] { itemId }).ToArray() });
    }
    public static PlayerAccount Unequip(PlayerAccount account, long expectedRevision, string commanderId, string itemId, AccountCommanderRules rules)
    {
        Ready(account, expectedRevision, rules);
        var commander = account.Roster.SingleOrDefault(c => c.Id == commanderId);
        if (commander == null || !commander.Items.Contains(itemId)) throw new RuleViolation("account.equipment", "Commander does not hold this item");
        return Replace(account, commander with { Items = commander.Items.Where(id => id != itemId).ToArray() });
    }
    private static PlayerAccount Replace(PlayerAccount account, OwnedCommander updated)
    {
        var next = account.Copy() with { Revision = checked(account.Revision + 1), Roster = account.Roster.Select(c => (c.Id == updated.Id ? updated : c).Copy()).ToArray() };
        next.Validate(); return next;
    }
}
