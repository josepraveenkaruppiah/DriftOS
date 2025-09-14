using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using DriftOS.Core.Settings;
using Serilog;

namespace DriftOS.App
{
    internal sealed class ForegroundWatcher : IDisposable
    {
        private readonly SettingsModel _settings;
        private readonly System.Threading.Timer _timer;
        private volatile bool _isGaming;
        private string _lastExe = "";
        private bool _lastFullscreen = false;

        public bool IsGaming => _isGaming;
        public string CurrentExe => _lastExe;

        public event Action<bool, string, bool>? StateChanged; // (isGaming, exe, isFullscreen)

        public ForegroundWatcher(SettingsModel settings)
        {
            _settings = settings;
            _timer = new System.Threading.Timer(Tick, null, 0, 200);
        }

        public void Dispose() => _timer.Dispose();

        private void Tick(object? _)
        {
            try
            {
                var hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                {
                    Update(false, "", false);
                    return;
                }

                // Get exe name
                GetWindowThreadProcessId(hwnd, out uint pid);
                string exe = "";
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    exe = (p.ProcessName + ".exe").ToLowerInvariant();
                }
                catch { /* process may have exited */ }

                // Check fullscreen (compare window to its monitor bounds)
                bool isFullscreen = false;
                if (_settings.PauseInFullscreenApps)
                {
                    if (GetWindowRect(hwnd, out RECT wr))
                    {
                        var hmon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                        MONITORINFO mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                        if (GetMonitorInfo(hmon, ref mi))
                        {
                            var mr = mi.rcMonitor;
                            const int tol = 2; // small tolerance
                            isFullscreen =
                                Math.Abs(wr.left - mr.left) <= tol &&
                                Math.Abs(wr.top - mr.top) <= tol &&
                                Math.Abs(wr.right - mr.right) <= tol &&
                                Math.Abs(wr.bottom - mr.bottom) <= tol;
                        }
                    }
                }

                // Blocklist match (wildcards supported)
                bool inBlocklist = false;
                if (!string.IsNullOrWhiteSpace(_settings.BlockedProcesses) && !string.IsNullOrEmpty(exe))
                {
                    var tokens = _settings.BlockedProcesses
                        .Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(t => t.Trim().ToLowerInvariant());
                    foreach (var pat in tokens)
                    {
                        if (WildcardIsMatch(exe, pat))
                        {
                            inBlocklist = true;
                            break;
                        }
                    }
                }

                bool gaming = isFullscreen || inBlocklist;
                Update(gaming, exe, isFullscreen);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ForegroundWatcher tick failed");
            }
        }

        private void Update(bool gaming, string exe, bool fullscreen)
        {
            if (gaming != _isGaming || !string.Equals(exe, _lastExe, StringComparison.Ordinal) || fullscreen != _lastFullscreen)
            {
                _isGaming = gaming;
                _lastExe = exe;
                _lastFullscreen = fullscreen;
                StateChanged?.Invoke(_isGaming, _lastExe, _lastFullscreen);
                if (_isGaming)
                    Log.Information("Auto-pause: {Exe} ({Reason})", _lastExe, _lastFullscreen ? "fullscreen" : "blocklist");
                else
                    Log.Information("Auto-pause: cleared (desktop/whitelisted)");
            }
        }

        private static bool WildcardIsMatch(string text, string pattern)
        {
            var rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(text, rx, RegexOptions.IgnoreCase);
        }

        #region Win32
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }
        #endregion
    }
}
