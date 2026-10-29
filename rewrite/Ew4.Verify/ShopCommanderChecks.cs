using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class ShopCommanderChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Shop commander: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        string scenario = content.Scenarios.First(s => s.Mode == 1 && content.Battle(s.Id).Markets.Count > 0
            && content.Battle(s.Id).Sites.Any(site => site.State6 == 1)).Id;
        var start = content.Start(scenario, null);
        var initial = start.Session;
        var world = initial.World;
        int shop = initial.Markets.First().Cell!.Value;
        int exchange = content.Battle(scenario).Sites.First(s => s.State6 == 1).Cell;
        var general = world.Units.First(u => u.Owner == initial.PlayerFaction && u.CommanderId != null && !world.Cells[u.Cell].Terrain.Sea);
        var plain = world.Units.First(u => u.Owner == initial.PlayerFaction && u.CommanderId == null && !world.Cells[u.Cell].Terrain.Sea);
        var owned = content.Catalogs.Accounts.FromDefinition(content.Catalogs.Runtime.Commanders.Keys.First(), "shop.owned") with { Items = [] };
        owned = owned with { Learning = owned.Learning with { Abilities = owned.Learning.Abilities.ToDictionary(p => p.Key, p => p.Key == "commerce" ? 8 : p.Value) } };
        var output = Path.Combine(root, "build/shop-commander-check-20261029");
        Directory.CreateDirectory(output);

        GameSession At(int cell, string kind)
        {
            var selected = kind == "scenario" ? general : plain;
            var units = world.Units.Select(u => u.Id == selected.Id ? u with { Cell = cell }
                : u.Cell == cell ? u with { Cell = selected.Cell } : u).ToArray();
            if (kind == "empty") units = units.Where(u => u.Cell != cell).ToArray();
            var state = initial with { World = world with { Units = units, Phase = TurnPhase.Active, PendingDialogue = null, QueuedDialogues = [],
                ActiveFaction = initial.PlayerFaction, Cells = world.Cells.Select((c, i) => i == cell ? c with { Owner = initial.PlayerFaction } : c).ToArray() } };
            if (kind == "owned")
            {
                state = state with { Account = state.Account with { Roster = [owned] } };
                state = RosterDeployment.Deploy(state, state.Revision, selected.Id, owned.Id, start.Rules.Roster).State;
            }
            state.Validate(); return state;
        }
        ClientGame Host(string name, GameSession session)
        {
            string profile = Path.Combine(output, name);
            Directory.CreateDirectory(Path.Combine(profile, "saves/mode-1"));
            File.WriteAllBytes(Path.Combine(profile, "account.json"), JsonSerializer.SerializeToUtf8Bytes(session.Account, ClientGame.Json));
            File.WriteAllBytes(Path.Combine(profile, "saves/mode-1/slot-0.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot(scenario, null, SessionSave.Encode(session)), ClientGame.Json));
            return new(content, profile);
        }
        JsonElement Call(ClientGame host, string route, object? command = null)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command ?? new { }, ClientGame.Json)));
            var result = response.RootElement;
            Check(result.GetProperty("ok").GetBoolean(), route + ": " + result.GetRawText()[..Math.Min(180, result.GetRawText().Length)]);
            return result.GetProperty("data").Clone();
        }
        foreach (var (route, cell) in new[] { ("commerce", shop), ("exchange", exchange) })
        foreach (string kind in new[] { "scenario", "owned", "plain", "empty" })
        {
            string name = route + "-" + kind;
            var state = At(cell, kind);
            var host = Host(name, state);
            var view = Call(host, "load", new { mode = 1 });
            var account = Call(host, "headquarters");
            var quote = Call(host, route, new { cell });
            File.WriteAllText(Path.Combine(output, name + ".json"), quote.GetRawText());
            bool hasCommander = kind is "scenario" or "owned";
            Check((quote.GetProperty("commanderView").ValueKind != JsonValueKind.Null) == hasCommander, name + " portrait availability");
            if (hasCommander)
            {
                var unit = state.World.Units.Single(u => u.Cell == cell);
                var detail = Call(host, "unit-info", new { unit = unit.Id, revision = view.GetProperty("revision").GetInt64() }).GetProperty("commander");
                Check(quote.GetProperty("commanderView").GetRawText() == detail.GetRawText(), name + " exact battle commander data");
                Check(quote.GetProperty("commander").GetString() == unit.CommanderId, name + " identity matches commerce contributor");
                if (kind == "owned") Check(quote.GetProperty("commerce").GetInt32() == 8, name + " uncapped commercial ability");
                if (kind == "scenario") Check(!account.GetProperty("roster").EnumerateArray().Any(c => c.GetProperty("id").GetString() == unit.CommanderId), name + " independent of account roster");
            }
            else Check(quote.GetProperty("commerce").GetInt32() == 0, name + " no phantom commerce bonus");
            Check(Call(host, "action", new { op = "state" }).GetRawText() == view.GetRawText(), name + " read preserves battle");
            Check(Call(host, "headquarters").GetRawText() == account.GetRawText(), name + " read preserves account");
            if (route == "commerce" && hasCommander)
            {
                string portrait = quote.GetProperty("commanderView").GetRawText();
                var offer = quote.GetProperty("offers").EnumerateArray().First(o => o.GetProperty("available").GetBoolean());
                var item = quote.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetString() == offer.GetProperty("itemId").GetString());
                long price = content.Catalogs.Items.Items.Values.Single(i => i.Equipment.Id == item.GetProperty("id").GetString()).Price;
                int level = quote.GetProperty("commerce").GetInt32();
                Check(offer.GetProperty("price").GetInt64() == ItemTrading.PurchasePrice(price, level, start.Rules.Trading.Items).Medals, name + " discounted quote");
                var bought = Call(host, route, new { cell, op = "buy", revision = quote.GetProperty("revision").GetInt64(), offer = offer.GetProperty("id").GetString() });
                Check(bought.GetProperty("commanderView").GetRawText() == portrait, name + " portrait after purchase");
                Check(bought.GetProperty("medals").GetInt64() == quote.GetProperty("medals").GetInt64() - offer.GetProperty("price").GetInt64(), name + " actual discounted debit");
                var sold = Call(host, route, new { cell, op = "sell", revision = bought.GetProperty("revision").GetInt64(), item = item.GetProperty("id").GetString() });
                Check(sold.GetProperty("commanderView").GetRawText() == portrait, name + " portrait after sale");
            }
            Check(Call(host, route, new { cell }).GetProperty("commander").GetRawText() == quote.GetProperty("commander").GetRawText(), name + " reopening");
        }
        var headquarters = Call(new ClientGame(content, Path.Combine(output, "headquarters")), "commerce", new { cell = -1 });
        Check(headquarters.GetProperty("commanderView").ValueKind == JsonValueKind.Null && headquarters.GetProperty("commerce").GetInt32() == 0, "headquarters has no city commander");
        File.WriteAllText(Path.Combine(output, "fixture.json"), JsonSerializer.Serialize(new { scenario, shop, exchange }, ClientGame.Json));
        return checks;
    }
}
