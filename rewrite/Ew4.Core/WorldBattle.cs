using System;
using System.Collections.Generic;
using System.Linq;

namespace Ew4.Core;

public sealed record WorldBattleRules
{
    public CombatRules Combat { get; init; } = new();
    public CombatContextRules Context { get; init; } = new();
    public SplashRules Splash { get; init; } = new();
    public ProgressionRules UnitProgression { get; init; } = new()
    {
        Levels = new[] { new ProgressionLevel("0", null) }, AccumulateAtCap = true
    };
    public IReadOnlyDictionary<string, ProgressionRules> UnitProgressionByProfile { get; init; } = new Dictionary<string, ProgressionRules>();
    public ProgressionRules RankProgression { get; init; } = new() { Levels = new[] { new ProgressionLevel("0", null) } };
    public ProgressionRules TitleProgression { get; init; } = new() { Levels = new[] { new ProgressionLevel("0", null) } };
    public IReadOnlyDictionary<string, int> RankHealthBonuses { get; init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, int> TitleHealing { get; init; } = new Dictionary<string, int>();
    public bool LegacyUnitExperienceCostScaling { get; init; } = true;
    public bool CheckFactionSurvival { get; init; } = true;
    public bool ResolveIgnition { get; init; } = true;
    public bool RecalculateLocalMorale { get; init; } = true;
    public bool CollectMedals { get; init; }
    public int InstallationDestructionProbability { get; init; } = 35;
    public int KillMoraleProbability { get; init; } = 30;
    public int SuppressionProbability { get; init; } = 25;
    public int KillDialogueProbability { get; init; } = 8;
    public IReadOnlyCollection<string> RetainActionOnKill { get; init; } = new[] { "cavalry" };
    public IReadOnlyCollection<string> SuppressingCategories { get; init; } = new[] { "cavalry" };
    public IReadOnlyCollection<string> SuppressibleCategories { get; init; } = new[] { "infantry", "artillery" };
    public IReadOnlyList<int> BuildingFireWeights { get; init; } = new[] { 6, 4, 2 };
    public IReadOnlyDictionary<string, IReadOnlyList<int>> TerrainFireWeights { get; init; } = new Dictionary<string, IReadOnlyList<int>>
    {
        ["open"] = new[] { 6, 3, 1 }, ["medium"] = new[] { 6, 5, 4 }, ["dense"] = new[] { 6, 6, 6 }
    };
    public IReadOnlyCollection<string> NeutralCamps { get; init; } = Array.Empty<string>();
    public string? PlayerCamp { get; init; }
    public VictoryRules Victory { get; init; } = new();

    public void Validate()
    {
        Context.Validate(); Splash.Validate();
        foreach (int probability in new[] { InstallationDestructionProbability, KillMoraleProbability, SuppressionProbability, KillDialogueProbability })
            if (probability < 0 || probability > 100) throw new RuleViolation("battle.configuration", "Invalid battle probability");
        foreach (var track in UnitProgressionByProfile.Values.Concat(new[] { UnitProgression, RankProgression, TitleProgression }))
            Progression.Award(new(0, 0), 0, track);
        if (UnitProgressionByProfile.Keys.Any(string.IsNullOrWhiteSpace) || RankHealthBonuses.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0)
            || TitleHealing.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0) || (PlayerCamp != null && string.IsNullOrWhiteSpace(PlayerCamp))
            || Victory.RoundLimit < 1 || !Enum.IsDefined(typeof(ObjectiveUnitOwnership), Victory.UnitObjectiveOwnership)
            || RetainActionOnKill.Concat(SuppressingCategories).Concat(SuppressibleCategories).Concat(NeutralCamps).Any(string.IsNullOrWhiteSpace)
            || TerrainFireWeights.Keys.Any(string.IsNullOrWhiteSpace) || Combat.StarRollCap < 0 || Combat.MinimumDamage < 1)
            throw new RuleViolation("battle.configuration", "Invalid battle configuration");
        foreach (var weights in TerrainFireWeights.Values.Append(BuildingFireWeights))
            if (weights.Any(p => p < 0) || weights.Sum(p => (long)p) > 100)
                throw new RuleViolation("battle.configuration", "Invalid ignition weights");
    }
}

internal sealed record ResolvedBattle(string Attacker, string Defender, int Taken, int Dealt, bool Counterattack,
    ulong RandomState, string? SplashTarget = null, int SplashDamage = 0);

