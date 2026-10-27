using System.Buffers.Binary;
using System.Text;
using Ew4.Core;

namespace Ew4.Legacy;

internal static class LegacyBytes
{
    internal static int I32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt32LittleEndian(data[offset..]);
    internal static short I16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data[offset..]);
    internal static float F32(ReadOnlySpan<byte> data, int offset) => BitConverter.Int32BitsToSingle(I32(data, offset));
    internal static string Text(ReadOnlySpan<byte> data)
    {
        int end = data.IndexOf((byte)0);
        if (end < 0) throw new RuleViolation("legacy.string", "Unterminated fixed-width string");
        try { return new UTF8Encoding(false, true).GetString(data[..end]); }
        catch (DecoderFallbackException) { throw new RuleViolation("legacy.string", "Invalid UTF-8 resource string"); }
    }
    internal static int Area(int width, int height)
    {
        if (width < 1 || height < 1 || width > 32768 || height > 32768 || (long)width * height > 100_000_000)
            throw new RuleViolation("legacy.dimensions", "Invalid dimensions or resource reader budget exceeded");
        return width * height;
    }
}

public sealed record LegacyTerrainCell(byte BackgroundKind, byte BackgroundIndex, sbyte BackgroundX, sbyte BackgroundY,
    byte ForegroundKind, byte ForegroundIndex, sbyte ForegroundX, sbyte ForegroundY,
    byte RiverEdges, byte BlockedSeaEdges, byte Unknown10, byte Unknown11)
{
    public static LegacyTerrainCell Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 12) throw new RuleViolation("legacy.terrain_record", "Expected a 12-byte terrain record");
        return new(raw[0], raw[1], unchecked((sbyte)raw[2]), unchecked((sbyte)raw[3]), raw[4], raw[5],
            unchecked((sbyte)raw[6]), unchecked((sbyte)raw[7]), raw[8], raw[9], raw[10], raw[11]);
    }
}

public sealed class LegacyTerrainMap
{
    private readonly byte[] source;
    public int Width { get; }
    public int Height { get; }
    public IReadOnlyList<LegacyTerrainCell> Cells { get; }
    private LegacyTerrainMap(byte[] raw, int width, int height, LegacyTerrainCell[] cells)
    { source = raw; Width = width; Height = height; Cells = Array.AsReadOnly(cells); }
    public byte[] ToBytes() => (byte[])source.Clone();
    public static LegacyTerrainMap Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 16 || !raw[..4].SequenceEqual("YSAE"u8) || LegacyBytes.I32(raw, 4) != 1)
            throw new RuleViolation("legacy.terrain_header", "Unsupported or truncated terrain header");
        int width = LegacyBytes.I32(raw, 8), height = LegacyBytes.I32(raw, 12), count = LegacyBytes.Area(width, height);
        if (16L + (long)count * 12 != raw.Length) throw new RuleViolation("legacy.terrain_length", "Terrain size differs from dimensions");
        var cells = new LegacyTerrainCell[count];
        for (int i = 0; i < count; i++) cells[i] = LegacyTerrainCell.Read(raw.Slice(16 + i * 12, 12));
        return new(raw.ToArray(), width, height, cells);
    }
}

public sealed record LegacyFaction(Resources Resources, bool Ai, int Camp, int SurvivalMode, string CountryId,
    string Variant, float IncomeMultiplier, float AiEvasionBonus, int Color, IReadOnlyList<int> Values68,
    int Count172, int State176)
{
    public static LegacyFaction Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 180) throw new RuleViolation("legacy.faction_record", "Expected a 180-byte scenario faction record");
        var values = new int[26];
        for (int i = 0; i < values.Length; i++) values[i] = LegacyBytes.I32(raw, 68 + i * 4);
        return new(new(LegacyBytes.I32(raw, 0), LegacyBytes.I32(raw, 4), LegacyBytes.I32(raw, 8)),
            LegacyBytes.I32(raw, 12) != 0, LegacyBytes.I32(raw, 16), LegacyBytes.I32(raw, 20),
            LegacyBytes.Text(raw.Slice(24, 16)), LegacyBytes.Text(raw.Slice(40, 16)), LegacyBytes.F32(raw, 56),
            LegacyBytes.F32(raw, 60), LegacyBytes.I32(raw, 64), Array.AsReadOnly(values),
            LegacyBytes.I32(raw, 172), LegacyBytes.I32(raw, 176));
    }
}

