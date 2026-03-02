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
        }

        lastInParty = inParty;

        CheckInviteDialog(config);
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
            lastInviterName = inviterName;
            // Auto-accept invite using /join command
            Plugin.CommandManager.ProcessCommand("/join");
            log.Information($"Auto-accepted party invite from whitelisted player: {inviterName}");
        }
    }

    private unsafe void CheckInviteDialog(CharacterConfig config)
    {
        if (config.InviteWhitelist.Count == 0)
            return;

        // Don't auto-accept if already in a party
        if (Plugin.PartyList.Length > 0)
            return;

        nint addonPtr = gameGui.GetAddonByName("SelectYesno", 1);
        if (addonPtr == nint.Zero)
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

        if (!IsWhitelisted(config, normalizedInviter))
            return;

        var now = Environment.TickCount64;
        if (lastPromptInviter == normalizedInviter && now - lastPromptHandled < 1000)
            return;

        AcceptInvite(addon);
        lastPromptHandled = now;
        lastPromptInviter = normalizedInviter;
        lastInviterName = normalizedInviter;
        log.Information($"Accepted SelectYesno invite from whitelisted player: {normalizedInviter}");
    }

    private unsafe void AcceptInvite(AddonSelectYesno* addon)
    {
        var args = stackalloc AtkValue[1];
        args[0].Type = FFXIVClientStructs.FFXIV.Component.GUI.ValueType.Int;
        args[0].Int = 0; // 0 = Yes button
        addon->AtkUnitBase.FireCallback(1, args);
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
