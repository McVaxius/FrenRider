using FrenRider.Models;
using FrenRider.Services;
using System.Text.Json;

namespace FrenRider.Tests;

public sealed class ConfigManagerActiveCharacterTests
{
    private const string ActiveCharacterKey = "Active Character@Excalibur";
    private const string OtherCharacterKey = "Other Character@Excalibur";
    private const string DifferentAccountCharacterKey = "Different Account@Excalibur";

    [Fact]
    public void LauncherAccountSelectionGroupsCharactersSeparatesAccountsAndPreservesLegacyProfile()
    {
        var launcherRoot = Path.Combine(Path.GetTempPath(), "FrenRiderTests", Path.GetRandomFileName());
        var configDirectory = Path.Combine(launcherRoot, "pluginConfigs", "FrenRider");
        Directory.CreateDirectory(configDirectory);

        try
        {
            const string legacyAccountId = "legacy-content-id";
            const string firstLauncherAccountId = "raw-launcher-account-a";
            const string secondLauncherAccountId = "raw-launcher-account-b";
            var legacyAccount = new AccountConfig
            {
                AccountId = legacyAccountId,
                AccountAlias = "Legacy W/X/V Account",
                DefaultConfig = new CharacterConfig
                {
                    FrenName = "Legacy Default@Excalibur",
                    Cling = 4.5f,
                },
                Characters = new Dictionary<string, CharacterConfig>
                {
                    [ActiveCharacterKey] = new()
                    {
                        FrenName = "Preserved Fren@Excalibur",
                        Cling = 7.25f,
                        Enabled = true,
                    },
                },
            };
            WriteAccount(configDirectory, legacyAccount);
            WriteLauncherConfig(launcherRoot, firstLauncherAccountId);

            var accounts = new Dictionary<string, AccountConfig>
            {
                [legacyAccountId] = legacyAccount,
            };
            var persistenceOrder = new List<string>();
            Assert.True(ConfigManager.TryReadLauncherAccountId(configDirectory, out var selectedAccountId, out var readError), readError);
            Assert.Equal(firstLauncherAccountId, selectedAccountId);

            Assert.True(ConfigManager.TrySelectLauncherAccount(
                accounts,
                selectedAccountId,
                ActiveCharacterKey,
                "Account A",
                SaveAccount,
                DeleteReplacedLegacyAccount,
                out var selectedAccount,
                out var selectionFailure), selectionFailure);

            var firstAccount = Assert.IsType<AccountConfig>(selectedAccount);
            Assert.Equal(firstLauncherAccountId, firstAccount.AccountId);
            Assert.Equal("Legacy W/X/V Account", firstAccount.AccountAlias);
            Assert.Equal("Legacy Default@Excalibur", firstAccount.DefaultConfig.FrenName);
            Assert.Equal(4.5f, firstAccount.DefaultConfig.Cling);
            Assert.Equal("Preserved Fren@Excalibur", firstAccount.Characters[ActiveCharacterKey].FrenName);
            Assert.Equal(7.25f, firstAccount.Characters[ActiveCharacterKey].Cling);
            Assert.True(firstAccount.Characters[ActiveCharacterKey].Enabled);
            Assert.True(File.Exists(Path.Combine(configDirectory, $"{firstLauncherAccountId}_FrenRider.json")));
            Assert.False(File.Exists(Path.Combine(configDirectory, $"{legacyAccountId}_FrenRider.json")));
            Assert.True(
                persistenceOrder.IndexOf($"save:{firstLauncherAccountId}")
                < persistenceOrder.IndexOf($"delete:{legacyAccountId}"));

            Assert.True(ConfigManager.TrySelectLauncherAccount(
                accounts,
                firstLauncherAccountId,
                OtherCharacterKey,
                "Account A",
                SaveAccount,
                DeleteReplacedLegacyAccount,
                out selectedAccount,
                out selectionFailure), selectionFailure);
            Assert.True(ConfigManager.TryEnsureCharacterExists(selectedAccount, OtherCharacterKey, out var addedCharacter));
            Assert.True(addedCharacter);
            Assert.True(SaveAccount(firstLauncherAccountId));

            Assert.Same(firstAccount, selectedAccount);
            Assert.Equal(2, firstAccount.Characters.Count);
            Assert.Contains(ActiveCharacterKey, firstAccount.Characters.Keys);
            Assert.Contains(OtherCharacterKey, firstAccount.Characters.Keys);

            WriteLauncherConfig(launcherRoot, secondLauncherAccountId);
            Assert.True(ConfigManager.TryReadLauncherAccountId(configDirectory, out selectedAccountId, out readError), readError);
            Assert.Equal(secondLauncherAccountId, selectedAccountId);
            Assert.True(ConfigManager.TrySelectLauncherAccount(
                accounts,
                selectedAccountId,
                DifferentAccountCharacterKey,
                "Account B",
                SaveAccount,
                DeleteReplacedLegacyAccount,
                out selectedAccount,
                out selectionFailure), selectionFailure);
            Assert.True(ConfigManager.TryEnsureCharacterExists(selectedAccount, DifferentAccountCharacterKey, out addedCharacter));
            Assert.True(addedCharacter);
            Assert.True(SaveAccount(secondLauncherAccountId));

            var secondAccount = Assert.IsType<AccountConfig>(selectedAccount);
            Assert.NotSame(firstAccount, secondAccount);
            Assert.Equal(secondLauncherAccountId, secondAccount.AccountId);
            Assert.Single(secondAccount.Characters);
            Assert.Contains(DifferentAccountCharacterKey, secondAccount.Characters.Keys);
            Assert.DoesNotContain(ActiveCharacterKey, secondAccount.Characters.Keys);
            Assert.True(File.Exists(Path.Combine(configDirectory, $"{secondLauncherAccountId}_FrenRider.json")));

            WriteLauncherConfig(launcherRoot, firstLauncherAccountId);
            var reloadedAccounts = Directory
                .GetFiles(configDirectory, "*_FrenRider.json")
                .Select(file => JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(file))!)
                .ToDictionary(account => account.AccountId);
            Assert.True(ConfigManager.TryReadLauncherAccountId(configDirectory, out selectedAccountId, out readError), readError);
            Assert.True(ConfigManager.TrySelectLauncherAccount(
                reloadedAccounts,
                selectedAccountId,
                OtherCharacterKey,
                "Account A",
                accountId =>
                {
                    WriteAccount(configDirectory, reloadedAccounts[accountId]);
                    return true;
                },
                _ => { },
                out selectedAccount,
                out selectionFailure), selectionFailure);

            var reloadedFirstAccount = Assert.IsType<AccountConfig>(selectedAccount);
            Assert.Equal(firstLauncherAccountId, reloadedFirstAccount.AccountId);
            Assert.Equal(2, reloadedFirstAccount.Characters.Count);
            Assert.Equal("Preserved Fren@Excalibur", reloadedFirstAccount.Characters[ActiveCharacterKey].FrenName);

            bool SaveAccount(string accountId)
            {
                persistenceOrder.Add($"save:{accountId}");
                if (!accounts.TryGetValue(accountId, out var account))
                    return false;

                WriteAccount(configDirectory, account);
                return true;
            }

            void DeleteReplacedLegacyAccount(string accountId)
            {
                persistenceOrder.Add($"delete:{accountId}");
                var file = Path.Combine(configDirectory, $"{accountId}_FrenRider.json");
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
        finally
        {
            Directory.Delete(launcherRoot, recursive: true);
        }
    }

    [Fact]
    public void ActiveLookupReturnsOnlyExactProfileFromCurrentAccount()
    {
        var account = CreateAccount();

        var resolved = ConfigManager.ResolveActiveConfigOrDisabled(
            account,
            account.AccountId,
            ActiveCharacterKey);

        Assert.Same(account.Characters[ActiveCharacterKey], resolved);
        Assert.NotSame(account.DefaultConfig, resolved);
        Assert.NotSame(account.Characters[OtherCharacterKey], resolved);
        Assert.Equal("Active Fren@Excalibur", resolved.FrenName);
    }

    [Theory]
    [InlineData(null, ActiveCharacterKey)]
    [InlineData("", ActiveCharacterKey)]
    [InlineData("different-account", ActiveCharacterKey)]
    [InlineData("account", null)]
    [InlineData("account", "")]
    [InlineData("account", "Missing Character@Excalibur")]
    public void MissingOrMismatchedActiveStateReturnsFreshDisabledConfig(
        string? currentAccountId,
        string? activeCharacterKey)
    {
        var account = CreateAccount();

        var first = ConfigManager.ResolveActiveConfigOrDisabled(account, currentAccountId, activeCharacterKey);
        first.Enabled = true;
        var second = ConfigManager.ResolveActiveConfigOrDisabled(account, currentAccountId, activeCharacterKey);

        Assert.False(second.Enabled);
        Assert.False(second.AutoSyncFate);
        Assert.False(second.AdsEnableChestOpening);
        Assert.False(second.RaiseOfferAutoAccept);
        Assert.False(second.TeleportOfferAutoAccept);
        Assert.False(second.PartyInviteAutoAccept);
        Assert.False(second.ExitAfterDutyEnds);
        Assert.NotSame(first, second);
        Assert.NotSame(account.DefaultConfig, second);
        Assert.NotSame(account.Characters[ActiveCharacterKey], second);
        Assert.NotSame(account.Characters[OtherCharacterKey], second);
    }

    [Fact]
    public void CharacterMissingFromCurrentAccountDoesNotResolveFromAnotherAccount()
    {
        var selectedAccount = CreateAccount();
        selectedAccount.Characters.Remove(ActiveCharacterKey);
        var otherAccount = CreateAccount("other-account");
        otherAccount.Characters[ActiveCharacterKey].FrenName = "Wrong Account Fren@Excalibur";

        var resolved = ConfigManager.ResolveActiveConfigOrDisabled(
            selectedAccount,
            selectedAccount.AccountId,
            ActiveCharacterKey);

        Assert.False(resolved.Enabled);
        Assert.NotSame(selectedAccount.DefaultConfig, resolved);
        Assert.NotSame(otherAccount.Characters[ActiveCharacterKey], resolved);
        Assert.NotEqual("Wrong Account Fren@Excalibur", resolved.FrenName);
    }

    [Fact]
    public void EditingDefaultOrOtherProfileCannotChangeActiveProfile()
    {
        var account = CreateAccount();
        var active = ConfigManager.ResolveActiveConfigOrDisabled(account, account.AccountId, ActiveCharacterKey);
        var defaultEditing = ConfigManager.ResolveEditingConfigOrDisabled(account, "");
        var otherEditing = ConfigManager.ResolveEditingConfigOrDisabled(account, OtherCharacterKey);

        defaultEditing.FrenName = "Edited Default@Excalibur";
        defaultEditing.Enabled = true;
        otherEditing.FrenName = "Edited Other@Excalibur";
        otherEditing.Enabled = false;

        Assert.Same(account.DefaultConfig, defaultEditing);
        Assert.Same(account.Characters[OtherCharacterKey], otherEditing);
        Assert.Same(account.Characters[ActiveCharacterKey], active);
        Assert.Equal("Active Fren@Excalibur", active.FrenName);
        Assert.True(active.Enabled);
    }

    [Fact]
    public void CharacterCreationUsesOnlyLauncherSelectedAccount()
    {
        var selectedAccount = CreateAccount();
        selectedAccount.Characters.Remove(ActiveCharacterKey);
        selectedAccount.DefaultConfig.FrenName = "Selected Default@Excalibur";
        var otherAccount = CreateAccount("other-account");
        otherAccount.Characters[ActiveCharacterKey].FrenName = "Other Account Existing@Excalibur";

        var succeeded = ConfigManager.TryEnsureCharacterExists(
            selectedAccount,
            ActiveCharacterKey,
            out var added);

        Assert.True(succeeded);
        Assert.True(added);
        Assert.Equal("Selected Default@Excalibur", selectedAccount.Characters[ActiveCharacterKey].FrenName);
        Assert.NotSame(selectedAccount.DefaultConfig, selectedAccount.Characters[ActiveCharacterKey]);
        Assert.Equal("Other Account Existing@Excalibur", otherAccount.Characters[ActiveCharacterKey].FrenName);
    }

    [Fact]
    public void ActiveProfileCannotBeDeletedWhileOtherProfileCan()
    {
        var account = CreateAccount();

        var deletedActive = ConfigManager.TryDeleteCharacter(account, ActiveCharacterKey, ActiveCharacterKey);
        var deletedOther = ConfigManager.TryDeleteCharacter(account, ActiveCharacterKey, OtherCharacterKey);

        Assert.False(deletedActive);
        Assert.True(deletedOther);
        Assert.True(account.Characters.ContainsKey(ActiveCharacterKey));
        Assert.False(account.Characters.ContainsKey(OtherCharacterKey));
    }

    private static AccountConfig CreateAccount(string accountId = "account")
        => new()
        {
            AccountId = accountId,
            DefaultConfig = new CharacterConfig
            {
                FrenName = "Default Fren@Excalibur",
                Enabled = true,
            },
            Characters = new Dictionary<string, CharacterConfig>
            {
                [ActiveCharacterKey] = new()
                {
                    FrenName = "Active Fren@Excalibur",
                    Enabled = true,
                },
                [OtherCharacterKey] = new()
                {
                    FrenName = "Other Fren@Excalibur",
                    Enabled = true,
                },
            },
        };

    private static void WriteLauncherConfig(string launcherRoot, string accountId)
        => File.WriteAllText(
            Path.Combine(launcherRoot, "launcherConfigV3.json"),
            JsonSerializer.Serialize(new { CurrentAccountId = accountId }));

    private static void WriteAccount(string configDirectory, AccountConfig account)
        => File.WriteAllText(
            Path.Combine(configDirectory, $"{account.AccountId}_FrenRider.json"),
            JsonSerializer.Serialize(account));
}
