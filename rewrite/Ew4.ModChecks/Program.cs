using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Ew4.Mods;

if (args.ElementAtOrDefault(1) == "--campaign-music-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { campaignMusicModChecks = CampaignMusicModChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--formation-counts-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { formationCountChecks = FormationCountChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--deployment-limits-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { deploymentLimitModChecks = DeploymentLimitModChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--item-extensions-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { itemExtensionModChecks = ItemExtensionModChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}
if (args.ElementAtOrDefault(1) == "--countries-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { countryChecks = CountryChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--unit-visuals-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { unitVisualChecks = UnitVisualChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--legacy-skills-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { legacySkillParameterModChecks = LegacySkillParameterModChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--identity-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { identityChecks = ModIdentityChecks.Run(Path.GetFullPath(args[0])), errors = 0 }));
    return;
}

if (args.ElementAtOrDefault(1) == "--deletion-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { deletionModChecks = ModDeletionChecks.Run(Path.GetFullPath(args[0])), errors = 0 }));
    return;
}

if (args.ElementAtOrDefault(1) == "--extension-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { extensionModChecks = ExtensionModChecks.Run(Path.GetFullPath(args[0])),
        commonSkillModChecks = CommonSkillModChecks.Run(Path.GetFullPath(args[0])),
        legacySkillParameterModChecks = LegacySkillParameterModChecks.Run(Path.GetFullPath(args[0])),
        deploymentLimitModChecks = DeploymentLimitModChecks.Run(Path.GetFullPath(args[0])), errors = 0 })); return;
}

if (args.ElementAtOrDefault(1) == "--jurisdiction-only")
{
    Console.WriteLine(JsonSerializer.Serialize(new { jurisdictionModChecks = JurisdictionModChecks.Run(Path.GetFullPath(args[0])), errors = 0 }));
    return;
}

if (args.ElementAtOrDefault(1) == "--audit-apks")
{
    CompatibilityAudit.Run(Path.GetFullPath(args[0]));
    return;
}

if (args.ElementAtOrDefault(1) == "--real-apks")
{
    RealApkChecks.Run(Path.GetFullPath(args[0]), args.Skip(2).ToArray());
    return;
}

string root = Path.GetFullPath(args[0]), data = Path.Combine(root, "rewrite/data"), report = Path.Combine(root, "rewrite/reports/mod-import-20261019");
string run = Path.Combine(report, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(run); int checks = LegacyFormatChecks.Run();
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Reject(Action action, string message) { try { action(); } catch (Exception e) when (e is not OutOfMemoryException) { checks++; return; } throw new Exception(message); }
var baseline = new ModBase(p => File.ReadAllBytes(p.StartsWith("frontend/") ? Path.Combine(root, "rewrite/Ew4.Web/wwwroot", p[9..]) : Path.Combine(data, p)),
    Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(data, p).Replace('\\', '/')));
