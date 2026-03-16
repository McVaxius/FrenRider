using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace FrenRider.Windows;

public class AutoDutyWarningWindow : Window
{
    private readonly Plugin plugin;
    private readonly IChatGui chatGui;
    private readonly IPluginLog log;
    private bool warningAcknowledged = false;

    public AutoDutyWarningWindow(Plugin plugin, IChatGui chatGui, IPluginLog log) 
        : base("⚠️ AutoDuty Detected - Action Required", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove)
    {
        this.plugin = plugin;
        this.chatGui = chatGui;
        this.log = log;
        
        // Center the window on screen
        var viewport = ImGui.GetMainViewport();
        var windowSize = new Vector2(400, 200);
        Position = new Vector2(
            (viewport.WorkSize.X - windowSize.X) / 2,
            (viewport.WorkSize.Y - windowSize.Y) / 2
        );
    }

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "⚠️ WARNING: AutoDuty Plugin Detected");
        ImGui.Spacing();
        
        ImGui.Text("AutoDuty is enabled and may cause issues:");
        ImGui.Text("• Force respawn at entrance");
        ImGui.Text("• Leave instances at random times");
        ImGui.Text("• Interfere with FrenRider automation");
        ImGui.Spacing();
        
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "FrenRider requires AutoDuty to be disabled for proper operation.");
        ImGui.Spacing();

        // Disable AutoDuty button
        var buttonWidth = 120;
        ImGui.SetCursorPosX((400 - buttonWidth) / 2);
        
        if (ImGui.Button("Disable AutoDuty", new Vector2(buttonWidth, 30)))
        {
            try
            {
                // Send the command to disable AutoDuty
                chatGui.Print("/xldisableplugin AutoDuty");
                log.Information("[AutoDutyWarning] Sent /xldisableplugin AutoDuty command");
                warningAcknowledged = true;
                IsOpen = false;
            }
            catch (Exception ex)
            {
                log.Error($"[AutoDutyWarning] Failed to disable AutoDuty: {ex.Message}");
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "This window will close automatically after disabling AutoDuty.");
        
        // Prevent closing with X button or ESC
        if (ImGui.IsWindowHovered() && ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            // Don't allow ESC to close
        }
    }

    public override void OnClose()
    {
        // Only allow closing if we've acknowledged the warning
        if (!warningAcknowledged)
        {
            IsOpen = true; // Force window to stay open
        }
    }

    public void Reset()
    {
        warningAcknowledged = false;
    }
}
