using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ConfigManagerActiveCharacterTests
{
    private const string ActiveCharacterKey = "Active Character@Excalibur";
    private const string OtherCharacterKey = "Other Character@Excalibur";

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
    public void CharacterCreationUsesOnlyContentIdSelectedAccount()
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
}
