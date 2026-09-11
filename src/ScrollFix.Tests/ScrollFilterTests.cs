using ScrollFix;
using Xunit;

namespace ScrollFix.Tests;

public class ScrollFilterTests
{
    private const int Down = -ScrollFilter.WheelDelta;
    private const int Up = +ScrollFilter.WheelDelta;

    /// <summary>Deterministic clock so tests never depend on Thread.Sleep.</summary>
    private sealed class FakeClock
    {
        public long NowMs { get; private set; }

        public long Read() => NowMs;

        public void Advance(long ms) => NowMs += ms;
    }

    private static (ScrollFilter Filter, FakeClock Clock, AppSettings Settings) Create(
        FilterMode mode,
        int blockMs = 220,
        int confirm = 2,
        bool enabled = true)
    {
        var settings = new AppSettings
        {
            Enabled = enabled,
            Mode = mode,
            ReverseBlockMs = blockMs,
            ConfirmDirectionCount = confirm,
        };
        var clock = new FakeClock();
        return (new ScrollFilter(settings, clock.Read), clock, settings);
    }

    private static FilterDecision Tick(ScrollFilter f, FakeClock c, int delta, long advanceMs)
    {
        c.Advance(advanceMs);
        return f.Decide(delta);
    }

    // ---------------------------------------------------------------- common

    [Theory]
    [InlineData(FilterMode.Balanced)]
    [InlineData(FilterMode.Strict)]
    public void SameDirection_AlwaysAllowed(FilterMode mode)
    {
        var (f, c, _) = Create(mode);
        for (var i = 0; i < 10; i++)
        {
            Assert.True(Tick(f, c, Down, 40).Allow);
        }
    }

    [Theory]
    [InlineData(FilterMode.Balanced)]
    [InlineData(FilterMode.Strict)]
    public void Disabled_PassesEverything(FilterMode mode)
    {
        var (f, c, _) = Create(mode, enabled: false);
        Assert.True(Tick(f, c, Down, 10).Allow);
        Assert.True(Tick(f, c, Up, 10).Allow);
        Assert.True(Tick(f, c, Down, 10).Allow);
    }

    [Theory]
    [InlineData(FilterMode.Balanced)]
    [InlineData(FilterMode.Strict)]
    public void ReversalAfterPause_AllowedImmediately(FilterMode mode)
    {
        var (f, c, _) = Create(mode, blockMs: 220);
        Assert.True(Tick(f, c, Down, 0).Allow);
        var reversal = Tick(f, c, Up, 300);
        Assert.True(reversal.Allow);
        Assert.Equal(0, reversal.ReplayDelta);
    }

    // -------------------------------------------------------------- balanced

