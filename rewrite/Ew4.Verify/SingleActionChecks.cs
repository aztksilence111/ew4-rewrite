using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class SingleActionChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Single action: " + label); }
        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal)
            ? Path.Combine(assets, path[9..]) : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        var start = content.Start("conquest1", "fra");
        var rules = start.Rules;
        var original = start.Session.World;
        var player = start.Session.PlayerFaction;
        var target = original.Units.First(u => u.Owner == player && u.CommanderId != null
            && !rules.Transport.ExcludedCategories.Contains(u.Combatant.Profile.Category)
            && original.Commanders.Single(c => c.Id == u.CommanderId).MaximumTrainingLevel > 0);
        var step = rules.Upgrades.Steps.First(s => s.Kind == "city");
        target = target with { Transport = false, Acted = false, RemainingMovement = 1, Experience = 0,
            Veterancy = rules.Training.Stages[0].Id, Morale = MoraleState.Set(0, false),
            Combatant = target.Combatant with { ConstructionTurns = 0, Morale = 0,
                Skills = target.Combatant.Skills.Where(s => s != rules.Upgrades.DiscountSkill && s != rules.Transport.Purchase.DiscountSkill).ToArray() } };
        var world = original with {
            Units = original.Units.Select(u => u.Id == target.Id ? target : u).ToArray(),
            Cells = original.Cells.Select((c, i) => i == target.Cell ? c with { Owner = player, BuildingKind = step.Kind,
                BuildingLevel = step.FromLevel, BuildingUpgradesThisTurn = 0 } : c).ToArray(),
            Factions = original.Factions.Select(f => f.Id == player ? f with { Resources = new(1000000, 1000000, 1000000) } : f).ToArray() };
        world.Validate();
        var initial = start.Session with { World = world };
        string folder = Path.Combine(root, "rewrite/reports/single-actions-20261021");
        Directory.CreateDirectory(folder);
        ClientGame Host(string name, GameSession session)
        {
            string directory = Path.Combine(folder, "clients", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var slot = new ClientSlot("conquest1", "fra", SessionSave.Encode(session));
            File.WriteAllBytes(Path.Combine(directory, "session.json"), JsonSerializer.SerializeToUtf8Bytes(slot, ClientGame.Json));
            File.WriteAllBytes(Path.Combine(directory, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
            File.WriteAllBytes(Path.Combine(folder, name + "-slot.json"), JsonSerializer.SerializeToUtf8Bytes(slot, ClientGame.Json));
            var host = new ClientGame(content, directory);
            Call(host, "load", new { });
            return host;
        }
        JsonElement Call(ClientGame host, string route, object args, bool success = true)
        {
            using var parsed = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(args, ClientGame.Json)));
            Check(parsed.RootElement.GetProperty("ok").GetBoolean() == success, route + " returns expected status: " +
                (parsed.RootElement.GetProperty("ok").GetBoolean() ? "ok" : parsed.RootElement.GetProperty("message").GetString()));
            return parsed.RootElement.GetProperty(success ? "data" : "code").Clone();
        }
        Resources Funds(WorldState state) => state.Factions.Single(f => f.Id == player).Resources;
        Resources Delta(WorldState before, WorldState after) => new(Funds(before).Money - Funds(after).Money,
            Funds(before).Industry - Funds(after).Industry, Funds(before).Food - Funds(after).Food);
        var coreQuotes = new Dictionary<string, Resources> {
            ["train"] = WorldImprovements.TrainingCost(world, target.Id, rules.Training),
            ["upgrade"] = WorldImprovements.BuildingUpgradeCost(world, target.Cell, rules.Upgrades),
            ["transport"] = WorldSupportActions.TransportCost(world, target.Id, rules.Transport) };
        Check(coreQuotes["train"].Money == 30 && coreQuotes["train"].Food == target.Support.FoodConsumption * 3
            && coreQuotes["train"].Industry == 0, "original training money and food");
        foreach (var (op, cost) in coreQuotes)
        {
            var host = Host("rich", initial);
            var selection = Call(host, "action", new { op = "select", cell = target.Cell, revision = initial.Revision });
            var quote = selection.GetProperty("actionQuotes").GetProperty(op);
            Check(quote.GetProperty("cost").Deserialize<Resources>(ClientGame.Json) == cost && quote.GetProperty("affordable").GetBoolean(), op + " client quote matches core");
            var result = Call(host, "action", new { op, cell = target.Cell, unit = target.Id, revision = initial.Revision });
            var funds = result.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == player)
                .GetProperty("resources").Deserialize<Resources>(ClientGame.Json)!;
            Check(Funds(world) == new Resources(funds.Money + cost.Money, funds.Industry + cost.Industry, funds.Food + cost.Food), op + " spends exactly the displayed quote");
            Check(Call(host, "action", new { op, cell = target.Cell, unit = target.Id, revision = initial.Revision }, false).GetString() == "client.stale", op + " duplicate confirmation rejected");
        }
        var poor = initial with { World = world with { Factions = world.Factions.Select(f => f.Id == player ? f with { Resources = new(0, 0, 0) } : f).ToArray() } };
        var poorHost = Host("poor", poor);
        var poorSelection = Call(poorHost, "action", new { op = "select", cell = target.Cell, revision = poor.Revision });
        foreach (var op in coreQuotes.Keys)
        {
            var quote = poorSelection.GetProperty("actionQuotes").GetProperty(op);
            Check(quote.GetProperty("cost").Deserialize<Resources>(ClientGame.Json) == coreQuotes[op]
                && !quote.GetProperty("affordable").GetBoolean(), op + " keeps real price when unaffordable");
            Call(poorHost, "action", new { op, cell = target.Cell, unit = target.Id, revision = poor.Revision }, false);
            Check(Call(poorHost, "action", new { op = "state" }).GetProperty("revision").GetInt64() == poor.Revision, op + " rejected purchase leaves revision unchanged");
        }
        var unavailable = initial with { World = world with {
            Units = world.Units.Select(u => u.Id == target.Id ? u with { Acted = true, RemainingMovement = 0, Transport = true } : u).ToArray(),
            Cells = world.Cells.Select((c, i) => i == target.Cell ? c with { BuildingUpgradesThisTurn = rules.Upgrades.PerTurnLimit ?? 1 } : c).ToArray() } };
        var unavailableHost = Host("unavailable", unavailable);
        var hidden = Call(unavailableHost, "action", new { op = "select", cell = target.Cell, revision = unavailable.Revision });
        Check(hidden.GetProperty("actionQuotes").EnumerateObject().All(p => p.Value.ValueKind == JsonValueKind.Null), "ineligible actions have no quote or entry");
        foreach (var group in rules.Upgrades.Steps.GroupBy(s => s.Kind))
        {
            var upgrade = group.First();
            var sample = world with { Cells = world.Cells.Select((c, i) => i == target.Cell
                ? c with { BuildingKind = upgrade.Kind, BuildingLevel = upgrade.FromLevel } : c).ToArray() };
            var price = WorldImprovements.BuildingUpgradeCost(sample, target.Cell, rules.Upgrades);
            Check(Delta(sample, WorldImprovements.UpgradeBuilding(sample, sample.Revision, target.Cell, rules.Upgrades).State) == price,
                "quote matches building kind " + upgrade.Kind);
        }
        var skills = target.Combatant.Skills.Append(rules.Upgrades.DiscountSkill)
            .Concat(rules.Transport.Purchase.DiscountSkill is string discount ? [discount] : Array.Empty<string>()).Distinct().ToArray();
        var discounted = initial with { World = world with { Units = world.Units.Select(u => u.Id == target.Id
            ? u with { Combatant = u.Combatant with { Skills = skills } } : u).ToArray() } };
        var discountedHost = Host("discount", discounted);
        var discountedSelection = Call(discountedHost, "action", new { op = "select", cell = target.Cell, revision = discounted.Revision });
        var upgradeDiscount = discountedSelection.GetProperty("actionQuotes").GetProperty("upgrade").GetProperty("cost").Deserialize<Resources>(ClientGame.Json)!;
        Check(upgradeDiscount.Money == coreQuotes["upgrade"].Money * rules.Upgrades.DiscountPercent / 100
            && upgradeDiscount.Industry == coreQuotes["upgrade"].Industry * rules.Upgrades.DiscountPercent / 100,
            "commander building discount is quoted exactly");
        Check(Delta(discounted.World, WorldImprovements.UpgradeBuilding(discounted.World, discounted.World.Revision,
            target.Cell, rules.Upgrades).State) == upgradeDiscount, "discounted upgrade spends displayed quote");
        var transportDiscount = discountedSelection.GetProperty("actionQuotes").GetProperty("transport").GetProperty("cost").Deserialize<Resources>(ClientGame.Json)!;
        Check(Delta(discounted.World, WorldSupportActions.BuyTransport(discounted.World, discounted.World.Revision,
            target.Id, rules.Transport).State) == transportDiscount, "discounted transport spends displayed quote");
        Check(transportDiscount.Money == coreQuotes["transport"].Money * rules.Transport.Purchase.MoneyPercentWithSkill / 100,
            "transport commander money discount is exact");
        var empty = initial with { World = world with { Units = world.Units.Where(u => u.Cell != target.Cell).ToArray() } };
        var emptyHost = Host("empty", empty);
        var emptySelection = Call(emptyHost, "action", new { op = "select", cell = target.Cell, revision = empty.Revision });
        Check(emptySelection.GetProperty("unit").ValueKind == JsonValueKind.Null
            && emptySelection.GetProperty("actionQuotes").GetProperty("upgrade").GetProperty("affordable").GetBoolean(),
            "empty owned building has an upgrade quote without a selected unit");
        Call(emptyHost, "action", new { op = "upgrade", cell = target.Cell, revision = empty.Revision });
        var capped = initial with { World = world with { Units = world.Units.Select(u => u.Id == target.Id ? u with {
            Veterancy = rules.Training.Stages[world.Commanders.Single(c => c.Id == u.CommanderId).MaximumTrainingLevel].Id } : u).ToArray() } };
        var cappedHost = Host("capped", capped);
        var cappedSelection = Call(cappedHost, "action", new { op = "select", cell = target.Cell, revision = capped.Revision });
        Check(cappedSelection.GetProperty("actionQuotes").GetProperty("train").ValueKind == JsonValueKind.Null,
            "maximum training level is unavailable rather than free");
        var noCommander = initial with { World = world with { Units = world.Units.Select(u => u.Id == target.Id ? u with {
            CommanderId = null, CommanderBase = null, Combatant = target.CommanderBase?.Combatant ?? target.Combatant,
            Support = target.CommanderBase?.Support ?? target.Support } : u).ToArray() } };
        var disbandHost = Host("disband", noCommander);
        var disbandSelection = Call(disbandHost, "action", new { op = "select", cell = target.Cell, revision = noCommander.Revision });
        Check(disbandSelection.GetProperty("disband").ValueKind == JsonValueKind.Object, "disband exposes refund for unit without commander");
        Check(disbandSelection.GetProperty("actionQuotes").GetProperty("train").ValueKind == JsonValueKind.Null, "unit without commander has no training entry");
        Check(disbandSelection.GetProperty("disbandWarning").ValueKind == JsonValueKind.Null, "ordinary disband has no fort warning");
        var fortOffer = rules.Recruitment[player].Values.First(o => o.Template?.Profile.Category == "fort"
            && Enumerable.Range(0, world.Cells.Count).Any(c => WorldActions.CanRecruit(world, c, o)));
        int fortCell = Enumerable.Range(0, world.Cells.Count).First(c => WorldActions.CanRecruit(world, c, fortOffer));
        var fortWorld = WorldActions.Recruit(world, world.Revision, fortCell, "warning.fort", fortOffer).State;
        var fortSession = initial with { World = fortWorld };
        var fortHost = Host("fort-used", fortSession);
        var fortSelection = Call(fortHost, "action", new { op = "select", cell = fortCell, revision = fortSession.Revision });
        Check(fortSelection.GetProperty("disbandWarning").GetString() == "解散后本回合内不能再建造要塞。", "used fort allowance produces disband warning");
        var ordinaryUsed = noCommander with { World = noCommander.World with {
            Factions = fortWorld.Factions } };
        var ordinaryHost = Host("ordinary-fort-used", ordinaryUsed);
        Check(Call(ordinaryHost, "action", new { op = "select", cell = target.Cell, revision = ordinaryUsed.Revision })
            .GetProperty("disbandWarning").ValueKind == JsonValueKind.Null, "used fort allowance does not warn for ordinary unit");
        var oldFort = fortSession with { World = fortWorld with { Factions = fortWorld.Factions.Select(f => f.Id == player
            ? f with { TurnUses = new Dictionary<string, int>() } : f).ToArray() } };
        var oldFortHost = Host("fort-unused", oldFort);
        Check(Call(oldFortHost, "action", new { op = "select", cell = fortCell, revision = oldFort.Revision })
            .GetProperty("disbandWarning").ValueKind == JsonValueKind.Null, "unused fort allowance does not produce misleading warning");
        var builder = world.Units.First(u => u.Owner == player && u.Combatant.Profile.Category == "infantry");
        builder = builder with { CommanderId = target.CommanderId, RemainingMovement = 1, Acted = false,
            Combatant = builder.Combatant with { ConstructionTurns = 0, Skills = [] } };
        var defenseWorld = world with {
            Units = world.Units.Where(u => u.Id != builder.Id && u.Id != target.Id).Append(builder).ToArray(),
            Cells = world.Cells.Select((c, i) => i == builder.Cell ? c with { Owner = player, BuildingKind = null,
                BuildingLevel = 0, InstallationKind = null } : c).ToArray() };
        string defenseFolder = Path.Combine(root, "build/installation-fix-20261023");
        Directory.CreateDirectory(defenseFolder);
        ClientGame DefenseHost(string name, WorldState snapshot)
        {
            snapshot.Validate();
            var session = initial with { World = snapshot };
            var host = Host("defense-" + name, session);
            File.Copy(Path.Combine(folder, "defense-" + name + "-slot.json"), Path.Combine(defenseFolder, name + "-slot.json"), true);
            return host;
        }
        foreach (string mode in new[] { "rich", "poor", "discount" })
        {
            var sample = defenseWorld with {
                Factions = defenseWorld.Factions.Select(f => f.Id == player && mode == "poor" ? f with { Resources = new(0, 0, 0) } : f).ToArray(),
                Units = defenseWorld.Units.Select(u => u.Id == builder.Id && mode == "discount" ? u with {
                    Combatant = u.Combatant with { Skills = rules.Installations.Values.Select(r => r.Purchase.DiscountSkill!).ToArray() } } : u).ToArray() };
            var host = DefenseHost(mode, sample);
            var offers = Call(host, "action", new { op = "select", cell = builder.Cell, revision = initial.Revision }).GetProperty("installationOffers");
            Check(offers.GetArrayLength() == 3, mode + " retains all three defense offers");
            foreach (var offer in offers.EnumerateArray())
            {
                string id = offer.GetProperty("id").GetString()!;
                var rule = rules.Installations[id];
                var quote = offer.GetProperty("quote");
                var cost = quote.GetProperty("cost").Deserialize<Resources>(ClientGame.Json)!;
                Check(offer.GetProperty("kind").GetString() == rule.Kind && !string.IsNullOrWhiteSpace(offer.GetProperty("name").GetString()), "defense card ID has type and name");
                Check(cost == WorldSupportActions.InstallationCost(sample, builder.Id, rule), "defense quote matches core");
                Check(quote.GetProperty("affordable").GetBoolean() == (mode != "poor"), "defense affordability");
                if (mode == "discount") Check(cost.Money < rule.Purchase.Cost.Money, "defense commander discount");
                var fresh = DefenseHost(mode, sample);
                var result = Call(fresh, "action", new { op = "installation", unit = builder.Id, offer = id, revision = initial.Revision }, mode != "poor");
                if (mode == "poor")
                {
                    Check(result.GetString() == "purchase.resources", "poor defense purchase rejected");
                    Check(Call(fresh, "action", new { op = "state" }).GetProperty("revision").GetInt64() == initial.Revision, "poor defense purchase leaves state unchanged");
                    continue;
                }
                Check(result.GetProperty("cells")[builder.Cell].GetProperty("installationKind").GetString() == rule.Kind, "defense construction updates tile");
                var funds = result.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == player).GetProperty("resources").Deserialize<Resources>(ClientGame.Json)!;
                Check(Funds(sample) == new Resources(funds.Money + cost.Money, funds.Industry + cost.Industry, funds.Food + cost.Food), "defense spends displayed quote");
                Check(Call(fresh, "action", new { op = "installation", unit = builder.Id, offer = id, revision = initial.Revision }, false).GetString() == "client.stale", "duplicate defense confirmation rejected");
                Check(Call(fresh, "action", new { op = "select", cell = builder.Cell, revision = initial.Revision + 1 }).GetProperty("installationOffers").GetArrayLength() == 0, "existing defense hides construction offers");
            }
        }
        foreach (string mode in new[] { "building", "installed", "foreign", "cavalry" })
        {
            var sample = defenseWorld with {
                Cells = defenseWorld.Cells.Select((c, i) => i != builder.Cell ? c : c with {
                    BuildingKind = mode == "building" ? "city" : null, BuildingLevel = mode == "building" ? 1 : 0,
                    InstallationKind = mode == "installed" ? "bunker" : null,
                    Owner = mode == "foreign" ? original.Factions.First(f => f.Id != player).Id : player }).ToArray(),
                Units = defenseWorld.Units.Select(u => u.Id == builder.Id && mode == "cavalry" ? u with {
                    CommanderBase = null, Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Category = "cavalry" } } } : u).ToArray() };
            var host = DefenseHost(mode, sample);
            Check(Call(host, "action", new { op = "select", cell = builder.Cell, revision = initial.Revision }).GetProperty("installationOffers").GetArrayLength() == 0, mode + " prevents defense construction");
        }
        File.WriteAllText(Path.Combine(defenseFolder, "backend.json"), JsonSerializer.Serialize(new { builder.Id, builder.Cell, errors = 0 }, ClientGame.Json));
        File.WriteAllText(Path.Combine(folder, "backend.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            target = new { target.Id, target.Cell }, quotes = coreQuotes }, ClientGame.Json));
        return checks;
    }
}
