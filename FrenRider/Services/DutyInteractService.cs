using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace FrenRider.Services;

public class DutyInteractService
{
    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private const float FrenLostDistance = 50f;
    private const float InteractScanRange = 10f;
    private const float NudgeDistance = 3f;
    private const float InteractRange = 2.5f;

    private List<string> interactableKeywords = new();
    private string listFilePath = "";
    private DateTime lastFileCheck = DateTime.MinValue;
    private DateTime lastScanTime = DateTime.MinValue;
    private DateTime lastNudgeTime = DateTime.MinValue;
    private DateTime lastInteractTime = DateTime.MinValue;
    private uint? lastInteractedEntityId;
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
                // Close enough to interact
                if ((now - lastInteractTime).TotalSeconds > 3 && interactable.GameObjectId != lastInteractedEntityId)
                {
                    lastInteractTime = now;
                    lastInteractedEntityId = (uint)interactable.GameObjectId;
                    isNavigatingToInteractable = false;

                    Plugin.TargetManager.Target = interactable;
                    SendCommand("/interact");
                    Plugin.Log.Information($"[DutyInteract] Interacting with: {interactable.Name.TextValue}");
                }
            }
            else
            {
                // Navigate to it
                if (!isNavigatingToInteractable)
                {
                    isNavigatingToInteractable = true;
                    Plugin.Log.Information($"[DutyInteract] Navigating to: {interactable.Name.TextValue} ({dist:F1}y)");
                }
                var coords = FormatVector(interactable.Position);
                SendCommand($"/vnav moveto {coords}");
            }
        }
        else
        {
            isNavigatingToInteractable = false;
            StateDetail = "Scanning for interactables...";

            // Nothing found and pathing stopped - nudge forward
            if (pathingStopped && (now - lastNudgeTime).TotalSeconds > 5)
            {
                lastNudgeTime = now;
                NudgeForward();
            }
        }
    }

    private Dalamud.Game.ClientState.Objects.Types.IGameObject? FindNearestInteractable()
    {
        if (interactableKeywords.Count == 0) return null;

        var localPlayer = Plugin.ObjectTable.LocalPlayer;
        if (localPlayer == null) return null;

        Dalamud.Game.ClientState.Objects.Types.IGameObject? nearest = null;
        var nearestDist = float.MaxValue;

        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj == null) continue;
            if (obj.ObjectKind != ObjectKind.EventObj) continue;

            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name)) continue;

            var dist = Vector3.Distance(localPlayer.Position, obj.Position);
            if (dist > InteractScanRange) continue;

            // Check if name matches any keyword (partial, case-insensitive)
            var matches = interactableKeywords.Any(kw =>
                name.Contains(kw, StringComparison.OrdinalIgnoreCase));

            if (matches && dist < nearestDist)
            {
                nearest = obj;
                nearestDist = dist;
            }
        }

        return nearest;
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
        SendCommand($"/vnav moveto {coords}");
        StateDetail = "No interactables found, nudging forward...";
        Plugin.Log.Information($"[DutyInteract] Nudging forward to {coords}");
    }

    private void Reset()
    {
        if (IsActive)
        {
            isNavigatingToInteractable = false;
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
