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
    private readonly Plugin plugin;
    private Vector2 contentFramePadding;

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
    }

    public void Dispose()
    {
    }

    public override void PreDraw()
    {
        // Native Begin must establish the work rectangle before the inset content measures it.
        var compact = plugin.Configuration.UiCompact;
        var width = UiHelpers.Scale(FrenRiderPresentation.MiniWidth(compact));
        var statusWidth = Math.Max(MaterialText.Measure(UiText.T("ON")).X, MaterialText.Measure(UiText.T("OFF")).X);
        var headerLabelWidth = Math.Max(MaterialText.Measure(UiText.T("FrenRider")).X, UiHelpers.Scale(compact ? 148 : 138));
        width = Math.Max(width, headerLabelWidth + statusWidth + UiHelpers.Scale(compact ? 172 : 173));
        if (IsEurekaTerritory(plugin.ZoneService.TerritoryId))
        {
            using var actionFont = UiText.Font(UiFontRole.Action);
            var labelWidth = Math.Max(MaterialText.Measure(UiText.T("Attack")).X,
                Math.Max(MaterialText.Measure(UiText.T("Defense")).X, MaterialText.Measure(UiText.T("Off")).X));
            var style = ImGui.GetStyle();
            var rowWidth = MathF.Ceiling(labelWidth + style.FramePadding.X * 2) * 3 + UiHelpers.Scale(24);
            width = Math.Max(width, rowWidth + UiHelpers.Scale(42));
        }
        contentFramePadding = ImGui.GetStyle().FramePadding;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
            new Vector2(UiHelpers.Scale(compact ? 7 : 9), (UiHelpers.Scale(FrenRiderPresentation.MiniTitleHeight(compact)) - ImGui.GetFontSize()) * .5f));
        width = Math.Max(width, UiGui.TitleMinimumWidth(this, UiText.T("Fren Rider Mini"), people: true, brandSize: 32, compactBrand: compact));
        ImGui.SetNextWindowSize(new Vector2(MathF.Ceiling(width), 0));
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        UiGui.ImageTitle(this, UiText.T("Fren Rider Mini"), plugin.OriginalIcon);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        motion.DrawChrome();
        var compact = plugin.Configuration.UiCompact;
        using var fields = new MaterialStyleScope();
        fields.Style(ImGuiStyleVar.FramePadding, contentFramePadding);
        fields.Style(ImGuiStyleVar.FrameBorderSize, 1);
        fields.Color(ImGuiCol.FrameBg, MaterialTheme.Current.Colors.SurfaceContainer);
        fields.Color(ImGuiCol.Border, MaterialTheme.Current.Colors.OutlineVariant);
        var position = ImGui.GetWindowPos();
        var scale = MaterialTheme.Metrics.Scale;
        var start = position + new Vector2(21, compact ? 64 : 78) * scale;
        var width = ImGui.GetWindowSize().X - UiHelpers.Scale(42);
        var colors = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(position + new Vector2(7, FrenRiderPresentation.MiniTitleHeight(compact) + 6) * scale,
            position + ImGui.GetWindowSize() - new Vector2(7) * scale, colors.SurfaceContainerLowest, colors.Background, UiHelpers.Scale(4));
        var originalIdRoot = ImGui.GetID("");
        ImGui.SetCursorScreenPos(start);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
        var visible = ImGui.BeginTable("##FrenRiderMiniBounds", 1, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
            new Vector2(Math.Max(1, width), 0));
        ImGui.PopStyleVar();
        try
        {
            if (!visible) return;
            ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthFixed, Math.Max(1, width));
            ImGui.TableNextColumn();
            ImGuiP.PushOverrideID(originalIdRoot);
            try { DrawContent(); }
            finally { ImGui.PopID(); }
        }
        finally { if (visible) ImGui.EndTable(); }
        ImGui.SetCursorScreenPos(new Vector2(start.X,
            Math.Max(ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y, position.Y + UiHelpers.Scale(FrenRiderPresentation.MiniHeight(compact,
                IsEurekaTerritory(plugin.ZoneService.TerritoryId))) - ImGui.GetStyle().WindowPadding.Y)));
        ImGui.Dummy(new Vector2(width, 0));
    }

    private void DrawContent()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var compact = plugin.Configuration.UiCompact;
        var eureka = IsEurekaTerritory(plugin.ZoneService.TerritoryId);
        var position = ImGui.GetWindowPos();
        var scale = MaterialTheme.Metrics.Scale;
        var left = position.X + (compact ? 28 : 29) * scale;

        ImGui.SetCursorScreenPos(new Vector2(left, position.Y + (compact ? 64 : 78) * scale));
        var enabled = config.Enabled;
        if (UiGui.HeaderToggle("FrenRider", ref enabled, compact ? 148 : 138, new Vector2(54, 32)))
            plugin.ConfigManager.SetFrenRiderEnabled(enabled);

        var statusTop = new Vector2(ImGui.GetItemRectMax().X + UiHelpers.Scale(compact ? 37 : 36),
            ImGui.GetItemRectMin().Y);
        var rowHeight = ImGui.GetItemRectMax().Y - ImGui.GetItemRectMin().Y;
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(statusTop);
        var dot = statusTop + new Vector2(UiHelpers.Scale(7), rowHeight * .5f);
        ImGui.GetWindowDrawList().AddCircleFilled(dot, UiHelpers.Scale(7), MaterialCanvas.Color(enabled ? UiHelpers.Green : UiHelpers.Grey), 24);
        ImGui.Dummy(new Vector2(UiHelpers.Scale(14), rowHeight));
        ImGui.SameLine(0, UiHelpers.Scale(compact ? 10 : 11));
        UiGui.TextColored(enabled ? UiHelpers.Green : UiHelpers.Grey, enabled ? "ON" : "OFF");

        ImGui.SetCursorScreenPos(position + new Vector2(compact ? 22 : 21, compact ? 112 : eureka ? 122 : 129) * scale);
        ImGui.Separator();
        ImGui.SetCursorScreenPos(new Vector2(left, position.Y + (compact ? 124 : eureka ? 128 : 142) * scale));
        using (var selectorStyle = new MaterialStyleScope())
        {
            selectorStyle.Style(ImGuiStyleVar.FramePadding,
                new Vector2(contentFramePadding.X, ((compact ? 42 : 40) * scale - ImGui.GetFontSize()) * .5f));
            DrawFrenSelector(config);
        }

        if (!eureka)
            return;

        ImGui.SetCursorScreenPos(position + new Vector2(21, compact ? 182 : 186) * scale);
        ImGui.Separator();
        ImGui.SetCursorScreenPos(position + new Vector2(25, compact ? 198 : 206) * scale);
        UiGui.TextColored(MaterialTheme.Current.Colors.Secondary, "MAGIA");

        using var actionFont = UiText.Font(UiFontRole.Action);
        using var buttonSpacing = new MaterialStyleScope();
        buttonSpacing.Style(ImGuiStyleVar.ItemSpacing, new Vector2(12 * scale, ImGui.GetStyle().ItemSpacing.Y));
        ImGui.SetCursorScreenPos(position + new Vector2(21, compact ? 224 : 234) * scale);
        var size = new Vector2((ImGui.GetContentRegionAvail().X - ImGui.GetStyle().ItemSpacing.X * 2) / 3,
            UiHelpers.Scale(compact ? 42 : 44));
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
        if (UiGui.Button("Defense", size))
            GameHelpers.SendChatCommand("/magiaauto defense", "Fren Rider mini");

        ContinueButton("Off", size.X);
        if (UiGui.Button("Off", size))
            GameHelpers.SendChatCommand("/magiaauto off", "Fren Rider mini");
    }

    private static void ContinueButton(string label, float minimumWidth)
    {
        var width = Math.Max(minimumWidth, MaterialText.Measure(UiText.T(label)).X + ImGui.GetStyle().FramePadding.X * 2);
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + width <= right) ImGui.SameLine();
    }

    private void DrawFrenSelector(CharacterConfig config)
    {
        var fieldStart = ImGui.GetCursorScreenPos().X + Math.Max(UiHelpers.Scale(plugin.Configuration.UiCompact ? 96 : 88), MaterialText.Measure(UiText.T("Fren")).X + ImGui.GetStyle().ItemSpacing.X);
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
