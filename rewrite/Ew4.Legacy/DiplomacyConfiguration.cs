using System.Text.RegularExpressions;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record DiplomacyPresentation
{
    public string Title { get; init; } = "外交";
    public string WarText { get; init; } = "宣战";
    public string DemandText { get; init; } = "劝降";
    public string ConfirmText { get; init; } = "确认";
    public string CancelText { get; init; } = "取消";
    public string BuildingsText { get; init; } = "核心地区 {remaining}/{initial}";
    public string OccupiedText { get; init; } = "我方占领 {count}";
    public string ChanceText { get; init; } = "成功率 {chance}%";
    public string WarConfirmation { get; init; } = "向{name}宣战？双方将立即进入战争状态。";
    public string AlliedWarning { get; init; } = "将解除双方同盟。";
    public string DemandConfirmation { get; init; } = "要求{name}投降？失败后需再攻占一处新的核心地区才能重试。";
    public string AcceptedText { get; init; } = "{name}接受投降。";
    public string RefusedText { get; init; } = "{name}拒绝投降，需再攻占一处新的核心地区。";
    public string CoreText { get; init; } = "重叠核心 {count}/{initial} · 核心加成 +{bonus}% · 组合修正 {pair}%";
    public string CapitalText { get; init; } = "首都：{name} · 国库缴获 {percent}%{used}";
    public string CapturedText { get; init; } = "（已触发）";
    public string NoCapitalText { get; init; } = "未设首都";
    public string LootedText { get; init; } = "攻陷{name}首都，缴获金币{money}、工业{industry}、粮食{food}（{percent}%）";
    public string LostTreasuryText { get; init; } = "首都失陷，损失金币{money}、工业{industry}、粮食{food}（{percent}%）";
    public string DeclaredText { get; init; } = "已向{name}宣战。";
    public string[] Relations { get; init; } = ["盟友", "中立", "敌对", "已灭亡"];
    public Dictionary<string, string> Reasons { get; init; } = new() {
        ["target"] = "无效目标", ["defeated"] = "已灭亡", ["turn"] = "等待本国回合", ["ai"] = "请先停止AI执行",
        ["protected"] = "剧情目标国家，暂不开放外交", ["war"] = "双方已交战", ["peace"] = "需先宣战",
        ["human"] = "不能随机劝降真人国家", ["legacy"] = "本局缺少开局记录", ["no_buildings"] = "没有可计入的核心地区",
        ["occupation"] = "尚未占领核心地区", ["progress"] = "需再攻占一处新核心"
    };
    public int Width { get; init; } = 440;
    public int RowHeight { get; init; } = 96;
    public int FontSize { get; init; } = 14;
    public int TitleFontSize { get; init; } = 18;
    public int Gap { get; init; } = 8;
    public int ButtonSize { get; init; } = 30;
    public int EntrySize { get; init; } = 27;
    public int EntryGap { get; init; } = 8;
    public string Font { get; init; } = "font_text_2_cn_hd";
    public string TextColor { get; init; } = "#41413e";
    public string HeaderColor { get; init; } = "#414442";
    public string HeaderTextColor { get; init; } = "#ffffff";
    public string RowColor { get; init; } = "#d5d5cf";
    public string BorderColor { get; init; } = "#999b94";
    public string BackgroundImage { get; init; } = "client/ui/image_ui_hd/form_back.png";
    public string TitleImage { get; init; } = "client/ui/image_ui_hd/form_title.png";
    public string CloseImage { get; init; } = "client/ui/image_ui_hd/button_close.png";
    public string PatternImage { get; init; } = "client/ui/image_ui_hd/common_pattern.png";
    public string EntryBackground { get; init; } = "client/ui/diplomacy/button.png";
    public string WarBackground { get; init; } = "client/ui/image_ui_hd/btn_common_red.png";
    public string DemandBackground { get; init; } = "client/ui/image_ui_hd/btn_common_green.png";
    public string EntryIcon { get; init; } = "client/ui/diplomacy/handshake.png";
    public string WarIcon { get; init; } = "client/ui/diplomacy/swords.png";
    public string DemandIcon { get; init; } = "client/ui/diplomacy/scroll-text.png";

    public void Validate(Func<string, byte[]> read)
    {
        var defaults = new DiplomacyPresentation();
        if (Relations == null || Relations.Length != 4 || Reasons == null || Reasons.Keys.Any(k => !defaults.Reasons.ContainsKey(k))
            || Width is < 320 or > 640 || RowHeight is < 74 or > 140 || FontSize is < 12 or > 22 || TitleFontSize is < 14 or > 28
            || Gap is < 2 or > 20 || ButtonSize is < 24 or > 64 || EntrySize is < 20 or > 64 || EntryGap is < 4 or > 32
            || Font == null || !Regex.IsMatch(Font, "^[a-zA-Z0-9_-]{1,80}$"))
            throw new RuleViolation("diplomacy.presentation", "Invalid diplomacy layout");
        foreach (var property in GetType().GetProperties().Where(p => p.PropertyType == typeof(string)))
        {
            string? value = (string?)property.GetValue(this);
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240)
                throw new RuleViolation("diplomacy.presentation", "Invalid diplomacy text or image");
            if (property.Name.EndsWith("Color", StringComparison.Ordinal) && !Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$"))
                throw new RuleViolation("diplomacy.presentation", "Invalid diplomacy color");
        }
        if (Relations.Concat(Reasons.Values).Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 160))
            throw new RuleViolation("diplomacy.presentation", "Invalid diplomacy labels");
        foreach (string path in new[] { BackgroundImage, TitleImage, CloseImage, PatternImage, EntryBackground, WarBackground, DemandBackground, EntryIcon, WarIcon, DemandIcon })
        {
            if (!path.StartsWith("client/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':')
                || path.Split('/').Any(p => p is "" or "." or "..") || !path.EndsWith(".png", StringComparison.Ordinal))
                throw new RuleViolation("diplomacy.presentation", "Invalid diplomacy image path");
            byte[] bytes = read(path);
            if (bytes.Length < 8 || bytes.Length > 4 * 1024 * 1024 || !bytes.AsSpan(0, 8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10}))
                throw new RuleViolation("diplomacy.presentation", "Diplomacy images must be PNG");
        }
    }
}

