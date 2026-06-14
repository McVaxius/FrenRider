using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FrenRider.Models;
using Lumina.Excel.Sheets;

namespace FrenRider.Services;

public enum FrenTeleportState
{
    Idle,
    Waiting,
    ReadingParty,
    Cooldown,
    Blocked,
    TeleportIssued,
}

public sealed class FrenTeleportService
{
    private const int MinDelaySeconds = 5;
    private const int MaxDelaySeconds = 300;
    private const long AttemptCooldownMs = 60000;
    private const long PartyWindowTimeoutMs = 5000;
    private const float RowYThreshold = 6f;

    private static readonly TimeSpan LifestreamCacheTtl = TimeSpan.FromSeconds(2);
    private static readonly uint[][] PartyMemberListTextNodePaths =
    {
        new uint[] { 26, 2, 41 },
        new uint[] { 26, 2, 42 },
        new uint[] { 26, 3, 42 },
        new uint[] { 26, 4, 42 },
        new uint[] { 26, 5, 42 },
        new uint[] { 26, 6, 42 },
        new uint[] { 26, 7, 42 },
        new uint[] { 26, 8, 42 },
        new uint[] { 26, 9, 42 },
    };

    private readonly Plugin plugin;
    private readonly FrenTracker tracker;
    private readonly ZoneService zoneService;

    private readonly Dictionary<string, LocationMatch> locationIndex = new(StringComparer.Ordinal);
    private bool locationIndexBuilt;

    private bool cachedLifestreamLoaded;
    private DateTime lifestreamCacheExpiresUtc = DateTime.MinValue;

    private long timerStartedMs;
    private long cooldownUntilMs;
    private long partyWindowOpenedMs;
    private bool openedPartyWindow;
    private string activeStateKey = "";
    private string lastLoggedStatus = "";

    public FrenTeleportState State { get; private set; } = FrenTeleportState.Idle;
    public string StatusText { get; private set; } = "Off";

    public FrenTeleportService(Plugin plugin, FrenTracker tracker, ZoneService zoneService)
    {
        this.plugin = plugin;
        this.tracker = tracker;
        this.zoneService = zoneService;
    }

    public void Update()
    {
        var config = plugin.ConfigManager.GetActiveConfig();
        var now = Environment.TickCount64;

        if (!Plugin.ClientState.IsLoggedIn)
        {
            ResetState("Not logged in", FrenTeleportState.Idle);
            return;
        }

        if (!config.Enabled)
        {
            ResetState("FrenRider disabled", FrenTeleportState.Idle);
            return;
        }

        if (!config.TryTeleportToFrenWhenOutOfZone)
        {
            ResetState("Off", FrenTeleportState.Idle);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.FrenName))
        {
            ResetState("No fren configured", FrenTeleportState.Blocked);
            return;
        }

        if (zoneService.ZoneChanged)
        {
            ResetState("Zone changed", FrenTeleportState.Idle);
            return;
        }

        if (IsBlocked(out var blockReason))
        {
            ResetState($"Blocked: {blockReason}", FrenTeleportState.Blocked);
            return;
        }

        var fren = tracker.Fren;
        if (fren == null || !fren.IsFound)
        {
            ResetState("Fren not found", FrenTeleportState.Idle);
            return;
        }

        if (!fren.InParty)
        {
            ResetState("Fren not in party", FrenTeleportState.Idle);
            return;
        }

        if (fren.IsVisible)
        {
            ResetState("Fren visible", FrenTeleportState.Idle);
            return;
        }

        var stateKey = BuildStateKey(config.FrenName, fren.Name);
        if (!string.Equals(activeStateKey, stateKey, StringComparison.Ordinal))
        {
            activeStateKey = stateKey;
            timerStartedMs = 0;
            cooldownUntilMs = 0;
            ClearPartyWindowOwnership();
        }

