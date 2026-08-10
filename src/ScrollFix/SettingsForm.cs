namespace ScrollFix;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly CheckBox _enabled;
    private readonly CheckBox _aggressive;
    private readonly NumericUpDown _blockMs;
    private readonly NumericUpDown _maxNotches;
    private readonly NumericUpDown _confirmCount;
    private readonly CheckBox _autostart;
    private readonly Label _blockedLabel;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Scroll Fix";
        Text = "Scroll Fix — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(400, 340);
        TopMost = true;

        _enabled = new CheckBox
        {
            Text = "Filter enabled",
            Checked = settings.Enabled,
            AutoSize = true,
            Location = new Point(16, 16),
        };

        _aggressive = new CheckBox
        {
            Text = "Aggressive mode (recommended for worn wheels)",
            Checked = settings.AggressiveMode,
            AutoSize = true,
            Location = new Point(16, 44),
        };

        var lblMs = new Label { Text = "Reverse block window (ms)", AutoSize = true, Location = new Point(16, 80) };
        _blockMs = new NumericUpDown
        {
            Minimum = 40,
            Maximum = 500,
            Value = Math.Clamp(settings.ReverseBlockMs, 40, 500),
            Location = new Point(300, 76),
            Width = 70,
        };

        var lblNotches = new Label { Text = "Max ghost notches (mild mode)", AutoSize = true, Location = new Point(16, 116) };
        _maxNotches = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 6,
            Value = settings.MaxGhostNotches,
            Location = new Point(300, 112),
            Width = 70,
        };

        var lblConfirm = new Label { Text = "Confirm direction count (mild)", AutoSize = true, Location = new Point(16, 152) };
        _confirmCount = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 5,
            Value = settings.ConfirmDirectionCount,
            Location = new Point(300, 148),
            Width = 70,
        };

        _autostart = new CheckBox
        {
            Text = "Start with Windows",
            Checked = settings.StartWithWindows,
            AutoSize = true,
            Location = new Point(16, 188),
        };

        _blockedLabel = new Label
        {
            Text = $"Ghost scrolls blocked: {settings.BlockedCount}",
            AutoSize = true,
            Location = new Point(16, 220),
        };

        var hint = new Label
        {
            Text = "Aggressive: opposite scrolls within the window are always blocked.\nTo reverse on purpose, pause briefly (~0.2s) then scroll.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(16, 248),
        };

        var reset = new Button { Text = "Reset counter", Location = new Point(16, 296), Width = 110 };
        var save = new Button { Text = "Save", Location = new Point(200, 296), Width = 80 };
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, Location = new Point(290, 296), Width = 80 };

        save.Click += (_, _) =>
        {
            Apply();
            MessageBox.Show(this, "Settings saved.", "Scroll Fix", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        reset.Click += (_, _) =>
        {
            _settings.BlockedCount = 0;
            _settings.Save();
            _blockedLabel.Text = "Ghost scrolls blocked: 0";
        };

        CancelButton = close;

        Controls.AddRange(
        [
            _enabled,
            _aggressive,
            lblMs,
            _blockMs,
            lblNotches,
            _maxNotches,
            lblConfirm,
            _confirmCount,
            _autostart,
            _blockedLabel,
            hint,
            reset,
            save,
            close,
        ]);
    }

    private void Apply()
    {
        _settings.Enabled = _enabled.Checked;
        _settings.AggressiveMode = _aggressive.Checked;
        _settings.ReverseBlockMs = (int)_blockMs.Value;
        _settings.MaxGhostNotches = (int)_maxNotches.Value;
        _settings.ConfirmDirectionCount = (int)_confirmCount.Value;
        _settings.StartWithWindows = _autostart.Checked;
        _settings.Clamp();
        _settings.Save();
        Autostart.SetEnabled(_settings.StartWithWindows);
        _blockedLabel.Text = $"Ghost scrolls blocked: {_settings.BlockedCount}";
    }
}
