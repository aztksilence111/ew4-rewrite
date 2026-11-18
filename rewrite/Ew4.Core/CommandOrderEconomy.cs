using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed partial class RuleEventBus
{
    private readonly Dictionary<string,WorldTurnRules> orderEconomy = new(StringComparer.Ordinal);
    public void RegisterOrderEconomy(string scenario, WorldTurnRules rules) => orderEconomy[scenario]=rules;
    internal Resources OrderIncome(WorldState w,int cell) => orderEconomy.TryGetValue(w.ScenarioId,out var rules)
        ? rules.Income(w.Cells[cell],cell) : new(0,0,0);
}

public static partial class CommandOrders
{
    public static Resources Income(WorldState w, int cell, Resources original, Resources income, RuleEventBus? bus)
    {
        if (bus?.OrdersEnabled != true) return income;
        var effects = new Dictionary<(string Skill, string Resource), long>();
        double production = 1;
        foreach (var source in w.Units.Where(u => u.Cell == cell && u.Owner == w.Cells[cell].Owner))
        {
            production = Math.Max(production, source.Support.IncomeMultiplier);
            foreach (var (skill, _, r, _) in BoundRules(source, bus))
            {
                if (r.Plan == "teaching_income") production = Math.Max(production, 1.2);
                if (r.Mode == "income" && r.Event == "combat" && Matches(w, source, source, r.Source)) Add(skill, r);
            }
            foreach (var grant in source.Orders.Where(g => g.Cell == cell && Live(w, source, g, bus)
                && Epoch(w,g.Owner)+1<g.Epoch+g.Lifetime))
            {
                var skill = bus.Skills[grant.Skill]; var r = GrantRule(skill, grant);
                if (r.Income > 0) Add(skill, r);
            }
        }
        income = new(Math.Max(income.Money, (long)Math.Floor(original.Money * production)),
            Math.Max(income.Industry, (long)Math.Floor(original.Industry * production)),
            Math.Max(income.Food, (long)Math.Floor(original.Food * production)));
        return new(checked(income.Money + effects.Where(p => p.Key.Resource == "money").Sum(p => p.Value)),
            checked(income.Industry + effects.Where(p => p.Key.Resource == "industry").Sum(p => p.Value)),
            checked(income.Food + effects.Where(p => p.Key.Resource == "food").Sum(p => p.Value)));
        void Add(ExtensionSkill skill, CommandOrder r)
        {
            long basis = r.Resource == "money" ? original.Money : r.Resource == "industry" ? original.Industry : original.Food;
            long amount = Math.Min(r.IncomeCap, checked((long)Math.Floor(basis * r.Income)));
            var key = (skill.Id, r.Resource ?? "food"); effects[key] = Math.Max(effects.GetValueOrDefault(key), amount);
        }
    }

    public static Resources Discount(WorldState w, UnitState target, string action, Resources original, Resources native, RuleEventBus? bus)
    {
        if (bus?.OrdersEnabled != true) return native;
        double money = 0, industry = 0, food = 0;
        void Add(CommandOrder r) { money = Math.Max(money, r.Bonus.MoneyDiscount); industry = Math.Max(industry, r.Bonus.IndustryDiscount); food = Math.Max(food, r.Bonus.FoodDiscount); }
        foreach (var source in w.Units.Where(s => s.Owner == target.Owner))
            foreach (var (_, _, r, _) in BoundRules(source, bus).Where(p => p.Rule.Mode == "discount"))
            {
                if (!(r.Resource == action || r.Resource == "construction" && action == "fort") || r.Event != "combat" || !Matches(w, source, source, r.Source)
                    || !Matches(w, source, target, r.Recipients) || Distance(w, source.Cell, target.Cell) > r.Recipients.Radius) continue;
                Add(r);
            }
        foreach (var g in target.Orders.Where(g => Live(w, target, g, bus)))
        {
            var r = GrantRule(bus.Skills[g.Skill], g);
            if (r.Mode == "discount" && r.Resource == action && Unit(w, g.Source) is { } source
                && Distance(w, source.Cell, target.Cell) <= r.Recipients.Radius) Add(r);
        }
        long Price(long value, long existing, double reduction) => Math.Max(value>0 && action is "construction" or "fort" or "building_upgrade"?1:0,
            Math.Min(existing, checked((long)Math.Floor(value * (1 - reduction)))));
        return new(Price(original.Money, native.Money, money), Price(original.Industry, native.Industry, industry), Price(original.Food, native.Food, food));
    }

    public static int SplashDamage(WorldState w, UnitState target, int damage, RuleEventBus bus) =>
        BoundRules(target, bus).Any(p => p.Rule.Plan == "splash_protection" && Matches(w, target, target, p.Rule.Source))
            ? (int)Math.Floor(damage * .75) : damage;

    private static WorldState ImmediateIncome(WorldState w,UnitState source,CommandOrder r,RuleEventBus bus)
    {
        var basis=bus.OrderIncome(w,source.Cell);
        long value=r.Resource=="money"?basis.Money:r.Resource=="industry"?basis.Industry:basis.Food;
        long amount=Math.Min(r.IncomeCap,(long)Math.Floor(value*r.Income));
        return w with { Factions=w.Factions.Select(f=>f.Id!=source.Owner?f:f with { Resources=new(
            checked(f.Resources.Money+(r.Resource=="money"?amount:0)),
            checked(f.Resources.Industry+(r.Resource=="industry"?amount:0)),
            checked(f.Resources.Food+(r.Resource=="food"?amount:0))) }).ToArray() };
    }
}
