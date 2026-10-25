using System.Text.Json;
using Ew4.Client;

internal static class RequestBattleChecks
{
    internal static int Run(string root)
    {
        int checks = 0, reads = 0;
        bool broken = false;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException(label); }
        byte[] Read(string path)
        {
            if (path == "original/conquest1.btl")
            {
                reads++;
                if (broken) throw new IOException("Injected changed content");
            }
            return File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/", StringComparison.Ordinal)
                ? "legacy/" + path[5..] : path));
        }
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string folder = Path.Combine(root, "build/request-battle-checks", Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var result = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            return result.RootElement.Clone();
        }
        Check(Call("start", new { scenario = "conquest1", country = "fra" }).GetProperty("ok").GetBoolean(), "Request fixture start");
        reads = 0;
        var first = Call("action", new { op = "state" });
        Check(first.GetProperty("ok").GetBoolean() && reads == 1, "A full view must parse its battle once per request");
        reads = 0;
        var second = Call("action", new { op = "state" });
        Check(second.GetProperty("ok").GetBoolean() && reads == 1, "Battle reuse must end with the request");
        Check(JsonElement.DeepEquals(first, second), "Repeated full views must remain identical");
        broken = true;
        Check(!Call("action", new { op = "state" }).GetProperty("ok").GetBoolean(), "Changed content must not use a previous request's battle");
        broken = false; reads = 0;
        Check(Call("action", new { op = "state" }).GetProperty("ok").GetBoolean() && reads == 1, "A failed request must permit a fresh read");
        return checks;
    }
}
