using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class BossModConflictWarningServiceTests
{
    [Fact]
    public void ConflictWarnsImmediatelyThenEveryFiveSeconds()
    {
        var conflict = true;
        var now = 1_000L;
        var warnings = new List<string>();
        var service = BossModConflictWarningService.CreateForTesting(
            () => conflict,
            warnings.Add,
            () => now);

        service.Update(frenRiderEnabled: true);
        Assert.Single(warnings);
        Assert.Equal(BossModConflictWarningService.WarningMessage, warnings[0]);

        now += BossModConflictWarningService.WarningIntervalMs - 1;
        service.Update(frenRiderEnabled: true);
        Assert.Single(warnings);

        now++;
        service.Update(frenRiderEnabled: true);
        Assert.Equal(2, warnings.Count);

        now += BossModConflictWarningService.WarningIntervalMs;
        service.Update(frenRiderEnabled: true);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void ConflictClearResetsThrottleForImmediateNextWarning()
    {
        var conflict = true;
        var now = 1_000L;
        var warningCount = 0;
        var service = BossModConflictWarningService.CreateForTesting(
            () => conflict,
            _ => warningCount++,
            () => now);

        service.Update(frenRiderEnabled: true);
        conflict = false;
        now++;
        service.Update(frenRiderEnabled: true);
        conflict = true;
        now++;
        service.Update(frenRiderEnabled: true);

        Assert.Equal(2, warningCount);
    }

    [Fact]
    public void DisableResetsThrottleAndNeverWarnsWhileDisabled()
    {
        var now = 1_000L;
        var warningCount = 0;
        var service = BossModConflictWarningService.CreateForTesting(
            () => true,
            _ => warningCount++,
            () => now);

        service.Update(frenRiderEnabled: false);
        Assert.Equal(0, warningCount);

        service.Update(frenRiderEnabled: true);
        Assert.Equal(1, warningCount);

        now++;
        service.Update(frenRiderEnabled: false);
        service.Update(frenRiderEnabled: true);
        Assert.Equal(2, warningCount);
    }
}
