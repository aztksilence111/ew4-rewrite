using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Client;

public sealed record PauseObjectiveRow(string Id, string Text, bool? Met = null, string Detail = "",
    string Relation = "", IReadOnlyList<PauseObjectiveRow>? Children = null);
public sealed record PauseRating(int Stars, int Start, int? End, bool Available);
public sealed record PauseObjectiveModel(PauseObjectiveRow Win, IReadOnlyList<PauseObjectiveRow> Limits,
    IReadOnlyList<PauseRating> Ratings, string Description, string Note);

public static class PauseObjectiveProjection
{
    public static IReadOnlyList<PauseRating> Ratings(int round, int minimum, int highest, int? limit)
    {
        if (minimum < 1 || highest < 0) return [];
        // Find integer boundaries using the settlement function, including skipped star bands.
        int FirstAtMost(int stars)
        {
            int low = 1, high = int.MaxValue;
            while (low < high)
            {
                int middle = low + (int)(((long)high - low) / 2);
                if (BattleSettlement.Stars(middle, minimum, highest) <= stars) high = middle;
                else low = middle + 1;
            }
            return low;
        }
        var result = new List<PauseRating>();
        for (int stars = 5; stars >= 1; stars--)
        {
            int start = stars == 5 ? 1 : FirstAtMost(stars);
            int? end = stars == 1 ? null : FirstAtMost(stars - 1) - 1;
            if (limit.HasValue) end = Math.Min(end ?? int.MaxValue, limit.Value);
            if (end < start || BattleSettlement.Stars(start, minimum, highest) != stars) continue;
            result.Add(new(stars, start, end, !end.HasValue || round <= end.Value));
        }
        return result;
    }

