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
    private readonly IDalamudPluginInterface pluginInterface;
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
                Setting("Force BossMod preset regardless of rotation", (source, target) => target.ForceBossModPresetRegardlessOfRotation = source.ForceBossModPresetRegardlessOfRotation),
                Setting("BossMod AI", (source, target) => target.BossModAI = source.BossModAI),
                Setting("Positional", (source, target) => target.PositionalInCombat = source.PositionalInCombat),
                Setting("Max AI Distance", (source, target) => target.MaxAIDistance = source.MaxAIDistance),
                Setting("LB Threshold %", (source, target) => target.LimitPct = source.LimitPct),
                Setting("RSR Rotation Type", (source, target) => target.RotationType = source.RotationType),
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
                Setting("Push Presets On Enable", (source, target) => target.AutorotPushOnEnable = source.AutorotPushOnEnable),
            }),
    };

    public ConfigManager(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;
        configDir = Path.Combine(pluginInterface.GetPluginConfigDirectory());
        if (!Directory.Exists(configDir))
            Directory.CreateDirectory(configDir);

        LoadAllAccounts();
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

    public void EnsureAccountSelected(ulong contentId, string? aliasHint = null)
    {
        ActiveCharacterKey = "";

        if (contentId == 0)
        {
            CurrentAccountId = "";
            log.Warning("Cannot select an active account with content ID 0");
            return;
        }

        var accountId = contentId.ToString("X");
        log.Information($"EnsureAccountSelected: ContentId={contentId:X16}, AccountId={accountId}");
        if (!accounts.TryGetValue(accountId, out var account))
        {
            // Migration: if only one legacy account exists, move it to the new ID
            if (accounts.Count == 1)
            {
                var kvp = accounts.First();
                var oldId = kvp.Key;
                account = kvp.Value;
                accounts.Remove(oldId);
                account.AccountId = accountId;
                accounts[accountId] = account;

                try
                {
                    var oldFile = Path.Combine(configDir, $"{oldId}_FrenRider.json");
                    if (File.Exists(oldFile))
                        File.Delete(oldFile);
                }
                catch (Exception ex)
                {
                    log.Warning($"Failed to delete legacy config file for {oldId}: {ex.Message}");
                }

                SaveAccount(accountId);
                log.Information($"Migrated legacy account {oldId} -> {accountId}");
            }
            else
            {
                account = new AccountConfig
                {
                    AccountId = accountId,
                    AccountAlias = !string.IsNullOrWhiteSpace(aliasHint)
                        ? aliasHint
                        : $"Account {accounts.Count + 1}",
                };
                accounts[accountId] = account;
                SaveAccount(accountId);
                log.Information($"Created account {accountId} ({account.AccountAlias})");
            }
        }
        else if (!string.IsNullOrWhiteSpace(aliasHint) && string.IsNullOrWhiteSpace(account.AccountAlias))
        {
            account.AccountAlias = aliasHint;
            SaveAccount(accountId);
        }

        CurrentAccountId = accountId;
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";
        if (string.IsNullOrEmpty(CurrentAccountId))
        {
            ActiveCharacterKey = "";
            log.Warning($"Cannot activate character {charKey} without a content-ID-selected account");
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

        ActiveCharacterKey = charKey;
        if (added)
        {
            SaveAccount(CurrentAccountId);
            log.Information($"Added character {charKey} to account {CurrentAccountId}");
        }
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
        var newId = Guid.NewGuid().ToString("N")[..8];
        var newAccount = new AccountConfig
        {
            AccountId = newId,
            AccountAlias = alias,
        };
        accounts[newId] = newAccount;
        SaveAccount(newId);
        return newId;
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
