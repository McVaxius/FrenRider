using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.Models;

namespace FrenRider.Services;

public class ConfigManager
{
    private readonly IPluginLog log;
    private readonly string configDir;

    private readonly Dictionary<string, AccountConfig> accounts = new();

    public string CurrentAccountId { get; set; } = "";
    public string ActiveCharacterKey { get; private set; } = "";

    // Event to notify when FrenRider enabled state changes
    public event Action<bool>? OnFrenRiderEnabledChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private sealed record ConfigCopySetting(string Label, Action<CharacterConfig, CharacterConfig> Copy);

    private sealed record ConfigCopyTab(string Name, string[] Aliases, ConfigCopySetting[] Settings);

    private static readonly ConfigCopyTab[] DefaultSyncTabs =
    {
        new(
            "Profile",
            new[] { "Profile", "Party" },
            new[]
            {
                Setting("Fren Name", (source, target) => target.FrenName = source.FrenName),
                Setting("Fly You Fools", (source, target) => target.FlyYouFools = source.FlyYouFools),
                Setting("Try Teleport to Fren When Out of Zone", (source, target) => target.TryTeleportToFrenWhenOutOfZone = source.TryTeleportToFrenWhenOutOfZone),
                Setting("Teleport Delay", (source, target) => target.TeleportToFrenDelaySeconds = source.TeleportToFrenDelaySeconds),
                Setting("Nudge in duty when fren not nearby/in-zone", (source, target) => target.NudgeInDutyWhenFrenNotNearbyOrInZone = source.NudgeInDutyWhenFrenNotNearbyOrInZone),
                Setting("Respawn after death outside duties", CopyRespawnOutsideDuties),
                Setting("Respawn after death inside duties", CopyRespawnInsideDuties),
                Setting("Mount-up to chase fren", CopyMountUpToChaseFren),
                Setting("Mount Name", (source, target) => target.FoolFlier = source.FoolFlier),
                Setting("Summon Chocobo", (source, target) => target.ForceGysahl = source.ForceGysahl),
                Setting("Companion Stance", (source, target) => target.CompanionStrat = source.CompanionStrat),
                Setting("Update Interval", (source, target) => target.UpdateInterval = source.UpdateInterval),
                Setting("Auto Discard", (source, target) => target.EnableAutoDiscard = source.EnableAutoDiscard),
            }),
        new(
            "Follow",
            new[] { "Follow", "Distance" },
            new[]
            {
                Setting("Cling Distance", (source, target) => target.Cling = source.Cling),
                Setting("Cling Type", (source, target) => target.ClingType = source.ClingType),
                Setting("Cling Type (Duty)", (source, target) => target.ClingTypeDuty = source.ClingTypeDuty),
                Setting("Social Distance", (source, target) => target.SocialDistancing = source.SocialDistancing),
                Setting("Social Distance Indoors", (source, target) => target.SocialDistancingIndoors = source.SocialDistancingIndoors),
                Setting("X Wiggle", (source, target) => target.SocialDistanceXWiggle = source.SocialDistanceXWiggle),
                Setting("Z Wiggle", (source, target) => target.SocialDistanceZWiggle = source.SocialDistanceZWiggle),
                Setting("Max Follow Distance", (source, target) => target.MaxBistance = source.MaxBistance),
                Setting("Max Follow Distance (Foray)", (source, target) => target.MaxBistanceForay = source.MaxBistanceForay),
                Setting("DD Extra Distance", (source, target) => target.DDDistance = source.DDDistance),
                Setting("FATE Extra Distance", (source, target) => target.FDistance = source.FDistance),
                Setting("Auto Sync FATE", (source, target) => target.AutoSyncFate = source.AutoSyncFate),
                Setting("Formation Following", (source, target) => target.Formation = source.Formation),
                Setting("Follow in Combat", (source, target) => target.FollowInCombat = source.FollowInCombat),
                Setting("Harmonized Cling Reset Ticks", (source, target) => target.HClingReset = source.HClingReset),
            }),
        new(
            "Combat",
            new[] { "Combat" },
            new[]
            {
                Setting("Configure rotation preset manually", (source, target) => target.ConfigureRotationPresetManually = source.ConfigureRotationPresetManually),
                Setting("BM Rotation Preset", (source, target) => target.AutoRotationType = source.AutoRotationType),
                Setting("BM Rotation Preset (DD)", (source, target) => target.AutoRotationTypeDD = source.AutoRotationTypeDD),
                Setting("BM Rotation Preset (FATE)", (source, target) => target.AutoRotationTypeFATE = source.AutoRotationTypeFATE),
                Setting("Rotation Plugin", (source, target) => target.RotationPlugin = source.RotationPlugin),
                Setting("Rotation Plugin (Foray)", (source, target) => target.RotationPluginForay = source.RotationPluginForay),
                Setting("Daedalus Engage Mode", (source, target) => target.DaedalusTargetMode = source.DaedalusTargetMode),
                Setting("Force BossMod preset regardless of rotation", (source, target) => target.ForceBossModPresetRegardlessOfRotation = source.ForceBossModPresetRegardlessOfRotation),
                Setting("BossMod AI", (source, target) => target.BossModAI = source.BossModAI),
                Setting("Positional", (source, target) => target.PositionalInCombat = source.PositionalInCombat),
                Setting("Max AI Distance", (source, target) => target.MaxAIDistance = source.MaxAIDistance),
                Setting("LB Threshold %", (source, target) => target.LimitPct = source.LimitPct),
                Setting("RSR Operating Mode", (source, target) => target.RotationType = source.RotationType),
                Setting("RSR Aggro Type", (source, target) => target.RsrAggroType = source.RsrAggroType),
                Setting("BMR reduce activation range for outdoor areas", (source, target) => target.BmrReduceActivationRangeForOutdoorAreas = source.BmrReduceActivationRangeForOutdoorAreas),
                Setting("BMR Disable Hunt Modules", (source, target) => target.BmrDisableHuntModules = source.BmrDisableHuntModules),
                Setting("BMR Disable Queen Lunatender", (source, target) => target.BmrDisableQueenLunatender = source.BmrDisableQueenLunatender),
                Setting("Cleanup Mode", (source, target) => target.CleanupMode = source.CleanupMode),
            }),
        new(
            "Duty",
            new[] { "Duty", "Ads", "Duty / ADS / Exit" },
            new[]
            {
                Setting("ADS Legacy Handoff", CopyAdsLegacySettings),
                Setting("ADS Solo", CopyAdsDutyFamily(AdsDutyCategory.Solo)),
                Setting("ADS 4-Man", CopyAdsDutyFamily(AdsDutyCategory.FourMan)),
                Setting("ADS 8-Man", CopyAdsDutyFamily(AdsDutyCategory.EightMan)),
                Setting("ADS Alliance", CopyAdsDutyFamily(AdsDutyCategory.Alliance)),
                Setting("ADS Guild Hest", CopyAdsDutyFamily(AdsDutyCategory.GuildHest)),
                Setting("ADS Deep Dungeon", CopyAdsDutyFamily(AdsDutyCategory.DeepDungeon)),
                Setting("ADS Treasure Dungeon", CopyAdsDutyFamily(AdsDutyCategory.TreasureDungeon)),
                Setting("ADS Other", CopyAdsDutyFamily(AdsDutyCategory.Other)),
                Setting("ADS Chest Opening", (source, target) => target.AdsEnableChestOpening = source.AdsEnableChestOpening),
                Setting("ADS Preset Selection", (source, target) => target.AdsPresetSelection = source.AdsPresetSelection),
                Setting("ADS Duty Migration State", (source, target) => target.AdsDutyFamilySettingsMigrated = source.AdsDutyFamilySettingsMigrated),
                Setting("ADS Exit Method", CopyExitMethod),
                Setting("Invite Whitelist", (source, target) => target.InviteWhitelist = new List<string>(source.InviteWhitelist)),
                Setting("Raise offers", (source, target) => target.RaiseOfferAutoAccept = source.RaiseOfferAutoAccept),
                Setting("Teleport offers", (source, target) => target.TeleportOfferAutoAccept = source.TeleportOfferAutoAccept),
                Setting("Party invites", (source, target) => target.PartyInviteAutoAccept = source.PartyInviteAutoAccept),
                Setting("Exit Method", CopyExitMethod),
                Setting("Duty-end delay", (source, target) => target.ExitAfterDutySeconds = source.ExitAfterDutySeconds),
            }),
        new(
            "Automation",
            new[] { "Automation", "Misc" },
            new[]
            {
                Setting("Loot Type", (source, target) => target.FulfType = source.FulfType),
                Setting("Food", CopyFood),
                Setting("Use HQ food", (source, target) => target.FeedMeUseHighQuality = source.FeedMeUseHighQuality),
                Setting("Search for Food if Depleted", (source, target) => target.FeedMeSearch = source.FeedMeSearch),
                Setting("XP Item", (source, target) => target.XpItem = source.XpItem),
                Setting("Repair Mode", (source, target) => target.Repair = source.Repair),
                Setting("Repair At % Durability", (source, target) => target.TornClothes = source.TornClothes),
                Setting("Enable Auto Desynth", (source, target) => target.EnableAutoDesynth = source.EnableAutoDesynth),
                Setting("Echo Messages", (source, target) => target.SpamPrinter = source.SpamPrinter),
                Setting("Debug Mode", (source, target) => target.DebugMode = source.DebugMode),
                Setting("Idle Mode", (source, target) => target.IdleActionMode = source.IdleActionMode),
                Setting("Idle Command", (source, target) => target.IdleAction = source.IdleAction),
                Setting("List Source", (source, target) => target.IdleListMode = source.IdleListMode),
                Setting("Custom Idle List", (source, target) => target.CustomIdleList = CharacterConfig.CloneCustomIdleList(source.CustomIdleList)),
                Setting("Idle Ticks Before Action", (source, target) => target.IdleTicksBeforeAction = source.IdleTicksBeforeAction),
                Setting("Auto Discard", (source, target) => target.EnableAutoDiscard = source.EnableAutoDiscard),
                Setting("Equip job stone for current class", (source, target) => target.EquipJobStoneForCurrentClass = source.EquipJobStoneForCurrentClass),
                Setting("Push Presets On Enable", (source, target) => target.AutorotPushOnEnable = source.AutorotPushOnEnable),
            }),
    };

