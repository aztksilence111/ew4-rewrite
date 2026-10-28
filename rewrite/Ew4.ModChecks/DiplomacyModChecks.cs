using System.Text;
using System.Text.Json;
using Ew4.Legacy;
using Ew4.Mods;
using Ew4.Core;

internal static class DiplomacyModChecks
{
    public static int Run(string root)
    {
        int checks=0;
        void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Diplomacy Mod: "+label);}
        string data=Path.Combine(root,"rewrite/data"),run=Path.Combine(root,"build/diplomacy-20261028/mod-checks",Guid.NewGuid().ToString("N"));
        byte[] Read(string p)=>File.ReadAllBytes(p.StartsWith("frontend/",StringComparison.Ordinal)?Path.Combine(root,"rewrite/Ew4.Web/wwwroot",p[9..]):Path.Combine(data,p));
        string[] files=Directory.EnumerateFiles(data,"*",SearchOption.AllDirectories).Select(p=>Path.GetRelativePath(data,p).Replace('\\','/')).ToArray();
        var baseline=new ModBase(Read,files);
        var oldBase=new ModBase(Read,files.Where(p=>p!=DiplomacyConfiguration.Path));
        var store=new ModStore(baseline,Path.Combine(run,"mods"),Path.Combine(run,"saves"));
        string oldAuthor=Path.Combine(run,"old-author"),author=Path.Combine(run,"author");
        foreach(string folder in new[]{author,oldAuthor})
        {
            Directory.CreateDirectory(Path.Combine(folder,"rewrite"));
            File.WriteAllText(Path.Combine(folder,"rewrite/extension.json"),"{\"schema\":1,\"apiVersion\":3}");
        }
        var oldStore=new ModStore(oldBase,Path.Combine(run,"old-mods"),Path.Combine(run,"old-saves"));
        oldStore.Seal(oldAuthor,"diplomacy-check","外交检查","1.0",["conquest1"]);
        var oldManifest=oldStore.Validate(oldAuthor);
        Check(store.Validate(oldAuthor).LogicRevision==oldManifest.LogicRevision,"adding default diplomacy preserves installed Mod identity");
        byte[] config=Encoding.UTF8.GetBytes("{\"schema\":1,\"rules\":{\"maximumChance\":80},\"presentation\":{\"title\":\"使节\",\"width\":400,\"entryIcon\":\"client/ui/diplomacy/custom.png\",\"reasons\":{\"progress\":\"继续攻城\"}}}");
        File.WriteAllBytes(Path.Combine(author,DiplomacyConfiguration.Path),config);
        Directory.CreateDirectory(Path.Combine(author,"client/ui/diplomacy"));
        File.Copy(Path.Combine(data,"client/ui/diplomacy/handshake.png"),Path.Combine(author,"client/ui/diplomacy/custom.png"),true);
        store.Seal(author,"diplomacy-check","外交检查","1.1",["conquest1"]);
        var installed=store.Import(author,"ignored","ignored");
        Check(installed.LogicRevision==oldManifest.LogicRevision,"presentation and per-game diplomacy policy keep save namespace");
        store.Activate(installed.Key);
        JsonElement Call(ModStore target,string route,object command)
        {
            using var doc=JsonDocument.Parse(target.Dispatch("/api/"+route,JsonSerializer.Serialize(command)));
            Check(doc.RootElement.GetProperty("ok").GetBoolean(),route+": "+doc.RootElement.GetRawText()[..Math.Min(160,doc.RootElement.GetRawText().Length)]);
            return doc.RootElement.GetProperty("data").Clone();
        }
        var view=Call(store,"start",new{scenario="conquest1",country="fra"});
        var diplomacy=Call(store,"diplomacy",new{});
        Check(diplomacy.GetProperty("presentation").GetProperty("title").GetString()=="使节","installed label exposed");
        Check(diplomacy.GetProperty("presentation").GetProperty("width").GetInt32()==400,"installed layout exposed");
        Check(diplomacy.GetProperty("presentation").GetProperty("reasons").TryGetProperty("legacy",out _),"partial labels retain default reasons");
        Check(store.ReadContent("ui/diplomacy/custom.png").SequenceEqual(Read("client/ui/diplomacy/handshake.png")),"custom icon delivered");
        Call(store,"save",new{mode=2,slot=1});
        string archive=Path.Combine(run,"diplomacy-"+Guid.NewGuid().ToString("N")+".ew4mod");store.Export(installed.Key,archive);
        var offline=new ModStore(baseline,Path.Combine(run,"offline-mods"),Path.Combine(run,"offline-saves"));
        var imported=offline.Import(archive,"ignored","ignored");offline.Activate(imported.Key);
        Call(offline,"start",new{scenario="conquest1",country="fra"});
        Check(Call(offline,"diplomacy",new{}).GetProperty("presentation").GetProperty("title").GetString()=="使节","offline package roundtrip retains diplomacy");
        store.Activate(null);Call(store,"start",new{scenario="conquest1",country="fra"});
        Check(Call(store,"diplomacy",new{}).GetProperty("presentation").GetProperty("title").GetString()=="外交","returning to builtin restores presentation");
        var prepared=StartupAssets.Prepare(baseline,Path.Combine(run,"startup"));
        Check(prepared.Content.SaveNamespace==null,"default diplomacy does not hide builtin accounts or saves");
        var tuned=StartupAssets.Prepare(new ModBase(p=>p==DiplomacyConfiguration.Path?config:p=="client/ui/diplomacy/custom.png"?Read("client/ui/diplomacy/handshake.png"):Read(p),files.Append("client/ui/diplomacy/custom.png")),Path.Combine(run,"tuned-startup"));
        Check(tuned.Content.SaveNamespace==null,"presentation and persisted policy do not fork builtin save namespace");
        var player=view.GetProperty("playerFaction").GetString();
        var target=view.GetProperty("factions").EnumerateArray().First(f=>f.GetProperty("id").GetString()!=player).GetProperty("id").GetString()!;
        int cell=view.GetProperty("cells").EnumerateArray().Select((c,i)=>(c,i)).First(p=>p.c.GetProperty("owner").GetString()==target
            && p.c.GetProperty("buildingKind").GetString() is "city" or "industry" or "stable" or "port").i;
        File.WriteAllBytes(Path.Combine(author,"rewrite/extension.json"),JsonSerializer.SerializeToUtf8Bytes(new {
            schema=1,apiVersion=3,scenarioEvents="rewrite/homeland-events.json"}));
        File.WriteAllBytes(Path.Combine(author,"rewrite/homeland-events.json"),JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string,ScriptedScenarioEvent[]> {
            ["conquest1"]=[new() {Id="homeland",Effects=[
                new() {Type="faction_core",Faction=target,Cells=[cell]},
                new() {Type="core_add",Faction="@player",Cells=[cell]},
                new() {Type="surrender_modifier",Faction="@player",OtherFaction=target,Value=-15},
                new() {Type="loot_modifier",Faction="@player",OtherFaction=target,Value=12},
                new() {Type="faction_capital",Faction=target,Cells=[cell]},
                new() {Type="faction_create",Faction="extra.treasury",Name="国库事件",Camp="extra",TemplateFaction=target,
                    InitialResources=new(321,234,123),Capital=cell}
            ]}]
        },ExtensionLoader.Json));
        store.Seal(author,"diplomacy-homeland","本土事件","1.0",["conquest1"]);
        var homelandMod=store.Import(author,"ignored","ignored");store.Activate(homelandMod.Key);
        var expandedView=Call(store,"start",new{scenario="conquest1",country="fra"});
        void Expanded(JsonElement result)
        {
            var row=result.GetProperty("factions").EnumerateArray().Single(f=>f.GetProperty("id").GetString()==target);
            var quote=row.GetProperty("quote");
            Check(quote.GetProperty("initial").GetInt32()==1,"imported Mod opening event overrides cores");
            Check(quote.GetProperty("sharedCores").GetInt32()==1 && quote.GetProperty("pairBonus").GetInt32()==-15,
                "opening claims and signed surrender adjustment survive initialization");
            Check(quote.GetProperty("lootPercent").GetInt32()==52 && row.GetProperty("capital").GetProperty("cell").GetInt32()==cell,
                "opening capital and independent loot adjustment survive initialization");
        }
        Expanded(Call(store,"diplomacy",new{}));
        Check(expandedView.GetProperty("factions").EnumerateArray().Any(f=>f.GetProperty("id").GetString()=="extra.treasury"),
            "opening event creates additional faction with initial treasury");
        string homelandArchive=Path.Combine(run,"homeland-"+Guid.NewGuid().ToString("N")+".ew4mod");store.Export(homelandMod.Key,homelandArchive);
        var offlineHomeland=new ModStore(baseline,Path.Combine(run,"homeland-offline-mods"),Path.Combine(run,"homeland-offline-saves"));
        var homelandImport=offlineHomeland.Import(homelandArchive,"ignored","ignored");offlineHomeland.Activate(homelandImport.Key);
        Call(offlineHomeland,"start",new{scenario="conquest1",country="fra"});
        Expanded(Call(offlineHomeland,"diplomacy",new{}));
        return checks;
    }
}
