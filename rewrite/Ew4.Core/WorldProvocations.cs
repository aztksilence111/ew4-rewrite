using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ProvocationRules(string NeutralCamp, IReadOnlyList<string> CombatantCamps);
public sealed record ProvocationDialogue(string Faction, string Kind, int Variant);

public static class WorldProvocations
{
    private static readonly string[] Kinds = ["alliance protest", "alliance neutral", "neutral protest", "neutral declarewar"];

    public static ProvocationDialogue? Dialogue(WorldState state, string id)
    {
        var parts = id.Split('.', 4);
        return parts.Length == 4 && parts[0] == "diplomacy" && Kinds.Contains(parts[1])
            && int.TryParse(parts[2], out int variant) && variant is >= 1 and <= 4
            && state.Factions.Any(f => f.Id == parts[3]) ? new(parts[3], parts[1], variant) : null;
    }

    public static WorldChange Captured(WorldState before, WorldState after, int cell)
    {
        var policy = after.Provocations;
        string? previous = before.Cells[cell].Owner, current = after.Cells[cell].Owner;
        if (policy == null || after.Phase == TurnPhase.Finished || previous == null || current == null || previous == current
            || before.Cells[cell].BuildingKind is null or "farmland") return new(after, []);
        var captor = after.Factions.Single(f => f.Id == current);
        var victim = after.Factions.Single(f => f.Id == previous);
        if (!captor.Human || victim.Defeated || victim.Human && after.LocalMultiplayer == null
            || WorldDiplomacy.Override(after, previous, current) != null) return new(after, []);
        bool allied = victim.Camp == captor.Camp;
        if (!allied && victim.Camp != policy.NeutralCamp) return new(after, []);
        bool scripted = after.TriggeredEvents.Except(before.TriggeredEvents).Any(id => after.Events.Any(e => e.Id == id
            && e.Rules.Any(r => r.Trigger == TriggerKind.CellCaptured && r.TriggerCell == cell)));
        if (scripted && !victim.DiplomacyWarned) return new(after, []);
        return React(after, victim, captor, allied, scripted);
    }

    public static WorldChange Attacked(WorldState state, string attacker, string defender, IReadOnlyCollection<string> neutralCamps)
    {
        var engaged = LocalMultiplayerGame.Engaged(state, attacker, defender, neutralCamps);
        if (!ReferenceEquals(engaged, state))
        {
            string changed = state.Factions.First(f => f.Camp != engaged.Factions.Single(p => p.Id == f.Id).Camp).Id;
            return Talk(engaged, changed, "neutral declarewar");
        }
        if (state.Provocations == null || WorldDiplomacy.Override(state, attacker, defender) != null)
            return new(WorldDiplomacy.Attacked(state, attacker, defender, neutralCamps), []);
        var source = state.Factions.Single(f => f.Id == attacker);
        var target = state.Factions.Single(f => f.Id == defender);
        if (!source.Human || target.Human || target.Camp != state.Provocations.NeutralCamp) return new(state, []);
        return React(state, target, source, false);
    }

    private static WorldChange React(WorldState state, FactionState victim, FactionState opponent, bool allied, bool suppressDialogue = false)
    {
        var rules = state.Provocations!;
        var random = new Ew4Random(state.RandomState);
        bool escalate = victim.DiplomacyWarned && random.Next(100) < (allied ? 30 : 50);
        string camp = victim.Camp;
        if (escalate)
        {
            if (allied) camp = rules.NeutralCamp;
            else
            {
                int index = rules.CombatantCamps.ToList().IndexOf(opponent.Camp);
                int offset = random.Next(100) < 50 ? 1 : 2;
                camp = index >= 0 && rules.CombatantCamps.Count == 3
                    ? rules.CombatantCamps[(index + offset) % 3]
                    : rules.CombatantCamps.FirstOrDefault(c => c != opponent.Camp) ?? victim.Id;
            }
        }
        var next = state with { RandomState = random.State, Factions = state.Factions.Select(f => f.Id == victim.Id
            ? f with { Camp = camp, DiplomacyWarned = !(allied && escalate) } : f).ToArray() };
        // Legacy provocation changes a country's camp, so old bilateral attack markers must not keep it at war after neutrality.
        if (camp != victim.Camp)
            next = next with { NeutralHostilities = next.NeutralHostilities.Where(h => h.First != victim.Id && h.Second != victim.Id).ToArray() };
        if (suppressDialogue) return new(next, []);
        return Talk(next, victim.Id, allied ? escalate ? "alliance neutral" : "alliance protest"
            : escalate ? "neutral declarewar" : "neutral protest");
    }

    private static WorldChange Talk(WorldState state, string faction, string kind)
    {
        var notices = new List<WorldNotice> { new("diplomacy." + kind.Replace(' ', '_'), faction) };
        // Native 5c12c suppresses the speech when the country has no commander.
        if (!state.Units.Any(u => u.Owner == faction && u.CommanderId != null)) return new(state, notices);
        var random = new AiRandom(state.AiRandomState);
        string id = $"diplomacy.{kind}.{random.Next(4) + 1}.{faction}";
        state = state with { AiRandomState = random.State };
        state = state.PendingDialogue == null ? state with { PendingDialogue = id }
            : state with { QueuedDialogues = state.QueuedDialogues.Append(id).ToArray() };
        notices.Add(new("dialogue", id));
        return new(state, notices);
    }
}
