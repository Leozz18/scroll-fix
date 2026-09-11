using System.Diagnostics;

namespace ScrollFix;

/// <summary>
/// Millisecond clock backed by QueryPerformanceCounter. Environment.TickCount
/// has ~15.6 ms granularity, far too coarse to tell an encoder bounce (0-16 ms)
/// from a human notch (30 ms and up).
/// </summary>
public static class HighResClock
{
    private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

    public static long NowMs() => (long)(Stopwatch.GetTimestamp() / TicksPerMs);
}
