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
    public string SelectedCharacterKey { get; set; } = ""; // "" = default config

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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

    public IReadOnlyDictionary<string, AccountConfig> Accounts => accounts;

    public AccountConfig? GetCurrentAccount()
    {
        if (string.IsNullOrEmpty(CurrentAccountId)) return null;
        return accounts.TryGetValue(CurrentAccountId, out var acc) ? acc : null;
    }

    public CharacterConfig GetActiveConfig()
    {
        var account = GetCurrentAccount();
        if (account == null)
            return new CharacterConfig();

        if (string.IsNullOrEmpty(SelectedCharacterKey))
            return account.DefaultConfig;

        return account.Characters.TryGetValue(SelectedCharacterKey, out var cc)
            ? cc
            : account.DefaultConfig;
    }

    public CharacterConfig GetCurrentCharacterConfig(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null) return new CharacterConfig();
        if (string.IsNullOrEmpty(charKey)) return account.DefaultConfig;
        return account.Characters.TryGetValue(charKey, out var cc) ? cc : account.DefaultConfig;
    }

    public void EnsureCharacterExists(string characterName, string worldName)
    {
        if (string.IsNullOrEmpty(characterName) || string.IsNullOrEmpty(worldName))
            return;

        var charKey = $"{characterName}@{worldName}";

        // Search all accounts for this character
        foreach (var kvp in accounts)
        {
            if (kvp.Value.Characters.ContainsKey(charKey))
            {
                CurrentAccountId = kvp.Key;
                SelectedCharacterKey = charKey;
                return;
            }
        }

        // Character not found in any account
        if (accounts.Count == 0)
        {
            // Create first account
            var newId = Guid.NewGuid().ToString("N")[..8];
            var newAccount = new AccountConfig
            {
                AccountId = newId,
                AccountAlias = "Account 1",
            };
            newAccount.Characters[charKey] = newAccount.DefaultConfig.Clone();
            accounts[newId] = newAccount;
            CurrentAccountId = newId;
            SelectedCharacterKey = charKey;
            SaveAccount(newId);
            log.Information($"Created new account {newId} with character {charKey}");
        }
        else if (accounts.Count == 1)
        {
            // Add to the only existing account
            var acc = accounts.First();
            acc.Value.Characters[charKey] = acc.Value.DefaultConfig.Clone();
            CurrentAccountId = acc.Key;
            SelectedCharacterKey = charKey;
            SaveAccount(acc.Key);
            log.Information($"Added character {charKey} to account {acc.Key}");
        }
        else
        {
            // Multiple accounts exist - add to current or first
            var targetId = !string.IsNullOrEmpty(CurrentAccountId) && accounts.ContainsKey(CurrentAccountId)
                ? CurrentAccountId
                : accounts.First().Key;
            accounts[targetId].Characters[charKey] = accounts[targetId].DefaultConfig.Clone();
            CurrentAccountId = targetId;
            SelectedCharacterKey = charKey;
            SaveAccount(targetId);
            log.Information($"Added character {charKey} to account {targetId}");
        }
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

    public void ResetCharacterTabToDefault(string charKey, string tabName)
    {
        var account = GetCurrentAccount();
        if (account == null) return;

        var target = string.IsNullOrEmpty(charKey) ? account.DefaultConfig : null;
        if (target == null && account.Characters.TryGetValue(charKey!, out var cc))
            target = cc;
        if (target == null) return;

        var source = string.IsNullOrEmpty(charKey) ? new CharacterConfig() : account.DefaultConfig;

        switch (tabName)
        {
            case "Party":
                target.FrenName = source.FrenName;
                target.FlyYouFools = source.FlyYouFools;
                target.FoolFlier = source.FoolFlier;
                target.ForceGysahl = source.ForceGysahl;
                target.CompanionStrat = source.CompanionStrat;
                target.UpdateInterval = source.UpdateInterval;
                break;
            case "Distance":
                target.Cling = source.Cling;
                target.ClingType = source.ClingType;
                target.ClingTypeDuty = source.ClingTypeDuty;
                target.SocialDistancing = source.SocialDistancing;
                target.SocialDistancingIndoors = source.SocialDistancingIndoors;
                target.SocialDistanceXWiggle = source.SocialDistanceXWiggle;
                target.SocialDistanceZWiggle = source.SocialDistanceZWiggle;
                target.MaxBistance = source.MaxBistance;
                target.MaxBistanceForay = source.MaxBistanceForay;
                target.DDDistance = source.DDDistance;
                target.FollowInCombat = source.FollowInCombat;
                target.FDistance = source.FDistance;
                target.Formation = source.Formation;
                target.HClingReset = source.HClingReset;
                break;
            case "Combat":
                target.AutoRotationType = source.AutoRotationType;
                target.AutoRotationTypeDD = source.AutoRotationTypeDD;
                target.AutoRotationTypeFATE = source.AutoRotationTypeFATE;
                target.RotationPlugin = source.RotationPlugin;
                target.RotationPluginForay = source.RotationPluginForay;
                target.BossModAI = source.BossModAI;
                target.PositionalInCombat = source.PositionalInCombat;
                target.MaxAIDistance = source.MaxAIDistance;
                target.LimitPct = source.LimitPct;
                target.RotationType = source.RotationType;
                break;
            case "Misc":
                target.FulfType = source.FulfType;
                target.FeedMeItem = source.FeedMeItem;
                target.FeedMeSearch = source.FeedMeSearch;
                target.XpItem = source.XpItem;
                target.Repair = source.Repair;
                target.TornClothes = source.TornClothes;
                target.CbtEdse = source.CbtEdse;
                target.SpamPrinter = source.SpamPrinter;
                target.IdleAction = source.IdleAction;
                target.IdleActionMode = source.IdleActionMode;
                target.IdleListMode = source.IdleListMode;
                target.CustomIdleList = (string[])source.CustomIdleList.Clone();
                target.IdleTicksBeforeAction = source.IdleTicksBeforeAction;
                break;
        }

        SaveCurrentAccount();
    }

    public bool DeleteCharacter(string charKey)
    {
        var account = GetCurrentAccount();
        if (account == null || string.IsNullOrEmpty(charKey)) return false;
        if (!account.Characters.ContainsKey(charKey)) return false;

        account.Characters.Remove(charKey);
        if (SelectedCharacterKey == charKey)
            SelectedCharacterKey = "";

        SaveCurrentAccount();
        log.Information($"Deleted character config: {charKey}");
        return true;
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

    private void SaveAccount(string accountId)
    {
        if (!accounts.TryGetValue(accountId, out var account)) return;

        try
        {
            var fileName = $"{accountId}_FrenRider.json";
            var filePath = Path.Combine(configDir, fileName);
            var json = JsonSerializer.Serialize(account, JsonOptions);
            File.WriteAllText(filePath, json);
            log.Debug($"Saved account {accountId}");
        }
        catch (Exception ex)
        {
            log.Error($"Failed to save account {accountId}: {ex.Message}");
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