public sealed record DiplomacyConfiguration
{
    public const string Path = "rewrite/diplomacy.json";
    public int Schema { get; init; } = 1;
    public DemandRules Rules { get; init; } = new();
    public Dictionary<string, string[]> ScenarioLocks { get; init; } = new();
    public Dictionary<string, FactionPairModifier[]> ScenarioPairs { get; init; } = new();
    public Dictionary<string, Dictionary<string, int>> ScenarioCapitals { get; init; } = new();
    public DiplomacyPresentation Presentation { get; init; } = new();
    public static DiplomacyConfiguration Read(Func<string, byte[]> read)
    {
        byte[] bytes;
        try { bytes = read(Path); }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or KeyNotFoundException) { return new(); }
        var value = ExtensionLoader.ParseJson<DiplomacyConfiguration>(bytes);
        if (value.Schema != 1 || value.Rules == null || value.Presentation == null || value.ScenarioLocks == null || value.ScenarioPairs == null || value.ScenarioCapitals == null
            || value.ScenarioPairs.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null)
            || value.ScenarioCapitals.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Any(c => string.IsNullOrWhiteSpace(c.Key) || c.Value < 0))
            || value.ScenarioLocks.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value == null || p.Value.Any(string.IsNullOrWhiteSpace)))
            throw new RuleViolation("diplomacy.configuration", "Invalid diplomacy configuration");
        value.Rules.Validate(); value.Presentation.Validate(read);
        foreach (var pairs in value.ScenarioPairs.Values) (new DemandRules { PairModifiers = pairs }).Validate();
        return value with { Presentation = value.Presentation with { Reasons = new DiplomacyPresentation().Reasons
            .Concat(value.Presentation.Reasons).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Last().Value) } };
    }

    public DemandRules ResolveRules(string scenario, IReadOnlyDictionary<string, string> countries, string player)
    {
        IEnumerable<string> Resolve(string selector) => selector == "@player" ? new[] { player }
            : countries.ContainsKey(selector) ? new[] { selector } : countries.Where(p => p.Value == selector).Select(p => p.Key);
        var pairs = Rules.PairModifiers.Concat(ScenarioPairs.GetValueOrDefault("*") ?? []).Concat(ScenarioPairs.GetValueOrDefault(scenario) ?? [])
            .SelectMany(p => Resolve(p.Actor).SelectMany(a => Resolve(p.Target).Where(t => t != a)
                .Select(t => p with { Actor = a, Target = t }))).GroupBy(p => (p.Actor, p.Target)).Select(g => g.Last()).ToArray();
        var locked = Rules.LockedFactions.Concat(ScenarioLocks.GetValueOrDefault("*") ?? []).Concat(ScenarioLocks.GetValueOrDefault(scenario) ?? [])
            .SelectMany(Resolve).Distinct().OrderBy(f => f, StringComparer.Ordinal).ToArray();
        return Rules with { LockedFactions = locked, PairModifiers = pairs };
    }
}
