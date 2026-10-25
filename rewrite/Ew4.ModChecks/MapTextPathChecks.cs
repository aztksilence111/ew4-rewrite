using System.Text.Json.Nodes;
using Ew4.Mods;

internal static class MapTextPathChecks
{
    internal static int Run(string root)
    {
        int checks = 0;
        JsonObject Map(string path) => JsonNode.Parse("{\"labels\":[{\"text\":\"TEST SEA\",\"size\":50,\"textPath\":" + path + "}]}")!.AsObject();
        void Accept(JsonObject map) { ResourceValidation.CheckMapLabelPaths(map); checks++; }
        void Reject(JsonObject map)
        {
            try { ResourceValidation.CheckMapLabelPaths(map); }
            catch (InvalidDataException) { checks++; return; }
            throw new Exception("Invalid map text path accepted");
        }
        Accept(new JsonObject());
        Accept(JsonNode.Parse("{\"labels\":[{\"libName\":\"legacy\"}]}")!.AsObject());
        Accept(Map("[[-300,0],[0,100],[300,0]]"));
        Accept(Map("[[-300,0],[-150,100],[0,0],[150,-100],[300,0]]"));
        foreach (string path in new[] { "[]", "[[0,0],[1,1]]", "[[0,0],[1,1],[2,2],[3,3]]",
            "[[0,0],[0,0],[0,0]]", "[[0,0],[1],[2,2]]", "[[0,0],[\"x\",0],[2,2]]",
            "[[0,0],[1e99,0],[2,2]]", "[[0,0],[9000,0],[2,2]]" }) Reject(Map(path));
        var badSize = Map("[[0,0],[1,1],[2,0]]"); badSize["labels"]![0]!["size"] = 0; Reject(badSize);
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "rewrite/data/client/manifest.json")))!.AsObject();
        foreach (var (_, map) in manifest["maps"]!.AsObject()) Accept(map!.AsObject());
        return checks;
    }
}
