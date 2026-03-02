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

        // Check if this is a party invite message (type 2105 is party invite)
        if (type != (XivChatType)2105)
            return;

        // Don't auto-accept if already in a party
        if (Plugin.PartyList.Length > 0)
            return;

        // Extract sender name from SeString
        var senderName = sender.TextValue;
        var normalizedSender = senderName.Split('@')[0].Trim();
        
        // Check if sender is on whitelist
        if (config.InviteWhitelist.Any(wl => 
            normalizedSender.Equals(wl, StringComparison.OrdinalIgnoreCase)))
        {
            lastInviterName = normalizedSender;
            // Auto-accept invite using /join command
            Plugin.CommandManager.ProcessCommand("/join");
            log.Information($"Auto-accepted party invite from whitelisted player: {normalizedSender}");
        }
    }
}
