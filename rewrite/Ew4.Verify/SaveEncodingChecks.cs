using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ew4.Client;
using Ew4.Core;
using Ew4.Persistence;

internal static class SaveEncodingChecks
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver {
            Modifiers = { info => {
                if (info.Type == typeof(MovementCell)) {
                    info.Properties.Single(p => p.Name == "barrier").ShouldSerialize = (_, value) => (TerrainBarrier)value! != TerrainBarrier.None;
                    info.Properties.Single(p => p.Name == "minimumEntryCost").ShouldSerialize = (_, value) => (int)value! != 0;
                }
            } }
        },
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    // Frozen reference encoder from before the pooled-buffer change.
    private static byte[] Envelope(string format, int version, byte[] payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("format", format); writer.WriteNumber("version", version);
            writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(payload)));
            writer.WritePropertyName("payload"); writer.WriteRawValue(payload, true); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
    private static byte[] World(WorldState world) => Envelope("ew4.rewrite.save", WorldSave.Version, JsonSerializer.SerializeToUtf8Bytes(world, Options));
    private static byte[] Session(GameSession state)
    {
        using var payload = new MemoryStream();
        using (var data = new Utf8JsonWriter(payload, new JsonWriterOptions { Encoder = Options.Encoder }))
        {
            data.WriteStartObject();data.WriteString("contentRevision", state.ContentRevision);data.WriteNumber("revision",state.Revision);
            data.WriteString("playerFaction",state.PlayerFaction);data.WritePropertyName("world");data.WriteRawValue(World(state.World),true);
            void Property<T>(string name,T value){data.WritePropertyName(name);JsonSerializer.Serialize(data,value,Options);}
            Property("account",state.Account);Property("markets",state.Markets);Property("tavernCandidates",state.TavernCandidates);
            Property("rosterDeployments",state.RosterDeployments);data.WriteString("battleId",state.BattleId);
            Property("battleResult",state.BattleResult);data.WriteNumber("collectedMedals",state.CollectedMedals);data.WriteEndObject();
        }
        return Envelope("ew4.rewrite.session",SessionSave.Version,payload.ToArray());
    }
    internal static int Run(string root)
    {
        int checks=0;
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        byte[] Read(string path)=>File.ReadAllBytes(Path.Combine(root,"rewrite/data",path.StartsWith("data/")?"legacy/"+path[5..]:path));
        var content=new ClientContent(Read,["conquest1.btl"]);
        var original=content.Start("conquest1","fra").Session;
        var binding=new SessionContentBinding(original.ContentRevision,original.World.ContentRevision,original.Account.ContentRevision);
        for(int i=0;i<12;i++)
        {
            var state=original with { BattleId="保存<>&\"\\"+i,Revision=i,World=original.World with { Revision=i,RandomState=(ulong)i } };
            byte[] expected=Session(state),encoded=SessionSave.Encode(state);
            Check(expected.SequenceEqual(encoded),"Session encoding changed authenticated bytes");
            Check(World(state.World).SequenceEqual(WorldSave.Encode(state.World)),"World encoding changed authenticated bytes");
            Check(encoded.SequenceEqual(SessionSave.Encode(SessionSave.Decode(expected,binding))),"Old encoder save no longer roundtrips");
            var parallel=Enumerable.Range(0,8).AsParallel().Select(_=>SessionSave.Encode(state)).ToArray();
            Check(parallel.All(bytes=>bytes.SequenceEqual(expected)),"Save buffer is shared across concurrent encodes");
        }
        string folder=Path.Combine(root,"build/device-plan-20261024/save-checks");Directory.CreateDirectory(folder);
        string target=Path.Combine(folder,"atomic-session.json");
        SessionSave.WriteAtomic(target,original);SessionSave.WriteAtomic(target,original with { Revision=original.Revision+1 });
        Check(SessionSave.Read(target,binding).Revision==original.Revision+1&&SessionSave.Read(target+".bak",binding).Revision==original.Revision,"Atomic save/backup changed");
        byte[] committed=File.ReadAllBytes(target);
        try{SessionSave.WriteAtomic(target,original with { Revision=-1 });throw new InvalidOperationException("Invalid save accepted");}catch(RuleViolation){}
        Check(File.ReadAllBytes(target).SequenceEqual(committed),"Failed validation overwrites committed save");
        Check(Directory.GetFiles(folder,"*.tmp").Length==0,"Temporary save leaked");
        return checks;
    }
}
