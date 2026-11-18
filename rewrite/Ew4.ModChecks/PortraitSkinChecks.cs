using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;
using Ew4.Mods;

internal static class PortraitSkinChecks
{
    internal static int Run(string root)
    {
        int checks=0;void Check(bool yes,string label){checks++;if(!yes)throw new Exception("Portrait Mod: "+label);}
        string run=Path.Combine(root,"build/portrait-gallery/qa/mod-check",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(run);
        string data=Path.Combine(root,"rewrite/data");byte[] Read(string p)=>File.ReadAllBytes(p.StartsWith("frontend/")?Path.Combine(root,"rewrite/Ew4.Web/wwwroot",p[9..]):Path.Combine(data,p));
        byte[] ExampleRead(string p){string file=Path.Combine(root,"rewrite/mod-kit/examples/commander-skins",p);return File.Exists(file)?File.ReadAllBytes(file):Read(p.StartsWith("data/")?"legacy/"+p[5..]:p);}
        var example=new ClientContent(ExampleRead,["conquest1.btl"]);
        Check(example.Extensions.CommanderSkins?.Skins.Single().Id=="demo.expedition","documented example loads through strict configuration parser");
        var paths=Directory.EnumerateFiles(data,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(data,p).Replace('\\','/')).Concat(["frontend/conquest-pages.json","frontend/city-names.json"]);
        var baseline=new ModBase(Read,paths);var content=baseline.Content(Read,["conquest1"]);string id=content.Catalogs.Runtime.Commanders.First().Value.Definition.Id;
        byte[] Png(int size){using var stream=new MemoryStream();var rgba=new byte[size*size*4];for(int i=0;i<rgba.Length;i+=4){rgba[i]=34;rgba[i+1]=119;rgba[i+2]=194;rgba[i+3]=255;}
            new StbImageWriteSharp.ImageWriter().WritePng(rgba,size,size,StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,stream);return stream.ToArray();}
        string author=Path.Combine(run,"author");Directory.CreateDirectory(Path.Combine(author,"rewrite/skins"));
        void Put(string path,object value)=>File.WriteAllBytes(Path.Combine(author,path),JsonSerializer.SerializeToUtf8Bytes(value,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Put("rewrite/extension.json",new{schema=1,apiVersion=3,metagame="rewrite/metagame.json"});
        Put("rewrite/metagame.json",new MetagameRules{Objectives=[new("skin.first","task","首位将领","",[new("commanderOwned")],new(),Internal:true),new("skin.future","achievement","未来奖励","",[new("commanderOwned",999)],new(),Internal:true)]});
        File.WriteAllBytes(Path.Combine(author,"rewrite/skins/demo.png"),Png(256));
        var store=new ModStore(baseline,Path.Combine(run,"mods"),Path.Combine(run,"saves"));store.Seal(author,"portrait-demo","Portrait demo","1",["conquest1"]);
        string prior=store.Validate(author).LogicRevision;
        Put(CommanderSkinLibrary.Path,new CommanderSkinLibrary(1,[new("demo.blue",id,"远征服","rewrite/skins/demo.png",Unlock:["skin.first"],UnlockDescription:"获得任意一位将领后解锁",Automatic:true),new("demo.future",id,"纪念服","rewrite/skins/demo.png",Unlock:["skin.future"],UnlockDescription:"未来成就尚未完成")]));
        store.Seal(author,"portrait-demo","Portrait demo","1",["conquest1"]);Check(store.Validate(author).LogicRevision==prior,"skins preserve Mod save namespace");
        var installed=store.Import(author,"portrait-demo","Portrait demo");store.Activate(installed.Key);
        JsonNode Gallery(object command){var response=JsonNode.Parse(store.Dispatch("/api/portraits",JsonSerializer.Serialize(command,new JsonSerializerOptions(JsonSerializerDefaults.Web))))!;Check((bool?)response["ok"]==true,"gallery request "+response["message"]);return response["data"]!;}
        var gallery=Gallery(new{});Check(gallery["entries"]!.AsArray().Single(e=>(string?)e!["id"]==id)!["skins"]!.AsArray().Count==2,"skins served to client");
        gallery=Gallery(new{op="upload",commander=id,revision=(long)gallery["revision"]!,knownVersion=(string)gallery["identity"]!,portrait=Convert.ToBase64String(Png(384)),marker=Convert.ToBase64String(Png(128))});
        string image=(string)gallery["entries"]!.AsArray().Single(e=>(string?)e!["id"]==id)!["portrait"]!;
        Check(store.ReadContent("r-"+installed.Key+"/"+image).Length>0,"versioned custom portrait route");
        store.Activate(null);Check(Gallery(new{})["entries"]!.AsArray().All(e=>e!["portrait"]==null),"vanilla profile isolated");
        Check(store.ReadContent("r-"+installed.Key+"/"+image).Length>0,"in-flight Mod portrait stays in original profile");
        store.Activate(installed.Key);Check((string?)Gallery(new{})["entries"]!.AsArray().Single(e=>(string?)e!["id"]==id)!["portrait"]==image,"Mod profile survives switching");
        using var output=new MemoryStream();store.ExportSaves(output,"test","web");using var zip=new ZipArchive(new MemoryStream(output.ToArray()));
        Check(zip.Entries.Any(e=>e.FullName.EndsWith("/portraits/library.json"))&&zip.Entries.Count(e=>e.FullName.EndsWith(".png"))==2,"export includes portrait map and derivatives");
        var restored=new ModStore(baseline,Path.Combine(run,"restored-mods"),Path.Combine(run,"restored-saves"));Check(restored.ImportSaves(new MemoryStream(output.ToArray())).Imported,"portrait files import");
        string fixture=Path.Combine(root,"build/portrait-gallery/qa/skin-demo.ew4mod");if(File.Exists(fixture))File.Delete(fixture);store.Export(installed.Key,fixture);
        return checks;
    }
}
