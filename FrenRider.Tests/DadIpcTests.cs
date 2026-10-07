using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;
using System.Text.Json;
using System.Reflection;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace FrenRider.Tests;

public sealed class DadIpcTests
{
    private const string QuestionableSettingsJson = """{"AdsSoloEnabled":true,"AdsSoloMaturityThreshold":0,"AdsSoloHandoffDelaySeconds":10,"AdsFourManEnabled":true,"AdsFourManMaturityThreshold":0,"AdsFourManHandoffDelaySeconds":2,"UseAdsLeaveAfterAdsDuty":true,"ExitAfterDutyEnds":false,"LeaveWhenAllLeft":false,"ExitAfterDutySeconds":20}""";

    [Theory]
    [InlineData(2U, 4, 1, true)]
    [InlineData(2U, 1, 1, false)]
    [InlineData(2U, 8, 1, false)]
    [InlineData(2U, 4, 3, false)]
    [InlineData(3U, 4, 1, false)]
    [InlineData(4U, 4, 1, false)]
    [InlineData(5U, 8, 3, false)]
    [InlineData(0U, 4, 1, false)]
    public void DungeonTargetingScopeExcludesOtherDutyFamilies(uint contentType, int members, int parties, bool accepted)
        => Assert.Equal(accepted, CombatService.IsFourPlayerDungeon(contentType, members, parties));

    [Fact]
    public void DungeonEffectiveSelectionPreservesProfilesAndRetainsNewerIntentionalSelections()
    {
        var profile = new CharacterConfig { RsrAggroType = 3, RotationType = 1, RotationPlugin = 2 };
        var saved = JsonSerializer.Serialize(profile);
        var live = new DungeonRsrLiveOwnership(new RsrLiveTargetingSnapshot(new object(), new object(), "Basic", 30, 2, null, 2));
        var ownership = new DadDungeonRsrAggroOwnership("run", "character", profile, 1, 2, profile.RsrAggroType, live);
        Assert.Equal(0, ownership.ResolveSelection("character", profile));
        Assert.Equal(saved, JsonSerializer.Serialize(profile));
        Assert.Equal(3, ownership.ResolveSelection("other-character", profile));
        Assert.Equal(3, ownership.ResolveSelection("character", profile.Clone()));
        profile.RsrAggroType = 1;
        Assert.Equal(1, ownership.ResolveSelection("character", profile));
        Assert.Equal(1, profile.RotationType);
        Assert.Equal(2, profile.RotationPlugin);
    }

