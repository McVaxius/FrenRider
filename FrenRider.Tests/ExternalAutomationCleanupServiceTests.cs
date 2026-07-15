using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class ExternalAutomationCleanupServiceTests
{
    [Fact]
    public void CaptureStoresSnapshotsPerAccountAndCharacter()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "one")] = Snapshot("account", "one", forbidMovement: true);
        provider.Snapshots[("account", "two")] = Snapshot("account", "two", forbidMovement: false);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "one", "test");
        service.CaptureIfMissing("account", "two", "test");
        service.Cleanup(new CharacterConfig(), "account", "two", "test");

        Assert.Contains("/bmrai forbidmovement off", sender.Commands);
        Assert.DoesNotContain("/bmrai forbidmovement on", sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotReplaysCapturedState()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, cbtAutoFollow: true);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        Assert.Equal(
            new[]
            {
                "/bmrai forbidmovement on",
                "/bmrai followoutofcombat off",
                "/bmrai followcombat on",
                "/bmrai followmodule off",
                "/vbmai forbidmovement on",
                "/vbmai followoutofcombat off",
                "/vbmai followcombat on",
                "/vbmai followmodule off",
                "/cbt enable AutoFollow",
            },
            sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotTurnsBmrAiOffFirstWhenCapturedIdle()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, bmrAiActive: false);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        Assert.Equal("/bmrai off", sender.Commands[0]);
        Assert.Contains("/bmrai forbidmovement on", sender.Commands);
        Assert.DoesNotContain("/bmrai on", sender.Commands);
    }

    [Fact]
    public void RestoreSnapshotTurnsBmrAiOnLastWhenCapturedActive()
    {
        var provider = new FakeSnapshotProvider();
        provider.Snapshots[("account", "character")] = Snapshot("account", "character", forbidMovement: true, bmrAiActive: true);
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);

        service.CaptureIfMissing("account", "character", "test");
        var result = service.Cleanup(new CharacterConfig(), "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Restored, result.State);
        var followModuleIndex = sender.Commands.IndexOf("/bmrai followmodule off");
        var aiOnIndex = sender.Commands.IndexOf("/bmrai on");
        Assert.True(aiOnIndex > followModuleIndex);
        Assert.DoesNotContain("/bmrai off", sender.Commands);
    }

    [Fact]
    public void TurnEverythingOffStopsManagedAutomationAndWrathWhenStarted()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: new AutorotRsrCleanupController(() => true, sender));
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        service.MarkWrathAutoStarted("account", "character", "test");
        var result = service.Cleanup(config, "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/cbt disable AutoFollow", "/wrath auto off" },
            sender.Commands);
        Assert.Contains(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
        Assert.DoesNotContain(AutorotRsrCleanupController.FallbackCommand, sender.Commands);
    }

    [Fact]
    public void TurnEverythingOffDoesNotStopWrathWhenFrenRiderDidNotStartIt()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        service.Cleanup(config, "account", "character", "test");

        Assert.DoesNotContain("/wrath auto off", sender.Commands);
    }

    [Fact]
    public void FailedCommandReportsPartialCleanup()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender { FailedCommands = { "/vbmai off" } };
        var service = new ExternalAutomationCleanupService(sender, provider);
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        var result = service.Cleanup(config, "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Contains("Partial", result.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TypedRsrSuccessDoesNotSendFallbackAndReportsOneCompositeAction()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(() => true, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.DoesNotContain(AutorotRsrCleanupController.FallbackCommand, sender.Commands);
        Assert.Equal(1, result.Commands.Count(command =>
            command is AutorotRsrCleanupController.TypedActionLabel or AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
    }

    [Fact]
    public void FailedTypedRsrUsesSuccessfulCommandFallbackOnce()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(() => false, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(1, sender.Commands.Count(command =>
            command == AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.FallbackCommand, result.Commands);
        Assert.DoesNotContain(AutorotRsrCleanupController.TypedActionLabel, result.Commands);
    }

    [Fact]
    public void ThrowingTypedRsrStillUsesCommandFallbackOnce()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var controller = new AutorotRsrCleanupController(
            () => throw new InvalidOperationException("typed IPC unavailable"),
            sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(1, sender.Commands.Count(command =>
            command == AutorotRsrCleanupController.FallbackCommand));
        Assert.Contains(AutorotRsrCleanupController.FallbackCommand, result.Commands);
    }

    [Fact]
    public void TypedAndFallbackRsrFailureCountsAsOneFailedCompositeAction()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender
        {
            FailedCommands = { AutorotRsrCleanupController.FallbackCommand },
        };
        var controller = new AutorotRsrCleanupController(() => false, sender);
        var service = new ExternalAutomationCleanupService(
            sender,
            provider,
            rsrCleanupController: controller);

        var result = service.Cleanup(
            new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff },
            "account",
            "character",
            "test");

        Assert.Equal(ExternalAutomationCleanupState.Partial, result.State);
        Assert.Contains("1/4", result.StatusText, StringComparison.Ordinal);
        Assert.Equal(1, result.Commands.Count(command =>
            command is AutorotRsrCleanupController.TypedActionLabel or AutorotRsrCleanupController.FallbackCommand));
    }

    private static ExternalAutomationSnapshot Snapshot(
        string accountId,
        string characterKey,
        bool forbidMovement,
        bool cbtAutoFollow = false,
        bool? bmrAiActive = null,
        bool? vbmAiActive = null)
    {
        var bmr = new BossModAutomationSnapshot(
            true,
            bmrAiActive,
            forbidMovement,
            false,
            true,
            false,
            false,
            null,
            string.Empty);
        var vbm = new BossModAutomationSnapshot(
            true,
            vbmAiActive,
            forbidMovement,
            false,
            true,
            false,
            false,
            null,
            string.Empty);
        var cbt = new CbtAutomationSnapshot(true, cbtAutoFollow, string.Empty);
        return new ExternalAutomationSnapshot(accountId, characterKey, bmr, vbm, cbt, DateTimeOffset.UtcNow);
    }

    private sealed class FakeSnapshotProvider : IExternalAutomationSnapshotProvider
    {
        public Dictionary<(string AccountId, string CharacterKey), ExternalAutomationSnapshot> Snapshots { get; } = new();

        public ExternalAutomationSnapshot Capture(string accountId, string characterKey)
            => Snapshots.TryGetValue((accountId, characterKey), out var snapshot)
                ? snapshot
                : Snapshot(accountId, characterKey, forbidMovement: false);
    }

    private sealed class FakeCommandSender : IExternalAutomationCommandSender
    {
        public List<string> Commands { get; } = new();
        public HashSet<string> FailedCommands { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TrySendCommand(string command)
        {
            Commands.Add(command);
            return !FailedCommands.Contains(command);
        }
    }
}
