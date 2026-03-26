using System;
using System.Linq;
using Dalamud.Plugin.Services;
using FrenRider.Windows;

namespace FrenRider.Services;

public class AutoDutyDetectionService
{
    private readonly Plugin plugin;
    private readonly IChatGui chatGui;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly AutoDutyWarningWindow warningWindow;
    
    private bool autoDutyDetected = false;
    private bool warningShown = false;
    private DateTime lastCheck = DateTime.MinValue;
    private const int CHECK_INTERVAL_SECONDS = 5;

    public AutoDutyDetectionService(Plugin plugin, IChatGui chatGui, IFramework framework, IPluginLog log, AutoDutyWarningWindow warningWindow)
    {
        this.plugin = plugin;
        this.chatGui = chatGui;
        this.framework = framework;
        this.log = log;
        this.warningWindow = warningWindow;
        
        framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework fw)
    {
        // Only check every few seconds to avoid spam
        var now = DateTime.UtcNow;
        if ((now - lastCheck).TotalSeconds < CHECK_INTERVAL_SECONDS)
            return;
        
        lastCheck = now;
        CheckForAutoDuty();
    }

    private void CheckForAutoDuty()
    {
        try
        {
            // Method 1: Check if AutoDuty plugin is installed AND enabled
            bool autoDutyInstalled = false;
            bool autoDutyEnabled = false;
            try
            {
                var installedPlugins = Plugin.PluginInterface.InstalledPlugins;
                var autodutyPlugin = installedPlugins.FirstOrDefault(p => 
                    p.InternalName.Equals("AutoDuty", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("AutoDuty", StringComparison.OrdinalIgnoreCase));
                
                if (autodutyPlugin != null)
                {
                    autoDutyInstalled = true;
                    autoDutyEnabled = autodutyPlugin.IsLoaded; // Check if actually loaded/enabled
                    
                    if (autoDutyEnabled)
                    {
                        log.Information("[AutoDutyDetection] AutoDuty plugin is enabled and running");
                    }
                    else
                    {
                        log.Information("[AutoDutyDetection] AutoDuty plugin is installed but disabled");
                    }
                }
            }
            catch (Exception ex)
            {
                log.Debug($"[AutoDutyDetection] Could not check installed plugins: {ex.Message}");
            }

            // Method 2: Try to detect AutoDuty IPC or specific chat patterns
            bool autoDutyActive = false;
            
            // Check for AutoDuty in chat (might show status messages)
            // This is a fallback method if plugin detection fails
            
            var wasDetected = autoDutyDetected;
            autoDutyDetected = autoDutyEnabled || autoDutyActive; // Only detect if actually enabled

            log.Debug($"[AutoDutyDetection] Detection result: Installed={autoDutyInstalled}, Enabled={autoDutyEnabled}, Active={autoDutyActive}, Detected={autoDutyDetected}");

            // Log state changes and show warning if both conditions are met
            if (autoDutyDetected && !wasDetected)
            {
                log.Warning("[AutoDutyDetection] AutoDuty plugin detected - showing warning");
                ShowWarning();
            }
            else if (!autoDutyDetected && wasDetected)
            {
                log.Information("[AutoDutyDetection] AutoDuty plugin no longer detected - resetting warning state");
                warningShown = false;
                warningWindow.Reset();
                // Also close the window if it's open
                if (warningWindow.IsOpen)
                {
                    warningWindow.IsOpen = false;
                    log.Information("[AutoDutyDetection] Closed warning window since AutoDuty is no longer detected");
                }
            }
            else if (autoDutyDetected && wasDetected)
            {
                log.Debug("[AutoDutyDetection] AutoDuty still detected - checking if warning should be shown");
                
                // Also show warning if FrenRider is enabled and AutoDuty is detected (even if no state change)
                if (plugin.ConfigManager.GetActiveConfig().Enabled && !warningShown)
                {
                    log.Information("[AutoDutyDetection] FrenRider enabled + AutoDuty detected - showing warning (no state change)");
                    ShowWarning();
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"[AutoDutyDetection] Error checking for AutoDuty: {ex.Message}");
        }
    }

    private void ShowWarning()
    {
        log.Debug($"[AutoDutyDetection] ShowWarning called - warningShown={warningShown}, enabled={plugin.ConfigManager.GetActiveConfig().Enabled}");
        
        if (!warningShown && plugin.ConfigManager.GetActiveConfig().Enabled)
        {
            warningShown = true;
            warningWindow.IsOpen = true;
            log.Warning("[AutoDutyDetection] AutoDuty warning window opened");
        }
        else if (warningShown)
        {
            log.Debug("[AutoDutyDetection] Warning already shown, not opening again");
        }
        else if (!plugin.ConfigManager.GetActiveConfig().Enabled)
        {
            log.Debug("[AutoDutyDetection] FrenRider is disabled, not showing AutoDuty warning");
        }
    }

    public bool IsAutoDutyDetected()
    {
        return autoDutyDetected;
    }

    public bool ShouldShowMainWindowWarning()
    {
        return autoDutyDetected && plugin.ConfigManager.GetActiveConfig().Enabled && !warningWindow.IsOpen;
    }

    public void ResetWarning()
    {
        warningShown = false;
        warningWindow.Reset();
        warningWindow.IsOpen = false;
    }

    public void HandleFrenRiderDisabled()
    {
        log.Information("[AutoDutyDetection] FrenRider disabled - clearing warning lifecycle state");
        warningShown = false;
        warningWindow.Reset();
        warningWindow.IsOpen = false;
    }

    public void ForceShowWarning()
    {
        log.Information("[AutoDutyDetection] Force showing warning window");
        warningShown = false;
        ShowWarning();
    }

    public void ForceCheck()
    {
        log.Information("[AutoDutyDetection] Force checking for AutoDuty");
        CheckForAutoDuty();
    }
}
