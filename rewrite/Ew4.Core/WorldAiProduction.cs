using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record ProductionOfferGroup(string Id, IReadOnlyList<string> HighestFirst, bool Naval = false);
public sealed record WorldProductionRules
{
    public IReadOnlyDictionary<string, RecruitmentOffer> Offers { get; init; } = new Dictionary<string, RecruitmentOffer>();
    public IReadOnlyList<ProductionOfferGroup> Groups { get; init; } = Array.Empty<ProductionOfferGroup>();
    public IReadOnlyDictionary<string, RecruitmentOffer> Fortresses { get; init; } = new Dictionary<string, RecruitmentOffer>();
    public IReadOnlyDictionary<string, InstallationBuildRules> Installations { get; init; } = new Dictionary<string, InstallationBuildRules>();
    public AiRecruitRules Recruit { get; init; } = new();
    public AiOfferPoolRules Pool { get; init; } = new();
    public AiUpgradeRules Upgrade { get; init; } = new();
    public AiInstallationRules? InstallationScoring { get; init; }
    public AiFortressRules? FortressScoring { get; init; }
    public BuildingUpgradeRules? BuildingUpgrades { get; init; }
    public TransportPurchaseRules? Transport { get; init; }
    public WorldCommanderRules? Commanders { get; init; }
    public AiCommanderRules CommanderScoring { get; init; } = new();
    public IReadOnlyList<AiTaskWeight> Tasks { get; init; } = new AiTaskWeight[]
    {
        new("recruit", 64), new("naval", 8), new("fortress", 6), new("installation", 9), new("upgrade", 6), new("transport", 7)
    };
    public int PressureRadius { get; init; } = 4;
    public int InstallationRadius { get; init; } = 4;
    public int FortressEnemyRadius { get; init; } = 6;
    public int FortressSupportRadius { get; init; } = 4;
    public string FortressCategory { get; init; } = "fort";
    public IReadOnlyCollection<string> HoldBehaviors { get; init; } = new[] { "3" };

    public void Validate() => PlanningScope.Validate(this, ValidateCore);
    private void ValidateCore()
    {
        Recruit.Validate(); Pool.Validate(); Upgrade.Validate();
        InstallationScoring?.Validate(); FortressScoring?.Validate(); BuildingUpgrades?.Validate(); Transport?.Purchase.Validate();
        Commanders?.Validate(); CommanderScoring.Validate();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (Offers.Any(p => p.Key != p.Value.Id || p.Value.Template == null)
            || Fortresses.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value.Template == null)
            || Groups.Any(g => string.IsNullOrWhiteSpace(g.Id) || !ids.Add(g.Id)
                || g.HighestFirst.Any(id => !Offers.ContainsKey(id)))
            || Groups.SelectMany(g => g.HighestFirst).GroupBy(id => id, StringComparer.Ordinal).Any(g => g.Count() > 1)
            || Tasks.Any(t => !new[] { "recruit", "naval", "fortress", "installation", "upgrade", "transport" }.Contains(t.Id))
            || Math.Min(Math.Min(PressureRadius, InstallationRadius), Math.Min(FortressEnemyRadius, FortressSupportRadius)) < 0
            || string.IsNullOrWhiteSpace(FortressCategory) || HoldBehaviors.Any(string.IsNullOrWhiteSpace)
            || Installations.Any(p => string.IsNullOrWhiteSpace(p.Key) || string.IsNullOrWhiteSpace(p.Value.Kind)
                || p.Value.Builders.Any(string.IsNullOrWhiteSpace))
            || InstallationScoring != null && InstallationScoring.Recipes.Any(r => !Installations.ContainsKey(r.Id))
            || FortressScoring != null && FortressScoring.Recipes.Any(r => !Fortresses.ContainsKey(r.Id))
            || Transport != null && Transport.ExcludedCategories.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("ai.production_configuration", "Invalid world production mappings or neighborhood policy");
        AiProduction.SelectTask(Tasks, _ => 0);
        foreach (var offer in Offers.Values.Concat(Fortresses.Values)) { offer.Validate(); offer.Template!.Validate(); }
        foreach (var installation in Installations.Values) installation.Purchase.Validate();
    }
}

public abstract record WorldProductionCommand(long ExpectedRevision);
public sealed record AiRecruitCommand(long ExpectedRevision, int Cell, string OfferId, string NewUnitId,
    bool Fortress = false, string? RelocateUnitId = null, int? Relocation = null) : WorldProductionCommand(ExpectedRevision);