    public static PauseObjectiveModel Build(WorldState source, VictoryRules rules, IReadOnlyCollection<string> neutrals,
        string observer, int minimum, int highest, Func<string, string> label, Func<string, string> factionName)
    {
        var local = source.LocalMultiplayer;
        var custom = source.ScenarioVictory ?? rules.Custom;
        bool neutralPlayer = local?.ShareAlliedVictory == true && WorldDiplomacy.IsNeutral(source, observer, source.DiplomacyNeutralCamps);
        bool Shares(string other) => local == null ? WorldDiplomacy.Allied(source, observer, other)
            : LocalMultiplayerGame.Won(source, observer, other);
        var world = source;
        string camp = world.Factions.Single(f => f.Id == observer).Camp;
        // Match the candidate projection used by the multiplayer winner evaluator.
        if (local != null && custom != null)
        {
            world = source with { LocalMultiplayer = null, ScenarioPlayerFaction = observer,
                DiplomaticRelations = [], NeutralHostilities = [],
                Factions = source.Factions.Select(f => f with { Camp = Shares(f.Id) ? observer : f.Id, Human = f.Id == observer }).ToArray() };
            camp = observer; neutrals = [];
        }
        var facts = WorldVictory.Facts(world, neutrals, custom == null ? rules.UnitObjectiveOwnership : ObjectiveUnitOwnership.UnitOwner);
        var enemies = facts.Where(f => !f.Defeated && f.Combatant && f.Camp != camp).ToArray();
        bool Owned(int cell) => world.Cells[cell].Owner is string owner && (world.ScenarioPlayerFaction is string player
            ? WorldDiplomacy.Allied(world, owner, player) : world.Factions.Single(f => f.Id == owner).Camp == camp);
        string Names(IEnumerable<string> ids) => string.Join("、", ids.Select(factionName));
        PauseObjectiveRow Describe(VictoryCondition c, string id)
        {
            PauseObjectiveRow Leaf(string text, bool? met, string detail = "") => new(id, text, met, detail);
            int count;
            switch (c.Type)
            {
                case "all": case "any":
                    var children = c.Conditions.Select((child, i) => Describe(child, id + "." + i)).ToArray();
                    bool? met = c.Type == "all" ? children.Any(r => r.Met == false) ? false : children.All(r => r.Met == true) ? true : null
                        : children.Any(r => r.Met == true) ? true : children.All(r => r.Met == false) ? false : null;
                    return new(id, c.Type == "all" ? "全部满足" : "满足任一", met, Relation: c.Type, Children: children);
                case "enemy_factions_defeated":
                    count = enemies.Count(f => c.IncludeProvokedNeutrals || !neutrals.Contains(world.Factions.Single(s => s.Id == f.Id).Camp)
                        || world.ScenarioPlayerFaction is string player && WorldDiplomacy.Override(world, player, f.Id) == "hostile");
                    return Leaf("消灭全部参战敌国", count == 0, $"尚余 {count} 个敌对国家");
                case "enemy_objectives_cleared":
                    count = enemies.Sum(f => f.Objectives);
                    return Leaf("清除敌方全部目标", count == 0, $"尚余 {count} 个目标");
                case "factions_defeated":
                    count = c.Factions.Count(id => world.Factions.Any(f => f.Id == id && f.Defeated));
                    return Leaf("消灭指定国家", count == c.Factions.Count, Names(c.Factions) + $"（{count}/{c.Factions.Count}）");
                case "units_destroyed": case "units_lost":
                    count = c.Units.Count(id => world.Units.All(u => u.Id != id));
                    return Leaf(c.Type == "units_lost" ? "任一指定部队被消灭" : "消灭全部指定部队",
                        c.Type == "units_lost" ? count > 0 : count == c.Units.Count, $"{count}/{c.Units.Count}");
                case "cells_owned": case "cells_lost":
                    count = c.Cells.Count(Owned);
                    string sites = string.Join("、", c.Cells.Select(cell => string.IsNullOrWhiteSpace(world.Cells[cell].Name)
                        ? "地格 " + cell : label(world.Cells[cell].Name!)));
                    return Leaf(c.Type == "cells_lost" ? "失去任一指定地点" : "控制全部指定地点",
                        c.Type == "cells_lost" ? count < c.Cells.Count : count == c.Cells.Count, sites + $"（已控制 {count}/{c.Cells.Count}）");
                case "round_reached": return Leaf($"到达第 {c.Round} 回合", world.Round >= c.Round);
                case "resources":
                    bool enough = world.Factions.Where(f => f.Human && f.Camp == camp).Any(f => f.Resources.Money >= c.Resources.Money
                        && f.Resources.Industry >= c.Resources.Industry && f.Resources.Food >= c.Resources.Food);
                    var targets = new List<string>();
                    if (c.Resources.Money > 0) targets.Add("金币 " + c.Resources.Money);
                    if (c.Resources.Industry > 0) targets.Add("工业 " + c.Resources.Industry);
                    if (c.Resources.Food > 0) targets.Add("粮食 " + c.Resources.Food);
                    return Leaf("任一己方人类国家储备达标", enough, string.Join("、", targets));
                case "script": return Leaf("特殊胜利条件", null, "按剧情及规则说明判定");
                default: throw new RuleViolation("pause.condition", "Unsupported victory condition");
            }
        }
        PauseObjectiveRow win;
        var limits = new List<PauseObjectiveRow>();
        string note = "完成胜利条件后，按通关回合评定星级。";
        int? roundLimit = custom == null ? rules.RoundLimit : custom.RoundLimit;
        if (local != null && custom == null)
        {
            bool initial = local.InitialObjectiveOwner is string initialOwner && Shares(initialOwner);
            var owners = source.Cells.Where(c => c.Objective).Select(c => c.Owner)
                .Concat(source.Units.Where(u => u.Objective).Select(u => rules.UnitObjectiveOwnership == ObjectiveUnitOwnership.UnitOwner
                    ? u.Owner : source.Cells[u.Cell].Owner)).ToArray();
            bool controls = owners.Length > 0 && owners.All(o => o != null && Shares(o));
            bool sole = source.Factions.All(f => Shares(f.Id) || f.Defeated
                || local.ShareAlliedVictory && WorldDiplomacy.IsNeutral(source, f.Id, source.DiplomacyNeutralCamps));
            bool targets = !initial && (controls || local.HadObjectives && owners.Length == 0 && observer == source.ActiveFaction);
            win = new("win", "满足任一", !neutralPlayer && (sole || targets), Relation: "any", Children: [
                new("win.survivor", "成为最后存续的获胜方", !neutralPlayer && sole),
                new("win.targets", "控制全部目标", !neutralPlayer && targets,
                    initial ? "初始全部持有不算获胜；目标易手后可重新夺回" : $"已控制 {owners.Count(o => o != null && Shares(o))}/{owners.Length}")]);
            if (neutralPlayer) limits.Add(new("limits.neutral", "中立国家须先加入参战阵营，才可获胜", false));
            limits.Add(new("limits.survival", "本国须保持存续", !source.Factions.Single(f => f.Id == observer).Defeated));
            roundLimit = null;
        }
        else
        {
            win = custom == null ? new("win", rules.ObjectivesEnabled ? "满足任一" : "全部满足", Relation: rules.ObjectivesEnabled ? "any" : "all",
                Children: rules.ObjectivesEnabled ? [Describe(new() { Type = "enemy_factions_defeated" }, "win.enemies"),
                    new("win.objectives", "清除敌方全部目标", enemies.Sum(f => f.Objectives) == 0, $"尚余 {enemies.Sum(f => f.Objectives)} 个目标")]
                    : [Describe(new() { Type = "enemy_factions_defeated" }, "win.enemies")]) : Describe(custom.Win, "win");
            if (local == null)
            {
                if (custom == null || custom.RequirePlayerSurvival)
                    limits.Add(new("limits.survival", "己方人类国家须保持存续", world.Factions.Any(f => f.Human && f.Camp == camp && !f.Defeated)));
                if (custom == null && rules.ObjectivesEnabled)
                    limits.Add(new("limits.targets", "敌方目标尚存时，不得失去全部己方目标", enemies.Sum(f => f.Objectives) == 0
                        || facts.Where(f => !f.Defeated && f.Combatant && f.Camp == camp).Sum(f => f.Objectives) > 0));
                if (custom?.Lose != null)
                    limits.Add(new("limits.custom", "满足以下失败条件即告失败", Children: [Describe(custom.Lose, "lose")], Relation: "failure"));
            }
            else
            {
                roundLimit = null;
                limits.Add(new("limits.survival", "本国须保持存续", !source.Factions.Single(f => f.Id == observer).Defeated));
                if (neutralPlayer) limits.Add(new("limits.neutral", "中立国家须先加入参战阵营，才可获胜", false));
            }
        }
        if (roundLimit.HasValue) limits.Add(new("limits.round", $"不得超过第 {roundLimit.Value} 回合", source.Round <= roundLimit));
        var ratings = local == null ? Ratings(source.Round, minimum, highest, roundLimit) : [];
        if (ratings.Count > 0 && ratings.Any(r => r.Stars == 5 && !r.Available)) note = "满星期限已过，仍可继续争取其他评价。";
        if (ratings.Count > 0 && ratings.All(r => !r.Available)) note = "已超过本局允许通关的回合期限。";
        if (custom?.Lose != null && local == null) note += custom.Priority == "defeat" ? "胜负同时满足时，失败优先。" : "胜负同时满足时，胜利优先。";
        if (local != null) note = local.ShareAlliedVictory ? "战役同盟共享胜利；按各国实际胜负结算。" : "征服各国独立争胜；按当前人类国家展示。";
        return new(win, limits, ratings, label(custom?.DescriptionKey ?? ""), note);
    }
}

