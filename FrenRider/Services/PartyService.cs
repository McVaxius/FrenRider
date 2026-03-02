using System;
using System.Linq;
using System.Text.RegularExpressions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FrenRider.Models;
using ECommons.UIHelpers.AddonMasterImplementations;

namespace FrenRider.Services;

public class PartyService
{
    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private readonly IGameGui gameGui;
    private bool lastInParty;
    private string? lastInviterName;
    private long lastPromptHandled;
    private string? lastPromptInviter;
    private int callbackAttempts;

    private const int MaxCallbackAttempts = 8;
    private const int CallbackRetryMs = 250;

    private static readonly Regex InvitePromptRegex = new("Join (?<name>.+?)'?s party\\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public PartyService(Plugin plugin, IPluginLog log, IGameGui gameGui)
    {
        this.plugin = plugin;
        this.log = log;
        this.gameGui = gameGui;
    }

    public void Initialize()
    {
        Plugin.ChatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose()
    {
        Plugin.ChatGui.ChatMessage -= OnChatMessage;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled || config.InviteWhitelist.Count == 0)
            return;

        var inParty = Plugin.PartyList.Length > 0;

        // If we just joined a party and we had a whitelisted inviter, set them as fren
        if (inParty && !lastInParty && !string.IsNullOrEmpty(lastInviterName))
        {
            config.FrenName = lastInviterName;
            plugin.ConfigManager.SaveCurrentAccount();
            log.Information($"Auto-set fren to whitelisted inviter: {lastInviterName}");
            lastInviterName = null;
            callbackAttempts = 0;
        }

        lastInParty = inParty;

        try
        {
            CheckInviteDialog(config);
        }
        catch (Exception ex)
        {
            log.Error(ex, "PartyService.CheckInviteDialog failed");
        }
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled || config.InviteWhitelist.Count == 0)
            return;

        // Don't auto-accept if already in a party
        if (Plugin.PartyList.Length > 0)
            return;

        // Parse message text for party invite pattern: "Name invites you to a party."
        var messageText = message.TextValue;
        if (string.IsNullOrEmpty(messageText))
            return;

        // Check if this is a party invite message
        if (!messageText.Contains("invites you to a party", StringComparison.OrdinalIgnoreCase))
            return;

        // Extract inviter name from message (format: "Name invites you to a party.")
        var inviterName = messageText.Split(new[] { " invites you to a party" }, StringSplitOptions.None)[0].Trim();
        
        // Check if inviter is on whitelist
        var normalized = NormalizeName(inviterName);
        if (IsWhitelisted(config, normalized, out var matchedEntry))
        {
            lastInviterName = matchedEntry; // Store the whitelist entry, not the full name
            log.Information($"Whitelisted party invite detected from: {matchedEntry}. Waiting for SelectYesno dialog to accept.");
        }
    }

    private unsafe void CheckInviteDialog(CharacterConfig config)
    {
        if (config.InviteWhitelist.Count == 0)
            return;

        // Don't auto-accept if already in a party
        if (Plugin.PartyList.Length > 0)
            return;

        // Just check index 1 - no need to spam multiple indices
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
        var prompt = promptSe.TextValue;
        if (string.IsNullOrEmpty(prompt))
            return;

        var match = InvitePromptRegex.Match(prompt.Trim());
        if (!match.Success)
            return;

        var inviterRaw = match.Groups["name"].Value;
        var normalizedInviter = NormalizeName(inviterRaw);
        if (string.IsNullOrEmpty(normalizedInviter))
            return;

        if (!IsWhitelisted(config, normalizedInviter, out var matchedEntry))
            return;

        var now = Environment.TickCount64;

        if (lastPromptInviter != normalizedInviter)
        {
            lastPromptInviter = normalizedInviter;
            lastPromptHandled = 0;
            callbackAttempts = 0;
            lastInviterName = matchedEntry; // Store the whitelist entry, not the full name
            log.Information($"SelectYesno invite matched whitelist: {normalizedInviter}");
        }

        if (callbackAttempts >= MaxCallbackAttempts)
        {
            if (now - lastPromptHandled >= 2000)
            {
                lastPromptHandled = now;
                log.Warning($"Reached max callback attempts ({MaxCallbackAttempts}) for invite from {normalizedInviter}. Waiting for dialog state change.");
            }
            return;
        }

        if (now - lastPromptHandled < CallbackRetryMs)
            return;

        var accepted = AcceptInvite(addon, normalizedInviter, callbackAttempts + 1);
        lastPromptHandled = now;
        callbackAttempts++;

        if (accepted)
        {
            log.Information($"Issued accept attempt #{callbackAttempts} for whitelisted invite from: {normalizedInviter}");
        }
    }

    private unsafe bool AcceptInvite(AddonSelectYesno* addon, string inviterName, int attempt)
    {
        try
        {
            // Use YesAlready's exact pattern
            new AddonMaster.SelectYesno(&addon->AtkUnitBase).Yes();
            log.Information($"Invite accept attempt #{attempt} for {inviterName}: AddonMaster.SelectYesno.Yes() called");
            return true;
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Invite accept attempt #{attempt} for {inviterName} threw an exception");
            return false;
        }
    }

    private static string NormalizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var trimmed = raw.Trim();
        
        // Strip @Server part if present
        var atIndex = trimmed.IndexOf('@');
        if (atIndex >= 0)
        {
            trimmed = trimmed.Substring(0, atIndex).Trim();
        }

        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            // Take first two tokens (first name and last name)
            trimmed = $"{tokens[0]} {tokens[1]}";
        }

        // Apply proper capitalization without server processing
        var parts = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = string.Join(" ", parts.Select(w => 
            w.Length > 0 ? char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "") : string.Empty));
        
        return result;
    }

    private static bool IsWhitelisted(CharacterConfig config, string inviterName, out string matchedEntry)
    {
        matchedEntry = null;
        foreach (var wl in config.InviteWhitelist)
        {
            var normalizedWl = NormalizeName(wl);
            if (inviterName.Contains(normalizedWl, StringComparison.OrdinalIgnoreCase) ||
                normalizedWl.Contains(inviterName, StringComparison.OrdinalIgnoreCase))
            {
                matchedEntry = wl; // Return the original whitelist entry
                return true;
            }
        }
        return false;
    }
}
