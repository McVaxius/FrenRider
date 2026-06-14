using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FrenRider.Models;

namespace FrenRider.Services;

public class DutyInteractService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private const float FrenLostDistance = 50f;
    private const float InteractScanRange = 50f;
    private const float MountAttemptDistance = 3f;
    private const float NudgeDistance = 3f;
    private const float InteractRange = 2.5f;
    private const float InteractableStuckDistance = 1f;
    private const double MountAttemptIntervalSeconds = 15;
    private const double InteractableRepathCheckSeconds = 5;
    private static readonly string[] BuiltInInteractableKeywords =
    {
        "chest",
        "coffer",
        "treasure",
        "treasure chest",
        "treasure coffer",
    };

    private List<string> interactableKeywords = new();
    private string listFilePath = "";
    private DateTime lastFileCheck = DateTime.MinValue;
    private DateTime lastScanTime = DateTime.MinValue;
    private DateTime lastNudgeTime = DateTime.MinValue;
    private DateTime lastInteractTime = DateTime.MinValue;
    private DateTime lastCommenceClickTime = DateTime.MinValue;
    private DateTime lastMountAttemptTime = DateTime.MinValue;
    private DateTime lastInteractableRepathCheckTime = DateTime.MinValue;
    private uint? lastInteractedEntityId;
    private ulong? navigatingInteractableGameObjectId;
    private Vector3 interactableRepathCheckPosition;
    private bool isNavigatingToInteractable;

    public string StateDetail { get; private set; } = "";
    public bool IsActive { get; private set; }

    public DutyInteractService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;

        var configDir = Plugin.PluginInterface.GetPluginConfigDirectory();
        listFilePath = Path.Combine(configDir, "interactables.txt");
        EnsureListFileExists();
        LoadKeywords();
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        if (!config.Enabled)
        {
            Reset();
            return;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            Reset();
            StateDetail = "ADS utility active";
            return;
        }

        if (plugin.AdsUtilityIpcService.ShouldSuppressGenericYesNo())
        {
            Reset();
            StateDetail = "ADS utility dialog active";
            return;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            StopInteractableNavigation("ADS authority active");
            Reset();
            StateDetail = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return;
        }

        // Handle ContentsFinderConfirm popup (duty commence dialog)
        if (GameHelpers.IsAddonVisible("ContentsFinderConfirm"))
        {
            var current = DateTime.UtcNow;
            if ((current - lastCommenceClickTime).TotalSeconds > 2) // Rate limit to prevent spam
            {
                lastCommenceClickTime = current;
                IsActive = true;
                StateDetail = "Clicking Commence on duty popup";
                Plugin.Log.Information("[DutyInteract] Clicking Commence on ContentsFinderConfirm");
                
                // Fire commence callback - typically callback index 8 = Commence button
                GameHelpers.FireAddonCallback("ContentsFinderConfirm", true, 8);
            }
            return;
        }

        // Only active in duties
        if (zoneService.CurrentZone != ZoneType.Duty && zoneService.CurrentZone != ZoneType.DeepDungeon)
        {
            Reset();
            return;
        }

        // Not while in combat
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            Reset();
            return;
        }

        // Not during loading screens
        if (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            Reset();
            return;
        }

        // Check fren state
        var fren = tracker.Fren;
        var frenTooFar = fren == null || !fren.IsFound || !fren.IsVisible || fren.Distance > FrenLostDistance;
        var pathingStopped = plugin.FollowService.State == FollowState.TooFar
                         || plugin.FollowService.State == FollowState.Idle;

        if (!frenTooFar && !pathingStopped)
        {
            Reset();
            return;
        }

        IsActive = true;

        // Reload keywords periodically (every 30s) so user edits are picked up
        var now = DateTime.UtcNow;
        if ((now - lastFileCheck).TotalSeconds > 30)
        {
            lastFileCheck = now;
            LoadKeywords();
        }

        // Scan every 2s
        if ((now - lastScanTime).TotalSeconds < 2) return;
        lastScanTime = now;

        // Scan for matching interactables within range
        var interactable = FindNearestInteractable();
        if (interactable != null)
        {
            var localPlayer = Plugin.ObjectTable.LocalPlayer;
            if (localPlayer == null) return;

            var dist = Vector3.Distance(localPlayer.Position, interactable.Position);
            StateDetail = $"Found: {interactable.Name.TextValue} ({dist:F1}y)";

            if (dist <= InteractRange)
            {
                if (isNavigatingToInteractable)
                    StopInteractableNavigation($"close enough to interact ({dist:F1}y <= {InteractRange:F1}y)");

                // Close enough to interact
                if ((now - lastInteractTime).TotalSeconds > 3)
                {
                    lastInteractTime = now;
                    lastInteractedEntityId = (uint)interactable.GameObjectId;

                    Plugin.Log.Information($"[DutyInteract] Interacting with: {interactable.Name.TextValue}");
                    GameHelpers.InteractWithObject(interactable);
                }
            }
            else
            {
                // Navigate to it
                if (TryMountTowardInteractable(config, interactable, dist, now))
                {
                    StateDetail = $"Mounting for: {interactable.Name.TextValue} ({dist:F1}y)";
                    return;
                }

                if (Plugin.Condition[ConditionFlag.Mounting71])
                {
                    StateDetail = $"Mounting for: {interactable.Name.TextValue} ({dist:F1}y)";
                    return;
                }

                NavigateToInteractable(interactable, localPlayer.Position, dist, now);
            }
        }
        else
        {
            isNavigatingToInteractable = false;
            StateDetail = "Scanning for interactables...";
            var boundByDuty = Plugin.Condition[ConditionFlag.BoundByDuty]
                              || Plugin.Condition[ConditionFlag.BoundByDuty56];

            // Nothing found and pathing stopped - nudge forward
            if (pathingStopped && (now - lastNudgeTime).TotalSeconds > 5)
            {
                lastNudgeTime = now;
                if (!boundByDuty)
                {
                    StateDetail = "No interactables found; not bound by duty";
                    Plugin.Log.Information("[DutyInteract] Skipping forward nudge because BoundByDuty is false.");
                    return;
                }

                NudgeForward();
            }
        }
    }

    private IGameObject? FindNearestInteractable()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return null;

        IGameObject? nearest = null;
        var nearestDist = float.MaxValue;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            if (!obj.IsTargetable) continue;
            if (obj.ObjectKind != ObjectKind.EventObj && obj.ObjectKind != ObjectKind.Treasure) continue;

            var name = obj.Name.TextValue;
            if (obj.ObjectKind != ObjectKind.Treasure && !MatchesInteractableKeyword(name)) continue;

            var dist = Vector3.Distance(localPlayer.Position, obj.Position);
            if (dist > InteractScanRange) continue;

            if (dist < nearestDist)
            {
                nearest = obj;
                nearestDist = dist;
            }
        }

        return nearest;
    }

    private bool MatchesInteractableKeyword(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var keyword in BuiltInInteractableKeywords)
        {
            if (name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return interactableKeywords.Any(kw =>
            name.Contains(kw, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryMountTowardInteractable(CharacterConfig config, IGameObject interactable, float distance, DateTime now)
    {
        if (distance <= MountAttemptDistance)
            return false;

        if ((now - lastMountAttemptTime).TotalSeconds < MountAttemptIntervalSeconds)
            return false;

        lastMountAttemptTime = now;

        if (!GameHelpers.CanUseMountActionNow(out var reason))
        {
            Plugin.Log.Debug($"[DutyInteract] Skipping mount for {interactable.Name.TextValue}: {reason}");
            return false;
        }

        var command = GetMountCommand(config.FoolFlier);
        Plugin.Log.Information($"[DutyInteract] Trying mount for far interactable {interactable.Name.TextValue} ({distance:F1}y): {command}");
        SendCommand(command);
        return true;
    }

    private void NavigateToInteractable(IGameObject interactable, Vector3 localPosition, float distance, DateTime now)
    {
        var gameObjectId = interactable.GameObjectId;
        if (isNavigatingToInteractable && navigatingInteractableGameObjectId != gameObjectId)
            StopInteractableNavigation($"switching target to {interactable.Name.TextValue}");

        if (!isNavigatingToInteractable)
        {
            isNavigatingToInteractable = true;
            navigatingInteractableGameObjectId = gameObjectId;
            interactableRepathCheckPosition = localPosition;
            lastInteractableRepathCheckTime = now;
            IssueInteractableNavCommand(interactable, $"initial navigation ({distance:F1}y)");
            return;
        }

        if ((now - lastInteractableRepathCheckTime).TotalSeconds < InteractableRepathCheckSeconds)
            return;

        var moved = Vector3.Distance(localPosition, interactableRepathCheckPosition);
        if (moved < InteractableStuckDistance)
        {
            var reason = $"stuck repath (moved {moved:F1}y < {InteractableStuckDistance:F1}y in {InteractableRepathCheckSeconds:F0}s)";
            StopInteractableNavigation(reason);
            isNavigatingToInteractable = true;
            navigatingInteractableGameObjectId = gameObjectId;
            interactableRepathCheckPosition = localPosition;
            lastInteractableRepathCheckTime = now;
            IssueInteractableNavCommand(interactable, reason);
            return;
        }

        interactableRepathCheckPosition = localPosition;
        lastInteractableRepathCheckTime = now;
    }

    private void IssueInteractableNavCommand(IGameObject interactable, string reason)
    {
        var coords = FormatVector(interactable.Position);
        var command = $"/vnav moveto {coords}";
        StateDetail = $"Moving to: {interactable.Name.TextValue}";
        Plugin.Log.Information($"[DutyInteract] Navigating to {interactable.Name.TextValue}: reason={reason}; cmd={command}");
        SendCommand(command);
    }

    private void StopInteractableNavigation(string reason)
    {
        if (!isNavigatingToInteractable)
            return;

        Plugin.Log.Information($"[DutyInteract] Stopping interactable navigation: {reason}");
        SendCommand("/vnavmesh stop");
        isNavigatingToInteractable = false;
        navigatingInteractableGameObjectId = null;
    }

    private static string GetMountCommand(string mountName)
    {
        if (string.Equals(mountName, "Mount Roulette", StringComparison.OrdinalIgnoreCase))
            return "/generalaction \"Mount Roulette\"";

        if (string.IsNullOrWhiteSpace(mountName))
            return "/mount \"Company Chocobo\"";

        return $"/mount \"{mountName}\"";
    }

    private void NudgeForward()
    {
        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return;

        // Move forward in the direction the character is facing
        var rotation = localPlayer.Rotation;
        var forward = new Vector3(
            (float)Math.Sin(rotation),
            0,
            (float)Math.Cos(rotation)
        );

        var target = localPlayer.Position + forward * NudgeDistance;
        var coords = FormatVector(target);
        isNavigatingToInteractable = true;
        navigatingInteractableGameObjectId = null;
        SendCommand($"/vnav moveto {coords}");
        StateDetail = "No interactables found, nudging forward...";
        Plugin.Log.Information($"[DutyInteract] Nudging forward to {coords}");
    }

    private void Reset()
    {
        if (IsActive)
        {
            isNavigatingToInteractable = false;
            navigatingInteractableGameObjectId = null;
            lastInteractedEntityId = null;
        }
        IsActive = false;
        StateDetail = "";
    }

    private void EnsureListFileExists()
    {
        if (File.Exists(listFilePath)) return;

        try
        {
            var defaultContent = string.Join(Environment.NewLine, new[]
            {
                "# Duty interactable keywords (one per line, partial match, case-insensitive)",
                "# Lines starting with # are comments",
                "# Edit this file and save - changes are picked up automatically every 30s",
                "magitek",
                "console",
                "door",
                "portal",
                "gate",
                "lever",
                "switch",
                "panel",
                "barrier",
            });
            File.WriteAllText(listFilePath, defaultContent);
            Plugin.Log.Information($"[DutyInteract] Created default interactables list: {listFilePath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyInteract] Failed to create interactables file: {ex.Message}");
        }
    }

    public void LoadKeywords()
    {
        try
        {
            if (!File.Exists(listFilePath))
            {
                EnsureListFileExists();
            }

            var lines = File.ReadAllLines(listFilePath);
            interactableKeywords = lines
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.StartsWith('#'))
                .ToList();

            Plugin.Log.Debug($"[DutyInteract] Loaded {interactableKeywords.Count} keywords from {listFilePath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"[DutyInteract] Failed to load keywords: {ex.Message}");
        }
    }

    public string GetListFilePath() => listFilePath;
    public int KeywordCount => interactableKeywords.Count;

    private static string FormatVector(Vector3 value)
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:F2} {1:F2} {2:F2}", value.X, value.Y, value.Z);
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
            Plugin.Log.Error($"[DutyInteract] Command failed [{command}]: {ex.Message}");
        }
    }
}
