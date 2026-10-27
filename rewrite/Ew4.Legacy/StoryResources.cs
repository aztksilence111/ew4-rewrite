using Ew4.Core;

namespace Ew4.Legacy;

public sealed partial class ExtensionLoader
{
    private static IEnumerable<string> StoryMedia(StoryLibrary library) => library.Backgrounds.Values.Concat(
        library.Characters.Values.SelectMany(c => c.Poses.Values).SelectMany(p =>
            (p.Image == null ? Array.Empty<string>() : new[] { p.Image }).Concat(p.Layers.Values.SelectMany(l => l.Parts.Values))));
}
