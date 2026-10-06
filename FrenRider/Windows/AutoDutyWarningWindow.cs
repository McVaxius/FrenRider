using AethertekUI.Dalamud;
using AethertekUI;
using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;

namespace FrenRider.Windows;

public class AutoDutyWarningWindow : Window
{
    private readonly MaterialWindowMotion motion = new();
    private readonly AethertekUI.MaterialWindowOpacity windowOpacity = new();
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

    public override void PreDraw()
    {
        var style = ImGui.GetStyle();
        var titleWidth = MaterialText.Measure(UiText.T("AutoDuty Detected - Action Required")).X
            + ImGui.GetFontSize() * 2 + style.FramePadding.X * 3 + style.ItemInnerSpacing.X;
        ImGui.SetNextWindowSize(new Vector2(MathF.Ceiling(Math.Max(UiHelpers.Scale(520), titleWidth)), 0));
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * .5f, ImGuiCond.Appearing, new Vector2(.5f));
        motion.Prepare(this, reducedMotion: false, roundedCorners: true);
    }

    public override void PostDraw()
    {
        motion.Restore(this);
        plugin.ApplyWindowOpacity(windowOpacity, WindowName);
    }

    public override void Draw()
    {
        motion.DrawChrome();
        UiGui.Title("⚠️ AutoDuty Detected - Action Required", UiText.T("AutoDuty Detected - Action Required"));
        ImGui.PushTextWrapPos(0);
        UiGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "⚠️ WARNING: AutoDuty Plugin Detected");
        ImGui.Spacing();
        
        UiGui.TextWrapped("AutoDuty is enabled and may cause issues:");
        UiGui.TextWrapped("• Force respawn at entrance");
        UiGui.TextWrapped("• Leave instances at random times");
        UiGui.TextWrapped("• Interfere with FrenRider automation");
        ImGui.Spacing();
        
        UiGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1.0f), "FrenRider requires AutoDuty to be disabled for proper operation.");
        ImGui.Spacing();

        // Disable AutoDuty button - centered
        var buttonWidth = Math.Max(UiHelpers.Scale(120), MaterialText.Measure(UiText.T("Disable AutoDuty")).X + ImGui.GetStyle().FramePadding.X * 2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - buttonWidth) / 2));
        
        if (UiGui.Button("Disable AutoDuty", new Vector2(buttonWidth, UiHelpers.Scale(30))))
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
        UiGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f), "This window will close automatically after disabling AutoDuty.");
        ImGui.PopTextWrapPos();
        if (ImGui.IsWindowAppearing())
        {
            // A reopened auto-sized window can still carry the previous density's height.
            var viewport = ImGui.GetMainViewport();
            var size = motion.GetLogicalSize();
            size.Y = ImGui.GetItemRectMax().Y - ImGui.GetWindowPos().Y + ImGui.GetStyle().WindowPadding.Y;
            ImGui.SetWindowPos(viewport.WorkPos + (viewport.WorkSize - size) * .5f);
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
