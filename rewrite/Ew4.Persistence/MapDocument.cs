using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Persistence;

public sealed record LoadedMap(WorldMapDefinition Map, string Revision);

public static class MapDocument
{
    private sealed record Payload(string Format, int Version, string Id, string Name, int Width, int Height,
        IReadOnlyList<CellState> Cells, IReadOnlyList<MapImageLayer> Images);
    public static byte[] Encode(WorldMapDefinition map) => JsonSerializer.SerializeToUtf8Bytes(
        new Payload("ew4.rewrite.map", map.Cells.Any(c => c.Terrain.Barrier != TerrainBarrier.None || c.Terrain.MinimumEntryCost > 0) ? 2 : 1,
            map.Id, map.Name, map.Width, map.Height, map.Cells, map.Images), WorldSave.Options);

    public static LoadedMap Decode(byte[] bytes, SaveReadLimits? limits = null)
    {
        limits ??= new();
        if (limits.MaximumBytes < 1 || limits.MaximumDepth < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        if (bytes.Length > limits.MaximumBytes) throw new RuleViolation("map.size", "Map exceeds configured read budget");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
            WorldSave.CheckUnique(document.RootElement);
            foreach (string key in new[] { "format", "version", "id", "name", "width", "height", "cells", "images" })
                WorldSave.Require(document.RootElement, key);
            var data = JsonSerializer.Deserialize<Payload>(bytes, WorldSave.Options) ?? throw new RuleViolation("map.format", "Missing map data");
            if (data.Format != "ew4.rewrite.map") throw new RuleViolation("map.format", "Unknown map format");
            if (data.Version is not (1 or 2)) throw new RuleViolation("map.version", "Map requires an explicit migration");
            foreach (var cell in document.RootElement.GetProperty("cells").EnumerateArray())
            {
                foreach (string key in new[] { "terrain", "name", "buildingKind", "buildingLevel", "installationKind", "fireTerrain", "defenseTerrain" })
                    WorldSave.Require(cell, key);
                foreach (string key in new[] { "active", "sea", "terrainCost", "blockedSeaEdges", "riverEdges", "bridgeEdges" })
                    WorldSave.Require(cell.GetProperty("terrain"), key);
            }
            var map = new WorldMapDefinition(data.Id, data.Name, data.Width, data.Height, data.Cells, data.Images);
            return new(map, Convert.ToHexStringLower(SHA256.HashData(Encode(map))));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or NullReferenceException or ArgumentException)
        { throw new RuleViolation("map.malformed", "Malformed map document: " + error.GetType().Name); }
    }

    public static void WriteAtomic(string path, WorldMapDefinition map) => WorldSave.WriteAtomicBytes(path, Encode(map));
    public static LoadedMap Read(string path, SaveReadLimits? limits = null)
    {
        limits ??= new();
        if (new FileInfo(path).Length > limits.MaximumBytes) throw new RuleViolation("map.size", "Map exceeds configured read budget");
        return Decode(File.ReadAllBytes(path), limits);
    }
}
