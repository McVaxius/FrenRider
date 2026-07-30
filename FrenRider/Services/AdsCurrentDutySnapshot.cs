using System;
using System.Text.Json;
using FrenRider.Models;

namespace FrenRider.Services;

public sealed record AdsCurrentDutySnapshot(
    string DutyName,
    uint TerritoryTypeId,
    uint ContentFinderConditionId,
    AdsDutyCategory Category,
    string SupportLevel,
    string ClearanceStatus,
    int ClearanceLevel,
    DateTime CapturedAtUtc)
{
    public bool MatchesIdentity(uint territoryTypeId, uint contentFinderConditionId)
        => TerritoryTypeId == territoryTypeId
           && ContentFinderConditionId == contentFinderConditionId;

    public static bool TryParseStatusJson(
        string json,
        DateTime capturedAtUtc,
        out AdsCurrentDutySnapshot? snapshot,
        out string failure)
    {
        snapshot = null;
        failure = string.Empty;
        if (string.IsNullOrWhiteSpace(json))
        {
            failure = "ADS.GetStatusJson returned an empty payload";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!TryReadBoolean(root, "inInstancedDuty", out var inInstancedDuty)
                || !inInstancedDuty)
            {
                failure = "ADS status does not identify an active instanced duty";
                return false;
            }

            if (!TryReadBoolean(root, "hasCatalogMetadata", out var hasCatalogMetadata)
                || !hasCatalogMetadata)
            {
                failure = "ADS current duty has no catalog metadata";
                return false;
            }

            if (!TryReadString(root, "duty", out var dutyName))
            {
                failure = "ADS current-duty catalog row has no duty name";
                return false;
            }

            if (!TryReadUInt32(root, "territoryTypeId", out var territoryTypeId)
                || territoryTypeId == 0)
            {
                failure = "ADS current-duty catalog row has no territory identity";
                return false;
            }

            if (!TryReadUInt32(root, "contentFinderConditionId", out var contentFinderConditionId))
            {
                failure = "ADS current-duty catalog row has no CFC identity";
                return false;
            }

            if (!TryReadString(root, "dutyCategory", out var categoryName)
                || !TryParseCategory(categoryName, out var category))
            {
                failure = $"ADS current-duty catalog row has unsupported category '{categoryName}'";
                return false;
            }

            if (!TryReadString(root, "supportLevel", out var supportLevel)
                || !IsKnownSupportLevel(supportLevel))
            {
                failure = $"ADS current-duty catalog row has unsupported support level '{supportLevel}'";
                return false;
            }

            if (!TryReadString(root, "clearanceStatus", out var clearanceStatus)
                || !TryParseClearance(clearanceStatus, out var clearanceLevel))
            {
                failure = $"ADS current-duty catalog row has unsupported clearance status '{clearanceStatus}'";
                return false;
            }

            snapshot = new AdsCurrentDutySnapshot(
                dutyName,
                territoryTypeId,
                contentFinderConditionId,
                category,
                supportLevel,
                clearanceStatus,
                clearanceLevel,
                capturedAtUtc);
            return true;
        }
        catch (JsonException ex)
        {
            failure = $"ADS.GetStatusJson was invalid JSON: {ex.Message}";
            return false;
        }
    }

    private static bool TryReadBoolean(JsonElement root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    private static bool TryReadUInt32(JsonElement root, string propertyName, out uint value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.Number
               && property.TryGetUInt32(out value);
    }

    private static bool TryReadString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString()?.Trim() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool TryParseCategory(string value, out AdsDutyCategory category)
    {
        category = value switch
        {
            "Solo" => AdsDutyCategory.Solo,
            "FourMan" => AdsDutyCategory.FourMan,
            "EightMan" => AdsDutyCategory.EightMan,
            "AllianceRaid" => AdsDutyCategory.Alliance,
            "GuildHest" => AdsDutyCategory.GuildHest,
            "DeepDungeon" => AdsDutyCategory.DeepDungeon,
            "TreasureDungeon" => AdsDutyCategory.TreasureDungeon,
            "Other" => AdsDutyCategory.Other,
            _ => (AdsDutyCategory)(-1),
        };
        return category >= AdsDutyCategory.Solo && category <= AdsDutyCategory.Other;
    }

    private static bool IsKnownSupportLevel(string value)
        => value is "Unsupported" or "PassiveOnly" or "ActiveSupported";

    private static bool TryParseClearance(string value, out int clearanceLevel)
    {
        clearanceLevel = value switch
        {
            "NotCleared" => 0,
            "OnePlayerUnsyncCleared" => 1,
            "OnePlayerDutySupport" => 2,
            "FourPlayerSyncCleared" => 3,
            _ => -1,
        };
        return clearanceLevel >= 0;
    }
}
