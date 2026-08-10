using Microsoft.Win32;

namespace ScrollFix;

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScrollFix";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey, true);

        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Unable to resolve ScrollFix executable path.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else if (key.GetValue(ValueName) is not null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        return key?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
    }
}
