using System;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;

namespace FrenRider.Services;

/// <summary>
/// Handles automatic exit behaviour based on configurable rules:
/// 1. Exit if an exit object (Cairn of Return, etc.) exists in the zone
/// 2. Exit N seconds after duty ends (via IDutyState.DutyCompleted hook)
/// 3. Leave duty when all other party members have left
/// </summary>
public class ExitBehaviourService : IDisposable
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    // Duty completion tracking
    private bool dutyCompleted;
    private DateTime dutyCompletedTime;
    private bool dutyLeaveIssued;

    // Known exit object names (from LootGoblin treasure dungeon patterns)
    private static readonly string[] ExitObjectNames =
    {
        "Cairn of Return",
        "Exit",
    };

    public ExitBehaviourService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;

        // Hook DutyCompleted event
        Plugin.DutyState.DutyCompleted += OnDutyCompleted;
    }

    public void Dispose()
    {
        Plugin.DutyState.DutyCompleted -= OnDutyCompleted;
    }

    private void OnDutyCompleted(object? sender, ushort territoryId)
    {
        dutyCompleted = true;
        dutyCompletedTime = DateTime.Now;
        dutyLeaveIssued = false;
        Plugin.Log.Information($"[ExitBehaviour] Duty completed in territory {territoryId}");
    }

    /// <summary>
    /// Called every framework tick. Evaluates exit rules and takes action if needed.
    /// </summary>
    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled) return;

        var inDuty = Plugin.Condition[ConditionFlag.BoundByDuty] ||
                     Plugin.Condition[ConditionFlag.BoundByDuty56];

        // Reset duty completion state when no longer in duty
        if (!inDuty)
        {
            if (dutyCompleted)
            {
                dutyCompleted = false;
                dutyLeaveIssued = false;
                Plugin.Log.Debug("[ExitBehaviour] No longer in duty - reset completion state");
            }
            return;
        }

        // Don't try to leave during loading screens
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return;

        // Don't try to leave during combat
        if (Plugin.Condition[ConditionFlag.InCombat])
            return;

        // Rule 1: Exit if exit object exists
        if (config.ExitIfExitExists)
        {
            CheckExitObject();
        }

        // Rule 2: Exit N seconds after duty ends
        if (config.ExitAfterDutyEnds && dutyCompleted && !dutyLeaveIssued)
        {
            var elapsed = (DateTime.Now - dutyCompletedTime).TotalSeconds;
            if (elapsed >= config.ExitAfterDutySeconds)
            {
                Plugin.Log.Information($"[ExitBehaviour] Leaving duty - {config.ExitAfterDutySeconds}s elapsed since duty completed");
                LeaveDuty();
                dutyLeaveIssued = true;
            }
        }

        // Rule 3: Leave when all others have left
        if (config.LeaveWhenAllLeft)
        {
            CheckPartyEmpty();
        }
    }

    private void CheckExitObject()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            if (obj.ObjectKind != ObjectKind.EventObj) continue;

            var name = obj.Name.ToString();
            if (string.IsNullOrEmpty(name)) continue;

            foreach (var exitName in ExitObjectNames)
            {
                if (name.Contains(exitName, StringComparison.OrdinalIgnoreCase))
                {
                    var dist = Vector3.Distance(localPlayer.Position, obj.Position);
                    if (dist < 50f)
                    {
                        Plugin.Log.Information($"[ExitBehaviour] Found exit object '{name}' at {dist:F1}y - interacting");
                        // Target and interact with exit object
                        Plugin.TargetManager.Target = obj;
                        SendCommand("/interact");
                        return;
                    }
                }
            }
        }
    }

    private void CheckPartyEmpty()
    {
        // Only applies when in a duty
        var partyCount = Plugin.PartyList.Length;

        // PartyList.Length == 0 when solo (not in party at all)
        // PartyList.Length == 1 when you're the only one left in a party
        if (partyCount <= 1)
        {
            Plugin.Log.Information($"[ExitBehaviour] Party empty (count={partyCount}) - leaving duty");
            LeaveDuty();
        }
    }

    private void LeaveDuty()
    {
        SendCommand("/leaveDuty");
    }

    private static unsafe void SendCommand(string command)
    {
        try
        {
            if (Plugin.CommandManager.ProcessCommand(command))
                return;

            var uiModule = UIModule.Instance();
            if (uiModule == null) return;

            var bytes = Encoding.UTF8.GetBytes(command);
            var utf8String = Utf8String.FromSequence(bytes);
            uiModule->ProcessChatBoxEntry(utf8String, nint.Zero);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[ExitBehaviour] Command failed [{command}]: {ex.Message}");
        }
    }
}
