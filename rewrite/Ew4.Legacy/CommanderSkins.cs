using Ew4.Core;

namespace Ew4.Legacy;

public sealed record CommanderSkin(string Id, string Commander, string Name, string Image,
    string? Marker = null, string[]? Unlock = null, string? UnlockDescription = null, bool Automatic = false, int Priority = 0);
public sealed record CommanderSkinLibrary(int Schema, CommanderSkin[] Skins)
{
    public const string Path = "rewrite/commander-skins.json";
    public void Validate(Func<string, byte[]> read, MetagameRules metagame)
    {
        if (Schema != 1 || Skins == null || Skins.Length > 4096 || Skins.Any(s => s == null)
            || Skins.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != Skins.Length)
            throw new RuleViolation("skins.configuration", "Invalid skin library");
        foreach (var skin in Skins)
        {
            if (string.IsNullOrWhiteSpace(skin.Id) || skin.Id is "custom" or "original" or "automatic" || skin.Id.Length > 128 || string.IsNullOrWhiteSpace(skin.Commander)
                || string.IsNullOrWhiteSpace(skin.Name) || skin.Name.Length > 256 || skin.UnlockDescription?.Length > 1024
                || skin.Unlock?.Any(id => !metagame.Objectives.Any(o => o.Id == id)) == true)
                throw new RuleViolation("skins.configuration", "Invalid skin or unlock objective: " + skin.Id);
            foreach (var path in new[] { skin.Image, skin.Marker ?? skin.Image }.Distinct())
            {
                if (!ExtensionLoader.PathValid(path) || !path.EndsWith(".png", StringComparison.Ordinal))
                    throw new RuleViolation("skins.image", "Skin image must be a rewrite PNG: " + path);
                var png = read(path);
                if (png.Length < 24 || png.Length > 4 * 1024 * 1024 || !png.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)) is < 1 or > 4096
                    || System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20)) is < 1 or > 4096)
                    throw new RuleViolation("skins.image", "Invalid or oversized skin PNG: " + path);
            }
        }
    }
}
