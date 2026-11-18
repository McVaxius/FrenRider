using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FrenRider.IPC;
using FrenRider.Models;
using FrenRider.Services;
using Xunit;

namespace FrenRider.Tests;

[CollectionDefinition("Activation mode static services", DisableParallelization = true)]
public sealed class ActivationModeStaticCollection { }

[Collection("Activation mode static services")]
public sealed class ActivationModeTests
{
    [Theory]
    [InlineData(false, FrenRiderActivationMode.AlwaysOn, false, false)]
    [InlineData(false, FrenRiderActivationMode.AlwaysOn, true, false)]
    [InlineData(true, FrenRiderActivationMode.AlwaysOn, false, true)]
    [InlineData(true, FrenRiderActivationMode.AlwaysOn, true, true)]
    [InlineData(false, FrenRiderActivationMode.DutyOnly, false, false)]
    [InlineData(false, FrenRiderActivationMode.DutyOnly, true, false)]
    [InlineData(true, FrenRiderActivationMode.DutyOnly, false, false)]
    [InlineData(true, FrenRiderActivationMode.DutyOnly, true, true)]
    public void DutyAdmissionPreservesSavedRunIntent(bool enabled, FrenRiderActivationMode mode,
        bool inDuty, bool expected)
    {
        var config = new CharacterConfig { Enabled = enabled, ActivationMode = mode };
        Assert.Equal(expected, config.IsEnabledForDuty(inDuty));
        Assert.Equal(enabled, config.Enabled);
    }

