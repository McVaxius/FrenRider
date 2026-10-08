using AethertekUI.Dalamud;
using System.Linq;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Collections.Generic;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly MaterialWindowMotion motion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
    private static readonly string CurrentVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "unknown";
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Fren Rider##MainWindow")
    {
        Size = new Vector2(1140, 920);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Cog, Priority = 0, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleConfigUi(); },
            ShowTooltip = () => UiGui.SetTooltip("Settings"),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.Compress, Priority = -10, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.ToggleMiniUi(); },
            ShowTooltip = () => UiGui.SetTooltip("Fren Rider Mini"),
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
            Icon = FontAwesomeIcon.ToggleOn, Priority = -30, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left && plugin.ConfigManager.GetCurrentAccount() != null) plugin.ConfigManager.SetAllFrenRiderEnabled(true); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("All FR on") + "\n" + UiText.T(plugin.ConfigManager.GetCurrentAccount() == null
                ? "No account loaded." : "Set DEFAULT CONFIG and every local character in the current account, including the active temporary profile.")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.ToggleOff, Priority = -40, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left && plugin.ConfigManager.GetCurrentAccount() != null) plugin.ConfigManager.SetAllFrenRiderEnabled(false); },
            ShowTooltip = () => MaterialText.SetTooltip(UiText.T("All FR off") + "\n" + UiText.T(plugin.ConfigManager.GetCurrentAccount() == null
                ? "No account loaded." : "Set DEFAULT CONFIG and every local character in the current account, including the active temporary profile.")),
        });
        TitleBarButtons.Add(new()
        {
            Icon = FontAwesomeIcon.FeatherAlt, Priority = -50, IconOffset = new(2, 1),
            Click = button => { if (button == ImGuiMouseButton.Left) plugin.OpenChocoboSettings(); },
            ShowTooltip = () => UiGui.SetTooltip("Chocobo settings"),
        });
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,
            UiHelpers.Scale(new Vector2(12, plugin.Configuration.UiCompact ? 7 : 12)));
        var minimumWidth = (UiGui.TitleMinimumWidth(this, "Fren Rider v" + CurrentVersion)
            + ImGui.GetFontSize() + ImGui.GetStyle().ItemInnerSpacing.X) / Math.Max(.01f, MaterialTheme.Metrics.Scale);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(Math.Max(460, minimumWidth), 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        UiGui.ImageTitle(this, "Fren Rider v" + CurrentVersion, plugin.OriginalIcon);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        motion.DrawChrome();
        using var fields = new MaterialStyleScope();
        fields.Style(ImGuiStyleVar.FrameBorderSize, 1);
        fields.Color(ImGuiCol.FrameBg, MaterialTheme.Current.Colors.SurfaceContainer);
        fields.Color(ImGuiCol.Border, MaterialTheme.Current.Colors.OutlineVariant);
        using var controls = MaterialControls.Push(FrenRiderPresentation.Controls(plugin.Configuration.UiCompact ? 32 : 36));
        var config = plugin.ConfigManager.GetActiveConfig();

        var headerStart = ImGui.GetCursorScreenPos();
        DrawTopBar(config);
        var actionsY = Math.Max(headerStart.Y + UiHelpers.Scale(FrenRiderPresentation.HeaderHeight(plugin.Configuration.UiCompact)),
            ImGui.GetCursorScreenPos().Y);
        ImGui.Separator();
        ImGui.SetCursorScreenPos(new Vector2(headerStart.X, actionsY));
        DrawAccountControls();
        ImGui.SetCursorScreenPos(new Vector2(headerStart.X,
            ImGui.GetItemRectMax().Y + UiHelpers.Scale(plugin.Configuration.UiCompact ? 13 : 14)));
        if (ImGui.BeginChild("##FrenRiderOperatorScroll",
                new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X - UiHelpers.Scale(2)), 0), false))
        {
            DrawWarnings();
            Panel("Operator", MaterialIcon.Info, () => DrawOperatorProfile(config));
            Panel("Party", MaterialIcon.Table, DrawPartySummary);
            Panel("Automation", MaterialIcon.Settings, DrawAutomationStack);
            Panel("Duty / ADS / Exit", MaterialIcon.Return, () => DrawDutyPanel(config));
            ImGui.Indent(UiHelpers.Scale(2));
            try { DrawDebugDetails(config); }
            finally { ImGui.Unindent(UiHelpers.Scale(2)); }
            ImGui.Dummy(UiHelpers.Scale(new Vector2(0, 8)));
        }
        ImGui.EndChild();
    }

    private void Panel(string title, MaterialIcon icon, Action draw)
    {
        var c = MaterialTheme.Current.Colors;
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padding = UiHelpers.Scale(12);
        var topPadding = UiHelpers.Scale(plugin.Configuration.UiCompact ? 10 : 12);
        var bottomPadding = UiHelpers.Scale(title switch
        {
            "Operator" => plugin.Configuration.UiCompact ? 5 : 11,
            "Party" => plugin.Configuration.UiCompact ? 3 : 9,
            "Automation" => plugin.Configuration.UiCompact ? 4 : 10,
            _ => plugin.Configuration.UiCompact ? 11 : 14,
        });
        var originalIdRoot = ImGui.GetID("");
        using var spacing = new MaterialStyleScope();
        spacing.Style(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, UiHelpers.Scale(plugin.Configuration.UiCompact ? 2 : 4)));
        spacing.Style(ImGuiStyleVar.CellPadding, new Vector2(UiHelpers.Scale(4), UiHelpers.Scale(plugin.Configuration.UiCompact ? 2 : 2.5f)));
        var headerBottom = min.Y;
        dl.ChannelsSplit(2);
        try
        {
            dl.ChannelsSetCurrent(1);
            ImGui.BeginGroup();
            try
            {
                ImGui.Dummy(new Vector2(0, Math.Max(0, topPadding - ImGui.GetStyle().ItemSpacing.Y)));
                ImGui.SetCursorScreenPos(min + new Vector2(padding, topPadding));
                ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
                var visible = ImGui.BeginTable("##FrenRiderPanelBounds" + title, 1,
                    ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings,
                    new Vector2(Math.Max(1, width - padding * 2), 0));
                ImGui.PopStyleVar();
                try
                {
                    if (!visible) return;
                    ImGui.TableSetupColumn("Content", ImGuiTableColumnFlags.WidthFixed, Math.Max(1, width - padding * 2));
                    ImGui.TableNextColumn();
                    ImGuiP.PushOverrideID(originalIdRoot);
                    try
                    {
                    using (UiText.Font(UiFontRole.PluginName))
                    {
                        var p = ImGui.GetCursorScreenPos();
                        if (title == "Operator") FrenRiderPresentation.Person(p, UiHelpers.Scale(24), c.OnSurface);
                        else if (title == "Party") FrenRiderPresentation.People(p, UiHelpers.Scale(24), c.OnSurface);
                        else if (title == "Duty / ADS / Exit") FrenRiderPresentation.Flag(p, UiHelpers.Scale(24), c.OnSurface);
                        else MaterialIcons.Draw(icon, p, UiHelpers.Scale(24), c.OnSurface);
                        ImGui.Dummy(UiHelpers.Scale(new Vector2(28, 24)));
                        ImGui.SameLine();
                        UiGui.TextUnformatted(title);
                    }
                    headerBottom = ImGui.GetCursorScreenPos().Y;
                    ImGui.Separator();
                    var inset = UiHelpers.Scale(title == "Automation"
                        ? plugin.Configuration.UiCompact ? 15 : 20
                        : plugin.Configuration.UiCompact ? 1 : 6);
                    ImGui.Indent(inset);
                    try { draw(); }
                    finally { ImGui.Unindent(inset); }
                    }
                    finally { ImGui.PopID(); }
                }
                finally { if (visible) ImGui.EndTable(); }
                ImGui.SetCursorScreenPos(new Vector2(min.X, ImGui.GetCursorScreenPos().Y));
                ImGui.Dummy(new Vector2(width, Math.Max(0, bottomPadding - ImGui.GetStyle().ItemSpacing.Y)));
            }
            finally { ImGui.EndGroup(); }
            var max = new Vector2(min.X + width, ImGui.GetItemRectMax().Y);
            dl.ChannelsSetCurrent(0);
            MaterialCanvas.Surface(min, max, c.Surface, c.Background, UiHelpers.Scale(4));
            dl.AddRectFilled(min, new Vector2(max.X, headerBottom), MaterialCanvas.Color(c.SurfaceContainerLow), UiHelpers.Scale(4));
            dl.AddRect(min, max, MaterialCanvas.Color(c.OutlineVariant), UiHelpers.Scale(4));
        }
        finally { dl.ChannelsMerge(); }
        ImGui.Dummy(new Vector2(0, UiHelpers.Scale(plugin.Configuration.UiCompact ? 8 : 5)));
    }

    private static void NextGroup(float logicalWidth)
    {
        var right = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + UiHelpers.Scale(logicalWidth) <= right) ImGui.SameLine();
    }

    private static void NextButton(string label)
        => NextGroup((MaterialText.Measure(UiText.T(label)).X + ImGui.GetTextLineHeight()
            + UiHelpers.Scale(8) + ImGui.GetStyle().FramePadding.X * 2) / Math.Max(.01f, MaterialTheme.Metrics.Scale));

    private void DrawTopBar(CharacterConfig config)
    {
        var compact = plugin.Configuration.UiCompact;
        var icon = plugin.OriginalIcon;
        var imageMin = ImGui.GetCursorScreenPos();
        MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(), icon.Handle, icon.Size,
            imageMin, imageMin + UiHelpers.Scale(new Vector2(compact ? 28 : 32)));
        ImGui.Dummy(UiHelpers.Scale(new Vector2(compact ? 30 : 36, compact ? 28 : 32)));
        ImGui.SameLine();
        using (UiText.Font(UiFontRole.Title)) MaterialText.Text("Fren Rider");
        NextGroup(110);
        UiHelpers.StatusPill(config.Enabled ? "Enabled" : "Disabled", config.Enabled ? UiHelpers.Green : UiHelpers.Grey);

        NextGroup(MaterialText.Measure(UiText.T("Run")).X / Math.Max(.01f, MaterialTheme.Metrics.Scale) + 52);
        var enabled = config.Enabled;
        if (UiGui.HeaderToggle("Run", ref enabled))
            plugin.ConfigManager.SetFrenRiderEnabled(enabled);

        NextGroup(MaterialText.Measure(UiText.T("DTR")).X / Math.Max(.01f, MaterialTheme.Metrics.Scale) + 52);
        var dtrEnabled = plugin.Configuration.DtrBarEnabled;
        if (UiGui.HeaderToggle("DTR", ref dtrEnabled))
        {
            plugin.Configuration.DtrBarEnabled = dtrEnabled;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Show Fren Rider status in the server info bar.");

        NextGroup(MaterialText.Measure(UiText.T("Krangle")).X / Math.Max(.01f, MaterialTheme.Metrics.Scale) + 52);
        var krangleEnabled = plugin.Configuration.KrangleEnabled;
        if (UiGui.HeaderToggle("Krangle", ref krangleEnabled))
        {
            plugin.Configuration.KrangleEnabled = krangleEnabled;
            plugin.Configuration.Save();
            KrangleService.ClearCache();
        }
        if (ImGui.IsItemHovered())
            UiGui.SetTooltip("Obfuscate character names in FrenRider UI.");

        NextGroup((ImGui.GetFrameHeight() + MaterialText.Measure(UiText.T("Transparency")).X + ImGui.GetStyle().ItemInnerSpacing.X)
            / Math.Max(.01f, MaterialTheme.Metrics.Scale));
        plugin.DrawTransparency();

        NextButton("Settings");
        if (UiGui.Button("Settings", icon: MaterialIcon.Settings))
            plugin.ToggleConfigUi();

        NextButton("Ko-fi");
        if (UiGui.Button("Ko-fi", icon: MaterialIcon.Heart))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }

        NextButton("Chocobo");
        if (UiGui.Button("Chocobo", icon: MaterialIcon.Egg))
            plugin.OpenChocoboSettings();

        if (plugin.Configuration.UiLanguageVisibleOnMainWindow)
        {
            var languageName = "English";
            foreach (var language in UiText.Languages)
                if (language.Code == UiText.Current.Language) languageName = language.Name;
            var appearanceMetrics = FrenRiderPresentation.Controls(compact ? 28 : 32, 18);
            var languageWidth = Math.Max(UiHelpers.Scale(140), MathF.Ceiling(MaterialText.Measure(languageName).X
                + appearanceMetrics.Height + appearanceMetrics.Gap * 3 + Math.Min(appearanceMetrics.IconSize, appearanceMetrics.Height)));
            NextGroup(languageWidth / Math.Max(.01f, MaterialTheme.Metrics.Scale));
            plugin.DrawLanguageSelector();
        }
        if (plugin.Configuration.UiCompactVisibleOnMainWindow)
        {
            using (var compactStyle = new MaterialStyleScope())
            {
                compactStyle.Style(ImGuiStyleVar.FramePadding, new Vector2(ImGui.GetStyle().FramePadding.X, 0));
                NextGroup((ImGui.GetFrameHeight() + MaterialText.Measure("C").X + ImGui.GetStyle().ItemInnerSpacing.X)
                    / Math.Max(.01f, MaterialTheme.Metrics.Scale));
                plugin.DrawCompactSelector();
            }
        }
        NextGroup(Math.Max(ImGui.GetFrameHeight(), MaterialControls.Metrics.Height) / Math.Max(.01f, MaterialTheme.Metrics.Scale));
        if (UiGui.IconButton("Close", MaterialIcon.Close)) IsOpen = false;
    }

    private void DrawAccountControls()
    {
        using var actionFont = UiText.Font(UiFontRole.Action);
        using var buttonSpacing = new MaterialStyleScope();
        buttonSpacing.Style(ImGuiStyleVar.ItemSpacing,
            new Vector2(UiHelpers.Scale(plugin.Configuration.UiCompact ? 14 : 12), ImGui.GetStyle().ItemSpacing.Y));
        var available = ImGui.GetContentRegionAvail().X;
        var minimum = Math.Max(MaterialText.Measure(UiText.T("All FR off")).X, MaterialText.Measure(UiText.T("All FR on")).X)
            + ImGui.GetTextLineHeight() + UiHelpers.Scale(8) + ImGui.GetStyle().FramePadding.X * 2;
        var sameRow = minimum * 2 + ImGui.GetStyle().ItemSpacing.X <= available;
        var buttonSize = new Vector2(
            sameRow ? (available - ImGui.GetStyle().ItemSpacing.X) / 2f : available,
            MaterialControlMetrics.Measure(MaterialTheme.Metrics, ImGui.GetTextLineHeight(), MaterialControlContext.Toolbar).Height);

        ImGui.BeginDisabled(plugin.ConfigManager.GetCurrentAccount() == null);
        DrawAllFrenRiderButton(false, buttonSize);
        if (sameRow) ImGui.SameLine();
        DrawAllFrenRiderButton(true, buttonSize);
        ImGui.EndDisabled();
    }

    private void DrawAllFrenRiderButton(bool enabled, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, enabled
            ? FrenRiderPresentation.Rgb(0x54955B)
            : FrenRiderPresentation.Rgb(0xD44D60));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled
            ? new Vector4(0.15f, 0.6f, 0.2f, 1f)
            : new Vector4(0.8f, 0.15f, 0.15f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled
            ? new Vector4(0.1f, 0.35f, 0.1f, 1f)
            : new Vector4(0.45f, 0.05f, 0.05f, 1f));
        if (UiGui.Button(enabled ? "All FR on" : "All FR off", size, MaterialIcon.Power,
            FrenRiderPresentation.Rgb(enabled ? 0x367940u : 0xB33144u)))
            plugin.ConfigManager.SetAllFrenRiderEnabled(enabled);
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiGui.SetTooltip("Set DEFAULT CONFIG and every local character in the current account, including the active temporary profile.");
    }

    private void DrawWarnings()
    {
        if (!Plugin.ClientState.IsLoggedIn)
            UiHelpers.WarningStrip("Not logged in. FrenRider waits until a character is loaded.");

        if (plugin.AutoDutyDetectionService.ShouldShowMainWindowWarning())
            UiHelpers.WarningStrip("AutoDuty detected while FrenRider is enabled.");
    }

    private void DrawOperatorProfile(CharacterConfig config)
    {
        var compact = plugin.Configuration.UiCompact;
        using var fields = MaterialControls.Push(FrenRiderPresentation.Controls(36));
        using var columnStyle = new MaterialStyleScope();
        columnStyle.Style(ImGuiStyleVar.FramePadding,
            new Vector2(UiHelpers.Scale(compact ? 14 : 16), ImGui.GetStyle().FramePadding.Y));
        columnStyle.Style(ImGuiStyleVar.CellPadding,
            UiHelpers.Scale(new Vector2(compact ? 7 : 10, compact ? 2 : 3)));
        var fren = plugin.FrenTracker.Fren;
        var account = plugin.ConfigManager.GetCurrentAccount();
        var visible = fren?.IsFound == true && fren.IsVisible;
        var trackedName = fren is null ? "" : Disp(fren.Name)
            + (string.IsNullOrWhiteSpace(fren.ClassJobName) ? "" : $" [{fren.ClassJobName}]");
        var tracking = fren is null ? UiText.T("Inactive") : fren.IsFound && fren.IsVisible
            ? UiText.F("{0} / {1} / {2:F1}y", trackedName, UiText.T(fren.InParty ? "in party" : "not in party"), fren.Distance)
            : fren.IsFound ? UiText.F("{0} in party but not visible", Disp(fren.Name)) : UiText.T("Fren not found");
        var cells = new (string Label, string Value, Vector4? Color)[]
        {
            ("Character", GetLocalCharacterText(), null),
            ("Account", account is null ? UiText.T("No account loaded") : Disp(account.AccountAlias), null),
            ("Fren", string.IsNullOrWhiteSpace(config.FrenName) ? UiText.T("No fren configured") : Disp(config.FrenName), null),
            ("Tracking", tracking, fren is null ? UiHelpers.Grey : visible ? UiHelpers.Green : fren.IsFound ? UiHelpers.Yellow : UiHelpers.Red),
            ("Position", visible ? UiText.F("{0:F0} / {1:F0} / {2:F0}", fren!.Position.X, fren.Position.Y, fren.Position.Z) : "—", null),
        };
        var fieldWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - UiHelpers.Scale(compact ? 2 : 3));
        var columns = fieldWidth >= UiHelpers.Scale(900) ? 5 : fieldWidth >= UiHelpers.Scale(520) ? 3 : 1;
        if (ImGui.BeginTable("##OperatorFields", columns, ImGuiTableFlags.SizingStretchProp, new Vector2(fieldWidth, 0)))
        {
            for (var column = 0; column < columns; column++)
            {
                var weight = columns != 5 ? 1 : compact
                    ? column switch { 0 => 2, 1 or 2 => 1.95f, 3 => 2.5f, _ => 1.48f }
                    : column switch { 0 => 2.1f, 1 or 2 => 2.05f, 3 => 2.35f, _ => 1.45f };
                ImGui.TableSetupColumn("##operator" + column, ImGuiTableColumnFlags.WidthStretch,
                    weight);
            }
            foreach (var cell in cells)
            {
                ImGui.TableNextColumn();
                UiGui.TextUnformatted(cell.Label);
                UiHelpers.ReadOnlyField(cell.Value, cell.Color, localize: false);
            }
            ImGui.EndTable();
        }
    }

    private void DrawFrenStatus(CharacterConfig config)
    {
        var tracker = plugin.FrenTracker;
        var fren = tracker.Fren;
        if (string.IsNullOrWhiteSpace(config.FrenName))
            return;

        if (fren == null)
        {
            UiHelpers.AlignedRow("Tracking", "Inactive", UiHelpers.Grey);
            return;
        }

        if (fren.IsFound && fren.IsVisible)
        {
            var jobInfo = string.IsNullOrWhiteSpace(fren.ClassJobName) ? "" : $" [{fren.ClassJobName}]";
            var partyInfo = fren.InParty ? "in party" : "not in party";
            UiHelpers.AlignedRow("Tracking", $"{Disp(fren.Name)}{jobInfo}, {partyInfo}, {fren.Distance:F1}y", UiHelpers.Green);
            UiHelpers.AlignedRow("Position", $"{fren.Position.X:F0}, {fren.Position.Y:F0}, {fren.Position.Z:F0}");
            return;
        }

        if (fren.IsFound)
            UiHelpers.AlignedRow("Tracking", $"{Disp(fren.Name)} in party but not visible", UiHelpers.Yellow);
        else
            UiHelpers.AlignedRow("Tracking", "Fren not found", UiHelpers.Red);
    }

    private void DrawPartySummary()
    {
        var compact = plugin.Configuration.UiCompact;
        using var tableStyle = new MaterialStyleScope();
        tableStyle.Style(ImGuiStyleVar.CellPadding,
            new Vector2(UiHelpers.Scale(compact ? 8 : 7), ImGui.GetStyle().CellPadding.Y));
        tableStyle.Color(ImGuiCol.TableHeaderBg, Vector4.Zero);
        tableStyle.Color(ImGuiCol.TableRowBg, Vector4.Zero);
        tableStyle.Color(ImGuiCol.TableRowBgAlt, Vector4.Zero);
        tableStyle.Color(ImGuiCol.ChildBg, Vector4.Zero);
        var available = Math.Max(1, ImGui.GetContentRegionAvail().X - UiHelpers.Scale(compact ? 3 : 6));
        var statusMinimum = Math.Max(MaterialText.Measure(UiText.T("Mounted")).X,
            Math.Max(MaterialText.Measure(UiText.T("On foot")).X, MaterialText.Measure(UiText.T("Not visible")).X))
            + UiHelpers.Scale(12) + ImGui.GetStyle().ItemSpacing.X + ImGui.GetStyle().CellPadding.X * 2;
        var jobTextWidth = MaterialText.Measure(UiText.T("Job")).X;
        foreach (var member in plugin.FrenTracker.Party)
            jobTextWidth = Math.Max(jobTextWidth, MaterialText.Measure(member.ClassJobName).X);
        var jobMinimum = UiHelpers.Scale(24) + ImGui.GetStyle().ItemSpacing.X + jobTextWidth + ImGui.GetStyle().CellPadding.X * 2;
        float distanceMinimum;
        using (UiText.Font(UiFontRole.BodyStrong))
            distanceMinimum = Math.Max(UiHelpers.Scale(compact ? 136 : 130), MaterialText.Measure(UiText.T("Distance")).X + ImGui.GetStyle().CellPadding.X * 2);
        var indexWidth = UiHelpers.Scale(compact ? 39 : 43);
        var nameWeight = compact ? 1.67f : 1.77f;
        var statusWeight = compact ? 1.135f : 1.16f;
        var minimumWidth = Math.Max(UiHelpers.Scale(640), indexWidth + distanceMinimum + ImGui.GetStyle().CellPadding.X * 10
            + MathF.Ceiling(Math.Max(jobMinimum, statusMinimum / statusWeight) * (nameWeight + 1 + statusWeight)));
        var rowHeight = UiHelpers.Scale(plugin.Configuration.UiCompact ? 32 : 35);
        var height = MathF.Ceiling(ImGui.GetTextLineHeight() + ImGui.GetStyle().CellPadding.Y * 2
            + Math.Max(4, plugin.FrenTracker.Party.Count) * rowHeight)
            + (available < minimumWidth ? ImGui.GetStyle().ScrollbarSize : 0);
        if (!ImGui.BeginTable("##FrenRiderParty", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.PadOuterX,
                new Vector2(available, height), Math.Max(available, minimumWidth))) return;
        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, indexWidth);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, nameWeight);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch, statusWeight);
        ImGui.TableSetupColumn("Distance", ImGuiTableColumnFlags.WidthFixed, distanceMinimum);
        ImGui.TableSetupScrollFreeze(0, 1);
        using (UiText.Font(UiFontRole.BodyStrong))
        {
            var headerHeight = new[] { "#", "Name", "Job", "Status", "Distance" }
                .Select(label => MaterialText.Measure(UiText.T(label)).Y).Max() + ImGui.GetStyle().CellPadding.Y * 2;
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
            for (var column = 0; column < ImGui.TableGetColumnCount(); column++)
            {
                if (!ImGui.TableSetColumnIndex(column)) continue;
                var original = ImGui.TableGetColumnName(column);
                var position = ImGui.GetCursorScreenPos();
                var captionWidth = ImGui.GetContentRegionAvail().X;
                var foreground = ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.Zero);
                ImGui.TableHeader(original);
                ImGui.PopStyleColor();
                foreground.W *= ImGui.GetStyle().Alpha;
                // Native outer padding keeps the font's left bearing visible.
                var translated = UiText.T(original);
                MaterialText.AddText(ImGui.GetWindowDrawList(),position, ImGui.ColorConvertFloat4ToU32(foreground), translated);
                if (translated != original && MaterialText.Measure(translated).X > captionWidth - UiHelpers.Scale(16) && ImGui.IsItemHovered())
                    MaterialText.SetTooltip(translated);
            }
        }
        foreach (var member in plugin.FrenTracker.Party)
        {
            ImGui.PushID(member.Name + "@" + member.WorldName);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn(); MaterialText.Text(UiText.F("{0}", member.PartyIndex + 1));
            ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(Disp(member.Name), localize: false);
            ImGui.TableNextColumn();
            if (!string.IsNullOrWhiteSpace(member.ClassJobName))
            {
                FrenRiderPresentation.Job(ImGui.GetCursorScreenPos(), UiHelpers.Scale(24), member.ClassJobName, member.Role);
                ImGui.Dummy(UiHelpers.Scale(new Vector2(24)));
                ImGui.SameLine();
            }
            UiHelpers.SafeWrappedText(string.IsNullOrWhiteSpace(member.ClassJobName) ? "—" : member.ClassJobName, localize: false);
            ImGui.TableNextColumn();
            var statusColor = member.IsVisible ? UiHelpers.Green : UiHelpers.Red;
            var dot = ImGui.GetCursorScreenPos() + new Vector2(UiHelpers.Scale(6), ImGui.GetTextLineHeight() * .5f);
            ImGui.GetWindowDrawList().AddCircleFilled(dot, UiHelpers.Scale(6), MaterialCanvas.Color(statusColor), 24);
            ImGui.Dummy(new Vector2(UiHelpers.Scale(12), ImGui.GetTextLineHeight()));
            ImGui.SameLine();
            UiGui.TextColored(statusColor, member.IsVisible ? member.IsMounted ? "Mounted" : "On foot" : "Not visible");
            ImGui.TableNextColumn(); MaterialText.Text(member.IsVisible ? UiText.F("{0:F0}y", member.DistanceToPlayer) : "—");
            ImGui.PopID();
        }
        if (plugin.FrenTracker.Party.Count == 0)
        {
            ImGui.TableNextRow(); ImGui.TableNextColumn(); MaterialText.Text("—");
            ImGui.TableNextColumn(); UiGui.TextDisabled("Not in a party");
        }
        for (var slot = Math.Max(1, plugin.FrenTracker.Party.Count); slot < 4; slot++)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
            ImGui.TableNextColumn(); MaterialText.Text(UiText.F("{0}", slot + 1));
            for (var column = 1; column < 5; column++) { ImGui.TableNextColumn(); MaterialText.Text("—"); }
        }
        ImGui.EndTable();
    }

    private void DrawAutomationStack()
    {
        var compact = plugin.Configuration.UiCompact;
        using var fields = MaterialControls.Push(FrenRiderPresentation.Controls(compact ? 30 : 36));
        using var rowSpacing = new MaterialStyleScope();
        rowSpacing.Style(ImGuiStyleVar.FramePadding,
            new Vector2(UiHelpers.Scale(compact ? 14 : 16), ImGui.GetStyle().FramePadding.Y));
        rowSpacing.Style(ImGuiStyleVar.CellPadding, UiHelpers.Scale(new Vector2(compact ? 8 : 12, 3)));
        var follow = plugin.FollowService;
        var mount = plugin.MountService;
        var combat = plugin.CombatService;
        var auto = plugin.AutomationService;
        var rows = new List<(string Label, string Value, Vector4? Color)>
        {
            ("Follow", UiText.StateDetail(follow.State.ToString(), follow.StateDetail), GetFollowColor(follow.State)),
            ("Idle", auto.IsIdle ? string.IsNullOrWhiteSpace(auto.LastIdleAction) ? "Idle" : UiText.F("Idle; last {0}", auto.LastIdleAction) : "Active checks running", auto.IsIdle ? UiHelpers.Blue : UiHelpers.Green),
            ("Mount", UiText.StateDetail(mount.State.ToString(), mount.StateDetail), GetMountColor(mount.State)),
            ("Fren teleport", UiText.TeleportStatus(plugin.FrenTeleportService.StatusText), GetTeleportColor(plugin.FrenTeleportService.State)),
            ("Combat", UiText.StateDetail(combat.State.ToString(), combat.StateDetail), GetCombatColor(combat.State)),
            ("Phoenix Down", plugin.PhoenixDownRecoveryService.StatusText, UiHelpers.Yellow),
            ("Cleanup", plugin.ExternalAutomationCleanupService.StatusText, GetCleanupColor(plugin.ExternalAutomationCleanupService.State)),
            ("Respawn", plugin.RespawnService.StatusText, GetRespawnColor(plugin.RespawnService.State)),
            ("Food", string.IsNullOrWhiteSpace(auto.FoodStatus) ? "Idle" : auto.FoodStatus, auto.FoodStatus.StartsWith("Well Fed", StringComparison.OrdinalIgnoreCase) ? UiHelpers.Green : UiHelpers.Yellow),
            ("Repair", string.IsNullOrWhiteSpace(auto.RepairStatus) ? "Idle" : auto.RepairStatus, GetRepairColor(auto.RepairStatus)),
        };
        if (!string.IsNullOrWhiteSpace(auto.AutoDesynthStatus)) rows.Add(("Desynth", auto.AutoDesynthStatus, GetDesynthColor(auto.AutoDesynthStatus)));
        if (!string.IsNullOrWhiteSpace(auto.CompanionStatus)) rows.Add(("Companion", auto.CompanionStatus, UiHelpers.Green));
        else
        {
            var buddyTime = GameHelpers.GetBuddyTimeRemaining();
            var gysahlCount = GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId);
            rows.Add(("Companion", buddyTime > 0
                ? UiText.F("Active, {0}m {1:D2}s remaining", (int)(buddyTime / 60), (int)(buddyTime % 60))
                : gysahlCount > 0 ? UiText.F("Inactive, {0} Gysahl Greens", gysahlCount) : UiText.T("Inactive, no Gysahl Greens"),
                buddyTime > 0 ? UiHelpers.Green : UiHelpers.Grey));
        }
        if (plugin.FormationService.IsActive) rows.Add(("Formation", UiText.F("Slot {0}", plugin.FormationService.AssignedSlot), UiHelpers.Blue));
        var firstLabelWidth = UiHelpers.Scale(compact ? 135 : 140);
        var secondLabelWidth = UiHelpers.Scale(compact ? 152 : 150);
        for (var row = 0; row < rows.Count; row++)
        {
            var labelWidth = MathF.Ceiling(MaterialText.Measure(UiText.T(rows[row].Label)).X + UiHelpers.Scale(2));
            if (row % 2 == 0) firstLabelWidth = Math.Max(firstLabelWidth, labelWidth);
            else secondLabelWidth = Math.Max(secondLabelWidth, labelWidth);
        }
        var groupGap = UiHelpers.Scale(compact ? 27 : 31);
        var fieldWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - UiHelpers.Scale(compact ? 6 : 15));
        var columns = fieldWidth >= Math.Max(UiHelpers.Scale(760), firstLabelWidth + secondLabelWidth + groupGap
            + ImGui.GetStyle().CellPadding.X * 6 + UiHelpers.Scale(300)) ? 4 : 2;
        if (!ImGui.BeginTable("##AutomationFields", columns, ImGuiTableFlags.SizingStretchProp, new Vector2(fieldWidth, 0))) return;
        for (var i = 0; i < columns; i++)
            ImGui.TableSetupColumn("##automation" + i, i % 2 == 0 ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch,
                i % 2 == 0 ? columns == 2 ? Math.Max(firstLabelWidth, secondLabelWidth) : i == 0 ? firstLabelWidth : secondLabelWidth + groupGap
                    : i == 1 && columns == 4 ? compact ? .965f : .952f : 1);
        foreach (var row in rows)
        {
            ImGui.TableNextColumn();
            if (columns == 4 && ImGui.TableGetColumnIndex() == 2)
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + groupGap);
            UiHelpers.SafeWrappedText(row.Label);
            ImGui.TableNextColumn(); UiHelpers.ReadOnlyField(row.Value, row.Color);
        }
        ImGui.EndTable();
    }

    private void DrawCompanionStatus(AutomationService auto)
    {
        if (!string.IsNullOrWhiteSpace(auto.CompanionStatus))
        {
            UiHelpers.AlignedRow("Companion", auto.CompanionStatus, UiHelpers.Green);
            return;
        }

        var buddyTime = GameHelpers.GetBuddyTimeRemaining();
        if (buddyTime > 0)
        {
            var minutes = (int)(buddyTime / 60);
            var seconds = (int)(buddyTime % 60);
            UiHelpers.AlignedRow("Companion", $"Active, {minutes}m {seconds:D2}s remaining", UiHelpers.Green);
            return;
        }

        var gysahlCount = GameHelpers.GetInventoryItemCount(GameHelpers.GysahlGreensItemId);
        UiHelpers.AlignedRow("Companion", gysahlCount > 0 ? $"Inactive, {gysahlCount} Gysahl Greens" : "Inactive, no Gysahl Greens", UiHelpers.Grey);
    }

    private static Vector4? GetRepairColor(string status)
    {
        if (status.StartsWith("Sent", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("Repair complete", StringComparison.OrdinalIgnoreCase) ||
            status.StartsWith("No equipped gear below", StringComparison.OrdinalIgnoreCase))
        {
            return UiHelpers.Green;
        }

        return UiHelpers.Yellow;
    }

    private static Vector4? GetDesynthColor(string status)
        => status.StartsWith("Completed", StringComparison.OrdinalIgnoreCase)
            ? UiHelpers.Green
            : status.StartsWith("Failed", StringComparison.OrdinalIgnoreCase)
                ? UiHelpers.Red
                : UiHelpers.Yellow;

    private void DrawDutyPanel(CharacterConfig config)
    {
        var compact = plugin.Configuration.UiCompact;
        using var fields = new MaterialStyleScope();
        fields.Style(ImGuiStyleVar.FramePadding,
            new Vector2(UiHelpers.Scale(compact ? 14 : 16), ImGui.GetStyle().FramePadding.Y));
        var zone = plugin.ZoneService;
        var zoneExtra = "";
        if (zone.InFate) zoneExtra += UiText.F(", FATE {0}", zone.CurrentFateId);
        if (zone.IsIndoors) zoneExtra += UiText.T(", indoors");
        var zoneText = UiText.F("{0} (territory {1}{2})", UiText.T(zone.CurrentZone.ToString()), zone.TerritoryId, zoneExtra);
        var ads = plugin.AdsIntegrationService;
        var adsColor = ads.IsControllingDuty
            ? UiHelpers.Green
            : ads.IsHandoffPending
                ? UiHelpers.Yellow
                : ads.AdsLoaded
                    ? UiHelpers.Blue
                    : UiHelpers.Grey;
        var exitMethod = config.UseAdsLeaveAfterAdsDuty
            ? UiText.F("ADS Exit Method ({0}s)", config.ExitAfterDutySeconds)
            : config.ExitAfterDutyEnds
                ? UiText.F("FrenRider: leave after {0}s", config.ExitAfterDutySeconds)
                : config.LeaveWhenAllLeft
                    ? UiText.T("FrenRider: leave when party leaves")
                    : UiText.T("No automatic exit");
        var labels = new[] { "Zone", "ADS", "Exit method" };
        var labelWidths = compact ? new[] { 76f, 94f, 141f } : new[] { 73f, 95f, 150f };
        var labelInsets = compact ? new[] { 8f, 35f, 31f } : new[] { 7f, 34f, 35f };
        var totalLabelWidth = 0f;
        var maximumLabelWidth = 0f;
        for (var i = 0; i < labels.Length; i++)
        {
            labelWidths[i] = MathF.Ceiling(Math.Max(UiHelpers.Scale(labelWidths[i]), MaterialText.Measure(UiText.T(labels[i])).X
                + UiHelpers.Scale(labelInsets[i] + 2)));
            totalLabelWidth += labelWidths[i];
            maximumLabelWidth = Math.Max(maximumLabelWidth, labelWidths[i]);
        }
        var fieldWidth = Math.Max(1, ImGui.GetContentRegionAvail().X - UiHelpers.Scale(compact ? 6 : 10));
        var columns = fieldWidth >= Math.Max(UiHelpers.Scale(760), totalLabelWidth + ImGui.GetStyle().CellPadding.X * 10 + UiHelpers.Scale(420)) ? 6 : 2;
        if (!ImGui.BeginTable("##DutyFields", columns, ImGuiTableFlags.SizingStretchProp, new Vector2(fieldWidth, 0))) return;
        for (var i = 0; i < columns; i++)
            ImGui.TableSetupColumn("##duty" + i, i % 2 == 0 ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch,
                i % 2 == 0 ? columns == 6 ? labelWidths[i / 2] : maximumLabelWidth
                    : columns == 2 || i == 3 ? 1 : i == 1 ? compact ? 1.065f : 1.216f : compact ? .93f : .907f);
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + UiHelpers.Scale(labelInsets[0]));
        UiHelpers.SafeWrappedText("Zone");
        ImGui.TableNextColumn(); UiHelpers.ReadOnlyField(zoneText);
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + UiHelpers.Scale(labelInsets[columns == 6 ? 1 : 0]));
        UiHelpers.SafeWrappedText("ADS");
        ImGui.TableNextColumn(); UiHelpers.ReadOnlyField(ads.StatusText, adsColor);
        ImGui.TableNextColumn();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + UiHelpers.Scale(labelInsets[columns == 6 ? 2 : 0]));
        UiHelpers.SafeWrappedText("Exit method");
        ImGui.TableNextColumn(); UiHelpers.ReadOnlyField(exitMethod, config.UseAdsLeaveAfterAdsDuty ? UiHelpers.Blue : UiHelpers.Grey);
        ImGui.EndTable();
    }

    private void DrawDebugDetails(CharacterConfig config)
    {
        using (var headerStyle = new MaterialStyleScope())
        {
            headerStyle.Color(ImGuiCol.Header, Vector4.Zero);
            if (!UiGui.CollapsingHeader("Compact debug")) return;
        }

        var party = plugin.FrenTracker.Party;
        UiHelpers.AlignedRow("Members", UiText.F("{0} total, {1} visible, {2} mounted", party.Count,
            party.FindAll(member => member.IsVisible).Count, party.FindAll(member => member.IsMounted).Count));
        var composition = plugin.FrenTracker.GetPartyComposition();
        if (composition.Count > 0)
        {
            var jobs = new List<string>();
            foreach (var job in composition) jobs.Add(UiText.F("{0} {1}", job.Value, UiText.T(job.Key)));
            UiHelpers.AlignedRow("Jobs", string.Join(", ", jobs));
        }
        UiHelpers.AlignedRow("Config", $"Run={config.Enabled}, FlyYouFools={config.FlyYouFools}, FarChase={config.MountUpToChaseFren}, FollowMode={config.ClingType}/{config.ClingTypeDuty}");
        UiHelpers.AlignedRow("Distances", $"Cling={config.Cling:F1}, Max={config.MaxBistance:F0}, ForayMax={config.MaxBistanceForay:F0}");
        UiHelpers.AlignedRow("ADS flags", $"Loaded={plugin.AdsIntegrationService.AdsLoaded}, Pending={plugin.AdsIntegrationService.IsHandoffPending}, RuntimeOwned={plugin.AdsIntegrationService.IsControllingDuty}, Readable={plugin.AdsIntegrationService.RuntimeOwnershipReadable}, Source={plugin.AdsIntegrationService.RuntimeOwnershipSource}, ExitTakeover={plugin.AdsIntegrationService.ExitTakeoverActive}");
        UiHelpers.AlignedRow("ADS authority", $"Source={plugin.AdsIntegrationService.RuntimeOwnershipSource}, Readable={plugin.AdsIntegrationService.RuntimeOwnershipReadable}, ExitTakeover={plugin.AdsIntegrationService.ExitTakeoverActive}");
        UiHelpers.AlignedRow("Exit", string.IsNullOrWhiteSpace(plugin.ExitBehaviourService.StateDetail) ? "Waiting for duty-end rule" : plugin.ExitBehaviourService.StateDetail, UiHelpers.Yellow);
        if (plugin.DutyInteractService.IsActive)
            UiHelpers.AlignedRow("Duty interact", plugin.DutyInteractService.StateDetail, UiHelpers.Yellow);
    }

    private string GetLocalCharacterText()
    {
        if (!Plugin.ClientState.IsLoggedIn)
            return UiText.T("Not logged in");

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return UiText.T("Logged in, local player unavailable");

        var charName = player.Name.ToString();
        var worldName = player.HomeWorld.Value.Name.ToString();
        return Disp($"{charName}@{worldName}");
    }

    private static Vector4 GetFollowColor(FollowState state)
        => state switch
        {
            FollowState.Following => UiHelpers.Blue,
            FollowState.InRange => UiHelpers.Green,
            FollowState.TooFar => UiHelpers.Orange,
            FollowState.InCombat => UiHelpers.Red,
            _ => UiHelpers.Grey,
        };

    private static Vector4 GetMountColor(MountState state)
        => state switch
        {
            MountState.Mounted => UiHelpers.Green,
            MountState.Mounting or MountState.WaitingToMount => UiHelpers.Yellow,
            MountState.Dismounting => UiHelpers.Orange,
            _ => UiHelpers.Grey,
        };

    private static Vector4 GetCombatColor(CombatState state)
        => state switch
        {
            CombatState.InCombat => UiHelpers.Red,
            CombatState.EnteringCombat => UiHelpers.Orange,
            CombatState.LeavingCombat => UiHelpers.Grey,
            _ => UiHelpers.Grey,
        };

    private static Vector4 GetCleanupColor(ExternalAutomationCleanupState state)
        => state switch
        {
            ExternalAutomationCleanupState.Restored or ExternalAutomationCleanupState.ForceOff => UiHelpers.Green,
            ExternalAutomationCleanupState.Captured => UiHelpers.Blue,
            ExternalAutomationCleanupState.Partial => UiHelpers.Yellow,
            ExternalAutomationCleanupState.Failed => UiHelpers.Red,
            _ => UiHelpers.Grey,
        };

    private static Vector4 GetTeleportColor(FrenTeleportState state)
        => state switch
        {
            FrenTeleportState.Waiting or FrenTeleportState.ReadingParty => UiHelpers.Yellow,
            FrenTeleportState.TeleportIssued => UiHelpers.Green,
            FrenTeleportState.Cooldown => UiHelpers.Orange,
            FrenTeleportState.Blocked => UiHelpers.Red,
            _ => UiHelpers.Grey,
        };

    private static Vector4 GetRespawnColor(RespawnState state)
        => state switch
        {
            RespawnState.Waiting => UiHelpers.Yellow,
            RespawnState.Returning => UiHelpers.Orange,
            RespawnState.Blocked => UiHelpers.Red,
            _ => UiHelpers.Grey,
        };

    private string Disp(string name)
        => plugin.Configuration.KrangleEnabled ? KrangleService.KrangleName(name) : name;
}