    [Fact]
    public void QuestionableIpcUsesOnlyActiveProfileAndClearsOnBothPluginUnloadsAndLogout()
    {
        var directory = Directory.CreateTempSubdirectory("FrenRiderTests-");
        var providers = new Dictionary<string, Delegate>();
        Delegate? pluginsChanged = null;
        var loggedIn = true;
        var pluginProperties = new[] { "ClientState", "PlayerState" }.ToDictionary(name => name,
            name => typeof(Plugin).GetProperty(name, BindingFlags.Static | BindingFlags.NonPublic)!);
        var previous = pluginProperties.ToDictionary(pair => pair.Key, pair => pair.Value.GetValue(null));
        object Proxy(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = DispatchProxy.Create(type, typeof(QuestionableTestProxy));
            ((QuestionableTestProxy)proxy).Handler = handler;
            return proxy;
        }
        try
        {
            pluginProperties["ClientState"].SetValue(null, Proxy(typeof(IClientState), (method, _) =>
                method.Name == "get_IsLoggedIn" ? loggedIn : throw new InvalidOperationException(method.Name)));
            pluginProperties["PlayerState"].SetValue(null, Proxy(typeof(IPlayerState), (method, _) =>
                method.Name == "get_ContentId" ? 1UL : throw new InvalidOperationException(method.Name)));
            var pi = (IDalamudPluginInterface)Proxy(typeof(IDalamudPluginInterface), (method, args) =>
            {
                if (method.Name == "GetPluginConfigDirectory") return directory.FullName;
                if (method.Name == "add_ActivePluginsChanged") { pluginsChanged = (Delegate)args![0]!; return null; }
                if (method.Name == "remove_ActivePluginsChanged") { pluginsChanged = null; return null; }
                if (method.Name == "GetIpcProvider")
                    return Proxy(method.ReturnType, (call, values) =>
                    {
                        if (call.Name == "RegisterFunc") providers.Add((string)args![0]!, (Delegate)values![0]!);
                        else if (call.Name == "UnregisterFunc") providers.Remove((string)args![0]!);
                        else throw new InvalidOperationException(call.Name);
                        return null;
                    });
                throw new InvalidOperationException(method.Name);
            });
            var log = (IPluginLog)Proxy(typeof(IPluginLog), (_, _) => null);
            var manager = new ConfigManager(pi, log);
            var account = CreateAccount();
            ((Dictionary<string, AccountConfig>)manager.Accounts).Add(account.AccountId, account);
            manager.CurrentAccountId = account.AccountId;
            manager.EnsureCharacterExists("Participant One", "Excalibur");
            var active = manager.GetActiveConfig();
            var other = account.Characters["Other Character@Excalibur"];
            var saved = JsonSerializer.Serialize(account);
            var combat = new CombatService(null!, null!, null!, null!);
            using var ipc = new DadIPC(pi, manager, new FrenTracker(null!), combat, log);
            Assert.IsType<Func<string, uint, bool>>(providers[DadIPC.AcquireDungeonRsrAggroEndpoint]);
            Assert.IsType<Func<string, bool>>(providers[DadIPC.ReleaseDungeonRsrAggroEndpoint]);
            bool Apply(string owner) => ((Func<string, string, bool>)providers[DadIPC.ApplyQuestionableDutySettingsEndpoint])(owner, QuestionableSettingsJson);
            var release = (Func<string, bool>)providers[DadIPC.ReleaseQuestionableDutySettingsEndpoint];
            Assert.True(Apply("run"));
            Assert.False(release("other-run"));
            Assert.Equal(new AdsDutyFamilySettings(true, 0, 10), manager.GetEffectiveAdsDutyFamilySettings(active, AdsDutyCategory.Solo));
            Assert.Equal(new DutyExitSettings(true, false, false, 20), manager.GetEffectiveDutyExitSettings(active));
            Assert.True(manager.IsQuestionableDutyFamilyControlled(manager.GetCurrentCharacterConfig(ActiveCharacterKey), AdsDutyCategory.FourMan));
            foreach (var profile in new[] { other, account.DefaultConfig })
            {
                Assert.False(manager.IsQuestionableDutyFamilyControlled(profile, AdsDutyCategory.Solo));
                Assert.Equal(profile.GetAdsDutyFamilySettings(AdsDutyCategory.Solo), manager.GetEffectiveAdsDutyFamilySettings(profile, AdsDutyCategory.Solo));
                Assert.Equal(DutyExitSettings.FromConfig(profile), manager.GetEffectiveDutyExitSettings(profile));
            }
            Assert.Equal(saved, JsonSerializer.Serialize(account));
            Assert.Empty(directory.EnumerateFiles());
            loggedIn = false;
            ipc.UpdateQuestionableDutySettings();
            loggedIn = true;
            Assert.False(manager.IsQuestionableDutyFamilyControlled(active, AdsDutyCategory.Solo));
            Assert.True(Apply("new-run"));
            pluginsChanged!.DynamicInvoke(Proxy(typeof(IActivePluginsChangedEventArgs), (method, _) =>
                method.Name == "get_AffectedInternalNames" ? new HashSet<string> { "dad" } : null));
            Assert.False(manager.IsQuestionableDutyFamilyControlled(active, AdsDutyCategory.Solo));
            Assert.True(Apply("new-run"));
            ipc.Dispose();
            Assert.False(manager.IsQuestionableDutyFamilyControlled(active, AdsDutyCategory.Solo));
            Assert.Empty(providers);
            using var reloaded = new DadIPC(pi, manager, new FrenTracker(null!), combat, log);
            Assert.True(Apply("new-run"));
            ipc.Dispose(); // Old instance cannot unregister or clear the new instance.
            Assert.True(manager.IsQuestionableDutyFamilyControlled(active, AdsDutyCategory.Solo));
            Assert.Equal(saved, JsonSerializer.Serialize(account));
        }
        finally
        {
            foreach (var (name, value) in previous) pluginProperties[name].SetValue(null, value);
            directory.Delete(true);
        }
    }

