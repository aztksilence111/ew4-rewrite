using Ew4.Mods;

if (args.Length != 2) throw new ArgumentException("EastAsiaProbe <repository root> <generated probe directory>");
string root = Path.GetFullPath(args[0]), target = Path.GetFullPath(args[1]);
string data = Path.Combine(root, "rewrite/data"), source = Path.Combine(root, "rewrite/content/eastasia");
foreach (string file in new[] { "original/def_battlelist.xml", "original/conquest7.btl", "original/conquest7.names.xml", "frontend/conquest-pages.json" })
{
    string path = Path.Combine(target, file);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.Copy(Path.Combine(source, file), path, true);
}
var baseline = new ModBase(p => File.ReadAllBytes(Path.Combine(data, p)), Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
var store = new ModStore(baseline, target + "-mods", target + "-saves");
store.Seal(target, "eastasia-check", "East Asia map check", "1.0", baseline.Scenarios.Append("conquest7").Distinct().ToArray());
store.Validate(target);
Console.WriteLine("East Asia probe sealed with conquest7; scenario, resource and geography validation passed");
