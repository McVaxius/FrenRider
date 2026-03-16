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
            // Method 1: Check if AutoDuty plugin is installed via PluginInterface
            bool autoDutyInstalled = false;
            try
            {
                var installedPlugins = Plugin.PluginInterface.InstalledPlugins;
                autoDutyInstalled = installedPlugins.Any(p => 
                    p.InternalName.Equals("AutoDuty", StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Contains("AutoDuty", StringComparison.OrdinalIgnoreCase));
                
                if (autoDutyInstalled)
                {
                    log.Information("[AutoDutyDetection] AutoDuty plugin detected");
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
            autoDutyDetected = autoDutyInstalled || autoDutyActive;

            log.Debug($"[AutoDutyDetection] Detection result: Installed={autoDutyInstalled}, Active={autoDutyActive}, Detected={autoDutyDetected}");

            // Log state changes
            if (autoDutyDetected && !wasDetected)
            {
                log.Warning("[AutoDutyDetection] AutoDuty plugin detected - showing warning");
                ShowWarning();
            }
            else if (!autoDutyDetected && wasDetected)
            {
                log.Information("[AutoDutyDetection] AutoDuty plugin no longer detected");
                warningShown = false;
                warningWindow.Reset();
            }
        }
        catch (Exception ex)
        {
            log.Error($"[AutoDutyDetection] Error checking for AutoDuty: {ex.Message}");
        }
    }

    private void ShowWarning()
    {
        log.Debug($"[AutoDutyDetection] ShowWarning called - warningShown={warningShown}");
        
        if (!warningShown)
        {
            warningShown = true;
            warningWindow.IsOpen = true;
            log.Warning("[AutoDutyDetection] AutoDuty warning window opened");
        }
        else if (warningShown)
        {
            log.Debug("[AutoDutyDetection] Warning already shown, not opening again");
        }
    }

    public bool IsAutoDutyDetected()
    {
        return autoDutyDetected;
    }

    public void ResetWarning()
    {
        warningShown = false;
        warningWindow.Reset();
    }

    public void ForceShowWarning()
    {
        log.Information("[AutoDutyDetection] Force showing warning window");
        warningShown = false;
        ShowWarning();
    }
}