    [Fact]
    public void QuestionableOverridePreservesSavedSettingsAndRequiresTheOwner()
    {
        var config = new CharacterConfig { AdsDutyFamilySettingsMigrated = true };
        config.SetAdsDutyFamilySettings(AdsDutyCategory.Solo, false, 3, 120);
        config.SetAdsDutyFamilySettings(AdsDutyCategory.FourMan, false, 3, 90);
        var saved = JsonSerializer.Serialize(config);
        var settings = new QuestionableDutySettingsOverride();

        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        Assert.False(settings.Apply("other-run", QuestionableSettingsJson, "character"));
        Assert.False(settings.Release("other-run"));
        Assert.Equal(new AdsDutyFamilySettings(true, 0, 10), settings.Resolve(config, AdsDutyCategory.Solo, "character"));
        Assert.Equal(new AdsDutyFamilySettings(true, 0, 2), settings.Resolve(config, AdsDutyCategory.FourMan, "character"));
        foreach (var category in Enum.GetValues<AdsDutyCategory>().Except([AdsDutyCategory.Solo, AdsDutyCategory.FourMan]))
            Assert.Equal(config.GetAdsDutyFamilySettings(category), settings.Resolve(config, category, "character"));
        Assert.Equal(saved, JsonSerializer.Serialize(config));

        Assert.True(settings.Release("run"));
        Assert.True(settings.Release("run"));
        Assert.Equal(config.GetAdsDutyFamilySettings(AdsDutyCategory.Solo), settings.Resolve(config, AdsDutyCategory.Solo, "character"));
        Assert.Equal(saved, JsonSerializer.Serialize(config));
    }

