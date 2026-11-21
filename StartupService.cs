using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace HelperBar;

internal static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HelperBar.Personal";
    public static string CommandFor(string executable) => $"\"{executable}\" --startup";
    private static string Command => CommandFor(Path.Combine(AppContext.BaseDirectory, "HelperBar.exe"));
    public static bool IsEnabled()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return string.Equals(key?.GetValue(ValueName) as string, Command, StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException) { return false; }
    }
    public static bool SetEnabled(bool enabled, out string message)
    {
        try
        {
            if (enabled && (!File.Exists(Path.Combine(AppContext.BaseDirectory, "HelperBar.exe")) || Command.Length > 260))
            { message = "Не вдалося додати запуск: перевір шлях до HelperBar.exe."; return false; }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
            else key.DeleteValue(ValueName, false);
            message = enabled ? "Острів запускатиметься разом із Windows" : "Автозапуск вимкнено";
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        { message = "Windows не дозволила змінити автозапуск."; return false; }
    }
}
