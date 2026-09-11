using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScrollFix;

/// <summary>
/// Hidden tray application host (no main window).
/// The hook callback only runs the filter; all UI updates and disk writes
/// happen on the UI thread via a timer so the low-level hook never stalls.
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    public const string RepoUrl = "https://github.com/Leozz18/scroll-fix";

    private static readonly TimeSpan PauseDuration = TimeSpan.FromMinutes(10);

    private readonly AppSettings _settings;
    private readonly ScrollFilter _filter;
    private readonly MouseWheelHook _hook;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _blockedItem;
    private readonly ToolStripMenuItem _traceItem;
    private readonly WheelTrace _trace;
    private readonly System.Windows.Forms.Timer _uiTimer;

    private SettingsForm? _settingsForm;
    private int _lastShownBlocked = -1;
    private int _lastSavedBlocked;
    private DateTime _pausedUntil = DateTime.MinValue;
    private bool _enabledBeforePause;

    // Ghost-rate window used to suggest the Strict preset to burst-prone wheels.
    private const int SuggestStrictThreshold = 30;
    private static readonly TimeSpan SuggestStrictWindow = TimeSpan.FromMinutes(10);
    private DateTime _rateWindowStart = DateTime.UtcNow;
    private int _blockedAtWindowStart;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        _lastSavedBlocked = _settings.BlockedCount;
        Autostart.SetEnabled(_settings.StartWithWindows);

        _filter = new ScrollFilter(_settings);
        _trace = new WheelTrace(WheelTrace.DefaultPath);
        _hook = new MouseWheelHook(Decide);

        _enabledItem = new ToolStripMenuItem("Enabled", null, OnToggleEnabled)
        {
            Checked = _settings.Enabled,
        };
        _pauseItem = new ToolStripMenuItem("Pause for 10 minutes", null, OnTogglePause);
        _blockedItem = new ToolStripMenuItem(BlockedText()) { Enabled = false };
        _traceItem = new ToolStripMenuItem("Log wheel events (diagnostics)", null, OnToggleTrace)
        {
            Checked = _settings.TraceEnabled,
        };

        var diagnostics = new ToolStripMenuItem("Diagnostics");
        diagnostics.DropDownItems.Add(_traceItem);
        diagnostics.DropDownItems.Add(new ToolStripMenuItem("Open settings folder", null, (_, _) => OpenUrl(AppSettings.SettingsDirectory)));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, OnOpenSettings));
        menu.Items.Add(_blockedItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(diagnostics);
        menu.Items.Add(new ToolStripMenuItem("GitHub / report a bug", null, (_, _) => OpenUrl(RepoUrl)));
        menu.Items.Add(new ToolStripMenuItem($"Scroll Fix v{AppVersion}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, OnQuit));

        _tray = new NotifyIcon
        {
            Icon = BuildIcon(_settings.Enabled),
            Text = TrayText(),
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += OnOpenSettings;
        _tray.BalloonTipClicked += OnOpenSettings;
        _blockedAtWindowStart = _settings.BlockedCount;

        _uiTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _uiTimer.Tick += OnUiTick;
        _uiTimer.Start();

        try
        {
            _hook.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not install the mouse hook:\n{ex.Message}",
                "Scroll Fix",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitThread();
            return;
        }

        Application.ApplicationExit += (_, _) => Cleanup();
    }

    private static string AppVersion =>
        typeof(TrayAppContext).Assembly.GetName().Version?.ToString(3) ?? "dev";

    /// <summary>Hook thread. Filter first, then (optionally) record. No IO here.</summary>
    private FilterDecision Decide(int delta)
    {
        var decision = _filter.Decide(delta);
        if (_settings.TraceEnabled)
        {
            _trace.Record(HighResClock.NowMs(), delta, decision, _settings.BlockedCount);
        }

        return decision;
    }

    /// <summary>
    /// Wheels that fire ghost bursts are served far better by Strict mode
    /// (0 vs ~9 wrong-way notches per 6 min on a real trace). Tell the user once.
    /// </summary>
    private void MaybeSuggestStrict(int blocked)
    {
        var now = DateTime.UtcNow;
        if (now - _rateWindowStart > SuggestStrictWindow)
        {
            _rateWindowStart = now;
            _blockedAtWindowStart = blocked;
            return;
        }

        if (_settings.StrictSuggested || _settings.Mode != FilterMode.Balanced || !_settings.Enabled)
        {
            return;
        }

        if (blocked - _blockedAtWindowStart >= SuggestStrictThreshold)
        {
            _settings.StrictSuggested = true;
            TrySave();
            _tray.ShowBalloonTip(
                15000,
                "Scroll Fix: your wheel fires ghost bursts",
                $"{SuggestStrictThreshold}+ ghosts blocked in a few minutes. The \"Worn encoder\" preset (Strict mode) handles this best. Click here to open Settings.",
                ToolTipIcon.Info);
        }
    }

    private void OnToggleTrace(object? sender, EventArgs e)
    {
        _settings.TraceEnabled = !_settings.TraceEnabled;
        _traceItem.Checked = _settings.TraceEnabled;
        TrySave();
    }

    private void OnUiTick(object? sender, EventArgs e)
    {
        // Auto-resume after a pause.
        if (_pausedUntil != DateTime.MinValue && DateTime.UtcNow >= _pausedUntil)
        {
            EndPause();
        }

        _trace.Flush();

        var blocked = _settings.BlockedCount;
        MaybeSuggestStrict(blocked);
        if (blocked != _lastShownBlocked)
        {
            _lastShownBlocked = blocked;
            _blockedItem.Text = BlockedText();
            _tray.Text = TrayText();
        }

        // Persist the counter at most once per second and only when it changed.
        if (blocked != _lastSavedBlocked)
        {
            _lastSavedBlocked = blocked;
            TrySave();
        }
    }

    private void OnToggleEnabled(object? sender, EventArgs e)
    {
        if (_pausedUntil != DateTime.MinValue)
        {
            EndPause();
        }

        SetEnabled(!_settings.Enabled);
        TrySave();
    }

    private void OnTogglePause(object? sender, EventArgs e)
    {
        if (_pausedUntil != DateTime.MinValue)
        {
            EndPause();
            return;
        }

        _enabledBeforePause = _settings.Enabled;
        _pausedUntil = DateTime.UtcNow + PauseDuration;
        _pauseItem.Text = "Resume now";
        SetEnabled(false, persist: false);
    }

    private void EndPause()
    {
        _pausedUntil = DateTime.MinValue;
        _pauseItem.Text = "Pause for 10 minutes";
        SetEnabled(_enabledBeforePause, persist: false);
    }

    private void SetEnabled(bool enabled, bool persist = true)
    {
        _settings.Enabled = enabled;
        _filter.UpdateSettings(_settings);
        _filter.Reset();
        _enabledItem.Checked = enabled;
        RefreshIcon();
        if (persist)
        {
            TrySave();
        }
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.BringToFront();
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        _settingsForm.SettingsApplied += (_, _) =>
        {
            _filter.UpdateSettings(_settings);
            _filter.Reset();
            _enabledItem.Checked = _settings.Enabled;
            RefreshIcon();
        };
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        TrySave();
        Cleanup();
        ExitThread();
    }

    private void TrySave()
    {
        try
        {
            _settings.Save();
        }
        catch
        {
            // A failed save must never take the tray app down.
        }
    }

    private void RefreshIcon()
    {
        var old = _tray.Icon;
        _tray.Icon = BuildIcon(_settings.Enabled);
        _tray.Text = TrayText();
        old?.Dispose();
    }

    private string TrayText()
    {
        // NotifyIcon.Text is limited to 127 chars.
        var state = _pausedUntil != DateTime.MinValue ? "paused"
            : _settings.Enabled ? _settings.Mode.ToString().ToLowerInvariant()
            : "off";
        return $"Scroll Fix ({state}) — blocked {_settings.BlockedCount}";
    }

    private string BlockedText() => $"Ghost scrolls blocked: {_settings.BlockedCount}";

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Browser launch failures are not fatal.
        }
    }

    private void Cleanup()
    {
        _uiTimer.Stop();
        _uiTimer.Dispose();
        _hook.Dispose();
        _trace.Flush();
        _tray.Visible = false;
        _tray.Icon?.Dispose();
        _tray.Dispose();
    }

    private static Icon BuildIcon(bool enabled)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var fill = enabled ? Color.FromArgb(46, 160, 67) : Color.FromArgb(160, 160, 160);
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 2, 2, 28, 28);
            using var pen = new Pen(Color.White, 2);
            g.DrawRectangle(pen, 14, 6, 4, 20);
            g.DrawEllipse(pen, 10, 11, 12, 12);
        }

        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
