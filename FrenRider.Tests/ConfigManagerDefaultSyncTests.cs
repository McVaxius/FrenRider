using System.Reflection;
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
    public void DaedalusTargetModeSyncsByRowAndCombatTab()
    {
        var account = CreateAccount();
        account.DefaultConfig.DaedalusTargetMode = DaedalusTargetMode.Split;
        var target = account.Characters["Alt One@World"];
        target.DaedalusTargetMode = DaedalusTargetMode.None;
        target.RotationPlugin = 1;

        var rowCount = ConfigManager.ApplyDefaultSettingToAllCharacters(
            account,
            "Daedalus Engage Mode");

        Assert.Equal(2, rowCount);
        Assert.Equal(DaedalusTargetMode.Split, target.DaedalusTargetMode);
        Assert.Equal(1, target.RotationPlugin);

        account.DefaultConfig.DaedalusTargetMode = DaedalusTargetMode.KillAdds;
        var tabCount = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Combat");

        Assert.Equal(2, tabCount);
        Assert.Equal(DaedalusTargetMode.KillAdds, target.DaedalusTargetMode);
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
    public void ProfileTabSyncCopiesIndependentRespawnScopes()
    {
        var account = CreateAccount();
        account.DefaultConfig.RespawnOutsideDuties = true;
        account.DefaultConfig.RespawnOutsideDutiesDelaySeconds = 17;
        account.DefaultConfig.RespawnInsideDuties = true;
        account.DefaultConfig.RespawnInsideDutiesDelaySeconds = 23;

        var target = account.Characters["Alt One@World"];
        target.RespawnOutsideDuties = false;
        target.RespawnOutsideDutiesDelaySeconds = 31;
        target.RespawnInsideDuties = false;
        target.RespawnInsideDutiesDelaySeconds = 37;

        var count = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Profile");

        Assert.Equal(2, count);
        Assert.True(target.RespawnOutsideDuties);
        Assert.Equal(17, target.RespawnOutsideDutiesDelaySeconds);
        Assert.True(target.RespawnInsideDuties);
        Assert.Equal(23, target.RespawnInsideDutiesDelaySeconds);
    }

    [Fact]
    public void FullSyncCopiesEnabledState()
    {
        var account = CreateAccount();
        account.DefaultConfig.Enabled = true;

        foreach (var character in account.Characters.Values)
            character.Enabled = false;

        ConfigManager.ApplyDefaultToAllCharacters(account);

        Assert.All(account.Characters.Values, character => Assert.True(character.Enabled));
    }

    [Fact]
    public void EnabledNamedSettingSyncsFromDefaultToLocalCharactersOnly()
    {
        var account = CreateAccount();
        account.DefaultConfig.Enabled = true;
        account.RemoteProfiles.Add(new RemoteProfileRow
        {
            Config = new CharacterConfig
            {
                Enabled = false,
            },
        });

        foreach (var character in account.Characters.Values)
            character.Enabled = false;

        var count = ConfigManager.ApplyDefaultSettingToAllCharacters(
            account,
            "Fren Rider enabled by default");

        Assert.Equal(account.Characters.Count, count);
        Assert.All(account.Characters.Values, character => Assert.True(character.Enabled));
        Assert.False(account.RemoteProfiles[0].Config.Enabled);
    }

    [Fact]
    public void ProfileTabSyncCopiesEnabledState()
    {
        var account = CreateAccount();
        account.DefaultConfig.Enabled = true;
        account.DefaultConfig.Cling = 9f;

        foreach (var character in account.Characters.Values)
        {
            character.Enabled = false;
            character.Cling = 2f;
        }

        var count = ConfigManager.ApplyDefaultTabToAllCharacters(account, "Profile");

        Assert.Equal(account.Characters.Count, count);
        Assert.All(account.Characters.Values, character =>
        {
            Assert.True(character.Enabled);
            Assert.Equal(2f, character.Cling);
        });
    }

    [Fact]
    public void ProfileAcceptanceSyncsFromDefaultToLocalCharactersOnly()
    {
        var account = CreateAccount();
        account.DefaultConfig.ProfileAcceptancePolicy = FrenRiderProfileAcceptancePolicy.Permanent;
        account.RemoteProfiles.Add(new RemoteProfileRow
        {
            OwnerId = "owner",
            IslandId = "island",
            CharacterId = "opaque",
            Config = new CharacterConfig
            {
                ProfileAcceptancePolicy = FrenRiderProfileAcceptancePolicy.Off,
            },
        });

        var count = ConfigManager.ApplyDefaultSettingToAllCharacters(account, "DAD Profile Acceptance");

        Assert.Equal(account.Characters.Count, count);
        Assert.All(account.Characters.Values, character =>
            Assert.Equal(FrenRiderProfileAcceptancePolicy.Permanent, character.ProfileAcceptancePolicy));
        Assert.Equal(FrenRiderProfileAcceptancePolicy.Off, account.RemoteProfiles[0].Config.ProfileAcceptancePolicy);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void AdsExitMethodRowSyncCopiesCompleteExitSelection(
        bool useAdsLeaveAfterAdsDuty,
        bool exitAfterDutyEnds,
        bool leaveWhenAllLeft)
    {
        var account = CreateAccount();
        account.DefaultConfig.UseAdsLeaveAfterAdsDuty = useAdsLeaveAfterAdsDuty;
        account.DefaultConfig.ExitAfterDutyEnds = exitAfterDutyEnds;
        account.DefaultConfig.LeaveWhenAllLeft = leaveWhenAllLeft;
        account.DefaultConfig.ExitAfterDutySeconds = 91;
        account.DefaultConfig.FrenName = "Must Not Sync";
        account.DefaultConfig.Enabled = true;

        var index = 0;
        foreach (var target in account.Characters.Values)
        {
            target.UseAdsLeaveAfterAdsDuty = !useAdsLeaveAfterAdsDuty;
            target.ExitAfterDutyEnds = !exitAfterDutyEnds;
            target.LeaveWhenAllLeft = !leaveWhenAllLeft;
            target.Enabled = index++ % 2 == 0;
        }

        var preserved = account.Characters.ToDictionary(
            pair => pair.Key,
            pair => new
            {
                pair.Value.ExitAfterDutySeconds,
                pair.Value.FrenName,
                pair.Value.Enabled,
            });

        var count = ConfigManager.ApplyDefaultSettingToAllCharacters(account, "ADS Exit Method");

        Assert.Equal(account.Characters.Count, count);
        foreach (var pair in account.Characters)
        {
            var target = pair.Value;
            var original = preserved[pair.Key];
            Assert.Equal(useAdsLeaveAfterAdsDuty, target.UseAdsLeaveAfterAdsDuty);
            Assert.Equal(exitAfterDutyEnds, target.ExitAfterDutyEnds);
            Assert.Equal(leaveWhenAllLeft, target.LeaveWhenAllLeft);
            Assert.Equal(original.ExitAfterDutySeconds, target.ExitAfterDutySeconds);
            Assert.Equal(original.FrenName, target.FrenName);
            Assert.Equal(original.Enabled, target.Enabled);
        }
    }

    [Fact]
    public void AdsDutyFamilyRowSyncCopiesHandoffDelayWithTheFamilySettings()
    {
        var account = CreateAccount();
        account.DefaultConfig.SetAdsDutyFamilySettings(AdsDutyCategory.Solo, true, 2, 37);
        account.DefaultConfig.SetAdsDutyFamilySettings(AdsDutyCategory.FourMan, true, 1, 18);

        var target = account.Characters["Alt One@World"];
        target.SetAdsDutyFamilySettings(AdsDutyCategory.Solo, false, 0, 2);
        target.SetAdsDutyFamilySettings(AdsDutyCategory.FourMan, false, 3, 99);

        var count = ConfigManager.ApplyDefaultSettingToAllCharacters(account, "ADS Solo");

        Assert.Equal(account.Characters.Count, count);
        Assert.Equal(
            account.DefaultConfig.GetAdsDutyFamilySettings(AdsDutyCategory.Solo),
            target.GetAdsDutyFamilySettings(AdsDutyCategory.Solo));
        Assert.Equal(
            new AdsDutyFamilySettings(false, 3, 99),
            target.GetAdsDutyFamilySettings(AdsDutyCategory.FourMan));
    }

    [Fact]
    public void FullSyncCopiesEveryPersistedCharacterSetting()
    {
        var account = CreateAccount();
        AssignDistinctPersistedValues(account.DefaultConfig, 0, true, 0);
        account.DefaultConfig.UseAdsLeaveAfterAdsDuty = true;
        account.DefaultConfig.ExitAfterDutyEnds = false;
        account.DefaultConfig.LeaveWhenAllLeft = false;

        foreach (var target in account.Characters.Values)
        {
            AssignDistinctPersistedValues(target, 100, false, 1);
            target.UseAdsLeaveAfterAdsDuty = false;
            target.ExitAfterDutyEnds = true;
            target.LeaveWhenAllLeft = true;

            foreach (var property in PersistedCharacterProperties)
                AssertPropertyValuesNotEqual(property, account.DefaultConfig, target);
        }

        var count = ConfigManager.ApplyDefaultToAllCharacters(account);

        Assert.Equal(account.Characters.Count, count);
        foreach (var target in account.Characters.Values)
        {
            foreach (var property in PersistedCharacterProperties)
                AssertPropertyValuesEqual(property, account.DefaultConfig, target);
        }
    }

    private static readonly PropertyInfo[] PersistedCharacterProperties = typeof(CharacterConfig)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(property => property.CanRead && property.CanWrite)
        .OrderBy(property => property.Name, StringComparer.Ordinal)
        .ToArray();

    private static void AssignDistinctPersistedValues(
        CharacterConfig config,
        int offset,
        bool boolValue,
        int enumIndex)
    {
        for (var index = 0; index < PersistedCharacterProperties.Length; index++)
        {
            var property = PersistedCharacterProperties[index];
            object value = property.PropertyType == typeof(int) && property.Name.EndsWith("MaturityThreshold", StringComparison.Ordinal)
                ? offset == 0 ? 2 : 1
                : property.PropertyType switch
            {
                var type when type == typeof(bool) => boolValue,
                var type when type == typeof(int) => offset + index + 1,
                var type when type == typeof(float) => offset + index + 0.25f,
                var type when type == typeof(string) => $"value-{offset}-{property.Name}",
                var type when type == typeof(string[]) => new[] { $"value-{offset}-{property.Name}" },
                var type when type == typeof(List<string>) => new List<string> { $"value-{offset}-{property.Name}" },
                var type when type.IsEnum => Enum.GetValues(type).GetValue(enumIndex % Enum.GetValues(type).Length)!,
                _ => throw new InvalidOperationException($"Add a persisted-value test fixture for {property.Name} ({property.PropertyType})."),
            };

            property.SetValue(config, value);
        }
    }

    private static void AssertPropertyValuesEqual(
        PropertyInfo property,
        CharacterConfig expectedConfig,
        CharacterConfig actualConfig)
    {
        var expected = property.GetValue(expectedConfig);
        var actual = property.GetValue(actualConfig);
        if (expected is string[] expectedArray && actual is string[] actualArray)
            Assert.True(expectedArray.SequenceEqual(actualArray), $"{property.Name} array values differ.");
        else if (expected is List<string> expectedList && actual is List<string> actualList)
            Assert.True(expectedList.SequenceEqual(actualList), $"{property.Name} list values differ.");
        else
            Assert.True(Equals(expected, actual), $"{property.Name}: expected {expected}, actual {actual}.");
    }

    private static void AssertPropertyValuesNotEqual(
        PropertyInfo property,
        CharacterConfig expectedConfig,
        CharacterConfig actualConfig)
    {
        var expected = property.GetValue(expectedConfig);
        var actual = property.GetValue(actualConfig);
        if (expected is string[] expectedArray && actual is string[] actualArray)
            Assert.False(expectedArray.SequenceEqual(actualArray), $"{property.Name} must begin with distinct array values.");
        else if (expected is List<string> expectedList && actual is List<string> actualList)
            Assert.False(expectedList.SequenceEqual(actualList), $"{property.Name} must begin with distinct list values.");
        else
            Assert.NotEqual(expected, actual);
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
