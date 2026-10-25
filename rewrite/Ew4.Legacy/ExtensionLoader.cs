using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record ExtensionLayout(string Mode = "list", int Columns = 1, int Gap = 4, int IconSize = 19, int MaxHeight = 74,
    int? Left = null, int? Top = null, int? Width = null, int? Height = null);
public sealed record ExtensionBinding(string Target, string Mode, SkillSlot[]? Skills = null, string[]? Traits = null);
public sealed partial class ExtensionLoader
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public RuleEventBus Bus { get; }
    public string Revision { get; }
    public IReadOnlyList<ExtensionBinding> UnitBindings { get; }
    public IReadOnlyList<ExtensionBinding> CommanderBindings { get; }
    public IReadOnlyDictionary<string, ExtensionVictoryRules> Victories { get; }
    public IReadOnlyDictionary<string, ExtensionLayout> Layouts { get; }
    public IReadOnlyDictionary<string, VictoryPresentation> VictoryPresentations { get; private init; } = new Dictionary<string, VictoryPresentation>();
    public IReadOnlyDictionary<string, ScriptedScenarioEvent[]> ScenarioEvents { get; }
    public bool Enabled => Revision.Length > 0;
    public int ApiVersion { get; private set; } = 1;
    public GeographyCatalog Geography { get; private init; } = GeographyCatalog.Empty;
    public IReadOnlyDictionary<string, MapColorsPresentation> MapColors { get; private init; } = new Dictionary<string, MapColorsPresentation>();
    public LegacySkillParameters? LegacySkills { get; private init; }
    public IReadOnlyDictionary<string, int?> DeploymentLimits { get; private init; } = new Dictionary<string, int?>();
    public IReadOnlyDictionary<string, int> FormationCounts { get; private init; } = new Dictionary<string, int>();
    public IReadOnlyDictionary<string, string> CampaignVisibility { get; private init; } = new Dictionary<string, string>();
    public MetagameRules Metagame { get; private init; } = new();
    public IReadOnlyDictionary<string, StageIntroPresentation> StageIntroPresentations { get; private init; } = new Dictionary<string, StageIntroPresentation>();
    public IReadOnlyDictionary<string, PauseObjectivesPresentation> PauseObjectivesPresentations { get; private init; } = new Dictionary<string, PauseObjectivesPresentation>();
    public IReadOnlyDictionary<string, CommanderDialogueLines> CommanderDialogues { get; private init; } = new Dictionary<string, CommanderDialogueLines>();
    private ExtensionLoader(RuleEventBus bus, string revision, ExtensionBinding[] units, ExtensionBinding[] commanders,
        Dictionary<string, ExtensionVictoryRules> victories, Dictionary<string, ExtensionLayout> layouts,
        Dictionary<string, ScriptedScenarioEvent[]> scenarioEvents)
    { Bus = bus; Revision = revision; UnitBindings = units; CommanderBindings = commanders; Victories = victories; Layouts = layouts; ScenarioEvents = scenarioEvents; }
    private sealed record Manifest(int Schema, int ApiVersion, string[]? Skills = null, string[]? Traits = null,
        string? UnitBindings = null, string? CommanderBindings = null, string? VictoryRules = null, string? Presentation = null, string[]? Scripts = null,
        string? ScenarioEvents = null, string[]? Statuses = null, string? VictoryPresentation = null, string? LegacySkillParameters = null,
        string? CommanderDialogues = null, string? ItemBindings = null, string? DeploymentLimits = null, string? Metagame = null,
        string? StageIntroPresentation = null, string? FormationCounts = null, string? CampaignVisibility = null,
        string? Geography = null, string? PauseObjectivesPresentation = null, string? MapColors = null);
    private sealed record CampaignVisibilityFile(int Schema, Dictionary<string, string>? Visibility);
    private sealed record LegacySkillFile(int Schema, Dictionary<string, Dictionary<string, double>> Skills);
    private sealed record DeploymentLimitFile([property: JsonNumberHandling(JsonNumberHandling.Strict)] int Schema,
        [property: JsonNumberHandling(JsonNumberHandling.Strict)] Dictionary<string, int?>? Limits);
    private sealed record FormationCountFile([property: JsonNumberHandling(JsonNumberHandling.Strict)] int Schema,
        [property: JsonNumberHandling(JsonNumberHandling.Strict)] Dictionary<string, int>? Profiles);

    public static bool PathValid(string? p) => p != null && p.StartsWith("rewrite/", StringComparison.Ordinal) && p.Length < 240
        && !p.Contains('\\') && !p.Contains(':') && p.Split('/').All(s => s.Length > 0 && s != "." && s != "..")
        && p.All(c => c >= 32);
    public static T ParseJson<T>(byte[] data, bool caseSensitiveKeys = false)
    {
        if (data.Length > 2 * 1024 * 1024) throw new RuleViolation("extension.size", "JSON exceeds 2 MiB");
        using var document = JsonDocument.Parse(data, new JsonDocumentOptions { MaxDepth = 32 });
        Unique(document.RootElement, caseSensitiveKeys);
        return JsonSerializer.Deserialize<T>(data, caseSensitiveKeys ? new JsonSerializerOptions(Json) { PropertyNameCaseInsensitive = false } : Json)
            ?? throw new RuleViolation("extension.json", "Missing JSON value");
    }
    public static ExtensionLoader Load(Func<string, byte[]> read, CountryCatalog? countries = null)
    {
        countries ??= CountryCatalog.Load(read);
        byte[] bytes;
        try { bytes = read("rewrite/extension.json"); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException)
        { return new(new(new Dictionary<string, ExtensionSkill>()), "", [], [], new(), new(), new()); }
        var loaded = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        T Read<T>(string path)
        {
            if (!PathValid(path) || !path.EndsWith(".json", StringComparison.Ordinal)) throw new RuleViolation("extension.path", path);
            var data = path == "rewrite/extension.json" ? bytes : read(path);
            if (data.Length > 2 * 1024 * 1024) throw new RuleViolation("extension.size", path);
            loaded[path] = data;
            return ParseJson<T>(data);
        }
        var m = Read<Manifest>("rewrite/extension.json");
        if (m.Schema != 1 || m.ApiVersion is not (1 or 2 or 3)) throw new RuleViolation("extension.version", "Unsupported extension schema/API version");
        if (m.Geography != null && m.ApiVersion < 3) throw new RuleViolation("extension.version", "Geography requires API 3");
        var geography = m.Geography == null ? GeographyCatalog.Empty : Read<GeographyCatalog>(m.Geography);
        if (m.MapColors != null && m.ApiVersion < 3) throw new RuleViolation("extension.version", "Map colors require API 3");
        var mapColors = m.MapColors == null ? new Dictionary<string, MapColorsPresentation>() : Read<Dictionary<string, MapColorsPresentation>>(m.MapColors);
        MapColorsPresentation.Validate(mapColors);
        if (m.ApiVersion == 1 && m.Statuses != null) throw new RuleViolation("extension.version", "Statuses require API 2");
        var deploymentLimits = new Dictionary<string, int?>();
        var formationCounts = new Dictionary<string, int>();
        var campaignVisibility = new Dictionary<string, string>();
        if (m.CampaignVisibility != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Campaign visibility requires API 2");
            var visibility = Read<CampaignVisibilityFile>(m.CampaignVisibility);
            if (visibility.Schema != 1 || visibility.Visibility == null || visibility.Visibility.Any(p =>
                string.IsNullOrWhiteSpace(p.Key) || p.Value is not ("original" or "visible" or "hidden")))
                throw new RuleViolation("extension.campaign_visibility", "Invalid campaign visibility schema, scenario or mode");
            campaignVisibility = visibility.Visibility;
        }
        if (m.FormationCounts != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Formation counts require API 2");
            var formations = Read<FormationCountFile>(m.FormationCounts);
            if (formations.Schema != 1 || formations.Profiles == null || formations.Profiles.Any(p => !Id(p.Key) || p.Value is < 1 or > 99))
                throw new RuleViolation("extension.formation_count", "Formation counts require known profiles and integer counts from 1 to 99");
            formationCounts = formations.Profiles;
        }
        var metagame = new MetagameRules();
        if (m.Metagame != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Metagame rules require API 2");
            metagame = Read<JsonElement>(m.Metagame).Deserialize<MetagameRules>(new JsonSerializerOptions(Json) { NumberHandling = JsonNumberHandling.Strict })
                ?? throw new RuleViolation("metagame.configuration", "Missing metagame configuration");
            metagame.Validate();
        }
        if (m.DeploymentLimits != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Deployment limits require API 2");
            var quotas = Read<DeploymentLimitFile>(m.DeploymentLimits);
            if (quotas.Schema != 1 || quotas.Limits == null || quotas.Limits.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value < 0))
                throw new RuleViolation("extension.deployment_limit", "Invalid deployment limit schema, scenario or nonnegative integer/null limit");
            deploymentLimits = quotas.Limits;
        }
        var commanderDialogues = new Dictionary<string, CommanderDialogueLines>();
        if (m.CommanderDialogues != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Commander dialogues require API 2");
            var dialogues = Read<CommanderDialogueFile>(m.CommanderDialogues);
            dialogues.Validate();
            commanderDialogues = dialogues.Commanders;
        }
        LegacySkillParameters? legacySkills = null;
        if (m.LegacySkillParameters != null)
        {
            if (m.ApiVersion < 2) throw new RuleViolation("extension.version", "Original skill parameters require API 2");
            var parameters = Read<LegacySkillFile>(m.LegacySkillParameters);
            if (parameters.Schema != 1) throw new RuleViolation("legacy.skill_parameters", "Unsupported skill parameter schema");
            legacySkills = LegacySkillParameters.FromOverrides(parameters.Skills);
        }
        var skills = new Dictionary<string, ExtensionSkill>(StringComparer.Ordinal);
        foreach (string path in (m.Skills ?? []).Concat(m.Traits ?? []))
            foreach (var skill in Read<ExtensionSkill[]>(path))
            {
                if (skill == null || skill.Id == null || skill.Effects == null || skill.Cost == null
                    || skill.NameKey == null || skill.DescriptionKey == null || !Id(skill.Id) || skill.Kind is not ("passive" or "active" or "tag")
                    || skill.Target is not ("self" or "ally" or "enemy" or "cell") || skill.Target == "cell" && (m.ApiVersion < 2 || skill.Kind != "active")
                    || skill.Range < 0 || skill.Cooldown < 0 || !skills.TryAdd(skill.Id, skill)
                    || skill.Icon != null && (!PathValid(skill.Icon) || !skill.Icon.EndsWith(".png", StringComparison.Ordinal)))
                    throw new RuleViolation("extension.skill", "Invalid/duplicate skill: " + skill?.Id);
                skill.Cost.Validate();
                if (skill.Cost.Money < 0 || skill.Cost.Industry < 0 || skill.Cost.Food < 0) throw new RuleViolation("extension.cost", skill.Id);
                foreach (var e in skill.Effects)
                {
                    ValidateEffect(e, m.ApiVersion);
                    if (e.Operation is "summon" or "teleport" && (skill.Kind != "active" || skill.Target != "cell"))
                        throw new RuleViolation("extension.effect", "Summon and teleport require an active cell skill");
                    if (e.Event == "skill.activate" && skill.Kind != "active" || e.Event != "skill.activate" && skill.Kind == "active")
                        throw new RuleViolation("extension.effect", "Effect event does not match skill kind");
                }
            }
        var statuses = new Dictionary<string, ExtensionStatus>(StringComparer.Ordinal);
        foreach (string path in m.Statuses ?? []) foreach (var status in Read<ExtensionStatus[]>(path))
        {
            if (status == null || status.Id == null || !Id(status.Id) || skills.ContainsKey(status.Id) || !statuses.TryAdd(status.Id, status)
                || status.NameKey == null || status.DescriptionKey == null || status.Effects == null || status.MaxStacks is < 1 or > 32
                || status.Stacking is not ("refresh" or "replace" or "stack")
                || status.Icon != null && (!PathValid(status.Icon) || !status.Icon.EndsWith(".png", StringComparison.Ordinal)))
                throw new RuleViolation("extension.status", "Invalid or duplicate status");
            foreach (var effect in status.Effects)
            {
                ValidateEffect(effect, m.ApiVersion);
                if (effect.Event is not ("combat.modify_damage" or "combat.modify_incoming_damage" or "combat.counterattack" or "combat.attack_count"
                    or "movement.modify" or "turn.start" or "turn.end"))
                    throw new RuleViolation("extension.status", "Unsupported status event");
            }
        }
        var effects = skills.Values.SelectMany(s => s.Effects).Concat(statuses.Values.SelectMany(s => s.Effects)).ToArray();
        foreach (var effect in effects.Where(e => e.Operation is "apply_status" or "remove_status"))
            if (effect.Status == null || !statuses.ContainsKey(effect.Status)
                || effect.Operation == "apply_status" && effect.Script == null && (effect.Value < 1 || effect.Value > statuses[effect.Status].MaxStacks))
                throw new RuleViolation("extension.status_reference", effect.Status ?? "Missing status");
        var units = m.UnitBindings == null ? [] : Read<ExtensionBinding[]>(m.UnitBindings);
        var commanders = m.CommanderBindings == null ? [] : Read<ExtensionBinding[]>(m.CommanderBindings);
        var items = m.ItemBindings == null ? [] : Read<ExtensionItemBinding[]>(m.ItemBindings);
        if (m.ItemBindings != null && m.ApiVersion < 2) throw new RuleViolation("extension.version", "Item bindings require API 2");
        ValidateItems(items, skills);
        foreach (var binding in units.Concat(commanders))
        {
            if (binding == null || string.IsNullOrWhiteSpace(binding.Target) || binding.Mode is not ("append" or "replace" or "remove"))
                throw new RuleViolation("extension.binding", "Invalid binding target/mode");
            var slots = binding.Skills ?? [];
            if (slots.Any(s => s == null || string.IsNullOrWhiteSpace(s.Id) || !Known(s.Id) || s.Level < 1 || string.IsNullOrWhiteSpace(s.InstanceId) || s.InstanceId.Contains('/')
                || s.InstanceId.Length > 64 || s.InstanceId.StartsWith("status.", StringComparison.Ordinal)
                || s.X.HasValue != s.Y.HasValue || !float.IsFinite(s.X ?? 0) || !float.IsFinite(s.Y ?? 0))
                || slots.Select(s => s.InstanceId).Distinct().Count() != slots.Length || (binding.Traits ?? []).Any(t => string.IsNullOrWhiteSpace(t) || !Known(t)))
                throw new RuleViolation("extension.binding", "Invalid skill reference/instance/position: " + binding.Target);
        }
        var victories = m.VictoryRules == null ? new Dictionary<string, ExtensionVictoryRules>() : Read<Dictionary<string, ExtensionVictoryRules>>(m.VictoryRules);
        foreach (var v in victories.Values) ValidateVictory(v);
        var scenarioEvents = m.ScenarioEvents == null ? new Dictionary<string, ScriptedScenarioEvent[]>() : Read<Dictionary<string, ScriptedScenarioEvent[]>>(m.ScenarioEvents);
        scenarioEvents = scenarioEvents.ToDictionary(p => p.Key, p => p.Value.Select(countries.Resolve).ToArray());
        foreach (var events in scenarioEvents.Values) Core.ScenarioRules.ValidateDefinitions(events);
        var eventVictories = scenarioEvents.Values.SelectMany(v => v).SelectMany(Core.ScenarioRules.Effects)
            .Select(e => e.VictoryRules).OfType<ExtensionVictoryRules>().ToArray();
        foreach (string image in scenarioEvents.Values.SelectMany(v => v).Select(e => e.Popup?.Image)
            .Concat(scenarioEvents.Values.SelectMany(v => v).SelectMany(Core.ScenarioRules.Effects).Select(e => e.FlagImage)).OfType<string>().Distinct())
        {
            var data = read(image);
            if (data.Length > 4 * 1024 * 1024) throw new RuleViolation("extension.size", image);
        }
        var scenarioHandlers = scenarioEvents.Values.SelectMany(v => v).SelectMany(e =>
            Core.ScenarioRules.Conditions(e).Where(c => c.Script != null).Select(c => (Script: c.Script!, Function: c.Function!))
                .Concat(Core.ScenarioRules.Effects(e).Where(a => a.Script != null).Select(a => (Script: a.Script!, Function: a.Function!))))
            .Concat(eventVictories.SelectMany(v => Conditions(v.Win).Concat(v.Lose == null ? [] : Conditions(v.Lose)))
                .Where(c => c.Type == "script").Select(c => (Script: c.Script!, Function: c.Function!))).ToArray();
        var layouts = m.Presentation == null ? new Dictionary<string, ExtensionLayout>() : Read<Dictionary<string, ExtensionLayout>>(m.Presentation);
        var victoryScreens = m.VictoryPresentation == null ? new Dictionary<string, VictoryPresentation>() : Read<Dictionary<string, VictoryPresentation>>(m.VictoryPresentation);
        if (victoryScreens.Count > 0) VictoryPresentation.Validate(victoryScreens, read("client/manifest.json"), read);
        if (m.StageIntroPresentation != null && m.ApiVersion < 2) throw new RuleViolation("extension.version", "Stage intro presentation requires API 2");
        var introScreens = m.StageIntroPresentation == null ? new Dictionary<string, StageIntroPresentation>() : Read<Dictionary<string, StageIntroPresentation>>(m.StageIntroPresentation);
        if (introScreens.Count > 0) StageIntroPresentation.Validate(introScreens, read("client/manifest.json"), read);
        if (m.PauseObjectivesPresentation != null && m.ApiVersion < 2) throw new RuleViolation("extension.version", "Pause objectives presentation requires API 2");
        var pauseScreens = m.PauseObjectivesPresentation == null ? new Dictionary<string, PauseObjectivesPresentation>()
            : Read<Dictionary<string, PauseObjectivesPresentation>>(m.PauseObjectivesPresentation);
        if (pauseScreens.Count > 0) PauseObjectivesPresentation.Validate(pauseScreens, read("client/manifest.json"), read);
        foreach (var layout in layouts)
            if (layout.Value == null || layout.Key is not ("commander" or "unit" or "headquarters" or "tasks" or "achievements") || layout.Value.Mode is not ("list" or "grid" or "positioned")
                || layout.Value.Columns is < 1 or > 16 || layout.Value.Gap is < 0 or > 32 || layout.Value.IconSize is < 12 or > 64
                || layout.Value.MaxHeight is < 40 or > 400 || layout.Value.Left is < 0 or > 560 || layout.Value.Top is < 0 or > 300
                || layout.Value.Width is < 40 or > 560 || layout.Value.Height is < 20 or > 300) throw new RuleViolation("extension.layout", layout.Key);
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string path in effects.Select(e => e.Script)
            .Concat(victories.Values.SelectMany(v => Conditions(v.Win).Concat(v.Lose == null ? [] : Conditions(v.Lose))).Select(c => c.Script))
            .Where(s => s != null).Select(s => s!).Concat(scenarioHandlers.Select(h => h.Script)).Concat(m.Scripts ?? []).Distinct())
        {
            if (!PathValid(path) || !path.EndsWith(".lua", StringComparison.Ordinal)) throw new RuleViolation("extension.path", path);
            var data = read(path);
            if (data.Length > 256 * 1024) throw new RuleViolation("extension.size", path);
            loaded[path] = data; scripts[path] = Encoding.UTF8.GetString(data);
        }
        var runtime = scripts.Count == 0 ? null : new ExtensionScriptRuntime(scripts);
        foreach (var handler in scenarioHandlers) runtime!.ValidateHandler(handler.Script, handler.Function);
        foreach (var effect in effects.Where(e => e.Script != null)) runtime!.ValidateHandler(effect.Script!, effect.Function!);
        foreach (var condition in victories.Values.SelectMany(v => Conditions(v.Win).Concat(v.Lose == null ? [] : Conditions(v.Lose))).Where(c => c.Type == "script"))
            runtime!.ValidateHandler(condition.Script!, condition.Function!);
        foreach (var icon in skills.Values.Select(s => s.Icon).Concat(statuses.Values.Select(s => s.Icon)).OfType<string>().Distinct())
        {
            var data = read(icon);
            if (data.Length > 4 * 1024 * 1024) throw new RuleViolation("extension.size", icon);
        }
        string revision = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', loaded.Select(p => p.Key + ":" + Convert.ToHexStringLower(SHA256.HashData(p.Value)))))));
        return new(new(skills, runtime, statuses), revision, units, commanders, victories, layouts, scenarioEvents)
            { ApiVersion = m.ApiVersion, VictoryPresentations = victoryScreens, LegacySkills = legacySkills, CommanderDialogues = commanderDialogues,
                ItemBindings = items, DeploymentLimits = deploymentLimits, FormationCounts = formationCounts, CampaignVisibility = campaignVisibility,
                Metagame = metagame, StageIntroPresentations = introScreens, PauseObjectivesPresentations = pauseScreens, Geography = geography,
                MapColors = mapColors };
        bool Known(string id) => skills.ContainsKey(id) || id.StartsWith("ew4.skill.", StringComparison.Ordinal)
            && int.TryParse(id[10..], out int value) && value is >= 0 and < 40 || BuiltinTraits.Contains(id);
    }
    public static readonly string[] BuiltinTraits = ["no_retaliation", "line_splash", "siege_gun", "coastal_gun", "consumed_on_attack",
        "ai.rocket", "ai.direct_artillery", "ai.counter_mounted"];
    private static bool Id(string id) => id.Length is > 0 and <= 128 && id.Contains('.') && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
    private static void Unique(JsonElement node, bool caseSensitiveKeys = false)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(caseSensitiveKeys ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            foreach (var p in node.EnumerateObject()) { if (!names.Add(p.Name)) throw new RuleViolation("extension.json", "Duplicate JSON key: " + p.Name); Unique(p.Value, caseSensitiveKeys); }
        }
        else if (node.ValueKind == JsonValueKind.Array) foreach (var v in node.EnumerateArray()) Unique(v, caseSensitiveKeys);
    }
    private static void ValidateEffect(ExtensionEffect e, int apiVersion)
    {
        if (e == null) throw new RuleViolation("extension.effect", "Null effect");
        bool valid = e.Event switch {
            "combat.modify_damage" => e.Operation is "damage_multiplier" or "damage_add",
            "combat.modify_incoming_damage" when apiVersion >= 2 => e.Operation is "damage_multiplier" or "damage_add",
            "movement.modify" when apiVersion >= 2 => e.Operation == "movement_add",
            "combat.counterattack" => e.Operation == "counterattack",
            "combat.attack_count" => e.Operation == "attack_count_add",
            "unit.recruited" or "unit.move_end" or "unit.captured" or "combat.after" or "unit.damaged" or "unit.destroyed" or "turn.start" or "turn.end" or "scenario.start"
                => e.Operation is "heal" or "money" or "industry" or "food" || apiVersion >= 2 && e.Operation is "damage" or "movement",
            "skill.activate" => e.Operation is "heal" or "damage" or "money" or "industry" or "food"
                || apiVersion >= 2 && e.Operation is "summon" or "teleport" or "apply_status" or "remove_status" or "movement",
            _ => false };
        bool query = e.Event.StartsWith("combat.", StringComparison.Ordinal) && e.Event != "combat.after" || e.Event == "movement.modify";
        if (!valid || !double.IsFinite(e.Value) || e.Value < 0 && e.Operation is not ("movement" or "movement_add") || e.Script == null != (e.Function == null)
            || e.Scope is not ("source" or "target" or "area") || e.Relation is not ("any" or "ally" or "enemy")
            || e.Radius is < 0 or > 32 || e.Scope != "area" && e.Radius != 0
            || (query || e.Operation is "money" or "industry" or "food" or "summon" or "teleport") && (e.Scope != "target" || e.Relation != "any" || e.Radius != 0)
            || apiVersion == 1 && (e.Scope != "target" || e.Relation != "any" || e.Radius != 0 || e.Profile != null || e.Status != null || e.Duration != 0)
            || e.Operation == "summon" && (e.Profile == null || !Id(e.Profile) || e.Value != 1) || e.Operation != "summon" && e.Profile != null
            || e.Operation is "apply_status" or "remove_status" && (e.Status == null || !Id(e.Status))
            || e.Operation is not ("apply_status" or "remove_status") && e.Status != null
            || (e.Operation == "apply_status" ? e.Duration is < 1 or > 1000 : e.Duration != 0)
            || e.Function != null && !System.Text.RegularExpressions.Regex.IsMatch(e.Function, "^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new RuleViolation("extension.effect", "Invalid event/operation/handler: " + e.Event + "/" + e.Operation);
    }

    public void ConfigureRuntime(LegacyRuntimeCatalog runtime)
    {
        if (FormationCounts.Keys.Any(id => !runtime.Profiles.ContainsKey(id)))
            throw new RuleViolation("extension.formation_count", "Unknown formation profile");
        foreach (var binding in ItemBindings)
        {
            var item = runtime.Items.Values.SingleOrDefault(i => i.Id == binding.Target)
                ?? throw new RuleViolation("extension.item_target", "Unknown item: " + binding.Target);
            if (item.Consumable ? item.SkillSlots?.Count > 0 : item.UseSkill != null)
                throw new RuleViolation("extension.item_binding", "Consumables use useSkill; equipment uses skills: " + item.Id);
            if (item.TargetCategory != null && !runtime.Profiles.Values.Any(p => p.Profile.Category == item.TargetCategory))
                throw new RuleViolation("extension.item_binding", "Unknown item target category: " + item.TargetCategory);
        }
        var commanders = runtime.Commanders.Values.Select(c => c.Definition.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string id in CommanderDialogues.Keys.Concat(CommanderDialogues.Values.SelectMany(d => d.KillRules)
            .SelectMany(r => r.When.VictimCommanders)).Distinct(StringComparer.Ordinal))
            if (!commanders.Contains(id)) throw new RuleViolation("extension.commander_dialogues", "Unknown commander definition: " + id);
        Bus.RegisterUnitTemplates(runtime.Profiles.Values
            .Select(u => new SkillSummonTemplate(u.Profile, new UnitSupport { FoodConsumption = u.FoodConsumption })));
    }
    private static IEnumerable<VictoryCondition> Conditions(VictoryCondition c)
        => ExtensionVictory.Conditions(c);
    public void ValidateVictoryScripts(ExtensionVictoryRules rules)
    {
        foreach (var condition in Conditions(rules.Win).Concat(rules.Lose == null ? [] : Conditions(rules.Lose)).Where(c => c.Type == "script"))
        {
            if (Bus.Scripts is not ExtensionScriptRuntime runtime)
                throw new RuleViolation("extension.lua_missing", "VICT scripts require rewrite/extension.json and its scripts list");
            runtime.ValidateHandler(condition.Script!, condition.Function!);
        }
    }
    public static void ValidateVictory(ExtensionVictoryRules v)
        => ExtensionVictory.Validate(v);

    public string PatchCatalog(string json)
    {
        if (!Enabled || Geography.Maps.Length > 0 && Bus.Skills.Count == 0 && UnitBindings.Count == 0 && CommanderBindings.Count == 0) return json;
        var catalog = JsonNode.Parse(json)!.AsObject();
        catalog["overridden_skills"] = JsonSerializer.SerializeToNode(Bus.Skills.Keys.Where(s => s.StartsWith("ew4.skill.", StringComparison.Ordinal)).ToArray());
        foreach (var definition in Bus.Skills.Values)
            if (!catalog["skills"]!.AsArray().Any(s => (string?)s!["id"] == definition.Id))
                catalog["skills"]!.AsArray().Add(new JsonObject { ["id"] = definition.Id });
        Apply("profiles", UnitBindings, true); Apply("commanders", CommanderBindings, false);
        return catalog.ToJsonString();
        void Apply(string collection, IReadOnlyList<ExtensionBinding> bindings, bool unit)
        {
            foreach (var binding in bindings)
            {
                var entry = catalog[collection]!.AsArray().SingleOrDefault(e => (string?)e!["id"] == binding.Target)
                    ?? throw new RuleViolation("extension.binding_target", binding.Target);
                var oldSlots = entry["skill_slots"]?.Deserialize<SkillSlot[]>(Json)
                    ?? (unit ? [] : entry["skills"]!.AsArray().Select((s, i) => new SkillSlot((string)s!, "legacy." + i)).ToArray());
                var slots = Merge(oldSlots, binding.Skills ?? [], s => s.InstanceId, binding.Mode);
                entry["skill_slots"] = JsonSerializer.SerializeToNode(slots, Json);
                if (!unit) entry["skills"] = JsonSerializer.SerializeToNode(slots.Select(s => s.Id).ToArray());
                if (unit && binding.Traits != null) entry["traits"] = JsonSerializer.SerializeToNode(Merge(
                    entry["traits"]!.AsArray().Select(s => (string)s!).ToArray(), binding.Traits, s => s, binding.Mode));
            }
        }
    }
    private static T[] Merge<T>(T[] old, T[] values, Func<T, string> key, string mode)
    {
        if (mode == "replace") return values;
        var names = values.Select(key).ToHashSet(StringComparer.Ordinal);
        if (mode == "append" && old.Any(v => names.Contains(key(v)))) throw new RuleViolation("extension.instance", "Duplicate skill instance");
        return mode == "remove" ? old.Where(v => !names.Contains(key(v))).ToArray() : old.Concat(values).ToArray();
    }
}
