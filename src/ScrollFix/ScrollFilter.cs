namespace ScrollFix;

/// <summary>
/// Result of filtering one wheel event.
/// </summary>
/// <param name="Allow">Pass the current event through to applications.</param>
/// <param name="Blocked">The current event was swallowed (ghost or held).</param>
/// <param name="ReplayDelta">
/// Signed wheel delta to re-inject after the current event (0 = nothing).
/// Used when previously held notches turn out to be a real reversal.
/// </param>
public readonly record struct FilterDecision(bool Allow, bool Blocked = false, int ReplayDelta = 0);

/// <summary>
/// Suppresses reverse scrolls that look like worn-encoder ghosts.
///
/// Balanced mode (default): the first opposite notch inside the block window is
/// held back. If the next notch goes the same (new) way, the reversal is real:
/// the current notch passes and the held notches are replayed, so nothing is
/// lost. If the next notch goes back to the original direction, the held notch
/// was a ghost and is dropped for good.
///
/// Strict mode: every opposite notch inside the window is dropped and the
/// window is extended. Best for encoders that fire ghost bursts of 2+ pulses.
/// Reversing on purpose requires a short pause.
/// </summary>
public sealed class ScrollFilter
{
    public const int WheelDelta = 120;

    private readonly object _gate = new();
    private readonly Func<long> _clock;
    private AppSettings _settings;

    // Last accepted direction / time.
    private int _lastDir;
    private long _lastTimeMs;

    // Held (unconfirmed) reversal state — Balanced mode only.
    private int _pendingDir;
    private int _pendingCount;
    private int _pendingHeldDelta;
    private long _pendingLastMs;

    public ScrollFilter(AppSettings settings)
        : this(settings, static () => Environment.TickCount64)
    {
    }

    public ScrollFilter(AppSettings settings, Func<long> clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public void UpdateSettings(AppSettings settings)
    {
        lock (_gate)
        {
            _settings = settings;
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _lastDir = 0;
            _lastTimeMs = 0;
            ClearPending();
        }
    }

    public FilterDecision Decide(int delta)
    {
        lock (_gate)
        {
            if (!_settings.Enabled || delta == 0)
            {
                return new FilterDecision(Allow: true);
            }

            var direction = delta > 0 ? 1 : -1;
            var now = _clock();
            var elapsed = now - _lastTimeMs;

            // Same direction as established (or first event): always allow.
            if (direction == _lastDir || _lastDir == 0)
            {
                if (_pendingCount > 0)
                {
                    // Wheel went back to the original direction: held notches were ghosts.
                    _settings.BlockedCount += _pendingCount;
                    ClearPending();
                }

                Accept(direction, now);
                return new FilterDecision(Allow: true);
            }

            // Balanced: a held reversal is waiting for confirmation.
            if (_pendingCount > 0)
            {
                if (now - _pendingLastMs <= _settings.ReverseBlockMs)
                {
                    _pendingCount++;
                    _pendingLastMs = now;

                    if (_pendingCount >= Math.Max(2, _settings.ConfirmDirectionCount))
                    {
                        // Real reversal: pass this notch, replay the ones we held.
                        var replay = _pendingHeldDelta;
                        ClearPending();
                        Accept(direction, now);
                        return new FilterDecision(Allow: true, ReplayDelta: replay);
                    }

                    _pendingHeldDelta += delta;
                    return new FilterDecision(Allow: false, Blocked: true);
                }

                // Pending cluster went stale without confirmation: those were ghosts.
                _settings.BlockedCount += _pendingCount;
                ClearPending();
            }

            // Opposite direction after a real pause: intentional reversal.
            if (elapsed > _settings.ReverseBlockMs)
            {
                Accept(direction, now);
                return new FilterDecision(Allow: true);
            }

            if (_settings.Mode == FilterMode.Strict)
            {
                // Extend the lock while ghost bursts keep firing.
                _lastTimeMs = now;
                _settings.BlockedCount++;
                return new FilterDecision(Allow: false, Blocked: true);
            }

            // Balanced: hold the first opposite notch and wait for the next one.
            return Hold(direction, delta, now);
        }
    }

    private FilterDecision Hold(int direction, int delta, long now)
    {
        _pendingDir = direction;
        _pendingCount = 1;
        _pendingHeldDelta = delta;
        _pendingLastMs = now;
        return new FilterDecision(Allow: false, Blocked: true);
    }

    private void Accept(int direction, long nowMs)
    {
        _lastDir = direction;
        _lastTimeMs = nowMs;
    }

    private void ClearPending()
    {
        _pendingDir = 0;
        _pendingCount = 0;
        _pendingHeldDelta = 0;
        _pendingLastMs = 0;
    }
}