public sealed record AiInstallationCommand(long ExpectedRevision, string UnitId, string RecipeId) : WorldProductionCommand(ExpectedRevision);
public sealed record AiUpgradeCommand(long ExpectedRevision, int Cell) : WorldProductionCommand(ExpectedRevision);
public sealed record AiTransportCommand(long ExpectedRevision, string UnitId) : WorldProductionCommand(ExpectedRevision);
public sealed record AiProductionPlan(long ExpectedRevision, ulong InitialRandomState, ulong FinalRandomState,
    string? AttemptedTask, WorldProductionCommand? Command);

public static partial class WorldAi
{
    private static bool CanPay(Resources funds, Resources cost) =>
        AiProduction.AffordableCopies(funds, cost) is not 0;

    private static string RecruitIdentity(WorldState state)
    {
        string prefix = "ai.recruit." + state.ActiveFaction + "." + state.Revision;
        var used = new HashSet<string>(state.Units.Select(u => u.Id), StringComparer.Ordinal);
        string id = prefix;
        for (long suffix = 1; used.Contains(id); suffix = checked(suffix + 1)) id = prefix + "." + suffix;
        return id;
    }

    public static AiProductionPlan PlanProduction(WorldState state, WorldAiRules ai, WorldProductionRules production, WorldTurnRules turns)
    {
        var faction = WorldActions.Ready(state, state.Revision);
        ai.Validate(); production.Validate(); turns.Validate();
        var random = new AiRandom(state.AiRandomState);
        var map = new HexMap(state.Width, state.Height);
        var owned = Enumerable.Range(0, state.Cells.Count).Where(i => state.Cells[i].Owner == faction.Id && state.Cells[i].Terrain.Active).ToArray();
        int armySize = state.Units.Count(u => u.Owner == faction.Id);
        int? armyLimit = WorldArmyCapacity.Limit(state, faction.Id);
        var byCell = state.Units.GroupBy(u => u.Cell).ToDictionary(g => g.Key, g => g.OrderBy(u => u.Id, StringComparer.Ordinal).ToArray());
        UnitState[] At(int cell) => byCell.TryGetValue(cell, out var units) ? units : Array.Empty<UnitState>();
        var camps = state.Factions.ToDictionary(f => f.Id, f => f.Camp, StringComparer.Ordinal);
        bool Enemy(UnitState unit) => WorldDiplomacy.Hostile(state, faction.Id, unit.Owner, ai.NeutralCamps);
        var strengths = new Dictionary<string, long>(StringComparer.Ordinal);
        long Strength(UnitState unit)
        {
            if (!strengths.TryGetValue(unit.Id, out long value))
                strengths[unit.Id] = value = AiTactics.Strength(Facts(state, unit, ai), ai.Tactics);
            return value;
        }
        long Pressure(int cell, bool enemy)
        {
            long total = 0;
            foreach (var unit in state.Units)
            {
                int distance = map.Distance(cell, unit.Cell);
                if (distance > 0 && distance <= production.PressureRadius && (enemy ? Enemy(unit) : camps[unit.Owner] == faction.Camp))
                    total = checked(total + Strength(unit) / distance);
            }
            return total;
        }
        int[] Neighbors(int cell) => Enumerable.Range(0, 6).Select(d => map.Neighbor(cell, d)).Where(i => i >= 0).ToArray();
        bool Coastal(int cell) => Neighbors(cell).Any(i => state.Cells[i].Terrain.Active && state.Cells[i].Terrain.Sea);
        var income = WorldTurns.FactionIncome(state, faction, turns);
        long upkeep = WorldTurns.FactionUpkeep(state, faction.Id);
        bool CanSupply(RecruitmentOffer offer)
        {
            if (faction.Shortage > 0) return false;
            // Reserve the next owner turn's upkeep after paying the offer and applying the income cap.
            decimal food = (decimal)faction.Resources.Food - offer.Cost.Food + income.Food;
            if (turns.ResourceCap.HasValue) food = Math.Min(food, turns.ResourceCap.Value);
            long added = offer.Support.Logistics || offer.Template!.Has(Ew4Combat.Skill(22)) ? 0 : offer.Support.FoodConsumption;
            return food >= (decimal)upkeep + added;
        }
        bool EligibleOffer(RecruitmentOffer offer) => !(armySize >= armyLimit)
            && faction.Recruitment.ContainsKey(offer.Id) && CanPay(faction.Resources, offer.Cost)
            && CanSupply(offer)
            && (!offer.PerTurnLimit.HasValue || !faction.TurnUses.TryGetValue(offer.TurnGroup, out int count) || count < offer.PerTurnLimit);

        // Availability is derived from configured offers and legal world sites, never legacy byte slots.
        var installationUnits = state.Units.Where(u => u.Owner == faction.Id && !u.Acted && u.Combatant.ConstructionTurns == 0
            && state.Cells[u.Cell].Environment == 0).OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal).ToArray();
        var installations = installationUnits.ToDictionary(u => u.Id, u => production.Installations
            .Where(p => WorldSupportActions.CanBuildInstallationValidated(state, faction, u, p.Value)).Select(p => p.Key).ToArray(), StringComparer.Ordinal);
        var upgradeCells = production.BuildingUpgrades == null ? Array.Empty<int>() : owned.Where(i => state.Cells[i].Environment == 0
            && WorldImprovements.CanUpgradeBuildingValidated(state, faction, i, production.BuildingUpgrades)).ToArray();
        var transports = production.Transport == null ? Array.Empty<UnitState>() : state.Units.Where(u => u.Owner == faction.Id
            && !state.Cells[u.Cell].Terrain.Sea && state.Cells[u.Cell].Environment == 0 && Coastal(u.Cell)
            && WorldSupportActions.CanBuyTransportValidated(state, faction, u, production.Transport)).OrderBy(u => u.Cell).ThenBy(u => u.Id, StringComparer.Ordinal).ToArray();
        bool Available(string task) => task switch
        {
            "recruit" => production.Groups.Any(g => !g.Naval && g.HighestFirst.Any(id => EligibleOffer(production.Offers[id]))),
            "naval" => production.Groups.Any(g => g.Naval && g.HighestFirst.Any(id => EligibleOffer(production.Offers[id]))),
            "fortress" => production.FortressScoring != null && production.Fortresses.Values.Any(EligibleOffer),
            "installation" => production.InstallationScoring != null && installations.Values.Any(v => v.Length > 0),
            "upgrade" => upgradeCells.Length > 0,
            "transport" => transports.Length > 0,
            _ => false
        };
        string? task = AiProduction.SelectTask(production.Tasks.Where(t => t.Weight > 0 && Available(t.Id)).ToArray(), random.Next);
        WorldProductionCommand? command = null;

