using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class LocalMultiplayerChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new InvalidOperationException("Local multiplayer: " + label); }
        var basis = WorldChecks.Example();
        var local = basis with { LocalMultiplayer = new("winner", basis.Factions.Select(f => f.Id).ToArray()), ScenarioPlayerFaction = "winner",
            Factions = basis.Factions.Select(f => f with { Human = true }).ToArray() };
        var rules = new VictoryRules(1, true);
        Check(WorldVictory.Evaluate(local with { Round = 100 }, "b", rules, []) == BattleOutcome.Continue, "single player round limit does not end local play");
        var marked = local with { Cells = local.Cells.Select((c, i) => c with { Objective = i is 0 or 8 }).ToArray() };
        var allies = marked with { Factions = marked.Factions.Select(f => f with { Camp = "same" }).ToArray() };
        Check(LocalMultiplayerGame.Winner(allies, rules) == null, "allies with separate objectives cannot share victory");
        var captured = allies with { Cells = allies.Cells.Select(c => c with { Owner = "winner" }).ToArray() };
        var won = LocalMultiplayerGame.Resolve(captured, rules);
        Check(won.LocalMultiplayer!.WinnerFaction == "winner" && won.Phase == TurnPhase.Finished, "one owner of all goals wins");
        Check(LocalMultiplayerGame.Finish(won, "loser").LocalMultiplayer!.WinnerFaction == "winner", "winner cannot be overwritten");
        var aiWinner = captured with { LocalMultiplayer = new("loser", ["loser"]),
            Factions = captured.Factions.Select(f => f with { Human = f.Id == "loser" }).ToArray() };
        Check(LocalMultiplayerGame.Resolve(aiWinner, rules).Outcome == BattleOutcome.Defeat, "AI can win and every human loses");
        var mainLost = local with { Factions = local.Factions.Select(f => f.Id == "winner" ? f with { Defeated = true } : f).ToArray() };
        Check(LocalMultiplayerGame.Winner(mainLost, rules) == "loser", "eliminated original country does not force global defeat");
        var tied = local with { ScenarioVictory = new() { Win = new() { Type = "round_reached", Round = 1 } } };
        Check(LocalMultiplayerGame.Winner(tied, rules) == local.Factions[0].Id, "simultaneous completion uses saved order");
        var money = local with { ScenarioVictory = new() { Win = new() { Type = "resources", Resources = new(10, 0, 0) },
                Lose = new() { Type = "round_reached", Round = 1 } },
            Factions = local.Factions.Select(f => f with { Resources = new(f.Id == "winner" ? 10 : 0, 0, 0) }).ToArray() };
        Check(LocalMultiplayerGame.Winner(money, rules) == "winner", "custom goals use each country's resources and ignore single player loss");
        var home = captured with { LocalMultiplayer = captured.LocalMultiplayer! with { InitialObjectiveOwner = "winner", HadObjectives = true } };
        Check(LocalMultiplayerGame.Winner(home, rules) == null, "defender does not win from initial objective ownership");
        var contested = LocalMultiplayerGame.Resolve(home with {
            Cells = home.Cells.Select((c, i) => i == 0 ? c with { Owner = "loser" } : c).ToArray() }, rules);
        Check(contested.LocalMultiplayer!.InitialObjectiveOwner == null && contested.Outcome == BattleOutcome.Continue,
            "losing an objective makes defender eligible for recapture victory");
        Check(LocalMultiplayerGame.Resolve(contested with { Cells = home.Cells }, rules).LocalMultiplayer!.WinnerFaction == "winner",
            "defender wins after reclaiming all objectives");
        Check(LocalMultiplayerGame.Winner(captured with { Cells = captured.Cells.Select((c, i) => i == 0 ? c with { Owner = null } : c).ToArray() }, rules) == null,
            "unowned marked objectives still need to be captured");
        Check(LocalMultiplayerGame.Resolve(captured with { PendingDialogue = "pending" }, rules).Phase != TurnPhase.Finished,
            "pending event choice resolves before automatic victory");
        var campaign = allies with { LocalMultiplayer = allies.LocalMultiplayer! with { ShareAlliedVictory = true },
            Factions = allies.Factions.Append(allies.Factions[0] with { Id = "enemy", Camp = "enemy", Human = false }).ToArray() };
        var shared = LocalMultiplayerGame.Resolve(campaign, rules);
        Check(shared.Phase == TurnPhase.Finished && shared.Outcome == BattleOutcome.Victory
            && LocalMultiplayerGame.Won(shared, "winner", shared.LocalMultiplayer!.WinnerFaction!)
            && LocalMultiplayerGame.Won(shared, "loser", shared.LocalMultiplayer.WinnerFaction!)
            && !LocalMultiplayerGame.Won(shared, "enemy", shared.LocalMultiplayer.WinnerFaction!), "campaign allies share split objectives and results");
        shared.Validate();
        Check(WorldSave.Decode(WorldSave.Encode(shared), shared.ContentRevision).LocalMultiplayer!.ShareAlliedVictory,
            "campaign sharing persists in a validated save");
        var defending = campaign with { LocalMultiplayer = campaign.LocalMultiplayer! with {
            InitialObjectiveOwner = LocalMultiplayerGame.InitialObjectiveOwner(campaign) } };
        Check(LocalMultiplayerGame.Winner(defending, rules) == null, "allied defenders cannot win from initial split ownership");
        var lostGoal = LocalMultiplayerGame.Resolve(defending with { Cells = defending.Cells.Select((c,i)=>i==0?c with { Owner="enemy" }:c).ToArray() }, rules);
        Check(lostGoal.LocalMultiplayer!.InitialObjectiveOwner == null, "enemy capture releases the campaign defending guard");
        Check(LocalMultiplayerGame.Resolve(lostGoal with { Cells = campaign.Cells }, rules).Phase == TurnPhase.Finished,
            "campaign defenders win after allied recapture");
        var diplomatic = WorldDiplomacy.Set(campaign, "winner", "loser", "hostile");
        Check(LocalMultiplayerGame.Winner(diplomatic, rules) == null, "hostility override prevents campaign sharing even in same camp");
        var crossCamp = WorldDiplomacy.Set(campaign with { Factions = campaign.Factions.Select(f=>f.Id=="loser"?f with { Camp="other" }:f).ToArray() }, "winner", "loser", "allied");
        Check(LocalMultiplayerGame.Winner(crossCamp, rules) != null, "explicit alliance shares victory across camp IDs");
        Check(LocalMultiplayerGame.Winner(campaign with { Cells = campaign.Cells.Select((c,i)=>i==0?c with { Owner=null }:c).ToArray() }, rules) == null,
            "campaign alliance still needs unowned objectives");
        var customCampaign = campaign with { ScenarioVictory = new() { Win = new() { Type="cells_owned", Cells=[0,8] } } };
        Check(LocalMultiplayerGame.Winner(customCampaign, rules) != null
            && LocalMultiplayerGame.Winner(customCampaign with { LocalMultiplayer=customCampaign.LocalMultiplayer! with { ShareAlliedVictory=false } }, rules) == null,
            "custom campaign ownership shares allies while conquest keeps exact ownership");
        var aiLead = campaign with { LocalMultiplayer=campaign.LocalMultiplayer! with { MainFaction="loser", HumanFactions=["loser"] },
            Factions=campaign.Factions.Select(f=>f with { Human=f.Id=="loser" }).ToArray() };
        var aiShared = LocalMultiplayerGame.Finish(aiLead, "winner");
        aiShared.Validate();
        Check(aiShared.Outcome == BattleOutcome.Victory && WorldVictory.Evaluate(aiLead, "same", rules, []) == BattleOutcome.Victory,
            "an AI ally's campaign victory counts as human victory");
        var allianceDefense = aiLead with { Cells=aiLead.Cells.Select((c,i)=>c with { Objective=i is 0 or 4 or 8,
            Owner=i==0?"enemy":i==4?"loser":i==8?"enemy":"winner" }).ToArray() };
        Check(LocalMultiplayerGame.Resolve(allianceDefense, rules).Outcome == BattleOutcome.Continue,
            "losing one's own objectives does not end a campaign while an ally still holds a goal");
        var allianceLost=LocalMultiplayerGame.Resolve(allianceDefense with { Cells=allianceDefense.Cells.Select(c=>c.Objective?c with { Owner="enemy" }:c).ToArray() },rules);
        Check(allianceLost.Outcome == BattleOutcome.Defeat
            && !LocalMultiplayerGame.Won(allianceLost,"winner",allianceLost.LocalMultiplayer!.WinnerFaction!)
            && !LocalMultiplayerGame.Won(allianceLost,"loser",allianceLost.LocalMultiplayer.WinnerFaction!),
            "all members lose together when the opposing side collects every objective");
        allianceLost.Validate();
        var coreEnd = WorldTurns.EndTurn(captured, captured.Revision, new() { PlayerCamp = "b", Victory = rules }).State;
        Check(coreEnd.LocalMultiplayer!.WinnerFaction == "winner", "core turn transaction stores winner before serialization");
        Check(WorldSave.Decode(WorldSave.Encode(coreEnd), coreEnd.ContentRevision).LocalMultiplayer!.WinnerFaction == "winner",
            "core-only finished save retains winner without a client projection");
        void Invalid(WorldState state, string label)
        {
            try { LocalMultiplayerGame.Validate(state); } catch (RuleViolation e) when (e.Code == "local.state") { Check(true, label);return; }
            Check(false, label);
        }
        Invalid(local with { LocalMultiplayer = local.LocalMultiplayer! with { HumanFactions = local.LocalMultiplayer.HumanFactions.Reverse().ToArray() } }, "save rejects reordered seats");
        Invalid(won with { Outcome = BattleOutcome.Defeat }, "save rejects inconsistent winner outcome");
        var controlled = ScenarioRules.Evaluate(local with { ScenarioEvents = [new() { Id = "control", Effects = [
            new() { Type = "player_control", Faction = "loser" }, new() { Type = "defeat" }] }] }).State;
        Check(controlled.Factions.All(f => f.Human) && controlled.ScenarioPlayerFaction == "winner" && controlled.Outcome == BattleOutcome.Continue,
            "single player control and defeat effects cannot replace locked local selection");
        var ending = ScenarioRules.Evaluate(local with { ScenarioEvents = [new() { Id = "end", Effects = [
            new() { Type = "ending", Faction = "loser", Key = "local-ending", Outcome = "victory" }] }] }).State;
        Check(ending.LocalMultiplayer!.WinnerFaction == "loser" && ending.ScenarioEnding == "local-ending", "explicit faction victory event has one winner");
        var copied = won.Copy();
        Check(!ReferenceEquals(copied.LocalMultiplayer!.HumanFactions, won.LocalMultiplayer!.HumanFactions), "copy owns its selection list");
        var savedWorld = WorldSave.Decode(WorldSave.Encode(won), won.ContentRevision);
        Check(savedWorld.LocalMultiplayer!.WinnerFaction == "winner", "world save preserves local winner");

        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("data/", StringComparison.Ordinal)
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        string folder = Path.Combine(root, "rewrite/reports/local-multiplayer-20261021", Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object? args = null, string? error = null)
        {
            using var document = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(args ?? new { }, ClientGame.Json)));
            var result = document.RootElement;
            Check(result.GetProperty("ok").GetBoolean() == (error == null), route + ": " + result.GetRawText()[..Math.Min(240, result.GetRawText().Length)]);
            if (error != null) Check(result.GetProperty("code").GetString() == error, "expected " + error);
            return result.GetProperty(error == null ? "data" : "code").Clone();
        }
        long Revision(JsonElement view) => view.GetProperty("revision").GetInt64();
        foreach (var scenario in content.Scenarios.Where(s => s.Mode is 1 or 2))
        {
            var battle = content.Battle(scenario.Id);
            string? country = scenario.Mode == 2 ? Call("countries", new { scenario = scenario.Id })[0].GetProperty("id").GetString() : null;
            var choices = Call("local-players", new { scenario = scenario.Id, country });
            string[] offered = choices.EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToArray();
            var playable = battle.Factions.Select((f, i) => (f, i)).Where(p => scenario.Mode == 1 || p.f.Camp != 4)
                .Select(p => LegacyScenarioInitialization.FactionId(scenario.Id, p.i, p.f)).ToArray();
            Check(offered.Length == playable.Length && offered.ToHashSet().SetEquals(playable), "campaign includes neutrals; conquest excludes neutrals " + scenario.Id);
            int primary = scenario.Mode == 2 ? battle.Factions.ToList().FindIndex(f => content.Countries.Canonical(f.CountryId) == country)
                : battle.Factions.ToList().FindIndex(f => !f.Ai);
            Check(choices[0].GetProperty("locked").GetBoolean() && offered[0] == LegacyScenarioInitialization.FactionId(scenario.Id, primary, battle.Factions[primary])
                && choices.EnumerateArray().Count(f => f.GetProperty("locked").GetBoolean()) == 1, "original combatant locked once " + scenario.Id);
            var allSelected = Call("start", new { scenario = scenario.Id, country, localPlayers = offered });
            Check(allSelected.GetProperty("factions").EnumerateArray().All(f =>
                f.GetProperty("human").GetBoolean() == offered.Contains(f.GetProperty("id").GetString()!)),
                "select all assigns only combatants to humans " + scenario.Id);
            foreach (var neutral in battle.Factions.Select((f, i) => (f, i)).Where(p => scenario.Mode == 2 && p.f.Camp == 4))
            {
                Call("start", new { scenario = scenario.Id, country, localPlayers = new[] { offered[0],
                    LegacyScenarioInitialization.FactionId(scenario.Id, neutral.i, neutral.f) } }, "local.selection");
                if (scenario.Mode == 2) Call("local-players", new { scenario = scenario.Id, country = neutral.f.CountryId }, "local.selection");
            }
        }
        var selection = Call("local-players", new { scenario = "conquest1", country = "fra" });
        string[] ids = selection.EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToArray();
        Check(selection[0].GetProperty("locked").GetBoolean() && selection.EnumerateArray().Count(f => f.GetProperty("locked").GetBoolean()) == 1, "original country locked once");
        Check(ids.Distinct().Count() == ids.Length, "stable faction identities");
        Call("start", new { scenario = "conquest1", country = "fra", localPlayers = Array.Empty<string>() }, "local.selection");
        Call("start", new { scenario = "conquest1", country = "fra", localPlayers = new[] { ids[1] } }, "local.selection");
        Call("start", new { scenario = "conquest1", country = "fra", localPlayers = new[] { ids[0], ids[0] } }, "local.selection");
        Call("start", new { scenario = "conquest1", country = "fra", localPlayers = new[] { ids[0], "unknown" } }, "local.selection");
        Call("start", new { scenario = "tutorials1", localPlayers = ids }, "local.selection");
        Call("local-players", new { scenario = "tutorials1" }, "local.mode");
        var view = Call("start", new { scenario = "conquest1", country = "fra", localPlayers = ids.Reverse().ToArray() });
        Check(view.GetProperty("localMultiplayer").GetProperty("humanFactions").EnumerateArray().Select(e => e.GetString()).SequenceEqual(ids), "selection input order does not alter turn order");
        Check(view.GetProperty("factions").EnumerateArray().All(f => f.GetProperty("human").GetBoolean() == ids.Contains(f.GetProperty("id").GetString()!)),
            "all-select includes every playable faction and leaves neutral countries to AI");
        long medals = view.GetProperty("medals").GetInt64();
        for (int i = 1; i <= ids.Length; i++)
        {
            view = Call("action", new { op = "end", revision = Revision(view) });
            while (!view.GetProperty("factions").EnumerateArray().Single(f => f.GetProperty("id").GetString() == view.GetProperty("activeFaction").GetString())
                .GetProperty("human").GetBoolean())
                view = Call("action", new { op = "ai_batch", revision = Revision(view) }).GetProperty("state");
            Check(view.GetProperty("activeFaction").GetString() == ids[i % ids.Length]
                && view.GetProperty("playerFaction").GetString() == ids[i % ids.Length], "human handoff " + i);
            Check(view.GetProperty("round").GetInt32() == (i == ids.Length ? 2 : 1), "round counts full rotation " + i);
            Call("action", new { op = "ai", revision = Revision(view) }, "client.ai_turn");
        }
        Check(view.GetProperty("medals").GetInt64() == medals, "handoff does not settle or reward");
        Call("save", new { slot = 1, mode = 2 });
        host = new ClientGame(content, folder);
        var restored = Call("load", new { slot = 1, mode = 2 });
        Check(restored.GetProperty("playerFaction").GetString() == view.GetProperty("playerFaction").GetString()
            && restored.GetProperty("localMultiplayer").GetRawText() == view.GetProperty("localMultiplayer").GetRawText(), "restart and load preserve mode selection and seat");

        var chosen = new[] { ids[0], ids[2] };
        var started = content.Start("conquest1", "fra", localPlayers: chosen);
        var mixed = started.Session with { BattleId = "local-mixed", World = started.Session.World with {
            ScenarioVictory = new() { Win = new() { Type = "round_reached", Round = 999 } },
            Factions = started.Session.World.Factions.Select(f => f.Id == ids[1] ? f with { Camp = "ew4.camp.4" } : f).ToArray() } };
        void Install(GameSession session, string name)
        {
            string path = Path.Combine(folder, name); Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, "slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
                new ClientSlot("conquest1", "fra", SessionSave.Encode(session), LocalPlayers: session.World.LocalMultiplayer!.HumanFactions), ClientGame.Json));
            host = new ClientGame(content, path);view = Call("load", new { slot = 1 });
        }
        var source = content.Battle("conquest1");
        var legacyPlayers = source.Factions.Select((f, i) => LegacyScenarioInitialization.FactionId("conquest1", i, f)).ToArray();
        Install(content.Start("conquest1", "fra", localPlayers: legacyPlayers).Session, "legacy-neutral");
        Check(view.GetProperty("localMultiplayer").GetProperty("humanFactions").GetArrayLength() == legacyPlayers.Length,
            "old saves with neutral human seats still load without rewriting the running match");
        Install(mixed, "ordinary");
        view = Call("action", new { op = "end", revision = Revision(view) });
        Check(view.GetProperty("activeFaction").GetString() == ids[1] && view.GetProperty("playerFaction").GetString() == ids[0], "unselected country belongs to AI");
        Call("action", new { op = "end", revision = Revision(view) }, "client.player_turn");
        view = Call("action", new { op = "ai", revision = Revision(view) });
        Check(view.GetProperty("playerFaction").GetString() == ids[2] && view.GetProperty("activeFaction").GetString() == ids[2], "ordinary AI stops at next human");
        var normal = view;
        Install(mixed, "batched");
        view = Call("action", new { op = "end", revision = Revision(view) });
        var batch = Call("action", new { op = "ai_batch", revision = Revision(view) });
        view = batch.GetProperty("state");
        Check(batch.GetProperty("steps").GetInt32() == 1 && batch.GetProperty("error").ValueKind == JsonValueKind.Null
            && view.GetProperty("playerFaction").GetString() == ids[2], "AI batch cannot cross a human turn");
        foreach (string field in new[] { "round", "activeFaction", "playerFaction", "factions", "localMultiplayer", "units" })
            Check(normal.GetProperty(field).GetRawText() == view.GetProperty(field).GetRawText(), "normal and batch match " + field);
        var mainEliminated = started.Session with { BattleId = "local-main-eliminated", World = started.Session.World with {
            ActiveFaction = started.Session.World.Factions[^1].Id, ScenarioVictory = new() { Win = new() { Type = "round_reached", Round = 999 } },
            Units = started.Session.World.Units.Where(u => u.Owner != ids[0]).ToArray(),
            Commanders = started.Session.World.Commanders.Select(c => c.Owner == ids[0] ? c with { Withdrawn = true } : c).ToArray(),
            Factions = started.Session.World.Factions.Select(f => f.Id == ids[0] ? f with { Defeated = true } : f).ToArray() } };
        var rotated = WorldTurns.EndTurn(mainEliminated.World, mainEliminated.World.Revision, started.Rules.Turns).State;
        Check(rotated.ActiveFaction == ids[1] && rotated.Outcome == BattleOutcome.Continue, "rotation skips defeated original country");

        var finished = mixed with { World = LocalMultiplayerGame.Finish(mixed.World, ids[2]), BattleId = "local-finished" };
        Install(finished, "finished");
        var localResult = view.GetProperty("localMultiplayer");
        Check(localResult.GetProperty("results").EnumerateArray().Count(r => r.GetProperty("outcome").GetString() == "Victory") == 1,
            "exactly one faction wins; all others lose");
        Check(view.GetProperty("battleResult").GetProperty("awardMedals").GetInt64() == 0 && view.GetProperty("medals").GetInt64() == mixed.Account.Medals,
            "local results do not grant single player rewards");
        Check(view.GetProperty("battleResult").GetProperty("nextScenario").ValueKind == JsonValueKind.Null, "local match cannot auto-continue as a single player campaign");
        string receipt = view.GetProperty("battleResult").GetProperty("id").GetString()!;
        Check(JsonSerializer.Deserialize<PlayerAccount>(File.ReadAllBytes(Path.Combine(folder, "finished", "account.json")), ClientGame.Json)!
            .BattleResults.ContainsKey(receipt), "local settlement writes a one-time receipt");
        Call("save", new { slot = 2 });view = Call("load", new { slot = 2 });
        Check(view.GetProperty("localMultiplayer").GetProperty("winnerFaction").GetString() == ids[2], "finished save preserves sole winner");
        foreach (string scenario in new[] { "campaign1_01", "campaign2_03", "campaign3_08", "conquest2" })
        {
            string? country = scenario.StartsWith("conquest", StringComparison.Ordinal) ? "usa" : null;
            var choices = Call("local-players", new { scenario, country });
            string[] selected = choices.EnumerateArray().Select(f => f.GetProperty("id").GetString()!).ToArray();
            var test = Call("start", new { scenario, country, localPlayers = selected });
            Check(test.GetProperty("localMultiplayer").GetProperty("shareAlliedVictory").GetBoolean() == !scenario.StartsWith("conquest", StringComparison.Ordinal),
                "real campaign/conquest selects the matching victory policy " + scenario);
            Check(test.GetProperty("phase").GetString() != "Finished", "real scenario does not auto-finish at opening " + scenario);
            Check(test.GetProperty("factions").EnumerateArray().All(f => f.GetProperty("human").GetBoolean() == selected.Contains(f.GetProperty("id").GetString()!)),
                "real campaign/conquest selection " + scenario);
        }
        var campaignChoices = Call("local-players", new { scenario="campaign1_17" });
        var campaignIds = campaignChoices.EnumerateArray().Select(f=>f.GetProperty("id").GetString()!).ToArray();
        var campaignStart = content.Start("campaign1_17", null, localPlayers: campaignIds);
        Check(campaignStart.Session.World.LocalMultiplayer!.ShareAlliedVictory, "reported North America campaign enables shared victory");
        var oldCampaign = campaignStart.Session with { World=campaignStart.Session.World with {
            LocalMultiplayer=campaignStart.Session.World.LocalMultiplayer with { ShareAlliedVictory=false } } };
        string campaignFolder=Path.Combine(folder,"campaign-old");Directory.CreateDirectory(campaignFolder);
        File.WriteAllBytes(Path.Combine(campaignFolder,"slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign1_17", null, SessionSave.Encode(oldCampaign), LocalPlayers: campaignIds), ClientGame.Json));
        host=new ClientGame(content,campaignFolder);view=Call("load",new { slot=1 });
        Check(view.GetProperty("localMultiplayer").GetProperty("shareAlliedVictory").GetBoolean(), "ongoing old campaign upgrades on load");
        var resultStart=content.Start("campaign2_03",null,localPlayers: content.Battle("campaign2_03").Factions.Select((f,i)=>LegacyScenarioInitialization.FactionId("campaign2_03",i,f)).ToArray());
        string resultWinner=resultStart.Session.PlayerFaction;
        var campaignFinished=resultStart.Session with { BattleId="campaign-shared-finished", World=LocalMultiplayerGame.Finish(resultStart.Session.World with {
            PendingDialogue=null,QueuedDialogues=[] },resultWinner) };
        string resultFolder=Path.Combine(folder,"campaign-finished");Directory.CreateDirectory(resultFolder);
        File.WriteAllBytes(Path.Combine(resultFolder,"slot-1.json"), JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign2_03", null, SessionSave.Encode(campaignFinished), LocalPlayers: campaignFinished.World.LocalMultiplayer!.HumanFactions), ClientGame.Json));
        host=new ClientGame(content,resultFolder);view=Call("load",new { slot=1 });
        Check(view.GetProperty("localMultiplayer").GetProperty("results").EnumerateArray().All(result=>
            (result.GetProperty("outcome").GetString()=="Victory") == WorldDiplomacy.Allied(campaignFinished.World,result.GetProperty("id").GetString()!,resultWinner)),
            "client campaign results match the winning alliance");
        Check(view.GetProperty("localMultiplayer").GetProperty("results").EnumerateArray().Count(result=>result.GetProperty("outcome").GetString()=="Victory")>1,
            "real campaign fixture includes multiple allied victors");
        string opposing=resultStart.Session.World.Factions.First(f=>!WorldDiplomacy.Allied(resultStart.Session.World,resultWinner,f.Id)).Id;
        var humanSide=resultStart.Session.World.Factions.Where(f=>WorldDiplomacy.Allied(resultStart.Session.World,resultWinner,f.Id)).Select(f=>f.Id).ToArray();
        var defeatStart=content.Start("campaign2_03",null,localPlayers:humanSide).Session;
        var campaignDefeat=defeatStart with { BattleId="campaign-shared-defeat", World=LocalMultiplayerGame.Finish(defeatStart.World with {
            PendingDialogue=null,QueuedDialogues=[] },opposing) };
        string defeatFolder=Path.Combine(folder,"campaign-defeat");Directory.CreateDirectory(defeatFolder);
        File.WriteAllBytes(Path.Combine(defeatFolder,"slot-1.json"),JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign2_03",null,SessionSave.Encode(campaignDefeat),LocalPlayers:humanSide),ClientGame.Json));
        host=new ClientGame(content,defeatFolder);view=Call("load",new { slot=1 });
        Check(view.GetProperty("battleResult").GetProperty("outcome").GetString()=="Defeat"
            && view.GetProperty("localMultiplayer").GetProperty("results").EnumerateArray().Where(r=>humanSide.Contains(r.GetProperty("id").GetString()!))
                .All(r=>r.GetProperty("outcome").GetString()=="Defeat"),"client reports the entire human alliance defeated");
        Check(view.GetProperty("battleResult").GetProperty("failureDescriptionKey").ValueKind==JsonValueKind.Null,
            "multiplayer defeat does not use the single player failure screen");
        var singleStart=content.Start("campaign2_03",null).Session;
        var singleDefeat=singleStart with { BattleId="single-failure-audit", World=singleStart.World with {
            PendingDialogue=null,QueuedDialogues=[],Phase=TurnPhase.Finished,Outcome=BattleOutcome.Defeat } };
        string singleFolder=Path.Combine(folder,"single-defeat");Directory.CreateDirectory(singleFolder);
        File.WriteAllBytes(Path.Combine(singleFolder,"slot-1.json"),JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign2_03",null,SessionSave.Encode(singleDefeat)),ClientGame.Json));
        host=new ClientGame(content,singleFolder);view=Call("load",new { slot=1 });
        Check(view.GetProperty("battleResult").GetProperty("failureDescriptionKey").GetString()=="desc_failure 1",
            "ordinary single player defeat selects the original combat failure description");
        Check(view.GetProperty("battleResult").GetProperty("failureSpeaker").GetProperty("portrait").GetString()=="Nelson.png",
            "campaign failure dialogue uses the scenario commander's portrait");
        var timeout=singleDefeat with { BattleId="single-timeout-audit",World=singleDefeat.World with { Round=2,
            ScenarioVictory=new() { Win=new() { Type="round_reached",Round=999 },RoundLimit=1 } } };
        string timeoutFolder=Path.Combine(folder,"single-timeout");Directory.CreateDirectory(timeoutFolder);
        File.WriteAllBytes(Path.Combine(timeoutFolder,"slot-1.json"),JsonSerializer.SerializeToUtf8Bytes(
            new ClientSlot("campaign2_03",null,SessionSave.Encode(timeout)),ClientGame.Json));
        host=new ClientGame(content,timeoutFolder);view=Call("load",new { slot=1 });
        Check(view.GetProperty("battleResult").GetProperty("failureDescriptionKey").GetString()=="desc_failure 2",
            "single player round-limit defeat selects the original timeout description");
        view = Call("start", new { scenario = "conquest1", country = "fra" });
        Check(view.GetProperty("localMultiplayer").ValueKind == JsonValueKind.Null && view.GetProperty("factions").EnumerateArray().Count(f => f.GetProperty("human").GetBoolean()) == 1,
            "omitting local selection keeps normal single player");
        Directory.CreateDirectory(Path.Combine(root, "rewrite/reports/local-multiplayer-20261021"));
        File.WriteAllText(Path.Combine(root, "rewrite/reports/local-multiplayer-20261021/backend.json"), JsonSerializer.Serialize(new { checks, errors = 0, folder }));
        return checks;
    }
}