public sealed record BattlePreview(long ExpectedRevision, string Attacker, string Defender, CombatResult Main,
    string? SplashTarget, int SplashDamage);

public static class WorldBattle
{
    public static WorldChange Attack(WorldState state, long expectedRevision, string attackerId, string defenderId, WorldBattleRules rules)
    {
        var preview = Preview(state, expectedRevision, attackerId, defenderId, rules);
        return CommitResolved(state, new(attackerId, defenderId, preview.Main.AttackerDamage, preview.Main.DefenderDamage,
            preview.Main.Counterattack, preview.Main.RandomState, preview.SplashTarget, preview.SplashDamage), rules);
    }

    public static BattlePreview Preview(WorldState state, long expectedRevision, string attackerId, string defenderId, WorldBattleRules rules)
    {
        state.Validate(); rules.Validate();
        if (state.Revision != expectedRevision) throw new RuleViolation("action.stale", "Attack uses a stale state revision");
        if (state.Phase != TurnPhase.Active) throw new RuleViolation("action.phase", "Faction turn is not active");
        if (state.PendingDialogue != null) throw new RuleViolation("action.dialogue_pending", "A dialogue is waiting");
        var attacker = state.Units.SingleOrDefault(u => u.Id == attackerId);
        var defender = state.Units.SingleOrDefault(u => u.Id == defenderId);
        if (attacker == null || defender == null || attacker.Owner != state.ActiveFaction
            || state.Factions.Single(f => f.Id == attacker.Owner).Defeated
            || WorldDiplomacy.Allied(state, attacker.Owner, defender.Owner))
            throw new RuleViolation("attack.target", "Attacker or hostile target is unavailable");
        var map = new HexMap(state.Width, state.Height);
        int distance = map.Distance(attacker.Cell, defender.Cell);
        if (AttackLegality.Check(true, true, attacker.Combatant.ConstructionTurns, attacker.Acted, attacker.Morale.Effective,
            distance, attacker.Combatant.Profile.MinimumRange, attacker.Combatant.Profile.MaximumRange) != AttackBlock.None)
            throw new RuleViolation("attack.unavailable", "Attacker cannot attack this target");
        var a = CombatContext.Resolve(state, attacker, defender.Combatant.Profile.Category, rules.Context);
        var d = CombatContext.Resolve(state, defender, attacker.Combatant.Profile.Category, rules.Context);
        var splash = SplashCombat.Target(state, attacker, defender, rules.Splash);
        CombatExchange? secondary = splash == null ? null : new(
            CombatContext.Resolve(state, attacker, splash.Combatant.Profile.Category, rules.Context),
            CombatContext.Resolve(state, splash, attacker.Combatant.Profile.Category, rules.Context), map.Distance(attacker.Cell, splash.Cell));
        var damage = SplashCombat.Calculate(new(a, d, distance), secondary, new Ew4Random(state.RandomState), rules.Combat, rules.Splash);
        return new(state.Revision, attackerId, defenderId, damage.Main, splash?.Id, damage.SplashDamage);
    }

    internal static WorldChange CommitResolved(WorldState state, ResolvedBattle battle, WorldBattleRules rules)
    {
        state.Validate(); rules.Validate();
        if (battle.Attacker == battle.Defender || battle.Taken < 0 || battle.Dealt < 0 || battle.SplashDamage < 0
            || (battle.SplashDamage > 0 && battle.SplashTarget == null)
            || battle.SplashTarget == battle.Attacker || battle.SplashTarget == battle.Defender
            || !state.Units.Any(u => u.Id == battle.Attacker) || !state.Units.Any(u => u.Id == battle.Defender)
            || (battle.SplashTarget != null && !state.Units.Any(u => u.Id == battle.SplashTarget)))
            throw new RuleViolation("battle.resolution", "Invalid resolved battle");
        return new Transaction(state, rules).Resolve(battle);
    }

