using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Core;
using Ew4.Legacy;

namespace Ew4.Client;

public sealed record UploadedPortrait(string Portrait, string Marker);
public sealed record PortraitProfile(int Schema = 1, long Revision = 0)
{
    public Dictionary<string, UploadedPortrait> Custom { get; init; } = new(StringComparer.Ordinal);
    // Absent = follow automatic skins; empty = original; custom = uploaded image.
    public Dictionary<string, string> Selected { get; init; } = new(StringComparer.Ordinal);
}

public sealed partial class ClientGame
{
    private string PortraitProfilePath => Path.Combine(saveDirectory, "portraits", "library.json");
    private PortraitProfile ReadPortraitProfile()
    {
        if (!File.Exists(PortraitProfilePath)) return new();
        if (new FileInfo(PortraitProfilePath).Length > 2 * 1024 * 1024) throw new RuleViolation("portraits.size", "头像图库清单过大");
        var p = JsonSerializer.Deserialize<PortraitProfile>(File.ReadAllBytes(PortraitProfilePath), Json);
        if (p == null || p.Schema != 1 || p.Revision < 0 || p.Custom == null || p.Selected == null
            || p.Custom.Count > 4096 || p.Selected.Count > 4096 || p.Custom.Any(q => q.Value == null || !PortraitHash(q.Value.Portrait) || !PortraitHash(q.Value.Marker)))
            throw new RuleViolation("portraits.profile", "头像图库清单损坏");
        return p;
    }
    private static bool PortraitHash(string? hash) => hash?.Length == 64 && hash.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static byte[] ReadPortrait(string directory, string name)
    {
        if (!name.EndsWith(".png", StringComparison.Ordinal) || !PortraitHash(name[..^4])) throw new FileNotFoundException("无效头像");
        var bytes = File.ReadAllBytes(Path.Combine(directory, "portraits", name));
        if (bytes.Length > 1024 * 1024 || Convert.ToHexStringLower(SHA256.HashData(bytes)) != name[..^4]) throw new IOException("头像校验失败");
        return bytes;
    }
    private static byte[] DecodePortraitImage(string? encoded, int size)
    {
        if (encoded == null || encoded.Length > 1400000) throw new RuleViolation("portraits.image", "头像图片过大或为空");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(encoded); }
        catch (FormatException) { throw new RuleViolation("portraits.image", "无效图片编码"); }
        if (bytes.Length < 24 || bytes.Length > 1024 * 1024 || !bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
            || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16)) != size
            || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20)) != size)
            throw new RuleViolation("portraits.image", "头像尺寸不符，请在图库中裁剪后保存");
        try
        {
            var decoded = StbImageSharp.ImageResult.FromMemory(bytes, StbImageSharp.ColorComponents.RedGreenBlueAlpha);
            if (decoded.Width != size || decoded.Height != size) throw new InvalidDataException();
            using var output = new MemoryStream();
            new StbImageWriteSharp.ImageWriter().WritePng(decoded.Data, size, size, StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha, output);
            bytes = output.ToArray();
        }
        catch (Exception e) when (e is not OutOfMemoryException) { throw new RuleViolation("portraits.image", "无法解码头像图片"); }
        return bytes;
    }
    private object PortraitGallery(ClientCommand command)
    {
        var profile = ReadPortraitProfile();
        var account = ObserveObjectives(Account());
        var definitions = content.Catalogs.Runtime.SourceTrees.GetProperty("def_commander.xml").GetProperty("children").EnumerateArray()
            .Select(e => e.GetProperty("attributes")).Select(a => {
                string Text(string key, string fallback = "") => a.TryGetProperty(key, out var v) ? v.GetString()! : fallback;
                int id = int.Parse(Text("id"), System.Globalization.CultureInfo.InvariantCulture);
                return new { id = content.Catalogs.Runtime.Commanders[id].Definition.Id, legacyId = id,
                    name = content.Label(Text("name")), image = Text("image", Text("name") + ".png"), country = Text("country"),
                    rating = int.Parse(Text("star", "0"), System.Globalization.CultureInfo.InvariantCulture),
                    owned = account.Roster.Any(c => c.DefinitionId == content.Catalogs.Runtime.Commanders[id].Definition.Id) };
            }).ToArray();
        var skins = content.Extensions.CommanderSkins?.Skins ?? [];
        bool Unlocked(CommanderSkin skin) => skin.Unlock == null || skin.Unlock.All(account.CompletedObjectives.Contains);
        if (command.Op is not ("" or "list"))
        {
            if (command.KnownVersion != (saveIdentity ?? "builtin")) throw new RuleViolation("portraits.identity", "内容包已切换，请重新打开图库");
            if (command.Revision != profile.Revision) throw new RuleViolation("portraits.revision", "图库已更新，请重新打开后再操作");
            if (!definitions.Any(d => d.id == command.Commander)) throw new RuleViolation("portraits.commander", "此将领不存在");
            string id = command.Commander!;
            if (command.Op == "upload")
            {
                // Validate both images before changing the selected appearance.
                byte[] portraitBytes = DecodePortraitImage(command.Portrait,384), markerBytes = DecodePortraitImage(command.Marker,128);
                string portrait = Convert.ToHexStringLower(SHA256.HashData(portraitBytes)), marker = Convert.ToHexStringLower(SHA256.HashData(markerBytes));
                string directory = Path.Combine(saveDirectory,"portraits"); Directory.CreateDirectory(directory);
                var shared = profile.Custom.Where(p=>p.Key!=id).SelectMany(p=>new[]{p.Value.Portrait,p.Value.Marker}).ToHashSet(StringComparer.Ordinal);
                long existing = Directory.EnumerateFiles(directory,"*.png").Where(f => shared.Contains(Path.GetFileNameWithoutExtension(f))).Sum(f=>new FileInfo(f).Length);
                if (existing + portraitBytes.Length + markerBytes.Length > 64L*1024*1024) throw new RuleViolation("portraits.size","当前档案的头像图库已满（64 MiB），请先移除部分自定义头像");
                File.WriteAllBytes(Path.Combine(directory,portrait+".png"),portraitBytes);
                File.WriteAllBytes(Path.Combine(directory,marker+".png"),markerBytes);
                profile.Custom[id] = new(portrait,marker); profile.Selected[id] = "custom";
            }
            else if (command.Op == "select")
            {
                if (command.Offer == "custom" && profile.Custom.ContainsKey(id)) profile.Selected[id] = "custom";
                else if (command.Offer == "original") profile.Selected[id] = "";
                else if (command.Offer == "automatic") profile.Selected.Remove(id);
                else if (skins.SingleOrDefault(s => s.Id == command.Offer && s.Commander == id) is { } skin && Unlocked(skin)) profile.Selected[id] = skin.Id;
                else throw new RuleViolation("portraits.locked", "此皮肤尚未解锁或已不可用");
            }
            else if (command.Op == "remove") { profile.Custom.Remove(id); profile.Selected.Remove(id); }
            else throw new RuleViolation("portraits.command", "未知头像操作");
            profile = profile with { Revision = checked(profile.Revision+1) };
            Directory.CreateDirectory(Path.GetDirectoryName(PortraitProfilePath)!);
            File.WriteAllBytes(PortraitProfilePath+".tmp",JsonSerializer.SerializeToUtf8Bytes(profile,Json));
            File.Move(PortraitProfilePath+".tmp",PortraitProfilePath,true);
            // Hashes are immutable URLs; only remove files no longer referenced by the manifest.
            var retained = profile.Custom.Values.SelectMany(p => new[]{p.Portrait,p.Marker}).ToHashSet(StringComparer.Ordinal);
            foreach(var file in Directory.EnumerateFiles(Path.GetDirectoryName(PortraitProfilePath)!,"*.png"))
                if (!retained.Contains(Path.GetFileNameWithoutExtension(file))) File.Delete(file);
        }
        var rows = definitions.Select(d => {
            string? choice = profile.Selected.GetValueOrDefault(d.id);
            var skin = choice == null ? skins.Where(s => s.Commander == d.id && s.Automatic && Unlocked(s)).OrderByDescending(s => s.Priority).ThenBy(s => s.Id,StringComparer.Ordinal).FirstOrDefault()
                : skins.SingleOrDefault(s => s.Id == choice && s.Commander == d.id && Unlocked(s));
            profile.Custom.TryGetValue(d.id,out var uploaded);
            string? portrait = choice == "custom" && uploaded != null ? "portraits/"+uploaded.Portrait+".png" : skin?.Image;
            string? marker = choice == "custom" && uploaded != null ? "portraits/"+uploaded.Marker+".png" : skin?.Marker ?? skin?.Image;
            return new { d.id,d.legacyId,d.name,d.image,d.country,d.rating,d.owned,portrait,marker,selected=choice,custom=uploaded!=null,
                skins=skins.Where(s=>s.Commander==d.id).Select(s=>new {s.Id,name=content.Label(s.Name),s.Image,s.Marker,unlocked=Unlocked(s),s.Automatic,
                    unlockDescription=s.UnlockDescription==null ? "完成对应任务或成就后解锁" : content.Label(s.UnlockDescription)}) };
        }).ToArray();
        return new { profile.Revision, entries=rows, identity=saveIdentity ?? "builtin" };
    }
}
