using System.Text.Json;

namespace ScrollFix;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// When true, any opposite scroll inside ReverseBlockMs is dropped (best for worn encoders).
    /// </summary>
    public bool AggressiveMode { get; set; } = true;

    public int ReverseBlockMs { get; set; } = 220;
    public int MaxGhostNotches { get; set; } = 2;
    public int ConfirmDirectionCount { get; set; } = 3;
    public bool StartWithWindows { get; set; }
    public int BlockedCount { get; set; }

    public static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScrollFix");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public void Clamp()
    {
        ReverseBlockMs = Math.Clamp(ReverseBlockMs, 40, 500);
        MaxGhostNotches = Math.Clamp(MaxGhostNotches, 1, 6);
        ConfirmDirectionCount = Math.Clamp(ConfirmDirectionCount, 1, 5);
        BlockedCount = Math.Max(0, BlockedCount);
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                var defaults = new AppSettings();
                defaults.Save();
                return defaults;
            }

            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            // Upgrade older settings files that never had AggressiveMode / weak defaults.
            if (!json.Contains("AggressiveMode", StringComparison.Ordinal))
            {
                settings.AggressiveMode = true;
                if (settings.ReverseBlockMs < 200)
                {
                    settings.ReverseBlockMs = 220;
                }

                if (settings.ConfirmDirectionCount < 3)
                {
                    settings.ConfirmDirectionCount = 3;
                }
            }

            settings.Clamp();
            settings.Save();
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Clamp();
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
    }
}