    private sealed class Transaction
    {
        private WorldState state;
        private readonly WorldState before;
        private readonly WorldBattleRules rules;
        private readonly List<WorldNotice> notices = new();
        internal Transaction(WorldState source, WorldBattleRules configuration)
        {
            before = source; state = source.Copy(); rules = configuration;
        }
        private UnitState? Unit(string id) => state.Units.SingleOrDefault(u => u.Id == id);
        private void SetUnit(UnitState unit) => state = state with { Units = state.Units.Select(u => u.Id == unit.Id ? unit : u).ToArray() };
        private int Roll()
        {
            var random = new Ew4Random(state.RandomState);
            int roll = random.Next(100);
            state = state with { RandomState = random.State };
            return roll;
        }
        private int Worth(UnitState victim) => checked(victim.Combatant.Profile.KillExperience * (victim.CommanderId != null ? 2 : 1));
        private void SetMorale(string id, int value, bool additive = false)
        {
            var unit = Unit(id);
            if (unit == null) return;
            bool leader = unit.Combatant.Has(Ew4Combat.Skill(3));
            var morale = additive ? unit.Morale.Add(value, leader) : MoraleState.Set(value, leader);
            SetUnit(unit with { Morale = morale, Combatant = unit.Combatant with { Morale = morale.Effective } });
            notices.Add(new(additive ? "battle.morale_added" : "battle.morale_set", id, value));
        }

        internal WorldChange Resolve(ResolvedBattle battle)
        {
            state = state with { RandomState = battle.RandomState };
            var attacker = Unit(battle.Attacker)!;
            var defender = Unit(battle.Defender)!;
            var splash = battle.SplashTarget == null ? null : Unit(battle.SplashTarget);
            if (state.Provocations == null)
            {
                state = WorldDiplomacy.Attacked(state, attacker.Owner, defender.Owner, rules.NeutralCamps);
                if (splash != null && battle.SplashDamage > 0)
                    state = WorldDiplomacy.Attacked(state, attacker.Owner, splash.Owner, rules.NeutralCamps);
            }
            if (battle.Counterattack) Ignite(attacker, defender.Combatant.Profile);
            bool attackerDead = Damage(attacker.Id, battle.Taken, defender);
            if (!attackerDead)
            {
                AwardUnit(attacker.Id, (long)battle.Dealt + battle.SplashDamage);
                AwardCommander(attacker.Id, checked(((long)battle.Dealt + battle.SplashDamage) * 2), false);
            }
            else RecordKill(defender, attacker);
            Ignite(defender, attacker.Combatant.Profile);
            bool defenderDead = Damage(defender.Id, battle.Dealt, attacker);
            if (defenderDead) RecordKill(attacker, defender);
            else AwardUnit(defender.Id, battle.Taken);
            if (Unit(attacker.Id) is UnitState survivor && !(defenderDead && rules.RetainActionOnKill.Contains(attacker.Combatant.Profile.Category)))
                SetUnit(survivor with { RemainingMovement = 0, Acted = true });
            if (defenderDead)
            {
                ClearInstallation(defender.Cell);
                if (Unit(attacker.Id) != null && Roll() < rules.KillMoraleProbability) SetMorale(attacker.Id, 1);
            }
            else if (rules.SuppressingCategories.Contains(attacker.Combatant.Profile.Category)
                && rules.SuppressibleCategories.Contains(defender.Combatant.Profile.Category)
                && Unit(defender.Id) is UnitState target && target.Morale.Effective != -3 && Roll() < rules.SuppressionProbability)
                SetMorale(defender.Id, -1, true);
            if (splash != null && battle.SplashDamage > 0 && Damage(splash.Id, battle.SplashDamage, attacker))
            {
                RecordKill(attacker, splash);
                ClearInstallation(splash.Cell);
            }
            CollectMedals(attacker, defender, battle.Dealt, battle.Taken);
            ConsumeWeapon(attacker.Id);
            if (battle.Counterattack) ConsumeWeapon(defender.Id);
            RefreshLocalMorale();
            var provocation = WorldProvocations.Attacked(state, attacker.Owner, defender.Owner, rules.NeutralCamps);
            state = provocation.State; notices.AddRange(provocation.Notices);
            if (splash != null && battle.SplashDamage > 0 && splash.Owner != defender.Owner
                && (state.Provocations == null || state.LocalMultiplayer?.ShareAlliedVictory == true
                    && state.LocalMultiplayer.HumanFactions.Contains(splash.Owner)))
            {
                var splashProvocation = WorldProvocations.Attacked(state, attacker.Owner, splash.Owner, rules.NeutralCamps);
                state = splashProvocation.State; notices.AddRange(splashProvocation.Notices);
            }
            if (rules.Combat.Extensions != null)
            {
                var beforeEffects = state;
                foreach (string id in new[] { attacker.Id, defender.Id })
                    state = rules.Combat.Extensions.Dispatch(state, "combat.after", id, new Ew4Random(state.RandomState));
                notices.AddRange(RuleEventBus.ChangeNotices(beforeEffects, state));
            }
            var scenarioEvents = ScenarioRules.Evaluate(state, rules.Combat.Extensions?.Scripts);
            state = scenarioEvents.State; notices.AddRange(scenarioEvents.Notices);
            if (rules.PlayerCamp != null && state.Outcome == BattleOutcome.Continue && (state.ScenarioEvents.Count == 0 || state.PendingDialogue == null))
            {
                state = WorldVictory.Resolve(state, rules.PlayerCamp, rules.Victory, rules.NeutralCamps);
                if (state.Outcome != BattleOutcome.Continue)
                {
                    // Dialogues are acknowledged before the host presents the final outcome.
                    notices.Add(new("battle.finished", state.Outcome.ToString()));
                }
            }
            state = state with { Revision = checked(before.Revision + 1) };
            state.Validate();
            return new(state, notices);
        }

