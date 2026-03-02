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

    private static readonly Regex InvitePromptRegex = new("Join (?<name>.+?)'s party\\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
        if (config.InviteWhitelist.Any(wl =>
            inviterName.Equals(wl, StringComparison.OrdinalIgnoreCase)))
        {
            var normalized = NormalizeName(inviterName);
            lastInviterName = normalized;
            log.Information($"Whitelisted party invite detected from: {normalized}. Waiting for SelectYesno dialog to accept.");
        }
    }

    private unsafe void CheckInviteDialog(CharacterConfig config)
    {
        if (config.InviteWhitelist.Count == 0)
            return;

        // Don't auto-accept if already in a party
        if (Plugin.PartyList.Length > 0)
            return;

        // Try multiple addon indices like AutoRetainer does
        for (int i = 1; i < 100; i++)
        {
            nint addonPtr = gameGui.GetAddonByName("SelectYesno", i);
            if (addonPtr == nint.Zero)
                continue;

            var addon = (AddonSelectYesno*)addonPtr;
            if (!addon->AtkUnitBase.IsVisible)
                continue;

            var promptNode = addon->PromptText;
            if (promptNode == null)
                continue;

            var textPtr = promptNode->NodeText.StringPtr;
            if (textPtr == null)
                continue;

            var promptSe = MemoryHelper.ReadSeStringNullTerminated(new IntPtr(textPtr));
            var prompt = promptSe.TextValue;
            if (string.IsNullOrEmpty(prompt))
                continue;

            var match = InvitePromptRegex.Match(prompt.Trim());
            if (!match.Success)
                continue;

            var inviterRaw = match.Groups["name"].Value;
            var normalizedInviter = NormalizeName(inviterRaw);
            if (string.IsNullOrEmpty(normalizedInviter))
                continue;

            if (!IsWhitelisted(config, normalizedInviter))
                continue;

            var now = Environment.TickCount64;

            if (lastPromptInviter != normalizedInviter)
            {
                lastPromptInviter = normalizedInviter;
                lastPromptHandled = 0;
                callbackAttempts = 0;
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
                lastInviterName = normalizedInviter;
                log.Information($"Issued accept attempt #{callbackAttempts} for whitelisted invite from: {normalizedInviter}");
            }
            return; // Found matching addon, exit loop
        }
    }

    private unsafe bool AcceptInvite(AddonSelectYesno* addon, string inviterName, int attempt)
    {
        try
        {
            var button = addon->YesButton;
            if (button == null)
            {
                log.Warning($"Invite accept attempt #{attempt} for {inviterName}: Yes button is null");
                return false;
            }

            if (!button->IsEnabled)
            {
                log.Warning($"Invite accept attempt #{attempt} for {inviterName}: Yes button is disabled");
                return false;
            }

            // Use ClickLib pattern like AutoRetainer
            var args = stackalloc AtkValue[1];
            args[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
            args[0].Int = 0;
            addon->AtkUnitBase.FireCallback(1, args, true);
            log.Information($"Invite accept attempt #{attempt} for {inviterName}: FireCallback sent");

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
        var tokens = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2)
        {
            trimmed = $"{tokens[0]} {tokens[1]}";
        }

        return ConfigManager.FixNameCapitalization(trimmed);
    }

    private static bool IsWhitelisted(CharacterConfig config, string normalizedName)
    {
        return config.InviteWhitelist.Any(wl =>
            NormalizeName(wl).Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
    }
}
