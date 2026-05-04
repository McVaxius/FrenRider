using System;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Fren Rider##MainWindow")
    {
        Size = new Vector2(650, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 360),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        DrawTopBar(config);
        ImGui.Separator();

        if (ImGui.BeginChild("##FrenRiderOperatorScroll", Vector2.Zero, false))
        {
            DrawWarnings();
            DrawOperatorProfile(config);
            DrawPartySummary();
            DrawAutomationStack();
            DrawDutyPanel();
            DrawDebugDetails(config);
        }
        ImGui.EndChild();
    }

    private void DrawTopBar(CharacterConfig config)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.TextUnformatted($"Fren Rider v{version}");

        ImGui.SameLine();
        UiHelpers.StatusPill(config.Enabled ? "Enabled" : "Disabled", config.Enabled ? UiHelpers.Green : UiHelpers.Grey);

        ImGui.SameLine();
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Run", ref enabled))
            plugin.ConfigManager.SetFrenRiderEnabled(enabled);

        ImGui.SameLine();
        var dtrEnabled = plugin.Configuration.DtrBarEnabled;
        if (ImGui.Checkbox("DTR", ref dtrEnabled))
        {
            plugin.Configuration.DtrBarEnabled = dtrEnabled;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show Fren Rider status in the server info bar.");

        ImGui.SameLine();
        var krangleEnabled = plugin.Configuration.KrangleEnabled;
        if (ImGui.Checkbox("Krangle", ref krangleEnabled))
        {
            plugin.Configuration.KrangleEnabled = krangleEnabled;
            plugin.Configuration.Save();
            KrangleService.ClearCache();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Obfuscate character names in FrenRider UI.");

        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();

        ImGui.SameLine();
        if (ImGui.Button("Ko-fi"))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/mcvaxius",
                UseShellExecute = true
            });
        }

        ImGui.SameLine();
        if (ImGui.Button("Close"))
            IsOpen = false;
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
        UiHelpers.SectionHeader("Operator");

        UiHelpers.AlignedRow("Character", GetLocalCharacterText(), Plugin.ClientState.IsLoggedIn ? UiHelpers.Green : UiHelpers.Red);

        var account = plugin.ConfigManager.GetCurrentAccount();
        UiHelpers.AlignedRow("Account", account != null ? Disp(account.AccountAlias) : "No account loaded", account != null ? null : UiHelpers.Yellow);

        var frenName = string.IsNullOrWhiteSpace(config.FrenName)
            ? "No fren configured"
            : Disp(config.FrenName);
        UiHelpers.AlignedRow("Fren", frenName, string.IsNullOrWhiteSpace(config.FrenName) ? UiHelpers.Yellow : UiHelpers.Blue);

        DrawFrenStatus(config);
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
        UiHelpers.SectionHeader("Party");

        var tracker = plugin.FrenTracker;
        var partyCount = tracker.Party.Count;
        var visibleCount = tracker.Party.FindAll(m => m.IsVisible).Count;
        var mountedCount = tracker.Party.FindAll(m => m.IsMounted).Count;

        UiHelpers.AlignedRow("Members", $"{partyCount} total, {visibleCount} visible, {mountedCount} mounted");

        var composition = tracker.GetPartyComposition();
        if (composition.Count > 0)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kvp in composition)
                parts.Add($"{kvp.Value} {kvp.Key}");
            UiHelpers.AlignedRow("Jobs", string.Join(", ", parts));
        }

        foreach (var member in tracker.Party)
        {
            var jobTag = string.IsNullOrWhiteSpace(member.ClassJobName) ? "?" : member.ClassJobName;
            var status = member.IsVisible
                ? $"{jobTag}, {(member.IsMounted ? "mounted" : "on foot")}, {member.DistanceToPlayer:F0}y"
                : $"{jobTag}, not visible";
            UiHelpers.AlignedRow(Disp(member.Name), status, member.IsVisible ? null : UiHelpers.Red, 170f);
        }
    }

    private void DrawAutomationStack()
    {
        UiHelpers.SectionHeader("Automation");

        var follow = plugin.FollowService;
        UiHelpers.AlignedRow("Follow", $"{follow.State} - {follow.StateDetail}", GetFollowColor(follow.State));

        var mount = plugin.MountService;
        var mountDetail = string.IsNullOrWhiteSpace(mount.StateDetail) ? mount.State.ToString() : $"{mount.State} - {mount.StateDetail}";
        UiHelpers.AlignedRow("Mount", mountDetail, GetMountColor(mount.State));

        var combat = plugin.CombatService;
        var combatDetail = string.IsNullOrWhiteSpace(combat.StateDetail) ? combat.State.ToString() : $"{combat.State} - {combat.StateDetail}";
        UiHelpers.AlignedRow("Combat", combatDetail, GetCombatColor(combat.State));

        var auto = plugin.AutomationService;
        var idleText = auto.IsIdle
            ? string.IsNullOrWhiteSpace(auto.LastIdleAction) ? "Idle" : $"Idle; last {auto.LastIdleAction}"
            : "Active checks running";
        UiHelpers.AlignedRow("Idle", idleText, auto.IsIdle ? UiHelpers.Blue : null);

        if (!string.IsNullOrWhiteSpace(auto.FoodStatus))
            UiHelpers.AlignedRow("Food", auto.FoodStatus, auto.FoodStatus.StartsWith("Well Fed", StringComparison.OrdinalIgnoreCase) ? UiHelpers.Green : UiHelpers.Yellow);

        DrawCompanionStatus(auto);

        var formation = plugin.FormationService;
        if (formation.IsActive)
            UiHelpers.AlignedRow("Formation", $"Slot {formation.AssignedSlot}", UiHelpers.Blue);
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

    private void DrawDutyPanel()
    {
        UiHelpers.SectionHeader("Duty / ADS / Exit");

        var zone = plugin.ZoneService;
        var zoneExtra = "";
        if (zone.InFate) zoneExtra += $", FATE {zone.CurrentFateId}";
        if (zone.IsIndoors) zoneExtra += ", indoors";
        UiHelpers.AlignedRow("Zone", $"{zone.CurrentZone} (territory {zone.TerritoryId}{zoneExtra})");

        var ads = plugin.AdsIntegrationService;
        var adsColor = ads.IsControllingDuty
            ? UiHelpers.Green
            : ads.IsHandoffPending
                ? UiHelpers.Yellow
                : ads.AdsLoaded
                    ? UiHelpers.Blue
                    : UiHelpers.Grey;
        UiHelpers.AlignedRow("ADS", ads.StatusText, adsColor);

        var exit = plugin.ExitBehaviourService;
        if (!string.IsNullOrWhiteSpace(exit.StateDetail))
            UiHelpers.AlignedRow("Exit", exit.StateDetail, UiHelpers.Yellow);
        else
            UiHelpers.AlignedRow("Exit", "Waiting for duty-end rule", UiHelpers.Grey);

        var dutyInteract = plugin.DutyInteractService;
        if (dutyInteract.IsActive)
            UiHelpers.AlignedRow("Duty interact", dutyInteract.StateDetail, UiHelpers.Yellow);
    }

    private void DrawDebugDetails(CharacterConfig config)
    {
        UiHelpers.SectionHeader("Details");

        if (!ImGui.CollapsingHeader("Compact debug", ImGuiTreeNodeFlags.DefaultOpen))
            return;

        UiHelpers.AlignedRow("Config", $"Run={config.Enabled}, FlyYouFools={config.FlyYouFools}, FollowMode={config.ClingType}/{config.ClingTypeDuty}");
        UiHelpers.AlignedRow("Distances", $"Cling={config.Cling:F1}, Max={config.MaxBistance:F0}, ForayMax={config.MaxBistanceForay:F0}");
        UiHelpers.AlignedRow("ADS flags", $"Loaded={plugin.AdsIntegrationService.AdsLoaded}, Pending={plugin.AdsIntegrationService.IsHandoffPending}, Controlling={plugin.AdsIntegrationService.IsControllingDuty}");
    }

    private string GetLocalCharacterText()
    {
        if (!Plugin.ClientState.IsLoggedIn)
            return "Not logged in";

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return "Logged in, local player unavailable";

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

    private string Disp(string name)
        => plugin.Configuration.KrangleEnabled ? KrangleService.KrangleName(name) : name;
}
