namespace ScrollFix;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _enabled;
    private readonly RadioButton _modeBalanced;
    private readonly RadioButton _modeStrict;
    private readonly NumericUpDown _blockMs;
    private readonly NumericUpDown _confirmCount;
    private readonly NumericUpDown _minReverseGap;
    private readonly NumericUpDown _minSameDirGap;
    private readonly Label _confirmLabel;
    private readonly CheckBox _autostart;
    private readonly Label _blockedLabel;
    private readonly Label _modeHint;

    /// <summary>Raised after settings were saved so the host can reload the filter.</summary>
    public event EventHandler? SettingsApplied;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;

        Text = "Scroll Fix — Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // --- Presets -------------------------------------------------------
        var presets = new GroupBox { Text = "Quick presets", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var presetRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        presetRow.Controls.Add(PresetButton("Balanced (default)", s => s.ApplyBalancedPreset()));
        presetRow.Controls.Add(PresetButton("Quick reverse", s => s.ApplyQuickReversePreset()));
        presetRow.Controls.Add(PresetButton("Worn encoder", s => s.ApplyWornEncoderPreset()));
        presets.Controls.Add(presetRow);

        // --- Filter --------------------------------------------------------
        var filterBox = new GroupBox { Text = "Filter", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _enabled = new CheckBox { Text = "Filter enabled", AutoSize = true, Checked = settings.Enabled };
        grid.Controls.Add(_enabled, 0, 0);
        grid.SetColumnSpan(_enabled, 2);

        _modeBalanced = new RadioButton
        {
            Text = "Balanced — holds one notch, confirms with the next, replays it if real",
            AutoSize = true,
            Checked = settings.Mode == FilterMode.Balanced,
        };
        _modeStrict = new RadioButton
        {
            Text = "Strict — drops every opposite notch inside the window (pause to reverse)",
            AutoSize = true,
            Checked = settings.Mode == FilterMode.Strict,
        };
        _modeBalanced.CheckedChanged += (_, _) => UpdateModeUi();
        grid.Controls.Add(_modeBalanced, 0, 1);
        grid.SetColumnSpan(_modeBalanced, 2);
        grid.Controls.Add(_modeStrict, 0, 2);
        grid.SetColumnSpan(_modeStrict, 2);

        grid.Controls.Add(new Label { Text = "Reverse block window (ms)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 3);
        _blockMs = new NumericUpDown
        {
            Minimum = 40,
            Maximum = 500,
            Increment = 10,
            Value = Math.Clamp(settings.ReverseBlockMs, 40, 500),
            Width = 72,
            Margin = new Padding(3, 6, 3, 3),
        };
        grid.Controls.Add(_blockMs, 1, 3);

        _confirmLabel = new Label { Text = "Notches to confirm a reversal", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        grid.Controls.Add(_confirmLabel, 0, 4);
        _confirmCount = new NumericUpDown
        {
            Minimum = 2,
            Maximum = 5,
            Value = Math.Clamp(settings.ConfirmDirectionCount, 2, 5),
            Width = 72,
            Margin = new Padding(3, 6, 3, 3),
        };
        grid.Controls.Add(_confirmCount, 1, 4);

        grid.Controls.Add(new Label { Text = "Min. reversal gap (ms) — opposite notch closer than this = burst", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 5);
        _minReverseGap = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 100,
            Increment = 5,
            Value = Math.Clamp(settings.MinReverseGapMs, 0, 100),
            Width = 72,
            Margin = new Padding(3, 6, 3, 3),
        };
        grid.Controls.Add(_minReverseGap, 1, 5);

        grid.Controls.Add(new Label { Text = "Drop same-direction duplicates closer than (ms, 0 = off)", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, 6);
        _minSameDirGap = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 30,
            Value = Math.Clamp(settings.MinSameDirGapMs, 0, 30),
            Width = 72,
            Margin = new Padding(3, 6, 3, 3),
        };
        grid.Controls.Add(_minSameDirGap, 1, 6);

        _modeHint = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(480, 0), Margin = new Padding(3, 8, 3, 3) };
        grid.Controls.Add(_modeHint, 0, 7);
        grid.SetColumnSpan(_modeHint, 2);

        filterBox.Controls.Add(grid);

        // --- General -------------------------------------------------------
        var generalBox = new GroupBox { Text = "General", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        var general = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
        general.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        general.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _autostart = new CheckBox { Text = "Start with Windows", AutoSize = true, Checked = settings.StartWithWindows };
        general.Controls.Add(_autostart, 0, 0);
        general.SetColumnSpan(_autostart, 2);

        _blockedLabel = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) };
        general.Controls.Add(_blockedLabel, 0, 1);
        var reset = new Button { Text = "Reset counter", AutoSize = true, Margin = new Padding(3, 4, 3, 3) };
        reset.Click += (_, _) =>
        {
            _settings.BlockedCount = 0;
            _settings.Save();
            RefreshBlocked();
        };
        general.Controls.Add(reset, 1, 1);
        generalBox.Controls.Add(general);

        // --- Buttons -------------------------------------------------------
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 8, 0, 0),
        };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
        var save = new Button { Text = "Save", AutoSize = true };
        var github = new Button { Text = "GitHub", AutoSize = true };
        github.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TrayAppContext.RepoUrl) { UseShellExecute = true });
            }
            catch
            {
                // ignore
            }
        };
        save.Click += (_, _) =>
        {
            Apply();
            save.Text = "Saved ✓";
            var t = new System.Windows.Forms.Timer { Interval = 1200 };
            t.Tick += (_, _) => { save.Text = "Save"; t.Stop(); t.Dispose(); };
            t.Start();
        };
        buttons.Controls.Add(close);
        buttons.Controls.Add(save);
        buttons.Controls.Add(github);

        root.Controls.Add(presets);
        root.Controls.Add(filterBox);
        root.Controls.Add(generalBox);
        root.Controls.Add(buttons);
        Controls.Add(root);

        AcceptButton = save;
        CancelButton = close;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(520, 0);

        UpdateModeUi();
        RefreshBlocked();
    }

    private Button PresetButton(string text, Action<AppSettings> apply)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(0, 0, 8, 0) };
        b.Click += (_, _) =>
        {
            // Apply to a scratch copy so the UI reflects the preset before saving.
            var scratch = new AppSettings();
            apply(scratch);
            _modeBalanced.Checked = scratch.Mode == FilterMode.Balanced;
            _modeStrict.Checked = scratch.Mode == FilterMode.Strict;
            _blockMs.Value = scratch.ReverseBlockMs;
            _confirmCount.Value = scratch.ConfirmDirectionCount;
            _minReverseGap.Value = scratch.MinReverseGapMs;
            _enabled.Checked = true;
            Apply();
        };
        return b;
    }

    private void UpdateModeUi()
    {
        var balanced = _modeBalanced.Checked;
        _confirmCount.Enabled = balanced;
        _confirmLabel.Enabled = balanced;
        _modeHint.Text = balanced
            ? "Balanced keeps fast up/down/up scrolling responsive: a real reversal costs one notch of latency and nothing is lost. Single-notch reversals inside the window are treated as ghosts."
            : "Strict blocks every opposite notch inside the window and extends it while ghosts keep firing. To reverse on purpose, stop scrolling for about the window length first.";
    }

    private void RefreshBlocked()
    {
        _blockedLabel.Text = $"Ghost scrolls blocked: {_settings.BlockedCount}";
    }

    private void Apply()
    {
        _settings.Enabled = _enabled.Checked;
        _settings.Mode = _modeStrict.Checked ? FilterMode.Strict : FilterMode.Balanced;
        _settings.ReverseBlockMs = (int)_blockMs.Value;
        _settings.ConfirmDirectionCount = (int)_confirmCount.Value;
        _settings.MinReverseGapMs = (int)_minReverseGap.Value;
        _settings.MinSameDirGapMs = (int)_minSameDirGap.Value;
        _settings.StartWithWindows = _autostart.Checked;
        _settings.Clamp();
        _settings.Save();

        try
        {
            Autostart.SetEnabled(_settings.StartWithWindows);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not update autostart:\n{ex.Message}", "Scroll Fix", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        RefreshBlocked();
        SettingsApplied?.Invoke(this, EventArgs.Empty);
    }
}