    public ConfigManager(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;
        configDir = pluginInterface.GetPluginConfigDirectory();
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        LoadAllAccounts();
    }

    internal bool TryReadLauncherAccountId(out string accountId)
    {
        if (TryReadLauncherAccountId(configDir, out accountId, out var error))
            return true;

        log.Warning(error);
        return false;
    }

    internal static bool TryReadLauncherAccountId(
        string configDirectory,
        out string accountId,
        out string error)
    {
        accountId = "";
        error = "";

        var pluginConfigsDirectory = Directory.GetParent(configDirectory);
        var launcherRoot = pluginConfigsDirectory?.Parent;
        if (pluginConfigsDirectory == null
            || !pluginConfigsDirectory.Name.Equals("pluginConfigs", StringComparison.OrdinalIgnoreCase)
            || launcherRoot == null)
        {
            error = $"Cannot resolve XIVLauncher root from plugin config directory {configDirectory}";
            return false;
        }

        var launcherConfigPath = Path.Combine(launcherRoot.FullName, "launcherConfigV3.json");
        try
        {
            using var document = JsonDocument.Parse(
                File.ReadAllText(launcherConfigPath),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                });

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("CurrentAccountId", out var accountIdElement)
                || accountIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(accountIdElement.GetString()))
            {
                error = $"XIVLauncher config {launcherConfigPath} has no usable CurrentAccountId";
                return false;
            }

