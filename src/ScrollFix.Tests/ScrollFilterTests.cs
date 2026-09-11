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
        bool enabled = true,
        int minReverseGap = 40,
        int minSameDirGap = 0)
    {
        var settings = new AppSettings
        {
            Enabled = enabled,
            Mode = mode,
            ReverseBlockMs = blockMs,
            ConfirmDirectionCount = confirm,
            MinReverseGapMs = minReverseGap,
            MinSameDirGapMs = minSameDirGap,
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
    public void Balanced_SlowGhostMidScroll_IsHeldThenDropped()
    {
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 50).Allow);

        var ghost = Tick(f, c, Up, 60); // slow enough to be plausible: held
        Assert.False(ghost.Allow);
        Assert.True(ghost.Blocked);
        Assert.Equal(0, s.BlockedCount); // undecided until the next notch

        var resume = Tick(f, c, Down, 50);
        Assert.True(resume.Allow);
        Assert.Equal(0, resume.ReplayDelta);
        Assert.Equal(1, s.BlockedCount);
    }

    // ---- burst rule (MinReverseGapMs), modelled on a real wheel-trace.log ----

    [Theory]
    [InlineData(FilterMode.Balanced)]
    [InlineData(FilterMode.Strict)]
    public void Burst_FourOppositePulsesInSameInstant_AllDropped(FilterMode mode)
    {
        // Real trace: "-120 pass, +120, +120, +120, +120" all within the same ms.
        var (f, c, s) = Create(mode);
        Assert.True(Tick(f, c, Down, 1000).Allow);

        for (var i = 0; i < 4; i++)
        {
            var d = Tick(f, c, Up, 0);
            Assert.False(d.Allow);
            Assert.Equal(0, d.ReplayDelta);
        }

        Assert.Equal(4, s.BlockedCount);
        Assert.True(Tick(f, c, Down, 60).Allow); // scrolling down continues untouched
    }

    [Fact]
    public void Burst_SecondPulseCannotConfirmHeldNotch()
    {
        // Held opposite notch at a plausible gap, then a burst pulse 5 ms later:
        // the burst must not count as a confirmation.
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 50).Allow);

        Assert.False(Tick(f, c, Up, 60).Allow);   // held
        var burst = Tick(f, c, Up, 5);            // physically impossible => ghost
        Assert.False(burst.Allow);
        Assert.Equal(0, burst.ReplayDelta);

        var resume = Tick(f, c, Down, 50);        // wheel keeps going down
        Assert.True(resume.Allow);
        Assert.Equal(2, s.BlockedCount);          // burst pulse + the held ghost
    }

    [Fact]
    public void Burst_AfterPause_FollowingBurstPulsesDropped()
    {
        // Real trace: 1 s pause, one down, then 4 ups in the same ms.
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 1094).Allow);
        Assert.False(Tick(f, c, Up, 0).Allow);
        Assert.False(Tick(f, c, Up, 0).Allow);
        Assert.False(Tick(f, c, Up, 0).Allow);
        Assert.False(Tick(f, c, Up, 0).Allow);
        Assert.Equal(4, s.BlockedCount);
    }

    [Fact]
    public void Burst_RuleOff_FallsBackToHoldConfirm()
    {
        var (f, c, _) = Create(FilterMode.Balanced, minReverseGap: 0);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.False(Tick(f, c, Up, 5).Allow);          // held (no burst rule)
        var confirm = Tick(f, c, Up, 5);
        Assert.True(confirm.Allow);
        Assert.Equal(Up, confirm.ReplayDelta);
    }

    [Fact]
    public void HumanReversal_AtNormalCadence_StillConfirmsAndReplays()
    {
        // 47 ms is the median same-direction cadence seen in the real trace.
        var (f, c, s) = Create(FilterMode.Balanced);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.True(Tick(f, c, Down, 47).Allow);
        Assert.False(Tick(f, c, Up, 47).Allow);
        var confirm = Tick(f, c, Up, 47);
        Assert.True(confirm.Allow);
        Assert.Equal(Up, confirm.ReplayDelta);
        Assert.Equal(0, s.BlockedCount);
    }

    // ---- optional same-direction duplicate rule ----

    [Fact]
    public void SameDirDuplicate_DroppedWhenRuleOn_PassedWhenOff()
    {
        var (on, c1, s1) = Create(FilterMode.Balanced, minSameDirGap: 8);
        Assert.True(Tick(on, c1, Down, 0).Allow);
        Assert.True(Tick(on, c1, Down, 50).Allow);
        Assert.False(Tick(on, c1, Down, 2).Allow);   // duplicate pulse
        Assert.True(Tick(on, c1, Down, 50).Allow);
        Assert.Equal(1, s1.BlockedCount);

        var (off, c2, s2) = Create(FilterMode.Balanced, minSameDirGap: 0);
        Assert.True(Tick(off, c2, Down, 0).Allow);
        Assert.True(Tick(off, c2, Down, 50).Allow);
        Assert.True(Tick(off, c2, Down, 2).Allow);
        Assert.Equal(0, s2.BlockedCount);
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

        Assert.False(Tick(f, c, Up, 50).Allow);
        Assert.False(Tick(f, c, Up, 50).Allow);
        var confirm = Tick(f, c, Up, 50);
        Assert.True(confirm.Allow);
        Assert.Equal(2 * Up, confirm.ReplayDelta);
    }

    [Fact]
    public void Balanced_HeldNotchGoesStale_CountedAsGhostOnNextEvent()
    {
        var (f, c, s) = Create(FilterMode.Balanced, blockMs: 220);
        Assert.True(Tick(f, c, Down, 0).Allow);
        Assert.False(Tick(f, c, Up, 50).Allow);

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

        Assert.False(Tick(f, c, 2 * Up, 50).Allow);   // fast wheel: two notches in one event
        var confirm = Tick(f, c, Up, 50);
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
        Assert.False(Tick(f, c, Up, 50).Allow);
        var second = Tick(f, c, Up, 50);
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
