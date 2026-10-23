using Ew4.Core;

namespace Ew4.Client;

public sealed record ClientActionQuote(Resources Cost, bool Affordable);

public sealed partial class ClientGame
{
    private ClientActionQuote? ActionQuote(Func<Resources> calculate)
    {
        try
        {
            var cost = calculate();
            var world = Require().Session.World;
            var funds = world.Factions.Single(f => f.Id == world.ActiveFaction).Resources;
            return new(cost, funds.Money >= cost.Money && funds.Industry >= cost.Industry && funds.Food >= cost.Food);
        }
        catch (RuleViolation e) when (e.Code is "training.unit" or "training.unavailable" or "training.stage" or "training.limit"
            or "upgrade.site" or "upgrade.used" or "upgrade.limit" or "purchase.unit" or "purchase.site" or "transport.unavailable" or "installation.unavailable")
        {
            return null;
        }
    }
}