            accountId = accountIdElement.GetString()!;
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to read XIVLauncher account ID from {launcherConfigPath}: {ex.Message}";
            return false;
        }
    }

    public static bool CanSyncDefaultTab(string tabName)
        => FindTab(tabName) != null;

    private static ConfigCopySetting Setting(string label, Action<CharacterConfig, CharacterConfig> copy)
        => new(label, copy);

    private static void CopyRespawnOutsideDuties(CharacterConfig source, CharacterConfig target)
    {
        target.RespawnOutsideDuties = source.RespawnOutsideDuties;
        target.RespawnOutsideDutiesDelaySeconds = source.RespawnOutsideDutiesDelaySeconds;
    }

    private static void CopyRespawnInsideDuties(CharacterConfig source, CharacterConfig target)
    {
        target.RespawnInsideDuties = source.RespawnInsideDuties;
        target.RespawnInsideDutiesDelaySeconds = source.RespawnInsideDutiesDelaySeconds;
    }

    private static void CopyMountUpToChaseFren(CharacterConfig source, CharacterConfig target)
    {
        target.MountUpToChaseFren = source.MountUpToChaseFren;
        target.MountUpToChaseFrenDistance = source.MountUpToChaseFrenDistance;
        target.MountUpToChaseFrenDelaySeconds = source.MountUpToChaseFrenDelaySeconds;
    }

    private static void CopyAdsLegacySettings(CharacterConfig source, CharacterConfig target)
    {
        target.UseAdsIfAvailable = source.UseAdsIfAvailable;
        target.AdsMaturityThreshold = source.AdsMaturityThreshold;
    }

    private static Action<CharacterConfig, CharacterConfig> CopyAdsDutyFamily(AdsDutyCategory category)
        => (source, target) =>
        {
            var settings = source.GetAdsDutyFamilySettings(category);
            target.EnsureAdsDutyFamilySettingsInitialized();
            target.SetAdsDutyFamilySettings(category, settings.Enabled, settings.MaturityThreshold);
        };

    private static void CopyExitMethod(CharacterConfig source, CharacterConfig target)
    {
        target.UseAdsLeaveAfterAdsDuty = source.UseAdsLeaveAfterAdsDuty;
        target.ExitAfterDutyEnds = source.ExitAfterDutyEnds;
        target.LeaveWhenAllLeft = source.LeaveWhenAllLeft;
        target.NormalizeExitMethodSelection();
    }

    private static void CopyFood(CharacterConfig source, CharacterConfig target)
    {
        target.FeedMeItemId = source.FeedMeItemId;
        target.FeedMeItem = source.FeedMeItem;
    }

    private static ConfigCopyTab? FindTab(string tabName)
        => DefaultSyncTabs.FirstOrDefault(tab =>
            tab.Aliases.Any(alias => alias.Equals(tabName, StringComparison.OrdinalIgnoreCase)));

    private static ConfigCopySetting? FindSetting(string label)
        => DefaultSyncTabs
            .SelectMany(tab => tab.Settings)
            .FirstOrDefault(setting => setting.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ConfigCopySetting> GetUniqueDefaultSyncSettings()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var setting in DefaultSyncTabs.SelectMany(tab => tab.Settings))
        {
            if (seen.Add(setting.Label))
                yield return setting;
        }
    }

    private static bool ApplyTabSettings(CharacterConfig source, CharacterConfig target, string tabName)
    {
        var tab = FindTab(tabName);
        if (tab == null)
            return false;

        foreach (var setting in tab.Settings)
            setting.Copy(source, target);

        return true;
    }

    public IReadOnlyDictionary<string, AccountConfig> Accounts => accounts;

    public AccountConfig? GetCurrentAccount()
    {
        if (string.IsNullOrEmpty(CurrentAccountId)) return null;
        return accounts.TryGetValue(CurrentAccountId, out var acc) ? acc : null;
    }

    public CharacterConfig GetActiveConfig()
    {
        var account = GetCurrentAccount();
        return ResolveActiveConfigOrDisabled(account, CurrentAccountId, ActiveCharacterKey);
    }

    public CharacterConfig GetCurrentCharacterConfig(string charKey)
    {
        var account = GetCurrentAccount();
        return ResolveEditingConfigOrDisabled(account, charKey);
    }

    internal static CharacterConfig ResolveActiveConfigOrDisabled(
        AccountConfig? account,
        string? currentAccountId,
        string? activeCharacterKey)
    {
        return TryResolveActiveConfig(account, currentAccountId, activeCharacterKey, out var activeConfig)
            ? activeConfig!
            : CreateDisabledConfig();
    }

    internal static CharacterConfig ResolveEditingConfigOrDisabled(AccountConfig? account, string? editingCharacterKey)
    {
        if (account == null)
            return CreateDisabledConfig();

        if (string.IsNullOrEmpty(editingCharacterKey))
            return account.DefaultConfig ?? CreateDisabledConfig();

        return account.Characters.TryGetValue(editingCharacterKey, out var characterConfig)
            && characterConfig != null
                ? characterConfig
                : CreateDisabledConfig();
    }

    private static CharacterConfig CreateDisabledConfig()
        => new()
        {
            Enabled = false,
            AutoSyncFate = false,
            AdsEnableChestOpening = false,
            FeedMeSearch = false,
            RaiseOfferAutoAccept = false,
            TeleportOfferAutoAccept = false,
            PartyInviteAutoAccept = false,
            ExitAfterDutyEnds = false,
            AutorotPushOnEnable = false,
        };

    private bool TryGetActiveConfig(out CharacterConfig? activeConfig)
        => TryResolveActiveConfig(GetCurrentAccount(), CurrentAccountId, ActiveCharacterKey, out activeConfig);

    internal static bool TryResolveActiveConfig(
        AccountConfig? account,
        string? currentAccountId,
        string? activeCharacterKey,
        out CharacterConfig? activeConfig)
    {
        activeConfig = null;
        if (account == null
            || string.IsNullOrWhiteSpace(currentAccountId)
            || !string.Equals(account.AccountId, currentAccountId, StringComparison.Ordinal)
            || account.Characters == null
            || string.IsNullOrWhiteSpace(activeCharacterKey)
            || !account.Characters.TryGetValue(activeCharacterKey, out var resolvedConfig)
            || resolvedConfig == null)
        {
            return false;
        }

        activeConfig = resolvedConfig;
        return true;
    }

    public void EnsureAccountSelected(
        string? launcherAccountId,
        string? currentCharacterKey,
        string? aliasHint = null)
    {
        ActiveCharacterKey = "";

        if (string.IsNullOrWhiteSpace(launcherAccountId))
        {
            CurrentAccountId = "";
            log.Warning("Cannot select an active account without XIVLauncher CurrentAccountId");
            return;
        }

        if (string.IsNullOrWhiteSpace(currentCharacterKey))
        {
            CurrentAccountId = "";
            log.Warning("Cannot select an active account without the current character key");
            return;
        }

        var accountId = launcherAccountId;
        var characterKey = currentCharacterKey;
        log.Information($"EnsureAccountSelected: LauncherAccountId={accountId}, Character={characterKey}");
        if (!TrySelectLauncherAccount(
                accounts,
                accountId,
                characterKey,
                aliasHint,
                SaveAccount,
                DeleteReplacedLegacyAccount,
                out _,
                out var failure))
        {
            CurrentAccountId = "";
            log.Warning(failure);
            return;
        }

        CurrentAccountId = accountId;
    }

    internal static bool TrySelectLauncherAccount(
        IDictionary<string, AccountConfig> accountConfigs,
        string launcherAccountId,
        string characterKey,
        string? aliasHint,
        Func<string, bool> saveAccount,
        Action<string> deleteReplacedLegacyAccount,
        out AccountConfig? selectedAccount,
        out string failure)
    {
        selectedAccount = null;
        failure = "";

        var matchingLegacyAccounts = accountConfigs
            .Where(kvp => !string.Equals(kvp.Key, launcherAccountId, StringComparison.Ordinal)
                          && kvp.Value.Characters != null
                          && kvp.Value.Characters.TryGetValue(characterKey, out var config)
                          && config != null)
            .ToList();

        if (accountConfigs.TryGetValue(launcherAccountId, out var existingAccount))
        {
            if (existingAccount.Characters == null)
            {
                failure = $"Launcher account {launcherAccountId} has an invalid character collection";
                return false;
            }

            var copiedCharacter = false;
            var needsCharacterMigration = !existingAccount.Characters.TryGetValue(characterKey, out var existingCharacter)
                                          || existingCharacter == null;
            if (needsCharacterMigration && matchingLegacyAccounts.Count > 1)
            {
                failure = $"Cannot safely migrate {characterKey}: multiple legacy account files contain that character";
                return false;
            }

            if (needsCharacterMigration && matchingLegacyAccounts.Count == 1)
            {
                existingAccount.Characters[characterKey] = matchingLegacyAccounts[0].Value.Characters[characterKey].Clone();
                copiedCharacter = true;
            }

            var previousAlias = existingAccount.AccountAlias;
            var changedAlias = !string.IsNullOrWhiteSpace(aliasHint)
                               && string.IsNullOrWhiteSpace(existingAccount.AccountAlias);
            if (changedAlias)
                existingAccount.AccountAlias = aliasHint!;

            if ((copiedCharacter || changedAlias) && !saveAccount(launcherAccountId))
            {
                if (copiedCharacter)
                    existingAccount.Characters.Remove(characterKey);
                if (changedAlias)
                    existingAccount.AccountAlias = previousAlias;
                failure = $"Failed to save launcher account {launcherAccountId}";
                return false;
            }

            selectedAccount = existingAccount;
            return true;
        }

        if (matchingLegacyAccounts.Count > 1)
        {
            failure = $"Cannot safely migrate {characterKey}: multiple legacy account files contain that character";
            return false;
        }

        if (accountConfigs.Count == 1 && matchingLegacyAccounts.Count == 1)
        {
            var legacyAccount = matchingLegacyAccounts[0];
            var account = legacyAccount.Value;
            var storedAccountId = account.AccountId;
            var storedAlias = account.AccountAlias;

            account.AccountId = launcherAccountId;
            if (!string.IsNullOrWhiteSpace(aliasHint) && string.IsNullOrWhiteSpace(account.AccountAlias))
                account.AccountAlias = aliasHint!;
            accountConfigs[launcherAccountId] = account;

            if (!saveAccount(launcherAccountId))
            {
                accountConfigs.Remove(launcherAccountId);
                account.AccountId = storedAccountId;
                account.AccountAlias = storedAlias;
                failure = $"Failed to save launcher account {launcherAccountId}; legacy account {legacyAccount.Key} was left unchanged";
                return false;
            }

            accountConfigs.Remove(legacyAccount.Key);
            deleteReplacedLegacyAccount(legacyAccount.Key);
            selectedAccount = account;
            return true;
        }

        var newAccount = new AccountConfig
        {
            AccountId = launcherAccountId,
            AccountAlias = !string.IsNullOrWhiteSpace(aliasHint)
                ? aliasHint
                : $"Account {accountConfigs.Count + 1}",
        };

        if (matchingLegacyAccounts.Count == 1)
            newAccount.Characters[characterKey] = matchingLegacyAccounts[0].Value.Characters[characterKey].Clone();

        accountConfigs[launcherAccountId] = newAccount;
        if (!saveAccount(launcherAccountId))
        {
            accountConfigs.Remove(launcherAccountId);
            failure = $"Failed to save launcher account {launcherAccountId}";
            return false;
        }

        selectedAccount = newAccount;
        return true;
    }

    private void DeleteReplacedLegacyAccount(string accountId)
    {
        try
        {
            var file = Path.Combine(configDir, $"{accountId}_FrenRider.json");
            if (File.Exists(file))
                File.Delete(file);
        }
        catch (Exception ex)
        {
            log.Warning($"Failed to delete replaced legacy config file for {accountId}: {ex.Message}");
        }
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";
        if (string.IsNullOrEmpty(CurrentAccountId))
        {
            ActiveCharacterKey = "";
            log.Warning($"Cannot activate character {charKey} without a launcher-account-selected config");
            return;
        }

        if (!accounts.TryGetValue(CurrentAccountId, out var accountForChar))
        {
            ActiveCharacterKey = "";
            log.Error($"Current account {CurrentAccountId} missing when adding {charKey}");
            return;
        }

        if (!TryEnsureCharacterExists(accountForChar, charKey, out var added))
        {
            ActiveCharacterKey = "";
            log.Error($"Failed to resolve character {charKey} in account {CurrentAccountId}");
            return;
        }

        if (added)
        {
            if (!SaveAccount(CurrentAccountId))
            {
                accountForChar.Characters.Remove(charKey);
                ActiveCharacterKey = "";
                log.Error($"Failed to persist character {charKey} in account {CurrentAccountId}");
                return;
            }

            log.Information($"Added character {charKey} to account {CurrentAccountId}");
        }

        ActiveCharacterKey = charKey;
    }

    internal static bool TryEnsureCharacterExists(AccountConfig? account, string? charKey, out bool added)
    {
        added = false;
        if (account == null
            || account.Characters == null
            || account.DefaultConfig == null
            || string.IsNullOrWhiteSpace(charKey))
        {
            return false;
        }

        if (account.Characters.TryGetValue(charKey, out var existingConfig) && existingConfig != null)
            return true;

        account.Characters[charKey] = account.DefaultConfig.Clone();
        added = true;
        return true;
    }

    public void ClearActiveCharacter()
    {
        if (string.IsNullOrEmpty(ActiveCharacterKey))
            return;

        log.Information($"Cleared active character profile: {ActiveCharacterKey}");
        ActiveCharacterKey = "";
    }

    public string CreateNewAccount(string alias)
    {
        log.Warning("Cannot create an account without XIVLauncher CurrentAccountId");
        return "";
    }

    public void SaveCurrentAccount()
    {
        if (!string.IsNullOrEmpty(CurrentAccountId))
            SaveAccount(CurrentAccountId);
    }

    public bool ClearActiveFrenName()
    {
        if (!TryGetActiveConfig(out var activeConfig) || activeConfig == null)
            return false;

        activeConfig.FrenName = string.Empty;
        return SaveAccount(CurrentAccountId);
    }

    /// <summary>
    /// Atomically configures and enables only the active character profile for DAD.
    /// The default profile is never used as a fallback by this operation.
    /// </summary>
    public bool ConfigureAndEnableActiveCharacter(string nameAtWorld)
    {
        if (string.IsNullOrWhiteSpace(CurrentAccountId))
            return false;

        var account = GetCurrentAccount();
        if (account == null
            || string.IsNullOrWhiteSpace(account.AccountId)
            || !string.Equals(account.AccountId, CurrentAccountId, StringComparison.Ordinal))
        {
            return false;
        }

        var succeeded = TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            nameAtWorld,
            () => SaveAccount(CurrentAccountId),
            out var becameEnabled);

        if (!succeeded)
            return false;

        if (becameEnabled)
        {
            try
            {
                OnFrenRiderEnabledChanged?.Invoke(true);
            }
            catch (Exception ex)
            {
                // The profile is already durably enabled at this point. Do not report a
                // retryable IPC failure after persistence succeeded, because that would
                // violate the endpoint's false-without-mutation contract.
                log.Error(ex, "[ConfigManager] FrenRider enable lifecycle callback failed after DAD configuration was saved.");
            }

            log.Information("[ConfigManager] FrenRider enabled by DAD for the active character profile.");
        }

        return true;
    }

    internal static bool TryConfigureAndEnableActiveCharacter(
        AccountConfig? account,
        string? activeCharacterKey,
        string? nameAtWorld,
        Func<bool> persist,
        out bool becameEnabled)
    {
        becameEnabled = false;

        if (account == null
            || string.IsNullOrWhiteSpace(account.AccountId)
            || account.Characters == null
            || string.IsNullOrWhiteSpace(activeCharacterKey)
            || !account.Characters.TryGetValue(activeCharacterKey, out var activeConfig)
            || activeConfig == null
            || !IsValidExactNameAtWorld(nameAtWorld))
        {
            return false;
        }

        var exactNameAtWorld = nameAtWorld!;
        var previousFrenName = activeConfig.FrenName;
        var wasEnabled = activeConfig.Enabled;

        if (wasEnabled && string.Equals(previousFrenName, exactNameAtWorld, StringComparison.Ordinal))
            return true;

        activeConfig.FrenName = exactNameAtWorld;
        activeConfig.Enabled = true;

        var saved = false;
        try
        {
            saved = persist();
        }
        catch
        {
            // Persistence exceptions are an ordinary endpoint rejection. Restore the
            // exact in-memory values so callers can safely retry.
        }

        if (!saved)
        {
            activeConfig.FrenName = previousFrenName;
            activeConfig.Enabled = wasEnabled;
            return false;
        }

        becameEnabled = !wasEnabled;
        return true;
    }

    internal static bool IsValidExactNameAtWorld(string? nameAtWorld)
    {
        if (string.IsNullOrWhiteSpace(nameAtWorld)
            || !string.Equals(nameAtWorld, nameAtWorld.Trim(), StringComparison.Ordinal)
            || nameAtWorld.Any(char.IsControl))
        {
            return false;
        }

        var separator = nameAtWorld.IndexOf('@');
        if (separator <= 0
            || separator != nameAtWorld.LastIndexOf('@')
            || separator >= nameAtWorld.Length - 1)
        {
            return false;
        }

        var characterName = nameAtWorld[..separator];
        var worldName = nameAtWorld[(separator + 1)..];
        return !string.IsNullOrWhiteSpace(characterName)
               && !string.IsNullOrWhiteSpace(worldName)
               && string.Equals(characterName, characterName.Trim(), StringComparison.Ordinal)
               && string.Equals(worldName, worldName.Trim(), StringComparison.Ordinal)
               && !worldName.Any(char.IsWhiteSpace);
    }

    public void SetFrenRiderEnabled(bool enabled)
    {
        if (!TryGetActiveConfig(out var currentConfig) || currentConfig == null)
        {
            log.Warning($"[ConfigManager] Ignored enabled state change to {enabled}: no active character profile");
            return;
        }

        var wasEnabled = currentConfig.Enabled;
        
        if (wasEnabled != enabled)
        {
            currentConfig.Enabled = enabled;
            SaveCurrentAccount();
            
            // Trigger the event
            OnFrenRiderEnabledChanged?.Invoke(enabled);
            
            log.Information($"[ConfigManager] FrenRider enabled state changed: {wasEnabled} -> {enabled}");
        }
    }

    public void ResetCharacterToDefault(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null) return;

        if (string.IsNullOrEmpty(charKey))
        {
            // Reset default config to plugin defaults
            account.DefaultConfig = new CharacterConfig();
        }
        else if (account.Characters.ContainsKey(charKey))
        {
            // Reset character to current default
            account.Characters[charKey] = account.DefaultConfig.Clone();
        }

        SaveCurrentAccount();
    }

    public int ApplyDefaultToAllCharacters()
    {
        var account = GetCurrentAccount();
        if (account == null) return 0;

        var count = ApplyDefaultToAllCharacters(account);
        if (count > 0)
            SaveCurrentAccount();

        log.Information($"[ConfigManager] Synced DEFAULT CONFIG to {count} character profiles in account {CurrentAccountId}");
        return count;
    }

    public int ApplyDefaultTabToAllCharacters(string tabName)
    {
        var account = GetCurrentAccount();
        if (account == null) return 0;

        var count = ApplyDefaultTabToAllCharacters(account, tabName);
        if (count > 0)
            SaveCurrentAccount();

        log.Information($"[ConfigManager] Synced DEFAULT CONFIG tab '{tabName}' to {count} character profiles in account {CurrentAccountId}");
        return count;
    }

    public int ApplyDefaultSettingToAllCharacters(string label)
    {
        var setting = FindSetting(label);
        if (setting == null)
        {
            log.Warning($"[ConfigManager] No DEFAULT CONFIG sync mapping found for setting '{label}'");
            return 0;
        }

        return ApplyDefaultSettingToAllCharacters(label, setting.Copy);
    }

    public int ApplyDefaultSettingToAllCharacters(string label, Action<CharacterConfig, CharacterConfig> copy)
    {
        var account = GetCurrentAccount();
        if (account == null) return 0;

        var count = ApplyDefaultSettingToAllCharacters(account, copy);
        if (count > 0)
            SaveCurrentAccount();

        log.Information($"[ConfigManager] Synced DEFAULT CONFIG setting '{label}' to {count} character profiles in account {CurrentAccountId}");
        return count;
    }

    internal static int ApplyDefaultToAllCharacters(AccountConfig account)
    {
        foreach (var target in account.Characters.Values)
        {
            foreach (var setting in GetUniqueDefaultSyncSettings())
                setting.Copy(account.DefaultConfig, target);
        }

        return account.Characters.Count;
    }

    internal static int ApplyDefaultTabToAllCharacters(AccountConfig account, string tabName)
    {
        var tab = FindTab(tabName);
        if (tab == null)
            return 0;

        foreach (var target in account.Characters.Values)
        {
            foreach (var setting in tab.Settings)
                setting.Copy(account.DefaultConfig, target);
        }

        return account.Characters.Count;
    }

    internal static int ApplyDefaultSettingToAllCharacters(
        AccountConfig account,
        string label)
    {
        var setting = FindSetting(label);
        return setting == null
            ? 0
            : ApplyDefaultSettingToAllCharacters(account, setting.Copy);
    }

    internal static int ApplyDefaultSettingToAllCharacters(
        AccountConfig account,
        Action<CharacterConfig, CharacterConfig> copy)
    {
        ArgumentNullException.ThrowIfNull(copy);

        foreach (var target in account.Characters.Values)
            copy(account.DefaultConfig, target);

        return account.Characters.Count;
    }

    public void ResetCharacterTabToDefault(string charKey, string tabName)
    {
        var account = GetCurrentAccount();
        if (account == null) return;

        var target = string.IsNullOrEmpty(charKey) ? account.DefaultConfig : null;
        if (target == null && account.Characters.TryGetValue(charKey!, out var cc))
            target = cc;
        if (target == null) return;

        var source = string.IsNullOrEmpty(charKey) ? new CharacterConfig() : account.DefaultConfig;
        if (!ApplyTabSettings(source, target, tabName))
            return;

        SaveCurrentAccount();
    }

    public bool DeleteCharacter(string charKey)
    {
        var account = GetCurrentAccount();
        if (!TryDeleteCharacter(account, ActiveCharacterKey, charKey))
        {
            if (string.Equals(ActiveCharacterKey, charKey, StringComparison.Ordinal))
                log.Warning($"Cannot delete active character config: {charKey}");
            return false;
        }

        SaveCurrentAccount();
        log.Information($"Deleted character config: {charKey}");
        return true;
    }

    internal static bool TryDeleteCharacter(AccountConfig? account, string? activeCharacterKey, string? charKey)
    {
        if (account == null
            || account.Characters == null
            || string.IsNullOrEmpty(charKey)
            || string.Equals(activeCharacterKey, charKey, StringComparison.Ordinal))
        {
            return false;
        }

        return account.Characters.Remove(charKey);
    }

    public IEnumerable<string> GetSortedCharacterKeys()
    {
        var account = GetCurrentAccount();
        if (account == null) return Enumerable.Empty<string>();
        return account.Characters.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);
    }

    public void UpdateAccountAlias(string alias)
    {
        var account = GetCurrentAccount();
        if (account == null) return;
        account.AccountAlias = alias;
        SaveCurrentAccount();
    }

    private void LoadAllAccounts()
    {
        try
        {
            var files = Directory.GetFiles(configDir, "*_FrenRider.json");
            foreach (var file in files)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var account = JsonSerializer.Deserialize<AccountConfig>(json, JsonOptions);
                    if (account != null && !string.IsNullOrEmpty(account.AccountId))
                    {
                        accounts[account.AccountId] = account;
                        if (MigrateLegacyRsrSettings(account))
                        {
                            SaveAccount(account.AccountId);
                            log.Information($"Migrated legacy RSR settings for account {account.AccountId}");
                        }
                        log.Information($"Loaded account {account.AccountId} ({account.AccountAlias}) with {account.Characters.Count} characters");
                    }
                }
                catch (Exception ex)
                {
                    log.Error($"Failed to load config file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            log.Error($"Failed to enumerate config files: {ex.Message}");
        }
    }

    internal static bool MigrateLegacyRsrSettings(AccountConfig account)
    {
        var migrated = account.DefaultConfig?.MigrateLegacyRsrOperatingMode() == true;
        if (account.Characters == null)
            return migrated;

        foreach (var config in account.Characters.Values)
            migrated |= config?.MigrateLegacyRsrOperatingMode() == true;

        return migrated;
    }

    private bool SaveAccount(string accountId)
    {
        if (!accounts.TryGetValue(accountId, out var account)) return false;

        try
        {
            var fileName = $"{accountId}_FrenRider.json";
            var filePath = Path.Combine(configDir, fileName);
            var json = JsonSerializer.Serialize(account, JsonOptions);
            File.WriteAllText(filePath, json);
            log.Debug($"Saved account {accountId}");
            return true;
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save account {accountId}: {ex.Message}");
            return false;
        }
    }

    public static string FixNameCapitalization(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        var parts = input.Split('@');
        var charPart = parts[0].Trim();
        var serverPart = parts.Length > 1 ? parts[1].Trim() : "";

        charPart = string.Join(" ", charPart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 0
                ? char.ToUpper(w[0]) + (w.Length > 1 ? w[1..].ToLower() : "")
                : w));

        if (serverPart.Length > 0)
            serverPart = char.ToUpper(serverPart[0]) + (serverPart.Length > 1 ? serverPart[1..].ToLower() : "");

        return serverPart.Length > 0 ? $"{charPart}@{serverPart}" : charPart;
    }
}
