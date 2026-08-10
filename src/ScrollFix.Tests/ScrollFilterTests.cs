using ScrollFix;
using Xunit;

namespace ScrollFix.Tests;

public class ScrollFilterTests
{
    [Fact]
    public void SameDirection_IsAllowed()
    {
        var filter = new ScrollFilter(new AppSettings
        {
            Enabled = true,
            AggressiveMode = true,
            ReverseBlockMs = 220,
        });

        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
    }

    [Fact]
    public void Aggressive_SingleGhost_IsBlocked()
    {
        var filter = new ScrollFilter(new AppSettings
        {
            Enabled = true,
            AggressiveMode = true,
            ReverseBlockMs = 220,
        });

        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        var ghost = filter.Decide(+ScrollFilter.WheelDelta);
        Assert.False(ghost.Allow);
        Assert.True(ghost.Blocked);
        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
    }

    [Fact]
    public void Aggressive_DoubleGhostBurst_StaysBlocked()
    {
        var filter = new ScrollFilter(new AppSettings
        {
            Enabled = true,
            AggressiveMode = true,
            ReverseBlockMs = 220,
        });

        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        Assert.False(filter.Decide(+ScrollFilter.WheelDelta).Allow);
        Assert.False(filter.Decide(+ScrollFilter.WheelDelta).Allow);
        Assert.False(filter.Decide(+ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
    }

    [Fact]
    public void Mild_RealReversal_AcceptedAfterConfirm()
    {
        var filter = new ScrollFilter(new AppSettings
        {
            Enabled = true,
            AggressiveMode = false,
            ReverseBlockMs = 200,
            ConfirmDirectionCount = 2,
            MaxGhostNotches = 2,
        });

        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        Assert.False(filter.Decide(+ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(+ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(+ScrollFilter.WheelDelta).Allow);
    }

    [Fact]
    public void Disabled_PassesThrough()
    {
        var filter = new ScrollFilter(new AppSettings
        {
            Enabled = false,
            AggressiveMode = true,
            ReverseBlockMs = 200,
        });

        Assert.True(filter.Decide(-ScrollFilter.WheelDelta).Allow);
        Assert.True(filter.Decide(+ScrollFilter.WheelDelta).Allow);
    }
}
