using System;
using System.Linq;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace FrenRider.Services;

public class PartyService
{
    private readonly Plugin plugin;
    private readonly IPluginLog log;
    private bool lastInParty;
    private string? lastInviterName;

    public PartyService(Plugin plugin, IPluginLog log)
    {
        this.plugin = plugin;
        this.log = log;
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
}
