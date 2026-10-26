using Ew4.Client;

namespace Ew4.Mods;

internal static class ContentValidation
{
    internal static void Check(ModBase baseline, Func<string, byte[]> read, IEnumerable<string> scenarios,
        HashSet<string> files, List<object>? diagnostics = null, bool startScenarios = true)
    {
        var content = baseline.Content(read, scenarios);
        var errors = new List<string>();
        Exception? firstError = null;
        foreach (var scenario in content.Scenarios)
            try
            {
                var battle = content.Battle(scenario.Id);
                if (!content.Maps.ContainsKey(battle.MapId)) throw new InvalidDataException("剧本缺少地图");
                if (startScenarios && scenario.Mode != 3)
                {
                    var started = content.Start(scenario.Id, scenario.Mode == 2 ? battle.Factions.First(f => f.Camp != 4).CountryId : null);
                    foreach (var diagnostic in started.MapDiagnostics)
                        diagnostics?.Add(new { scenario = scenario.Id, diagnostic.Code, diagnostic.Cell, diagnostic.Value });
                }
            }
            catch (Exception error) when (ModStore.Recoverable(error))
            { firstError ??= error; errors.Add(scenario.Id + ": " + error.Message); }
        if (errors.Count > 0) throw new InvalidDataException($"{errors.Count} 个剧本无法启动:\n" + string.Join('\n', errors), firstError);
        ResourceValidation.Check(read, files, content);
    }
}