public sealed record LegacySite(int Cell, short CityId, byte BuildingKind, byte BuildingLevel,
    byte State6, byte State7, sbyte State8, sbyte InstallationKind, byte Marker, sbyte State11,
    byte BridgeStyle0, byte BridgeStyle1, byte BridgeStyle2, byte State15)
{
    public byte BridgeEdges => (byte)(BridgeStyle0 | BridgeStyle1 | BridgeStyle2);
    public static LegacySite Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 16) throw new RuleViolation("legacy.site_record", "Expected a 16-byte site record");
        return new(LegacyBytes.I16(raw, 0), LegacyBytes.I16(raw, 2), raw[4], raw[5], raw[6], raw[7],
            unchecked((sbyte)raw[8]), unchecked((sbyte)raw[9]), raw[10], unchecked((sbyte)raw[11]), raw[12], raw[13], raw[14], raw[15]);
    }
}

public sealed record LegacyArmy(int Cell, sbyte Unit, sbyte Grade, short Health, short MaximumHealth,
    uint Experience, sbyte Veterancy, sbyte Movement, sbyte Morale, sbyte MoraleDuration, short Commander,
    byte Flag58, sbyte Objective, byte Behavior, byte Facing, byte Transport, byte Acted, byte ForceRecovery,
    byte Flag5c, sbyte State70, sbyte ConstructionTurns, short BehaviorExpiry, byte RestoreBehavior, sbyte Burning)
{
    public static LegacyArmy Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 32) throw new RuleViolation("legacy.army_record", "Expected a 32-byte army record");
        static sbyte S(ReadOnlySpan<byte> data, int index) => unchecked((sbyte)data[index]);
        return new(LegacyBytes.I16(raw, 0), S(raw, 2), S(raw, 3), LegacyBytes.I16(raw, 4), LegacyBytes.I16(raw, 6),
            BinaryPrimitives.ReadUInt32LittleEndian(raw[8..]), S(raw, 12), S(raw, 13), S(raw, 14), S(raw, 15), LegacyBytes.I16(raw, 16),
            raw[18], S(raw, 19), raw[20], raw[21], raw[22], raw[23], raw[24], raw[25], S(raw, 26), S(raw, 27),
            LegacyBytes.I16(raw, 28), raw[30], S(raw, 31));
    }
}

public sealed record LegacySection(string Name, int Offset, int Count, int Stride);

public sealed record LegacyEvent(int Id, int Condition, int Effect, int Round, int Dialogue, int Cell,
    string Country, bool Triggered, byte Padding41, byte Padding42, byte Padding43)
{
    public static LegacyEvent Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != 44 || raw[40] > 1) throw new RuleViolation("legacy.event_record", "Invalid 44-byte event record");
        return new(LegacyBytes.I32(raw, 0), LegacyBytes.I32(raw, 4), LegacyBytes.I32(raw, 8), LegacyBytes.I32(raw, 12),
            LegacyBytes.I32(raw, 16), LegacyBytes.I32(raw, 20), LegacyBytes.Text(raw.Slice(24, 16)), raw[40] != 0, raw[41], raw[42], raw[43]);
    }
}

