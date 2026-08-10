using System.Runtime.InteropServices;

namespace ScrollFix;

/// <summary>
/// Hidden tray application host (no main window).
/// </summary>
internal sealed class TrayAppContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly ScrollFilter _filter;
    private readonly MouseWheelHook _hook;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _blockedItem;
    private SettingsForm? _settingsForm;
    private int _persistCounter;

    public TrayAppContext()
    {
        _settings = AppSettings.Load();
        Autostart.SetEnabled(_settings.StartWithWindows);

        _filter = new ScrollFilter(_settings);
        _hook = new MouseWheelHook(ShouldBlock);

        _enabledItem = new ToolStripMenuItem("Enabled", null, OnToggleEnabled)
        {
            Checked = _settings.Enabled,
            CheckOnClick = false,
        };
        _blockedItem = new ToolStripMenuItem($"Blocked: {_settings.BlockedCount}")
        {
            Enabled = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, OnOpenSettings));
        menu.Items.Add(_blockedItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, OnQuit));

        _tray = new NotifyIcon
        {
            Icon = BuildIcon(_settings.Enabled),
            Text = _settings.Enabled ? "Scroll Fix (on)" : "Scroll Fix (off)",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += OnOpenSettings;

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

    private bool ShouldBlock(int delta)
    {
        var decision = _filter.Decide(delta);
        if (decision.Blocked)
        {
            _persistCounter++;
            if (_persistCounter % 10 == 0)
            {
                _settings.Save();
            }

            try
            {
                if (_tray.ContextMenuStrip?.InvokeRequired == true)
                {
                    _tray.ContextMenuStrip.BeginInvoke(UpdateBlockedLabel);
                }
                else
                {
                    UpdateBlockedLabel();
                }
            }
            catch
            {
                // Ignore UI races during shutdown.
            }
        }

        return !decision.Allow;
    }

    private void UpdateBlockedLabel()
    {
        _blockedItem.Text = $"Blocked: {_settings.BlockedCount}";
    }

    private void OnToggleEnabled(object? sender, EventArgs e)
    {
        _settings.Enabled = !_settings.Enabled;
        _settings.Save();
        _filter.UpdateSettings(_settings);
        if (!_settings.Enabled)
        {
            _filter.Reset();
        }

        _enabledItem.Checked = _settings.Enabled;
        var old = _tray.Icon;
        _tray.Icon = BuildIcon(_settings.Enabled);
        old?.Dispose();
        _tray.Text = _settings.Enabled ? "Scroll Fix (on)" : "Scroll Fix (off)";
    }

    private void OnOpenSettings(object? sender, EventArgs e)
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.BringToFront();
            _settingsForm.Focus();
            return;
        }

        _settingsForm = new SettingsForm(_settings);
        _settingsForm.FormClosed += (_, _) =>
        {
            _filter.UpdateSettings(_settings);
            _filter.Reset();
            _enabledItem.Checked = _settings.Enabled;
            var old = _tray.Icon;
            _tray.Icon = BuildIcon(_settings.Enabled);
            old?.Dispose();
            _tray.Text = _settings.Enabled ? "Scroll Fix (on)" : "Scroll Fix (off)";
            UpdateBlockedLabel();
            _settingsForm = null;
        };
        _settingsForm.Show();
    }

    private void OnQuit(object? sender, EventArgs e)
    {
        _settings.Save();
        Cleanup();
        ExitThread();
    }

    private void Cleanup()
    {
        _hook.Dispose();
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