checks += LegacyResourceChecks.Run(baseline, run);
checks += LegacySkillParameterModChecks.Run(root);
checks += JurisdictionModChecks.Run(root);
var store = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves"));
string Call(string route, object body)
{
    string json = store.Dispatch("/api/" + route, JsonSerializer.Serialize(body));
    var result = JsonNode.Parse(json)!; Check((bool?)result["ok"] == true, route + ": " + json[..Math.Min(400, json.Length)]); return result["data"]!.ToJsonString();
}
string State() => Call("action", new { op = "state" });
string raw = Path.Combine(run, "raw"); Directory.CreateDirectory(raw);
void Raw(string name, byte[] bytes) { string path = Path.Combine(raw, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); }
byte[] Original(string name) => File.ReadAllBytes(Path.Combine(data, "original", name));
Call("start", new { scenario = "conquest1", country = "fra" }); string builtinState = State();
string builtinSave = Path.Combine(run, "saves/saves/mode-2/slot-0.json"); byte[] saveBefore = File.ReadAllBytes(builtinSave);
var commanders = XDocument.Parse(Encoding.UTF8.GetString(Original("def_commander.xml"))); commanders.Root!.Elements().First().SetAttributeValue("business", 4);
Raw("def_commander.xml", Encoding.UTF8.GetBytes(commanders.ToString()));
Raw("stringtable_cn.ini", Original("stringtable_cn.ini").Concat(Encoding.UTF8.GetBytes("\nname_test_import=离线导入\n")).ToArray());
Raw("conquest7.btl", Original("conquest1.btl"));
var army = XDocument.Parse(Encoding.UTF8.GetString(Original("def_army.xml"))); var firstArmy = army.Root!.Elements().First().Elements("army").First();
int strength = (int)firstArmy.Attribute("strength")!; firstArmy.SetAttributeValue("strength", strength + 7); Raw("def_army.xml", Encoding.UTF8.GetBytes(army.ToString()));
byte[] title = File.ReadAllBytes(Path.Combine(data, "client/ui/title.png")); Raw("zh_CN.lproj/title_hd.png", title);
byte[] customSound = File.ReadAllBytes(Path.Combine(root, "apk_work/original/assets/sfx_leg.wav"));
Raw("sfx_presentation_test.wav", customSound);
Raw("def_effectsanim.xml", Encoding.UTF8.GetBytes("<effects><anim name=\"presentation test\"><group type=\"effect\"><item at=\"0\" sound=\"sfx_presentation_test.wav\" /></group></anim></effects>"));
Raw("def_elementanim.xml", File.ReadAllBytes(Path.Combine(root, "apk_work/original/assets/def_elementanim.xml")));
var imported = store.Import(raw, "test-mod", "测试内容包"); Check(store.List().Length == 1, "installed once"); Check(!imported.Active, "import does not activate");
Check(File.ReadAllBytes(builtinSave).SequenceEqual(saveBefore), "import preserves original save");
string installed = Path.Combine(run, "mods", imported.Key);
var importedEffects = JsonNode.Parse(File.ReadAllBytes(Path.Combine(installed, "client/effects.json")))!;
Check(importedEffects["sounds"]!.AsArray().Any(n => (string?)n == "sfx_presentation_test.wav"), "sound-only patch extends playback whitelist without def_motion");
Check(File.ReadAllBytes(Path.Combine(installed, "client/sounds/sfx_presentation_test.wav")).SequenceEqual(customSound), "custom sound bytes preserved");
Check((string?)importedEffects["timelines"]!["presentation test"]![0]!["sound"] == "sfx_presentation_test.wav", "custom effect timeline retained");
Check(importedEffects["elements"]!["damaged 3"]!.AsArray().Count == 3, "element animation groups imported without def_motion");
var catalog = JsonNode.Parse(File.ReadAllBytes(Path.Combine(installed, "legacy/catalog.json")))!;
Check((int)catalog["profiles"]![0]!["max_health"]! == strength + 7, "unit values converted");
Check((string)catalog["commanders"]![0]!["legacy_attributes"]!["business"]! == "4", "commander values converted");
store.Activate(imported.Key); Check(StbImageSharp.ImageResult.FromMemory(store.Read("client/ui/title.png"), StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data.SequenceEqual(StbImageSharp.ImageResult.FromMemory(title, StbImageSharp.ColorComponents.RedGreenBlueAlpha).Data), "derived UI override");
var pages = JsonNode.Parse(store.Read("frontend/conquest-pages.json"))!;
Check(pages.ToJsonString().Contains("conquest7"), "extra conquest reachable from menu");
Call("start", new { scenario = "conquest7", country = "fra" });
Check(File.Exists(Path.Combine(run, "saves/mods/test-mod", imported.LogicRevision, "saves/mode-2/slot-0.json")), "mod save isolation");
Check(File.ReadAllBytes(builtinSave).SequenceEqual(saveBefore), "gameplay preserves builtin saves");
store.Activate(null); Call("load", new { mode = 2 });
Check(JsonNode.Parse(State())!["scenario"]!.GetValue<string>() == "conquest1", "builtin game restored");
var restarted = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves")); Check(restarted.List().All(m => !m.Active), "builtin selection persistent");
store.Activate(imported.Key); restarted = new ModStore(baseline, Path.Combine(run, "mods"), Path.Combine(run, "saves")); Check(restarted.List().Single().Active, "mod selection persistent");
string Frontend(string text, string extension) => Encoding.UTF8.GetString(store.Frontend(Encoding.UTF8.GetBytes(text), extension));
string modCss = Frontend("a{background:url('/content/ui/main.png')}", ".css");
byte[] selectedTitle = store.Read("client/ui/title.png");
Check(modCss.Contains("/content/r-" + imported.Key + "/ui/main.png"), "CSS resource namespace follows active package");
Check(store.ReadContent("r-" + imported.Key + "/ui/title.png").SequenceEqual(store.Read("client/ui/title.png")), "revisioned content resolves selected resource");
Check(Frontend("<script src=\"app.js?v=1\"></script><link href=\"style.css\">", ".html").Contains("app.js?v=1&content=" + imported.Key), "script and CSS cache keys follow active package");
store.Activate(null);
Check(Frontend("a{background:url('/content/ui/main.png')}", ".css") != modCss, "builtin receives separate decoded-image URLs");
Check(store.ReadContent("r-" + imported.Key + "/ui/title.png").SequenceEqual(selectedTitle), "in-flight request keeps old package after switch");
Check(store.ReadContent("r-" + baseline.Revision + "/ui/title.png").SequenceEqual(baseline.Read("client/ui/title.png")), "builtin namespace always returns original resource");
store.Activate(imported.Key);
string export = Path.Combine(run, "test.ew4mod"); store.Export(imported.Key, export);
var other = new ModStore(baseline, Path.Combine(run, "other"), Path.Combine(run, "other-saves")); Check(other.Import(export, "ignored", "ignored").Key == imported.Key, "package roundtrip");
string cosmetic = Path.Combine(run, "cosmetic"); Directory.CreateDirectory(Path.Combine(cosmetic, "client/ui"));
File.Copy(Path.Combine(data, "client/ui/main.png"), Path.Combine(cosmetic, "client/ui/main.png")); store.Seal(cosmetic, "cosmetic", "美化", "1"); var c1 = store.Import(cosmetic, "ignored", "ignored");
File.Copy(Path.Combine(data, "client/ui/title.png"), Path.Combine(cosmetic, "client/ui/main.png"), true); store.Seal(cosmetic, "cosmetic", "美化", "2"); var c2 = store.Import(cosmetic, "ignored", "ignored");
Check(c1.Key != c2.Key && c1.LogicRevision == c2.LogicRevision, "cosmetic update retains save profile");
store.Activate(c1.Key);
Check((string?)JsonNode.Parse(store.ReadContent("manifest.json"))!["uiOverrides"]?["main"] == "ui/main.png", "direct derived menu image overrides remain supported");
store.Activate(imported.Key);
File.AppendAllText(Path.Combine(cosmetic, "client/ui/main.png"), "tamper"); Reject(() => store.Import(cosmetic, "ignored", "ignored"), "tampering must fail");
Check(store.List().Count(m => m.Active) == 1, "failed import preserves active selection");
foreach (var entries in new[] { new[] { "../evil" }, new[] { "client/A.png", "client/a.png" }, new[] { "client/a.png", "client/a.png" }, new[] { "C:/evil" }, new[] { "client/aux.png" } })
{
    string bad = Path.Combine(run, Guid.NewGuid().ToString("N") + ".zip"); using (var zip = ZipFile.Open(bad, ZipArchiveMode.Create)) foreach (string path in entries) { using var stream = zip.CreateEntry(path).Open(); stream.WriteByte(0); }
    Reject(() => store.Import(bad, "bad", "bad"), "invalid zip must fail");
}
commanders.Root!.Elements().First().SetAttributeValue("skill1", 99); Raw("def_commander.xml", Encoding.UTF8.GetBytes(commanders.ToString()));
Reject(() => store.Import(raw, "bad-skill", "bad"), "unimplemented skill IDs must fail");
commanders.Root!.Elements().First().SetAttributeValue("skill1", 0); Raw("def_commander.xml", Encoding.UTF8.GetBytes(commanders.ToString()));
var xml = Encoding.UTF8.GetBytes("<!DOCTYPE commanders [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><commanders>&x;</commanders>"); Raw("def_commander.xml", xml);
Reject(() => store.Import(raw, "bad-xml", "bad"), "XML external entities forbidden");
string mapRaw = Path.Combine(run, "raw-map"); Directory.CreateDirectory(Path.Combine(mapRaw, "map"));
string source = Path.Combine(root, "apk_work/original/assets"); File.Copy(Path.Combine(source, "map1_hd.bin"), Path.Combine(mapRaw, "map1_hd.bin"));
foreach (string p in Directory.EnumerateFiles(Path.Combine(source, "map"), "map1_*@2x.pvr")) File.Copy(p, Path.Combine(mapRaw, "map", Path.GetFileName(p)));
string pvr = Directory.EnumerateFiles(Path.Combine(mapRaw, "map")).First(); byte[] pixels = File.ReadAllBytes(pvr); pixels[52] ^= 0x10; File.WriteAllBytes(pvr, pixels);
var mapInfo = store.Import(mapRaw, "map-test", "地图测试");
Check(Directory.EnumerateFiles(Path.Combine(run, "mods", mapInfo.Key, "client/maps/europe"), "*.png").Count() > 10, "PVR map rebuilt into chunks");
string graphicRaw = Path.Combine(run, "raw-graphics"); Directory.CreateDirectory(graphicRaw);
foreach (string file in new[] { "army_militia.bin", "army_militia.xml", "commanders1_hd.pkm", "commanders1_hd.xml", "font_text_2_cn_hd.fnt", "font_fight_num.fnt", "font_fight_num.png", "font_fight_num_hd.fnt", "font_fight_num_hd.png" }) File.Copy(Path.Combine(source, file), Path.Combine(graphicRaw, file));
string animPath = Path.Combine(graphicRaw, "army_militia.bin"); byte[] animBytes = File.ReadAllBytes(animPath); float fps = BitConverter.ToSingle(animBytes, 16); BitConverter.GetBytes(fps + 1).CopyTo(animBytes, 16); File.WriteAllBytes(animPath, animBytes);
string pkmPath = Path.Combine(graphicRaw, "commanders1_hd.pkm"); byte[] pkmBytes = File.ReadAllBytes(pkmPath); pkmBytes[16] ^= 1; File.WriteAllBytes(pkmPath, pkmBytes);
var graphicInfo = store.Import(graphicRaw, "graphic-test", "图形测试"); string graphicFolder = Path.Combine(run, "mods", graphicInfo.Key, "client");
Check((float)JsonNode.Parse(File.ReadAllBytes(Path.Combine(graphicFolder, "animations/army_militia.json")))!["fps"]! == fps + 1, "raw BILE conversion");
Check(File.Exists(Path.Combine(graphicFolder, "images/commanders1_hd.png")), "ETC1 converted to PNG");
Check(Directory.EnumerateFiles(Path.Combine(graphicFolder, "ui/commanders1_hd"), "*.png").Count() > 10, "changed portrait atlas recropped");
var graphicManifest = JsonNode.Parse(File.ReadAllBytes(Path.Combine(graphicFolder, "manifest.json")))!;
foreach (string font in new[] { "font_fight_num", "font_fight_num_hd" })
{
    Check(graphicManifest["fonts"]![font]!["glyphs"]!.AsObject().Count == 11, "combat font metrics imported: " + font);
    Check(File.ReadAllBytes(Path.Combine(source, font + ".png")).SequenceEqual(File.ReadAllBytes(Path.Combine(graphicFolder, "images", font + ".png"))), "combat font bitmap preserved: " + font);
}
store.Activate(null);
// Loading the builtin slot refreshes its save timestamp; its gameplay content must stay intact.
var originalSlot = JsonNode.Parse(saveBefore)!.AsObject(); originalSlot.Remove("savedAt");
var finalSlot = JsonNode.Parse(File.ReadAllBytes(builtinSave))!.AsObject(); finalSlot.Remove("savedAt");
Check(JsonNode.DeepEquals(originalSlot, finalSlot), "all imports leave original slot content intact");
File.WriteAllText(Path.Combine(report, "results.json"), JsonSerializer.Serialize(new { checks, errors = 0, run, scopes = new[] { "raw data/UI/map", "extra scenario", "hash validation", "rollback", "save isolation", "package roundtrip", "unsafe archives", "unsupported native skills" } }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Mod checks: {checks}, errors: 0; {run}");