        if (task == "recruit" || task == "naval")
        {
            if (AiProduction.AttemptRecruit(faction.Resources.Food, income.Food, upkeep, random.Next(100), production.Recruit))
            {
                var groups = production.Groups.Where(g => g.Naval == (task == "naval")).ToArray();
                var projections = new Dictionary<int, (WorldState State, string? UnitId, int? Destination)>();
                var legal = new Dictionary<int, HashSet<string>>();
                foreach (int cell in owned)
                {
                    if (!groups.SelectMany(g => g.HighestFirst).Any(id => EligibleOffer(production.Offers[id])
                        && WorldActions.CanRecruitValidated(state, faction, cell, production.Offers[id], 0, armySize, armyLimit))) continue;
                    WorldState projected = state;
                    string? relocating = null;
                    int? destination = null;
                    if (At(cell).Length >= state.Limits.UnitsPerCell)
                    {
                        foreach (var unit in At(cell).Where(u => u.Owner == faction.Id && u.RemainingMovement > 0 && u.Morale.Effective != -3
                            && !production.HoldBehaviors.Contains(u.Behavior)))
                        {
                            var range = WorldActions.MovementRange(state, unit, ai.Movement);
                            int? target = Neighbors(cell).Where(i => At(i).Length < state.Limits.UnitsPerCell && range.ContainsKey(i)
                                && state.Cells[i].Owner == faction.Id && state.Cells[i].BuildingKind == null).Select(i => (int?)i).FirstOrDefault();
                            if (!target.HasValue) continue;
                            projected = WorldActions.Move(state, state.Revision, unit.Id, target.Value, ai.Movement).State;
                            relocating = unit.Id; destination = target; break;
                        }
                    }
                    var offers = groups.SelectMany(g => g.HighestFirst).Where(id => EligibleOffer(production.Offers[id])
                        && WorldActions.CanRecruitValidated(projected, faction, cell, production.Offers[id],
                            At(cell).Length - (relocating == null ? 0 : 1), armySize, armyLimit)).ToArray();
                    if (offers.Length == 0) continue;
                    projections[cell] = (projected, relocating, destination);
                    legal[cell] = new HashSet<string>(offers, StringComparer.Ordinal);
                }
                var sites = legal.Keys.OrderBy(i => i).Select(cell =>
                {
                    var occupant = At(cell).FirstOrDefault();
                    return new AiRecruitSite(cell, true, projections[cell].UnitId != null, true, false, false, projections[cell].Destination,
                        AiTactics.Location(Site(state, cell, ai), occupant == null ? null : Facts(state, occupant, ai), ai.Tactics),
                        At(cell).Aggregate(0L, (sum, u) => checked(sum + Strength(u))), Pressure(cell, true), state.Cells[cell].Environment);
                }).ToArray();
                var choice = AiProduction.SelectRecruitSite(sites, production.Recruit, random.Next);
                if (choice != null)
                {
                    var pool = AiProduction.RecruitPool(groups.Select(g => new AiRecruitOfferGroup(g.Id, true,
                        g.HighestFirst.Select(id => new AiRecruitOfferEntry(id, legal[choice.Cell].Contains(id))).ToArray())).ToArray(), production.Pool, random.Next);
                    string? selected = AiProduction.SelectRecruitOffer(pool, production.Pool, random.Next);
                    if (selected != null) command = new AiRecruitCommand(state.Revision, choice.Cell, selected, RecruitIdentity(state), false,
                        projections[choice.Cell].UnitId, choice.Relocation);
                }
            }
        }
        else if (task == "installation")
        {
            var stock = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var recipe in production.Installations)
            {
                long best = 0;
                foreach (var unit in installationUnits.Where(u => installations[u.Id].Contains(recipe.Key)))
                    best = Math.Max(best, AiProduction.AffordableCopies(faction.Resources, WorldSupportActions.Price(unit, recipe.Value.Purchase)) ?? long.MaxValue);
                stock[recipe.Key] = best;
            }
            var sites = installationUnits.Select(unit => new AiInstallationSite(unit.Cell, installations[unit.Id].Length > 0,
                state.Units.Where(u => Enemy(u) && map.Distance(unit.Cell, u.Cell) is var distance && distance > 0 && distance <= production.InstallationRadius)
                    .GroupBy(u => u.Combatant.Profile.Category).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal), installations[unit.Id])).ToArray();
            var choice = AiProduction.SelectInstallation(sites, stock, production.InstallationScoring!, random.Next);
            if (choice != null)
            {
                var unit = installationUnits.First(u => u.Cell == choice.Cell && installations[u.Id].Contains(choice.RecipeId));
                command = new AiInstallationCommand(state.Revision, unit.Id, choice.RecipeId);
            }
        }
        else if (task == "fortress")
        {
            var stock = production.Fortresses.ToDictionary(p => p.Key,
                p => EligibleOffer(p.Value) ? AiProduction.AffordableCopies(faction.Resources, p.Value.Cost) ?? long.MaxValue : 0, StringComparer.Ordinal);
            var sites = owned.Select(cell =>
            {
                var allowed = production.Fortresses.Where(p => EligibleOffer(p.Value)
                    && WorldActions.CanRecruitValidated(state, faction, cell, p.Value, At(cell).Length, armySize, armyLimit)).Select(p => p.Key).ToArray();
                bool eligible = !state.Cells[cell].Terrain.Sea && state.Cells[cell].Environment == 0 && state.Cells[cell].BuildingKind == null
                    && state.Cells[cell].InstallationKind == null && At(cell).Length == 0
                    && !Neighbors(cell).Any(i => At(i).Any(u => u.Combatant.Profile.Category == production.FortressCategory));
                var nearby = map.Within(cell, production.FortressSupportRadius).Where(i => state.Cells[i].Terrain.Active && state.Cells[i].Owner != null
                    && camps[state.Cells[i].Owner!] == faction.Camp && !state.Cells[i].Terrain.Sea
                    && At(i).Length == 0)
                    .Select(i => new AiFortressNearby(true, state.Cells[i].BuildingKind == null ? null : state.Cells[i].BuildingLevel, state.Cells[i].Terrain.Sea)).ToArray();
                return new AiFortressSite(cell, eligible, Coastal(cell), state.Cells[cell].Terrain.Sea,
                    state.Units.Count(u => Enemy(u) && map.Distance(cell, u.Cell) > 0 && map.Distance(cell, u.Cell) <= production.FortressEnemyRadius),
                    state.Units.Count(u => Enemy(u) && u.Combatant.Profile.Category == ai.Tactics.NavalCategory
                        && map.Distance(cell, u.Cell) > 0 && map.Distance(cell, u.Cell) <= production.PressureRadius),
                    map.Within(cell, production.PressureRadius).Count(i => state.Cells[i].Terrain.Active && state.Cells[i].Terrain.Sea
                        && state.Cells[i].Owner != null && camps[state.Cells[i].Owner!] == faction.Camp), nearby, allowed);
            }).ToArray();
            var choice = AiProduction.SelectFortress(sites, stock, true, production.FortressScoring!, random.Next);
            if (choice != null) command = new AiRecruitCommand(state.Revision, choice.Cell, choice.RecipeId, RecruitIdentity(state), true);
        }
        else if (task == "upgrade")
        {
            var choice = AiProduction.SelectUpgrade(upgradeCells.Select(cell => new AiUpgradeSite(cell, state.Cells[cell].BuildingKind,
                state.Cells[cell].Environment, Pressure(cell, true), Pressure(cell, false), true)).ToArray(), production.Upgrade, random.Next);
            if (choice.HasValue) command = new AiUpgradeCommand(state.Revision, choice.Value);
        }
        else if (task == "transport")
        {
            var choice = AiProduction.SelectTransport(transports.Select(u => new AiTransportSite(u.Cell, true, false, false, true, true, 0,
                Neighbors(u.Cell).Count(i => state.Cells[i].Terrain.Active && state.Cells[i].Terrain.Sea))).ToArray(), true);
            if (choice.HasValue) command = new AiTransportCommand(state.Revision, transports.First(u => u.Cell == choice.Value).Id);
        }
        return new(state.Revision, state.AiRandomState, random.State, task, command);
    }

    public static WorldChange ExecuteProduction(WorldState state, AiProductionPlan plan, WorldProductionRules production, WorldAiRules ai)
    {
        WorldActions.Ready(state, plan.ExpectedRevision); production.Validate(); ai.Validate();
        if (state.AiRandomState != plan.InitialRandomState) throw new RuleViolation("ai.production_stale_random", "AI random state changed after planning");
        var command = plan.Command ?? throw new RuleViolation("ai.production_empty", "Production attempt did not select a command");
        if (command.ExpectedRevision != plan.ExpectedRevision) throw new RuleViolation("ai.production_selection", "Command revision does not match its plan");
        WorldChange change;
        switch (command)
        {
            case AiRecruitCommand recruit:
                if (!(recruit.Fortress ? production.Fortresses : production.Offers).TryGetValue(recruit.OfferId, out var offer))
                    throw new RuleViolation("ai.production_offer", "Unknown production offer");
                if ((recruit.RelocateUnitId == null) != (recruit.Relocation == null))
                    throw new RuleViolation("ai.production_relocation", "Recruitment relocation must identify unit and destination");
                if (recruit.RelocateUnitId != null && state.Units.SingleOrDefault(u => u.Id == recruit.RelocateUnitId)?.Cell != recruit.Cell)
                    throw new RuleViolation("ai.production_relocation", "Relocation unit is not on the recruitment site");
                change = recruit.RelocateUnitId == null ? WorldActions.Recruit(state, state.Revision, recruit.Cell, recruit.NewUnitId, offer)
                    : WorldActions.RelocateAndRecruit(state, state.Revision, recruit.RelocateUnitId, recruit.Relocation!.Value, recruit.NewUnitId, offer, ai.Movement);
                break;
            case AiInstallationCommand installation:
                if (!production.Installations.TryGetValue(installation.RecipeId, out var build)) throw new RuleViolation("ai.production_offer", "Unknown installation recipe");
                change = WorldSupportActions.BuildInstallation(state, state.Revision, installation.UnitId, build);
                break;
            case AiUpgradeCommand upgrade:
                change = WorldImprovements.UpgradeBuilding(state, state.Revision, upgrade.Cell,
                    production.BuildingUpgrades ?? throw new RuleViolation("ai.production_offer", "No building upgrades configured"));
                break;
            case AiTransportCommand transport:
                change = WorldSupportActions.BuyTransport(state, state.Revision, transport.UnitId,
                    production.Transport ?? throw new RuleViolation("ai.production_offer", "No transport purchase configured"));
                break;
            default: throw new RuleViolation("ai.production_selection", "Unsupported production command");
        }
        var next = change.State with { AiRandomState = plan.FinalRandomState };
        next.Validate();
        return change with { State = next };
    }
}
