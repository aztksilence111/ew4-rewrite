using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Persistence;

public sealed record SessionContentBinding(string Session, string World, string Account);

public static class SessionSave
{
    public const int Version = 2;
    private const string Format = "ew4.rewrite.session";
    private sealed record Payload(string ContentRevision, long Revision, string PlayerFaction, JsonElement World,
        PlayerAccount Account, IReadOnlyList<SessionMarket> Markets, IReadOnlyList<TavernCandidate> TavernCandidates,
        IReadOnlyList<RosterDeploymentState> RosterDeployments, string BattleId = "", BattleResult? BattleResult = null, long CollectedMedals = 0);

    public static byte[] Encode(GameSession state)
    {
        state.Validate();
        // Write the authenticated nested world directly, avoiding a complete DOM parse
        // and re-encoding of every cell and unit on each autosave.
        using var payloadStream = new SaveBuffer();
        using (var data = new Utf8JsonWriter(payloadStream, new JsonWriterOptions { Encoder = WorldSave.Options.Encoder }))
        {
            data.WriteStartObject();
            data.WriteString("contentRevision", state.ContentRevision); data.WriteNumber("revision", state.Revision);
            data.WriteString("playerFaction", state.PlayerFaction);
            data.WritePropertyName("world"); WorldSave.WriteValidated(data, state.World);
            void Property<T>(string name, T value) { data.WritePropertyName(name); JsonSerializer.Serialize(data, value, WorldSave.Options); }
            Property("account", state.Account); Property("markets", state.Markets); Property("tavernCandidates", state.TavernCandidates);
            Property("rosterDeployments", state.RosterDeployments); data.WriteString("battleId", state.BattleId);
            Property("battleResult", state.BattleResult); data.WriteNumber("collectedMedals", state.CollectedMedals);
            data.WriteEndObject();
        }
        var payload = payloadStream.WrittenSpan;
        using var stream = new SaveBuffer();
        using (var writer = new Utf8JsonWriter(stream))
        {
            SaveBuffer.Envelope(writer, Format, Version, payload);
        }
        return stream.WrittenSpan.ToArray();
    }
    public static GameSession Decode(byte[] bytes, SessionContentBinding expected, SaveReadLimits? limits = null)
    {
        limits ??= new();
        if (limits.MaximumBytes < 1 || limits.MaximumDepth < 1) throw new ArgumentOutOfRangeException(nameof(limits));
        if (bytes.Length > limits.MaximumBytes) throw new RuleViolation("save.size", "Session exceeds configured read budget");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
            var root = document.RootElement; WorldSave.CheckUnique(root);
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 4 || root.GetProperty("format").GetString() != Format)
                throw new RuleViolation("save.format", "Unknown session format");
            if (root.GetProperty("version").GetInt32() != Version) throw new RuleViolation("save.version", "Session requires an explicit migration");
            var data = root.GetProperty("payload");
            foreach (string key in new[] { "contentRevision", "revision", "playerFaction", "world", "account", "markets", "tavernCandidates", "rosterDeployments" }) WorldSave.Require(data, key);
            foreach (var quota in data.GetProperty("rosterDeployments").EnumerateArray())
                foreach (string key in new[] { "faction", "limit", "commanderIds" }) WorldSave.Require(quota, key);
            var account = data.GetProperty("account");
            foreach (string key in new[] { "id", "contentRevision", "revision", "medals", "inventory", "roster" }) WorldSave.Require(account, key);
            foreach (var commander in account.GetProperty("roster").EnumerateArray())
            {
                foreach (string key in new[] { "id", "definitionId", "learning", "training", "items" }) WorldSave.Require(commander, key);
                var learning = commander.GetProperty("learning");
                foreach (string key in new[] { "rank", "title", "abilities" }) WorldSave.Require(learning, key);
                foreach (string key in new[] { "rank", "title" })
                { WorldSave.Require(learning.GetProperty(key), "level"); WorldSave.Require(learning.GetProperty(key), "experience"); }
            }
            foreach (var market in data.GetProperty("markets").EnumerateArray())
            {
                WorldSave.Require(market, "cell"); WorldSave.Require(market, "market");
                var value = market.GetProperty("market"); WorldSave.Require(value, "id"); WorldSave.Require(value, "offers");
                foreach (var offer in value.GetProperty("offers").EnumerateArray())
                    foreach (string key in new[] { "id", "itemId", "remaining" }) WorldSave.Require(offer, key);
            }
            foreach (var candidate in data.GetProperty("tavernCandidates").EnumerateArray())
            {
                foreach (string key in new[] { "id", "commanderId", "cell", "availableRound", "cost", "medals" }) WorldSave.Require(candidate, key);
                foreach (string key in new[] { "money", "industry", "food" }) WorldSave.Require(candidate.GetProperty("cost"), key);
            }
            byte[] payload = Encoding.UTF8.GetBytes(data.GetRawText());
            if (root.GetProperty("sha256").GetString() != Convert.ToHexStringLower(SHA256.HashData(payload)))
                throw new RuleViolation("save.checksum", "Session payload checksum mismatch");
            var parsed = JsonSerializer.Deserialize<Payload>(payload, WorldSave.Options) ?? throw new RuleViolation("save.payload", "Missing session state");
            if (parsed.ContentRevision != expected.Session || parsed.Account.ContentRevision != expected.Account)
                throw new RuleViolation("save.content_revision", "Session/account data differs from installed content");
            var world = WorldSave.Decode(Encoding.UTF8.GetBytes(parsed.World.GetRawText()), expected.World, limits);
            var result = new GameSession { ContentRevision = parsed.ContentRevision, Revision = parsed.Revision, PlayerFaction = parsed.PlayerFaction,
                World = world, Account = parsed.Account, Markets = parsed.Markets, TavernCandidates = parsed.TavernCandidates, RosterDeployments = parsed.RosterDeployments,
                BattleId = parsed.BattleId, BattleResult = parsed.BattleResult, CollectedMedals = parsed.CollectedMedals };
            result.Validate(); return result;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException
            or FormatException or OverflowException or NullReferenceException or ArgumentException)
        { throw new RuleViolation("save.malformed", "Malformed session: " + error.GetType().Name); }
    }
    public static void WriteAtomic(string path, GameSession state) => WorldSave.WriteAtomicBytes(path, Encode(state));
    public static GameSession Read(string path, SessionContentBinding expected, SaveReadLimits? limits = null)
    {
        limits ??= new();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (limits.MaximumBytes < 1 || stream.Length > limits.MaximumBytes) throw new RuleViolation("save.size", "Session exceeds configured read budget");
        var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
        return Decode(bytes, expected, limits);
    }
}