        private void CollectMedals(UnitState attacker, UnitState defender, int dealt, int taken)
        {
            if (!rules.CollectMedals || state.LocalMultiplayer != null || !state.Factions.Any(f => f.Human && !f.Defeated)) return;
            // Native 825d4 checks attacker, then defender, against the same defender tile.
            var random = new AiRandom(state.AiRandomState);
            var site = state.Cells[defender.Cell];
            foreach (var (unit, damage) in new[] { (attacker, dealt), (defender, taken) })
                if (state.Factions.Single(f => f.Id == unit.Owner).Human
                    && BattleMedals.Drops(damage, site.BuildingKind, site.BuildingLevel, (int)random.Next(100)))
                    notices.Add(new("battle.medal_dropped", unit.Owner, 1) { Target = unit.Id });
            state = state with { AiRandomState = random.State };
        }

        private void ConsumeWeapon(string id)
        {
            if (Unit(id) is not UnitState unit || !unit.Combatant.Profile.HasTrait("consumed_on_attack")) return;
            var removed = WorldDeaths.Remove(state, id, checkSurvival: rules.CheckFactionSurvival);
            state = removed.State;
            notices.Add(new("unit.consumed", id));
            notices.AddRange(removed.Notices);
        }

        private void Ignite(UnitState originalTarget, UnitProfile weapon)
        {
            notices.Add(new("battle.environment_check", originalTarget.Id));
            if (!rules.ResolveIgnition || weapon.IgnitionNumerator == 0) return;
            var cell = state.Cells[originalTarget.Cell];
            var unit = Unit(originalTarget.Id);
            var weights = cell.BuildingKind != null ? rules.BuildingFireWeights
                : rules.TerrainFireWeights.TryGetValue(cell.FireTerrain, out var row) ? row
                : throw new RuleViolation("battle.fire_terrain", "Missing terrain fire configuration");
            var scaled = weights.Select(p => checked((int)((long)p * weapon.IgnitionNumerator / weapon.IgnitionDenominator))).ToArray();
            var random = new Ew4Random(state.RandomState);
            var fire = Ignition.Resolve(new()
            {
                Sea = cell.Terrain.Sea, Occupied = unit != null, Naval = unit?.Combatant.Profile.Category == "naval",
                Transport = unit?.Transport ?? false, Immune = unit?.Combatant.FireImmune == true || unit?.Combatant.Has(Ew4Combat.Skill(2)) == true,
                Environment = cell.Environment, Burning = unit?.Burning ?? 0
            }, true, scaled, random);
            state = state with { RandomState = fire.RandomState, Cells = state.Cells.Select((c, i) => i == originalTarget.Cell ? c with { Environment = fire.Environment } : c).ToArray() };
            if (unit != null) SetUnit(unit with { Burning = fire.Burning });
        }

        private bool Damage(string id, int amount, UnitState killer)
        {
            var unit = Unit(id);
            if (unit == null) return false;
            notices.Add(new("battle.damage", id, amount));
            int health = (int)Math.Max(0, (long)unit.Combatant.Health - amount);
            if (health > 0)
            {
                SetUnit(unit with { Combatant = unit.Combatant with { Health = health } });
                return false;
            }
            var death = WorldDeaths.Remove(state, id, killer.Owner, rules.CheckFactionSurvival);
            state = death.State; notices.AddRange(death.Notices);
            if (killer.CommanderId != null)
                notices.Add(new("battle.kill_context", killer.Id) { Target = unit.Id });
            if (!death.FactionDefeated && unit.CommanderId == null && killer.CommanderId != null && rules.KillDialogueProbability > 0 && Roll() < rules.KillDialogueProbability)
                notices.Add(new("battle.kill_dialogue", killer.CommanderId) { Target = unit.Id });
            RefreshLocalMorale();
            return true;
        }

