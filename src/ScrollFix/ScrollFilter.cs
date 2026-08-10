namespace ScrollFix;

public readonly record struct FilterDecision(bool Allow, bool Blocked = false);

/// <summary>
/// Suppresses reverse scrolls that look like worn-encoder ghosts.
/// Opposite direction inside the block window is always dropped; a real
/// reverse needs a short pause (or enough confirmed notches after the window).
/// </summary>
public sealed class ScrollFilter
{
    public const int WheelDelta = 120;

    private readonly object _gate = new();
    private AppSettings _settings;
    private int _lastDir;
    private long _lastTimeMs;
    private int _pendingDir;
    private int _pendingCount;

    public ScrollFilter(AppSettings settings)
    {
        _settings = settings;
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
            _pendingDir = 0;
            _pendingCount = 0;
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
            var notches = Math.Abs(delta) / WheelDelta;
            if (notches == 0)
            {
                notches = 1;
            }

            var now = Environment.TickCount64;
            var elapsed = now - _lastTimeMs;

            // Same direction (or first event): always allow.
            if (direction == _lastDir || _lastDir == 0)
            {
                Accept(direction, now);
                return new FilterDecision(Allow: true);
            }

            // Aggressive mode (default): any opposite notch inside the window is a ghost.
            // This prevents double-ghost bursts from "confirming" a fake reversal.
            if (_settings.AggressiveMode)
            {
                if (elapsed <= _settings.ReverseBlockMs)
                {
                    // Extend the lock while ghost bursts keep firing.
                    _lastTimeMs = now;
                    _pendingDir = 0;
                    _pendingCount = 0;
                    _settings.BlockedCount++;
                    return new FilterDecision(Allow: false, Blocked: true);
                }

                Accept(direction, now);
                return new FilterDecision(Allow: true);
            }

            // Mild mode: require N consecutive opposite notches inside the window.
            if (elapsed <= _settings.ReverseBlockMs && notches <= _settings.MaxGhostNotches)
            {
                if (direction == _pendingDir)
                {
                    _pendingCount++;
                }
                else
                {
                    _pendingDir = direction;
                    _pendingCount = 1;
                }

                if (_pendingCount >= _settings.ConfirmDirectionCount)
                {
                    Accept(direction, now);
                    return new FilterDecision(Allow: true);
                }

                _settings.BlockedCount++;
                return new FilterDecision(Allow: false, Blocked: true);
            }

            Accept(direction, now);
            return new FilterDecision(Allow: true);
        }
    }

    private void Accept(int direction, long nowMs)
    {
        _lastDir = direction;
        _lastTimeMs = nowMs;
        _pendingDir = 0;
        _pendingCount = 0;
    }
}
