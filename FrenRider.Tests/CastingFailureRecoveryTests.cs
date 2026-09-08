using System.Numerics;
using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class CastingFailureRecoveryTests
{
    public static IEnumerable<object?[]> RecoveryCases()
    {
        foreach (var values in new bool?[][]
        {
            [true, true, true], [true, false, null], [null, true, false],
            [false, null, true], [false, false, false], [null, null, null],
        })
        foreach (var stop in new[]
        {
            "combat", "timeout", "ads", "handoff", "unreadable-ads", "utility",
            "hyper-focus", "questionable", "coppelia", "disable", "character", "zone", "checkbox", "unload",
        })
            yield return [values[0], values[1], values[2], stop];
    }

    [Theory]
    [MemberData(nameof(RecoveryCases))]
    public void RepeatedMovementErrorsTemporarilyUnlockOnlyEnabledSettings(
        bool? bmr, bool? vbm, bool? rsr, string stop)
    {
        bool?[] initial = [bmr, vbm, rsr];
        var values = initial.ToArray();
        var writes = new int[3];
        var settings = Enumerable.Range(0, 3).Select(index => new CastingMovementSetting(
            new[] { "BMR", "VBM", "RSR" }[index],
            () => values[index],
            enabled => { values[index] = enabled; writes[index]++; })).ToArray();
        var warnings = new List<string>();
        var recovery = new CastingFailureRecovery(() => settings, warnings.Add);
        var context = new CastingRecoveryContext(1, 2, 3, Vector3.Zero, true, false);
        var blockers = new[]
        {
            context with { AdsOwned = true },
            context with { HandoffPending = true },
            context with { InDuty = true, AdsOwnershipReadable = false },
            context with { UtilityActive = true },
            context with { HyperFocusActive = true },
            context with { QuestionableControlled = true },
            context with { CoppeliaControlled = true },
            context with { EnabledAndReady = false },
            context with { InCombat = true },
            context with { TargetId = 0 },
        };
        foreach (var blocked in blockers)
        {
            recovery.OnError(blocked, 0);
            recovery.OnError(blocked, 1000);
            recovery.OnError(blocked, 2000);
            Assert.False(recovery.IsRecovering);
        }
        Assert.Equal(initial, values);
        Assert.All(writes, count => Assert.Equal(0, count));

        // Target changes and movement between toasts invalidate the earlier errors.
        recovery.OnError(context, 3000);
        recovery.OnError(context, 3100);
        recovery.Update(context with { TargetId = 4 }, 3150);
        recovery.OnError(context, 3200);
        Assert.False(recovery.IsRecovering);
        recovery.OnError(context, 3300);
        recovery.Update(context with { Position = new Vector3(0.51f, 0, 0) }, 3350);
        recovery.OnError(context, 3400);
        Assert.False(recovery.IsRecovering);
        recovery.OnError(context, 3500);
        recovery.OnError(context, 8501);
        Assert.False(recovery.IsRecovering); // Previous errors have left the five-second window.
        Assert.All(writes, count => Assert.Equal(0, count));
        recovery.Reset();

        // Exactly 0.5 yalms remains eligible; the third error triggers recovery.
        recovery.OnError(context, 10000);
        recovery.OnError(context with { Position = new Vector3(0.5f, 0, 0) }, 11000);
        Assert.False(recovery.IsRecovering);
        Assert.All(writes, count => Assert.Equal(0, count));
        recovery.OnError(context, 12000);
        Assert.True(recovery.IsRecovering);
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(initial[i] == true ? false : initial[i], values[i]);
            Assert.Equal(initial[i] == true ? 1 : 0, writes[i]);
        }

        recovery.OnError(context, 16000); // Further failures cannot extend 17000's deadline.
        Assert.True(recovery.IsRecovering);
        var endContext = stop switch
        {
            "combat" => context with { InCombat = true },
            "ads" => context with { AdsOwned = true },
            "handoff" => context with { HandoffPending = true },
            "unreadable-ads" => context with { InDuty = true, AdsOwnershipReadable = false },
            "utility" => context with { UtilityActive = true },
            "hyper-focus" => context with { HyperFocusActive = true },
            "questionable" => context with { QuestionableControlled = true },
            "coppelia" => context with { CoppeliaControlled = true },
            "disable" => context with { EnabledAndReady = false },
            "character" => context with { CharacterId = 4 },
            "zone" => context with { TerritoryId = 5 },
            _ => context,
        };
        if (stop == "timeout")
        {
            recovery.Update(context, 16999);
            Assert.True(recovery.IsRecovering);
            recovery.Update(context, 17000);
        }
        else if (stop is "checkbox" or "unload")
            recovery.Reset(restore: stop == "unload");
        else
            recovery.Update(endContext, 16500);

        Assert.False(recovery.IsRecovering);
        // A manual checkbox choice cancels the old restoration before its own apply.
        for (var i = 0; i < values.Length; i++)
        {
            Assert.Equal(stop == "checkbox" && initial[i] == true ? false : initial[i], values[i]);
            Assert.Equal(initial[i] == true ? stop == "checkbox" ? 1 : 2 : 0, writes[i]);
        }
        recovery.Update(context, 22000);
        recovery.Reset();
        Assert.All(writes.Select((count, i) => (count, i)), entry =>
            Assert.Equal(initial[entry.i] == true ? stop == "checkbox" ? 1 : 2 : 0, entry.count));
        Assert.Empty(warnings);
    }
}
