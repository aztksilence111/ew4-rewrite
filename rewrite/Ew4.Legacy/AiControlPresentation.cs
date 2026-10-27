using System.Text.RegularExpressions;
using Ew4.Core;

namespace Ew4.Legacy;

public sealed record AiControlPresentation
{
    public string Title { get; init; } = "AI设定";
    public string[] Buttons { get; init; } = ["指定战斗方针", "框选单位", "指定行动目标", "开始执行", "停止执行"];
    public string[] Policies { get; init; } = ["玩家指挥", "0 自由行动", "1 积极进攻", "2 消极进攻", "3 原地防守", "4 逃离战场"];
    public string? PanelImage { get; init; }
    public string ButtonImage { get; init; } = "client/ui/image_ui_hd/btn_common_green.png";
    public string? BadgeImage { get; init; }
    public int Width { get; init; } = 224;
    public int ButtonHeight { get; init; } = 40;
    public int FontSize { get; init; } = 16;
    public int TitleFontSize { get; init; } = 20;
    public int Gap { get; init; } = 10;
    public string ConfirmText { get; init; } = "确认";
    public string CancelSelectionText { get; init; } = "取消选择";
    public string CancelText { get; init; } = "取消";
    public string CancelDraftText { get; init; } = "取消设定";
    public string ClearGoalText { get; init; } = "清除目标";
    public string AutoBattleText { get; init; } = "自动战斗";
    public string UnitCountText { get; init; } = "{count} 支部队";
    public string GoalText { get; init; } = "目标 {cell}";
    public string NoPolicyText { get; init; } = "未设方针";
    public string NoGoalText { get; init; } = "未设目标";
    public string ClearSelectionText { get; init; } = "取消全部选中";
    public string GoalOrderText { get; init; } = "目标优先顺序";
    public string GoalCountText { get; init; } = "{count} 个目标";
    public string RaisePriorityText { get; init; } = "提高优先级";
    public string LowerPriorityText { get; init; } = "降低优先级";
    public string RemoveTargetText { get; init; } = "移除目标";
    public string TargetLimitText { get; init; } = "最多设置32个目标";
    public int TargetListHeight { get; init; } = 180;
    public string PanelColor { get; init; } = "#e9e7e1";
    public string HeaderColor { get; init; } = "#414442";
    public string TextColor { get; init; } = "#303b36";
    public string ButtonTextColor { get; init; } = "#ffffff";
    public string SelectionColor { get; init; } = "#ffe995";
    public string[] TaskColors { get; init; } = ["#ffe995", "#55d9e8", "#cf8cf3", "#ffad62", "#8ee17b", "#f38eb6", "#83afff", "#e3d36f", "#73d5b5", "#dba6ff", "#f0b69d", "#b3d86b", "#96c9ed", "#e89fe1", "#babdf7", "#dae7a0"];
    public string[] TaskKinds { get; init; } = ["进攻地点", "防守地点", "沿线进攻", "沿线防守"];
    public string TaskText { get; init; } = "任务{number}";
    public string PreviousTaskText { get; init; } = "上一个任务";
    public string NextTaskText { get; init; } = "下一个任务";
    public string AddTaskText { get; init; } = "新增任务";
    public string DeleteTaskText { get; init; } = "删除当前任务";
    public string RadiusText { get; init; } = "防守范围";
    public string LineText { get; init; } = "绘制部署线";
    public string DeployFirstText { get; init; } = "部署完成后进攻";
    public string DefendGoalText { get; init; } = "指定防守地点";
    public string AttackGoalText { get; init; } = "指定进攻目标";
    public string TransferredText { get; init; } = "已转入{count}支部队";
    public string DeployingText { get; init; } = "部署中";
    public string AdvancingText { get; init; } = "推进中";
    public string DefendingText { get; init; } = "防守中";
    public string UnassignedText { get; init; } = "{count}支无可用站位";
    public string BadgeText { get; init; } = "AI";
    public string BadgeColor { get; init; } = "#343e38";
    public string BadgeTextColor { get; init; } = "#fff4c5";
    public string BadgeBorderColor { get; init; } = "#d8d0a2";
    public string? PressedButtonImage { get; init; }
    public string? ConfirmImage { get; init; }
    public int ConfirmWidth { get; init; } = 112;
    public int ConfirmHeight { get; init; } = 46;
    public int CancelWidth { get; init; } = 100;
    public int PolicyWidth { get; init; } = 236;
    public int PolicyButtonHeight { get; init; } = 36;
    public int OffsetRight { get; init; }
    public int OffsetY { get; init; }
    public int BadgeWidth { get; init; } = 20;
    public int BadgeHeight { get; init; } = 12;
    public int BadgeX { get; init; } = -10;
    public int BadgeY { get; init; } = -27;
    public string ArrowImage { get; init; } = "arrow_red.png";
    public string LineColor { get; init; } = "#ffdb65";
    public int LineWidth { get; init; } = 2;
    public int DashLength { get; init; } = 8;
    public static void Validate(AiControlPresentation value, Func<string, byte[]> read)
    {
        if (value == null || value.Title == null || value.Title.Length > 80 || value.Buttons == null || value.Buttons.Length != 5
            || value.Policies == null || value.Policies.Length != 6 || value.Buttons.Concat(value.Policies).Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80)
            || value.Width is < 120 or > 320 || value.ButtonHeight is < 28 or > 64 || value.FontSize is < 10 or > 24 || value.TitleFontSize is < 10 or > 30
            || value.Gap is < 0 or > 24 || value.TargetListHeight is < 72 or > 360 || value.ConfirmWidth is < 80 or > 240 || value.ConfirmHeight is < 36 or > 96
            || value.CancelWidth is < 80 or > 200 || value.PolicyWidth is < 180 or > 480 || value.PolicyButtonHeight is < 28 or > 64
            || value.OffsetRight is < 0 or > 80 || value.OffsetY is < -120 or > 120 || value.BadgeWidth is < 8 or > 64 || value.BadgeHeight is < 8 or > 48
            || value.BadgeX is < -80 or > 80 || value.BadgeY is < -80 or > 80 || value.LineWidth is < 1 or > 8 || value.DashLength is < 2 or > 32
            || value.LineColor == null || !Regex.IsMatch(value.LineColor, "^#[0-9a-fA-F]{6}$")
            || value.ArrowImage == null || !Regex.IsMatch(value.ArrowImage, "^[a-zA-Z0-9_-]+\\.png$"))
            throw new RuleViolation("extension.ai_control", "Invalid AI control presentation");
        if (new[] { value.ConfirmText, value.CancelText, value.CancelSelectionText, value.CancelDraftText, value.ClearGoalText, value.AutoBattleText,
                value.UnitCountText, value.GoalText, value.NoPolicyText, value.NoGoalText, value.BadgeText, value.ClearSelectionText, value.GoalOrderText,
                value.GoalCountText, value.RaisePriorityText, value.LowerPriorityText, value.RemoveTargetText, value.TargetLimitText }.Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80)
            || new[] { value.PanelColor, value.HeaderColor, value.TextColor, value.ButtonTextColor, value.SelectionColor,
                value.BadgeColor, value.BadgeTextColor, value.BadgeBorderColor }.Any(s => s == null || !Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$")))
            throw new RuleViolation("extension.ai_control", "Invalid AI control labels or colors");
        if (value.TaskColors == null || value.TaskColors.Length != PlayerAi.MaximumTasks || value.TaskColors.Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.TaskColors.Length
            || value.TaskColors.Any(s => s == null || !Regex.IsMatch(s, "^#[0-9a-fA-F]{6}$"))
            || value.TaskKinds == null || value.TaskKinds.Length != 4
            || value.TaskKinds.Concat(new[] { value.TaskText, value.PreviousTaskText, value.NextTaskText, value.AddTaskText, value.DeleteTaskText,
                value.RadiusText, value.LineText, value.DeployFirstText, value.DefendGoalText, value.AttackGoalText, value.TransferredText,
                value.DeployingText, value.AdvancingText, value.DefendingText, value.UnassignedText }).Any(s => string.IsNullOrWhiteSpace(s) || s.Length > 80))
            throw new RuleViolation("extension.ai_control", "Invalid AI task presentation");
        foreach (var path in new[] { value.PanelImage, value.ButtonImage, value.BadgeImage, value.PressedButtonImage, value.ConfirmImage }.OfType<string>())
        {
            if (!path.StartsWith("client/", StringComparison.Ordinal) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p.Length == 0 || p is "." or "..") || !path.EndsWith(".png", StringComparison.Ordinal))
                throw new RuleViolation("extension.ai_control", "Invalid AI presentation image path");
            var data = read(path);
            if (data.Length < 8 || data.Length > 4 * 1024 * 1024 || !data.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new RuleViolation("extension.ai_control", "AI presentation image must be PNG");
        }
        using var sprites = System.Text.Json.JsonDocument.Parse(read("client/manifest.json"));
        if (!sprites.RootElement.GetProperty("atlases").GetProperty("buildings_hd").TryGetProperty(value.ArrowImage, out _))
            throw new RuleViolation("extension.ai_control", "Unknown AI target arrow sprite");
    }
}
