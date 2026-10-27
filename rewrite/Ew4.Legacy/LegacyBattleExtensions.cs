using System.Buffers.Binary;
using System.Security.Cryptography;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record LegacyJurisdictionData(int Schema, int MapId, int MapWidth, int MapHeight,
    int Left, int Top, int Width, int Height, string BaseSha256, IReadOnlyList<int> CellCenters)
{
    public TerritorialJurisdictions Apply(WorldState world)
    {
        if (world.Width != MapWidth || world.Height != MapHeight)
            throw new RuleViolation("legacy.jurisdiction_map", "辖区底图尺寸与当前剧本不符");
        var assignments = Enumerable.Repeat(-1, world.Cells.Count).ToArray();
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                assignments[(Top + y) * MapWidth + Left + x] = CellCenters[y * Width + x];
        return TerritorialJurisdictions.FromAssignments(world, assignments);
    }
}

internal sealed record LegacyBattleExtension(string Tag, uint Version, uint Flags, byte[] Payload);

internal static class LegacyBattleExtensions
{
    private static ReadOnlySpan<byte> Magic => "EW4XBTL\0"u8;
    private const int MaximumBytes = 64 * 1024 * 1024;
    internal static LegacyBattleExtension[] Read(ReadOnlySpan<byte> trailer)
    {
        if (trailer.IsEmpty) return [];
        void Fail(string message) => throw new RuleViolation("legacy.battle_extension", message);
        if (trailer.Length < 16 || trailer.Length > MaximumBytes || !trailer[..8].SequenceEqual(Magic))
            Fail("未知或截断的BTL尾部扩展");
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(trailer[8..]), count = BinaryPrimitives.ReadUInt32LittleEndian(trailer[12..]);
        if (version != 1 || count is < 1 or > 64) Fail("不支持的BTL扩展容器版本或块数量");
        int cursor = 16;
        var blocks = new List<LegacyBattleExtension>();
        for (int i = 0; i < count; i++)
        {
            if (trailer.Length - cursor < 16) Fail("BTL扩展块头被截断");
            var header = trailer.Slice(cursor, 16);
            string tag = System.Text.Encoding.ASCII.GetString(header[..4]);
            if (header[..4].ToArray().Any(b => b is < (byte)'A' or > (byte)'Z')) Fail("BTL扩展块标识必须为四位大写ASCII");
            uint blockVersion = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]), flags = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]), length = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
            cursor += 16;
            if (flags > 1 || length > trailer.Length - cursor || blocks.Any(b => b.Tag == tag)) Fail("BTL扩展块重复、长度或标记无效: " + tag);
            if (tag is "JURI" or "VICT" or "CNTR" or "CELL" ? blockVersion != 1 || flags != 1 : flags == 1) Fail("不支持的必需BTL扩展: " + tag);
            blocks.Add(new(tag, blockVersion, flags, trailer.Slice(cursor, (int)length).ToArray()));
            cursor += (int)length;
        }
        if (cursor != trailer.Length) Fail("BTL扩展末尾含未定义数据");
        return blocks.ToArray();
    }

    internal static LegacyJurisdictionData? Jurisdictions(IReadOnlyList<LegacyBattleExtension> blocks, ReadOnlySpan<byte> body, IReadOnlyList<int> header)
    {
        var block = blocks.SingleOrDefault(b => b.Tag == "JURI");
        if (block == null) return null;
        var p = block.Payload;
        if (p.Length < 64) throw new RuleViolation("legacy.jurisdiction_length", "辖区扩展被截断");
        int count = LegacyBytes.I32(p, 28);
        if (count != (long)header[4] * header[5] || p.Length != 64L + 4L * count)
            throw new RuleViolation("legacy.jurisdiction_length", "辖区必须完整记录剧本区域的每一个格子");
        var values = new int[count];
        for (int i = 0; i < count; i++) values[i] = LegacyBytes.I32(p, 64 + i * 4);
        var data = new LegacyJurisdictionData(1, LegacyBytes.I32(p, 0), LegacyBytes.I32(p, 20), LegacyBytes.I32(p, 24),
            LegacyBytes.I32(p, 4), LegacyBytes.I32(p, 8), LegacyBytes.I32(p, 12), LegacyBytes.I32(p, 16),
            Convert.ToHexStringLower(p.AsSpan(32, 32)), Array.AsReadOnly(values));
        Validate(data, body, header);
        return data;
    }

    private static void Validate(LegacyJurisdictionData data, ReadOnlySpan<byte> body, IReadOnlyList<int> header)
    {
        if (data.Schema != 1 || data.MapId != header[1] || data.Left != header[2] || data.Top != header[3]
            || data.Width != header[4] || data.Height != header[5] || data.Left < 0 || data.Top < 0
            || data.MapWidth < 1 || data.MapHeight < 1 || (long)data.Left + data.Width > data.MapWidth
            || (long)data.Top + data.Height > data.MapHeight || (long)data.MapWidth * data.MapHeight > 100_000_000
            || data.CellCenters == null || data.CellCenters.Count != (long)data.Width * data.Height)
            throw new RuleViolation("legacy.jurisdiction_metadata", "辖区格式、底图或剧本区域信息不符");
        if (!string.Equals(data.BaseSha256, Convert.ToHexStringLower(SHA256.HashData(body)), StringComparison.OrdinalIgnoreCase))
            throw new RuleViolation("legacy.jurisdiction_source", "辖区绑定的BTL主体已变化，请重新导出并核对配置");
        for (int i = 0; i < data.CellCenters.Count; i++)
        {
            int center = data.CellCenters[i], x = center < 0 ? -1 : center % data.MapWidth, y = center < 0 ? -1 : center / data.MapWidth;
            if (center < -1 || center >= (long)data.MapWidth * data.MapHeight || center >= 0
                && (x < data.Left || x >= data.Left + data.Width || y < data.Top || y >= data.Top + data.Height))
                throw new RuleViolation("legacy.jurisdiction_center", $"辖区记录[{i}]的中心格编号无效: {center}");
        }
    }

    internal static byte[] Write(ReadOnlySpan<byte> body, IReadOnlyList<int> header, IReadOnlyList<LegacyBattleExtension> existing, LegacyJurisdictionData? data)
    {
        var blocks = existing.Where(b => b.Tag != "JURI").ToList();
        if (data != null)
        {
            Validate(data, body, header);
            if (64L + 4L * data.CellCenters.Count > MaximumBytes - 32) throw new RuleViolation("legacy.jurisdiction_length", "辖区超过64MiB扩展预算");
            var p = new byte[64 + 4 * data.CellCenters.Count];
            int[] fields = [data.MapId, data.Left, data.Top, data.Width, data.Height, data.MapWidth, data.MapHeight, data.CellCenters.Count];
            for (int i = 0; i < fields.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(i * 4), fields[i]);
            SHA256.HashData(body).CopyTo(p, 32);
            for (int i = 0; i < data.CellCenters.Count; i++) BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(64 + i * 4), data.CellCenters[i]);
            blocks.Add(new("JURI", 1, 1, p));
        }
        return Container(body, blocks);
    }
    private sealed record VictoryEnvelope(int Schema, string BaseSha256, Ew4.Core.ExtensionVictoryRules Rules);
    internal static Ew4.Core.ExtensionVictoryRules? Victory(IReadOnlyList<LegacyBattleExtension> blocks, ReadOnlySpan<byte> body)
    {
        var block = blocks.SingleOrDefault(b => b.Tag == "VICT");
        if (block == null) return null;
        var data = ExtensionLoader.ParseJson<VictoryEnvelope>(block.Payload);
        if (data.Schema != 1 || data.BaseSha256 != Convert.ToHexStringLower(SHA256.HashData(body)))
            throw new RuleViolation("legacy.victory", "VICT version/source mismatch");
        ExtensionLoader.ValidateVictory(data.Rules);
        return data.Rules;
    }
    internal static byte[] WriteVictory(ReadOnlySpan<byte> body, IReadOnlyList<LegacyBattleExtension> existing, Ew4.Core.ExtensionVictoryRules? rules)
    {
        var blocks = existing.Where(b => b.Tag != "VICT").ToList();
        if (rules != null)
        {
            ExtensionLoader.ValidateVictory(rules);
            blocks.Add(new("VICT", 1, 1, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new VictoryEnvelope(1,
                Convert.ToHexStringLower(SHA256.HashData(body)), rules), ExtensionLoader.Json)));
        }
        return Container(body, blocks);
    }
    internal static LegacyCountryIds? CountryIds(IReadOnlyList<LegacyBattleExtension> blocks, ReadOnlySpan<byte> body,
        IReadOnlyList<LegacyFaction> factions, IReadOnlyList<LegacyEvent> events)
    {
        var block = blocks.SingleOrDefault(b => b.Tag == "CNTR");
        if (block == null) return null;
        var data = ExtensionLoader.ParseJson<LegacyCountryIds>(block.Payload);
        data.Validate(body, factions.Count, events.Count);
        return data;
    }
    internal static byte[] WriteCountryIds(ReadOnlySpan<byte> body, IReadOnlyList<LegacyBattleExtension> existing,
        LegacyCountryIds? data, int factions, int events)
    {
        var blocks = existing.Where(b => b.Tag != "CNTR").ToList();
        if (data != null)
        {
            data.Validate(body, factions, events);
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, ExtensionLoader.Json);
            if (payload.Length > 2 * 1024 * 1024) throw new RuleViolation("legacy.country_ids", "CNTR JSON exceeds 2 MiB");
            blocks.Add(new("CNTR", 1, 1, payload));
        }
        return Container(body, blocks);
    }
    internal static LegacyCellIndices? CellIndices(IReadOnlyList<LegacyBattleExtension> blocks, ReadOnlySpan<byte> body,
        IReadOnlyList<int> header, int sites, int armies)
    {
        var block = blocks.SingleOrDefault(b => b.Tag == "CELL");
        if (block == null) return null;
        var data = ExtensionLoader.ParseJson<LegacyCellIndices>(block.Payload);
        data.Validate(body, header, sites, armies);
        return data;
    }
    internal static byte[] WriteCellIndices(ReadOnlySpan<byte> body, IReadOnlyList<int> header,
        IReadOnlyList<LegacyBattleExtension> existing, LegacyCellIndices data, int sites, int armies)
    {
        data.Validate(body, header, sites, armies);
        var blocks = existing.Where(b => b.Tag != "CELL").ToList();
        blocks.Add(new("CELL", 1, 1, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(data, ExtensionLoader.Json)));
        return Container(body, blocks);
    }
    private static byte[] Container(ReadOnlySpan<byte> body, IReadOnlyList<LegacyBattleExtension> blocks)
    {
        if (blocks.Count == 0) return body.ToArray();
        long length = 16L + blocks.Sum(b => 16L + b.Payload.Length);
        if (blocks.Count > 64 || length > MaximumBytes) throw new RuleViolation("legacy.battle_extension", "BTL扩展超过预算");
        using var stream = new MemoryStream();stream.Write(body);stream.Write(Magic);
        using var writer = new BinaryWriter(stream);
        writer.Write(1u);writer.Write((uint)blocks.Count);
        foreach (var b in blocks)
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes(b.Tag));writer.Write(b.Version);writer.Write(b.Flags);writer.Write((uint)b.Payload.Length);writer.Write(b.Payload);
        }
        return stream.ToArray();
    }
}
