using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class FeedbackUndoChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Feedback/undo: " + label); }
        void Blocked(MovementHistory history, WorldState world, string unit)
        {
            Check(history.Available(world, unit) == null, "blocked move is not offered");
            try { history.Undo(world, unit); } catch (RuleViolation e) when (e.Code == "client.undo_move") { checks++; return; }
            throw new InvalidOperationException("Blocked movement was reversed");
        }
        var basis = WorldChecks.Example();
        var own = basis.Units[1] with { Cell = 0, RemainingMovement = 12,
            Combatant = basis.Units[1].Combatant with { Profile = basis.Units[1].Combatant.Profile with { Movement = 12 } } };
        var state = basis with { Width = 5, Height = 3, Units = [own, basis.Units[0] with { Cell = 14 }],
            Cells = Enumerable.Range(0, 15).Select(i => new CellState { Owner = i == 0 ? "winner" : "loser",
                BuildingKind = i is 2 or 14 ? "city" : null }).ToArray() };
        var rules = new WorldMoveRules { SpendAllMovement = false };
        var history = new MovementHistory();
        foreach (int marker in new[] { 0, 1, 2 })
        {
            var before = state with { Cells = state.Cells.Select((c, i) => i == 2
                ? c with { Objective = marker == 1, BlocksTerritoryExpansion = marker != 0 } : c).ToArray() };
            var moved = WorldActions.Move(before, before.Revision, own.Id, 2, rules);
            var path = WorldActions.Reachable(before, own.Id, rules)[2].Path.ToArray();
            history.Reset(before); history.Record(before, moved.State, own.Id, path, moved.Notices);
            if (marker == 0) Check(history.Undo(moved.State, own.Id).Change.State.Units.Single(u => u.Id == own.Id).Cell == 0, "ordinary city without event remains reversible");
            else
            {
                Blocked(history, moved.State, own.Id);
                var loaded = new MovementHistory();loaded.Load(history.Save(), moved.State);Blocked(loaded, moved.State, own.Id);
                var earlier = before with { Cells = before.Cells.Select((c,i) => i == 2 ? c with { Objective = false, BlocksTerritoryExpansion = false } : c).ToArray() };
                var legacy = new MovementHistory();legacy.Reset(earlier);legacy.Record(earlier, moved.State, own.Id, path);
                var restored = new MovementHistory();restored.Load(legacy.Save(), moved.State);Blocked(restored, moved.State, own.Id);
            }
        }
        var first = WorldActions.Move(state, state.Revision, own.Id, 1, rules);
        history.Reset(state);history.Record(state, first.State, own.Id, [0,1]);
        var marked = first.State with { Cells = first.State.Cells.Select((c,i) => i == 2 ? c with { Objective = true } : c).ToArray() };
        var second = WorldActions.Move(marked, marked.Revision, own.Id, 2, rules);
        history.Record(marked, second.State, own.Id, [1,2]);
        Check(history.Save().Moves.Length == 0, "objective capture removes earlier moves for that unit");
        var third = WorldActions.Move(second.State, second.State.Revision, own.Id, 3, rules);
        history.Record(second.State, third.State, own.Id, [2,3]);
        var returned = history.Undo(third.State, own.Id).Change.State;
        Check(returned.Units.Single(u => u.Id == own.Id).Cell == 2, "later normal move can return only to captured objective");
        Blocked(history, returned, own.Id);

        foreach (bool native in new[] { true, false })
        {
            var before = native ? state with { Events = [new() { Id = "capture", DialogueId = "capture-dialogue",
                Rules = [new() { Trigger = TriggerKind.CellCaptured, TriggerCell = 2 }] }] }
                : state with { ScenarioPlayerFaction = "winner", ScenarioEvents = [new() { Id = "capture", Condition = new() { Type = "cells_owned", Faction = "winner", Cells = [2] },
                    Effects = [new() { Type = "money", Faction = "winner", Value = 7 }] }] };
            var moved = WorldActions.Move(before, before.Revision, own.Id, 2, rules);
            history.Reset(before);history.Record(before, moved.State, own.Id, [0,1,2], moved.Notices);
            Check(moved.Notices.Any(n => n.Kind == (native ? "event.triggered" : "scenario.triggered")), "capture fires actual event");
            var after = native ? WorldEvents.Acknowledge(moved.State, "capture-dialogue") : moved.State;
            Blocked(history, after, own.Id);
            var loaded = new MovementHistory();loaded.Load(history.Save(), after);Blocked(loaded, after, own.Id);
        }
        var ordinary = WorldActions.Move(state, state.Revision, own.Id, 2, rules);
        history.Reset(state);history.Record(state, ordinary.State, own.Id, [0,1,2]);
        history.Attack(own.Id);
        history.Record(ordinary.State, ordinary.State, own.Id, [2], [new("scenario.triggered", "repeat")]);
        Check(history.Save().Moves.Length == 0, "recurring event notice clears undo even without a new event ID");
        Check(history.Save().Attackers.Contains(own.Id), "event barrier preserves same-turn attack lock");
        Blocked(history, ordinary.State with { Phase = TurnPhase.Finished, Outcome = BattleOutcome.Victory }, own.Id);

        byte[] Read(string p) => File.ReadAllBytes(p.StartsWith("data/") ? Path.Combine(root,"rewrite/data/legacy",p[5..]) : Path.Combine(root,"rewrite/data",p));
        var content = new ClientContent(Read, ["campaign1_01.btl", "conquest1.btl"]);
        string folder = Path.Combine(root,"build/feedback-undo-fix-20261022/client",Guid.NewGuid().ToString("N"));
        var host = new ClientGame(content,folder);
        JsonElement Call(string route, object args)
        {
            using var reply = JsonDocument.Parse(host.Dispatch("/api/"+route,JsonSerializer.Serialize(args,ClientGame.Json)));
            Check(reply.RootElement.GetProperty("ok").GetBoolean(),route+" succeeded");return reply.RootElement.GetProperty("data").Clone();
        }
        Call("start",new {scenario="campaign1_01"});Call("save",new {mode=1,slot=1});
        Check(Call("saves",new {mode=1})[1].GetProperty("country").GetString()=="fra","new campaign slot displays actual player country");
        string save = Path.Combine(folder,"saves/mode-1/slot-1.json");
        var slot = JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(save),ClientGame.Json)!;
        Check(slot.Country==null && slot.DisplayCountry=="fra","display metadata preserves campaign start selection");
        File.WriteAllBytes(save,JsonSerializer.SerializeToUtf8Bytes(slot with {DisplayCountry=null},ClientGame.Json));
        byte[] old = File.ReadAllBytes(save);
        Check(Call("saves",new {mode=1})[1].GetProperty("country").GetString()=="fra","old campaign slot derives player country");
        Check(old.SequenceEqual(File.ReadAllBytes(save)),"listing old slots does not rewrite them");
        host=new(content,folder);Call("load",new {mode=1,slot=1});Call("save",new {mode=1,slot=2});
        Check(Call("saves",new {mode=1})[2].GetProperty("country").GetString()=="fra","old slot load and resave retain country");
        foreach (int mode in new[] { 0, 1, 2, 3, 4 })
        {
            var fixture = content.Start("conquest1","fra");var world = fixture.Session.World;
            var mover = world.Units.First(u => u.Owner == world.ActiveFaction && WorldActions.Reachable(world,u.Id,fixture.Rules.Movement)
                .Keys.Any(c => c != u.Cell && !world.Units.Any(n => n.Cell == c)));
            int target = WorldActions.Reachable(world,mover.Id,fixture.Rules.Movement).Keys.First(c => c != mover.Cell && !world.Units.Any(n => n.Cell == c));
            string enemy = world.Factions.First(f => f.Camp != world.Factions.Single(p => p.Id == world.ActiveFaction).Camp && !f.Defeated).Id;
            world = world with { Cells = world.Cells.Select((c,i) => i == target
                ? c with { Owner=enemy,BuildingKind="city",Objective=mode==1,BlocksTerritoryExpansion=mode is 1 or 2 } : c).ToArray(),
                Events = mode == 3 ? [new() {Id="fixture.capture",DialogueId="fixture.dialogue",Rules=[new() {Trigger=TriggerKind.CellCaptured,TriggerCell=target}]}] : [],
                TriggeredEvents=[],PendingDialogue=null,QueuedDialogues=[],ScenarioEvents=mode == 4 ? [new() {Id="fixture.capture",Condition=new() {Type="cells_owned",Faction=world.ActiveFaction,Cells=[target]},
                    Effects=[new() {Type="money",Faction=world.ActiveFaction,Value=7}]}] : [],
                ScenarioPlayerFaction=fixture.Session.PlayerFaction,
                TriggeredScenarioEvents=[],ScenarioChoices=new Dictionary<string,string>(),ScenarioEventProgress=new Dictionary<string,ScenarioEventProgress>() };
            string caseFolder=Path.Combine(folder,"capture-"+mode);string caseSave=Path.Combine(caseFolder,"saves/mode-2/slot-1.json");
            Directory.CreateDirectory(Path.GetDirectoryName(caseSave)!);
            File.WriteAllBytes(caseSave,JsonSerializer.SerializeToUtf8Bytes(new ClientSlot("conquest1","fra",SessionSave.Encode(fixture.Session with {World=world})),ClientGame.Json));
            host=new(content,caseFolder);var loaded=Call("load",new {mode=2,slot=1});
            var captured=Call("action",new {op="move",unit=mover.Id,cell=target,revision=loaded.GetProperty("revision").GetInt64()});
            if(mode==3)captured=Call("action",new {op="dialogue",revision=captured.GetProperty("revision").GetInt64()});
            Check(Call("action",new {op="select",cell=target,revision=captured.GetProperty("revision").GetInt64()}).GetProperty("undo").GetBoolean()==(mode==0),"actual host capture restriction "+mode);
            Call("save",new {mode=2,slot=1});host=new(content,caseFolder);loaded=Call("load",new {mode=2,slot=1});
            Check(Call("action",new {op="select",cell=target,revision=loaded.GetProperty("revision").GetInt64()}).GetProperty("undo").GetBoolean()==(mode==0),"host capture restriction survives restart "+mode);
            using var attempted=JsonDocument.Parse(host.Dispatch("/api/action",JsonSerializer.Serialize(new {op="undo",unit=mover.Id,revision=loaded.GetProperty("revision").GetInt64()},ClientGame.Json)));
            Check(attempted.RootElement.GetProperty("ok").GetBoolean()==(mode==0),"direct API respects capture restriction "+mode);
            if(mode!=0)Check(attempted.RootElement.GetProperty("code").GetString()=="client.undo_move","blocked API supplies stable undo error");
        }
        return checks;
    }
}
