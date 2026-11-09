using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class CustomAudioModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException(message); }
        string run = Path.Combine(root, "build/custom-audio-20261109/fixtures", Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "rewrite/data"), author = Path.Combine(run, "author");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/")
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        string example = Path.Combine(root, "rewrite/mod-kit/examples/custom-audio/client");
        Directory.CreateDirectory(Path.Combine(author, "client/music"));Directory.CreateDirectory(Path.Combine(author, "client/sounds"));
        foreach (string file in Directory.GetFiles(example, "*.json")) File.Copy(file, Path.Combine(author, "client", Path.GetFileName(file)), true);
        store.Seal(author, "custom-audio", "custom-audio", "1.0", scenarios: ["campaign1_01"]);
        string logic = store.Validate(author).LogicRevision;
        var music = JsonNode.Parse(File.ReadAllText(Path.Combine(author, "client/music.json")))!.AsObject();
        var audio = JsonNode.Parse(File.ReadAllText(Path.Combine(author, "client/audio.json")))!.AsObject();
        byte[] bgm = File.ReadAllBytes(Path.Combine(data, "client/music/battle2.mp3"));
        byte[] click = File.ReadAllBytes(Path.Combine(data, "client/sounds/sfx_click.wav"));
        File.WriteAllBytes(Path.Combine(author, "client/music/新增配乐.mp3"), bgm);
        File.WriteAllBytes(Path.Combine(author, "client/sounds/自定义点击.wav"), click);
        music["menu"] = new JsonArray("新增配乐.mp3", "battle1.mp3");
        music["scenarios"]!["conquest1"]!["defeat"] = null;
        audio["sounds"]!["sfx_click.wav"] = "自定义点击.wav";audio["sounds"]!["sfx_pop.wav"] = "";
        void Save()
        {
            File.WriteAllText(Path.Combine(author, "client/music.json"), music.ToJsonString());
            File.WriteAllText(Path.Combine(author, "client/audio.json"), audio.ToJsonString());
            store.Seal(author, "custom-audio", "custom-audio", "1.1", scenarios: ["campaign1_01"]);
        }
        Save();Check(store.Validate(author).LogicRevision == logic, "audio additions preserve rule/save namespace");
        var imported = store.Import(author, "ignored", "ignored");store.Activate(imported.Key);
        Check(store.ReadContent("music/新增配乐.mp3").SequenceEqual(bgm), "new BGM bytes served");
        Check(store.ReadContent("sounds/自定义点击.wav").SequenceEqual(click), "custom click bytes served");
        Check(JsonNode.Parse(store.ReadContent("audio.json"))!["sounds"]!["sfx_click.wav"]!.GetValue<string>() == "自定义点击.wav", "sound mapping survives activation");
        Check(JsonNode.Parse(store.ReadContent("music.json"))!["scenarios"]!["conquest1"]!["defeat"] == null, "explicit silent scenario survives activation");
        string exported = Path.Combine(run, "audio.ew4mod");store.Export(imported.Key, exported);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        offline.Activate(offline.Import(exported, "ignored", "ignored").Key);
        foreach (string path in new[] { "music.json", "audio.json", "music/新增配乐.mp3", "sounds/自定义点击.wav" })
            Check(offline.ReadContent(path).SequenceEqual(store.ReadContent(path)), "export/import round trip: " + path);
        store.Activate(null);
        Check(JsonNode.Parse(store.ReadContent("audio.json"))!["sounds"]!.AsObject().Count == 0, "deactivation restores builtin sounds");
        Check(JsonNode.Parse(store.ReadContent("music.json"))!["menu"]!.GetValue<string>() == "battle1.mp3", "deactivation restores builtin music");
        void Reject(Action mutate, string message)
        {
            string savedMusic = music.ToJsonString(), savedAudio = audio.ToJsonString();mutate();Save();
            bool rejected = false;
            try { AudioConfiguration.Validate(p => File.Exists(Path.Combine(author, p)) ? File.ReadAllBytes(Path.Combine(author, p)) : baseline.Read(p),
                baseline.Files.Concat(Directory.EnumerateFiles(author, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(author, p).Replace('\\', '/'))).ToHashSet(StringComparer.Ordinal)); }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or FormatException) { rejected = true; }
            Check(rejected, message);music = JsonNode.Parse(savedMusic)!.AsObject();audio = JsonNode.Parse(savedAudio)!.AsObject();
        }
        foreach (string bad in new[] { "missing.mp3", "../battle1.mp3", "sub/battle1.mp3", "x.exe", "", "https://test/x.mp3" })
            Reject(() => music["victory"] = bad, "invalid BGM rejected: " + bad);
        Reject(() => music["menu"] = new JsonArray((JsonNode?)null), "null item rejected");
        Reject(() => music["battle"] = new JsonArray(Enumerable.Range(0, 65).Select(_ => (JsonNode?)JsonValue.Create("battle1.mp3")).ToArray()), "oversized playlist rejected");
        Reject(() => music["playlistMode"] = "unknown", "unknown playlist mode rejected");
        Reject(() => music["scenarios"]!["conquest1"]!["typo"] = "battle1.mp3", "unknown scenario entry rejected");
        Reject(() => music["scenarios"]!["../bad"] = new JsonObject(), "scenario traversal rejected");
        Reject(() => music["unexpected"] = true, "unknown music field rejected");
        foreach (string bad in new[] { "missing.wav", "../sfx_click.wav", "sub/sfx_click.wav", "x.exe" })
            Reject(() => audio["sounds"]!["sfx_click.wav"] = bad, "invalid effect target rejected: " + bad);
        Reject(() => audio["sounds"]!["sfx_click.wav"] = null, "null effect target rejected");
        Reject(() => audio["sounds"]!["../alias"] = "sfx_click.wav", "effect alias traversal rejected");
        Reject(() => audio["sounds"] = new JsonArray(), "non-object mapping rejected");
        Reject(() => audio["unexpected"] = true, "unknown sound field rejected");
        music["menu"] = null;music["battle"] = new JsonArray();music["defeat"] = null;Save();
        Check(store.Validate(author).LogicRevision == logic, "silent music entries accepted without logic change");
        return checks;
    }
}
