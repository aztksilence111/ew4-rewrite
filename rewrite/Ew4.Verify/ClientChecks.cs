using System.Text.Json;
using Ew4.Client;
using Ew4.Core;

internal static class ClientChecks
{
    public static int Run(string root)
    {
        int checks = UnitNavigationChecks.Run();
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Client: " + label); }
        string assets = Path.Combine(root, "rewrite/data/original");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/", StringComparison.Ordinal) ? Path.Combine(assets, path[9..])
            : Path.Combine(root, "rewrite/data/legacy", path[5..]));
        var content = new ClientContent(Read, Directory.EnumerateFiles(assets, "*.btl"));
        string folder = Path.Combine(root, "rewrite/reports/client-slot-test", "run-" + Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object? command = null, bool success = true)
        {
            using var result = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            var response = result.RootElement;
            Check(response.GetProperty("ok").GetBoolean() == success, route + " success; " + response.GetRawText()[..Math.Min(200, response.GetRawText().Length)]);
            return response.GetProperty(success ? "data" : "code").Clone();
        }
        Check(Call("catalog").GetProperty("scenarios").GetArrayLength() == 95, "all original scenarios listed");
        Check(content.Scenarios.Single(s => s.Id == "conquest1").Name == "欧洲 1798", "original title localization");
        Check(content.Label("fra") == "法国", "country localization");
        Check(content.Chapters.Count == 6 && content.Chapters[0].Name == "帝国雄鹰", "source campaign chapters");
        Check(content.Scenarios.Single(s => s.Id == "tutorials1").Name == "基础教程", "source tutorial names");
        Check(content.Presentations["campaign1_01"].Description.Contains("土伦港"), "campaign introduction present");
        Check(content.Presentations["campaign3_08"].Alternate == "campaign3_08b", "campaign country branch retained");
        Check(content.Scenarios.Where(s => s.Mode == 1).All(s => s.Id.StartsWith("campaign")), "multiplayer not misclassified as campaign");
        Check(Call("countries", new { scenario = "conquest1" }).EnumerateArray().All(c => c.GetProperty("camp").GetInt32() != 4), "neutral factions excluded");
        var view = Call("start", new { scenario = "conquest1", country = "fra" });
        var sourceColors = content.Battle("conquest1").Factions.Select(f => f.Color).ToArray();
        Check(view.GetProperty("factions").EnumerateArray().Select(f => f.GetProperty("color").GetInt32()).SequenceEqual(sourceColors), "original packed faction colors retained");
        Check(view.GetProperty("units").GetArrayLength() == content.Battle("conquest1").Armies.Count, "render view retains original armies");
        Check(view.GetProperty("map").GetString() == "europe", "map binding");
        var detailRevision = view.GetProperty("revision").GetInt64();
        foreach (var displayed in view.GetProperty("units").EnumerateArray().Where(u => u.GetProperty("commanderId").ValueKind != JsonValueKind.Null))
        {
            string unitId = displayed.GetProperty("id").GetString()!;
            var detail = Call("unit-info", new { unit = unitId, revision = detailRevision }).GetProperty("commander");
            var source = content.Catalogs.Runtime.Commanders[content.Battle("conquest1").Armies[int.Parse(unitId.Split('.')[^1])].Commander];
            Check(detail.GetProperty("definitionId").GetString() == source.Definition.Id, "battle detail retains exact scenario commander identity");
            Check(detail.GetProperty("learning").GetProperty("rank").GetProperty("level").GetInt32() == source.Rank, "battle commander rank comes from instance");
            Check(detail.GetProperty("training").GetInt32() == source.Training && detail.GetProperty("battle").GetBoolean(), "scenario commander has training and read-only context");
        }
        var noCommanderId = view.GetProperty("units").EnumerateArray().First(u => u.GetProperty("commanderId").ValueKind == JsonValueKind.Null).GetProperty("id").GetString();
        Check(Call("unit-info", new { unit = noCommanderId, revision = detailRevision }).GetProperty("commander").ValueKind == JsonValueKind.Null, "plain units have no fake commander");
        Check(Call("unit-info", new { unit = noCommanderId, revision = detailRevision + 1 }, false).GetString() == "client.stale", "stale detail rejected");
        Check(Call("unit-info", new { unit = "missing", revision = detailRevision }, false).GetString() == "client.unit", "missing detail rejected");
        Check(Call("action", new { op = "state", revision = detailRevision }).GetRawText() == view.GetRawText(), "all detail reads preserve battle state");
        var availability=content.Start("conquest1","fra");
        var availabilityWorld=availability.Session.World;
        var attackMap=new HexMap(availabilityWorld.Width,availabilityWorld.Height);
        var camps=availabilityWorld.Factions.ToDictionary(f=>f.Id,f=>f.Camp);
        foreach(var fort in availabilityWorld.Units.Where(u=>u.Owner==availabilityWorld.ActiveFaction && u.Combatant.Profile.Category=="fort")) {
            var displayed=view.GetProperty("units").EnumerateArray().Single(u=>u.GetProperty("id").GetString()==fort.Id);
            bool target=availabilityWorld.Units.Any(u=>camps[u.Owner]!=camps[fort.Owner] && AttackLegality.Check(true,true,fort.Combatant.ConstructionTurns,fort.Acted,fort.Morale.Effective,
                attackMap.Distance(fort.Cell,u.Cell),fort.Combatant.Profile.MinimumRange,fort.Combatant.Profile.MaximumRange)==AttackBlock.None);
            Check(!displayed.GetProperty("canMove").GetBoolean(),"fort never pulses for movement");
            Check(displayed.GetProperty("canAttack").GetBoolean()==target,"fort badge requires a legal hostile target");
        }
        foreach (var displayed in view.GetProperty("units").EnumerateArray().Where(u => u.GetProperty("owner").GetString() == availabilityWorld.ActiveFaction))
        {
            var unit = availabilityWorld.Units.Single(u => u.Id == displayed.GetProperty("id").GetString());
            var selectedView = Call("action", new { op = "select", cell = unit.Cell, revision = detailRevision });
            Check(displayed.GetProperty("canNavigateMove").GetBoolean() == (unit.Combatant.Profile.Category != "fort"
                && unit.Combatant.ConstructionTurns == 0 && selectedView.GetProperty("range").GetArrayLength() > 0),
                "navigation move availability matches authoritative selection range");
        }
        var battleBounds = content.Battle("conquest1");
        var camera = view.GetProperty("cameraBounds");
        Check(camera.GetProperty("left").GetInt32() == battleBounds.Left * 64 - 52 && camera.GetProperty("width").GetInt32() == battleBounds.Width * 64 + 72, "source scenario bounds with native margin");
        long revision = view.GetProperty("revision").GetInt64();
        Check(Call("action", new { op = "end", revision = revision + 1 }, false).GetString() == "client.stale", "stale command rejected");
        Check(Call("action", new { op = "ai", revision }, false).GetString() == "client.ai_turn", "AI cannot act for human");
        Check(Call("action", null, false).GetString() == "client.command", "null command rejected");
        Check(Call("unknown", null, false).GetString() == "client.route", "unknown route rejected");
        string player = view.GetProperty("playerFaction").GetString()!;
        int immobile = 0; bool moved = false;
        foreach (var unit in view.GetProperty("units").EnumerateArray().Where(u => u.GetProperty("owner").GetString() == player))
        {
            var selection = Call("action", new { op = "select", revision, cell = unit.GetProperty("cell").GetInt32() });
            var range = selection.GetProperty("range").EnumerateArray().Select(c => c.GetInt32()).ToArray();
            if (unit.GetProperty("category").GetString() == "fort") {
                Check(range.Length == 0, "stationary fort can be selected");
                Check(!selection.GetProperty("transport").GetBoolean(), "fort transport not offered");
                Check(Call("action", new { op = "transport", revision, unit = unit.GetProperty("id").GetString() }, false).GetString() == "transport.unavailable", "fort transport cannot bypass UI");
                immobile++;
            }
            if (moved || unit.GetProperty("remainingMovement").GetInt32() < 1) continue;
            int destination = range.FirstOrDefault(c => !view.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("cell").GetInt32() == c), -1);
            if (destination < 0) continue;
            string id = unit.GetProperty("id").GetString()!;
            var next = Call("action", new { op = "move", revision, unit = id, cell = destination });
            Check(next.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("id").GetString() == id).GetProperty("cell").GetInt32() == destination, "actual move through host");
            var path = next.GetProperty("movements")[0].GetProperty("path").EnumerateArray().Select(c => c.GetInt32()).ToArray();
            Check(path[0] == unit.GetProperty("cell").GetInt32() && path[^1] == destination, "authoritative animation path endpoints");
            var hex = new HexMap(view.GetProperty("width").GetInt32(), view.GetProperty("height").GetInt32());
            Check(path.Zip(path.Skip(1)).All(p => Enumerable.Range(0,6).Any(d => hex.Neighbor(p.First,d) == p.Second)), "animation path follows adjacent cells");
            long undoRevision = next.GetProperty("revision").GetInt64();
            for (int attempt=0;attempt<12;attempt++) {
                Check(Call("action", new { op="select", revision=undoRevision, cell=destination }).GetProperty("undo").GetBoolean(), "move undo available without quota");
                if (attempt==0) { Call("save"); Check(Call("load").GetProperty("undoUnit").GetString()==id,"movement history survives load"); }
                var undone=Call("action",new { op="undo", revision=undoRevision, unit=id });
                Check(undone.GetProperty("movements")[0].GetProperty("instant").GetBoolean(),"movement undo is an instantaneous return");
                Check(undone.GetProperty("units").GetRawText()==view.GetProperty("units").GetRawText(),"movement undo restores unit positions and movement");
                Check(undone.GetProperty("factions").GetRawText()==view.GetProperty("factions").GetRawText(),"undo does not refund resources");
                Check(undone.GetProperty("cells").GetRawText()==view.GetProperty("cells").GetRawText(),"undo restores movement territory");
                next=Call("action",new { op="move", revision=undone.GetProperty("revision").GetInt64(),unit=id,cell=destination });
                undoRevision=next.GetProperty("revision").GetInt64();
            }
            revision = next.GetProperty("revision").GetInt64(); moved = true;
        }
        Check(immobile > 0 && moved, "real conquest includes fort selection and movement");
        var fixture=content.Start("conquest1","fra");var world=fixture.Session.World;
        var mover=world.Units.First(u=>u.Owner==world.ActiveFaction && WorldActions.Reachable(world,u.Id,fixture.Rules.Movement).Count>1);
        var route=WorldActions.Reachable(world,mover.Id,fixture.Rules.Movement).Values.First(r=>r.Cell!=mover.Cell && !world.Units.Any(u=>u.Cell==r.Cell));
        var movement=WorldActions.Move(world,world.Revision,mover.Id,route.Cell,fixture.Rules.Movement).State;
        var history=new MovementHistory();history.Reset(world);history.Record(world,movement,mover.Id,route.Path.ToArray());
        Check(history.Available(movement,mover.Id)!=null,"history permits movable unit");
        history.Attack(mover.Id);Check(history.Available(movement,mover.Id)==null,"attacker locked for whole turn even if cavalry regains movement");
        var history2=new MovementHistory();history2.Load(history.Save(),movement);Check(history2.Available(movement,mover.Id)==null,"attack lock survives save");
        history2.Observe(movement with {Round=movement.Round+1});Check(history2.Save().Moves.Length==0 && history2.Save().Attackers.Length==0,"next turn clears movement history");
        history.Reset(world);history.Record(world,movement,mover.Id,route.Path.ToArray());
        var spent=movement with {Factions=movement.Factions.Select(f=>f.Id!=movement.ActiveFaction?f:f with {Resources=f.Resources with {Money=f.Resources.Money+7}}).ToArray()};
        var rolled=history.Undo(spent,mover.Id).Change.State;
        Check(rolled.Factions.Single(f=>f.Id==world.ActiveFaction).Resources.Money==spent.Factions.Single(f=>f.Id==world.ActiveFaction).Resources.Money,"other transactions survive movement undo");
        var beforeSave = Call("action", new { op = "state", revision });
        Call("save");
        var restored = Call("load");
        foreach (string key in new[] { "revision", "round", "phase", "playerFaction", "units", "cells", "factions", "dialogue" })
            Check(beforeSave.GetProperty(key).GetRawText() == restored.GetProperty(key).GetRawText(), "save restores " + key);
        Call("save"); Check(File.Exists(Path.Combine(folder, "session.json.bak")), "slot retains previous backup");
        Call("save",new {slot=1,mode=2});
        var slots=Call("saves",new {mode=2});
        Check(slots.GetArrayLength()==7 && slots[0].GetProperty("exists").GetBoolean() && slots[1].GetProperty("exists").GetBoolean(),"automatic and six manual source slots");
        Check(Call("save",new {slot=7,mode=2},false).GetString()=="save.slot","out-of-range slot rejected");
        Call("start",new {scenario="conquest2",country="usa"});Call("save",new {slot=2,mode=2});
        Check(Call("load",new {slot=1,mode=2}).GetProperty("scenario").GetString()=="conquest1","manual slots do not overwrite each other");
        Check(Call("load",new {slot=2,mode=2}).GetProperty("scenario").GetString()=="conquest2","second manual slot loads independently");
        Call("save",new {slot=1,mode=2});
        Check(Call("load",new {slot=1,mode=2}).GetProperty("scenario").GetString()=="conquest2","same slot can be saved repeatedly");
        Call("start",new {scenario="campaign1_01"});Call("save",new {slot=1,mode=1});
        Check(Call("load",new {slot=1,mode=2}).GetProperty("scenario").GetString()=="conquest2","campaign and conquest slot banks are independent");
        var restartedSlotHost=new ClientGame(content,folder);
        using(var loadedSlot=JsonDocument.Parse(restartedSlotHost.Dispatch("/api/load","{\"slot\":1,\"mode\":1}")))
            Check(loadedSlot.RootElement.GetProperty("data").GetProperty("scenario").GetString()=="campaign1_01","manual slot survives host restart");
        Call("load");
        // All map choices use the same host and material cache, not a hard-coded Europe canvas.
        foreach (var scenario in content.Scenarios.Where(s => s.Mode == 2 && s.Id != "conquest1"))
        {
            string country = Call("countries", new { scenario = scenario.Id })[0].GetProperty("id").GetString()!;
            var started = Call("start", new { scenario = scenario.Id, country });
            Check(started.GetProperty("cells").GetArrayLength() == content.Maps[content.Battle(scenario.Id).MapId].Cells.Count, "render grid matches " + scenario.Id);
        }
        var profileDirectory = Path.Combine(folder, "account-" + Guid.NewGuid().ToString("N"));
        var rosterHost = new ClientGame(content, profileDirectory);
        JsonElement RosterCall(string route, object? command = null, bool success = true)
        {
            using var parsed = JsonDocument.Parse(rosterHost.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(parsed.RootElement.GetProperty("ok").GetBoolean() == success, "roster " + route + ": " + parsed.RootElement.GetRawText()[..Math.Min(120,parsed.RootElement.GetRawText().Length)]);
            return parsed.RootElement.GetProperty(success ? "data" : "code").Clone();
        }
        var headquarters = RosterCall("headquarters");
        Check(headquarters.GetProperty("definitions").GetArrayLength() == 588, "all original commander definitions exposed");
        Check(headquarters.GetProperty("roster").GetArrayLength() == 0, "fresh profile does not invent owned commanders");
        var definition = headquarters.GetProperty("definitions").EnumerateArray().Single(c => c.GetProperty("legacyId").GetInt32() == 112);
        Check(definition.GetProperty("price").GetInt64() == 520, "original explicit medal price");
        foreach (int group in Enumerable.Range(0, 3)) {
            var offers = headquarters.GetProperty("offers").GetProperty(group.ToString()).EnumerateArray().Select(e => e.GetString()).ToArray();
            Check(offers.Length == new[] {6,4,2}[group], "original offer count " + group);
            Check(headquarters.GetProperty("definitions").EnumerateArray().Where(d => offers.Contains(d.GetProperty("id").GetString()))
                .All(d => d.GetProperty("rating").GetInt32() >= group*3+1 && d.GetProperty("rating").GetInt32() <= group*3+3), "original rating pool " + group);
        }
        var firstOffer = headquarters.GetProperty("offers").GetProperty("0")[0].GetString();
        definition = headquarters.GetProperty("definitions").EnumerateArray().Single(d => d.GetProperty("id").GetString() == firstOffer);
        long recruitmentPrice = definition.GetProperty("price").GetInt64();
        string commanderDefinition = definition.GetProperty("id").GetString()!;
        headquarters = RosterCall("commander", new { op = "recruit", revision = 0, commander = commanderDefinition });
        Check(headquarters.GetProperty("medals").GetInt64() == 20_000_000 - recruitmentPrice && headquarters.GetProperty("roster").GetArrayLength() == 1, "recruit commits medals and owned roster together");
        string ownedId = headquarters.GetProperty("roster")[0].GetProperty("id").GetString()!;
        Check(RosterCall("commander", new { op = "recruit", revision = 1, commander = commanderDefinition }, false).GetString() == "account.recruit", "duplicate recruitment rejected");
        Check(RosterCall("commander", new { op = "rank", revision = 0, commander = ownedId }, false).GetString() == "account.revision", "stale promotion rejected");
        var ownedBefore = headquarters.GetProperty("roster")[0];
        long promotionPrice = ownedBefore.GetProperty("rankPrice").GetInt64();
        headquarters = RosterCall("commander", new { op = "rank", revision = 1, commander = ownedId });
        Check(headquarters.GetProperty("roster")[0].GetProperty("learning").GetProperty("rank").GetProperty("level").GetInt32()
            == ownedBefore.GetProperty("learning").GetProperty("rank").GetProperty("level").GetInt32() + 1, "promotion advances one configured stage");
        Check(headquarters.GetProperty("medals").GetInt64() == 20_000_000 - recruitmentPrice - promotionPrice, "promotion debits previewed native price");
        var restartedHost = new ClientGame(content, profileDirectory);
        using (var persisted = JsonDocument.Parse(restartedHost.Dispatch("/api/headquarters", "{}")))
            Check(persisted.RootElement.GetProperty("data").GetRawText() == headquarters.GetRawText(), "roster and medals survive host restart");
        var deploymentView = RosterCall("start", new { scenario = "conquest1", country = "fra" });
        var deploymentUnit = deploymentView.GetProperty("units").EnumerateArray().First(u => u.GetProperty("owner").GetString() == deploymentView.GetProperty("playerFaction").GetString()
            && u.GetProperty("commanderId").ValueKind == JsonValueKind.Null);
        deploymentView = RosterCall("action", new { op = "deploy", revision = deploymentView.GetProperty("revision").GetInt64(), unit = deploymentUnit.GetProperty("id").GetString(), commander = ownedId });
        Check(deploymentView.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("commanderId").GetString() == ownedId), "owned commander deploys through real session rules");
        Check(deploymentView.GetProperty("units").EnumerateArray().Single(u => u.GetProperty("commanderId").GetString() == ownedId)
            .GetProperty("commanderDefinitionId").GetString() == commanderDefinition, "owned commander exposes stable definition ID for dialogue configuration");
        Check(RosterCall("headquarters").GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "deployed commander locked in headquarters");
        var deployedDetail = RosterCall("unit-info", new { unit = deploymentUnit.GetProperty("id").GetString(), revision = deploymentView.GetProperty("revision").GetInt64() }).GetProperty("commander");
        Check(deployedDetail.GetProperty("id").GetString() == ownedId && deployedDetail.GetProperty("definitionId").GetString() == commanderDefinition, "owned battle detail uses exact deployed instance");
        Check(deployedDetail.GetProperty("learning").GetRawText() == headquarters.GetProperty("roster")[0].GetProperty("learning").GetRawText(), "owned battle detail retains promoted learning values");
        Check(RosterCall("commander", new { op = "rank", revision = 2, commander = ownedId }, false).GetString() == "account.deployed", "cannot mutate deployed commander stats behind combat state");
        RosterCall("save", new { slot = 1, mode = 2 });
        var savedFiles = Directory.GetFiles(profileDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, File.ReadAllBytes);
        Check(RosterCall("leave", new { revision = -1 }, false).GetString() == "client.stale", "stale exit cannot release the active session");
        Check(RosterCall("headquarters").GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "rejected exit preserves deployment lock");
        RosterCall("leave", new { revision = deploymentView.GetProperty("revision").GetInt64() });
        Check(!RosterCall("headquarters").GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "returning to menu releases deployed commander lock");
        Check(RosterCall("action", new { op = "state" }, false).GetString() == "client.no_session", "exited battle no longer accepts gameplay commands");
        RosterCall("leave", new { });
        Check(Directory.GetFiles(profileDirectory, "*", SearchOption.AllDirectories).Length == savedFiles.Count
            && savedFiles.All(p => File.ReadAllBytes(p.Key).SequenceEqual(p.Value)), "repeated exits preserve all account and save bytes");
        headquarters = RosterCall("commander", new { op = "rank", revision = 2, commander = ownedId });
        Check(headquarters.GetProperty("revision").GetInt64() == 3, "commander promotion available after leaving battle");
        foreach (int slot in new[] { 0, 1 })
        {
            deploymentView = RosterCall("load", new { slot, mode = 2 });
            Check(deploymentView.GetProperty("units").EnumerateArray().Any(u => u.GetProperty("commanderId").GetString() == ownedId), "save retains deployed commander after exit, slot " + slot);
            Check(RosterCall("headquarters").GetProperty("roster")[0].GetProperty("locked").GetBoolean(), "loading restores current battle lock, slot " + slot);
            Check(RosterCall("commander", new { op = "rank", revision = 3, commander = ownedId }, false).GetString() == "account.deployed", "loaded battle still protects commander stats, slot " + slot);
            RosterCall("leave", new { revision = deploymentView.GetProperty("revision").GetInt64() });
        }
        int speakers=0;
        foreach (string scenario in new[] { "campaign1_01", "campaign3_08", "campaign3_08b", "tutorials1", "tutorials2" }) {
            var started=RosterCall("start", new { scenario });
            Check(started.GetProperty("scenario").GetString() == scenario, "campaign/tutorial host entry " + scenario);
            var sourceWorld=content.Start(scenario,null).Session.World;
            Check(started.GetProperty("units").EnumerateArray().Where(u=>u.GetProperty("objective").GetBoolean()).Select(u=>u.GetProperty("id").GetString())
                .SequenceEqual(sourceWorld.Units.Where(u=>u.Objective).Select(u=>u.Id)), "display exposes exact backend unit objectives " + scenario);
            Check(started.GetProperty("units").EnumerateArray().All(u=>u.GetProperty("objective").GetBoolean()==(u.GetProperty("objectiveMarker").GetInt32()==1)),
                "red unit markers match victory objectives " + scenario);
            Check(started.GetProperty("cells").EnumerateArray().All(c=>c.GetProperty("objective").GetBoolean()==(c.GetProperty("objectiveMarker").GetInt32()==1)),
                "red site markers match victory objectives " + scenario);
            if(started.GetProperty("dialogue").ValueKind!=JsonValueKind.Null){var talk=started.GetProperty("dialoguePresentation");
                Check(talk.GetProperty("portrait").GetString()!.EndsWith(".png") && !string.IsNullOrWhiteSpace(talk.GetProperty("name").GetString()),"event dialogue retains speaker and original portrait");speakers++;}
        }
        Check(speakers>0,"campaign dialogue presentation exercised");
        checks += ClientWorkflowChecks.Run(content, folder);
        checks += ShopRefreshChecks.Run(content, folder);
        checks += AiBatchChecks.Run(content, folder);
        checks += FortConstructionChecks.Run(content, root);
        checks += CampaignProgressChecks.Run(content, folder, root);
        File.WriteAllText(Path.Combine(root, "rewrite/reports/client_host.json"), JsonSerializer.Serialize(new { checks, errors = 0,
            scope = "Original content loading, six conquest views, fort selection, actual move, stale/ownership validation and full save/load through shared UI API; not Android runtime or full game acceptance." }, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return checks;
    }
}
