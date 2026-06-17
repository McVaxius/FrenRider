using System;
using System.Text.RegularExpressions;

namespace FrenRider.Services;

public enum SelectYesnoPromptKind
{
    Unknown,
    Teleport,
    DeathReturn,
    Raise,
    Party,
    Misc,
}

public static class SelectYesnoPromptClassifier
{
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex PartyInviteRegex = new(@"\bjoin\s+.+?'?s\s+party\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReturnToDestinationRegex = new(@"^Return\s+to\s+.+\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ReturnHomeRegex = new(@"^Return\s+Home\?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static SelectYesnoPromptKind Classify(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return SelectYesnoPromptKind.Unknown;

        var text = Normalize(prompt);

        if (IsPartyPrompt(text))
            return SelectYesnoPromptKind.Party;

        if (IsRaisePrompt(text))
            return SelectYesnoPromptKind.Raise;

        if (IsTeleportPrompt(text))
            return SelectYesnoPromptKind.Teleport;

        if (IsKnownMiscPrompt(text))
            return SelectYesnoPromptKind.Misc;

        if (IsDeathReturnPrompt(text))
            return SelectYesnoPromptKind.DeathReturn;

        return SelectYesnoPromptKind.Unknown;
    }

    private static string Normalize(string prompt)
        => WhitespaceRegex.Replace(prompt.Trim(), " ");

    private static bool IsPartyPrompt(string text)
        => text.Contains("Would you like to join the party", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Join the party", StringComparison.OrdinalIgnoreCase)
            || PartyInviteRegex.IsMatch(text);

    private static bool IsRaisePrompt(string text)
        => text.Contains("Would you like to be raised", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Accept Raise", StringComparison.OrdinalIgnoreCase);

    private static bool IsTeleportPrompt(string text)
        => text.Contains("Accept Teleport to", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Accept Teleport", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Would you like to teleport", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Teleport to the", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownMiscPrompt(string text)
        => text.Contains("Use the teleporter", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Return to the levemete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Return to a levemete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Return to the starting point", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Duty calls", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Are you interested", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Move immediately to sealed area", StringComparison.OrdinalIgnoreCase);

    private static bool IsDeathReturnPrompt(string text)
    {
        if (!text.Contains("Return", StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Contains("levemete", StringComparison.OrdinalIgnoreCase)
            || text.Contains("starting point", StringComparison.OrdinalIgnoreCase))
            return false;

        return text.Contains("home point", StringComparison.OrdinalIgnoreCase)
            || text.Contains("return to your", StringComparison.OrdinalIgnoreCase)
            || text.Contains("return to the aetheryte", StringComparison.OrdinalIgnoreCase)
            || text.Contains("return to an aetheryte", StringComparison.OrdinalIgnoreCase)
            || ReturnToDestinationRegex.IsMatch(text)
            || ReturnHomeRegex.IsMatch(text);
    }
}
