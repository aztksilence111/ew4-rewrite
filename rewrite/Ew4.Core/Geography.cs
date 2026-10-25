using System;

namespace Ew4.Core;

public static class Geography
{
    public static void Validate(CellState cell)
    {
        var terrain = cell.Terrain;
        if (terrain.MinimumEntryCost < 0)
            throw new RuleViolation("geography.cost", "Minimum entry cost cannot be negative");
        if (terrain.Barrier is < TerrainBarrier.None or > TerrainBarrier.Ocean
            || terrain.Barrier == TerrainBarrier.Mountain && terrain.Sea
            || terrain.Barrier == TerrainBarrier.Ocean && !terrain.Sea)
            throw new RuleViolation("geography.terrain", "Barrier does not match the underlying land/sea terrain");
        if (terrain.Barrier != TerrainBarrier.None && (cell.Owner != null || cell.Objective
            || cell.BuildingKind != null || cell.InstallationKind != null || cell.Name != null))
            throw new RuleViolation("geography.occupied", "Impassable terrain must be neutral and free of sites or objectives");
    }
}