        if (cooldownUntilMs > now)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((cooldownUntilMs - now) / 1000.0));
            SetStatus(FrenTeleportState.Cooldown, $"Cooldown: retry in {remainingSeconds}s");
            return;
        }

        if (timerStartedMs == 0)
        {
            timerStartedMs = now;
            SetStatus(FrenTeleportState.Waiting, $"Fren out of zone; teleport in {GetDelaySeconds(config)}s");
            return;
        }

        var delayMs = GetDelaySeconds(config) * 1000L;
        var elapsedMs = now - timerStartedMs;
        if (elapsedMs < delayMs)
        {
            var remainingSeconds = Math.Max(1, (int)Math.Ceiling((delayMs - elapsedMs) / 1000.0));
            SetStatus(FrenTeleportState.Waiting, $"Fren out of zone; teleport in {remainingSeconds}s");
            return;
        }

        TryTeleport(config, now);
    }

    public void ResetForAreaTransition()
        => ResetState("Blocked: area transition", FrenTeleportState.Blocked);

    private void TryTeleport(CharacterConfig config, long now)
    {
        if (!IsLifestreamLoaded())
        {
            StartCooldown(now, "Lifestream not loaded");
            return;
        }

        var frenBaseName = GetBaseName(config.FrenName);
        if (!TryResolveFrenLocation(frenBaseName, now, out var location, out var locationReason, out var waiting))
        {
            if (waiting)
            {
                SetStatus(FrenTeleportState.ReadingParty, locationReason);
                return;
            }

            StartCooldown(now, locationReason);
            return;
        }

        SetStatus(FrenTeleportState.ReadingParty, $"Matched {location.DisplayName}", log: true);

        if (!TryPickAetheryte(location, out var candidate, out var candidateCount, out var pickReason))
        {
            StartCooldown(now, pickReason);
            return;
        }

        SetStatus(FrenTeleportState.ReadingParty, $"Found {candidateCount} aetherytes for {location.DisplayName}", log: true);

        var command = $"/li {candidate.Name}";
        if (!GameHelpers.SendChatCommand(command, "FrenTeleport"))
        {
            StartCooldown(now, $"Failed to send {command}");
            return;
        }

        timerStartedMs = 0;
        cooldownUntilMs = now + AttemptCooldownMs;
        SetStatus(
            FrenTeleportState.TeleportIssued,
            $"Sent {command}; matched {location.DisplayName}; found {candidateCount} aetherytes{ReleasePartyWindowStatusSuffix()}",
            log: true);
    }

    private bool IsBlocked(out string reason)
    {
        if (Plugin.Condition[ConditionFlag.InCombat])
        {
            reason = "in combat";
            return true;
        }

        if (Plugin.Condition[ConditionFlag.BoundByDuty] ||
            Plugin.Condition[ConditionFlag.BoundByDuty56])
        {
            reason = "in duty";
            return true;
        }

        if (Plugin.Condition[ConditionFlag.BetweenAreas] ||
            Plugin.Condition[ConditionFlag.BetweenAreas51])
        {
            reason = "area transition";
            return true;
        }

        if (plugin.AdsIntegrationService.ShouldPauseDutySystems)
        {
            reason = plugin.AdsIntegrationService.IsHandoffPending
                ? "ADS handoff pending"
                : "ADS active";
            return true;
        }

        if (plugin.AutomationService.IsUtilityGateActive)
        {
            reason = "ADS utility active";
            return true;
        }

        reason = "";
        return false;
    }

    private bool TryResolveFrenLocation(
        string frenBaseName,
        long now,
        out LocationMatch location,
        out string reason,
        out bool waiting)
    {
        location = null!;
        waiting = false;

        if (!GameHelpers.IsAddonVisible("PartyMemberList"))
        {
            if (!openedPartyWindow)
            {
                if (!GameHelpers.SendChatCommand("/partycmd", "FrenTeleport"))
                {
                    reason = "Failed to open /partycmd";
                    return false;
                }

                openedPartyWindow = true;
                partyWindowOpenedMs = now;
                waiting = true;
                reason = "Opening party window";
                return false;
            }

            if (now - partyWindowOpenedMs < PartyWindowTimeoutMs)
            {
                waiting = true;
                reason = "Waiting for party window";
                return false;
            }

            reason = "Party window did not open (addon visible=false, opened by FrenRider=true)";
            ClearPartyWindowOwnership();
            return false;
        }

        SetStatus(FrenTeleportState.ReadingParty, "Reading party window", log: true);

        if (TryReadPartyWindowLocation(frenBaseName, out location, out reason))
        {
            return true;
        }

        if (openedPartyWindow && now - partyWindowOpenedMs < PartyWindowTimeoutMs)
        {
            waiting = true;
            reason = $"Reading party window ({reason})";
            return false;
        }

        return false;
    }

    private unsafe bool TryReadPartyWindowLocation(string frenBaseName, out LocationMatch location, out string reason)
    {
        location = null!;
        reason = "";

        try
        {
            var unitManager = RaptureAtkUnitManager.Instance();
            if (unitManager == null)
            {
                reason = "UI manager unavailable";
                return false;
            }

            var addon = unitManager->GetAddonByName("PartyMemberList");
            if (addon == null || !addon->IsVisible)
            {
                reason = "Party window not visible";
                return false;
            }

            var rows = ReadTextRows(addon, out var diagnostics);
            if (rows.Count == 0)
            {
                reason = $"Party window had no readable rows ({diagnostics.Format()})";
                return false;
            }

            var normalizedFren = NormalizeText(frenBaseName);
            foreach (var row in rows)
            {
                if (!ContainsNormalized(row.NormalizedText, normalizedFren))
                    continue;

                diagnostics.MatchedRowExcerpt = BuildExcerpt(row.Text);
                if (TryMatchLocation(row.NormalizedText, out location))
                {
                    diagnostics.MatchedLocation = location.DisplayName;
                    reason = $"Matched {location.DisplayName} ({diagnostics.Format()})";
                    Plugin.Log.Debug($"[FrenTeleport] Party row for {frenBaseName} resolved to {location.DisplayName}: {row.Text}");
                    return true;
                }

                reason = $"Party row found for {frenBaseName}, but no zone name matched ({diagnostics.Format()})";
                return false;
            }

            reason = $"No party row found for {frenBaseName} ({diagnostics.Format()})";
            return false;
        }
        catch (Exception ex)
        {
            reason = $"Party window read failed: {ex.Message}";
            Plugin.Log.Warning(ex, "[FrenTeleport] PartyMemberList read failed");
            return false;
        }
    }

    private unsafe List<TextRow> ReadTextRows(AtkUnitBase* addon, out PartyWindowReadDiagnostics diagnostics)
    {
        var nodes = new List<TextNodeInfo>();
        var collected = new HashSet<nint>();
        diagnostics = new PartyWindowReadDiagnostics
        {
            AddonVisible = addon != null && addon->IsVisible,
        };

        foreach (var nodePath in PartyMemberListTextNodePaths)
        {
            var node = FindNestedPartyMemberListNode(addon, nodePath);
            if (TryAddTextNode(node, FormatNodePath(nodePath), nodes, collected))
                diagnostics.DirectPathHits++;
        }

        var recursiveStartCount = nodes.Count;
        var visited = new HashSet<nint>();
        var nodeCount = addon->UldManager.NodeListCount;
        for (var i = 0; i < nodeCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            if (node == null)
                continue;

            CollectTextNodes(node, nodes, collected, visited);
        }

        if (addon->UldManager.RootNode != null)
            CollectTextNodes(addon->UldManager.RootNode, nodes, collected, visited);

        diagnostics.RecursiveTextNodes = nodes.Count - recursiveStartCount;

        nodes.Sort(static (left, right) =>
        {
            var yCompare = left.Y.CompareTo(right.Y);
            return yCompare != 0 ? yCompare : left.X.CompareTo(right.X);
        });

        var rows = new List<TextRow>();
        foreach (var node in nodes)
        {
            var row = rows.FirstOrDefault(existing => Math.Abs(existing.Y - node.Y) <= RowYThreshold);
            if (row == null)
            {
                row = new TextRow(node.Y);
                rows.Add(row);
            }

            row.Nodes.Add(node);
        }

        foreach (var row in rows)
            row.Nodes.Sort(static (left, right) => left.X.CompareTo(right.X));

        diagnostics.RowCount = rows.Count;
        diagnostics.RowExcerpt = BuildRowsExcerpt(rows);
        return rows;
    }

    private unsafe bool TryAddTextNode(
        AtkResNode* node,
        string source,
        List<TextNodeInfo> nodes,
        HashSet<nint> collected)
    {
        if (node == null || node->GetNodeType() != NodeType.Text || !node->IsVisible())
            return false;

        var nodeAddress = (nint)node;
        if (collected.Contains(nodeAddress))
            return false;

        var textNode = node->GetAsAtkTextNode();
        if (textNode == null)
            return false;

        var text = ReadTextNode(textNode);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        collected.Add(nodeAddress);
        nodes.Add(new TextNodeInfo(text, textNode->ScreenX, textNode->ScreenY, nodeAddress, source));
        return true;
    }

    private unsafe void CollectTextNodes(
        AtkResNode* node,
        List<TextNodeInfo> nodes,
        HashSet<nint> collected,
        HashSet<nint> visited)
    {
        if (node == null)
            return;

        var nodeAddress = (nint)node;
        if (!visited.Add(nodeAddress))
            return;

        TryAddTextNode(node, "recursive", nodes, collected);

        if ((int)node->Type >= 1000)
        {
            var componentNode = (AtkComponentNode*)node;
            if (componentNode->Component != null && componentNode->Component->UldManager.RootNode != null)
                CollectTextNodes(componentNode->Component->UldManager.RootNode, nodes, collected, visited);
        }

        if (node->ChildNode != null)
            CollectTextNodes(node->ChildNode, nodes, collected, visited);

        if (node->PrevSiblingNode != null)
            CollectTextNodes(node->PrevSiblingNode, nodes, collected, visited);
    }

    private unsafe AtkResNode* FindNestedPartyMemberListNode(AtkUnitBase* addon, uint[] nodePath)
    {
        if (addon == null || nodePath.Length == 0)
            return null;

        var currentNode = addon->GetNodeById(nodePath[0]);
        for (var i = 1; currentNode != null && i < nodePath.Length; i++)
            currentNode = FindDescendantNodeById(currentNode, nodePath[i]);

        return currentNode;
    }

    private unsafe AtkResNode* FindDescendantNodeById(AtkResNode* parentNode, uint targetNodeId)
    {
        if (parentNode == null)
            return null;

        if ((int)parentNode->Type >= 1000)
        {
            var componentNode = (AtkComponentNode*)parentNode;
            if (componentNode->Component != null)
            {
                var directMatch = componentNode->Component->UldManager.SearchNodeById(targetNodeId);
                if (directMatch != null)
                    return directMatch;

                return FindNodeByIdInChain(componentNode->Component->UldManager.RootNode, targetNodeId);
            }
        }

        return FindNodeByIdInChain(parentNode->ChildNode, targetNodeId);
    }

    private unsafe AtkResNode* FindNodeByIdInChain(AtkResNode* startNode, uint targetNodeId)
    {
        var node = startNode;
        while (node != null)
        {
            if (node->NodeId == targetNodeId)
                return node;

            var descendantMatch = FindDescendantNodeById(node, targetNodeId);
            if (descendantMatch != null)
                return descendantMatch;

            node = node->PrevSiblingNode;
        }

        return null;
    }

    private static unsafe string ReadTextNode(AtkTextNode* textNode)
    {
        var textPtr = textNode->NodeText.StringPtr;
        if (!textPtr.HasValue)
            return "";

        var seString = MemoryHelper.ReadSeStringNullTerminated(new IntPtr(textPtr));
        return seString.TextValue.Trim();
    }

    private bool TryMatchLocation(string normalizedRow, out LocationMatch location)
    {
        EnsureLocationIndex();

        location = null!;
        foreach (var candidate in locationIndex.Values.OrderByDescending(match => match.NormalizedName.Length))
        {
            if (!ContainsNormalized(normalizedRow, candidate.NormalizedName))
                continue;

            location = candidate;
            return true;
        }

        return false;
    }

    private unsafe bool TryPickAetheryte(
        LocationMatch location,
        out AetheryteCandidate candidate,
        out int candidateCount,
        out string reason)
    {
        candidate = default;
        candidateCount = 0;
        reason = "";

        try
        {
            var telepo = Telepo.Instance();
            if (telepo == null)
            {
                reason = $"Telepo unavailable for {location.DisplayName} (candidate count=0)";
                return false;
            }

            telepo->UpdateAetheryteList();
            var teleportList = telepo->TeleportList.AsSpan();
            Plugin.Log.Debug($"[FrenTeleport] Teleport list entries={teleportList.Length} for {location.DisplayName}");

            var aetheryteSheet = Plugin.DataManager.GetExcelSheet<Aetheryte>();
            if (aetheryteSheet == null)
            {
                reason = $"Aetheryte sheet unavailable for {location.DisplayName} (candidate count=0)";
                return false;
            }

            var candidates = new List<AetheryteCandidate>();
            var diagnostics = new AetheryteCandidateDiagnostics(teleportList.Length);
            foreach (var info in teleportList)
            {
                if (info.AetheryteId == 0)
                {
                    diagnostics.ZeroId++;
                    continue;
                }

                if (IsHousingTeleport(info, out var housingReason))
                {
                    if (housingReason == HousingTeleportReason.ApartmentOrSharedHouse)
                        diagnostics.ApartmentOrSharedHouse++;
                    else
                        diagnostics.KnownHousingId++;

                    continue;
                }

                if (!aetheryteSheet.TryGetRow(info.AetheryteId, out var aetheryte))
                {
                    diagnostics.MissingLuminaRow++;
                    continue;
                }

                if (!aetheryte.IsAetheryte || aetheryte.Invisible)
                {
                    diagnostics.NonRealOrInvisible++;
                    continue;
                }

                var territoryId = (uint)info.TerritoryId;
                if (!location.TerritoryIds.Contains(territoryId))
                {
                    diagnostics.TerritoryMismatch++;
                    continue;
                }

                diagnostics.MatchedTerritoryIds.Add(territoryId);

                var name = aetheryte.Singular.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name))
                    name = aetheryte.PlaceName.ValueNullable?.Name.ToString().Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    diagnostics.MissingName++;
                    continue;
                }

                candidates.Add(new AetheryteCandidate(info.AetheryteId, info.SubIndex, territoryId, name));
            }

            Plugin.Log.Debug(
                $"[FrenTeleport] Aetheryte candidates for {location.DisplayName}: " +
                $"candidate count={candidates.Count}, {diagnostics.Format(location.TerritoryIds)}");

            if (candidates.Count == 0)
            {
                reason =
                    $"No unlocked Lifestream aetherytes matched {location.DisplayName} " +
                    $"(candidate count=0; {diagnostics.Format(location.TerritoryIds)})";
                return false;
            }

            candidateCount = candidates.Count;
            candidate = candidates[Random.Shared.Next(candidates.Count)];
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Teleport list read failed for {location.DisplayName}: {ex.Message} (candidate count=0)";
            Plugin.Log.Warning(ex, "[FrenTeleport] Failed to pick unlocked aetheryte");
            return false;
        }
    }

    private static bool IsHousingTeleport(TeleportInfo info, out HousingTeleportReason reason)
    {
        if (info.IsApartment || info.IsSharedHouse)
        {
            reason = HousingTeleportReason.ApartmentOrSharedHouse;
            return true;
        }

        if (info.AetheryteId is 56 or 57 or 58 or 59 or 60 or 61 or 96 or 97 or 164 or 165)
        {
            reason = HousingTeleportReason.KnownHousingId;
            return true;
        }

        reason = default;
        return false;
    }

    private bool IsLifestreamLoaded(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now < lifestreamCacheExpiresUtc)
            return cachedLifestreamLoaded;

        try
        {
            cachedLifestreamLoaded = Plugin.PluginInterface.InstalledPlugins.Any(installedPlugin =>
                installedPlugin.IsLoaded
                && (string.Equals(installedPlugin.InternalName, "Lifestream", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(installedPlugin.Name, "Lifestream", StringComparison.OrdinalIgnoreCase)
                    || installedPlugin.InternalName.Contains("Lifestream", StringComparison.OrdinalIgnoreCase)
                    || installedPlugin.Name.Contains("Lifestream", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex)
        {
            cachedLifestreamLoaded = false;
            Plugin.Log.Debug($"[FrenTeleport] Failed to inspect Lifestream availability: {ex.Message}");
        }

        lifestreamCacheExpiresUtc = now + LifestreamCacheTtl;
        return cachedLifestreamLoaded;
    }

    private void EnsureLocationIndex()
    {
        if (locationIndexBuilt)
            return;

        locationIndexBuilt = true;

        try
        {
            var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
            if (territorySheet == null)
                return;

            foreach (var territory in territorySheet)
            {
                if (territory.RowId == 0)
                    continue;

                AddLocationName(territory.PlaceName.ValueNullable?.Name.ToString(), territory.RowId);
                AddLocationName(territory.PlaceNameZone.ValueNullable?.Name.ToString(), territory.RowId);
                AddLocationName(territory.PlaceNameRegion.ValueNullable?.Name.ToString(), territory.RowId);
                AddLocationName(territory.Name.ToString(), territory.RowId);
            }

            Plugin.Log.Debug($"[FrenTeleport] Built location index with {locationIndex.Count} names");
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FrenTeleport] Failed to build territory/place-name index");
        }
    }

    private void AddLocationName(string? displayName, uint territoryId)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return;

        var normalized = NormalizeText(displayName);
        if (normalized.Length <= 3 || normalized.All(char.IsDigit))
            return;

        if (!locationIndex.TryGetValue(normalized, out var match))
        {
            match = new LocationMatch(displayName.Trim(), normalized);
            locationIndex[normalized] = match;
        }

        match.TerritoryIds.Add(territoryId);
    }

    private void StartCooldown(long now, string reason)
    {
        timerStartedMs = 0;
        cooldownUntilMs = now + AttemptCooldownMs;
        SetStatus(FrenTeleportState.Cooldown, $"{reason}{ReleasePartyWindowStatusSuffix()}; retry in {AttemptCooldownMs / 1000}s", log: true);
    }

    private void ResetState(string status, FrenTeleportState state)
    {
        var partyWindowSuffix = ReleasePartyWindowStatusSuffix();
        timerStartedMs = 0;
        cooldownUntilMs = 0;
        partyWindowOpenedMs = 0;
        activeStateKey = "";
        SetStatus(state, $"{status}{partyWindowSuffix}");
    }

    private string ReleasePartyWindowStatusSuffix()
    {
        if (!openedPartyWindow)
            return "";

        var leftOpen = GameHelpers.IsAddonVisible("PartyMemberList");
        ClearPartyWindowOwnership();
        return leftOpen ? "; Party window left open" : "";
    }

    private void ClearPartyWindowOwnership()
    {
        openedPartyWindow = false;
        partyWindowOpenedMs = 0;
    }

    private void SetStatus(FrenTeleportState state, string text, bool log = false)
    {
        State = state;
        StatusText = text;

        if (!log || string.Equals(lastLoggedStatus, text, StringComparison.Ordinal))
            return;

        lastLoggedStatus = text;
        Plugin.Log.Information($"[FrenTeleport] {text}");
    }

    private static int GetDelaySeconds(CharacterConfig config)
        => Math.Clamp(config.TeleportToFrenDelaySeconds, MinDelaySeconds, MaxDelaySeconds);

    private static string BuildStateKey(string configuredFrenName, string trackedFrenName)
        => $"{NormalizeText(configuredFrenName)}|{NormalizeText(trackedFrenName)}|{Plugin.ClientState.TerritoryType}";

    private static string GetBaseName(string name)
        => name.Split('@')[0].Trim();

    private static string FormatNodePath(uint[] nodePath)
        => $"path [{string.Join(",", nodePath)}]";

    private static string BuildRowsExcerpt(List<TextRow> rows)
        => BuildExcerpt(string.Join(" | ", rows.Take(3).Select(row => row.Text)));

    private static string BuildExcerpt(string text, int maxLength = 180)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var normalizedWhitespace = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalizedWhitespace.Length <= maxLength
            ? normalizedWhitespace
            : normalizedWhitespace[..maxLength] + "...";
    }

    private static bool ContainsNormalized(string haystack, string needle)
    {
        if (string.IsNullOrWhiteSpace(haystack) || string.IsNullOrWhiteSpace(needle))
            return false;

        var paddedHaystack = $" {haystack} ";
        var paddedNeedle = $" {needle} ";
        return paddedHaystack.Contains(paddedNeedle, StringComparison.Ordinal) ||
               haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        var sb = new StringBuilder(text.Length);
        var lastWasSpace = true;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                sb.Append(' ');
                lastWasSpace = true;
            }
        }

        return sb.ToString().Trim();
    }

    private sealed class LocationMatch
    {
        public LocationMatch(string displayName, string normalizedName)
        {
            DisplayName = displayName;
            NormalizedName = normalizedName;
        }

        public string DisplayName { get; }
        public string NormalizedName { get; }
        public HashSet<uint> TerritoryIds { get; } = new();
    }

    private sealed class PartyWindowReadDiagnostics
    {
        public bool AddonVisible { get; init; }
        public int DirectPathHits { get; set; }
        public int RecursiveTextNodes { get; set; }
        public int RowCount { get; set; }
        public string RowExcerpt { get; set; } = "";
        public string MatchedRowExcerpt { get; set; } = "";
        public string MatchedLocation { get; set; } = "";

        public string Format()
        {
            var parts = new List<string>
            {
                $"addon visible={AddonVisible.ToString().ToLowerInvariant()}",
                $"direct path hits={DirectPathHits}",
                $"recursive text nodes={RecursiveTextNodes}",
                $"rows={RowCount}",
            };

            if (!string.IsNullOrWhiteSpace(MatchedRowExcerpt))
                parts.Add($"matched row=\"{MatchedRowExcerpt}\"");

            if (!string.IsNullOrWhiteSpace(MatchedLocation))
                parts.Add($"matched location={MatchedLocation}");

            if (!string.IsNullOrWhiteSpace(RowExcerpt))
                parts.Add($"row excerpt=\"{RowExcerpt}\"");

            return string.Join(", ", parts);
        }
    }

    private sealed class AetheryteCandidateDiagnostics
    {
        public AetheryteCandidateDiagnostics(int totalEntries)
        {
            TotalEntries = totalEntries;
        }

        public int TotalEntries { get; }
        public int ZeroId { get; set; }
        public int ApartmentOrSharedHouse { get; set; }
        public int KnownHousingId { get; set; }
        public int MissingLuminaRow { get; set; }
        public int NonRealOrInvisible { get; set; }
        public int TerritoryMismatch { get; set; }
        public int MissingName { get; set; }
        public HashSet<uint> MatchedTerritoryIds { get; } = new();

        public string Format(IEnumerable<uint> targetTerritoryIds)
            => $"teleport list entries={TotalEntries}, " +
               $"target territory IDs={FormatTerritoryIds(targetTerritoryIds)}, " +
               $"matched territory IDs={FormatTerritoryIds(MatchedTerritoryIds)}, " +
               $"rejected: zero ID={ZeroId}, apartment/shared house={ApartmentOrSharedHouse}, " +
               $"known housing ID={KnownHousingId}, missing Lumina row={MissingLuminaRow}, " +
               $"non-real/invisible={NonRealOrInvisible}, territory mismatch={TerritoryMismatch}, missing name={MissingName}";

        private static string FormatTerritoryIds(IEnumerable<uint> territoryIds)
        {
            var ids = territoryIds.OrderBy(id => id).ToArray();
            return ids.Length == 0 ? "none" : string.Join("/", ids);
        }
    }

    private enum HousingTeleportReason
    {
        ApartmentOrSharedHouse,
        KnownHousingId,
    }

    private sealed record TextNodeInfo(string Text, float X, float Y, nint Address, string Source);

    private sealed class TextRow
    {
        public TextRow(float y)
        {
            Y = y;
        }

        public float Y { get; }
        public List<TextNodeInfo> Nodes { get; } = new();
        public string Text => string.Join(" ", Nodes.Select(node => node.Text));
        public string NormalizedText => NormalizeText(Text);
    }

    private readonly record struct AetheryteCandidate(uint AetheryteId, byte SubIndex, uint TerritoryId, string Name);
}
