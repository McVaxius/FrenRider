using System;
using FrenRider.Models;

namespace FrenRider.Services;

/// <summary>
/// Stub for exit behaviour logic.
/// Rules will be defined by the user later - can borrow patterns from LootGoblin.
/// 
/// Potential exit rules (TBD):
/// - Exit duty when all party members leave
/// - Exit duty when duty ends
/// - Return to town after X minutes idle
/// - Logout after X minutes with no fren
/// </summary>
public class ExitBehaviourService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    public ExitBehaviourService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    /// <summary>
    /// Called every framework tick. Evaluates exit rules and takes action if needed.
    /// Currently a stub - rules to be implemented when user provides them.
    /// </summary>
    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled) return;

        // TODO: Implement exit behaviour rules here
        // Examples from LootGoblin patterns:
        // - Check if fren left the zone/party
        // - Check idle timeout
        // - Check duty completion
        // - Auto-leave duty when conditions met
    }
}
