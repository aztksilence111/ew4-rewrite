using System.Text.Json;
using Ew4.Client;
using Ew4.Core;

internal static class ShopRefreshChecks
{
    public static int Run(ClientContent content, string folder)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Shop refresh: " + label); }
        string saves = Path.Combine(folder, "shop-refresh");
        var host = new ClientGame(content, saves);
        JsonElement Call(string route, object? command = null, string? error = null)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command ?? new { }, ClientGame.Json)));
            var result = document.RootElement;
            Check(result.GetProperty("ok").GetBoolean() == (error == null), route + ": " + result.GetRawText()[..Math.Min(180, result.GetRawText().Length)]);
            if (error != null) Check(result.GetProperty("code").GetString() == error, "expected " + error);
            return result.GetProperty(error == null ? "data" : "code").Clone();
        }
        long Revision(JsonElement data) => data.GetProperty("revision").GetInt64();
        var shop = Call("commerce");
        var old = shop;
        var account = Call("headquarters");
        shop = Call("commerce", new { op = "refresh", revision = Revision(shop) });
        Check(Revision(shop) == Revision(old) + 1, "refresh advances account once");
        Check(shop.GetProperty("medals").GetInt64() == old.GetProperty("medals").GetInt64(), "refresh is free");
        Check(shop.GetProperty("items").GetRawText() == old.GetProperty("items").GetRawText(), "refresh preserves owned inventory");
        Check(shop.GetProperty("offers").GetArrayLength() == 7, "five consumables and two equipment offers");
        Check(shop.GetProperty("offers").GetRawText() != old.GetProperty("offers").GetRawText(), "refresh changes offered types");
        Check(shop.GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("slot").GetInt32() < 5)
            .All(o => o.GetProperty("remaining").GetInt64() == 1), "consumable stock resets to one");
        Check(shop.GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("slot").GetInt32() >= 7)
            .Select(o => o.GetProperty("itemId").GetString()).Distinct().Count() == 2, "equipment offers are distinct");
        Call("commerce", new { op = "refresh", revision = Revision(old) }, "account.revision");
        Call("commerce", new { op = "buy", revision = Revision(old), offer = old.GetProperty("offers")[0].GetProperty("id").GetString() }, "account.revision");
        Check(Call("commerce").GetRawText() == shop.GetRawText(), "rejected stale commands do not reroll or charge");
        var after = Call("headquarters");
        foreach (string field in new[] { "medals", "badges", "roster", "offers", "princesses" })
            Check(account.GetProperty(field).GetRawText() == after.GetProperty(field).GetRawText(), "preserves account " + field);
        host = new ClientGame(content, saves);
        Check(Call("commerce").GetRawText() == shop.GetRawText(), "refreshed stock survives restart");
        var equipment = shop.GetProperty("offers").EnumerateArray().First(o => o.GetProperty("slot").GetInt32() >= 7);
        string purchased = equipment.GetProperty("itemId").GetString()!;
        shop = Call("commerce", new { op = "buy", revision = Revision(shop), offer = equipment.GetProperty("id").GetString() });
        shop = Call("commerce", new { op = "refresh", revision = Revision(shop) });
        Check(shop.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == purchased)
            .GetProperty("quantity").GetInt64() == 1, "refresh preserves purchased equipment");
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        var beforeWorld = view;
        shop = Call("commerce", new { op = "refresh", revision = Revision(shop) });
        view = Call("action", new { op = "state" });
        foreach (string field in new[] { "round", "activeFaction", "phase", "outcome", "factions", "units", "cells", "battleResult" })
            Check(view.GetProperty(field).GetRawText() == beforeWorld.GetProperty(field).GetRawText(), "refresh preserves world " + field);
        Call("commerce", new { cell = 0, op = "refresh", revision = Revision(view) }, "trading.command");
        Check(Call("action", new { op = "state" }).GetRawText() == view.GetRawText(), "map refresh rejection changes no world state");

        // Owning every permanent item must still leave equipment available for purchase.
        string profilePath = Path.Combine(saves, "account.json");
        var profile = JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(profilePath), ClientGame.Json)!;
        var inventory = profile.Inventory.ToDictionary(p => p.Key, p => p.Value);
        foreach (var definition in content.Catalogs.Items.Items.Values.Where(d => !d.Equipment.Consumable)) inventory[definition.Equipment.Id] = 1;
        profile = profile with { Revision = profile.Revision + 1, Inventory = inventory };
        File.WriteAllBytes(profilePath, JsonSerializer.SerializeToUtf8Bytes(profile, ClientGame.Json));
        host = new ClientGame(content, saves);
        shop = Call("commerce", new { op = "refresh", revision = profile.Revision });
        Check(shop.GetProperty("offers").GetArrayLength() == 7, "fully owned equipment pool still fills both equipment slots");
        Check(shop.GetProperty("offers").EnumerateArray().Where(o => o.GetProperty("slot").GetInt32() >= 7)
            .All(o => o.GetProperty("available").GetBoolean()), "owned equipment remains purchasable after refresh");
        return checks;
    }
}