public sealed class LegacyBattle
{
    private readonly byte[] source;
    private readonly LegacyBattleExtension[] extensions;
    public int LegacyLength { get; }
    public LegacyJurisdictionData? Jurisdictions { get; }
    public IReadOnlyList<int> Header { get; }
    public IReadOnlyList<LegacySection> Sections { get; }
    public IReadOnlyList<LegacyFaction> Factions { get; }
    public IReadOnlyList<LegacySite> Sites { get; }
    public IReadOnlyList<LegacySite> EffectiveSites { get; }
    public IReadOnlyList<LegacyArmy> Armies { get; }
    public IReadOnlyList<LegacyEvent> Events { get; }
    public IReadOnlyList<LegacyMarket> Markets { get; }
    public IReadOnlyList<LegacyTavern> Taverns { get; }
    public int MapId => Header[1];
    public Ew4.Core.ExtensionVictoryRules? Victory { get; }
    public LegacyCountryIds? CountryIds { get; }
    public LegacyCellIndices? CellIndices { get; }
    public int Left => Header[2];
    public int Top => Header[3];
    public int Width => Header[4];
    public int Height => Header[5];
    private LegacyBattle(byte[] raw, int[] header, LegacySection[] sections, LegacyFaction[] factions, LegacySite[] sites, LegacyArmy[] armies,
        LegacyEvent[] events, LegacyMarket[] markets, LegacyTavern[] taverns, int legacyLength, LegacyBattleExtension[] extensions)
    {
        source = raw; Header = Array.AsReadOnly(header); Sections = Array.AsReadOnly(sections);
        CountryIds = LegacyBattleExtensions.CountryIds(extensions, raw.AsSpan(0, legacyLength), factions, events);
        CountryIds?.Apply(factions, events);
        CellIndices = LegacyBattleExtensions.CellIndices(extensions, raw.AsSpan(0, legacyLength), header, sites.Length, armies.Length);
        if (CellIndices != null)
        {
            for (int i = 0; i < sites.Length; i++) sites[i] = sites[i] with { Cell = CellIndices.Sites[i] };
            for (int i = 0; i < armies.Length; i++) armies[i] = armies[i] with { Cell = CellIndices.Armies[i] };
        }
        Factions = Array.AsReadOnly(factions); Sites = Array.AsReadOnly(sites); Armies = Array.AsReadOnly(armies); Events = Array.AsReadOnly(events);
        EffectiveSites = Array.AsReadOnly(sites.GroupBy(s => s.Cell).Select(g => g.Last()).ToArray());
        Markets = Array.AsReadOnly(markets); Taverns = Array.AsReadOnly(taverns);
        LegacyLength = legacyLength; this.extensions = extensions;
        Jurisdictions = LegacyBattleExtensions.Jurisdictions(extensions, raw.AsSpan(0, legacyLength), Header);
        Victory = LegacyBattleExtensions.Victory(extensions, raw.AsSpan(0, legacyLength));
    }
    public byte[] ToBytes() => (byte[])source.Clone();
    public byte[] ToLegacyBytes() => source.AsSpan(0, LegacyLength).ToArray();
    public byte[] WithJurisdictions(LegacyJurisdictionData? data) => LegacyBattleExtensions.Write(source.AsSpan(0, LegacyLength), Header, extensions, data);
    public byte[] WithVictory(Ew4.Core.ExtensionVictoryRules? rules) => LegacyBattleExtensions.WriteVictory(source.AsSpan(0, LegacyLength), extensions, rules);
    public byte[] WithCountryIds(LegacyCountryIds? data) => LegacyBattleExtensions.WriteCountryIds(source.AsSpan(0, LegacyLength), extensions, data, Factions.Count, Events.Count);
    public byte[] WithCellIndices(LegacyCellIndices data) => LegacyBattleExtensions.WriteCellIndices(source.AsSpan(0, LegacyLength), Header, extensions, data, Sites.Count, Armies.Count);
    public byte[] SectionBytes(string name)
    {
        var section = Sections.Single(s => s.Name == name);
        return source.AsSpan(section.Offset, section.Count * section.Stride).ToArray();
    }
    public static LegacyBattle Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 72 || LegacyBytes.I32(raw, 0) != 2)
            throw new RuleViolation("legacy.battle_header", "Only verified scenario BTL version 2 is supported");
        var header = new int[18];
        for (int i = 0; i < header.Length; i++) header[i] = LegacyBytes.I32(raw, i * 4);
        int area = LegacyBytes.Area(header[4], header[5]);
        (string Name, int Count, int Stride)[] layout = [("factions", header[6], 180), ("ownership", area, 1),
            ("sites", header[7], 16), ("armies", header[8], 32), ("opaque8", header[16], 8),
            ("opaque116", header[10], 116), ("opaque108", header[14], 108), ("opaque44", header[9], 44)];
        long cursor = 72;
        var sections = new List<LegacySection>();
        foreach (var entry in layout)
        {
            long length = (long)entry.Count * entry.Stride;
            if (entry.Count < 0 || cursor + length > raw.Length)
                throw new RuleViolation("legacy.battle_section", "Invalid or truncated section: " + entry.Name);
            sections.Add(new(entry.Name, (int)cursor, entry.Count, entry.Stride));
            cursor += length;
        }
        var extensions = LegacyBattleExtensions.Read(raw[(int)cursor..]);
        var factions = new LegacyFaction[header[6]];
        var sites = new LegacySite[header[7]];
        var armies = new LegacyArmy[header[8]];
        var events = new LegacyEvent[header[9]];
        var markets = new LegacyMarket[header[10]];
        var taverns = new LegacyTavern[header[14]];
        for (int i = 0; i < factions.Length; i++) factions[i] = LegacyFaction.Read(raw.Slice(sections[0].Offset + i * 180, 180));
        for (int i = 0; i < sites.Length; i++) sites[i] = LegacySite.Read(raw.Slice(sections[2].Offset + i * 16, 16));
        for (int i = 0; i < armies.Length; i++) armies[i] = LegacyArmy.Read(raw.Slice(sections[3].Offset + i * 32, 32));
        for (int i = 0; i < events.Length; i++) events[i] = LegacyEvent.Read(raw.Slice(sections[7].Offset + i * 44, 44));
        for (int i = 0; i < markets.Length; i++) markets[i] = LegacyMarket.Read(raw.Slice(sections[5].Offset + i * 116, 116));
        for (int i = 0; i < taverns.Length; i++) taverns[i] = LegacyTavern.Read(raw.Slice(sections[6].Offset + i * 108, 108));
        return new(raw.ToArray(), header, sections.ToArray(), factions, sites, armies, events, markets, taverns, (int)cursor, extensions);
    }
}
