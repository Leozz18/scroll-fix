using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace ScrollFix;

/// <summary>
/// Optional diagnostics: records every wheel event and the filter's decision.
/// Record() is called on the hook thread and only enqueues; Flush() runs on
/// the UI thread and does the file IO. The log is capped so it can run for days.
/// </summary>
public sealed class WheelTrace
{
    private const int MaxQueued = 20_000;
    private const long MaxFileBytes = 8 * 1024 * 1024;

    private readonly ConcurrentQueue<string> _lines = new();
    private readonly string _path;
    private long _lastMs;
    private int _queued;

    public WheelTrace(string path)
    {
        _path = path;
    }

    public static string DefaultPath => Path.Combine(AppSettings.SettingsDirectory, "wheel-trace.log");

    public string Path_ => _path;

    /// <summary>Hook thread: cheap, allocation-light, no IO.</summary>
    public void Record(long nowMs, int delta, FilterDecision decision, int blockedCount)
    {
        if (_queued >= MaxQueued)
        {
            return; // UI thread is not flushing; do not grow unbounded
        }

        var dt = _lastMs == 0 ? 0 : nowMs - _lastMs;
        _lastMs = nowMs;

        var what = decision.Allow
            ? (decision.ReplayDelta != 0 ? "pass+replay" : "pass")
            : "hold/drop";

        // t=<ms since boot> dt=<ms since previous event> delta=<+/-120*n> <decision> blocked=<total> [replay=<delta>]
        var sb = new StringBuilder(80);
        sb.Append(nowMs.ToString(CultureInfo.InvariantCulture)).Append('\t')
          .Append(dt.ToString(CultureInfo.InvariantCulture)).Append('\t')
          .Append(delta > 0 ? "+" : string.Empty).Append(delta.ToString(CultureInfo.InvariantCulture)).Append('\t')
          .Append(what).Append('\t')
          .Append(blockedCount.ToString(CultureInfo.InvariantCulture));
        if (decision.ReplayDelta != 0)
        {
            sb.Append("\treplay=").Append(decision.ReplayDelta.ToString(CultureInfo.InvariantCulture));
        }

        _lines.Enqueue(sb.ToString());
        Interlocked.Increment(ref _queued);
    }

    /// <summary>UI thread: append queued lines to disk.</summary>
    public void Flush()
    {
        if (_lines.IsEmpty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var info = new FileInfo(_path);
            if (info.Exists && info.Length > MaxFileBytes)
            {
                // Keep the file bounded: start over, note the rotation.
                File.WriteAllText(_path, "# rotated\n");
            }

            using var w = new StreamWriter(_path, append: true, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (!info.Exists || info.Length == 0)
            {
                w.WriteLine("# t_ms\tdt_ms\tdelta\tdecision\tblocked_total\t[replay]");
            }

            while (_lines.TryDequeue(out var line))
            {
                Interlocked.Decrement(ref _queued);
                w.WriteLine(line);
            }
        }
        catch
        {
            // Diagnostics must never affect the app.
        }
    }
}
