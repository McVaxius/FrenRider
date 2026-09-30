using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FrenRider.Models;

namespace FrenRider.Services;

// Runtime-only: never installed in, copied to, or serialized with a character profile.
internal sealed class QuestionableDutySettingsOverride
{
    private string? runId;
    private string? characterIdentity;

    internal bool Apply(string owner, string settingsJson, string activeCharacterIdentity)
    {
        ObserveCharacter(activeCharacterIdentity);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrEmpty(activeCharacterIdentity))
            return false;
        try
        {
            using var document = JsonDocument.Parse(settingsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 10 ||
                !root.GetProperty("AdsSoloEnabled").GetBoolean() ||
                root.GetProperty("AdsSoloMaturityThreshold").GetInt32() != 0 ||
                root.GetProperty("AdsSoloHandoffDelaySeconds").GetInt32() != 10 ||
                !root.GetProperty("AdsFourManEnabled").GetBoolean() ||
                root.GetProperty("AdsFourManMaturityThreshold").GetInt32() != 0 ||
                root.GetProperty("AdsFourManHandoffDelaySeconds").GetInt32() != 2 ||
                !root.GetProperty("UseAdsLeaveAfterAdsDuty").GetBoolean() ||
                root.GetProperty("ExitAfterDutyEnds").GetBoolean() ||
                root.GetProperty("LeaveWhenAllLeft").GetBoolean() ||
                root.GetProperty("ExitAfterDutySeconds").GetInt32() != 20)
                return false;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or FormatException)
        {
            return false;
        }

        if (runId != null)
            return string.Equals(runId, owner, StringComparison.Ordinal);
        runId = owner;
        characterIdentity = activeCharacterIdentity;
        return true;
    }

    internal bool Release(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
            return false;
        if (runId == null)
            return true;
        if (!string.Equals(runId, owner, StringComparison.Ordinal))
            return false;
        Clear();
        return true;
    }

    internal void ObserveCharacter(string activeCharacterIdentity)
    {
        if (!string.Equals(characterIdentity, activeCharacterIdentity, StringComparison.Ordinal))
            Clear();
    }

    internal bool Controls(AdsDutyCategory category, string activeCharacterIdentity)
    {
        ObserveCharacter(activeCharacterIdentity);
        return runId != null && category is AdsDutyCategory.Solo or AdsDutyCategory.FourMan;
    }

    internal AdsDutyFamilySettings Resolve(CharacterConfig config, AdsDutyCategory category, string activeCharacterIdentity)
        => Controls(category, activeCharacterIdentity)
            ? new(true, 0, category == AdsDutyCategory.Solo ? 10 : 2)
            : config.GetAdsDutyFamilySettings(category);

    internal DutyExitSettings ResolveExit(CharacterConfig config, string activeCharacterIdentity)
        => Controls(AdsDutyCategory.Solo, activeCharacterIdentity)
            ? new(true, false, false, 20)
            : DutyExitSettings.FromConfig(config);

    internal void Clear()
    {
        runId = null;
        characterIdentity = null;
    }
}
