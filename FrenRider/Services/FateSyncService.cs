using System;
using Dalamud.Game.ClientState.Conditions;

namespace FrenRider.Services;

public sealed class FateSyncService
{
    private const string LevelSyncCommand = "/levelsync on";
    private static readonly TimeSpan DeferLogInterval = TimeSpan.FromSeconds(10);

    private readonly Plugin plugin;
    private readonly ZoneService zoneService;
    private ushort lastSyncedFateId;
    private ushort lastDeferredFateId;
    private string lastDeferredReason = "";
    private DateTime lastDeferLogAt = DateTime.MinValue;

    public FateSyncService(Plugin plugin, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.AutoSyncFate)
            return;

        if (plugin.AutomationService.IsRepairFlowActive)
            return;

        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null)
            return;

        if (!zoneService.InFate || zoneService.CurrentFateId == 0)
        {
            ResetFateState();
            return;
        }

        var fateId = zoneService.CurrentFateId;
        if (lastSyncedFateId == fateId)
            return;

        if (IsBlocked(out var reason))
        {
            LogDeferred(fateId, reason);
            return;
        }

        if (!GameHelpers.SendChatCommand(LevelSyncCommand, "[FR][FATE-SYNC]"))
            return;

        lastSyncedFateId = fateId;
        lastDeferredFateId = 0;
        lastDeferredReason = "";
        Plugin.Log.Information($"[FR][FATE-SYNC] Sent {LevelSyncCommand} for fateId={fateId}");
    }

    private static bool IsBlocked(out string reason)
    {
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "between areas";
            return true;
        }

        if (GameHelpers.IsMountedOrRiding())
        {
            reason = "mounted or riding pillion";
            return true;
        }

        reason = "";
        return false;
    }

    private void LogDeferred(ushort fateId, string reason)
    {
        var now = DateTime.UtcNow;
        if (lastDeferredFateId == fateId
            && string.Equals(lastDeferredReason, reason, StringComparison.Ordinal)
            && now - lastDeferLogAt < DeferLogInterval)
        {
            return;
        }

        lastDeferredFateId = fateId;
        lastDeferredReason = reason;
        lastDeferLogAt = now;
        Plugin.Log.Information($"[FR][FATE-SYNC] Deferring fateId={fateId}: {reason}");
    }

    private void ResetFateState()
    {
        lastSyncedFateId = 0;
        lastDeferredFateId = 0;
        lastDeferredReason = "";
    }
}
