using System.Text.Json;
using Ew4.Client;
using Ew4.Core;
using Ew4.Legacy;

internal static class PortraitGalleryChecks
{
    public static int Run(string root)
    {
        int checks=0;
        void Check(bool yes,string name){checks++;if(!yes)throw new Exception("Portrait gallery: "+name);}
        string folder=Path.Combine(root,"build/portrait-gallery/qa/core",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        byte[] Read(string path)=>File.ReadAllBytes(Path.Combine(root,"rewrite/data",path.StartsWith("data/")?"legacy/"+path[5..]:path));
        var basis=new ClientContent(Read,["conquest1.btl"]);
        byte[] Png(int size,byte red){using var stream=new MemoryStream();var pixels=new byte[size*size*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=red;pixels[i+3]=255;}
            new StbImageWriteSharp.ImageWriter().WritePng(pixels,size,size,StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,stream);return stream.ToArray();}
        var portrait=Png(384,220);var marker=Png(128,220);
        var client=new ClientGame(basis,Path.Combine(folder,"builtin"),saveIdentity:"builtin");
        JsonElement Result(object args,bool success=true){using var doc=JsonDocument.Parse(client.Dispatch("/api/portraits",JsonSerializer.Serialize(args,ClientGame.Json)));Check(doc.RootElement.GetProperty("ok").GetBoolean()==success,"request outcome");return doc.RootElement.Clone();}
        JsonElement Call(object args)=>Result(args).GetProperty("data");
        var view=Call(new{});var row=view.GetProperty("entries")[0];string id=row.GetProperty("id").GetString()!;
        Check(view.GetProperty("entries").GetArrayLength()>100,"all definitions available without ownership");
        var uploaded=Call(new {op="upload",commander=id,revision=view.GetProperty("revision").GetInt64(),knownVersion="builtin",portrait=Convert.ToBase64String(portrait),marker=Convert.ToBase64String(marker)});
        var actual=uploaded.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==id);
        Check(actual.GetProperty("custom").GetBoolean()&&actual.GetProperty("selected").GetString()=="custom","upload selected");
        string image=actual.GetProperty("portrait").GetString()!;
        Check(ClientGame.ReadPortrait(Path.Combine(folder,"builtin"),image[10..]).Length>0,"immutable image can be served");
        Result(new {op="select",commander=id,revision=0,knownVersion="builtin",offer="original"},false);
        Result(new {op="select",commander=id,revision=1,knownVersion="mods/other",offer="original"},false);
        int files=Directory.GetFiles(Path.Combine(folder,"builtin/portraits"),"*.png").Length;
        Result(new {op="upload",commander=id,revision=1,knownVersion="builtin",portrait=Convert.ToBase64String(Png(384,120)),marker="bad"},false);
        Check(Directory.GetFiles(Path.Combine(folder,"builtin/portraits"),"*.png").Length==files,"failed second image leaves no orphan");
        client=new ClientGame(basis,Path.Combine(folder,"builtin"),saveIdentity:"builtin");view=Call(new{});
        Check(view.GetProperty("revision").GetInt64()==1&&view.GetProperty("entries")[0].GetProperty("portrait").GetString()==image,"survives restart");
        view=Call(new {op="select",commander=id,revision=1,knownVersion="builtin",offer="original"});
        Check(view.GetProperty("entries")[0].GetProperty("portrait").ValueKind==JsonValueKind.Null,"original override restored");
        view=Call(new {op="select",commander=id,revision=2,knownVersion="builtin",offer="custom"});
        Check(view.GetProperty("entries")[0].GetProperty("portrait").GetString()==image,"stored upload reusable");
        view=Call(new {op="remove",commander=id,revision=3,knownVersion="builtin"});
        Check(!view.GetProperty("entries")[0].GetProperty("custom").GetBoolean()&&Directory.GetFiles(Path.Combine(folder,"builtin/portraits"),"*.png").Length==0,"remove cleans unreferenced images");
        try{ClientGame.ReadPortrait(Path.Combine(folder,"builtin"),"../account.json");throw new Exception("Traversal accepted");}catch(FileNotFoundException){checks++;}
        var extension=new Dictionary<string,byte[]> {
            ["rewrite/extension.json"]=JsonSerializer.SerializeToUtf8Bytes(new{schema=1,apiVersion=3}),
            ["rewrite/metagame.json"]=JsonSerializer.SerializeToUtf8Bytes(new MetagameRules {Objectives=[new("skin.task","task","Task","",[new("commanderOwned",Id:id)],new(),Internal:true)]},ClientGame.Json),
            ["rewrite/skins/red.png"]=Png(256,230)
        };
        extension["rewrite/extension.json"]=JsonSerializer.SerializeToUtf8Bytes(new{schema=1,apiVersion=3,metagame="rewrite/metagame.json"});
        byte[] ModRead(string path)=>extension.TryGetValue(path,out var data)?data:Read(path);
        string priorRevision=ExtensionLoader.Load(ModRead).Revision;
        extension[CommanderSkinLibrary.Path]=JsonSerializer.SerializeToUtf8Bytes(new CommanderSkinLibrary(1,[new("skin.red",id,"Red","rewrite/skins/red.png",Unlock:["skin.task"],Automatic:true,Priority:20)]),ClientGame.Json);
        var modContent=new ClientContent(ModRead,["conquest1.btl"]);
        Check(modContent.Extensions.Revision==priorRevision,"cosmetic skin configuration preserves rules fingerprint");
        client=new ClientGame(modContent,Path.Combine(folder,"mod"),saveIdentity:"mods/test/logic");view=Call(new{});
        var skinRow=view.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==id);
        Check(!skinRow.GetProperty("skins")[0].GetProperty("unlocked").GetBoolean()&&skinRow.GetProperty("portrait").ValueKind==JsonValueKind.Null,"locked skin not applied");
        Check(skinRow.GetProperty("skins")[0].GetProperty("priority").GetInt32()==20,"skin priority exposed for automatic carousel preview");
        Result(new{op="select",commander=id,revision=0,knownVersion="mods/test/logic",offer="skin.red"},false);
        var account=new PlayerAccount{Id="test",ContentRevision=modContent.Catalogs.Accounts.SourceRevision,Medals=100000,Badges=100000,
            Roster=[modContent.Catalogs.Accounts.FromDefinition(row.GetProperty("legacyId").GetInt32(),"test.owned")]};
        Directory.CreateDirectory(Path.Combine(folder,"mod"));File.WriteAllBytes(Path.Combine(folder,"mod/account.json"),JsonSerializer.SerializeToUtf8Bytes(account,ClientGame.Json));
        client=new ClientGame(modContent,Path.Combine(folder,"mod"),saveIdentity:"mods/test/logic");view=Call(new{});
        skinRow=view.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==id);
        Check(skinRow.GetProperty("portrait").GetString()=="rewrite/skins/red.png","objective completion automatically applies skin");
        view=Call(new{op="select",commander=id,revision=0,knownVersion="mods/test/logic",offer="original"});
        Check(view.GetProperty("entries").EnumerateArray().Single(e=>e.GetProperty("id").GetString()==id).GetProperty("portrait").ValueKind==JsonValueKind.Null,"manual original wins over automatic skin");
        Check(Call(new{}).GetProperty("identity").GetString()=="mods/test/logic","profile identity isolated");
        return checks;
    }
}
