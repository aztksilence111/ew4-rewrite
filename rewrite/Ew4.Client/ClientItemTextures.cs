using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;

namespace Ew4.Client;

public sealed record ItemTextureProfile(int Schema = 1, long Revision = 0)
{
    public Dictionary<string, string> Custom { get; init; } = new(StringComparer.Ordinal);
}

public sealed partial class ClientGame
{
    private string ItemTextureDirectory => Path.Combine(saveDirectory, "item-textures");
    public static byte[] ReadItemTexture(string directory, string name)
    {
        if (!name.EndsWith(".png", StringComparison.Ordinal) || !PortraitHash(name[..^4])) throw new FileNotFoundException("无效物品贴图");
        var bytes = File.ReadAllBytes(Path.Combine(directory, "item-textures", name));
        if (bytes.Length > 1024 * 1024 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != name[..^4]) throw new IOException("物品贴图校验失败");
        return bytes;
    }

    private object ItemTextureGallery(ClientCommand command)
    {
        string path = Path.Combine(ItemTextureDirectory, "library.json");
        ItemTextureProfile profile = new();
        if (File.Exists(path))
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new RuleViolation("item_textures.profile", "物品图库清单过大");
            profile = JsonSerializer.Deserialize<ItemTextureProfile>(File.ReadAllBytes(path), Json)
                ?? throw new RuleViolation("item_textures.profile", "物品图库清单损坏");
            if (profile.Schema != 1 || profile.Revision < 0 || profile.Custom == null || profile.Custom.Count > 4096
                || profile.Custom.Any(p => string.IsNullOrWhiteSpace(p.Key) || !PortraitHash(p.Value)))
                throw new RuleViolation("item_textures.profile", "物品图库清单损坏");
        }
        // Duplicate legacy IDs share a resource name. Keep all IDs and original files intact.
        var entries = content.ItemDefinitions.GroupBy(e => (string)e.Attribute("name")!, StringComparer.Ordinal)
            .Select(g => new { id = g.Key + ".png", name = content.Label("name_" + g.Key),
                itemIds = g.Select(e => "ew4.item." + (int)e.Attribute("id")!).ToArray() }).ToArray();
        if (command.Op is not ("" or "list"))
        {
            if (command.KnownVersion != (saveIdentity ?? "builtin")) throw new RuleViolation("item_textures.identity", "内容包已切换，请重新打开物品图库");
            if (command.Revision != profile.Revision) throw new RuleViolation("item_textures.revision", "物品图库已更新，请重新打开后操作");
            if (!entries.Any(e => e.id == command.Item)) throw new RuleViolation("item_textures.item", "此物品贴图不存在");
            if (command.Op == "upload")
            {
                if (command.Texture == null || command.Texture.Length > 1400000) throw new RuleViolation("item_textures.image", "物品图片为空或过大");
                byte[] bytes;
                try { bytes = Convert.FromBase64String(command.Texture); }
                catch (FormatException) { throw new RuleViolation("item_textures.image", "无效图片编码"); }
                if (bytes.Length < 24 || bytes.Length > 1024 * 1024 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) != 128
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) != 128)
                    throw new RuleViolation("item_textures.image", "请在物品图库中调整图片后保存");
                try
                {
                    var image = StbImageSharp.ImageResult.FromMemory(bytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
                    if (image.Width != 128 || image.Height != 128) throw new InvalidDataException();
                    using var output = new MemoryStream();
                    new StbImageWriteSharp.ImageWriter().WritePng(image.Data, 128, 128, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
                    bytes = output.ToArray();
                }
                catch (Exception e) when (e is not OutOfMemoryException) { throw new RuleViolation("item_textures.image", "无法解码物品图片"); }
                string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
                var retained = profile.Custom.Where(p => p.Key != command.Item).Select(p => p.Value).Distinct(StringComparer.Ordinal);
                long existing = retained.Sum(h => new FileInfo(Path.Combine(ItemTextureDirectory, h + ".png")).Length);
                if (existing + bytes.Length > 32L * 1024 * 1024) throw new RuleViolation("item_textures.size", "物品图库已满，请先恢复部分物品的默认贴图");
                Directory.CreateDirectory(ItemTextureDirectory);
                File.WriteAllBytes(Path.Combine(ItemTextureDirectory, hash + ".png"), bytes);
                profile.Custom[command.Item!] = hash;
            }
            else if (command.Op == "remove") profile.Custom.Remove(command.Item!);
            else throw new RuleViolation("item_textures.command", "未知物品贴图操作");
            profile = profile with { Revision = checked(profile.Revision + 1) };
            Directory.CreateDirectory(ItemTextureDirectory);
            File.WriteAllBytes(path + ".tmp", JsonSerializer.SerializeToUtf8Bytes(profile, Json));
            File.Move(path + ".tmp", path, true);
            var hashes = profile.Custom.Values.ToHashSet(StringComparer.Ordinal);
            foreach (string file in Directory.EnumerateFiles(ItemTextureDirectory, "*.png"))
                if (PortraitHash(Path.GetFileNameWithoutExtension(file)) && !hashes.Contains(Path.GetFileNameWithoutExtension(file))) File.Delete(file);
        }
        return new { profile.Revision, identity = saveIdentity ?? "builtin", entries = entries.Select(e => new {
            e.id, e.name, e.itemIds, texture = profile.Custom.TryGetValue(e.id, out string? hash) ? "item-textures/" + hash + ".png" : null }) };
    }
}
