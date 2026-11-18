using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;

internal static class ItemTextureChecks
{
    internal static int Run(string root, string output)
    {
        int checks = 0;
        void Check(bool pass, string label) { checks++; if (!pass) throw new Exception("Item textures: " + label); }
        byte[] Read(string path) => File.ReadAllBytes(Path.Combine(root, "rewrite/data", path.StartsWith("data/") ? "legacy/" + path[5..] : path));
        var content = new ClientContent(Read, ["conquest1.btl"]);
        string profile = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(profile);
        byte[] originalAccount = [1, 2, 255, 0]; File.WriteAllBytes(Path.Combine(profile, "account.json"), originalAccount);
        var client = new ClientGame(content, profile, saveIdentity: "builtin");
        JsonElement Call(object args, string? error = null)
        {
            using var response = JsonDocument.Parse(client.Dispatch("/api/item-textures", JsonSerializer.Serialize(args, ClientGame.Json)));
            var result = response.RootElement;
            Check(result.GetProperty("ok").GetBoolean() == (error == null), result.GetRawText());
            if (error != null) Check(result.GetProperty("code").GetString() == error, "expected " + error);
            return result.GetProperty(error == null ? "data" : "code").Clone();
        }
        byte[] Png(int size, byte red)
        {
            var pixels = new byte[size * size * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = red; pixels[i + 3] = i % 8 == 0 ? (byte)0 : (byte)255; }
            using var stream = new MemoryStream();
            new StbImageWriteSharp.ImageWriter().WritePng(pixels, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, stream); return stream.ToArray();
        }
        var view = Call(new { });
        Check(view.GetProperty("entries").GetArrayLength() == 54, "duplicate legacy IDs share 54 texture entries");
        var horse = view.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "War Horse.png");
        Check(horse.GetProperty("itemIds").GetArrayLength() == 4 && horse.GetProperty("itemIds")[1].GetString() == "ew4.item.10001", "legacy IDs retain their inherited resource");
        byte[] texture = Png(128, 99);
        view = Call(new { op = "upload", item = "War Horse.png", revision = 0, knownVersion = "builtin", texture = Convert.ToBase64String(texture) });
        var selected = view.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("id").GetString() == "War Horse.png");
        string url = selected.GetProperty("texture").GetString()!;
        byte[] saved = ClientGame.ReadItemTexture(profile, url[14..]);
        var decoded = StbImageSharp.ImageResult.FromMemory(saved, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
        Check(decoded.Width == 128 && decoded.Data[3] == 0 && decoded.Data[7] == 255, "uploaded PNG preserves transparent pixels");
        Call(new { op = "remove", item = "War Horse.png", revision = 0, knownVersion = "builtin" }, "item_textures.revision");
        Call(new { op = "remove", item = "War Horse.png", revision = 1, knownVersion = "mods/other" }, "item_textures.identity");
        Call(new { op = "upload", item = "../account.json", revision = 1, knownVersion = "builtin", texture = Convert.ToBase64String(texture) }, "item_textures.item");
        Call(new { op = "upload", item = "War Horse.png", revision = 1, knownVersion = "builtin", texture = Convert.ToBase64String(Png(256, 33)) }, "item_textures.image");
        Call(new { op = "upload", item = "War Horse.png", revision = 1, knownVersion = "builtin", texture = "bad" }, "item_textures.image");
        Check(Directory.GetFiles(Path.Combine(profile, "item-textures"), "*.png").Length == 1, "failed uploads leave no extra files");
        client = new ClientGame(content, profile, saveIdentity: "builtin");
        view = Call(new { });
        Check(view.GetProperty("revision").GetInt64() == 1 && view.GetProperty("entries").EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "War Horse.png").GetProperty("texture").GetString() == url, "selection survives host restart");
        string otherProfile = Path.Combine(output, "other-" + Guid.NewGuid().ToString("N"));
        var other = new ClientGame(content, otherProfile, saveIdentity: "mods/sample/logic");
        using (var otherView = JsonDocument.Parse(other.Dispatch("/api/item-textures", "{}")))
            Check(otherView.RootElement.GetProperty("data").GetProperty("entries").EnumerateArray().All(e => e.GetProperty("texture").ValueKind == JsonValueKind.Null), "other content profiles retain their own default textures");
        view = Call(new { op = "remove", item = "War Horse.png", revision = 1, knownVersion = "builtin" });
        Check(view.GetProperty("entries").EnumerateArray().All(e => e.GetProperty("texture").ValueKind == JsonValueKind.Null)
            && Directory.GetFiles(Path.Combine(profile, "item-textures"), "*.png").Length == 0, "restore default removes only the unused custom texture");
        Check(File.ReadAllBytes(Path.Combine(profile, "account.json")).SequenceEqual(originalAccount), "gallery neither requires nor rewrites account data");
        try { ClientGame.ReadItemTexture(profile, "../account.json"); throw new Exception("Traversal accepted"); } catch (FileNotFoundException) { checks++; }
        return checks;
    }
}
