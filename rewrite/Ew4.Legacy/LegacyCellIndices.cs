using System.Security.Cryptography;
using Ew4.Core;

namespace Ew4.Legacy;

// Explicit required extension: legacy 16-bit records retain their original interpretation.
public sealed record LegacyCellIndices(int Schema, string BaseSha256, int MapWidth, int MapHeight,
    IReadOnlyList<int> Sites, IReadOnlyList<int> Armies)
{
    internal void Validate(ReadOnlySpan<byte> body, IReadOnlyList<int> header, int siteCount, int armyCount)
    {
        if (Schema != 1 || BaseSha256 != Convert.ToHexStringLower(SHA256.HashData(body))
            || MapWidth < 1 || MapHeight < 1 || (long)MapWidth * MapHeight > 100_000_000
            || header[2] < 0 || header[3] < 0 || (long)header[2] + header[4] > MapWidth
            || (long)header[3] + header[5] > MapHeight || Sites == null || Armies == null
            || Sites.Count != siteCount || Armies.Count != armyCount)
            throw new RuleViolation("legacy.cell_indices", "CELL dimensions, source hash or record counts differ");
        foreach (int cell in Sites.Concat(Armies))
            if (cell < 0 || cell >= (long)MapWidth * MapHeight)
                throw new RuleViolation("legacy.cell_indices", "CELL contains an invalid full-map cell index");
    }
}