    [Fact]
    public void Balanced_SingleGhostMidScroll_IsDroppedAndCounted()
    {
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 40).Allow);

        var ghost = Tick(f, c, Up, 30);
        Assert.False(ghost.Allow);
        Assert.True(ghost.Blocked);
        Assert.Equal(0, s.BlockedCount); // undecided until the next notch

        var resume = Tick(f, c, Down, 30);
        Assert.True(resume.Allow);
        Assert.Equal(0, resume.ReplayDelta);
        Assert.Equal(1, s.BlockedCount);
    }

    [Fact]
    public void Balanced_QuickReversal_PassesSecondNotchAndReplaysFirst()
    {
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 40).Allow);

        var held = Tick(f, c, Up, 50);
        Assert.False(held.Allow);

        var confirm = Tick(f, c, Up, 50);
        Assert.True(confirm.Allow);
        Assert.Equal(Up, confirm.ReplayDelta); // the held notch is re-injected
        Assert.Equal(0, s.BlockedCount);       // nothing was a ghost

        Assert.True(Tick(f, c, Up, 40).Allow);
    }

    [Fact]
    public void Balanced_RapidUpDownUpDown_NeverLosesNotches()
    {
        var (f, c, _) = Create(FilterMode.Balanced, blockMs: 220);
        var deliveredUp = 0;
        var deliveredDown = 0;

        void Feed(int delta, long dt)
        {
            var d = Tick(f, c, delta, dt);
            if (d.Allow)
            {
                if (delta > 0) deliveredUp++; else deliveredDown++;
            }

            if (d.ReplayDelta != 0)
            {
                var n = Math.Abs(d.ReplayDelta) / ScrollFilter.WheelDelta;
                if (d.ReplayDelta > 0) deliveredUp += n; else deliveredDown += n;
            }
        }

        // 3 down, 3 up, 3 down, 3 up — 60 ms apart, all well inside the window.
        for (var burst = 0; burst < 2; burst++)
        {
            Feed(Down, 60); Feed(Down, 60); Feed(Down, 60);
            Feed(Up, 60); Feed(Up, 60); Feed(Up, 60);
        }

        Assert.Equal(6, deliveredDown);
        Assert.Equal(6, deliveredUp);
    }

    [Fact]
    public void Balanced_ConfirmCountThree_HoldsTwoNotches()
    {
        var (f, c, _) = Create(FilterMode.Balanced, confirm: 3);
        Assert.True(Tick(f, c, Down, 0).Allow);

        Assert.False(Tick(f, c, Up, 40).Allow);
        Assert.False(Tick(f, c, Up, 40).Allow);
        var confirm = Tick(f, c, Up, 40);
        Assert.True(confirm.Allow);
        Assert.Equal(2 * Up, confirm.ReplayDelta);
    }

    [Fact]
    public void Balanced_HeldNotchGoesStale_CountedAsGhostOnNextEvent()
    {
        var (f, c, s) = Create(FilterMode.Balanced, blockMs: 220);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.False(Tick(f, c, Up, 40).Allow);

        // Long silence, then reverse again: the old held notch was a ghost,
        // the new one is an intentional reversal after a pause.
        var later = Tick(f, c, Up, 1000);
        Assert.True(later.Allow);
        Assert.Equal(0, later.ReplayDelta);
        Assert.Equal(1, s.BlockedCount);
    }

    [Fact]
    public void Balanced_MultiNotchDelta_ReplaysFullHeldDelta()
    {
        var (f, c, _) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);

        Assert.False(Tick(f, c, 2 * Up, 40).Allow);   // fast wheel: two notches in one event
        var confirm = Tick(f, c, Up, 40);
        Assert.True(confirm.Allow);
        Assert.Equal(2 * Up, confirm.ReplayDelta);
    }

    // ---------------------------------------------------------------- strict

    [Fact]
    public void Strict_SingleGhost_IsBlockedImmediately()
    {
        var (f, c, s) = Create(FilterMode.Strict);
        Assert.True(Tick(f, c, Down, 0).Allow);
        var ghost = Tick(f, c, Up, 30);
        Assert.False(ghost.Allow);
        Assert.True(ghost.Blocked);
        Assert.Equal(1, s.BlockedCount);
        Assert.True(Tick(f, c, Down, 30).Allow);
    }

    [Fact]
    public void Strict_GhostBurst_StaysBlockedAndExtendsWindow()
    {
        var (f, c, s) = Create(FilterMode.Strict, blockMs: 220);
        Assert.True(Tick(f, c, Down, 0).Allow);

        // Three ghosts 150 ms apart: each one alone is inside the window only
        // because the previous ghost extended it.
        Assert.False(Tick(f, c, Up, 150).Allow);
        Assert.False(Tick(f, c, Up, 150).Allow);
        Assert.False(Tick(f, c, Up, 150).Allow);
        Assert.Equal(3, s.BlockedCount);

        Assert.True(Tick(f, c, Down, 30).Allow);
    }

    [Fact]
    public void Strict_NeverReplays()
    {
        var (f, c, _) = Create(FilterMode.Strict);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.False(Tick(f, c, Up, 40).Allow);
        var second = Tick(f, c, Up, 40);
        Assert.False(second.Allow);
        Assert.Equal(0, second.ReplayDelta);
    }

    // -------------------------------------------------------------- settings

    [Fact]
    public void Settings_Clamp_KeepsValuesInRange()
    {
        var s = new AppSettings { ReverseBlockMs = 5, ConfirmDirectionCount = 1, BlockedCount = -3 };
        s.Clamp();
        Assert.Equal(40, s.ReverseBlockMs);
        Assert.Equal(2, s.ConfirmDirectionCount);
        Assert.Equal(0, s.BlockedCount);

        s.ReverseBlockMs = 9999;
        s.ConfirmDirectionCount = 99;
        s.Clamp();
        Assert.Equal(500, s.ReverseBlockMs);
        Assert.Equal(5, s.ConfirmDirectionCount);
    }

    [Fact]
    public void Settings_Presets_SetExpectedModes()
    {
        var s = new AppSettings();

        s.ApplyWornEncoderPreset();
        Assert.Equal(FilterMode.Strict, s.Mode);

        s.ApplyQuickReversePreset();
        Assert.Equal(FilterMode.Balanced, s.Mode);
        Assert.True(s.ReverseBlockMs < AppSettings.DefaultBlockMs);

        s.ApplyBalancedPreset();
        Assert.Equal(FilterMode.Balanced, s.Mode);
        Assert.Equal(AppSettings.DefaultBlockMs, s.ReverseBlockMs);
    }
}
