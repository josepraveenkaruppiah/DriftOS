using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace DriftOS.App
{
    internal static class TouchKeyboard
    {
        // COM: UIHostNoLaunch exposes ITipInvocation.Toggle(hwnd)
        [ComImport, Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376")]
        private class UIHostNoLaunch { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
         Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
        private interface ITipInvocation
        {
            void Toggle(IntPtr hwnd);
        }

        // Win32 helpers
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        private const int SW_SHOW = 5;

        public static void ShowOrToggle()
        {
            // 1) Preferred: COM toggle of TabTip (Touch Keyboard)
            try
            {
                var inv = (ITipInvocation)new UIHostNoLaunch();
                var hTarget = GetForegroundWindow();
                inv.Toggle(hTarget);
                Serilog.Log.Information("Touch keyboard: COM Toggle OK (hWnd={Handle})", hTarget);
                return;
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80040154 /* REGDB_E_CLASSNOTREG */)
            {
                Serilog.Log.Information("Touch keyboard: UIHostNoLaunch not registered; will launch TabTip.exe");
            }
            catch (COMException ex) when ((uint)ex.HResult == 0x80004002 /* E_NOINTERFACE */)
            {
                Serilog.Log.Information("Touch keyboard: ITipInvocation not available; will launch TabTip.exe");
            }
            catch (Exception ex)
            {
                Serilog.Log.Information("Touch keyboard: COM path failed: {Msg}", ex.Message);
            }

            // 2) Launch TabTip.exe and surface the window (no OSK fallback)
            try
            {
                var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
                var tabtip = Path.Combine(common, "microsoft shared", "ink", "TabTip.exe");

                if (File.Exists(tabtip))
                {
                    Process.Start(new ProcessStartInfo(tabtip) { UseShellExecute = true });
                    Serilog.Log.Information("Touch keyboard: launched TabTip.exe");

                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Thread.Sleep(600);
                        var w = FindWindow("IPTip_Main_Window", null);
                        if (w != IntPtr.Zero)
                        {
                            ShowWindow(w, SW_SHOW);
                            SetForegroundWindow(w);
                            Serilog.Log.Information("Touch keyboard: brought TabTip to foreground");
                        }
                        else
                        {
                            Serilog.Log.Warning("Touch keyboard: TabTip window not found after launch");
                        }
                    });
                }
                else
                {
                    Serilog.Log.Warning("Touch keyboard: TabTip.exe not found");
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Touch keyboard: TabTip launch failed");
            }
        }
    }
}
