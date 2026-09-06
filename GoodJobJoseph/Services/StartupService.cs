using Microsoft.Win32;

namespace JosephExperience.Services;

public static class StartupService
{
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "JosephExperience";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
            var value = key?.GetValue(ValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RegistryKey);
            if (enabled)
            {
                key?.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            }
            else
            {
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Failure here should not crash the app.
        }
    }
}
