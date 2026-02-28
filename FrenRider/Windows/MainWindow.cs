using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace FrenRider.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base("Fren Rider##MainWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(400, 300),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var config = plugin.Configuration;

        // Header
        ImGui.Text("Fren Rider v0.0.1");
        ImGui.Separator();
        ImGui.Spacing();

        // Enable / Disable toggle
        var enabled = config.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            config.Enabled = enabled;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        // Fren Name
        var frenName = config.FrenName;
        if (ImGui.InputText("Fren Name", ref frenName, 64))
        {
            config.FrenName = frenName;
            config.Save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Name of the party member to follow. Can be partial as long as it's unique. Do not include @Server.");
        }

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

            // Party info
            var partyCount = Plugin.PartyList.Length;
            ImGui.Text($"Party Members: {partyCount}");

            if (partyCount > 0)
            {
                var foundFren = false;
                for (var i = 0; i < partyCount; i++)
                {
                    var member = Plugin.PartyList[i];
                    if (member != null)
                    {
                        var memberName = member.Name.ToString();
                        if (memberName.Contains(config.FrenName, StringComparison.OrdinalIgnoreCase))
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
