using System.Security.Cryptography;
using System.Text.Json;
using Ew4.Client;
using Ew4.Legacy;

string mode=args[0],root=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
int checks=0;
void Check(bool value,string label){checks++;if(!value)throw new Exception(label);}
byte[] Read(string path)=>File.ReadAllBytes(Path.Combine(root,"rewrite/data",path.StartsWith("data/")?"legacy/"+path[5..]:path));
byte[] Png(int size,byte red){using var stream=new MemoryStream();var rgba=new byte[size*size*4];for(int i=0;i<rgba.Length;i+=4){rgba[i]=red;rgba[i+3]=255;}new StbImageWriteSharp.ImageWriter().WritePng(rgba,size,size,StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,stream);return stream.ToArray();}
var basis=new ClientContent(Read,["conquest1.btl"]);
JsonElement Call(ClientGame client,object command){using var doc=JsonDocument.Parse(client.Dispatch("/api/portraits",JsonSerializer.Serialize(command,ClientGame.Json)));Check(doc.RootElement.GetProperty("ok").GetBoolean(),doc.RootElement.GetRawText());return doc.RootElement.GetProperty("data").Clone();}
var ids=Call(new ClientGame(basis,Path.Combine(output,"discovery")),new{}).GetProperty("entries").EnumerateArray().Take(4).Select(e=>e.GetProperty("id").GetString()!).ToArray();
foreach(string identity in new[]{"builtin","mods/upgrade/logic"}){
    string profile=Path.Combine(output,identity=="builtin"?"builtin":"mod");
    var files=new Dictionary<string,byte[]>();
    if(identity!="builtin"){
        files["rewrite/extension.json"]=JsonSerializer.SerializeToUtf8Bytes(new{schema=1,apiVersion=3});
        files["rewrite/skins/upgrade.png"]=Png(256,110);
        files[CommanderSkinLibrary.Path]=JsonSerializer.SerializeToUtf8Bytes(new CommanderSkinLibrary(1,[
            new("upgrade.auto",ids[0],"Automatic","rewrite/skins/upgrade.png",Automatic:true),
            new("upgrade.manual",ids[2],"Manual","rewrite/skins/upgrade.png"),
            new("upgrade.follow",ids[3],"Follow","rewrite/skins/upgrade.png",Automatic:true)]),ClientGame.Json);
    }
    var content=identity=="builtin"?basis:new ClientContent(p=>files.GetValueOrDefault(p)??Read(p),["conquest1.btl"]);
    var client=new ClientGame(content,profile,saveIdentity:identity);
    var view=Call(client,new{});
    if(mode=="seed"){
        foreach(string id in ids.Take(2))view=Call(client,new{op="upload",commander=id,knownVersion=identity,revision=view.GetProperty("revision").GetInt64(),portrait=Convert.ToBase64String(Png(384,220)),marker=Convert.ToBase64String(Png(128,200))});
        view=Call(client,new{op="select",commander=ids[1],knownVersion=identity,revision=view.GetProperty("revision").GetInt64(),offer="original"});
        if(identity!="builtin")view=Call(client,new{op="select",commander=ids[2],knownVersion=identity,revision=view.GetProperty("revision").GetInt64(),offer="upgrade.manual"});
        File.WriteAllText(Path.Combine(profile,"expected.json"),JsonSerializer.Serialize(new{
            rows=view.GetProperty("entries").EnumerateArray().Where(e=>ids.Contains(e.GetProperty("id").GetString())).Select(e=>new{id=e.GetProperty("id").GetString(),selected=e.GetProperty("selected"),portrait=e.GetProperty("portrait"),marker=e.GetProperty("marker"),custom=e.GetProperty("custom")}),
            hashes=Directory.EnumerateFiles(Path.Combine(profile,"portraits")).ToDictionary(p=>Path.GetFileName(p)!,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))))},ClientGame.Json));
    }else{
        using var expected=JsonDocument.Parse(File.ReadAllText(Path.Combine(profile,"expected.json")));
        foreach(var row in expected.RootElement.GetProperty("rows").EnumerateArray()){
            var actual=view.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==row.GetProperty("id").GetString());
            foreach(string key in new[]{"selected","portrait","marker","custom"})Check(actual.GetProperty(key).GetRawText()==row.GetProperty(key).GetRawText(),identity+" changed "+key);
        }
        foreach(var hash in expected.RootElement.GetProperty("hashes").EnumerateObject())Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(profile,"portraits",hash.Name))))==hash.Value.GetString(),identity+" changed bytes "+hash.Name);
        var custom=view.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==ids[0]);
        Check(custom.GetProperty("selected").GetString()=="custom",identity+" uploaded choice retained above automatic skins");
        foreach(string key in new[]{"portrait","marker"})Check(ClientGame.ReadPortrait(profile,custom.GetProperty(key).GetString()![10..]).Length>0,identity+" image readable");
    }
}
Console.WriteLine(JsonSerializer.Serialize(new{mode,checks,errors=0}));
