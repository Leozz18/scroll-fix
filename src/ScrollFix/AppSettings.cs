using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrollFix;

public enum FilterMode
{
    /// <summary>Hold the first opposite notch, confirm with the next one, replay if real.</summary>
    Balanced = 0,

    /// <summary>Drop every opposite notch inside the window. Reverse requires a pause.</summary>
    Strict = 1,
}

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public const int DefaultBlockMs = 220;
    public const int DefaultConfirmCount = 2;

    public bool Enabled { get; set; } = true;

    public FilterMode Mode { get; set; } = FilterMode.Balanced;

    /// <summary>Opposite notches arriving within this many ms are suspicious.</summary>
    public int ReverseBlockMs { get; set; } = DefaultBlockMs;

    /// <summary>Balanced mode: opposite notches needed to confirm a real reversal (min 2).</summary>
    public int ConfirmDirectionCount { get; set; } = DefaultConfirmCount;

    public bool StartWithWindows { get; set; }

    public int BlockedCount { get; set; }

    /// <summary>Legacy v1.0 flag, read only to detect old settings files; never written back.</summary>
    [JsonPropertyName("AggressiveMode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? LegacyAggressiveMode { get; set; }

    public static string SettingsDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScrollFix");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public void Clamp()
    {
        ReverseBlockMs = Math.Clamp(ReverseBlockMs, 40, 500);
        ConfirmDirectionCount = Math.Clamp(ConfirmDirectionCount, 2, 5);
        BlockedCount = Math.Max(0, BlockedCount);
    }

    /// <summary>Preset for badly worn encoders that fire ghost bursts.</summary>
    public void ApplyWornEncoderPreset()
    {
        Mode = FilterMode.Strict;
        ReverseBlockMs = 260;
        ConfirmDirectionCount = DefaultConfirmCount;
    }

    /// <summary>Preset for people who scroll up/down rapidly (gaming, timelines).</summary>
    public void ApplyQuickReversePreset()
    {
        Mode = FilterMode.Balanced;
        ReverseBlockMs = 160;
        ConfirmDirectionCount = DefaultConfirmCount;
    }

    /// <summary>Default preset.</summary>
    public void ApplyBalancedPreset()
    {
        Mode = FilterMode.Balanced;
        ReverseBlockMs = DefaultBlockMs;
        ConfirmDirectionCount = DefaultConfirmCount;
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
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Migrate v1.0.x files to the new default. v1.0 "aggressive" blocked rapid
            // intentional reversals (the #1 complaint); Balanced fixes that while still
            // dropping single ghosts. Users with ghost bursts can pick the Worn encoder preset.
            if (settings.LegacyAggressiveMode is not null && !json.Contains("\"Mode\"", StringComparison.Ordinal))
            {
                settings.ApplyBalancedPreset();
            }

            settings.LegacyAggressiveMode = null;
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
        LegacyAggressiveMode = null;
        Directory.CreateDirectory(SettingsDirectory);

        // Write to a temp file first so a crash mid-write cannot corrupt settings.
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, SettingsPath, overwrite: true);
    }
}