public sealed partial class ClientGame
{
    private string? pauseContext, pauseVersion;
    private object? pauseSnapshot;
    private WorldState? pauseWorld;
    private object PauseObjectives(ClientCommand command)
    {
        var current = Require(); var session = current.Session; var world = session.World;
        var scenario = content.Scenarios.Single(s => s.Id == world.ScenarioId);
        string context = session.BattleId + "/" + session.PlayerFaction;
        if (!ReferenceEquals(world, pauseWorld) || pauseContext != context)
        {
            var battle = content.Battle(world.ScenarioId);
            var model = PauseObjectiveProjection.Build(world, current.Rules.Turns.Victory, current.Rules.Turns.NeutralCamps,
                session.PlayerFaction, battle.Header[12], battle.Header[13], content.Label,
                id => world.Factions.SingleOrDefault(f => f.Id == id)?.Name is string name ? content.Label(name) : id);
            var screen = content.Extensions.PauseObjectivesPresentations.GetValueOrDefault(world.ScenarioId)
                ?? content.Extensions.PauseObjectivesPresentations.GetValueOrDefault("*") ?? new();
            var view = new { context, visible = scenario.Mode is 1 or 2, title = content.Label(screen.Title),
                scenario = content.Label(scenario.Name), country = content.Label(world.Factions.Single(f => f.Id == session.PlayerFaction).Name),
                model, presentation = screen };
            string version = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(view, Json))));
            if (version != pauseVersion || pauseContext != context) { pauseSnapshot = view; pauseVersion = version; }
            pauseWorld = world; pauseContext = context;
        }
        return new { context, revision = session.Revision, version = pauseVersion,
            snapshot = command.KnownVersion == pauseVersion ? null : pauseSnapshot };
    }
}
