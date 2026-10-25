using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed partial class ClientGame
{
    private Dictionary<string, string> FactionCountries(WorldState world)
    {
        var countries = RequestBattle(world.ScenarioId).Factions.Select((source, index) => (source, index))
            .ToDictionary(p => LegacyScenarioInitialization.FactionId(world.ScenarioId, p.index, p.source),
                p => content.Countries.Canonical(p.source.CountryId));
        foreach (var creation in ScenarioFactions.Creations(world.ScenarioEvents))
            countries[creation.Faction] = creation.Country ?? (ScenarioFactions.Template(world, creation) is string template
                ? countries.GetValueOrDefault(template) ?? creation.Faction : creation.Faction);
        return countries;
    }
    private string? SaveCountry(ClientSlot slot)
    {
        if (slot.DisplayCountry != null) return slot.DisplayCountry;
        // Old slots stored only the requested country, which is null for campaigns.
        using var session = JsonDocument.Parse(slot.Session);
        var payload = session.RootElement.GetProperty("payload");
        var world = payload.GetProperty("world").GetProperty("payload");
        var facts = new WorldState { ScenarioId = slot.Scenario,
            ScenarioPlayerFaction = world.TryGetProperty("scenarioPlayerFaction", out var player) ? player.GetString() : null,
            ScenarioEvents = world.TryGetProperty("scenarioEvents", out var events)
                ? events.Deserialize<ScriptedScenarioEvent[]>(Json) ?? [] : [] };
        return FactionCountries(facts).GetValueOrDefault(payload.GetProperty("playerFaction").GetString()!) ?? slot.Country;
    }
    private string SavePath(int slot, int mode, bool legacyFallback = false)
    {
        if (slot is < 0 or > 6 || mode is not (0 or 1 or 2 or 4))
            throw new RuleViolation("save.slot", "Invalid save slot or game mode");
        if (mode == 0) return slot == 0 ? Slot : Path.Combine(saveDirectory, "slot-" + slot + ".json");
        string path = Path.Combine(saveDirectory, "saves", "mode-" + mode, "slot-" + slot + ".json");
        if (legacyFallback && slot == 0 && !File.Exists(path) && File.Exists(Slot))
        {
            var old = ReadSlot(Slot);
            if (content.Scenarios.Any(s => s.Id == old.Scenario && s.Mode == mode)) return Slot;
        }
        return path;
    }

    private static ClientSlot ReadSlot(string path)
    {
        if (!File.Exists(path)) throw new RuleViolation("save.empty", "This save slot is empty");
        if (new FileInfo(path).Length > 64 * 1024 * 1024) throw new RuleViolation("save.size", "Save exceeds reader budget");
        return JsonSerializer.Deserialize<ClientSlot>(File.ReadAllBytes(path), Json)
            ?? throw new RuleViolation("save.format", "Invalid slot");
    }

    private object Saves(ClientCommand command) => Enumerable.Range(0, 7).Select(index =>
    {
        SavePath(index, command.Mode);
        try
        {
            string path = SavePath(index, command.Mode, true);
            if (!File.Exists(path)) return new { slot = index, exists = false, corrupt = false, scenario = "", name = "", country = (string?)null, round = 0, savedAt = (DateTimeOffset?)null };
            var saved = ReadSlot(path);
            return new { slot = index, exists = true, corrupt = false, scenario = saved.Scenario,
                name = content.Scenarios.Single(s => s.Id == saved.Scenario).Name, country = SaveCountry(saved),
                round = saved.Round, savedAt = (DateTimeOffset?)(saved.SavedAt ?? new DateTimeOffset(File.GetLastWriteTime(path))) };
        }
        catch (Exception error) when (error is JsonException or IOException or RuleViolation or InvalidOperationException or KeyNotFoundException)
        {
            return new { slot = index, exists = true, corrupt = true, scenario = "", name = "", country = (string?)null, round = 0, savedAt = (DateTimeOffset?)null };
        }
    }).ToArray();

    private void AutoSave()
    {
        var current = Require();
        int mode = content.Scenarios.Single(s => s.Id == current.Session.World.ScenarioId).Mode;
        if (mode is 1 or 2) Save(new ClientCommand(Slot: 0, Mode: mode, Tutorial: tutorialCheckpoint));
    }
}
