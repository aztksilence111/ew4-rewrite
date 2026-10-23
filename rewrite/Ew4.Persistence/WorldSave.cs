using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ew4.Core;

namespace Ew4.Persistence;

public sealed record SaveReadLimits(int MaximumBytes = 64 * 1024 * 1024, int MaximumDepth = 64);

public static class WorldSave
{
    public const int Version = 4;
    private const string Format = "ew4.rewrite.save";
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static byte[] Encode(WorldState state)
    {
        state.Validate();
        return EncodeValidated(state);
    }
    internal static byte[] EncodeValidated(WorldState state)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(state, Options);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", Version);
            writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(payload)));
            writer.WritePropertyName("payload");
            writer.WriteRawValue(payload, skipInputValidation: true);
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static WorldState Decode(byte[] bytes, string expectedContentRevision, SaveReadLimits? limits = null)
    {
        limits ??= new();
        if (limits.MaximumBytes < 1 || limits.MaximumDepth < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        if (bytes.Length > limits.MaximumBytes) throw new RuleViolation("save.size", "Save exceeds configured read budget");
        try
        {
            using var doc = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
            CheckUnique(doc.RootElement);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4
                || root.GetProperty("format").GetString() != Format)
                throw new RuleViolation("save.format", "Unknown save format");
            if (root.GetProperty("version").GetInt32() != Version)
                throw new RuleViolation("save.version", "Save version requires an explicit migration");
            var payloadElement = root.GetProperty("payload");
            foreach (string key in new[] { "scenarioId", "contentRevision", "width", "height", "round", "activeFaction", "revision",
                "randomState", "aiRandomState", "limits", "factions", "cells", "units", "commanders", "commanderOffers", "events", "triggeredEvents", "pendingDialogue", "queuedDialogues", "phase", "outcome" })
                if (!payloadElement.TryGetProperty(key, out _))
                    throw new RuleViolation("save.required_field", "Save payload missing " + key);
            foreach (var unit in payloadElement.GetProperty("units").EnumerateArray()) Require(unit, "objective");
            foreach (var cell in payloadElement.GetProperty("cells").EnumerateArray()) Require(cell, "blocksTerritoryExpansion");
            if (payloadElement.TryGetProperty("jurisdictions", out var jurisdictions) && jurisdictions.ValueKind != JsonValueKind.Null)
            {
                Require(jurisdictions, "cellCenters"); Require(jurisdictions, "borders");
                foreach (var border in jurisdictions.GetProperty("borders").EnumerateArray()) { Require(border, "first"); Require(border, "second"); }
            }
            foreach (var ev in payloadElement.GetProperty("events").EnumerateArray())
            {
                Require(ev, "rules");
                foreach (var rule in ev.GetProperty("rules").EnumerateArray())
                    foreach (string key in new[] { "trigger", "round", "unitId", "triggerCell", "requireCommander", "requireDifferentCamps", "effect", "factionId", "target", "value", "cell" })
                        Require(rule, key);
            }
            byte[] payload = System.Text.Encoding.UTF8.GetBytes(payloadElement.GetRawText());
            string expectedHash = Convert.ToHexStringLower(SHA256.HashData(payload));
            if (!string.Equals(root.GetProperty("sha256").GetString(), expectedHash, StringComparison.Ordinal))
                throw new RuleViolation("save.checksum", "Save payload checksum mismatch");
            var state = JsonSerializer.Deserialize<WorldState>(payload, Options)
                ?? throw new RuleViolation("save.payload", "Save has no world state");
            state.Validate();
            if (state.ContentRevision != expectedContentRevision)
                throw new RuleViolation("save.content_revision", "Save content revision does not match installed data");
            return state;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or NullReferenceException or ArgumentException)
        {
            throw new RuleViolation("save.malformed", "Malformed save: " + error.GetType().Name);
        }
    }

    internal static void Require(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out _)) throw new RuleViolation("save.required_field", "Save missing " + key);
    }

    internal static void CheckUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new RuleViolation("save.duplicate_property", "Ambiguous JSON property");
                CheckUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckUnique(item);
    }

    public static void WriteAtomic(string path, WorldState state)
    {
        WriteAtomicBytes(path, Encode(state));
    }

    internal static void WriteAtomicBytes(string path, byte[] bytes)
    {
        string target = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, target + ".bak");
            else File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static WorldState Read(string path, string expectedContentRevision, SaveReadLimits? limits = null)
    {
        limits ??= new();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (limits.MaximumBytes < 1 || stream.Length > limits.MaximumBytes)
            throw new RuleViolation("save.size", "Save exceeds configured read budget");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Decode(bytes, expectedContentRevision, limits);
    }
}