        private void RecordKill(UnitState killer, UnitState victim)
        {
            var faction = state.Factions.Single(f => f.Id == killer.Owner);
            var kills = faction.Kills.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            string type = victim.Combatant.Profile.Id;
            kills[type] = checked((kills.TryGetValue(type, out long prior) ? prior : 0) + 1);
            state = state with { Factions = state.Factions.Select(f => f.Id == faction.Id ? faction with { Kills = kills } : f).ToArray() };
            notices.Add(new("battle.kill", killer.Owner));
            AwardCommander(killer.Id, Worth(victim), true);
        }

        private void AwardUnit(string id, long amount)
        {
            var unit = Unit(id);
            if (unit == null) return;
            notices.Add(new("battle.experience", id, amount));
            var track = rules.UnitProgressionByProfile.TryGetValue(unit.Combatant.Profile.Id, out var specific) ? specific : rules.UnitProgression;
            if (rules.LegacyUnitExperienceCostScaling)
                track = track with { Levels = track.Levels.Select(l => l.ExperienceToNext.HasValue ? l with
                {
                    ExperienceToNext = checked((unit.CommanderId != null ? checked(l.ExperienceToNext.Value * 3) / 2 : l.ExperienceToNext.Value)
                        * (unit.Combatant.Profile.Category == "naval" ? 2 : 1))
                } : l).ToArray() };
            int level = track.Levels.ToList().FindIndex(l => l.Id == unit.Veterancy);
            var result = Progression.Award(new(level, unit.Experience), amount, track);
            SetUnit(unit with { Experience = result.State.Experience, Veterancy = track.Levels[result.State.Level].Id,
                Combatant = unit.Combatant with { Health = (int)Math.Min(unit.Combatant.Profile.MaxHealth, (long)unit.Combatant.Health + result.Healing) } });
        }

        private void AwardCommander(string id, long amount, bool title)
        {
            notices.Add(new(title ? "battle.title_experience" : "battle.rank_experience", id, amount));
            var unit = Unit(id);
            if (unit?.CommanderId == null) return;
            var commander = state.Commanders.Single(c => c.Id == unit.CommanderId);
            if (!commander.ProgressionEnabled) return;
            var track = title ? rules.TitleProgression : rules.RankProgression;
            var prior = title ? commander.Title : commander.Rank;
            long adjusted = Ew4Experience.CommanderGain(amount, title, unit.Combatant.Skills,
                title ? commander.TitleExperiencePercent : commander.RankExperiencePercent, skillParameters: rules.Combat.LegacySkills);
            var progression = Progression.Award(prior, adjusted, track);
            commander = title ? commander with { Title = progression.State } : commander with { Rank = progression.State };
            if (progression.LevelsGained > 0)
            {
                string level = track.Levels[progression.State.Level].Id;
                if (title && rules.TitleHealing.TryGetValue(level, out int heal))
                    unit = unit with { Support = unit.Support with { TitleHealing = heal } };
                if (!title && rules.RankHealthBonuses.TryGetValue(level, out int bonus))
                {
                    int maximum = checked(unit.Combatant.Profile.MaxHealth - commander.RankHealthBonus + bonus);
                    if (maximum < 1) throw new RuleViolation("battle.rank_health", "Rank change would produce an invalid maximum health");
                    int health = Math.Max(1, (int)Math.Min(maximum, (long)unit.Combatant.Health * maximum / unit.Combatant.Profile.MaxHealth));
                    unit = unit with { Combatant = unit.Combatant with { Health = health, Profile = unit.Combatant.Profile with { MaxHealth = maximum } } };
                    commander = commander with { RankHealthBonus = bonus };
                }
                notices.Add(new(title ? "commander.title_advanced" : "commander.rank_advanced", commander.Id, progression.LevelsGained));
            }
            SetUnit(unit);
            state = state with { Commanders = state.Commanders.Select(c => c.Id == commander.Id ? commander : c).ToArray() };
        }

        private void ClearInstallation(int cell)
        {
            if (Roll() >= rules.InstallationDestructionProbability) return;
            state = state with { Cells = state.Cells.Select((c, i) => i == cell ? c with { InstallationKind = null } : c).ToArray() };
            notices.Add(new("installation.cleared", cell.ToString()));
        }

        private void RefreshLocalMorale()
        {
            if (!rules.RecalculateLocalMorale) return;
            state = WorldDeaths.RefreshMorale(state, rules.NeutralCamps);
        }
    }
}
