using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class ItemQuantityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Item quantity: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data",
            path.StartsWith("data/", StringComparison.Ordinal) ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var trading = content.Catalogs.Items.Rules();
        Check(trading.Definitions.Values.All(d => !d.Unique && d.MaximumOwned == null), "all imported item IDs allow multiple copies");
        Check(content.ItemDisplayIds.Count == 228 && content.ItemDisplayIds.Values.Distinct().Count() == 54,
            "all 228 legacy IDs remain resolvable through 54 display groups");
        Check(content.ItemDisplayIds["ew4.item.10001"] == "ew4.item.0", "legacy war horse copies share a display group");
        string example = Path.Combine(root, "rewrite/mod-kit/examples/item-effects");
        var modFiles = Directory.EnumerateFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(example, p).Replace('\\', '/'), File.ReadAllBytes);
        var mod = new ClientContent(path => modFiles.TryGetValue(path, out var bytes) ? bytes : Read(path), ["conquest1"]);
        Check(mod.ItemDisplayIds["ew4.item.8"] != mod.ItemDisplayIds["ew4.item.10801"], "Mod effect override keeps its item separate from inherited aliases");
        var changedItems = JsonNode.Parse(Read("data/items.json"))!;
        var horseCopies = changedItems["items"]!.AsArray();
        horseCopies.Single(i => (int)i!["id"]! == 10001)!["price"] = 999;
        horseCopies.Single(i => (int)i!["id"]! == 10002)!["value"] = 2;
        horseCopies.Single(i => (int)i!["id"]! == 10003)!["name"] = "Mod Horse";
        byte[] changedBytes = JsonSerializer.SerializeToUtf8Bytes(changedItems);
        var changed = new ClientContent(path => path == "data/items.json" ? changedBytes : Read(path), ["conquest1"]);
        Check(new[] { "ew4.item.10001", "ew4.item.10002", "ew4.item.10003" }.All(id => changed.ItemDisplayIds[id] == id),
            "Mod price, value and resource-name overrides stay distinct");
        var custom = LegacyItemTradingCatalog.Read("<items><item id='90001' price='1' name='custom' function='0' value='0' /></items>");
        Check(!custom.Items[90001].Unique, "new Mod item IDs use the same unlimited ownership rule");

        string folder = Path.Combine(root, "build/item-quantity-20261118/qa/run-" + Guid.NewGuid().ToString("N"));
        string profile = Path.Combine(folder, "profile");
        Directory.CreateDirectory(profile);
        var seed = new PlayerAccount { Id = "local", ContentRevision = content.Catalogs.Accounts.SourceRevision, Medals = 100_000_000,
            Roster = [content.Catalogs.Accounts.FromDefinition(1, "legacy.horse") with { Items = ["ew4.item.10001"] }],
            Inventory = trading.Definitions.Values.Where(d => !d.Equipment.Consumable).ToDictionary(d => d.Equipment.Id, _ => 6L) };
        File.WriteAllBytes(Path.Combine(profile, "account.json"), JsonSerializer.SerializeToUtf8Bytes(seed, ClientGame.Json));
        var host = new ClientGame(content, profile);
        JsonElement Call(string route, object? command = null, string? error = null)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command ?? new { }, ClientGame.Json)));
            var result = document.RootElement;
            Check(result.GetProperty("ok").GetBoolean() == (error == null), route + ": " + result.GetRawText()[..Math.Min(180, result.GetRawText().Length)]);
            if (error != null) Check(result.GetProperty("code").GetString() == error, error);
            return result.GetProperty(error == null ? "data" : "code").Clone();
        }
        long Revision(JsonElement view) => view.GetProperty("revision").GetInt64();
        var shop = Call("commerce");
        Check(shop.GetProperty("offers").GetArrayLength() == 7, "owning six of every equipment ID still fills the shop");
        Check(shop.GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("slot").GetInt32() >= 7)
            .All(o => { string id = o.GetProperty("itemId").GetString()!; return content.ItemDisplayIds[id] == id; }),
            "default shop draws canonical items instead of redundant ID copies");
        var legacyHorse = shop.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == "ew4.item.10001");
        Check(legacyHorse.GetProperty("image").GetString() == "War Horse.png" && legacyHorse.GetProperty("available").GetInt64() == 5,
            "legacy alias inherits original resources and keeps its equipped copy reserved");
        var offer = shop.GetProperty("offers").EnumerateArray().First(o => o.GetProperty("slot").GetInt32() >= 7);
        string offerId = offer.GetProperty("id").GetString()!, itemId = offer.GetProperty("itemId").GetString()!;
        long price = offer.GetProperty("price").GetInt64();
        for (int i = 0; i < 12; i++) shop = Call("commerce", new { op = "buy", revision = Revision(shop), offer = offerId });
        JsonElement Item(JsonElement view) => view.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == itemId);
        Check(Item(shop).GetProperty("quantity").GetInt64() == 18 && Item(shop).GetProperty("available").GetInt64() == 18,
            "twelve repeated purchases add to six existing copies");
        Check(shop.GetProperty("medals").GetInt64() == seed.Medals - 12 * price, "each repeated purchase pays the exact price");
        Call("commerce", new { op = "buy", revision = Revision(shop) - 1, offer = offerId }, "account.revision");
        host = new ClientGame(content, profile);
        Check(Call("commerce").GetRawText() == shop.GetRawText(), "account quantity and unlimited equipment stock survive restart");
        Check(Call("headquarters").GetProperty("roster")[0].GetProperty("items")[0].GetString() == "ew4.item.10001",
            "legacy equipped alias survives reload without being rewritten");
        shop = Call("commerce", new { op = "sell", revision = Revision(shop), item = itemId });
        Check(Item(shop).GetProperty("quantity").GetInt64() == 17, "selling removes one copy");
        shop = Call("commerce", new { op = "refresh", revision = Revision(shop) });
        Check(shop.GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("slot").GetInt32() >= 7)
            .All(o => o.GetProperty("available").GetBoolean()), "already owned equipment remains available after refresh");

        var account = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(profile, "account.json")), ClientGame.Json)!;
        account = account with { Roster = account.Roster.Concat(Enumerable.Range(0, 6)
            .Select(i => content.Catalogs.Accounts.FromDefinition(1, "quantity." + i) with { Items = [] })).ToArray() };
        foreach (var commander in account.Roster.Where(c => c.Id.StartsWith("quantity.", StringComparison.Ordinal)).ToArray())
            account = AccountCommanders.Equip(account, account.Revision, commander.Id, itemId, content.Catalogs.Accounts.Rules());
        Check(account.Roster.Count(c => c.Items.Contains(itemId)) == 6 && account.Inventory[itemId] == 17, "six commanders can equip independent copies without reducing total ownership");
        var market = new ItemMarket("quantity", [new("copy", itemId)]);
        for (int i = 0; i < 11; i++) account = ItemTrading.Sell(account, account.Revision, market, itemId, 0, [], trading).Account;
        Check(account.Inventory[itemId] == 6, "only the eleven spare copies can be sold");
        try { ItemTrading.Sell(account, account.Revision, market, itemId, 0, [], trading); throw new InvalidOperationException("Sold equipped copy"); }
        catch (RuleViolation error) { Check(error.Code == "trading.unavailable", "all equipped copies remain protected"); }

        string scenario = content.Scenarios.First(s => s.Mode == 1 && content.Battle(s.Id).Markets
            .Any(m => m.Slots.Any(slot => slot.ItemId >= 0 && !content.Catalogs.Items.Items[slot.ItemId].Equipment.Consumable))).Id;
        var start = content.Start(scenario, null, account);
        var session = start.Session;
        var service = session.Markets.First(m => m.Market.Offers.Any(o => !trading.Definitions[o.ItemId].Equipment.Consumable));
        var battleOffer = service.Market.Offers.First(o => !trading.Definitions[o.ItemId].Equipment.Consumable);
        session = session with { World = session.World with { ActiveFaction = session.PlayerFaction, Phase = TurnPhase.Active,
            PendingDialogue = null, QueuedDialogues = [], Cells = session.World.Cells.Select((c, i) => i == service.Cell ? c with { Owner = session.PlayerFaction } : c).ToArray() } };
        long before = session.Account.Inventory[battleOffer.ItemId];
        for (int i = 0; i < 6; i++) session = SessionActions.BuyItem(session, session.Revision, service.Market.Id, battleOffer.Id, start.Rules.Trading).State;
        Check(session.Account.Inventory[battleOffer.ItemId] == before + 6, "battle shop also sells more than four copies of one ID");
        var restored = SessionSave.Decode(SessionSave.Encode(session), new(session.ContentRevision, session.World.ContentRevision, session.Account.ContentRevision));
        Check(restored.Account.Inventory[battleOffer.ItemId] == before + 6 && restored.Account.Roster.Count(c => c.Items.Contains(itemId)) == 6
            && restored.Account.Roster.Single(c => c.Id == "legacy.horse").Items.Single() == "ew4.item.10001",
            "battle save preserves large quantities and shared equipment IDs across commanders");
        checks += ShopRefreshChecks.Run(content, folder);
        return checks;
    }
}
