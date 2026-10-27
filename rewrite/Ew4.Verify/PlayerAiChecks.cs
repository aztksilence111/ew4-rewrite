using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Persistence;

internal static class PlayerAiChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Player AI: " + label); }
        void Reject(Action action, string code) { try { action(); } catch (RuleViolation e) { Check(e.Code == code, label: code + ": " + e.Code); return; } throw new InvalidOperationException("Expected " + code); }
        var original = WorldChecks.Example();
        var world = original with { Width = 9, Height = 1, Cells = Enumerable.Range(0, 9).Select(i => new CellState { Owner = i < 4 ? "loser" : "winner" }).ToArray(),
            Units = original.Units.Select(u => u with { Cell = u.Owner == "winner" ? 4 : 0, RemainingMovement = 6,
                Combatant = u.Combatant with { Profile = u.Combatant.Profile with { Grade = "0", AttackBands = [new(-1, 1)] } } }).ToArray() };
        const string unit = "unit.2";
        var ai = new WorldAiRules { Tactics = LegacyAiTactics.Rules(), ExperienceLevels = new Dictionary<string, int> { ["0"] = 0 } };
        var battle = new WorldBattleRules { ResolveIgnition = false, RecalculateLocalMorale = false, KillDialogueProbability = 0 };
        var turns = new WorldTurnRules { PlayerCamp = "b" };
        WorldState Configure(WorldState source, string policy = "manual", int goal = 6)
        {
            var next = PlayerAi.Configure(source, source.Revision, "policy", policy: policy).State;
            next = PlayerAi.Configure(next, next.Revision, "units", units: [unit]).State;
            return PlayerAi.Configure(next, next.Revision, "goal", goal: goal).State;
        }
        WorldState Tick(WorldState source) => PlayerAi.ExecuteStep(source, PlayerAi.PlanStep(source, ai), ai, battle, turns).State;
        WorldState Finish(WorldState source)
        {
            int steps = 0;
            while (PlayerAi.Pending(source) && source.Phase == TurnPhase.Active && source.PendingDialogue == null && steps++ < 100) source = Tick(source);
            Check(steps < 100, "queue terminates"); return source;
        }
        Check(world.Units.All(u => !PlayerAi.Managed(world, u)) && !PlayerAi.Control(world).Enabled, "opening is manual and disabled");
        Reject(() => PlayerAi.Start(world, world.Revision), "player_ai.incomplete");
        var draft = PlayerAi.Configure(world, world.Revision, "policy", policy: "free").State;
        Reject(() => PlayerAi.Start(draft, draft.Revision), "player_ai.incomplete");
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "units", units: []), "player_ai.selection");
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "units", units: ["unit.1"]), "player_ai.selection");
        draft = PlayerAi.Configure(draft, draft.Revision, "units", units: [unit]).State;
        Reject(() => PlayerAi.Start(draft, draft.Revision), "player_ai.incomplete");
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "goal", goal: 99), "player_ai.goal");
        draft = Configure(world);
        Check(!PlayerAi.Pending(draft) && !PlayerAi.Managed(draft, draft.Units[1]), "confirmed draft does not enable AI or badge");
        var direct = PlayerAi.BeginEnd(draft, draft.Revision, turns).State;
        Check(direct.ActiveFaction != "winner" && direct.Units.Single(u => u.Id == unit).Cell == 4, "unstarted configured orders skip player AI");
        var enabled = PlayerAi.Start(draft, draft.Revision).State;
        Check(PlayerAi.Control(enabled).Enabled && PlayerAi.Managed(enabled, enabled.Units[1]), "start enables managed badge");
        Check(PlayerAi.Order(enabled.Units[1]).Policy == "manual" && PlayerAi.Order(enabled.Units[1]).Goal == 6, "manual plus goal retained");
        Reject(() => PlayerAi.Configure(enabled, enabled.Revision, "policy", policy: "hold"), "player_ai.running");
        var pending = PlayerAi.BeginEnd(enabled, enabled.Revision, turns).State;
        Check(PlayerAi.Pending(pending) && pending.ActiveFaction == "winner", "end enters queue before changing faction");
        byte[] bytes = WorldSave.Encode(pending); var loaded = WorldSave.Decode(bytes, pending.ContentRevision);
        var plan = PlayerAi.PlanStep(pending, ai);
        Check(bytes.SequenceEqual(WorldSave.Encode(pending)), "planning does not mutate world or RNG");
        Check(plan.Plan?.Candidates.Any(c => c.Action is AiMoveCommand) == true, "goal produces legal movement");
        var after = Tick(pending);
        Check(after.Units.Single(u => u.Id == unit).Cell == 6 && PlayerAi.Order(after.Units.Single(u => u.Id == unit)).Goal == null, "goal reached and cleared in same commit");
        Check(after.Units.Single(u => u.Id == unit).Behavior == pending.Units[1].Behavior, "persistent legacy behavior untouched");
        Check(WorldSave.Encode(after).SequenceEqual(WorldSave.Encode(Tick(loaded))), "save resumes deterministically");
        var stopped = PlayerAi.Stop(pending, pending.Revision).State;
        Check(!PlayerAi.Pending(stopped) && !PlayerAi.Control(stopped).Enabled && PlayerAi.Order(stopped.Units[1]).Goal == 6, "stop clears queue and keeps order");
        Reject(() => Tick(stopped), "player_ai.inactive");
        var cleared = PlayerAi.Configure(stopped, stopped.Revision, "clear_goal").State;
        Check(PlayerAi.Order(cleared.Units[1]).Goal == null && !PlayerAi.Control(cleared).Draft.GoalConfirmed,
            "clear goal removes committed selected-unit target and confirmation");
        Check(PlayerAi.Order(stopped.Units[1]).Goal == 6, "clear goal preserves source state");
        var ordered = PlayerAi.Configure(draft, draft.Revision, "goal", goals: [6, 8]).State;
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "goal", goals: [6, 6]), "player_ai.goal");
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "goal", goals: [6, 99]), "player_ai.goal");
        Reject(() => PlayerAi.Configure(draft, draft.Revision, "goal", goals: Enumerable.Range(0,33).ToArray()), "player_ai.goal");
        ordered = PlayerAi.Start(ordered, ordered.Revision).State;
        var isolated = ordered.Copy();isolated.Units[1].PlayerControl!.Goals![0] = 7;
        Check(PlayerAi.Targets(PlayerAi.Order(ordered.Units[1])).SequenceEqual([6,8]), "world copy isolates per-unit target lists");
        isolated.Factions[1].PlayerAi!.Draft.Goals![0] = 7;
        Check(PlayerAi.Targets(PlayerAi.Control(ordered).Draft).SequenceEqual([6,8]), "world copy isolates draft target lists");
        var routePending = PlayerAi.BeginEnd(ordered, ordered.Revision, turns).State;
        var firstTarget = Tick(routePending);
        Check(firstTarget.Units[1].Cell == 6 && PlayerAi.Targets(PlayerAi.Order(firstTarget.Units[1])).SequenceEqual([8]), "first target consumed before second is attempted");
        var routeRestored = WorldSave.Decode(WorldSave.Encode(firstTarget), firstTarget.ContentRevision);
        var resumed = Tick(routeRestored);
        Check(resumed.Units[1].Cell == 6 && PlayerAi.Order(resumed.Units[1]).Goal == 8, "next target retained when original movement rule spends all movement");
        Check(WorldSave.Encode(resumed).SequenceEqual(WorldSave.Encode(Tick(firstTarget))), "multi-target progress deterministic across save boundary");
        var nextPlayer = Finish(resumed);nextPlayer = WorldTurns.EndTurn(nextPlayer,nextPlayer.Revision,turns).State;
        var secondTarget = Tick(PlayerAi.BeginEnd(nextPlayer,nextPlayer.Revision,turns).State);
        Check(secondTarget.Units[1].Cell==8&&PlayerAi.Targets(PlayerAi.Order(secondTarget.Units[1])).Length==0,"saved route resumes from next target on next player turn");
        var reordered = PlayerAi.Configure(draft, draft.Revision, "goal", goals: [8,6]).State;
        reordered = PlayerAi.Start(reordered, reordered.Revision).State;
        var priorityStep=Tick(PlayerAi.BeginEnd(reordered,reordered.Revision,turns).State);
        Check(priorityStep.Units[1].Cell==6&&PlayerAi.Targets(PlayerAi.Order(priorityStep.Units[1])).SequenceEqual([8,6]),
            "passing through lower-priority target does not consume it before first target");
        var extraUnit = world.Units[1] with { Id = "unit.extra", Cell = 7 };
        var changedSelection = draft with { Units = draft.Units.Append(extraUnit).ToArray() };
        changedSelection = PlayerAi.Configure(changedSelection, changedSelection.Revision, "units", units: [extraUnit.Id]).State;
        Check(!PlayerAi.Control(changedSelection).Draft.GoalConfirmed, "new selection requires target reconfirmation");
        Reject(() => PlayerAi.Start(changedSelection, changedSelection.Revision), "player_ai.incomplete");
        var casualty = ordered with { Units = ordered.Units.Append(extraUnit).Where(u => u.Id != unit).ToArray(),
            Factions = ordered.Factions.Select(f => f.Id == ordered.ActiveFaction ? f with { PlayerAi = f.PlayerAi! with {
                Draft = f.PlayerAi.Draft with { Units = [unit,extraUnit.Id] } } } : f).ToArray() };
        var repaired = PlayerAi.Reconcile(casualty);
        Check(PlayerAi.Control(repaired).Draft.Units.SequenceEqual([extraUnit.Id]) && PlayerAi.Control(repaired).Draft.UnitsConfirmed,
            "death removes stale IDs and retains surviving selection");
        repaired = PlayerAi.Stop(repaired, repaired.Revision).State;
        repaired = PlayerAi.Configure(repaired, repaired.Revision, "units", units: [extraUnit.Id]).State;
        Check(PlayerAi.Control(repaired).Draft.Units.SequenceEqual([extraUnit.Id]), "selection remains editable after casualty");
        var empty = PlayerAi.Reconcile(casualty with { Units = casualty.Units.Where(u=>u.Id!=extraUnit.Id).ToArray() });
        Check(PlayerAi.Control(empty).Draft.Units.Length==0&&!PlayerAi.Control(empty).Draft.UnitsConfirmed,"all selected units dying invalidates confirmation");
        var staleLoaded=WorldSave.Decode(WorldSave.Encode(casualty),casualty.ContentRevision);
        Check(PlayerAi.Control(staleLoaded).Draft.Units.SequenceEqual([extraUnit.Id]),"load repairs old stale selection");
        var transfer=PlayerAi.Reconcile(ordered with { Units=ordered.Units.Select(u=>u.Id==unit?u with { Owner="loser" }:u).ToArray() });
        Check(PlayerAi.Control(transfer).Draft.Units.Length==0,"ownership transfer removes selection without transferring command");
        var legacySingle=ordered with { Units=ordered.Units.Select(u=>u.PlayerControl==null?u:u with { PlayerControl=u.PlayerControl with { Goals=null } }).ToArray(),
            Factions=ordered.Factions.Select(f=>f.PlayerAi==null?f:f with { PlayerAi=f.PlayerAi with { Draft=f.PlayerAi.Draft with { Goals=null } } }).ToArray() };
        using(var previous=JsonDocument.Parse(WorldSave.Encode(legacySingle))) {
            var v6=previous.RootElement.EnumerateObject().ToDictionary(p=>p.Name,p=>(object?)p.Value.Clone());v6["version"]=6;
            var restored6=WorldSave.Decode(JsonSerializer.SerializeToUtf8Bytes(v6,new JsonSerializerOptions { Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),ordered.ContentRevision);
            Check(PlayerAi.Control(restored6).Enabled&&PlayerAi.Targets(PlayerAi.Order(restored6.Units[1])).SequenceEqual([6]),"v6 single-target migration preserves existing execution and order");
        }
        var skipped = PlayerAi.BeginEnd(stopped, stopped.Revision, turns).State;
        Check(skipped.ActiveFaction != "winner" && skipped.Units.Single(u => u.Id == unit).Cell == 4, "stopped order never acts at end");
        var ended = Finish(pending);
        Check(ended.ActiveFaction == "loser" && !PlayerAi.Control(ended).EndingTurn, "queue finishes before regular end");
        var touched = PlayerAi.MarkAction(enabled, unit);
        Check(!touched.Units[1].Acted, "player action does not overwrite combat Acted");
        var skippedQueue = PlayerAi.BeginEnd(touched, touched.Revision, turns).State;
        Check(PlayerAi.Control(skippedQueue).Queue.Length == 0 && Finish(skippedQueue).Units.Single(u => u.Id == unit).Cell == 4, "player touched unit skipped");
        var moved = WorldActions.Move(world, world.Revision, unit, 5, ai.Movement).State;
        var history = new MovementHistory();history.Record(world, moved, unit, [4,5]);
        moved = PlayerAi.MarkAction(moved, unit);
        Check(PlayerAi.Order(moved.Units[1]).ActionRound == moved.Round && !moved.Units[1].Acted, "manual movement records action despite Acted false");
        var undo = history.Undo(moved, unit).Change.State;
        Check(PlayerAi.Order(undo.Units[1]).ActionRound == 0, "undo restores earlier player-action marker");
        var noPath = enabled with { Cells = enabled.Cells.Select((c,i) => i == 6 ? c with { Terrain = c.Terrain with { Sea = true } } : c).ToArray() };
        var blockedEnd = Finish(PlayerAi.BeginEnd(noPath, noPath.Revision, turns).State);
        Check(blockedEnd.ActiveFaction == "loser" && PlayerAi.Order(blockedEnd.Units.Single(u => u.Id == unit)).Goal == 6, "unreachable target retained without stalling");
        foreach (string policy in PlayerAi.Policies)
        {
            var configured = Configure(world, policy, 4); configured = PlayerAi.Start(configured, configured.Revision).State;
            var completed = Finish(PlayerAi.BeginEnd(configured, configured.Revision, turns).State);
            Check(completed.ActiveFaction == "loser", policy + " completes");
            Check(completed.Units.Single(u => u.Id == unit).Behavior == world.Units[1].Behavior, policy + " preserves original behavior");
            if (policy is "manual" or "hold") Check(completed.Units.Single(u => u.Id == unit).Cell == 4, policy + " stays in place");
            if (policy == "retreat") Check(completed.Units.Single(u => u.Id == unit).Cell > 4, "retreat moves away from enemy without deleting unit");
        }
        var automatic = Configure(world, "hold");automatic = PlayerAi.Configure(automatic, automatic.Revision, "auto", autoBattle:true).State;
        Check(!PlayerAi.Automatic(automatic), "auto option alone never starts");
        automatic = PlayerAi.Start(automatic, automatic.Revision).State;
        Check(PlayerAi.Automatic(automatic) && PlayerAi.Order(automatic.Units[1]).Policy == "hold", "auto preserves explicit policy");
        var manualAuto = Configure(world, "manual", 4);
        manualAuto = PlayerAi.Configure(manualAuto, manualAuto.Revision, "auto", autoBattle: true).State;
        manualAuto = PlayerAi.Start(manualAuto, manualAuto.Revision).State;
        var manualPending = PlayerAi.BeginEnd(manualAuto, manualAuto.Revision, turns).State;
        Check(PlayerAi.PlanStep(manualPending, ai).Plan != null && PlayerAi.Order(manualPending.Units[1]).Policy == "manual",
            "auto temporarily plans for manual units without changing saved policy");
        var manualStopped = PlayerAi.Stop(manualPending, manualPending.Revision).State;
        Check(!PlayerAi.Managed(manualStopped, manualStopped.Units[1]) && PlayerAi.Order(manualStopped.Units[1]).Policy == "manual",
            "stop restores effective player command after temporary auto");
        var duringEnemy = automatic with { ActiveFaction = "loser" };
        var stopEnemy = PlayerAi.Stop(duringEnemy, duringEnemy.Revision, "winner").State;
        Check(stopEnemy.ActiveFaction == "loser" && !stopEnemy.Factions[1].PlayerAi!.Enabled, "global auto stops during enemy turn without rewinding");
        using var old = JsonDocument.Parse(WorldSave.Encode(world));
        var envelope = old.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone());envelope["version"] = 5;
        var migrated = WorldSave.Decode(JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), world.ContentRevision);
        Check(PlayerAi.Order(migrated.Units[1]).ActionRound == migrated.Round && !PlayerAi.Control(migrated).Enabled, "old save conservatively suppresses current-turn automation");

        string assets = Path.Combine(root,"rewrite/data/original"), folder=Path.Combine(root,"build/player-ai-20261026/verify-profile");
        byte[] Read(string path) => File.ReadAllBytes(path.StartsWith("original/",StringComparison.Ordinal)?Path.Combine(assets,path[9..]):Path.Combine(root,"rewrite/data/legacy",path[5..]));
        var content = new ClientContent(Read,Directory.EnumerateFiles(assets,"*.btl"));var host=new ClientGame(content,folder);
        JsonElement Call(string route,object command,bool ok=true)
        {using var doc=JsonDocument.Parse(host.Dispatch("/api/"+route,JsonSerializer.Serialize(command,ClientGame.Json)));Check(doc.RootElement.GetProperty("ok").GetBoolean()==ok,route+" status");return doc.RootElement.GetProperty(ok?"data":"code").Clone();}
        var view=Call("start",new{scenario="conquest1",country="fra"});
        long Revision()=>view.GetProperty("revision").GetInt64();
        view=Call("action",new{op="state"});string player=view.GetProperty("playerFaction").GetString()!;
        var own=view.GetProperty("units").EnumerateArray().First(u=>u.GetProperty("owner").GetString()==player);
        Check(!own.GetProperty("aiControlled").GetBoolean(),"real opening units are manual");
        string ownId=own.GetProperty("id").GetString()!;int ownCell=own.GetProperty("cell").GetInt32();
        Check(Call("action",new{op="player_ai_start",revision=Revision()},false).GetString()=="player_ai.incomplete","real API rejects missing steps");
        view=Call("action",new{op="player_ai_config",offer="units",units=new[]{ownId},revision=Revision()});
        view=Call("action",new{op="player_ai_config",offer="policy",policy="hold",revision=Revision()});
        Check(Call("action",new{op="player_ai_start",revision=Revision()},false).GetString()=="player_ai.incomplete","real API rejects missing target");
        view=Call("action",new{op="player_ai_config",offer="goal",goals=new[]{ownCell,ownCell+1},revision=Revision()});
        view=Call("action",new{op="player_ai_start",revision=Revision()});
        Check(view.GetProperty("playerAi").GetProperty("enabled").GetBoolean(),"real API starts after confirmed steps");
        Check(view.GetProperty("units").EnumerateArray().Single(u=>u.GetProperty("id").GetString()==ownId).GetProperty("aiGoals").GetArrayLength()==2,
            "real API assigns ordered unit targets");
        Call("save",new{slot=1,mode=2});view=Call("load",new{slot=1,mode=2});
        Check(view.GetProperty("playerAi").GetProperty("enabled").GetBoolean(),"real session save retains execution gate");
        Check(view.GetProperty("playerAi").GetProperty("draft").GetProperty("goals").GetArrayLength()==2,"real session save retains target priority");
        var secondOwn=view.GetProperty("units").EnumerateArray().First(u=>u.GetProperty("owner").GetString()==player
            && u.GetProperty("id").GetString()!=ownId && u.GetProperty("canMove").GetBoolean());
        view=Call("action",new{op="player_ai_config",offer="task_add",revision=Revision()});
        view=Call("action",new{op="player_ai_config",offer="mission",kind="defend",revision=Revision()});
        view=Call("action",new{op="player_ai_config",offer="policy",policy="hold",revision=Revision()});
        view=Call("action",new{op="player_ai_config",offer="units",units=new[]{secondOwn.GetProperty("id").GetString()},revision=Revision()});
        view=Call("action",new{op="player_ai_config",offer="goal",cell=secondOwn.GetProperty("cell").GetInt32(),revision=Revision()});
        view=Call("action",new{op="player_ai_start",revision=Revision()});
        Check(view.GetProperty("playerAi").GetProperty("tasks").EnumerateArray().All(t=>t.GetProperty("enabled").GetBoolean()),"real API runs attack and defense tasks together");
        Call("save",new{slot=1,mode=2});
        var branches = new List<byte[]>();
        foreach (string op in new[] { "ai", "ai_batch" })
        {
            string branch = Path.Combine(folder, op), sourceSlot = Path.Combine(folder, "saves/mode-2/slot-1.json"), branchSlot = Path.Combine(branch, "saves/mode-2/slot-1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(branchSlot)!); File.Copy(sourceSlot, branchSlot, true);
            var other = new ClientGame(content, branch);
            JsonElement Other(string route, object command)
            { using var result = JsonDocument.Parse(other.Dispatch("/api/"+route, JsonSerializer.Serialize(command, ClientGame.Json)));Check(result.RootElement.GetProperty("ok").GetBoolean(),op+" "+route);return result.RootElement.GetProperty("data").Clone(); }
            var state = Other("load",new{slot=1,mode=2});
            state = Other("action",new{op="end",revision=state.GetProperty("revision").GetInt64()});
            Check(state.GetProperty("playerAi").GetProperty("endingTurn").GetBoolean(),op+" waits before faction handoff");
            int count = 0;
            while(state.GetProperty("activeFaction").GetString()==player && count++<20)
            {
                state=Other("action",new{op,revision=state.GetProperty("revision").GetInt64()});
                if(op=="ai_batch"){Check(state.GetProperty("error").ValueKind==JsonValueKind.Null,"batch has no partial error");state=state.GetProperty("state");}
            }
            Check(count<20&&state.GetProperty("activeFaction").GetString()!=player,op+" completes player queue");
            Other("save",new{slot=2,mode=2});
            var saved=JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(Path.Combine(branch,"saves/mode-2/slot-2.json")),ClientGame.Json)!;
            branches.Add(saved.Session);
        }
        Check(branches[0].SequenceEqual(branches[1]),"real ordinary and batch AI preserve identical session, world and both RNGs");
        view=Call("action",new{op="player_ai_stop",revision=Revision()});
        Check(view.GetProperty("playerAi").GetProperty("enabled").GetBoolean(),"real API stop preserves other task execution");
        view=Call("action",new{op="player_ai_config",offer="task_select",taskId=1,revision=Revision()});
        view=Call("action",new{op="player_ai_stop",revision=Revision()});
        Check(!view.GetProperty("playerAi").GetProperty("enabled").GetBoolean(),"real API stops execution");
        var presentation=new AiControlPresentation();
        AiControlPresentation.Validate(presentation,path=>File.ReadAllBytes(Path.Combine(root,"rewrite/data",path)));
        Check(true,"presentation defaults resolve real PNG assets and arrow");
        var sample = ExtensionLoader.ParseJson<AiControlPresentation>(File.ReadAllBytes(Path.Combine(root,"rewrite/mod-kit/examples/ai-control/rewrite/ai-control.json")));
        AiControlPresentation.Validate(sample,path=>File.ReadAllBytes(Path.Combine(root,"rewrite/data",path)));
        Check(sample.ConfirmText=="确认"&&sample.ConfirmWidth==112,"text confirmation sample validates");
        byte[] ReadSample(string path) => File.ReadAllBytes(Path.Combine(root,path.StartsWith("rewrite/",StringComparison.Ordinal)?"rewrite/mod-kit/examples/ai-control":"rewrite/data",path));
        var extension=ExtensionLoader.Load(ReadSample);
        Check(extension.ApiVersion==3&&extension.AiControl.ConfirmText=="确认", "Mod manifest loads AI presentation entry");
        Reject(()=>AiControlPresentation.Validate(presentation with{PanelImage="client/../../escape.png"},_=>[]),"extension.ai_control");
        return checks + PlayerAiTaskChecks.Run();
    }
}
