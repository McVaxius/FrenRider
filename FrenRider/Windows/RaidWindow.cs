using System;
using System.Numerics;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FrenRider.Windows;

public sealed class RaidWindow : Window
{
    private const string Title = "FrenRider · Raid Mode";
    private readonly Plugin plugin;
    private readonly MaterialWindowMotion motion = new();
    private readonly MaterialWindowOpacity windowOpacity = new();
    private readonly MaterialWindowLock movementLock = new();

    public RaidWindow(Plugin plugin)
        : base(Title + "###FrenRiderRaidMode")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoScrollWithMouse;
    }

    public override void PreDraw()
    {
        var compact = plugin.Configuration.UiCompact;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiHelpers.Scale(new Vector2(compact ? 12 : 16)));
        var style = ImGui.GetStyle();
        var checkboxWidth = MaterialText.Measure(UiText.T("Remain until manually exited")).X
            + ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
        var waitWidth = MaterialText.Measure(UiText.T("Wait up to")).X
            + MaterialText.Measure(UiText.T("s for loot to appear")).X + UiHelpers.Scale(64)
            + style.ItemSpacing.X * 2 + ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
        var width = Math.Max(UiHelpers.Scale(compact ? 400 : 440),
            Math.Max(checkboxWidth, waitWidth) + style.WindowPadding.X * 2);
        width = Math.Max(width, UiGui.TitleMinimumWidth(this, UiText.T(Title),
            people: true, brandSize: 32, compactBrand: compact));
        if (FrenRiderPresentation.IsLcars) width += UiHelpers.Scale(40);
        ImGui.SetNextWindowSize(new Vector2(MathF.Ceiling(width), 0));
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled,
            roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        try
        {
            movementLock.Restore(this);
            motion.Restore(this);
            UiGui.ImageTitleWithStatus(this, UiText.T(Title), plugin.OriginalIcon, plugin.TitlebarStatus());
            plugin.DrawAppearanceTitleBarPopups(this, windowOpacity);
            plugin.ApplyWindowOpacity(windowOpacity, WindowName);
        }
        finally { ImGui.PopStyleVar(); }
    }

    public override void Draw()
    {
        motion.DrawChrome();
        FrenRiderPresentation.DrawBackdrop(windowOpacity.CurrentOpacity);
        using var glassBackgrounds = FrenRiderPresentation.GlassBackgroundStyle();
        using var controls = MaterialControls.Push(FrenRiderPresentation.Controls(plugin.Configuration.UiCompact ? 32 : 36));
        using var spacing = new MaterialStyleScope();
        spacing.Style(ImGuiStyleVar.ItemSpacing, UiHelpers.Scale(new Vector2(8, plugin.Configuration.UiCompact ? 8 : 12)));
        if (FrenRiderPresentation.IsLcars)
        {
            using (UiText.Font(UiFontRole.Title)) FrenRiderPresentation.LcarsHeader(UiText.T("Raid Mode"));
        }
        UiGui.LcarsPanel("##FrenRiderRaidFrame", DrawContent);
    }

    private void DrawContent()
    {
        var raid = plugin.RaidModeService;
        var origin = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        var dotSize = UiHelpers.Scale(12);
        ImGui.GetWindowDrawList().AddCircleFilled(origin + new Vector2(dotSize * .5f, lineHeight * .5f),
            dotSize * .5f, MaterialCanvas.Color(raid.IsEnabled ? UiHelpers.Green : UiHelpers.Grey), 24);
        ImGui.Dummy(new Vector2(dotSize, lineHeight));
        ImGui.SameLine();
        using (UiText.Font(UiFontRole.BodyStrong)) UiGui.Text("Raid Mode");
        var status = UiText.T(raid.StatusText);
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + MaterialText.Measure(status).X
            <= ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X)
            ImGui.SameLine();
        UiGui.TextDisabled(status);
        ImGui.Separator();

        var loot = raid.AdsLootEnabled;
        var lootState = UiText.T(loot ? "ON" : "OFF");
        var scale = Math.Max(.01f, MaterialTheme.Metrics.Scale);
        var labelWidth = (ImGui.GetContentRegionAvail().X - MaterialText.Measure(lootState).X
            - ImGui.GetStyle().ItemSpacing.X) / scale - 60;
        if (UiGui.HeaderToggle("ADS Loot", ref loot, Math.Max(0, labelWidth))) raid.AdsLootEnabled = loot;
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Use ADS for chests and loot rolls during Raid mode. Uses your ADS roll policy, or Need if it is Off; your normal settings are unchanged.");
        ImGui.SameLine();
        MaterialText.TextDisabled(lootState);

        var acceptRez = raid.AcceptRez;
        if (UiGui.Checkbox("Accept Rez", ref acceptRez)) raid.AcceptRez = acceptRez;

        var remain = raid.RemainUntilManualExit;
        if (UiGui.Checkbox("Remain until manually exited", ref remain)) raid.RemainUntilManualExit = remain;
        var indent = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
        ImGui.Indent(indent);
        UiGui.TextDisabled("Uncheck to leave after loot is distributed.");
        ImGui.BeginDisabled(remain);
        try
        {
            ImGui.AlignTextToFramePadding();
            UiGui.Text("Wait up to");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(UiHelpers.Scale(56));
            var waitSeconds = raid.LootAppearanceWaitSeconds;
            using (FrenRiderPresentation.NativeFieldStyle())
                if (ImGui.InputInt("##RaidLootAppearanceWaitSeconds", ref waitSeconds, 0, 0))
                    raid.LootAppearanceWaitSeconds = waitSeconds;
            ImGui.SameLine();
            ImGui.AlignTextToFramePadding();
            UiGui.Text("s for loot to appear");
        }
        finally { ImGui.EndDisabled(); }
        ImGui.Unindent(indent);

        using var actionFont = UiText.Font(UiFontRole.Action);
        if (UiGui.Button("Disable Raid Mode", new Vector2(ImGui.GetContentRegionAvail().X,
            UiHelpers.Scale(plugin.Configuration.UiCompact ? 36 : 40))))
            raid.Disable();
    }
}
