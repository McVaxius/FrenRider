using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ConfigManagerDefaultSyncTests
{
    [Fact]
    public void FullSyncCopiesMappedCharacterSettingsToCurrentAccountCharacters()
    {
        var account = CreateAccount();
        account.DefaultConfig.FrenName = "Default Fren@Gilgamesh";
        account.DefaultConfig.Cling = 7.5f;
        account.DefaultConfig.RotationPlugin = 3;
        account.DefaultConfig.NudgeInDutyWhenFrenNotNearbyOrInZone = true;
        account.DefaultConfig.ExitAfterDutySeconds = 42;
        account.DefaultConfig.FeedMeItemId = 123;
        account.DefaultConfig.FeedMeItem = "Default Food";

        var otherAccount = CreateAccount();
        otherAccount.DefaultConfig.FrenName = "Other Default";

        var count = ConfigManager.ApplyDefaultToAllCharacters(account);

        Assert.Equal(2, count);
        foreach (var character in account.Characters.Values)
        {
            Assert.Equal("Default Fren@Gilgamesh", character.FrenName);
            Assert.Equal(7.5f, character.Cling);
            Assert.Equal(3, character.RotationPlugin);
            Assert.True(character.NudgeInDutyWhenFrenNotNearbyOrInZone);
            Assert.Equal(42, character.ExitAfterDutySeconds);
            Assert.Equal(123, character.FeedMeItemId);
            Assert.Equal("Default Food", character.FeedMeItem);
        }

        Assert.NotEqual("Default Fren@Gilgamesh", otherAccount.Characters["Alt One@World"].FrenName);
    }

    [Fact]
    public void TabSyncCopiesOnlyTheRequestedTab()
    {
        var account = CreateAccount();
        account.DefaultConfig.FrenName = "Default Fren";
        account.DefaultConfig.Cling = 9f;
        account.DefaultConfig.AutoSyncFate = false;
        account.DefaultConfig.RotationPlugin = 1;
        account.DefaultConfig.CleanupMode = FrenRiderCleanupMode.TurnEverythingOff;

        var target = account.Characters["Alt One@World"];
        target.FrenName = "Keep Me";
        target.Cling = 2f;
        target.AutoSyncFate = true;
        target.RotationPlugin = 3;
        target.CleanupMode = FrenRiderCleanupMode.RestoreSnapshot;

        var followCount = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Follow");

        Assert.Equal(2, followCount);
        Assert.Equal("Keep Me", target.FrenName);
        Assert.Equal(9f, target.Cling);
        Assert.False(target.AutoSyncFate);
        Assert.Equal(3, target.RotationPlugin);
        Assert.Equal(FrenRiderCleanupMode.RestoreSnapshot, target.CleanupMode);

        var combatCount = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Combat");

        Assert.Equal(2, combatCount);
        Assert.Equal(FrenRiderCleanupMode.TurnEverythingOff, target.CleanupMode);
    }

    [Fact]
    public void PerSettingSyncCopiesOnlyThatSetting()
    {
        var account = CreateAccount();
        account.DefaultConfig.FrenName = "Default Fren";
        account.DefaultConfig.Cling = 8f;

        var target = account.Characters["Alt One@World"];
        target.FrenName = "Old Fren";
        target.Cling = 2f;

        var count = ConfigManager.ApplyDefaultSettingToAllCharacters(
            account,
            (source, character) => character.FrenName = source.FrenName);

        Assert.Equal(2, count);
        Assert.Equal("Default Fren", target.FrenName);
        Assert.Equal(2f, target.Cling);
    }

    [Fact]
    public void FullSyncDeepCopiesMutableListsAndArrays()
    {
        var account = CreateAccount();
        account.DefaultConfig.InviteWhitelist = new List<string> { "Trusted One" };
        account.DefaultConfig.CustomIdleList = new[] { "/wave", "/dance" };

        var target = account.Characters["Alt One@World"];

        ConfigManager.ApplyDefaultToAllCharacters(account);

        Assert.Equal(account.DefaultConfig.InviteWhitelist, target.InviteWhitelist);
        Assert.NotSame(account.DefaultConfig.InviteWhitelist, target.InviteWhitelist);
        Assert.Equal(account.DefaultConfig.CustomIdleList, target.CustomIdleList);
        Assert.NotSame(account.DefaultConfig.CustomIdleList, target.CustomIdleList);

        account.DefaultConfig.InviteWhitelist.Add("Late Add");
        account.DefaultConfig.CustomIdleList[0] = "/changed";

        Assert.Equal(new[] { "Trusted One" }, target.InviteWhitelist);
        Assert.Equal(new[] { "/wave", "/dance" }, target.CustomIdleList);
    }

    [Fact]
    public void ProfileTabSyncCopiesDutyNudgeFallbackSetting()
    {
        var account = CreateAccount();
        account.DefaultConfig.NudgeInDutyWhenFrenNotNearbyOrInZone = true;

        var target = account.Characters["Alt One@World"];
        target.NudgeInDutyWhenFrenNotNearbyOrInZone = false;
        target.Cling = 2f;

        var count = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Profile");

        Assert.Equal(2, count);
        Assert.True(target.NudgeInDutyWhenFrenNotNearbyOrInZone);
        Assert.Equal(2f, target.Cling);
    }

    [Fact]
    public void FullSyncDoesNotCopyHiddenRuntimeEnabledState()
    {
        var account = CreateAccount();
        account.DefaultConfig.Enabled = true;

        foreach (var character in account.Characters.Values)
            character.Enabled = false;

        ConfigManager.ApplyDefaultToAllCharacters(account);

        Assert.All(account.Characters.Values, character => Assert.False(character.Enabled));
    }

    private static AccountConfig CreateAccount()
        => new()
        {
            AccountId = "account",
            DefaultConfig = new CharacterConfig(),
            Characters =
            {
                ["Alt One@World"] = new CharacterConfig
                {
                    FrenName = "Alt One Fren",
                    Cling = 1f,
                    RotationPlugin = 0,
                    ExitAfterDutySeconds = 10,
                    FeedMeItemId = 1,
                    FeedMeItem = "Old Food",
                    InviteWhitelist = new List<string> { "Old Trusted" },
                    CustomIdleList = new[] { "/old" },
                },
                ["Alt Two@World"] = new CharacterConfig
                {
                    FrenName = "Alt Two Fren",
                    Cling = 2f,
                    RotationPlugin = 2,
                    ExitAfterDutySeconds = 20,
                    FeedMeItemId = 2,
                    FeedMeItem = "Other Food",
                    InviteWhitelist = new List<string> { "Other Trusted" },
                    CustomIdleList = new[] { "/other" },
                },
            },
        };
}
