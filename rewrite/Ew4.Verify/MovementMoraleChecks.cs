using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class MovementMoraleChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new InvalidOperationException("Movement morale: " + label); }
        var basis = WorldChecks.Example();
        var map = new HexMap(7, 7);
        foreach (int center in new[] { 24, 31 })
        for (int direction = 0; direction < 6; direction++)
        foreach (var effect in new[] { "normal", "leadership", "temporary", "shortage", "surround" })
        {
            int destination = map.Neighbor(center, direction);
            int opposite = map.Neighbor(center, (direction + 3) % 6);
            int start = map.Neighbor(destination, direction);
            var defender = basis.Units[0] with { Cell = center,
                Morale = effect == "temporary" ? MoraleState.Set(1, false) : new(),
                Combatant = basis.Units[0].Combatant with { Morale = effect == "temporary" ? 1 : 0,
                    Skills = effect == "leadership" ? [Ew4Combat.Skill(3)] : [] } };
            var mover = basis.Units[1] with { Cell = start, RemainingMovement = 6 };
            var allies = (effect == "surround" ? Enumerable.Range(0, 6).Where(d => d != direction)
                .Select(d => map.Neighbor(center, d)) : new[] { opposite }).Select((cell, i) =>
                mover with { Id = "flanker." + i, Cell = cell }).ToArray();
            var cells = Enumerable.Range(0, map.Count).Select(_ => new CellState { Owner = mover.Owner }).ToArray();
            cells[center] = cells[center] with { Owner = defender.Owner };
            var state = basis with { Width = map.Width, Height = map.Height, Cells = cells,
                Units = new[] { defender, mover }.Concat(allies).ToArray(),
                Factions = basis.Factions.Select(f => f.Id == defender.Owner && effect == "shortage" ? f with { Shortage = 2 } : f).ToArray() };
            byte[] before = WorldSave.Encode(state);
            var moved = WorldActions.Move(state, state.Revision, mover.Id, destination,
                new() { SpendAllMovement = false }).State;
            var moveRules = new WorldMoveRules { SpendAllMovement = false };
            using (PlanningScope.Begin(new WorldAiRules { Movement = moveRules }))
            {
                var projected = WorldActions.ProjectMove(state, mover.Id, destination, moveRules,
                    WorldActions.MovementRange(state, mover, moveRules)).State;
                Check(WorldSave.Encode(projected).SequenceEqual(WorldSave.Encode(moved)), "AI movement projection agrees with committed morale");
            }
            var target = moved.Units.Single(u => u.Id == defender.Id);
            int penalty = effect == "surround" ? 2 : 1;
            int expected = effect is "leadership" ? 0 : effect == "temporary" ? 1 : effect == "shortage" ? -2 : -penalty;
            Check(target.LocalPenalty == penalty && target.Morale.Effective == expected && target.Combatant.Morale == expected,
                $"immediate {effect} entry at {center}/{direction}");
            Check(target.Morale.Baseline == defender.Morale.Baseline && target.Morale.Duration == defender.Morale.Duration,
                "local recalculation preserves timed morale");
            Check(before.SequenceEqual(WorldSave.Encode(state)), "movement leaves source snapshot untouched");
            var restored = WorldSave.Decode(WorldSave.Encode(moved), state.ContentRevision);
            var loadedTarget = restored.Units.Single(u => u.Id == defender.Id);
            Check(loadedTarget.Morale == target.Morale && loadedTarget.LocalPenalty == target.LocalPenalty
                && loadedTarget.Combatant.Morale == target.Combatant.Morale, "morale survives save/load");
            var released = WorldActions.Move(restored, restored.Revision, mover.Id, start,
                new() { SpendAllMovement = false }).State.Units.Single(u => u.Id == defender.Id);
            int remaining = effect == "surround" ? 1 : 0;
            int recovered = effect == "leadership" ? 0 : effect == "temporary" ? 1 : effect == "shortage" ? -1 : -remaining;
            Check(released.LocalPenalty == remaining && released.Morale.Effective == recovered && released.Combatant.Morale == recovered,
                "leaving recomputes remaining flank and shortage");
        }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data",
            path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, Directory.EnumerateFiles(Path.Combine(root, "rewrite/data/original"), "*.btl"));
        var scenario = content.Scenarios.Single(s => s.Id == "campaign3_08");
        var campaign = content.Start(scenario.Id, null);
        var world = campaign.Session.World;
        var campaignMap = new HexMap(world.Width, world.Height);
        var targetGeneral = world.Units.Single(u => u.Id == "campaign3_08.unit.54");
        Check(scenario.Name == "重建帝国" && campaign.Session.PlayerFaction.EndsWith(".pru"), "actual Prussian campaign imported");
        Check(targetGeneral.CommanderId != null && !targetGeneral.Combatant.Has(Ew4Combat.Skill(3)), "actual Austrian general has no leadership exemption");
        int left = campaignMap.Neighbor(targetGeneral.Cell, 3), right = campaignMap.Neighbor(targetGeneral.Cell, 0);
        int approach = campaignMap.Neighbor(right, 5);
        var flank = world.Units.First(u => u.Owner == campaign.Session.PlayerFaction && u.Combatant.Profile.Category == "cavalry") with { Cell = left };
        var infantry = world.Units.First(u => u.Owner == campaign.Session.PlayerFaction && u.Combatant.Profile.Category == "infantry")
            with { Cell = approach };
        var fixtureCells = world.Cells.Select((c, i) => i == left || i == approach
            ? c with { Owner = campaign.Session.PlayerFaction } : c).ToArray();
        // Recreate the screenshot's flank geometry with the actual scenario's map and units.
        world = world with { Round = 3, Phase = TurnPhase.Active, PendingDialogue = null,
            Units = [targetGeneral, flank, infantry], Cells = fixtureCells };
        Check(WorldTurns.LocalPenalty(world, campaignMap, targetGeneral.Cell, targetGeneral.Owner, campaign.Rules.Movement.NeutralCamps) == 0,
            "screenshot fixture begins without a flank");
        string folder = Path.Combine(root, "build/movement-morale-20261030/profile");
        Directory.CreateDirectory(Path.Combine(folder, "saves/mode-1"));
        var slot = new ClientSlot(scenario.Id, null, SessionSave.Encode(campaign.Session with { World = world }));
        File.WriteAllBytes(Path.Combine(folder, "saves/mode-1/slot-0.json"), JsonSerializer.SerializeToUtf8Bytes(slot, ClientGame.Json));
        File.WriteAllBytes(Path.Combine(folder, "account.json"), JsonSerializer.SerializeToUtf8Bytes(campaign.Session.Account, ClientGame.Json));
        var host = new ClientGame(content, folder);
        JsonElement Call(string route, object command)
        {
            using var response = JsonDocument.Parse(host.Dispatch("/api/" + route, JsonSerializer.Serialize(command, ClientGame.Json)));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), "actual client " + route + ": " + response.RootElement.GetRawText()[..Math.Min(120, response.RootElement.GetRawText().Length)]);
            return response.RootElement.GetProperty("data").Clone();
        }
        JsonElement Target(JsonElement view) => (view.GetProperty("units").ValueKind == JsonValueKind.Array
            ? view.GetProperty("units") : view.GetProperty("unitUpdates")).EnumerateArray()
            .Single(u => u.GetProperty("id").GetString() == targetGeneral.Id);
        var loadedView = Call("load", new { mode = 1 });
        Check(Target(loadedView).GetProperty("morale").GetInt32() == 0, "client starts with normal target morale");
        var movedView = Call("action", new { op = "move", unit = infantry.Id, cell = right,
            revision = loadedView.GetProperty("revision").GetInt64() });
        Check(Target(movedView).GetProperty("morale").GetInt32() == -1, "client immediately projects the Austrian general's morale drop");
        var undone = Call("action", new { op = "undo", unit = infantry.Id, revision = movedView.GetProperty("revision").GetInt64() });
        Check(Target(undone).GetProperty("morale").GetInt32() == 0, "undo immediately releases flank morale");
        var repeated = Call("action", new { op = "move", unit = infantry.Id, cell = right,
            revision = undone.GetProperty("revision").GetInt64(), delta = true });
        Check(Target(repeated).GetProperty("morale").GetInt32() == -1, "incremental client response repeats the flank morale drop");
        Call("save", new { mode = 1, slot = 1 });
        Check(Target(Call("load", new { mode = 1, slot = 1 })).GetProperty("morale").GetInt32() == -1, "actual client save/reload retains flank morale");
        return checks;
    }
}
