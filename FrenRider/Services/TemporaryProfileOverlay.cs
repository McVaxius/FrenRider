using System;
using FrenRider.Models;

namespace FrenRider.Services;

internal sealed record DadProfileIdentity(
    string OwnerId,
    string IslandId,
    string CharacterId,
    string ProposalId);

internal interface IDadProfileStore
{
    bool TryResolveOrCreateRemoteProfile(
        string ownerId,
        string islandId,
        string characterId,
        string displayLabel,
        out RemoteProfileRow? row,
        out string code);

    bool TryGetLocalActiveConfig(out CharacterConfig? activeConfig);
    bool TryInstallTemporaryProfile(DadProfileIdentity identity, CharacterConfig config);
    bool TryReleaseTemporaryProfile(DadProfileIdentity identity);
    bool HasTemporaryProfile { get; }
    bool TryReplaceActiveProfilePermanently(CharacterConfig incoming);
    void ReleaseTemporaryProfileOnUnload();
}

internal sealed class TemporaryProfileOverlay
{
    private DadProfileIdentity? identity;
    private string accountId = "";
    private string characterKey = "";
    private CharacterConfig? config;

    internal DadProfileIdentity? Identity => identity;
    internal bool IsInstalled => config != null;

    internal CharacterConfig? Resolve(string currentAccountId, string activeCharacterKey)
        => config != null
           && string.Equals(accountId, currentAccountId, StringComparison.Ordinal)
           && string.Equals(characterKey, activeCharacterKey, StringComparison.Ordinal)
            ? config
            : null;

    internal bool TryInstall(
        DadProfileIdentity requestedIdentity,
        string currentAccountId,
        string activeCharacterKey,
        CharacterConfig requestedConfig)
    {
        if (config != null && identity != requestedIdentity)
            return false;

        identity = requestedIdentity;
        accountId = currentAccountId;
        characterKey = activeCharacterKey;
        config = requestedConfig;
        return true;
    }

    internal bool TryRelease(DadProfileIdentity requestedIdentity, out CharacterConfig? releasedConfig)
    {
        releasedConfig = null;
        if (config == null || identity != requestedIdentity)
            return false;

        releasedConfig = config;
        identity = null;
        accountId = "";
        characterKey = "";
        config = null;
        return true;
    }

    internal bool TryReleaseForActiveCharacter(
        string currentAccountId,
        string activeCharacterKey,
        CharacterConfig localConfig,
        out bool previousEnabled,
        out bool currentEnabled)
    {
        previousEnabled = localConfig.Enabled;
        currentEnabled = localConfig.Enabled;
        var effective = Resolve(currentAccountId, activeCharacterKey);
        if (effective == null || identity == null)
            return false;

        previousEnabled = effective.Enabled;
        return TryRelease(identity, out _);
    }
}
