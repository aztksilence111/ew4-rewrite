using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class NavalDeploymentChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Naval deployment: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/")
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        var owned = content.Catalogs.Accounts.FromDefinition(1, "naval.test.commander");
        var account = new PlayerAccount { Id = "naval-test", ContentRevision = content.Catalogs.Accounts.SourceRevision, Roster = [owned] };
        var started = content.Start("conquest1", "fra", account);
        var session = started.Session;
        var world = session.World;
        var ships = world.Units.Where(u => u.Owner == world.ActiveFaction && u.CommanderId == null
            && u.Combatant.Profile.Category == "naval" && world.Cells[u.Cell].Terrain.Sea).ToArray();
        Check(ships.Length > 0, "real French navy provides naval unit fixtures");
        var seaCells = ships.Select(u => u.Cell).ToHashSet();
        world = world with { Cells = world.Cells.Select((c, i) => seaCells.Contains(i) ? c with { Owner = null } : c).ToArray() };
        session = session with { World = world };
        foreach (var ship in ships)
        {
            Check(RosterDeployment.CanDeploy(session, ship.Id, owned.Id, started.Rules.Roster), "owned ship accepts available commander at sea");
            var deployed = RosterDeployment.Deploy(session, session.Revision, ship.Id, owned.Id, started.Rules.Roster).State;
            var assigned = deployed.World.Units.Single(u => u.Id == ship.Id);
            Check(assigned.CommanderId == owned.Id && assigned.Combatant.Profile.Category == "naval", "real deployment retains naval profile");
            Check(deployed.RosterDeployments.Single().CommanderIds.SequenceEqual([owned.Id]), "naval deployment consumes the normal quota");
            Check(deployed.World.Cells[ship.Cell] == world.Cells[ship.Cell], "deployment does not claim sea territory");
            var bytes = SessionSave.Encode(deployed);
            var binding = new SessionContentBinding(session.ContentRevision, world.ContentRevision, account.ContentRevision);
            Check(bytes.SequenceEqual(SessionSave.Encode(SessionSave.Decode(bytes, binding))), "naval assignment survives exact save roundtrip");
            Check(!RosterDeployment.CanDeploy(deployed, ship.Id, owned.Id, started.Rules.Roster), "cannot assign twice");
        }
        var target = ships[0];
        var noQuota = session with { RosterDeployments = session.RosterDeployments.Select(q => q with { Limit = 0 }).ToArray() };
        Check(!RosterDeployment.CanDeploy(noQuota, target.Id, owned.Id, started.Rules.Roster), "zero quota still rejects naval assignment");
        var enemy = world.Units.First(u => u.Owner != world.ActiveFaction && u.Combatant.Profile.Category == "naval");
        Check(!RosterDeployment.CanDeploy(session, enemy.Id, owned.Id, started.Rules.Roster), "enemy navy remains unavailable");
        var inactive = world with { Cells = world.Cells.Select((c, i) => i == target.Cell ? c with { Terrain = c.Terrain with { Active = false } } : c).ToArray() };
        Check(!RosterDeployment.IsDeploymentLocation(inactive, target), "inactive map cells remain unavailable");
        var army = world.Units.First(u => u.Owner == world.ActiveFaction && u.CommanderId == null && u.Combatant.Profile.Category == "infantry");
        Check(!RosterDeployment.IsDeploymentLocation(world, army with { Cell = target.Cell }), "land troops do not inherit the naval exception");
        var folder = Path.Combine(root, "rewrite/reports/mobile-display-pan-20261022/naval-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(account, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "session.json"), JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1", "fra", SessionSave.Encode(session)), ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            var response = document.RootElement;
            Check(response.GetProperty("ok").GetBoolean(), route + " succeeds: " + response);
            return response.GetProperty("data").Clone();
        }
        var view = Call("load", new { });
        long revision = view.GetProperty("revision").GetInt64();
        var selected = Call("action", new { op = "select", cell = target.Cell, revision });
        Check(selected.GetProperty("deploy").GetBoolean(), "shared UI exposes naval deployment button");
        var available = Call("deployment", new { unit = target.Id, revision });
        Check(available.GetProperty("commanders").EnumerateArray().Any(c => c.GetString() == owned.Id), "naval roster contains the available commander");
        var result = Call("action", new { op = "deploy", unit = target.Id, commander = owned.Id, revision });
        Check(result.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == target.Id)
            .GetProperty("commanderId").GetString() == owned.Id, "same naval assignment succeeds through shared client API");
        return checks;
    }
}
