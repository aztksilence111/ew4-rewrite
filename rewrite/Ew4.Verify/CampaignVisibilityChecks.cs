using System.Text;
using System.Text.Json;
using Ew4.Client;
using Ew4.Core;

internal static class CampaignVisibilityChecks
{
    public static int Run(string root)
    {
        int checks = 0;
        void Check(bool value, string name) { checks++; if (!value) throw new InvalidOperationException("Campaign visibility: " + name); }
        var files = new Dictionary<string, byte[]>();
        byte[] Read(string path) => files.TryGetValue(path, out var bytes) ? bytes : File.ReadAllBytes(path.StartsWith("data/")
            ? Path.Combine(root, "rewrite/data/legacy", path[5..]) : Path.Combine(root, "rewrite/data", path));
        var scenarios = Directory.GetFiles(Path.Combine(root, "rewrite/data/original"), "*.btl");
        ClientContent Content(bool show = true) => new(Read, scenarios, showHiddenCampaigns: show);
        void Put(string path, string value) => files[path] = Encoding.UTF8.GetBytes(value);
        const string config = "rewrite/definitions/campaign-visibility.json";
        const string manifest = "{\"schema\":1,\"apiVersion\":2,\"campaignVisibility\":\"" + config + "\"}";
        var visible = Content();
        var account = visible.Start("campaign1_01", null).Session.Account;
        Check(visible.Presentations.Values.All(p => !p.Hidden), "builtin entries are visible");
        Check(visible.Presentations.Keys.All(id => CampaignProgress.Accessible(visible, account, id)), "fresh account can enter every campaign");
        Check(Content(false).Presentations.Values.Count(p => p.Hidden) == 11, "mods preserve XML hiding by default");
        Put("rewrite/extension.json", manifest);
        Put(config, "{\"schema\":1,\"visibility\":{\"*\":\"original\"}}");
        var original = Content();
        Check(original.Presentations.Values.Count(p => p.Hidden) == 11, "interface restores all original hidden entries");
        Check(!CampaignProgress.Accessible(original, account, "campaign5_05"), "original mode gates hidden scenario");
        Check(CampaignProgress.Accessible(original, account with { UnlockedCampaigns = ["campaign5_05"] }, "campaign5_05"), "unlock opens original hidden entry");
        Put(config, "{\"schema\":1,\"visibility\":{\"*\":\"original\",\"campaign5_05\":\"visible\",\"campaign1_01\":\"hidden\"}}");
        var mixed = Content();
        Check(!mixed.Presentations["campaign5_05"].Hidden && mixed.Presentations["campaign1_01"].Hidden, "entry overrides wildcard");
        Check(!mixed.Presentations["campaign3_08"].Hidden && mixed.Presentations["campaign3_08"].Alternate == "campaign3_08b", "branch metadata retained");
        foreach (string bad in new[] {
            "{\"schema\":2,\"visibility\":{}}", "{\"schema\":1}", "{\"schema\":1,\"visibility\":null}",
            "{\"schema\":1,\"visibility\":{\"*\":true}}", "{\"schema\":1,\"visibility\":{\"*\":\"typo\"}}",
            "{\"schema\":1,\"visibility\":{\"missing\":\"hidden\"}}", "{\"schema\":1,\"visibility\":{\"conquest1\":\"hidden\"}}",
            "{\"schema\":1,\"visibility\":{\"campaign3_08b\":\"hidden\"}}", "{\"schema\":1,\"visibility\":{\"\":\"hidden\"}}",
            "{\"schema\":1,\"visibility\":{\"*\":\"visible\",\"*\":\"hidden\"}}", "{\"schema\":1,\"visibility\":{},\"typo\":1}" })
        {
            Put(config, bad);
            bool rejected = false;
            try { Content(); } catch (Exception error) when (error is RuleViolation or JsonException) { rejected = true; }
            Check(rejected, "reject invalid configuration: " + bad);
        }
        Put(config, "{\"schema\":1,\"visibility\":{}}");
        Put("rewrite/extension.json", manifest.Replace("\"apiVersion\":2", "\"apiVersion\":1"));
        bool apiRejected = false;
        try { Content(); } catch (RuleViolation) { apiRejected = true; }
        Check(apiRejected, "requires API 2");
        return checks;
    }
}
