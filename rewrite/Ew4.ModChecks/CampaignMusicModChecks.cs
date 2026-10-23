using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class CampaignMusicModChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        void Check(bool pass, string message) { checks++; if (!pass) throw new InvalidOperationException(message); }
        string run = Path.Combine(root, "build/campaign-music-20261023/mod-checks", Guid.NewGuid().ToString("N"));
        string data = Path.Combine(root, "rewrite/data");
        var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/")
            ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
            Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
        var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
        JsonElement Catalog()
        {
            using var response = JsonDocument.Parse(store.Dispatch("/api/catalog", "{}"));
            Check(response.RootElement.GetProperty("ok").GetBoolean(), "catalog succeeds");
            return response.RootElement.GetProperty("data").Clone();
        }
        int Hidden() => Catalog().GetProperty("presentations").EnumerateObject().Count(p => p.Value.GetProperty("hidden").GetBoolean());
        Check(Hidden() == 0, "builtin store exposes hidden campaigns initially");
        string author = Path.Combine(run, "author");
        string example = Path.Combine(root, "rewrite/mod-kit/examples/campaign-visibility");
        foreach (string file in Directory.GetFiles(Path.Combine(example, "rewrite"), "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(author, Path.GetRelativePath(example, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        store.Seal(author, "campaign-visibility", "visibility", "1.0");
        var imported = store.Import(author, "ignored", "ignored");store.Activate(imported.Key);
        Check(Hidden() == 11, "packaged original visibility restores 11 hidden campaigns");
        using (var blocked = JsonDocument.Parse(store.Dispatch("/api/start", "{\"scenario\":\"campaign5_05\"}")))
            Check(!blocked.RootElement.GetProperty("ok").GetBoolean() && blocked.RootElement.GetProperty("code").GetString() == "campaign.locked", "packaged hidden policy gates backend");
        string archive = Path.Combine(run, "visibility.ew4mod");store.Export(imported.Key, archive);
        var offline = new ModStore(baseline, Path.Combine(run, "offline-mods"), Path.Combine(run, "offline-saves"));
        offline.Activate(offline.Import(archive, "ignored", "ignored").Key);
        using (var result = JsonDocument.Parse(offline.Dispatch("/api/catalog", "{}")))
            Check(result.RootElement.GetProperty("data").GetProperty("presentations").GetProperty("campaign5_05").GetProperty("hidden").GetBoolean(), "offline export retains visibility configuration");
        store.Activate(null);Check(Hidden() == 0, "deactivation restores builtin visibility");

        string musicAuthor = Path.Combine(run, "music-author");Directory.CreateDirectory(Path.Combine(musicAuthor, "client/music"));
        File.Copy(Path.Combine(data, "client/music/battle2.mp3"), Path.Combine(musicAuthor, "client/music/custom.mp3"));
        string configPath = Path.Combine(musicAuthor, "client/music.json");
        var configuration = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "client/music.json")))!;
        configuration["menu"] = "custom.mp3";File.WriteAllText(configPath, configuration.ToJsonString());
        store.Seal(musicAuthor, "music", "music", "1.0");var music = store.Import(musicAuthor, "ignored", "ignored");store.Activate(music.Key);
        Check(JsonNode.Parse(store.ReadContent("music.json"))!["menu"]!.GetValue<string>() == "custom.mp3", "music overlay selected configuration");
        Check(store.ReadContent("music/custom.mp3").SequenceEqual(File.ReadAllBytes(Path.Combine(data, "client/music/battle2.mp3"))), "music overlay serves actual audio bytes");
        foreach (string bad in new[] { "missing.mp3", "../battle1.mp3", "custom.exe" })
        {
            configuration["menu"] = bad;File.WriteAllText(configPath, configuration.ToJsonString());store.Seal(musicAuthor, "music", "music", "bad");
            bool rejected = false;
            try { store.Validate(musicAuthor); } catch (Exception error) when (error is InvalidDataException or ArgumentException) { rejected = true; }
            Check(rejected, "invalid music reference rejected: " + bad);
        }
        string legacy = Path.Combine(run, "legacy");Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy, "global_data.xml"), "<GlobalDatas><FileConst><data name=\"BGMusic\" type=\"string\" value=\"custom.mp3\"/></FileConst></GlobalDatas>");
        File.Copy(Path.Combine(data, "client/music/battle3.mp3"), Path.Combine(legacy, "custom.mp3"));
        var legacyMusic = store.Import(legacy, "legacy-music", "legacy-music");store.Activate(legacyMusic.Key);
        Check(JsonNode.Parse(store.ReadContent("music.json"))!["menu"]!.GetValue<string>() == "custom.mp3", "legacy BGMusic maps to menu configuration");
        Check(store.ReadContent("music/custom.mp3").SequenceEqual(File.ReadAllBytes(Path.Combine(data, "client/music/battle3.mp3"))), "legacy renamed track survives import");
        Check(Hidden() == 11, "legacy mod preserves XML hiding without an extension");
        return checks;
    }
}
