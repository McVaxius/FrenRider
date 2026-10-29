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
    private readonly AethertekUI.Dalamud.MaterialWindowLock movementLock = new();
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
        motion.Prepare(this, reducedMotion: !plugin.Configuration.UiStylingEnabled, roundedCorners: plugin.Configuration.UiStylingEnabled);
        movementLock.Apply(this, plugin.Configuration.UiWindowsLocked);
    }

    public override void PostDraw()
    {
        try
        {
            movementLock.Restore(this);
            motion.Restore(this);
            UiGui.ImageTitleWithStatus(this, "Fren Rider v" + CurrentVersion, plugin.OriginalIcon, plugin.TitlebarStatus());
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
        using var controls = MaterialControls.Push(FrenRiderPresentation.Controls(plugin.Configuration.UiCompact ? 32 : 36));
        var config = plugin.ConfigManager.GetActiveConfig();
        var actionRoot = ImGui.GetID("");
        if (FrenRiderPresentation.IsLcars)
        {
            using (UiText.Font(UiFontRole.Title)) FrenRiderPresentation.LcarsHeader("FREN RIDER");
        }
        DrawTopBar(config);
        ImGui.Separator();
        var footerHeight = ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y * 3 + 1;
        if (FrenRiderPresentation.IsLcars) footerHeight += 8 * MaterialTheme.Metrics.Scale;
        using var scrollStyle = new MaterialStyleScope();
        if (FrenRiderPresentation.Style == FrenRiderUiStyle.Glassmorphism
            || FrenRiderPresentation.BackgroundBlurEnabled(plugin.Configuration))
            scrollStyle.Color(ImGuiCol.ChildBg, Vector4.Zero);
        var visible = ImGui.BeginChild("##FrenRiderOperatorScroll",
            new Vector2(0, -footerHeight), false);
        try
        {
            if (visible)
            {
                DrawWarnings();
                Panel("Activation mode", MaterialIcon.Power, () =>
                {
                    // Run and activation originally belong to the main window's root.
                    ImGuiP.PushOverrideID(actionRoot);
                    try
                    {
                        MaterialSettings.Columns("##RunGroups", UiHelpers.Scale(330), () =>
                        {
                            var enabled = config.Enabled;
                            if (UiGui.HeaderToggle("Run", ref enabled))
                                plugin.ConfigManager.SetFrenRiderEnabled(enabled);
                            NextButton(config.Enabled ? "Enabled" : "Disabled");
                            UiHelpers.StatusPill(config.Enabled ? "Enabled" : "Disabled", config.Enabled ? UiHelpers.Green : UiHelpers.Grey);
                            UiHelpers.AlignedRow("Fren", string.IsNullOrWhiteSpace(config.FrenName)
                                ? UiText.T("No fren configured") : Disp(config.FrenName));
                        }, () =>
                        {
                            UiGui.TextDisabled("Activation mode");
                            if (UiGui.ActivationModeControls(config)) plugin.ConfigManager.SaveCurrentAccount();
                        });
                        if (plugin.IsWaitingForDuty)
                        {
                            UiGui.TextColored(UiHelpers.Yellow, "Combat rotations paused outside duties");
                            if (ImGui.IsItemHovered()) UiGui.SetTooltip("Run is On. Following, repairs and other configured actions continue. Combat rotations wait for a duty.");
                        }
                    }
                    finally { ImGui.PopID(); }
                }, heading: false);
                MaterialSettings.Columns("##OperatorDutyGroups", UiHelpers.Scale(390),
                    () => Panel("Operator", MaterialIcon.Info, () => DrawOperatorProfile(config)),
                    () => Panel("Duty / ADS / Exit", MaterialIcon.Return, () => DrawDutyPanel(config)));
                Panel("Party", MaterialIcon.Table, DrawPartySummary);
                Panel("Automation", MaterialIcon.Settings, () =>
                {
                    DrawAutomationStack();
                    ImGui.Separator();
                    ImGuiP.PushOverrideID(actionRoot);
                    try { UiGui.DrawCompanionPurchases(plugin, config, mini: false); }
                    finally { ImGui.PopID(); }
                });
                ImGui.Indent(UiHelpers.Scale(2));
                try { DrawDebugDetails(config); }
                finally { ImGui.Unindent(UiHelpers.Scale(2)); }
                ImGui.Dummy(UiHelpers.Scale(new Vector2(0, 8)));
            }
        }
        finally { ImGui.EndChild(); }
        if (FrenRiderPresentation.IsLcars) FrenRiderPresentation.LcarsFooter("FrenRider " + CurrentVersion + " · LCARS");
        else
        {
            ImGui.Separator();
            MaterialText.TextDisabled("FrenRider " + CurrentVersion);
        }
    }

    private void Panel(string title, MaterialIcon icon, Action draw, bool heading = true)
    {
        var originalIdRoot = ImGui.GetID("");
        using var spacing = new MaterialStyleScope();
        spacing.Style(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, UiHelpers.Scale(plugin.Configuration.UiCompact ? 3 : 5)));
        spacing.Style(ImGuiStyleVar.CellPadding, UiHelpers.Scale(new Vector2(FrenRiderPresentation.IsLcars ? 20 : 10, plugin.Configuration.UiCompact ? 6 : 8)));
        if (!ImGui.BeginTable("##FrenRiderPanelBounds" + title, 1,
                ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings | ImGuiTableFlags.PadOuterX)) return;
        try
        {
            ImGui.TableNextColumn();
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            ImGuiP.PushOverrideID(originalIdRoot);
            try
            {
                if (FrenRiderPresentation.IsLcars || FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk)
                {
                    using (UiText.Font(UiFontRole.PluginName)) FrenRiderPresentation.LcarsHeading(UiText.Current.Culture.TextInfo.ToUpper(UiText.T(title)), LcarsAccent(title));
                }
                else if (heading)
                {
                    using (UiText.Font(UiFontRole.PluginName)) UiGui.TextUnformatted(title);
                }
                draw();
            }
            finally { ImGui.PopID(); }
            var inset = UiHelpers.Scale(new Vector2(FrenRiderPresentation.IsLcars ? 16 : 8, plugin.Configuration.UiCompact ? 4 : 6));
            var max = new Vector2(min.X + width, ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y);
            ImGuiP.TablePushBackgroundChannel();
            try
            {
                var c = MaterialTheme.Current.Colors;
                var dl = ImGui.GetWindowDrawList();
                if (FrenRiderPresentation.Decorated || FrenRiderPresentation.BackgroundBlurEnabled(plugin.Configuration))
                    FrenRiderPresentation.DrawPanel(min - inset, max + inset, LcarsAccent(title), background: true);
                else
                {
                    dl.AddRectFilled(min - inset, max + inset, MaterialCanvas.Color(c.SurfaceContainerLow), UiHelpers.Scale(6));
                    dl.AddRect(min - inset, max + inset, MaterialCanvas.Color(c.OutlineVariant), UiHelpers.Scale(6));
                }
            }
            finally { ImGuiP.TablePopBackgroundChannel(); }
        }
        finally { ImGui.EndTable(); }
        ImGui.Dummy(new Vector2(0, UiHelpers.Scale(plugin.Configuration.UiCompact ? 3 : 5)));
    }

    private static void NextGroup(float logicalWidth)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + UiHelpers.Scale(logicalWidth) <= right) ImGui.SameLine();
    }

    private static int LcarsAccent(string title) => title switch
    {
        "Operator" => 1, "Duty / ADS / Exit" => 2, "Party" => 0, "Automation" => 1, _ => 0,
    };

    private static void NextButton(string label)
        => NextGroup((MaterialText.Measure(UiText.T(label)).X + ImGui.GetTextLineHeight()
            + UiHelpers.Scale(8) + ImGui.GetStyle().FramePadding.X * 2) / Math.Max(.01f, MaterialTheme.Metrics.Scale));

    private void DrawTopBar(CharacterConfig config)
    {
        var compact = plugin.Configuration.UiCompact;
        var icon = plugin.OriginalIcon;
        var imageMin = ImGui.GetCursorScreenPos();
        var cyberpunk = FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk;
        if (cyberpunk)
        {
            var markerSize = UiHelpers.Scale(new Vector2(10, compact ? 28 : 32));
            var cut = UiHelpers.Scale(6);
            ImGui.GetWindowDrawList().AddQuadFilled(imageMin, imageMin + new Vector2(markerSize.X, cut),
                imageMin + markerSize, imageMin + new Vector2(0, markerSize.Y - cut),
                MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary));
            ImGui.Dummy(UiHelpers.Scale(new Vector2(14, compact ? 28 : 32)));
        }
        else
        {
            MaterialCanvas.DrawImage(ImGui.GetWindowDrawList(), icon.Handle, icon.Size,
                imageMin, imageMin + UiHelpers.Scale(new Vector2(compact ? 28 : 32)));
            ImGui.Dummy(UiHelpers.Scale(new Vector2(compact ? 30 : 36, compact ? 28 : 32)));
        }
        if (!FrenRiderPresentation.IsLcars)
        {
            ImGui.SameLine();
            using (UiText.Font(UiFontRole.Title)) MaterialText.Text(cyberpunk ? "FREN RIDER" : "Fren Rider");
        }

        NextButton("Settings");
        if (UiGui.Button("Settings", icon: MaterialIcon.Settings))
            plugin.ToggleConfigUi();

        NextButton("Account");
        if (UiGui.Button("Account", icon: MaterialIcon.Person)) ImGui.OpenPopup("##FrenRiderAccountActions");

        NextButton("Chocobo");
        if (UiGui.Button("Chocobo", icon: MaterialIcon.Chocobo))
            plugin.OpenChocoboSettings();

        NextButton("Ko-fi");
        if (UiGui.Button("Ko-fi", icon: MaterialIcon.Heart))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }

        NextGroup(Math.Max(ImGui.GetFrameHeight(), MaterialControls.Metrics.Height) / Math.Max(.01f, MaterialTheme.Metrics.Scale));
        if (UiGui.IconButton("Close", MaterialIcon.Close)) IsOpen = false;
        UiGui.TextDisabled("A Dalamud plugin for FFXIV multiplayer follow/combat automation.");
        var originalIdRoot = ImGui.GetID("");
        var accountWidth = Math.Max(UiHelpers.Scale(360),
            new[] { "All FR off", "All FR on", "Krangle", "DTR" }.Max(label => MaterialText.Measure(UiText.T(label)).X)
                + UiHelpers.Scale(80));
        ImGui.SetNextWindowSize(new Vector2(Math.Min(accountWidth,
            Math.Max(1, ImGui.GetMainViewport().WorkSize.X - ImGui.GetStyle().WindowPadding.X * 2)), 0), ImGuiCond.Always);
        if (ImGui.BeginPopup("##FrenRiderAccountActions"))
        {
            try
            {
                ImGuiP.PushOverrideID(originalIdRoot);
                try { DrawAccountControls(); }
                finally { ImGui.PopID(); }
            }
            finally { ImGui.EndPopup(); }
        }
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
        ImGui.Separator();
        var dtrEnabled = plugin.Configuration.DtrBarEnabled;
        if (UiGui.HeaderToggle("DTR", ref dtrEnabled))
        {
            plugin.Configuration.DtrBarEnabled = dtrEnabled;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Show Fren Rider status in the server info bar.");
        var krangleEnabled = plugin.Configuration.KrangleEnabled;
        if (UiGui.HeaderToggle("Krangle", ref krangleEnabled))
        {
            plugin.Configuration.KrangleEnabled = krangleEnabled;
            plugin.Configuration.Save();
            KrangleService.ClearCache();
        }
        if (ImGui.IsItemHovered()) UiGui.SetTooltip("Obfuscate character names in FrenRider UI.");
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
            UiHelpers.SafeWrappedText("Not logged in. FrenRider waits until a character is loaded.", UiHelpers.Yellow);

        if (plugin.AutoDutyDetectionService.ShouldShowMainWindowWarning())
            UiHelpers.SafeWrappedText("AutoDuty detected while FrenRider is enabled.", UiHelpers.Yellow);
    }

    private void DrawOperatorProfile(CharacterConfig config)
    {
        using var columnStyle = new MaterialStyleScope();
        columnStyle.Style(ImGuiStyleVar.CellPadding, UiHelpers.Scale(new Vector2(2, 2)));
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
        if (FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk)
        {
            var c = MaterialTheme.Current.Colors;
            columnStyle.Color(ImGuiCol.TableRowBg, c.SurfaceContainerLowest);
            columnStyle.Color(ImGuiCol.TableRowBgAlt, c.SurfaceContainer);
            var labelWidth = Math.Min(ImGui.GetContentRegionAvail().X * .38f,
                cells.Max(cell => MaterialText.Measure(UiText.T(cell.Label)).X) + UiHelpers.Scale(12));
            if (!ImGui.BeginTable("##OperatorFields", 2, ImGuiTableFlags.SizingStretchProp
                    | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.NoSavedSettings)) return;
            try
            {
                ImGui.TableSetupColumn("##operator-label", ImGuiTableColumnFlags.WidthFixed, labelWidth);
                ImGui.TableSetupColumn("##operator-value", ImGuiTableColumnFlags.WidthStretch);
                foreach (var cell in cells)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn(); UiGui.TextDisabled(cell.Label);
                    ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(cell.Value, cell.Color, localize: false);
                }
            }
            finally { ImGui.EndTable(); }
            return;
        }
        if (FrenRiderPresentation.Decorated)
        {
            var labelWidth = cells.Max(cell => MaterialText.Measure(UiText.T(cell.Label)).X) / Math.Max(.01f, MaterialTheme.Metrics.Scale) + 12;
            foreach (var cell in cells) UiHelpers.AlignedRow(cell.Label, cell.Value, cell.Color, labelWidth);
            return;
        }
        var columns = ImGui.GetContentRegionAvail().X >= UiHelpers.Scale(340) ? 2 : 1;
        if (ImGui.BeginTable("##OperatorFields", columns, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.NoSavedSettings))
        {
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    ImGui.TableNextColumn();
                    UiGui.TextDisabled(cells[i].Label);
                    using (UiText.Font(UiFontRole.BodyStrong))
                        UiHelpers.SafeWrappedText(cells[i].Value, localize: false);
                }
            }
            finally { ImGui.EndTable(); }
        }
        UiGui.TextDisabled("Fren");
        using (UiText.Font(UiFontRole.BodyStrong)) UiHelpers.SafeWrappedText(cells[2].Value, localize: false);
        ImGui.Separator();
        UiHelpers.AlignedRow("Tracking", cells[3].Value, cells[3].Color, 90);
        ImGui.Separator();
        UiHelpers.AlignedRow("Position", cells[4].Value, labelWidth: 90);
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
        var cyberpunk = FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk;
        var c = MaterialTheme.Current.Colors;
        tableStyle.Color(ImGuiCol.TableHeaderBg, cyberpunk ? c.SurfaceContainerHigh : Vector4.Zero);
        tableStyle.Color(ImGuiCol.TableRowBg, cyberpunk ? c.SurfaceContainerLowest : Vector4.Zero);
        tableStyle.Color(ImGuiCol.TableRowBgAlt, cyberpunk ? c.SurfaceContainer : Vector4.Zero);
        tableStyle.Color(ImGuiCol.ChildBg, cyberpunk ? c.SurfaceContainerLowest : Vector4.Zero);
        using var tightRows = MaterialTable.PushTightRows();
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
        var rowHeight = Math.Max(UiHelpers.Scale(compact ? 24 : 27), ImGui.GetTextLineHeight());
        var height = MathF.Ceiling(ImGui.GetTextLineHeight() + ImGui.GetStyle().CellPadding.Y * 2
            + Math.Max(1, plugin.FrenTracker.Party.Count) * rowHeight)
            + (available < minimumWidth ? ImGui.GetStyle().ScrollbarSize : 0);
        if (!ImGui.BeginTable("##FrenRiderParty", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.PadOuterX,
                new Vector2(available, height), Math.Max(available, minimumWidth))) return;
        try
        {
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
                try
                {
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
                    ImGui.TableNextColumn(); MaterialText.Text(UiText.F("{0}", member.PartyIndex + 1));
                    ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(Disp(member.Name), localize: false);
                    ImGui.TableNextColumn();
                    if (member.ClassJobId != 0)
                    {
                        FrenRiderPresentation.Job(ImGui.GetCursorScreenPos(), UiHelpers.Scale(24), member.ClassJobId);
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
                }
                finally { ImGui.PopID(); }
            }
            if (plugin.FrenTracker.Party.Count == 0)
            {
                ImGui.TableNextRow(); ImGui.TableNextColumn(); MaterialText.Text("—");
                ImGui.TableNextColumn(); UiGui.TextDisabled("Not in a party");
            }
        }
        finally { ImGui.EndTable(); }
    }

    private void DrawAutomationStack()
    {
        var compact = plugin.Configuration.UiCompact;
        using var tightRows = MaterialTable.PushTightRows();
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
        var labelWidth = rows.Max(row => MaterialText.Measure(UiText.T(row.Label)).X) + UiHelpers.Scale(4);
        var fieldWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var columns = fieldWidth >= Math.Max(UiHelpers.Scale(720), labelWidth * 2 + UiHelpers.Scale(420)) ? 4 : 2;
        var cyberpunk = FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk;
        using var tableStyle = new MaterialStyleScope();
        if (cyberpunk)
        {
            tableStyle.Color(ImGuiCol.TableRowBg, MaterialTheme.Current.Colors.SurfaceContainerLowest);
            tableStyle.Color(ImGuiCol.TableRowBgAlt, MaterialTheme.Current.Colors.SurfaceContainer);
        }
        if (!ImGui.BeginTable("##AutomationFields", columns, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH
                | (cyberpunk ? ImGuiTableFlags.RowBg : ImGuiTableFlags.None),
                new Vector2(fieldWidth, 0))) return;
        try
        {
            for (var i = 0; i < columns; i++)
                ImGui.TableSetupColumn("##automation" + i, i % 2 == 0 ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch,
                    i % 2 == 0 ? labelWidth : 1);
            for (var index = 0; index < rows.Count; index++)
            {
                var rowIndex = cyberpunk && columns == 4 ? index / 2 + index % 2 * ((rows.Count + 1) / 2) : index;
                var row = rows[rowIndex];
                ImGui.TableNextColumn(); UiGui.TextDisabled(row.Label);
                ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(row.Value, row.Color);
            }
        }
        finally { ImGui.EndTable(); }
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
        using var tightRows = MaterialTable.PushTightRows();
        var cyberpunk = FrenRiderPresentation.Style == FrenRiderUiStyle.Cyberpunk;
        using var tableStyle = new MaterialStyleScope();
        if (cyberpunk)
        {
            tableStyle.Color(ImGuiCol.TableRowBg, MaterialTheme.Current.Colors.SurfaceContainerLowest);
            tableStyle.Color(ImGuiCol.TableRowBgAlt, MaterialTheme.Current.Colors.SurfaceContainer);
        }
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
        var labelWidth = new[] { "Zone", "ADS", "Exit method" }.Max(label => MaterialText.Measure(UiText.T(label)).X)
            + UiHelpers.Scale(4);
        if (!ImGui.BeginTable("##DutyFields", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.BordersInnerH
                | (cyberpunk ? ImGuiTableFlags.RowBg : ImGuiTableFlags.None))) return;
        try
        {
            ImGui.TableSetupColumn("##duty0", ImGuiTableColumnFlags.WidthFixed, labelWidth);
            ImGui.TableSetupColumn("##duty1", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextColumn(); UiGui.TextDisabled("Zone");
            ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(zoneText);
            ImGui.TableNextColumn(); UiGui.TextDisabled("ADS");
            ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(ads.StatusText, adsColor);
            ImGui.TableNextColumn(); UiGui.TextDisabled("Exit method");
            ImGui.TableNextColumn(); UiHelpers.SafeWrappedText(exitMethod, config.UseAdsLeaveAfterAdsDuty ? UiHelpers.Blue : UiHelpers.Grey);
        }
        finally { ImGui.EndTable(); }
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
