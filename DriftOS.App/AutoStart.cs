using System;
using System.IO;
using Microsoft.Win32;

namespace DriftOS.App
{
    internal static class AutoStart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DriftOS";

        public static bool IsEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
                var val = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(val);
            }
            catch
            {
                return false;
            }
        }

        public static void Apply(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                               ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, true);

                if (enable)
                {
                    var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                              ?? Path.Combine(AppContext.BaseDirectory, "DriftOS.App.exe");

                    // Quote full path; no args needed.
                    key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
                }
                else
                {
                    // Use positional arg to support all frameworks/param names.
                    try { key.DeleteValue(ValueName, false); } catch { /* ignore if missing */ }
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to apply autostart setting (enable={Enable})", enable);
            }
        }

        // ---- Back-compat shims for older call sites ----
        public static void SetEnabled(bool enable) => Apply(enable);
        public static bool GetEnabled() => IsEnabled();
    }
}
