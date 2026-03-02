using System;
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
        : base("Fren Rider##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(350, 280),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.ConfigManager.GetActiveConfig();

        // Header
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
        ImGui.Text($"Fren Rider v{version}");
        ImGui.Separator();
        ImGui.Spacing();

        // Enable / Disable toggle
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            plugin.ConfigManager.SaveCurrentAccount();
        }

        // DTR bar toggle
        ImGui.SameLine();
        var dtrEnabled = plugin.Configuration.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar", ref dtrEnabled))
        {
            plugin.Configuration.DtrBarEnabled = dtrEnabled;
            plugin.Configuration.Save();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show Fren Rider status in the server info bar.\nDisable if you don't want the DTR bar entry.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Fren Name (read-only, pulled from config)
        ImGui.Text("Fren:");
        ImGui.SameLine();
        var frenName = config.FrenName;
        if (string.IsNullOrEmpty(frenName))
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "(not set - configure in Settings)");
        else
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1), Disp(frenName));

        ImGui.Spacing();

        // Status Display
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.Text("Status:");
        ImGui.Spacing();

        if (!Plugin.ClientState.IsLoggedIn)
        {
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "Not logged in.");
        }
        else
        {
            // Show logged-in character name
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer != null)
            {
                var charName = localPlayer.Name.ToString();
                var worldName = localPlayer.HomeWorld.Value.Name.ToString();
                var fullName = $"{charName}@{worldName}";
                ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1), $"Logged in. [{Disp(fullName)}]");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1), "Logged in.");
            }

            // Account info
            var account = plugin.ConfigManager.GetCurrentAccount();
            if (account != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({Disp(account.AccountAlias)})");
            }

            // Party info (from FrenTracker)
            var tracker = plugin.FrenTracker;
            var partyCount = tracker.Party.Count;
            ImGui.Text($"Party Members: {partyCount}");

            if (partyCount > 0)
            {
                // Party composition summary
                var comp = tracker.GetPartyComposition();
                var compParts = new System.Collections.Generic.List<string>();
                foreach (var kvp in comp)
                    compParts.Add($"{kvp.Value} {kvp.Key}");
                if (compParts.Count > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"({string.Join(", ", compParts)})");
                }
            }

            // Fren tracking status
            var fren = tracker.Fren;
            if (string.IsNullOrEmpty(frenName))
            {
                ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), "No fren configured.");
            }
            else if (fren == null)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1), "Tracking inactive.");
            }
            else if (fren.IsFound && fren.IsVisible)
            {
                var dispName = Disp(fren.Name);
                var jobInfo = !string.IsNullOrEmpty(fren.ClassJobName) ? $" [{fren.ClassJobName}]" : "";
                var partyInfo = fren.InParty ? "" : " (not in party)";
                ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1), $"Fren: {dispName}{jobInfo}{partyInfo}");
                ImGui.Text($"Distance: {fren.Distance:F1}y");
                ImGui.SameLine();
                ImGui.TextDisabled($"({fren.Position.X:F0}, {fren.Position.Y:F0}, {fren.Position.Z:F0})");
            }
            else if (fren.IsFound)
            {
                ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), $"Fren {Disp(fren.Name)} in party but not visible.");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "Fren not found.");
            }

            // Follow state
            var follow = plugin.FollowService;
            var stateColor = follow.State switch
            {
                FollowState.Following => new Vector4(0.4f, 0.8f, 1f, 1),
                FollowState.InRange => new Vector4(0.4f, 1f, 0.4f, 1),
                FollowState.TooFar => new Vector4(1f, 0.6f, 0.2f, 1),
                FollowState.InCombat => new Vector4(1f, 0.4f, 0.4f, 1),
                _ => new Vector4(0.5f, 0.5f, 0.5f, 1),
            };
            ImGui.TextColored(stateColor, $"Follow: {follow.State}");
            ImGui.SameLine();
            ImGui.TextDisabled($"- {follow.StateDetail}");

            // Mount state
            var mount = plugin.MountService;
            if (mount.State != MountState.Idle || (fren != null && fren.IsFound && fren.IsMounted))
            {
                var mountColor = mount.State switch
                {
                    MountState.Mounted => new Vector4(0.4f, 1f, 0.8f, 1),
                    MountState.Mounting or MountState.WaitingToMount => new Vector4(1f, 1f, 0.4f, 1),
                    MountState.Dismounting => new Vector4(1f, 0.6f, 0.4f, 1),
                    _ => new Vector4(0.5f, 0.5f, 0.5f, 1),
                };
                var mountText = mount.State != MountState.Idle
                    ? $"Mount: {mount.State} - {mount.StateDetail}"
                    : $"Fren mounted (ID {fren!.MountId})";
                ImGui.TextColored(mountColor, mountText);
            }

            // Combat state
            var combat = plugin.CombatService;
            if (combat.State != CombatState.OutOfCombat)
            {
                var combatColor = combat.State switch
                {
                    CombatState.InCombat => new Vector4(1f, 0.3f, 0.3f, 1),
                    CombatState.EnteringCombat => new Vector4(1f, 0.6f, 0.2f, 1),
                    CombatState.LeavingCombat => new Vector4(0.6f, 0.6f, 0.6f, 1),
                    _ => new Vector4(0.5f, 0.5f, 0.5f, 1),
                };
                ImGui.TextColored(combatColor, $"Combat: {combat.State}");
                if (!string.IsNullOrEmpty(combat.StateDetail))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"- {combat.StateDetail}");
                }
            }

            // Idle / Automation status
            var auto = plugin.AutomationService;
            if (auto.IsIdle)
            {
                ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1), "Idle");
                if (!string.IsNullOrEmpty(auto.LastIdleAction))
                {
                    ImGui.SameLine();
                    ImGui.TextDisabled($"- Last: {auto.LastIdleAction}");
                }
            }

            // Food status
            if (!string.IsNullOrEmpty(auto.FoodStatus))
            {
                var foodColor = auto.FoodStatus.StartsWith("Well Fed")
                    ? new Vector4(0.4f, 1f, 0.4f, 1)
                    : auto.FoodStatus.Contains("Ate") || auto.FoodStatus.Contains("Switched")
                        ? new Vector4(1f, 0.9f, 0.4f, 1)
                        : new Vector4(1f, 0.5f, 0.3f, 1);
                ImGui.TextColored(foodColor, $"Food: {auto.FoodStatus}");
            }

            // Companion status
            if (!string.IsNullOrEmpty(auto.CompanionStatus))
            {
                var compColor = auto.CompanionStatus.StartsWith("Companion:")
                    ? new Vector4(0.4f, 1f, 0.8f, 1)
                    : auto.CompanionStatus.Contains("Summoning")
                        ? new Vector4(1f, 1f, 0.4f, 1)
                        : new Vector4(0.8f, 0.6f, 0.4f, 1);
                ImGui.TextColored(compColor, auto.CompanionStatus);
            }

            // Formation info
            var formation = plugin.FormationService;
            if (formation.IsActive)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.6f, 1f, 1), $"Formation: Slot {formation.AssignedSlot}");
            }

            // Zone info
            var zone = plugin.ZoneService;
            var zoneExtra = "";
            if (zone.InFate) zoneExtra += $", FATE {zone.CurrentFateId}";
            if (zone.IsIndoors) zoneExtra += ", indoors";
            ImGui.Text($"Zone: {zone.CurrentZone}");
            ImGui.SameLine();
            ImGui.TextDisabled($"(ID {zone.TerritoryId}{zoneExtra})");
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Buttons
        if (ImGui.Button("Open Settings"))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.SameLine();

        if (ImGui.Button("Close"))
        {
            IsOpen = false;
        }
    }

    private string Disp(string name)
    {
        return plugin.Configuration.KrangleEnabled ? KrangleService.KrangleName(name) : name;
    }
}