    [Theory]
    [InlineData("")]
    [InlineData("other-character")]
    public void QuestionableOverrideClearsOnLogoutOrCharacterChange(string nextCharacter)
    {
        var settings = new QuestionableDutySettingsOverride();
        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        settings.ObserveCharacter(nextCharacter);
        Assert.False(settings.Controls(AdsDutyCategory.Solo, "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson, ""));
        Assert.True(settings.Apply("new-run", QuestionableSettingsJson, "character"));
        settings.Clear(); // Plugin unload; reload starts with no owner.
        Assert.False(settings.Controls(AdsDutyCategory.FourMan, "character"));
        Assert.True(settings.Apply("new-run", QuestionableSettingsJson, "character"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("broken")]
    [InlineData("{\"AdsSoloEnabled\":true}")]
    public void QuestionableOverrideRejectsMalformedSettingsWithoutReplacingTheOwner(string json)
    {
        var settings = new QuestionableDutySettingsOverride();
        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        Assert.False(settings.Apply("run", json, "character"));
        Assert.True(settings.Controls(AdsDutyCategory.Solo, "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson.Replace(":10", ":11"), "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson.Replace("true", "false"), "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson.Replace(":20", ":21"), "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson.Replace(":0", ":3"), "character"));
        Assert.False(settings.Apply("run", QuestionableSettingsJson[..^1] + ",\"Extra\":1}", "character"));
        Assert.False(settings.Apply("", QuestionableSettingsJson, "character"));
        Assert.False(settings.Release(""));
        Assert.True(settings.Release("run"));
    }

    [Theory]
    [InlineData(AdsDutyCategory.Solo, 10, false)]
    [InlineData(AdsDutyCategory.Solo, 10, true)]
    [InlineData(AdsDutyCategory.FourMan, 2, false)]
    [InlineData(AdsDutyCategory.FourMan, 2, true)]
    public void QuestionableOverrideHandsOffOnlyAfterContinuousReadiness(AdsDutyCategory category, int delay, bool savedEnabled)
    {
        var config = new CharacterConfig { Enabled = true, AdsDutyFamilySettingsMigrated = true };
        config.SetAdsDutyFamilySettings(category, savedEnabled, 3, 120);
        var saved = JsonSerializer.Serialize(config);
        var settings = new QuestionableDutySettingsOverride();
        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        var epoch = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        var now = epoch;
        var starts = 0;
        var owned = false;
        using var ipc = new AdsDutyIpcService(() => true, () => owned,
            () => JsonSerializer.Serialize(new
            {
                inInstancedDuty = true, ownershipMode = owned ? "OwnedStartInside" : "Observing",
                hasCatalogMetadata = true, duty = "Synthetic duty", territoryTypeId = 100u,
                contentFinderConditionId = 1u, dutyCategory = category.ToString(),
                supportLevel = "ActiveSupported", clearanceStatus = "NotCleared",
            }), () => { starts++; return true; }, () => now);
        var ads = new AdsIntegrationService(ipc, () => config, () => { }, _ => true, _ => { }, _ => { },
            (profile, family) => settings.Resolve(profile, family, "character"));
        var ready = new AdsHandoffReadinessConditions(true, true, true, false, false, false, false);
        void Frame(double seconds, bool loading = false)
        {
            now = epoch.AddSeconds(seconds);
            var conditions = ready with { IsBetweenAreas = loading };
            ads.ObserveHandoffReadiness(true, (100u, 1u), conditions, now);
            ads.Update(true, (100u, 1u), conditions, now, forceOwnershipRefresh: true);
        }

        Frame(0);
        Assert.True(ads.IsHandoffPending); // Saved disabled/T3 cannot reject M0.
        Assert.Contains("M0/T0", ads.StatusText);
        Frame(delay - 0.001);
        Assert.Equal(0, starts);
        Frame(delay, loading: true);
        Assert.Equal(0, starts);
        Frame(delay + 1);
        Frame(2 * delay + 0.999);
        Assert.Equal(0, starts);
        Frame(2 * delay + 1);
        Assert.Equal(1, starts);
        owned = true;
        Frame(2 * delay + 2);
        Assert.True(ads.IsControllingDuty);
        Assert.True(settings.Release("run"));
        Frame(2 * delay + 3);
        Assert.True(ads.IsControllingDuty); // Existing ADS ownership survives override release.
        Assert.Equal(1, starts);
        Assert.Equal(saved, JsonSerializer.Serialize(config));
    }

    [Theory]
    [InlineData(false, true, false, 1)]
    [InlineData(false, false, true, 120)]
    [InlineData(false, false, false, 2)]
    [InlineData(true, false, false, 100)]
    public void QuestionableOverrideUsesAdsExitAfterTwentySecondsAndPreservesSavedRules(
        bool adsExit, bool localExit, bool othersLeftExit, int savedDelay)
    {
        var config = new CharacterConfig
        {
            Enabled = true, UseAdsLeaveAfterAdsDuty = adsExit, ExitAfterDutyEnds = localExit,
            LeaveWhenAllLeft = othersLeftExit, ExitAfterDutySeconds = savedDelay,
        };
        var saved = JsonSerializer.Serialize(config);
        var settings = new QuestionableDutySettingsOverride();
        Assert.True(settings.Apply("run", QuestionableSettingsJson, "character"));
        var exits = settings.ResolveExit(config, "character");
        Assert.Equal(new DutyExitSettings(true, false, false, 20), exits);
        var epoch = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        using var ipc = new AdsDutyIpcService(() => true, () => true, () => "{}", () => true, () => epoch);
        var commands = new List<string>();
        var ads = new AdsIntegrationService(ipc, () => config, () => { }, command => { commands.Add(command); return true; },
            _ => { }, _ => { }, getExitSettings: profile => settings.ResolveExit(profile, "character"));
        ads.OnDutyStarted(100, epoch.AddSeconds(-120));
        ads.DutySession.ObserveAdsControl();
        ads.OnDutyCompleted(100, epoch);
        Assert.True(ads.ExitTakeoverActive); // Even a saved profile with no exit takes over after completion.
        var conditions = new DutyExitConditions(true, false, false, false, false);
        bool Exit(double seconds, DutyExitConditions? observed = null)
            => ExitBehaviourService.TryIssueCompletedExit(ads.DutySession, config, epoch.AddSeconds(seconds),
                observed ?? conditions, useAds => commands.Add(useAds ? "/ads leave" : "/dutyfinder"),
                settings.ResolveExit(config, "character"));
        Assert.False(Exit(19.999));
        Assert.False(Exit(20, conditions with { IsBetweenAreas = true }));
        Assert.False(Exit(20, conditions with { InCombat = true }));
        Assert.False(Exit(20, conditions with { UtilityActive = true }));
        Assert.False(Exit(20, conditions with { AdsPaused = true }));
        Assert.True(Exit(20));
        Assert.False(Exit(30));
        Assert.Equal(new[] { "/ads leave" }, commands);
        Assert.Equal(saved, JsonSerializer.Serialize(config));
        Assert.True(settings.Release("run"));
        Assert.Equal(DutyExitSettings.FromConfig(config), settings.ResolveExit(config, "character"));
        Assert.Equal(saved, JsonSerializer.Serialize(config));
    }

    private const string ActiveCharacterKey = "Participant One@Excalibur";

    [Fact]
    public void ApostropheTargetIsPersistedExactlyAndEnablesOnlyActiveCharacter()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];
        var otherConfig = account.Characters["Other Character@Excalibur"];
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "O'Brien Tia@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.True(succeeded);
        Assert.True(becameEnabled);
        Assert.Equal(1, saveCount);
        Assert.Equal("O'Brien Tia@Excalibur", activeConfig.FrenName);
        Assert.True(activeConfig.Enabled);
        Assert.Equal("Default Fren@Gilgamesh", account.DefaultConfig.FrenName);
        Assert.False(account.DefaultConfig.Enabled);
        Assert.Equal("Other Fren@Gilgamesh", otherConfig.FrenName);
        Assert.False(otherConfig.Enabled);
    }

    [Fact]
    public void MissingAccountIsRejectedWithoutPersistence()
    {
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            null,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Missing Character@Excalibur")]
    public void MissingActiveCharacterIsRejectedWithoutFallingBackToDefault(string selectedCharacterKey)
    {
        var account = CreateAccount();
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            selectedCharacterKey,
            "Venat Azem@Excalibur",
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
        Assert.Equal("Default Fren@Gilgamesh", account.DefaultConfig.FrenName);
        Assert.False(account.DefaultConfig.Enabled);
        Assert.Equal("Old Fren@Gilgamesh", account.Characters[ActiveCharacterKey].FrenName);
        Assert.False(account.Characters[ActiveCharacterKey].Enabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Venat Azem")]
    [InlineData("@Excalibur")]
    [InlineData("Venat Azem@")]
    [InlineData("Venat Azem@@Excalibur")]
    [InlineData(" Venat Azem@Excalibur")]
    [InlineData("Venat Azem@Excalibur ")]
    [InlineData("Venat Azem @Excalibur")]
    [InlineData("Venat Azem@ Excalibur")]
    [InlineData("Venat Azem@Crystal Tower")]
    public void MalformedTargetIsRejectedWithoutMutation(string? target)
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];
        var saveCount = 0;

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            target,
            () =>
            {
                saveCount++;
                return true;
            },
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal(0, saveCount);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void SaveFailureRollsBackTargetAndEnabledState()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () => false,
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void SaveExceptionRollsBackTargetAndEnabledState()
    {
        var account = CreateAccount();
        var activeConfig = account.Characters[ActiveCharacterKey];

        var succeeded = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            () => throw new IOException("disk unavailable"),
            out var becameEnabled);

        Assert.False(succeeded);
        Assert.False(becameEnabled);
        Assert.Equal("Old Fren@Gilgamesh", activeConfig.FrenName);
        Assert.False(activeConfig.Enabled);
    }

    [Fact]
    public void RepeatedSuccessfulConfigurationIsIdempotent()
    {
        var account = CreateAccount();
        var saveCount = 0;

        bool Persist()
        {
            saveCount++;
            return true;
        }

        var first = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            Persist,
            out var firstBecameEnabled);
        var second = ConfigManager.TryConfigureAndEnableActiveCharacter(
            account,
            ActiveCharacterKey,
            "Venat Azem@Excalibur",
            Persist,
            out var secondBecameEnabled);

        Assert.True(first);
        Assert.True(second);
        Assert.True(firstBecameEnabled);
        Assert.False(secondBecameEnabled);
        Assert.Equal(1, saveCount);
        Assert.Equal("Venat Azem@Excalibur", account.Characters[ActiveCharacterKey].FrenName);
        Assert.True(account.Characters[ActiveCharacterKey].Enabled);
    }

    [Fact]
    public void EndpointRegistersTypedHandlerAndForcesTrackerScanAfterSuccess()
    {
        Func<string, bool>? registeredHandler = null;
        var configuredTarget = string.Empty;
        var scanCount = 0;
        var unregisterCount = 0;

        var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => unregisterCount++,
            configureAndEnable: target =>
            {
                configuredTarget = target;
                return true;
            },
            forceNextTrackerScan: () => scanCount++);

        Assert.Equal("FrenRider.Dad.ConfigureAndEnable", DadIPC.ConfigureAndEnableEndpoint);
        Assert.NotNull(registeredHandler);
        Assert.True(registeredHandler!("O'Brien Tia@Excalibur"));
        Assert.Equal("O'Brien Tia@Excalibur", configuredTarget);
        Assert.Equal(1, scanCount);

        endpoint.Dispose();
        endpoint.Dispose();
        Assert.Equal(1, unregisterCount);
    }

    [Fact]
    public void EndpointRejectionDoesNotForceTrackerScan()
    {
        Func<string, bool>? registeredHandler = null;
        var scanCount = 0;
        using var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => { },
            configureAndEnable: _ => false,
            forceNextTrackerScan: () => scanCount++);

        Assert.False(registeredHandler!("Venat Azem@Excalibur"));
        Assert.Equal(0, scanCount);
    }

    [Fact]
    public void EndpointExceptionReturnsFalseAndDoesNotForceTrackerScan()
    {
        Func<string, bool>? registeredHandler = null;
        var scanCount = 0;
        using var endpoint = new DadIpcEndpoint(
            register: handler => registeredHandler = handler,
            unregister: () => { },
            configureAndEnable: _ => throw new InvalidOperationException("unexpected failure"),
            forceNextTrackerScan: () => scanCount++);

        Assert.False(registeredHandler!("Venat Azem@Excalibur"));
        Assert.Equal(0, scanCount);
    }

    private static AccountConfig CreateAccount()
        => new()
        {
            AccountId = "account",
            DefaultConfig = new CharacterConfig
            {
                FrenName = "Default Fren@Gilgamesh",
                Enabled = false,
            },
            Characters = new Dictionary<string, CharacterConfig>
            {
                [ActiveCharacterKey] = new()
                {
                    FrenName = "Old Fren@Gilgamesh",
                    Enabled = false,
                },
                ["Other Character@Excalibur"] = new()
                {
                    FrenName = "Other Fren@Gilgamesh",
                    Enabled = false,
                },
            },
        };
}
