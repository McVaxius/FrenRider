using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FrenRider.Models;

namespace FrenRider.Services;

/// <summary>
/// Automatically clicks Yes on specific dialog types when FrenRider is enabled.
/// Handles Raise offers, Teleport offers, and Party Invites.
/// </summary>
public class AutoYesService : IDisposable
{
    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private readonly ICondition condition;
    
    private readonly Dictionary<string, string> autoYesPatterns = new()
    {
        //Misc stuff from yesalerady
        {"misc1", "Use the teleporter?"},
        {"misc2", "Return to the levemete"},
//        {"misc2b", "Return to the starting point for the Praetorium?   ※You may be unable to re-enter ongoing battles."}, //haha this is bad
        {"misc3", "Duty calls"},
        {"misc4", "Are you interested"},    
        {"misc5", "Move immediately to sealed area"},
        
        // Raise offers
        {"raise", "Would you like to be raised"},
        {"raise2", "Accept Raise"},
        
        // Teleport offers  
        {"teleport", "Accept Teleport to"},
   //     {"teleport2", "Teleport to the"},
        {"teleport3", "Would you like to teleport"},
        {"teleport4", "Accept Teleport"},
   //     {"return", "Return to the"},
        
        // Party invites (already handled by PartyService, but added as backup)
        {"party", "Would you like to join the party"},
        {"party2", "Join the party"}
    };
    
    private DateTime lastCheckTime = DateTime.MinValue;
    private readonly TimeSpan checkInterval = TimeSpan.FromMilliseconds(500); // Check every 500ms
    private string lastHandledDialog = "";
    private DateTime lastHandledTime = DateTime.MinValue;
    private readonly TimeSpan handleCooldown = TimeSpan.FromSeconds(2); // Don't spam same dialog
    
    public AutoYesService(Plugin plugin, IGameGui gameGui, ICondition condition, IPluginLog log)
    {
        this.plugin = plugin;
        this.gameGui = gameGui;
        this.condition = condition;
        this.log = log;
    }
    
    public void Dispose()
    {
        // Nothing to dispose
    }
    
    /// <summary>
    /// Check for auto-yes dialogs and click Yes if enabled and pattern matches.
    /// This should be called regularly (e.g., in plugin update loop).
    /// </summary>
    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (config == null || !config.Enabled)
            return;
            
        // Rate limit checks
        var now = DateTime.Now;
        if (now - lastCheckTime < checkInterval)
            return;
        lastCheckTime = now;
        
        // Don't interfere during cutscenes or in combat
        if (condition[ConditionFlag.OccupiedInCutSceneEvent] || 
            condition[ConditionFlag.WatchingCutscene] ||
            condition[ConditionFlag.InCombat])
            return;
            
        unsafe
        {
            nint addonPtr = gameGui.GetAddonByName("SelectYesno", 1);
            if (addonPtr == 0)
                return;
                
            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible)
                return;
                
            var promptNode = addon->PromptText;
            if (promptNode == null)
                return;
                
            var textPtr = promptNode->NodeText.StringPtr;
            if (textPtr == null)
                return;
                
            var promptSe = MemoryHelper.ReadSeStringNullTerminated(new IntPtr(textPtr));
            var dialogText = promptSe.TextValue;
            if (string.IsNullOrEmpty(dialogText))
                return;
                
            // Check if we recently handled this same dialog
            if (dialogText == lastHandledDialog && now - lastHandledTime < handleCooldown)
                return;
                
            log.Debug($"[AutoYes] Dialog detected: {dialogText}");
            
            // Check for auto-yes patterns
            string matchedKey = null;
            
            foreach (var kvp in autoYesPatterns)
            {
                if (dialogText.Contains(kvp.Value, StringComparison.OrdinalIgnoreCase))
                {
                    matchedKey = kvp.Key;
                    log.Debug($"[AutoYes] Pattern matched: {kvp.Key} -> {kvp.Value}");
                    break;
                }
            }
            
            if (!string.IsNullOrEmpty(matchedKey))
            {
                // Check if this is a party invite that should be handled by PartyService instead
                if (matchedKey.StartsWith("party") && config.PartyInviteAutoAccept)
                {
                    // Let PartyService handle party invites based on whitelist
                    log.Debug($"[AutoYes] Party invite detected, letting PartyService handle: {dialogText}");
                    return;
                }
                
                // Handle raise offers
                if (matchedKey.StartsWith("raise") && config.RaiseOfferAutoAccept)
                {
                    log.Debug($"[AutoYes] Accepting raise offer (config enabled)");
                    ClickYesAndLog(dialogText, "Raise offer");
                    return;
                }
                else if (matchedKey.StartsWith("raise"))
                {
                    log.Debug($"[AutoYes] Skipping raise offer (config disabled)");
                }
                
                // Handle teleport offers
                if ((matchedKey.StartsWith("teleport") || matchedKey.StartsWith("return")) && config.TeleportOfferAutoAccept)
                {
                    log.Debug($"[AutoYes] Accepting teleport offer (config enabled)");
                    ClickYesAndLog(dialogText, "Teleport offer");
                    return;
                }
                else if (matchedKey.StartsWith("teleport") || matchedKey.StartsWith("return"))
                {
                    log.Debug($"[AutoYes] Skipping teleport offer (config disabled)");
                }
                
                // Fallback: if no specific config, handle all non-party dialogs
                if (!matchedKey.StartsWith("party"))
                {
                    log.Debug($"[AutoYes] Accepting dialog (fallback)");
                    ClickYesAndLog(dialogText, autoYesPatterns[matchedKey]);
                    return;
                }
            }
        }
    }
    
    private unsafe void ClickYesAndLog(string dialogText, string dialogType)
    {
        try
        {
            // Use the existing GameHelpers method
            GameHelpers.ClickYesIfVisible();
            
            // Track that we handled this dialog
            lastHandledDialog = dialogText;
            lastHandledTime = DateTime.Now;
            
            log.Information($"[AutoYes] Automatically clicked Yes on {dialogType}: {dialogText}");
        }
        catch (Exception ex)
        {
            log.Error($"[AutoYes] Failed to click Yes on {dialogType}: {ex.Message}");
        }
    }
}
