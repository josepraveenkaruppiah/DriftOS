using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace DriftOS.App;

public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "DriftOS";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var val = key?.GetValue(AppName) as string;
        return !string.IsNullOrEmpty(val);
    }

    public static void SetEnabled(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                      ?? Registry.CurrentUser.CreateSubKey(RunKey)!;

        if (enable)
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            key.SetValue(AppName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(AppName, false);
        }
    }
}
