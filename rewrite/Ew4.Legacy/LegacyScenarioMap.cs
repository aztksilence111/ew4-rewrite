using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record LegacyMapImport(IReadOnlyList<CellState> Cells, string SourceRevision, int OwnedCells,
    IReadOnlyDictionary<int, LegacySite> SiteRecords)
{
    public IReadOnlyList<LegacyMapDiagnostic> Diagnostics { get; init; } = [];
}
public sealed record LegacyMapDiagnostic(string Code, int Cell, int Value);

public static class LegacyScenarioMap
{
    private static readonly System.Text.Json.JsonSerializerOptions GeographyJson = new() {
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver {
            Modifiers = { info => {
                if (info.Type == typeof(MovementCell)) {
                    info.Properties.Single(p => p.Name == "Barrier").ShouldSerialize = (_, value) => (TerrainBarrier)value! != TerrainBarrier.None;
                    info.Properties.Single(p => p.Name == "MinimumEntryCost").ShouldSerialize = (_, value) => (int)value! != 0;
                }
            } }
        }
    };
    // This builds the map component only. Units, accounts, events and runtime rules must still be imported.
    public static LegacyMapImport Import(LegacyTerrainMap map, LegacyBattle battle, LegacyTerrainRules rules,
        IReadOnlyList<string> factionIds, IReadOnlyDictionary<int, string> buildingKinds,
        IReadOnlyDictionary<int, string> installationKinds, IReadOnlyDictionary<int, string>? cityNames = null,
        LegacyBuildingCatalog? buildingRules = null)
        => Import(LegacyMapDefinitions.Import(map, rules, "legacy.map." + battle.MapId, "Legacy map " + battle.MapId),
            battle, factionIds, buildingKinds, installationKinds, cityNames, buildingRules);

