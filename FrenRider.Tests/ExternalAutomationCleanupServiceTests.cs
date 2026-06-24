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
    public void TurnEverythingOffStopsManagedAutomationAndWrathWhenStarted()
    {
        var provider = new FakeSnapshotProvider();
        var sender = new FakeCommandSender();
        var service = new ExternalAutomationCleanupService(sender, provider);
        var config = new CharacterConfig { CleanupMode = FrenRiderCleanupMode.TurnEverythingOff };

        service.MarkWrathAutoStarted("account", "character", "test");
        var result = service.Cleanup(config, "account", "character", "test");

        Assert.Equal(ExternalAutomationCleanupState.ForceOff, result.State);
        Assert.Equal(
            new[] { "/bmrai off", "/vbmai off", "/cbt disable AutoFollow", "/wrath auto off" },
            sender.Commands);
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

    private static ExternalAutomationSnapshot Snapshot(
        string accountId,
        string characterKey,
        bool forbidMovement,
        bool cbtAutoFollow = false)
    {
        var bmr = new BossModAutomationSnapshot(
            true,
            forbidMovement,
            false,
            true,
            false,
            false,
            null,
            string.Empty);
        var vbm = new BossModAutomationSnapshot(
            true,
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