    [Theory]
    [InlineData(ConditionFlag.BoundByDuty)]
    [InlineData(ConditionFlag.BoundByDuty56)]
    [InlineData(ConditionFlag.BoundByDuty95)]
    public void AutomaticCompanionCareWaitsForEveryDutyFlagToClear(ConditionFlag dutyFlag)
    {
        var log = Proxy<IPluginLog>((_, _) => null);
        var pi = Proxy<IDalamudPluginInterface>((method, _) => method.Name == "get_InstalledPlugins"
            ? Array.Empty<IExposedPlugin>() : null);
        using var autorot = new AutorotIpcService(pi, log);
        using var runtime = new ExternalAutomationCleanupServiceTests.QuestingCombatRuntime(autorot, pi, log, 0, captureCommands: true);
        runtime.InstallActivationCollaborators();
        runtime.Profile.ChocoboAutoFeed = true;
        runtime.Profile.ChocoboFoodItemId = 7894;
        runtime.Profile.ChocoboAutoAllocateSkills = true;
        runtime.Conditions.Add(dutyFlag);

        foreach (var mode in new[] { FrenRiderActivationMode.AlwaysOn, FrenRiderActivationMode.DutyOnly })
        {
            runtime.Profile.ActivationMode = mode;
            Assert.True(runtime.Host.IsFrenRiderEffectivelyEnabled);
            CheckCompanionCare();
            Assert.Equal(0, runtime.CompanionDispatches);
        }

        runtime.Conditions.Clear();
        CheckCompanionCare();
        Assert.Equal(0, runtime.CompanionDispatches);
        runtime.Profile.ActivationMode = FrenRiderActivationMode.AlwaysOn;
        CheckCompanionCare();
        Assert.Equal(2, runtime.CompanionDispatches);
        Assert.True(runtime.Skills.IsActive);
        Assert.True(runtime.Food.IsActive);

        void CheckCompanionCare()
        {
            runtime.Host.CheckAutomaticChocoboSkills();
            runtime.Host.CheckAutomaticChocoboFood();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DutyEntryDuringSkillLearningLogBlocksOnlyAutomaticDispatch(bool automatic)
    {
        var log = Proxy<IPluginLog>((_, _) => null);
        var pi = Proxy<IDalamudPluginInterface>((method, _) => method.Name == "get_InstalledPlugins"
            ? Array.Empty<IExposedPlugin>() : null);
        using var autorot = new AutorotIpcService(pi, log);
        using var runtime = new ExternalAutomationCleanupServiceTests.QuestingCombatRuntime(autorot, pi, log, 0, captureCommands: true);
        runtime.Profile.Enabled = true;
        runtime.Profile.ChocoboAutoAllocateSkills = true;
        var snapshot = new CompanionSnapshot(9, 0, 0, 3, 0, 0, 0);
        var requests = 0;
        var releases = 0;
        var enterDuty = true;
        var skill = new ChocoboSkillService(() => true, () => runtime.ContentId, () => runtime.Profile,
            () => snapshot, (_, _) => { requests++; return true; }, message =>
            {
                if (!enterDuty || !message.StartsWith("dispatch learning:", StringComparison.Ordinal)) return;
                enterDuty = false;
                runtime.Conditions.Add(ConditionFlag.BoundByDuty95);
            }, () => 0, prepare: () => true, prepared: () => true, release: () => releases++);

        if (automatic) skill.CheckAutomatic();
        else Assert.True(skill.Start());
        Assert.True(skill.IsActive);
        skill.Tick();

        Assert.Equal(automatic ? 0 : 1, requests);
        Assert.Equal(automatic ? 1 : 0, releases);
        Assert.Equal(!automatic, skill.IsActive);
        if (automatic)
        {
            runtime.Conditions.Clear();
            skill.CheckAutomatic();
            Assert.True(skill.IsActive);
            skill.Tick();
            Assert.Equal(1, requests);
        }
        skill.Stop();
    }

    [Fact]
    public void OldConfigurationsDefaultToAlwaysOnAndModeSurvivesCloneAndProfileTransfer()
    {
        var old = JsonSerializer.Deserialize<CharacterConfig>("{\"Enabled\":true}")!;
        Assert.Equal(FrenRiderActivationMode.AlwaysOn, old.ActivationMode);
        Assert.True(old.IsEnabledForDuty(false));
        old.ActivationMode = FrenRiderActivationMode.DutyOnly;
        var copy = old.Clone();
        Assert.Equal(old.ActivationMode, copy.ActivationMode);
        Assert.True(DadProfileTransferService.TrySerializeProfile(copy, out var text, out _));
        Assert.True(DadProfileTransferService.TryDeserializeProfile(text, out var restored, out _));
        Assert.Equal(FrenRiderActivationMode.DutyOnly, restored!.ActivationMode);
        Assert.True(restored.Enabled);
        Assert.False(restored.IsEnabledForDuty(false));
    }

    [Fact]
    public void ModeSyncCopiesOnlyModeToLocalProfiles()
    {
        var account = new AccountConfig { AccountId = "synthetic" };
        account.DefaultConfig.ActivationMode = FrenRiderActivationMode.DutyOnly;
        account.DefaultConfig.Enabled = true;
        var local = new CharacterConfig { Enabled = false };
        account.Characters["Example Member@TestWorld"] = local;
        var remote = new RemoteProfileRow { Config = new CharacterConfig() };
        account.RemoteProfiles.Add(remote);
        Assert.Equal(1, ConfigManager.ApplyDefaultSettingToAllCharacters(account, "Activation mode"));
        Assert.Equal(FrenRiderActivationMode.DutyOnly, local.ActivationMode);
        Assert.False(local.Enabled);
        Assert.Equal(FrenRiderActivationMode.AlwaysOn, remote.Config.ActivationMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DutyTransitionsSuspendAutomaticWorkAndPreserveManualCompanionQueues(bool enabled)
    {
        foreach (var manual in new[] { false, true })
        {
            var log = Proxy<IPluginLog>((_, _) => null);
            var pi = Proxy<IDalamudPluginInterface>((method, _) => method.Name == "get_InstalledPlugins"
                ? Array.Empty<IExposedPlugin>() : null);
            using var autorot = new AutorotIpcService(pi, log);
            using var runtime = new ExternalAutomationCleanupServiceTests.QuestingCombatRuntime(autorot, pi, log, 0, captureCommands: true);
            runtime.StopActivity();
            runtime.InstallActivationCollaborators();
            runtime.Profile.Enabled = enabled;
            runtime.Profile.ActivationMode = FrenRiderActivationMode.DutyOnly;
            runtime.Profile.ChocoboAutoFeed = true;
            runtime.Profile.ChocoboFoodItemId = 7894;
            runtime.Profile.ChocoboAutoAllocateSkills = true;
            var saved = JsonSerializer.Serialize(runtime.Profile);

            runtime.Conditions.Add(Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty);
            runtime.RefreshActivation(); // Real parent activation; no fabricated applied-owner record.
            Assert.Equal(enabled, runtime.Host.IsFrenRiderEffectivelyEnabled);
            var admitted = manual || enabled;
            Assert.Equal(manual, runtime.Food.Start(automatically: !manual));
            Assert.Equal(manual, runtime.Skills.Start(automatically: !manual));
            Assert.Equal(admitted, runtime.Exploration.Start(automatically: !manual));
            if (enabled)
            {
                typeof(PartyService).GetField("lastInviterName", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(runtime.Host.PartyService, "Example Inviter");
                runtime.SetAutoDesynth("PendingExit");
            }
            runtime.Commands.Clear();
            var dispatchesBeforeWait = runtime.CompanionDispatches;

            runtime.Conditions.Clear();
            runtime.RefreshActivation(); // Real parent Duty Only suspension with every collaborator wired.
            runtime.RefreshActivation(); // Repeated outside-duty frames must not repeat cleanup.
            runtime.Food.Tick();
            runtime.Skills.Tick();
            runtime.Exploration.Tick();

            Assert.Equal(enabled, runtime.Profile.Enabled);
            Assert.False(runtime.Host.IsFrenRiderEffectivelyEnabled);
            Assert.False(runtime.Host.AreCombatRotationsEnabled);
            Assert.Equal(enabled, runtime.Host.IsWaitingForDuty);
            Assert.Equal(manual, runtime.Food.IsActive);
            Assert.Equal(manual, runtime.Skills.IsActive);
            Assert.Equal(manual, runtime.Exploration.IsActive);
            Assert.Empty(runtime.Commands);
            Assert.Equal(dispatchesBeforeWait, runtime.CompanionDispatches);
            if (enabled)
            {
                Assert.Equal("Waiting for duty", runtime.Host.FollowService.StateDetail);
                Assert.Equal("Raid mode disabled", runtime.Host.RaidModeService.StatusText);
                Assert.Contains("paused", runtime.Automation.AutoDesynthStatus);
                Assert.Null(typeof(PartyService).GetField("lastInviterName", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(runtime.Host.PartyService));
            }
            runtime.Food.Stop();
            runtime.Skills.Stop();
            runtime.Exploration.Stop("manual stop");
            var dispatches = runtime.CompanionDispatches;
            Assert.False(runtime.Food.Start(automatically: true));
            Assert.False(runtime.Skills.Start(automatically: true));
            Assert.False(runtime.Exploration.Start(automatically: true));
            Assert.Equal(dispatches, runtime.CompanionDispatches);
            Assert.True(runtime.Food.Start(automatically: false));
            Assert.True(runtime.Skills.Start(automatically: false));
            Assert.True(runtime.Exploration.Start(automatically: false));
            Assert.Equal(dispatches + 3, runtime.CompanionDispatches);
            Assert.Equal(saved, JsonSerializer.Serialize(runtime.Profile));
        }
    }

    [Fact]
    public void DadDutyEnableUpdatesTemporaryOverlayWithoutChangingRestoredLocalMode()
    {
        WithManager((manager, local) =>
        {
            var identity = new DadProfileIdentity("owner", "island", "opaque", "proposal");
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig { Enabled = true }));
            Assert.True(manager.ConfigureAndEnableActiveCharacter("Example Leader@TestWorld"));
            Assert.Equal(FrenRiderActivationMode.DutyOnly, manager.GetActiveConfig().ActivationMode);
            Assert.Equal(FrenRiderActivationMode.AlwaysOn, local.ActivationMode);
            Assert.False(local.Enabled);
            Assert.True(manager.TryReleaseTemporaryProfile(identity));
            Assert.Same(local, manager.GetActiveConfig());
            Assert.Equal(FrenRiderActivationMode.AlwaysOn, manager.GetActiveConfig().ActivationMode);
        });
    }

    [Theory]
    [InlineData("on", true)]
    [InlineData(" \tON\r\n", true)]
    [InlineData("off", false)]
    [InlineData("  OfF  ", false)]
    [InlineData("stop", false)]
    [InlineData(" \tStOp\r\n", false)]
    public void ActivationCommandsPersistSavedRunIntentWithoutChangingDutyMode(string arguments, bool enabled)
    {
        WithManager((manager, local) =>
        {
            local.Enabled = !enabled;
            local.ActivationMode = FrenRiderActivationMode.DutyOnly;
            var notifications = new List<bool>();
            manager.OnFrenRiderEnabledChanged += notifications.Add;

            Assert.True(Plugin.TryApplyActivationCommand(manager, arguments));

            Assert.Equal(enabled, local.Enabled);
            Assert.Equal(FrenRiderActivationMode.DutyOnly, local.ActivationMode);
            Assert.Equal(new[] { enabled }, notifications);
            var directory = (string)typeof(ConfigManager).GetField("configDir", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(manager)!;
            var saved = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(
                Path.Combine(directory, $"{manager.CurrentAccountId}_FrenRider.json")))!;
            Assert.Equal(enabled, saved.Characters[manager.ActiveCharacterKey].Enabled);
            Assert.Equal(FrenRiderActivationMode.DutyOnly, saved.Characters[manager.ActiveCharacterKey].ActivationMode);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("toggle")]
    [InlineData("testchocobo stop")]
    [InlineData("  TESTCHOCOBO STOP  ")]
    [InlineData("stop now")]
    [InlineData("on off")]
    public void OtherCommandsDoNotChangeSavedRunIntentOrTriggerActivationCleanup(string arguments)
    {
        WithManager((manager, local) =>
        {
            local.Enabled = true;
            var notifications = new List<bool>();
            manager.OnFrenRiderEnabledChanged += notifications.Add;

            Assert.False(Plugin.TryApplyActivationCommand(manager, arguments));

            Assert.True(local.Enabled);
            Assert.Empty(notifications);
        });
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("off", false)]
    [InlineData("stop", true)]
    [InlineData("stop", false)]
    public void StopPersistsAcrossDutyTransitionsAndOverlayReleaseUntilExplicitOn(string arguments, bool overlayEnabled)
    {
        WithManager((manager, local) =>
        {
            local.Enabled = true;
            local.ActivationMode = FrenRiderActivationMode.DutyOnly;
            var identity = new DadProfileIdentity("owner", "island", "opaque", "proposal");
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig
            {
                Enabled = overlayEnabled,
                ActivationMode = FrenRiderActivationMode.DutyOnly,
            }));
            var notifications = new List<bool>();
            manager.OnFrenRiderEnabledChanged += notifications.Add;

            Assert.True(Plugin.TryApplyActivationCommand(manager, arguments));
            Assert.True(Plugin.TryApplyActivationCommand(manager, arguments));
            Assert.Equal(new[] { false, false }, notifications);
            Assert.False(manager.GetActiveConfig().Enabled);
            Assert.False(local.Enabled);
            var directory = (string)typeof(ConfigManager).GetField("configDir", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(manager)!;
            var saved = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(
                Path.Combine(directory, $"{manager.CurrentAccountId}_FrenRider.json")))!;
            Assert.False(saved.Characters[manager.ActiveCharacterKey].Enabled);

            var condition = typeof(Plugin).GetProperty("Condition", BindingFlags.Static | BindingFlags.NonPublic)!;
            var previous = condition.GetValue(null);
            var inDuty = false;
            try
            {
                condition.SetValue(null, Proxy<ICondition>((method, args) => method.Name == "get_Item"
                    && args![0] is Dalamud.Game.ClientState.Conditions.ConditionFlag flag
                    && flag == Dalamud.Game.ClientState.Conditions.ConditionFlag.BoundByDuty && inDuty));
                var plugin = (Plugin)RuntimeHelpers.GetUninitializedObject(typeof(Plugin));
                typeof(Plugin).GetField("<ConfigManager>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(plugin, manager);

                foreach (var releaseOverlay in new[] { false, true })
                {
                    if (releaseOverlay)
                    {
                        Assert.True(manager.TryReleaseTemporaryProfile(identity));
                        Assert.Same(local, manager.GetActiveConfig());
                    }
                    foreach (var duty in new[] { false, true, false, true, false })
                    {
                        inDuty = duty;
                        typeof(Plugin).GetMethod("RefreshEffectiveActivation", BindingFlags.Instance | BindingFlags.NonPublic)!
                            .Invoke(plugin, null);
                        Assert.False(manager.GetActiveConfig().Enabled);
                        Assert.False(plugin.IsFrenRiderEffectivelyEnabled);
                        Assert.False(plugin.AreCombatRotationsEnabled);
                        Assert.False(plugin.IsWaitingForDuty);
                        Assert.False(local.Enabled);
                    }
                }

                Assert.Equal(new[] { false, false }, notifications);
                Assert.True(Plugin.TryApplyActivationCommand(manager, "on"));
                Assert.True(local.Enabled);
                Assert.Equal(FrenRiderActivationMode.DutyOnly, local.ActivationMode);
                Assert.Equal(new[] { false, false, true }, notifications);
                foreach (var duty in new[] { false, true, false })
                {
                    inDuty = duty;
                    Assert.True(manager.GetActiveConfig().Enabled);
                    Assert.Equal(duty, plugin.IsFrenRiderEffectivelyEnabled);
                    Assert.Equal(duty, plugin.AreCombatRotationsEnabled);
                    Assert.Equal(!duty, plugin.IsWaitingForDuty);
                }
            }
            finally { condition.SetValue(null, previous); }
        });
    }

    [Fact]
    public void StopWithoutActiveProfileNotifiesCleanupWithoutCreatingOrEnablingProfile()
    {
        WithManager((manager, local) =>
        {
            local.Enabled = true;
            manager.ClearActiveCharacter();
            var directory = (string)typeof(ConfigManager).GetField("configDir", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(manager)!;
            var filesBefore = Directory.GetFiles(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            var notifications = new List<bool>();
            manager.OnFrenRiderEnabledChanged += notifications.Add;

            Assert.True(Plugin.TryApplyActivationCommand(manager, "stop"));
            Assert.True(Plugin.TryApplyActivationCommand(manager, "off"));
            Assert.True(Plugin.TryApplyActivationCommand(manager, "on"));

            Assert.Equal(new[] { false, false }, notifications);
            Assert.False(manager.GetActiveConfig().Enabled);
            Assert.Equal("", manager.ActiveCharacterKey);
            Assert.True(local.Enabled);
            Assert.Equal(filesBefore, Directory.GetFiles(directory).OrderBy(path => path, StringComparer.Ordinal).ToArray());
        });
    }

    [Fact]
    public void ExplicitOffDisablesSavedProfileAndRepeatedOffNotifiesCleanup()
    {
        WithManager((manager, local) =>
        {
            local.Enabled = true;
            var identity = new DadProfileIdentity("owner", "island", "opaque", "proposal");
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig { Enabled = true }));

            manager.DisableForRecovery();
            Assert.False(manager.GetActiveConfig().Enabled);
            Assert.True(local.Enabled);
            manager.SetFrenRiderEnabled(true);
            Assert.True(manager.TryReleaseTemporaryProfile(identity));
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig { Enabled = false }));
            Assert.True(manager.TryReleaseTemporaryProfile(identity));
            Assert.True(local.Enabled);
            Assert.True(manager.TryInstallTemporaryProfile(identity, new CharacterConfig { Enabled = true }));

            var notifications = new List<bool>();
            manager.OnFrenRiderEnabledChanged += notifications.Add;
            manager.SetFrenRiderEnabled(false);
            Assert.False(local.Enabled);
            manager.SetFrenRiderEnabled(false);
            manager.SetFrenRiderEnabled(true);
            manager.OnFrenRiderEnabledChanged += _ => throw new InvalidOperationException("Synthetic cleanup failure");
            Assert.Throws<InvalidOperationException>(() => manager.SetFrenRiderEnabled(false));
            Assert.Equal(new[] { false, false, true, false }, notifications);
            Assert.False(manager.GetActiveConfig().Enabled);
        });
    }

    internal static void WithManager(Action<ConfigManager, CharacterConfig> verify)
    {
        var directory = Directory.CreateTempSubdirectory("fr-activation-");
        try
        {
            var pi = Proxy<IDalamudPluginInterface>((method, _) => method.Name == "GetPluginConfigDirectory"
                ? directory.FullName : null);
            var manager = new ConfigManager(pi, Proxy<IPluginLog>((_, _) => null));
            var account = new AccountConfig { AccountId = "synthetic" };
            var local = new CharacterConfig();
            account.Characters["Example Member@TestWorld"] = local;
            ((Dictionary<string, AccountConfig>)manager.Accounts).Add(account.AccountId, account);
            manager.CurrentAccountId = account.AccountId;
            manager.EnsureCharacterExists("Example Member", "TestWorld");
            verify(manager, local);
        }
        finally
        {
            if (!string.Equals(directory.Parent!.FullName.TrimEnd('\\'), Path.GetTempPath().TrimEnd('\\'),
                    StringComparison.OrdinalIgnoreCase)) throw new IOException("Unexpected synthetic test directory.");
            directory.Delete(true);
        }
    }

    internal static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var result = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)result).Handler = handler;
        return result;
    }

    public class TestProxy : DispatchProxy
    {
        internal Func<MethodInfo, object?[]?, object?> Handler = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
}