    public static LegacyMapImport Import(WorldMapDefinition map, LegacyBattle battle,
        IReadOnlyList<string> factionIds, IReadOnlyDictionary<int, string> buildingKinds,
        IReadOnlyDictionary<int, string> installationKinds, IReadOnlyDictionary<int, string>? cityNames = null,
        LegacyBuildingCatalog? buildingRules = null)
    {
        if (factionIds.Count != battle.Factions.Count || factionIds.Any(string.IsNullOrWhiteSpace)
            || factionIds.Distinct(StringComparer.Ordinal).Count() != factionIds.Count)
            throw new RuleViolation("legacy.faction_mapping", "Each serialized faction needs a distinct modern identity");
        if (battle.Left < 0 || battle.Top < 0 || (long)battle.Left + battle.Width > map.Width || (long)battle.Top + battle.Height > map.Height)
            throw new RuleViolation("legacy.region", "Scenario rectangle is outside its map");
        var ownership = battle.SectionBytes("ownership");
        var cells = new CellState[map.Cells.Count];
        var diagnostics = new List<LegacyMapDiagnostic>();
        int owned = 0;
        for (int cell = 0; cell < cells.Length; cell++)
        {
            int x = cell % map.Width - battle.Left, y = cell / map.Width - battle.Top;
            bool active = x >= 0 && y >= 0 && x < battle.Width && y < battle.Height;
            string? owner = null;
            if (active && map.Cells[cell].Terrain.CanTraverse())
            {
                int index = unchecked((sbyte)ownership[y * battle.Width + x]);
                if (index >= 0)
                {
                    // 80904 returns null for an absent faction; 7cb80 leaves this tile unowned.
                    if (index < factionIds.Count) { owner = factionIds[index]; owned++; }
                    else diagnostics.Add(new("legacy.owner_unresolved", cell, index));
                }
            }
            cells[cell] = map.Cells[cell] with { Terrain = map.Cells[cell].Terrain with { Active = active && map.Cells[cell].Terrain.Active }, Owner = owner };
        }
        var sites = new Dictionary<int, LegacySite>();
        foreach (var site in battle.Sites)
        {
            // 7cb80 applies sites in file order through the full-map tile lookup a9744.
            if (site.Cell < 0 || site.Cell >= cells.Length)
            {
                diagnostics.Add(new("legacy.site_outside_map", site.Cell, site.BuildingKind));
                continue;
            }
            if (!cells[site.Cell].Terrain.Active) diagnostics.Add(new("legacy.site_inactive", site.Cell, site.BuildingKind));
            if (sites.ContainsKey(site.Cell)) diagnostics.Add(new("legacy.site_replaced", site.Cell, site.BuildingKind));
            sites[site.Cell] = site;
            if (cells[site.Cell].Terrain.Barrier != TerrainBarrier.None && (site.BuildingKind != 255
                || site.InstallationKind != -1 || site.CityId >= 0 || site.Marker != 0))
                throw new RuleViolation("geography.site", $"{map.Id}: site conflicts with impassable cell {site.Cell}");
            if (site.BridgeEdges > 63) throw new RuleViolation("legacy.bridge", "Unknown bridge direction flags");
            string? building = null, installation = null, name = null;
            if (site.BuildingKind != 255 && (!buildingKinds.TryGetValue(site.BuildingKind, out building) || string.IsNullOrWhiteSpace(building)))
                throw new RuleViolation("legacy.building_kind", "Missing building identity mapping");
            if (site.InstallationKind != -1 && (!installationKinds.TryGetValue(site.InstallationKind, out installation) || string.IsNullOrWhiteSpace(installation)))
                throw new RuleViolation("legacy.installation_kind", "Missing installation identity mapping");
            // 51b2c retains the site even when 6c830 cannot resolve its label.
            if (site.CityId >= 0 && cityNames != null) cityNames.TryGetValue(site.CityId, out name);
            if (string.IsNullOrWhiteSpace(name))
            {
                name = null;
                if (site.CityId >= 0) diagnostics.Add(new("legacy.city_label_missing", site.Cell, site.CityId));
            }
            if (site.Marker > 2) throw new RuleViolation("legacy.marker", "Unknown objective marker");
            if (buildingRules != null && building != null
                && (!buildingRules.Definitions.TryGetValue(site.BuildingKind, out var definition) || definition.Kind != building))
                throw new RuleViolation("legacy.building_mapping", "Building rule identity differs from map identity mapping");
            int level = building == null ? 0 : buildingRules?.NormalizeLevel(site.BuildingKind, site.BuildingLevel) ?? site.BuildingLevel;
            cells[site.Cell] = cells[site.Cell] with { Name = name, BuildingKind = building, BuildingLevel = level,
                BuildingUpgradesThisTurn = building != null && site.State15 != 0 ? 1 : 0,
                InstallationKind = installation, Objective = site.Marker == 1, BlocksTerritoryExpansion = site.Marker != 0,
                Terrain = cells[site.Cell].Terrain with { BridgeEdges = site.BridgeEdges } };
        }
        var environments = battle.SectionBytes("opaque8");
        var seen = new HashSet<int>();
        for (int offset = 0; offset < environments.Length; offset += 8)
        {
            int cell = BinaryPrimitives.ReadInt32LittleEndian(environments.AsSpan(offset));
            int intensity = BinaryPrimitives.ReadInt32LittleEndian(environments.AsSpan(offset + 4));
            if (cell < 0 || cell >= cells.Length || !cells[cell].Terrain.Active || intensity < 0 || !seen.Add(cell))
                throw new RuleViolation("legacy.environment", "Invalid or duplicate environment record");
            cells[cell] = cells[cell] with { Environment = intensity };
        }
        // Include resolved map conversion and identity/name policy, not only binary assets.
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(Encoding.UTF8.GetBytes(buildingRules?.SourceRevision ?? ""));
        digest.AppendData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(map, GeographyJson)); digest.AppendData(battle.ToBytes());
        Span<byte> size = stackalloc byte[4];
        foreach (var cell in cells)
        {
            string row = System.Text.Json.JsonSerializer.Serialize(cell, GeographyJson);
            byte[] encoded = Encoding.UTF8.GetBytes(row);
            BinaryPrimitives.WriteInt32LittleEndian(size, encoded.Length);
            digest.AppendData(size); digest.AppendData(encoded);
        }
        return new(Array.AsReadOnly(cells), Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(), owned,
            new System.Collections.ObjectModel.ReadOnlyDictionary<int, LegacySite>(sites)) { Diagnostics = diagnostics.AsReadOnly() };
    }
}
