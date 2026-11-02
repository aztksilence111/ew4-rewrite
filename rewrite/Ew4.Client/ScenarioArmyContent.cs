using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

internal static class ScenarioArmyContent
{
    public static WorldState Prepare(ClientContent content, WorldState world, WorldCommanderRules rules)
    {
        var effects = ScenarioArmies.Effects(world).ToArray();
        if (!effects.Any(e => e.Type is "unit_spawn" or "commander_assign" or "commander_detach")) return world;
        var battle = content.Battle(world.ScenarioId);
        var original = battle.Armies.Select((a, i) => (id: world.ScenarioId + ".unit." + i + ".commander", a.Commander))
            .Where(a => a.Commander >= 0).ToDictionary(a => a.id, a => a.Commander);
        foreach (var candidate in battle.Taverns.SelectMany(t => t.Candidates).Select((c, i) => (c, i)))
            original.Add(world.ScenarioId + ".tavern." + candidate.i + ".commander", (short)candidate.c.CommanderId);
        world = world with { Commanders = world.Commanders.Select(c => original.TryGetValue(c.Id, out short id)
            ? c with { DefinitionId = content.Catalogs.Runtime.Commanders[id].Definition.Id,
                DisplayName = content.Catalogs.Runtime.Commanders[id].Definition.DisplayName } : c).ToArray() };
        var declarations = effects.Where(e => e.Commander != null && e.CommanderDefinition != null)
            .GroupBy(e => e.Commander!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
        foreach (var group in effects.Where(e => e.Commander != null && e.CommanderDefinition != null).GroupBy(e => e.Commander))
            if (group.Select(e => e.CommanderDefinition).Distinct().Count() != 1)
                throw new RuleViolation("scenario.army_definition", "A commander instance cannot change its definition");

        ScenarioEffect PrepareEffect(ScenarioEffect effect)
        {
            var next = effect;
            if (effect.Type == "unit_spawn" && effect.Profile != null)
            {
                if (!content.Catalogs.Runtime.Profiles.TryGetValue(effect.Profile, out var profile))
                    throw new RuleViolation("scenario.army_definition", "Unknown reinforcement profile: " + effect.Profile);
                next = next with { UnitTemplate = new(content.Extensions.Bus.RefreshAttributes(new() { Profile = profile.Profile, Health = profile.Profile.MaxHealth }),
                    new() { FoodConsumption = profile.FoodConsumption },
                    content.Extensions.Bus.MovementBudget(new() { Profile = profile.Profile, Health = profile.Profile.MaxHealth })) };
            }
            if (effect.Type is "unit_spawn" or "commander_assign" or "commander_detach" && effect.Commander != null)
            {
                var declaration = declarations.GetValueOrDefault(effect.Commander);
                ScenarioCommanderTemplate? template = null;
                if ((effect.CommanderDefinition ?? declaration?.CommanderDefinition) is int id)
                {
                    if (!content.Catalogs.Runtime.Commanders.TryGetValue(id, out var source))
                        throw new RuleViolation("scenario.army_definition", "Unknown event commander: " + id);
                    template = new()
                    {
                        Definition = source.Definition,
                        Runtime = new() { Rank = new(source.Rank, 0), Title = new(source.Title, 0), RankHealthBonus = source.HealthBonus,
                            MaximumTrainingLevel = source.Training, RankExperiencePercent = source.RankExperiencePercent, TitleExperiencePercent = source.TitleExperiencePercent }
                    };
                }
                else if (rules.Definitions.TryGetValue(effect.Commander, out var definition))
                {
                    var runtime = world.Commanders.Single(c => c.Id == effect.Commander);
                    template = new() { Definition = definition with { Id = runtime.DefinitionId ?? definition.Id }, Runtime = runtime };
                }
                else template = effect.CommanderTemplate;
                if (template == null) throw new RuleViolation("scenario.army_definition", "Declare a source definition for future commanders: " + effect.Commander);
                next = next with { CommanderTemplate = template with { Extensions = content.Extensions.Bus,
                    TitleHealing = rules.TitleHealing,
                    Skills = (rules.LegacySkills ?? LegacySkillParameters.Default).Skills.ToDictionary(p => p.Key, p => p.Value.ToDictionary(v => v.Key, v => v.Value)) } };
            }
            return next;
        }
        ScriptedScenarioEvent PrepareEvent(ScriptedScenarioEvent ev) => ev with
        {
            Effects = ev.Effects.Select(PrepareEffect).ToArray(), InsufficientResourcesEffects = ev.InsufficientResourcesEffects.Select(PrepareEffect).ToArray(),
            Popup = ev.Popup == null ? null : ev.Popup with { Choices = ev.Popup.Choices.Select(c => c with { Effects = c.Effects.Select(PrepareEffect).ToArray() }).ToArray() }
        };
        world = world with { ScenarioEvents = world.ScenarioEvents.Select(PrepareEvent).ToArray() };
        if (world.Story is { } story)
            world = world with { Story = story with
            {
                Library = story.Library with { Scenes = story.Library.Scenes.ToDictionary(p => p.Key, p => p.Value with
                { Steps = p.Value.Steps!.Select(s => s with { Choices = s.Choices.Select(c => c with { Effects = c.Effects.Select(PrepareEffect).ToArray() }).ToArray() }).ToArray() }) },
                Runs = story.Runs.Select(r => r with { Continuation = r.Continuation.Select(PrepareEffect).ToArray(),
                    PendingEffects = r.PendingEffects.Select(PrepareEffect).ToArray() }).ToArray()
            } };
        return world;
    }
}
