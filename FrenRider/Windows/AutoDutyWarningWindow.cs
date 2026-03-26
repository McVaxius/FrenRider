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
    private readonly ICommandManager commandManager;
    private bool warningAcknowledged = false;

    public AutoDutyWarningWindow(Plugin plugin, IChatGui chatGui, IPluginLog log) 
        : base("⚠️ AutoDuty Detected - Action Required", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        this.chatGui = chatGui;
        this.log = log;
        this.commandManager = Plugin.CommandManager;
        
        // Don't set position here - let ImGui handle it initially
        RespectCloseHotkey = false;
    }

    public override void Draw()
    {
        // Center the window when it first appears
        if (ImGui.IsWindowAppearing())
        {
            var viewport = ImGui.GetMainViewport();
            var posX = (viewport.WorkSize.X - 400) / 2;
            var posY = (viewport.WorkSize.Y - 200) / 2;
            ImGui.SetWindowPos(new Vector2(posX, posY));
            log.Information($"[AutoDutyWarning] Window centered at: X={posX:F1}, Y={posY:F1}, Viewport: {viewport.WorkSize.X}x{viewport.WorkSize.Y}");
        }

        ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "⚠️ WARNING: AutoDuty Plugin Detected");
        ImGui.Spacing();
        
        ImGui.Text("AutoDuty is enabled and may cause issues:");
        ImGui.Text("• Force respawn at entrance");
        ImGui.Text("• Leave instances at random times");
        ImGui.Text("• Interfere with FrenRider automation");
        ImGui.Spacing();
        
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "FrenRider requires AutoDuty to be disabled for proper operation.");
        ImGui.Spacing();

        // Disable AutoDuty button - centered
        var buttonWidth = 120;
        var windowWidth = 400;
        ImGui.SetCursorPosX((windowWidth - buttonWidth) / 2);
        
        if (ImGui.Button("Disable AutoDuty", new Vector2(buttonWidth, 30)))
        {
            try
            {
                // Send the command to disable AutoDuty using CommandManager
                commandManager?.ProcessCommand("/xldisableplugin AutoDuty");
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
