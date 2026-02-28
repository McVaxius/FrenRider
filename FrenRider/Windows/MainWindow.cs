using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FrenRider.Models;

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
        ImGui.Text("Fren Rider v0.1.0");
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
            ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1), frenName);

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
            ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1), "Logged in.");

            // Account info
            var account = plugin.ConfigManager.GetCurrentAccount();
            if (account != null)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"[{account.AccountAlias}]");
            }

            // Party info
            var partyCount = Plugin.PartyList.Length;
            ImGui.Text($"Party Members: {partyCount}");

            if (partyCount > 0 && !string.IsNullOrEmpty(frenName))
            {
                var foundFren = false;
                for (var i = 0; i < partyCount; i++)
                {
                    var member = Plugin.PartyList[i];
                    if (member != null)
                    {
                        var memberName = member.Name.ToString();
                        if (memberName.Contains(frenName.Split('@')[0], StringComparison.OrdinalIgnoreCase))
                        {
                            ImGui.TextColored(new Vector4(0.4f, 1, 0.4f, 1), $"Fren found: {memberName}");
                            foundFren = true;
                            break;
                        }
                    }
                }

                if (!foundFren)
                {
                    ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), "Fren not found in party.");
                }
            }
            else if (string.IsNullOrEmpty(frenName))
            {
                ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), "No fren configured.");
            }
            else
            {
                ImGui.TextColored(new Vector4(1, 1, 0.4f, 1), "Not in a party.");
            }

            // Current zone
            ImGui.Text($"Zone ID: {Plugin.ClientState.TerritoryType}");
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
}
