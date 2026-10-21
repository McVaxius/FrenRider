using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    public void DutyTransitionsChangeOnlyCombatEligibilityWithoutPluginLifecycleCleanup(bool enabled)
    {
        WithManager((manager, _) =>
        {
            var config = manager.GetActiveConfig();
            config.Enabled = enabled;
            config.ActivationMode = FrenRiderActivationMode.DutyOnly;
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
                typeof(Plugin).GetField("wasEffectivelyEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(plugin, enabled);
                // Setup/cleanup services are absent: entering/leaving duties must
                // never invoke the master activation lifecycle.
                foreach (var duty in new[] { false, true, false, true, false })
                {
                    inDuty = duty;
                    typeof(Plugin).GetMethod("RefreshEffectiveActivation", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(plugin, null);
                    Assert.Equal(enabled, config.Enabled);
                    Assert.Equal(enabled, plugin.IsFrenRiderEffectivelyEnabled);
                    Assert.Equal(enabled && duty, plugin.AreCombatRotationsEnabled);
                    Assert.Equal(enabled && !duty, plugin.IsWaitingForDuty);
                }
            }
            finally { condition.SetValue(null, previous); }
        });
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
