using System.Globalization;
using System.Text.Json;
using Ew4.Core;
using Ew4.Persistence;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private const int CommanderSectionCapacity = 20;
    private PlayerAccount? localAccount;
    private string Profile => Path.Combine(saveDirectory, "account.json");
    private PlayerAccount Account()
    {
        if (localAccount != null) return localAccount;
        if (File.Exists(Profile))
        {
            if (new FileInfo(Profile).Length > 16 * 1024 * 1024) throw new RuleViolation("account.size", "Account exceeds read budget");
            localAccount = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Profile), Json)
                ?? throw new RuleViolation("account.format", "Invalid local account");
        }
        else localAccount = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision,
            Medals = content.Extensions.Metagame.InitialMedals, Badges = content.Extensions.Metagame.InitialBadges };
        if (localAccount.CommanderOffers.Count == 0)
            for (int group = 0; group < 3; group++) localAccount = DrawCommanders(localAccount, group);
        localAccount.Validate();
        if (localAccount.ContentRevision != content.Catalogs.Accounts.SourceRevision)
            throw new RuleViolation("account.content", "Local account requires a content migration");
        foreach (var owned in localAccount.Roster) AccountCommanders.Resolve(owned, content.Catalogs.Accounts.Rules());
        var balances = localAccount.RefreshBalances.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (key, policy) in content.Extensions.Metagame.Refresh.Where(p => p.Value.Limit.HasValue))
            if (!balances.ContainsKey(key)) balances[key] = new(policy.Initial ?? policy.Limit!.Value, Now(), RefreshUses.Victories(localAccount));
        if (balances.Count != localAccount.RefreshBalances.Count)
            StoreAccount(localAccount with { Revision = checked(localAccount.Revision + 1), RefreshBalances = balances });
        return localAccount;
    }
    private DateTimeOffset Now() => clock?.Invoke() ?? DateTimeOffset.UtcNow;
    private RefreshPolicy? RefreshPolicy(string target) => content.Extensions.Metagame.Refresh.GetValueOrDefault(target);
    private object RefreshView(PlayerAccount account, string target)
    {
        var policy = RefreshPolicy(target);
        int? remaining = policy?.Limit == null ? null : RefreshUses.Recover(account.RefreshBalances[target], policy, Now(), RefreshUses.Victories(account)).Remaining;
        return new { limit = policy?.Limit, remaining, recovery = policy?.Recovery };
    }
    private void StoreAccount(PlayerAccount next)
    {
        next.Validate();
        if (localAccount != null && next.Revision == localAccount.Revision && File.Exists(Profile)) return;
        Directory.CreateDirectory(saveDirectory);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
        string temporary = Profile + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None)) { stream.Write(bytes); stream.Flush(true); }
        if (File.Exists(Profile)) File.Replace(temporary, Profile, Profile + ".bak"); else File.Move(temporary, Profile);
        localAccount = next;
    }
    private HashSet<string> LockedCommanders() => game == null ? [] : game.Session.RosterDeployments
        .SelectMany(q => q.CommanderIds).Concat(game.Session.World.Commanders.Select(c => c.Id)).ToHashSet(StringComparer.Ordinal);
    private object Headquarters()
    {
        var account = Account(); var locked = LockedCommanders();
        var sources = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children");
        return new { account.Revision, account.Medals, account.Badges, sectionCapacity = CommanderSectionCapacity,
            refresh = Enumerable.Range(0, 3).ToDictionary(i => i, i => RefreshView(account, "academy." + i)),
            skillLayout = content.Extensions.Layouts.GetValueOrDefault("commander"), offers = account.CommanderOffers, roster = account.Roster.Select(c => new { c.Id, c.DefinitionId, c.Learning, c.Training, c.Items, c.Tag,
            locked = locked.Contains(c.Id), rankPrice = PromotionPrice(c, false), titlePrice = PromotionPrice(c, true), fullPrice = FullPromotionPrice(c),
            healthBonus = content.Catalogs.Combat.RankHealth[c.Learning.Rank.Level.ToString(CultureInfo.InvariantCulture)],
            healing = content.Catalogs.Combat.TitleHealing[c.Learning.Title.Level.ToString(CultureInfo.InvariantCulture)] }),
            rankHealth = content.Catalogs.Combat.RankHealth, titleHealing = content.Catalogs.Combat.TitleHealing,
            progression = new { rank = content.Catalogs.Combat.RankProgression.Levels.Select(l => l.ExperienceToNext),
                title = content.Catalogs.Combat.TitleProgression.Levels.Select(l => l.ExperienceToNext) },
            definitions = sources.EnumerateArray().Select(e =>
            {
                var a = e.GetProperty("attributes");
                string Text(string key, string fallback = "") => a.TryGetProperty(key, out var v) ? v.GetString()! : fallback;
                int Number(string key) => int.Parse(Text(key, "0"), CultureInfo.InvariantCulture);
                int id = Number("id"); var source = content.Catalogs.Runtime.Commanders[id];
                return new { id = source.Definition.Id, legacyId = id, name = content.Label(Text("name")), image = Text("image", Text("name") + ".png"),
                    country = Text("country"), rating = Number("star"), rank = source.Rank, title = source.Title, source.Training,
                    price = Number("price"), badgePrice = BadgePrice(Number("star")), recruitable = Number("drawlots") != 0,
                    healthBonus = content.Catalogs.Combat.RankHealth[source.Rank.ToString(CultureInfo.InvariantCulture)],
                    healing = content.Catalogs.Combat.TitleHealing[source.Title.ToString(CultureInfo.InvariantCulture)],
                    skills = (source.Definition.SkillSlots.Count > 0 ? source.Definition.SkillSlots
                        : source.Definition.Skills.Select((s, i) => new SkillSlot(s, "legacy." + i)).ToArray()).OrderBy(s => s.Order).Select(SkillView),
                    abilities = content.Catalogs.Accounts.FromDefinition(id, "preview").Learning.Abilities };
            }),
            items = ItemViews(account), equipmentCapacity = content.Catalogs.Accounts.Rules().EquipmentCapacity,
            princesses = content.PrincessDefinitions.Select(p => new { id = "princess." + (string)p.Attribute("id")!,
                definitionId = "ew4.commander." + (string)p.Attribute("commanderid")!, image = (string)p.Attribute("name")! + ".png",
                unlocked = true, claimed = account.Roster.Any(c => c.DefinitionId == "ew4.commander." + (string)p.Attribute("commanderid")!) }) };
    }
    // Original 60030/195150: three offer pools, distinct from individual ability stars.
    private static readonly int[] BadgePrices = [0, 0, 0, 3, 3, 4, 6, 8];
    private static int BadgePrice(int rating) => rating > 0 && rating <= BadgePrices.Length ? BadgePrices[rating - 1] : 0;
    private int CommanderSection(string definitionId)
    {
        var source = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
            .Select(e => e.GetProperty("attributes")).Single(a => "ew4.commander." + a.GetProperty("id").GetString() == definitionId);
        return Math.Clamp((int.Parse(source.GetProperty("star").GetString()!, CultureInfo.InvariantCulture) - 1) / 3, 0, 2);
    }
    private void RequireCommanderSlot(PlayerAccount account, string definitionId)
    {
        int section = CommanderSection(definitionId);
        if (account.Roster.Count(c => CommanderSection(c.DefinitionId) == section) >= CommanderSectionCapacity)
            throw new RuleViolation("account.capacity", "该星区已满（20位将领）");
    }
    private PlayerAccount DrawCommanders(PlayerAccount account, int group)
    {
        if (group is < 0 or > 2) throw new RuleViolation("account.offer_group", "Unknown original recruitment section");
        int[] counts = [6, 4, 2];
        var excluded = account.Roster.Select(c => c.DefinitionId).Concat(account.CommanderOffers.Values.SelectMany(v => v)).ToHashSet(StringComparer.Ordinal);
        var pool = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
            .Select(e => e.GetProperty("attributes")).Where(a => a.GetProperty("drawlots").GetString() == "1"
                && int.Parse(a.GetProperty("star").GetString()!, CultureInfo.InvariantCulture) is int rating && rating >= group * 3 + 1 && rating <= group * 3 + 3)
            .Select(a => "ew4.commander." + a.GetProperty("id").GetString()).Where(id => !excluded.Contains(id)).ToArray();
        Random.Shared.Shuffle(pool);
        var offers = account.CommanderOffers.ToDictionary(p => p.Key, p => p.Value);
        offers[group] = pool.Take(counts[group]).ToArray();
        return account with { CommanderOffers = offers };
    }
    private long? PromotionPrice(OwnedCommander owned, bool title)
    {
        var track = title ? content.Catalogs.Combat.TitleProgression : content.Catalogs.Combat.RankProgression;
        var current = title ? owned.Learning.Title : owned.Learning.Rank;
        if (current.Level < 0 || current.Level >= track.Levels.Count) throw new RuleViolation("account.progression", "Unknown progression stage");
        if (track.Levels[current.Level].ExperienceToNext is not long threshold) return null;
        // Original 56444/56650: remaining XP times an IEEE single-precision medal rate.
        return (long)Math.Ceiling((float)Math.Max(0, threshold - current.Experience) * (title ? .2f : .008f));
    }
    private long FullPromotionPrice(OwnedCommander owned)
    {
        long Remaining(ProgressionState state, ProgressionRules track) => Math.Max(0,
            track.Levels.Skip(state.Level).Sum(level => level.ExperienceToNext ?? 0) - state.Experience);
        // Native 56754: sum remaining XP, round each track, cap at 3600 medals.
        return Math.Min(3600, (long)Math.Ceiling((float)Remaining(owned.Learning.Rank, content.Catalogs.Combat.RankProgression) * .008f)
            + (long)Math.Ceiling((float)Remaining(owned.Learning.Title, content.Catalogs.Combat.TitleProgression) * .2f));
    }
    private object CommanderAction(ClientCommand command)
    {
        var account = Account(); var rules = content.Catalogs.Accounts.Rules();
        if (command.Revision != account.Revision) throw new RuleViolation("account.revision", "Roster changed; refresh this command");
        PlayerAccount next;
        if (command.Op == "claim")
        {
            var reward = content.PrincessDefinitions.SingleOrDefault(p => "princess." + (string)p.Attribute("id")! == command.Offer)
                ?? throw new RuleViolation("account.reward", "Unknown princess reward");
            int id = (int)reward.Attribute("commanderid")!;
            if (account.Roster.Any(c => c.DefinitionId == "ew4.commander." + id))
                throw new RuleViolation("account.reward_claimed", "Princess already claimed");
            if (account.Roster.Count >= rules.MaximumRoster) throw new RuleViolation("account.capacity", "Roster is full");
            var owned = content.Catalogs.Accounts.FromDefinition(id, "local.commander." + id + "." + Guid.NewGuid().ToString("N"));
            RequireCommanderSlot(account, owned.DefinitionId);
            var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value);
            foreach (string item in owned.Items) inventory[item] = checked(inventory.GetValueOrDefault(item) + 1);
            next = account.Copy() with { Revision = checked(account.Revision + 1), Roster = account.Roster.Append(owned).ToArray(), Inventory = inventory,
                ClaimedRewards = account.ClaimedRewards.Append(command.Offer!).Distinct(StringComparer.Ordinal).ToArray() };
        }
        else if (command.Op == "preview_inherit")
        {
            var donor = account.Roster.Single(c => c.Id == command.Target);
            var receiver = account.Roster.Single(c => c.Id == command.Commander);
            var preview = AccountCommanders.Inherit(account, account.Revision, donor.Id, receiver.Id, LockedCommanders(), rules);
            return new { learning = preview.Roster.Single(c => c.Id == receiver.Id).Learning, price = rules.InheritanceMedals };
        }
        else if (command.Op == "refresh")
            next = DrawCommanders(RefreshUses.Consume(account, "academy." + command.Group, RefreshPolicy("academy." + command.Group), Now()), command.Group)
                with { Revision = checked(account.Revision + 1) };
        else if (command.Op is "recruit" or "recruit_badge")
        {
            var source = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
                .Select(e => e.GetProperty("attributes")).FirstOrDefault(a => "ew4.commander." + a.GetProperty("id").GetString() == command.Commander);
            bool badge = command.Op == "recruit_badge";
            long price = source.ValueKind == JsonValueKind.Undefined ? 0 : badge ? BadgePrice(int.Parse(source.GetProperty("star").GetString()!, CultureInfo.InvariantCulture))
                : source.TryGetProperty("price", out var rawPrice) && long.TryParse(rawPrice.GetString(), out long medals) ? medals : 0;
            if (source.ValueKind == JsonValueKind.Undefined || source.GetProperty("drawlots").GetString() != "1" || price <= 0
                || !account.CommanderOffers.TryGetValue(command.Group, out var offers) || !offers.Contains(command.Commander!)
                || account.Roster.Any(c => c.DefinitionId == command.Commander))
                throw new RuleViolation("account.recruit", "Commander has no available medal offer");
            if ((badge ? account.Badges : account.Medals) < price) throw new RuleViolation("account.medals", "Insufficient recruitment currency");
            int legacyId = int.Parse(source.GetProperty("id").GetString()!, CultureInfo.InvariantCulture);
            var owned = content.Catalogs.Accounts.FromDefinition(legacyId, "local.commander." + legacyId);
            RequireCommanderSlot(account, owned.DefinitionId);
            var inventory = account.Inventory.ToDictionary(p => p.Key, p => p.Value);
            foreach (string item in owned.Items) inventory[item] = checked(inventory.GetValueOrDefault(item) + 1);
            next = account with { Revision = checked(account.Revision + 1), Medals = account.Medals - (badge ? 0 : price), Badges = account.Badges - (badge ? price : 0),
                Roster = account.Roster.Append(owned).ToArray(), Inventory = inventory,
                CommanderOffers = account.CommanderOffers.ToDictionary(p => p.Key, p => p.Key == command.Group ? (IReadOnlyList<string>)p.Value.Where(id => id != command.Commander).ToArray() : p.Value) };
        }
        else if (command.Op == "tags")
        {
            var ids = command.TaggedCommanders ?? throw new RuleViolation("account.tag", "Missing commander tag selection");
            var tagged = ids.ToHashSet(StringComparer.Ordinal);
            if (tagged.Count != ids.Count || tagged.Any(id => !account.Roster.Any(c => c.Id == id)))
                throw new RuleViolation("account.tag", "Tag selection must contain distinct owned commanders");
            next = account.Copy() with { Revision = checked(account.Revision + 1),
                Roster = account.Roster.Select(c => c with { Tag = tagged.Contains(c.Id) ? string.IsNullOrEmpty(c.Tag) ? "置顶" : c.Tag : "" }).ToArray() };
        }
        else if (command.Op == "tag")
        {
            var owned = account.Roster.SingleOrDefault(c => c.Id == command.Commander)
                ?? throw new RuleViolation("account.commander", "Commander is not owned");
            string tag = (command.Tag ?? "").Trim();
            if (tag.Length > 12 || tag.Any(char.IsControl))
                throw new RuleViolation("account.tag", "标签最多12个字，不能含换行或控制字符");
            next = account.Copy() with { Revision = checked(account.Revision + 1),
                Roster = account.Roster.Select(c => c.Id == owned.Id ? c with { Tag = tag } : c).ToArray() };
        }
        else
        {
            var owned = account.Roster.SingleOrDefault(c => c.Id == command.Commander)
                ?? throw new RuleViolation("account.commander", "Commander is not owned");
            if (LockedCommanders().Contains(owned.Id)) throw new RuleViolation("account.deployed", "Commander is deployed in the current game");
            if (command.Op == "full")
            {
                long price = FullPromotionPrice(owned);
                if (price > account.Medals) throw new RuleViolation("account.medals", "Insufficient medals");
                var learning = owned.Learning with { Rank = new(content.Catalogs.Combat.RankProgression.Levels.Count - 1, 0),
                    Title = new(content.Catalogs.Combat.TitleProgression.Levels.Count - 1, 0) };
                next = account.Copy() with { Revision = checked(account.Revision + 1), Medals = account.Medals - price,
                    Roster = account.Roster.Select(c => c.Id == owned.Id ? c with { Learning = learning } : c).ToArray() };
            }
            else if (command.Op is "rank" or "title")
            {
                bool title = command.Op == "title";
                long price = PromotionPrice(owned, title) ?? throw new RuleViolation("account.cap", "Commander already reached the configured final stage");
                if (price > account.Medals) throw new RuleViolation("account.medals", "Insufficient medals");
                var current = title ? owned.Learning.Title : owned.Learning.Rank;
                var promoted = new ProgressionState(current.Level + 1, 0);
                var updated = owned with { Learning = title ? owned.Learning with { Title = promoted } : owned.Learning with { Rank = promoted } };
                next = account.Copy() with { Revision = checked(account.Revision + 1), Medals = account.Medals - price,
                    Roster = account.Roster.Select(c => c.Id == owned.Id ? updated : c).ToArray() };
            }
            else next = command.Op switch {
                "equip" => AccountCommanders.Equip(account, account.Revision, owned.Id, command.Item!, rules),
                "unequip" => AccountCommanders.Unequip(account, account.Revision, owned.Id, command.Item!, rules),
                "inherit" => AccountCommanders.Inherit(account, account.Revision, command.Target!, owned.Id, LockedCommanders(), rules),
                _ => throw new RuleViolation("account.command", "Unknown commander command") };
        }
        StoreAccount(next);
        if (game != null) game = game with { Session = game.Session with { Account = next, Revision = checked(game.Session.Revision + 1) } };
        return Headquarters();
    }
}
