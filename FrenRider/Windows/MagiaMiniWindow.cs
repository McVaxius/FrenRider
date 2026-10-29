using AethertekUI.Dalamud;
using System;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

public sealed class MagiaMiniWindow : Window, IDisposable
{
    private readonly MaterialWindowMotion motion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
    private readonly Plugin plugin;

    public MagiaMiniWindow(Plugin plugin)
        : base("Fren Rider Mini###FrenRiderMagiaMini")
    {
        this.plugin = plugin;
        Flags = ImGuiWindowFlags.AlwaysAutoResize
                | ImGuiWindowFlags.NoScrollbar
                | ImGuiWindowFlags.NoScrollWithMouse;
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Home, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleMainUi(); },
            ShowTooltip = () => UiGui.SetTooltip("Fren Rider"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => UiGui.SetTooltip("Settings"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.PowerOff, Priority = -20, IconOffset = new(2, 1),
            Click = button =>
            {
                if (button == ImGuiMouseButton.Left && Plugin.ClientState.IsLoggedIn && plugin.ConfigManager.TryGetActiveConfig(out var active) && active != null)
                    plugin.ConfigManager.SetFrenRiderEnabled(!active.Enabled);
            },
            ShowTooltip = () => MaterialText.SetTooltip(Plugin.ClientState.IsLoggedIn && plugin.ConfigManager.TryGetActiveConfig(out var active) && active != null
                ? UiText.T("Run") + ": " + UiText.T(active.Enabled ? "Enabled" : "Disabled")
                : UiText.T("Not logged in. FrenRider waits until a character is loaded.")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.FeatherAlt, Priority = -30, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.OpenChocoboSettings(); },
            ShowTooltip = () => UiGui.SetTooltip("Chocobo settings"),
        });
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        var compact = plugin.Configuration.UiCompact;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, UiHelpers.Scale(new Vector2(compact ? 10 : 12)));
        var framePadding = FrenRiderPresentation.IsLcars ? UiHelpers.Scale(40) : 0;
        var width = UiHelpers.Scale(compact ? 400 : 440) + framePadding;
        width = Math.Max(width, UiGui.TitleMinimumWidth(this, UiText.T("Fren Rider Mini"), people: true, brandSize: 32, compactBrand: compact));
        if (IsEurekaTerritory(plugin.ZoneService.TerritoryId))
        {
            using var actionFont = UiText.Font(UiFontRole.Action);
            var labelWidth = Math.Max(MaterialText.Measure(UiText.T("Attack")).X,
                Math.Max(MaterialText.Measure(UiText.T("Defense")).X, MaterialText.Measure(UiText.T("Off")).X));
            width = Math.Max(width, (labelWidth + ImGui.GetStyle().FramePadding.X * 2) * 3
                + ImGui.GetStyle().ItemSpacing.X * 2 + ImGui.GetStyle().WindowPadding.X * 2 + framePadding);
        }
        ImGui.SetNextWindowSize(new Vector2(MathF.Ceiling(width), 0));
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        try
        {
            movementLock.Restore(this);
            motion.Restore(this);
            UiGui.ImageTitleWithStatus(this, UiText.T("Fren Rider Mini"), plugin.OriginalIcon, plugin.TitlebarStatus());
            plugin.DrawAppearanceTitleBarPopups(this);
            plugin.ApplyWindowOpacity(windowOpacity, WindowName);
        }
        finally { ImGui.PopStyleVar(); }
    }

    public override void Draw()
    {
        motion.DrawChrome();
        FrenRiderPresentation.DrawBackdrop(windowOpacity.CurrentOpacity);
        using var fields = new MaterialStyleScope();
        fields.Style(ImGuiStyleVar.FrameBorderSize, 1);
        fields.Color(ImGuiCol.FrameBg, MaterialTheme.Current.Colors.SurfaceContainer);
        fields.Color(ImGuiCol.Border, MaterialTheme.Current.Colors.OutlineVariant);
        using var controls = MaterialControls.Push(MaterialControlContext.Toolbar);
        var originalIdRoot = ImGui.GetID("");
        using var tableStyle = new MaterialStyleScope();
        tableStyle.Style(ImGuiStyleVar.CellPadding, Vector2.Zero);
        var width = Math.Max(1, ImGui.GetContentRegionAvail().X);
        if (!ImGui.BeginTable("##FrenRiderMiniBounds", 1, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                new Vector2(width, 0))) return;
        try
        {
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthFixed, width);
            ImGui.TableNextColumn();
            ImGuiP.PushOverrideID(originalIdRoot);
            try { DrawContent(); }
            finally { ImGui.PopID(); }
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawContent()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var compact = plugin.Configuration.UiCompact;
        if (FrenRiderPresentation.IsLcars)
        {
            using (UiText.Font(UiFontRole.Title)) FrenRiderPresentation.LcarsHeader("FREN RIDER");
        }
        else
        {
            var icon = plugin.OriginalIcon;
            var imageMin = ImGui.GetCursorScreenPos();
            var brandSize = UiHelpers.Scale(compact ? 26 : 30);
            MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(), icon.Handle, icon.Size, imageMin, imageMin + new Vector2(brandSize));
            ImGui.Dummy(new Vector2(brandSize));
            ImGui.SameLine();
            using (UiText.Font(UiFontRole.Title)) UiGui.Text("Fren Rider");
        }
        UiGui.LcarsPanel("##FrenRiderMiniRunFrame", () => DrawRunControls(config));
        if (IsEurekaTerritory(plugin.ZoneService.TerritoryId))
            UiGui.LcarsPanel("##FrenRiderMiniMagiaFrame", DrawMagiaControls, 2);
        if (!FrenRiderPresentation.IsLcars) ImGui.Separator();
        UiGui.LcarsPanel("##FrenRiderMiniCompanionFrame", () => UiGui.DrawCompanionPurchases(plugin, config, mini: true), 1);
        if (FrenRiderPresentation.IsLcars) FrenRiderPresentation.LcarsFooter(UiText.T("Fren Rider Mini"));
    }

    private void DrawRunControls(CharacterConfig config)
    {
        var enabled = config.Enabled;
        if (UiGui.HeaderToggle("FrenRider", ref enabled, display: UiText.T("Run")))
            plugin.ConfigManager.SetFrenRiderEnabled(enabled);
        var status = plugin.IsWaitingForDuty ? UiHelpers.Yellow : enabled ? UiHelpers.Green : UiHelpers.Grey;
        ContinueButton(enabled ? "ON" : "OFF", 0);
        UiHelpers.StatusPill(enabled ? "ON" : "OFF", status,
            plugin.IsWaitingForDuty ? "Combat rotations paused outside duties" : null);
        DrawFrenSelector(config);
    }

    private void DrawMagiaControls()
    {
        UiGui.SettingsSection("MAGIA");
        using var actionFont = UiText.Font(UiFontRole.Action);
        var labelWidth = Math.Max(MaterialText.Measure(UiText.T("Attack")).X,
            Math.Max(MaterialText.Measure(UiText.T("Defense")).X, MaterialText.Measure(UiText.T("Off")).X));
        var size = new Vector2(Math.Max(labelWidth + ImGui.GetStyle().FramePadding.X * 2,
            (ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2) / 3), 0);
        var colors = MaterialTheme.Current.Colors;
        using (var attackStyle = new MaterialStyleScope())
        {
            var fill = MaterialColor.Layer(colors.PrimaryContainer, colors.Primary, .25f);
            attackStyle.Color(ImGuiCol.Button, fill);
            attackStyle.Color(ImGuiCol.ButtonHovered, MaterialColor.Layer(fill, colors.OnSurface, .08f));
            attackStyle.Color(ImGuiCol.ButtonActive, MaterialColor.Layer(fill, colors.OnSurface, .12f));
            if (UiGui.Button("Attack", size, gradientBottom: colors.PrimaryContainer))
                GameHelpers.SendChatCommand("/magiaauto attack", "Fren Rider mini");
        }
        ContinueButton("Defense", size.X);
        if (UiGui.Button("Defense", size)) GameHelpers.SendChatCommand("/magiaauto defense", "Fren Rider mini");
        ContinueButton("Off", size.X);
        if (UiGui.Button("Off", size)) GameHelpers.SendChatCommand("/magiaauto off", "Fren Rider mini");
    }

    private static void ContinueButton(string label, float minimumWidth)
    {
        var width = Math.Max(minimumWidth, MaterialText.Measure(UiText.T(label)).X + ImGui.GetStyle().FramePadding.X * 2);
        var right = FrenRiderPresentation.Decorated
            ? ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X
            : ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
    }

    private void DrawFrenSelector(CharacterConfig config)
    {
        var fieldStart = ImGui.GetCursorScreenPos().X + MaterialText.Measure(UiText.T("Fren")).X + ImGui.GetStyle().ItemSpacing.X;
        ImGui.AlignTextToFramePadding();
        UiGui.TextUnformatted("Fren");
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(fieldStart, ImGui.GetCursorScreenPos().Y));
        ImGui.SetNextItemWidth(MaterialLayout.FitNextItemWidth(ImGui.GetContentRegionAvail().X, UiHelpers.Scale(140)));

        var preview = string.IsNullOrWhiteSpace(config.FrenName)
            ? UiText.T("<none>")
            : Display(config.FrenName);

        if (!ImGui.BeginCombo("##FrenRiderMiniFren", preview))
            return;

        var noneSelected = string.IsNullOrWhiteSpace(config.FrenName);
        if (UiGui.Selectable("<none>", noneSelected))
        {
            config.FrenName = string.Empty;
            plugin.ConfigManager.SaveCurrentAccount();
        }

        if (noneSelected)
            ImGui.SetItemDefaultFocus();

        var partyCount = Plugin.PartyList.Length;
        var savedFrenInParty = false;
        if (partyCount == 0)
        {
            UiGui.TextDisabled("Not in a party");
        }

        for (var i = 0; i < partyCount; i++)
        {
            var member = Plugin.PartyList[i];
            if (member == null)
                continue;

            var memberName = member.Name.ToString();
            var worldName = member.World.Value.Name.ToString();
            var identity = $"{memberName}@{worldName}";
            var selected = string.Equals(config.FrenName, identity, StringComparison.OrdinalIgnoreCase);
            savedFrenInParty |= selected;

            // Use the exact native identity for selection, independent of locale or Krangle.
            if (UiGui.Selectable(identity, selected, Display(identity)))
            {
                config.FrenName = identity;
                plugin.ConfigManager.SaveCurrentAccount();
            }

            if (selected)
                ImGui.SetItemDefaultFocus();
        }

        if (!savedFrenInParty && !noneSelected)
        {
            ImGui.BeginDisabled();
            UiGui.Selectable(config.FrenName, true, Display(config.FrenName));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiGui.SetTooltip("Saved fren is outside the current party.");
        }

        ImGui.EndCombo();
    }

    private string Display(string identity)
        => plugin.Configuration.KrangleEnabled
            ? KrangleService.KrangleName(identity)
            : identity;

    private static bool IsEurekaTerritory(uint territoryId)
        => territoryId is 732 or 763 or 795 or 827;
}
