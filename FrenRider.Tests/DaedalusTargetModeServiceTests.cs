using FrenRider.Models;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class DaedalusTargetModeServiceTests
{
    [Theory]
    [InlineData(DaedalusTargetMode.None, 0, 123UL)]
    [InlineData(DaedalusTargetMode.Focus, 1, 999UL)]
    [InlineData(DaedalusTargetMode.Split, 2, 123UL)]
    [InlineData(DaedalusTargetMode.KillAdds, 3, 123UL)]
    public void AllModesMapAndPreserveExpectedCoordinationState(
        DaedalusTargetMode mode,
        int expectedMode,
        ulong expectedFocusTargetId)
    {
        var bus = new FakeCoordinationBus();
        var plugin = new FakeDaedalusPlugin(bus);
        var focusTargetId = mode == DaedalusTargetMode.Focus ? 999UL : (ulong?)null;

        var applied = DaedalusTargetModeService.TryBroadcastTargetMode(
            plugin,
            mode,
            focusTargetId,
            out var failure);

        Assert.True(applied, failure);
        Assert.Equal(expectedMode, (int)bus.LastMode!.Value);
        Assert.Equal(expectedFocusTargetId, bus.LastFocusTargetId);
        Assert.Equal("off-tank@world", bus.LastOffTankSenderId);
    }

    [Fact]
    public void FocusWithoutLivingEnemyLeavesDaedalusUnchanged()
    {
        var bus = new FakeCoordinationBus();
        var plugin = new FakeDaedalusPlugin(bus);

        var applied = DaedalusTargetModeService.TryBroadcastTargetMode(
            plugin,
            DaedalusTargetMode.Focus,
            focusTargetId: null,
            out var failure);

        Assert.False(applied);
        Assert.Contains("living enemy hard target", failure, StringComparison.Ordinal);
        Assert.Null(bus.LastMode);
        Assert.Equal(123UL, bus.FocusTargetId);
        Assert.Equal("off-tank@world", bus.OffTankSenderId);
    }

    [Theory]
    [InlineData(0UL, true, true, 1U, false)]
    [InlineData(100UL, false, true, 1U, false)]
    [InlineData(100UL, true, false, 1U, false)]
    [InlineData(100UL, true, true, 0U, false)]
    [InlineData(100UL, true, true, 1U, true)]
    public void FocusTargetValidationRequiresLivingEnemyHardTarget(
        ulong objectId,
        bool isEnemy,
        bool isTargetable,
        uint currentHp,
        bool expected)
    {
        var valid = DaedalusTargetModeService.TryResolveFocusTargetId(
            objectId,
            isEnemy,
            isTargetable,
            currentHp,
            out var focusTargetId);

        Assert.Equal(expected, valid);
        Assert.Equal(expected ? objectId : 0UL, focusTargetId);
    }

    [Theory]
    [InlineData(4, 0, false, true)]
    [InlineData(0, 4, true, true)]
    [InlineData(4, 0, true, false)]
    [InlineData(0, 4, false, false)]
    public void EffectiveRotationUsesCurrentNormalOrForayContext(
        int normalPlugin,
        int forayPlugin,
        bool isForay,
        bool expected)
    {
        Assert.Equal(
            expected,
            DaedalusTargetModeService.IsEffectiveRotation(normalPlugin, forayPlugin, isForay));
    }

    [Fact]
    public void MissingOrChangedReflectionMembersAreContained()
    {
        var exception = Record.Exception(() =>
        {
            var applied = DaedalusTargetModeService.TryBroadcastTargetMode(
                new MissingCoordinationBusPlugin(),
                DaedalusTargetMode.Split,
                focusTargetId: null,
                out var failure);

            Assert.False(applied);
            Assert.NotEmpty(failure);
        });

        Assert.Null(exception);
    }

    [Fact]
    public void BroadcastInvocationFailureIsContained()
    {
        var exception = Record.Exception(() =>
        {
            var applied = DaedalusTargetModeService.TryBroadcastTargetMode(
                new FakeDaedalusPlugin(new ThrowingCoordinationBus()),
                DaedalusTargetMode.Split,
                focusTargetId: null,
                out var failure);

            Assert.False(applied);
            Assert.Contains("broadcast failed", failure, StringComparison.Ordinal);
        });

        Assert.Null(exception);
    }

    private enum FakeTargetMode
    {
        None = 0,
        Focus = 1,
        Split = 2,
        KillAdds = 3,
    }

    private sealed class FakeDaedalusPlugin
    {
        private readonly object coordinationBus;

        public FakeDaedalusPlugin(object coordinationBus)
        {
            this.coordinationBus = coordinationBus;
        }
    }

    private sealed class MissingCoordinationBusPlugin
    {
    }

    private sealed class FakeCoordinationBus
    {
        public ulong FocusTargetId { get; private set; } = 123;
        public string OffTankSenderId { get; private set; } = "off-tank@world";
        public FakeTargetMode? LastMode { get; private set; }
        public ulong LastFocusTargetId { get; private set; }
        public string LastOffTankSenderId { get; private set; } = string.Empty;

        public void BroadcastTargetMode(
            FakeTargetMode mode,
            ulong focusTargetId,
            string offTankSenderId)
        {
            LastMode = mode;
            LastFocusTargetId = focusTargetId;
            LastOffTankSenderId = offTankSenderId;
            FocusTargetId = focusTargetId;
            OffTankSenderId = offTankSenderId;
        }
    }

    private sealed class ThrowingCoordinationBus
    {
        public ulong FocusTargetId => 123;
        public string OffTankSenderId => "off-tank@world";

        public void BroadcastTargetMode(
            FakeTargetMode mode,
            ulong focusTargetId,
            string offTankSenderId)
            => throw new InvalidOperationException("broadcast failed");
    }
}
