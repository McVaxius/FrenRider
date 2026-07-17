using FrenRider.Services;

namespace FrenRider.Tests;

public sealed class FollowServiceBossModTargetNameTests
{
    [Fact]
    public void CrossWorldConcatenatedSuffixIsRemoved()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx YyyLich", "Lich"));
    }

    [Fact]
    public void SameWorldNameIsUnchanged()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("Xxx Yyy", "Lich"));
    }

    [Fact]
    public void SurnameMatchingWorldIsNotRemoved()
    {
        Assert.Equal(
            "Xxx Lich",
            FollowService.ResolveBossModTargetName("Xxx Lich", "Lich"));
    }

    [Fact]
    public void EmptyWorldAndSurroundingWhitespaceAreHandledSafely()
    {
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx YyyLich  ", "  Lich  "));
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx Yyy  ", string.Empty));
        Assert.Equal(
            "Xxx Yyy",
            FollowService.ResolveBossModTargetName("  Xxx Yyy  ", "   "));
    }
}
