using Ew4.Core;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private string? Service(int cell) => content.Battle(Require().Session.World.ScenarioId).Sites
        .FirstOrDefault(s => s.Cell == cell)?.State6 switch { 1 => "trade", 2 => "shop", 3 => "bar", _ => null };

    private void ServiceAccess(int cell, string service)
    {
        var current = Require(); var world = current.Session.World;
        var active = world.Factions.Single(f => f.Id == world.ActiveFaction);
        if (cell < 0 || cell >= world.Cells.Count || world.Phase != TurnPhase.Active || world.PendingDialogue != null
            || active.Id != current.Session.PlayerFaction || !active.Human || !world.Cells[cell].Terrain.Active
            || world.Cells[cell].BuildingKind == null || world.Cells[cell].Owner != active.Id || Service(cell) != service)
            throw new RuleViolation("session.market_access", "Select an owned service location during your turn");
    }

    // Initial-army commanders are absent from the tavern import. Extend only the
    // client lookup so existing scenario/save content fingerprints stay stable.
    private SessionTradingRules TradingRules()
    {
        var current = Require();
        var commerce = current.Rules.Trading.CommanderCommerce.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (army, index) in content.Battle(current.Session.World.ScenarioId).Armies.Select((a, i) => (a, i)))
            if (content.Catalogs.Accounts.CommanderCommerce.TryGetValue(army.Commander, out int level))
                commerce.TryAdd(current.Session.World.ScenarioId + ".unit." + index + ".commander", level);
        return current.Rules.Trading with { CommanderCommerce = commerce };
    }

    private object Exchange(ClientCommand command)
    {
        ServiceAccess(command.Cell, "trade");
        var current = Require(); var session = current.Session; var world = session.World;
        int commerce = SessionActions.CommerceLevel(session, new(command.Cell, new("exchange", [])), TradingRules());
        float rate = .4f * (5 - Math.Min(5, commerce)) + 1f;
        // Native 5a0f8/5a39c and tables 194ef0..194f20, IEEE single precision.
        int[] amounts = [2, 10, 50, 250], money = [10, 50, 10, 50];
        if (command.Op is "buy" or "sell")
        {
            if (command.Revision != session.Revision) throw new RuleViolation("client.stale", "State changed; refresh the command");
            if (!int.TryParse(command.Offer, out int index) || index < 0 || index >= amounts.Length)
                throw new RuleViolation("exchange.offer", "Unknown resource exchange offer");
            bool buy = command.Op == "buy", industry = index < 2;
            long quantity = buy ? amounts[index] : (int)(amounts[index] * rate);
            long payment = buy ? (int)(money[index] * rate) : money[index];
            var faction = world.Factions.Single(f => f.Id == session.PlayerFaction); var funds = faction.Resources;
            if (buy ? funds.Money < payment : (industry ? funds.Industry : funds.Food) < quantity)
                throw new RuleViolation("exchange.resources", "Insufficient resources");
            long delta = buy ? quantity : -quantity;
            var updated = new Resources(checked(funds.Money + (buy ? -payment : payment)),
                checked(funds.Industry + (industry ? delta : 0)), checked(funds.Food + (!industry ? delta : 0)));
            var next = world with { Revision = checked(world.Revision + 1),
                Factions = world.Factions.Select(f => f.Id == faction.Id ? f with { Resources = updated } : f).ToArray() };
            next.Validate(); Commit(new(next, [new WorldNotice("resources.exchanged", faction.Id, command.Cell)]));
            session = Require().Session;
        }
        else if (command.Op != "") throw new RuleViolation("exchange.command", "Unknown exchange operation");
        var resources = session.World.Factions.Single(f => f.Id == session.PlayerFaction).Resources;
        return new { session.Revision, commerce, rate, resources, commander = session.World.Units.FirstOrDefault(u => u.Cell == command.Cell)?.CommanderId,
            offers = amounts.Select((amount, index) => new { id = index.ToString(), resource = index < 2 ? "industry" : "food",
                buyAmount = amount, buyMoney = (int)(money[index] * rate), sellAmount = (int)(amount * rate), sellMoney = money[index],
                canBuy = resources.Money >= (int)(money[index] * rate), canSell = (index < 2 ? resources.Industry : resources.Food) >= (int)(amount * rate) }) };
    }

    private object? AiCombat(WorldState world, AiStepPlan plan)
        => AiCombat(world, plan.Unit);
    private object? AiCombat(WorldState world, AiUnitPlan? plan)
    {
        if (plan == null) return null;
        int best = AiPlanning.Best(plan.Candidates);
        if (best < 0) return null;
        var command = plan.Candidates[best].Action;
        string? target = command switch { AiStrikeCommand strike => strike.TargetId, AiMoveCommand move => move.TargetId, _ => null };
        if (target == null) return null;
        var movedChange = command is AiMoveCommand movement
            ? WorldActions.Move(world, world.Revision, command.UnitId, movement.Destination, Require().Rules.Movement) : null;
        var projected = movedChange?.State ?? world;
        if (projected.Phase == TurnPhase.Finished || projected.PendingDialogue != null) return null;
        var preview = WorldBattle.Preview(projected, projected.Revision, command.UnitId, target, Require().Rules.Battle);
        var movements = movedChange?.Movements ?? [];
        return new { attacker = preview.Attacker, defender = preview.Defender, counterattack = preview.Main.Counterattack, movements,
            damage = preview.Main.DefenderDamage, counterDamage = preview.Main.AttackerDamage,
            critical = preview.Main.OffensiveProc && !preview.Main.DefensiveProc, counterCritical = false,
            splashTarget = preview.SplashTarget, splashDamage = preview.SplashDamage };
    }
}
