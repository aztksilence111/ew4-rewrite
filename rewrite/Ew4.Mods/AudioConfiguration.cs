using System.Text.Json.Nodes;

namespace Ew4.Mods;

internal static class AudioConfiguration
{
    internal static void Validate(Func<string, byte[]> read, HashSet<string> files)
    {
        string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text : throw new InvalidDataException("Audio reference must be a string");
        void Filename(string name)
        {
            ModFiles.PathName(name);
            if (name.Contains('/') || name.Contains('\\') || !new[] { ".mp3", ".ogg", ".wav" }.Contains(Path.GetExtension(name)))
                throw new InvalidDataException("Invalid audio filename: " + name);
        }
        void Track(JsonNode? node, string folder)
        {
            string name = Text(node); Filename(name);
            if (!files.Contains("client/" + folder + "/" + name)) throw new InvalidDataException("Missing audio: " + folder + "/" + name);
        }
        void Playlist(JsonNode? node)
        {
            // Null or an empty list explicitly silences an entry; an absent optional entry inherits.
            if (node == null) return;
            if (node is not JsonArray tracks) { Track(node, "music"); return; }
            if (tracks.Count > 64) throw new InvalidDataException("Music playlist exceeds 64 tracks");
            foreach (var track in tracks) Track(track, "music");
        }
        if (files.Contains("client/music.json"))
        {
            var music = ModFiles.Object(read("client/music.json"));
            if ((int?)music["schema"] != 1 || !music.ContainsKey("menu") || !music.ContainsKey("battle") || !music.ContainsKey("defeat")
                || music.Any(p => p.Key is not ("schema" or "menu" or "battle" or "defeat" or "victory" or "scenarios" or "playlistMode")))
                throw new InvalidDataException("Invalid music configuration");
            if (music.ContainsKey("playlistMode") && Text(music["playlistMode"]) is not ("loop" or "sequence"))
                throw new InvalidDataException("Invalid music playlist mode");
            foreach (string entry in new[] { "menu", "battle", "defeat", "victory" }) Playlist(music[entry]);
            if (music.ContainsKey("scenarios"))
            {
                if (music["scenarios"] is not JsonObject scenarios || scenarios.Count > 2048)
                    throw new InvalidDataException("Invalid scenario music configuration");
                foreach (var (id, node) in scenarios)
                {
                    ModFiles.PathName(id);
                    if (id.Contains('/') || id.Contains('.') || node is not JsonObject entries
                        || entries.Any(p => p.Key is not ("battle" or "victory" or "defeat")))
                        throw new InvalidDataException("Invalid scenario music: " + id);
                    foreach (var (_, entry) in entries) Playlist(entry);
                }
            }
        }
        if (files.Contains("client/audio.json"))
        {
            var audio = ModFiles.Object(read("client/audio.json"));
            if ((int?)audio["schema"] != 1 || audio["sounds"] is not JsonObject sounds || sounds.Count > 4096
                || audio.Any(p => p.Key is not ("schema" or "sounds")))
                throw new InvalidDataException("Invalid sound configuration");
            foreach (var (alias, target) in sounds)
            {
                ModFiles.PathName(alias);
                if (alias.Contains('/') || alias.Contains('\\')) throw new InvalidDataException("Invalid sound alias: " + alias);
                if (Text(target) != "") Track(target, "sounds");
            }
        }
    }
}
