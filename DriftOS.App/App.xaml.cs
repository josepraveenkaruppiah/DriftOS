using System;
using System.Diagnostics;   // Stopwatch, Process
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;     // Mutex
using System.Windows;       // WPF
using System.Windows.Interop; // HwndSource
using DriftOS.Core.IO;
using DriftOS.Core.Settings;
using DriftOS.Input.XInput;
using Serilog;

namespace DriftOS.App;

public partial class App : System.Windows.Application
{
    // ----- Settings & persistence -----
    public static ISettingsStore SettingsStore { get; private set; } = null!;
    public static SettingsModel Settings { get; private set; } = null!;

    // ----- Core services -----
    private XInputPoller? _poller;
    private IMouseOutput? _mouse;
    private TrayIconService? _tray;

    // ----- State -----
    private bool _enabled = true;      // master enable
    private bool _latchedMode = false; // RB toggle state
    private bool _rbDown = false;      // RB press tracking
    private bool _injectPrev = false;  // previous inject state

    private bool _isLeftDown = false;
    private bool _isRightDown = false;

    // Pointer movement accumulators/filters
    private long _lastTicks = 0;
    private double _filtVx = 0, _filtVy = 0;
    private double _accumX = 0, _accumY = 0;
    private const double MaxStepPx = 18;

    // Scrolling accumulators/filters
    private long _lastScrollTicks = 0;
    private double _filtSv = 0, _filtSh = 0;
    private double _accSv = 0, _accSh = 0;
    private const int WheelDelta = 120;

    private ushort _prevButtons = 0;

    // Single-instance guard
    private Mutex? _singleInstanceMutex;

    // LB (touch keyboard) debounce
    private long _lastKbTicks = 0;

    // ===== Global Hotkey support (Ctrl+Shift+F10) =====
    private IntPtr _hotkeyHwnd = IntPtr.Zero;
    private HwndSource? _hotkeySource;

    private const int WM_HOTKEY = 0x0312;
    private const int HOTKEY_ID_TOGGLE = 0xD051;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008;
    private const uint VK_F10 = 0x79;

    // ===== COM for TabTip (touch keyboard) =====
    [ComImport, Guid("37C994E7-432B-401B-91B1-63F5E5674A1F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation { void Toggle(IntPtr hwnd); }
    [ComImport, Guid("4CE576FA-83DC-4f88-951C-9D0782B4E376")]
    private class TipInvocation { }

    // ===== Win32 helpers for TabTip =====
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_SHOW = 5;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---- Single-instance guard ----
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, @"Global\DriftOS_SingleInstance", out createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("DriftOS is already running.", "DriftOS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ---- Serilog ----
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriftOS", "logs", "driftos-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(a => a.File(logsPath, rollingInterval: RollingInterval.Day))
            .CreateLogger();

        // ---- Settings ----
        SettingsStore = new JsonSettingsStore();
        Settings = SettingsStore.Load();
        if (Settings.PointerSpeed <= 0) Settings.PointerSpeed = Settings.Sensitivity > 0 ? Settings.Sensitivity : 1.0;
        if (Settings.ScrollSpeedV <= 0) Settings.ScrollSpeedV = Settings.PointerSpeed;
        if (Settings.ScrollSpeedH <= 0) Settings.ScrollSpeedH = Settings.PointerSpeed;
        if (Settings.PointerAlpha <= 0) Settings.PointerAlpha = 0.35;
        if (Settings.ScrollAlpha <= 0) Settings.ScrollAlpha = 0.50;
        if (Settings.ScrollGamma <= 0) Settings.ScrollGamma = 1.60;

        try { AutoStart.Apply(Settings.AutoStart); } catch { }

        // ---- IO backends ----
        _mouse = new SendInputMouseOutput();
        _poller = new XInputPoller(hz: 120);

        // ---- Controller loop ----
        _poller.OnStateEx += (lx, ly, rx, ry, buttons) =>
        {
            const ushort RB = 0x0200;
            const ushort LB = 0x0100; // Left bumper -> touch keyboard
            const ushort A = 0x1000;
            const ushort B = 0x2000;
            const ushort DPAD_UP = 0x0001;
            const ushort DPAD_DOWN = 0x0002;
            const ushort DPAD_LEFT = 0x0004;
            const ushort DPAD_RIGHT = 0x0008;

            // RB toggle on release edge
            bool rbNow = (buttons & RB) != 0;
            bool rbWas = (_prevButtons & RB) != 0;
            if (rbNow && !rbWas) _rbDown = true;
            else if (!rbNow && rbWas)
            {
                if (_rbDown)
                {
                    _latchedMode = !_latchedMode;
                    _tray?.ShowInfo("DriftOS", _latchedMode ? "Mouse mode ON (RB toggle)" : "Mouse mode OFF");
                }
                _rbDown = false;
            }

            // LB → show/toggle touch keyboard on release (300ms debounce)
            bool lbNow = (buttons & LB) != 0;
            bool lbWas = (_prevButtons & LB) != 0;
            if (!lbNow && lbWas)
            {
                long now = Stopwatch.GetTimestamp();
                double since = (now - _lastKbTicks) / (double)Stopwatch.Frequency;
                if (since > 0.30)
                {
                    // REPLACE THIS LINE:
                    // ShowTouchKeyboard();  or Dispatcher.BeginInvoke(() => ShowTouchKeyboard());

                    // WITH THIS:
                    Dispatcher.BeginInvoke(new Action(TouchKeyboard.ShowOrToggle));

                    _lastKbTicks = now;
                }
            }

            bool injectNow = _enabled && _latchedMode;

            // Transitions
            if (!injectNow && _injectPrev)
            {
                if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
                if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
                _accumX = _accumY = 0; _filtVx = _filtVy = 0;
                _filtSv = _filtSh = 0; _accSv = _accSh = 0; _lastScrollTicks = 0;
            }
            else if (injectNow && !_injectPrev)
            {
                _lastTicks = Stopwatch.GetTimestamp();
                _accumX = _accumY = 0; _filtVx = _filtVy = 0;
                _filtSv = _filtSh = 0; _accSv = _accSh = 0; _lastScrollTicks = _lastTicks;
            }
            _injectPrev = injectNow;

            if (!injectNow)
            {
                _prevButtons = buttons;
                return;
            }

            // ---------------- Pointer (left stick) ----------------
            double dz = Math.Clamp(Settings.Deadzone, 0.0, 0.30);
            double mag = Math.Sqrt(lx * lx + ly * ly);

            if (mag >= dz)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                double dt = Math.Max(1e-3, (nowTicks - _lastTicks) / (double)Stopwatch.Frequency);
                _lastTicks = nowTicks;

                double scaled = (mag - dz) / (1.0 - dz);
                double curved = Math.Pow(scaled, 3);
                double pps = 900.0 * Settings.PointerSpeed;

                double ux = lx / mag;
                double uy = ly / mag;

                double targetVx = ux * curved * pps;
                double targetVy = -uy * curved * pps;

                double alphaPtr = Math.Clamp(Settings.PointerAlpha, 0.05, 0.95);
                if (_filtVx != 0 && targetVx != 0 && Math.Sign(_filtVx) != Math.Sign(targetVx)) { _filtVx = 0; _accumX = 0; }
                if (_filtVy != 0 && targetVy != 0 && Math.Sign(_filtVy) != Math.Sign(targetVy)) { _filtVy = 0; _accumY = 0; }
                _filtVx += alphaPtr * (targetVx - _filtVx);
                _filtVy += alphaPtr * (targetVy - _filtVy);

                _accumX += _filtVx * dt;
                _accumY += _filtVy * dt;

                int dx = (int)Math.Truncate(Math.Clamp(_accumX, -MaxStepPx, MaxStepPx));
                int dy = (int)Math.Truncate(Math.Clamp(_accumY, -MaxStepPx, MaxStepPx));
                if (dx != 0 || dy != 0)
                {
                    _accumX -= dx; _accumY -= dy;
                    _mouse!.Move(dx, dy);
                }
            }
            else
            {
                _filtVx = _filtVy = 0; _accumX = _accumY = 0;
            }

            // ---------------- Scrolling (right stick + D-pad) ----------------
            long nowS = Stopwatch.GetTimestamp();
            if (_lastScrollTicks == 0) _lastScrollTicks = nowS;
            double dtS = Math.Max(1e-3, (nowS - _lastScrollTicks) / (double)Stopwatch.Frequency);
            _lastScrollTicks = nowS;

            double dzScroll = Math.Clamp(Settings.Deadzone, 0.0, 0.30);

            // Right-stick analog
            double rmag = Math.Sqrt(rx * rx + ry * ry);
            double sx = 0, sy = 0;
            if (rmag >= dzScroll)
            {
                double scaledS = (rmag - dzScroll) / (1.0 - dzScroll);
                sx = (rx / rmag) * scaledS;
                sy = (ry / rmag) * scaledS;
            }

            // D-pad digital
            int dV = ((buttons & DPAD_UP) != 0 ? +1 : 0) + ((buttons & DPAD_DOWN) != 0 ? -1 : 0);
            int dH = ((buttons & DPAD_RIGHT) != 0 ? +1 : 0) + ((buttons & DPAD_LEFT) != 0 ? -1 : 0);

            // Combined drives
            double vDrive = (-sy) + dV; // up = positive
            double hDrive = (sx) + dH;  // right = positive
            vDrive = Math.Clamp(vDrive, -1, 1);
            hDrive = Math.Clamp(hDrive, -1, 1);

            double gammaS = Math.Clamp(Settings.ScrollGamma, 1.0, 2.5);
            double driveV = Math.Sign(vDrive) * Math.Pow(Math.Abs(vDrive), gammaS);
            double driveH = Math.Sign(hDrive) * Math.Pow(Math.Abs(hDrive), gammaS);

            double baseV = 18.0 * Settings.ScrollSpeedV;
            double baseH = 16.0 * Settings.ScrollSpeedH;
            double targetSv = driveV * baseV;
            double targetSh = driveH * baseH;

            if (Settings.InvertScrollV) targetSv = -targetSv;
            if (Settings.InvertScrollH) targetSh = -targetSh;

            if (_filtSv != 0 && targetSv != 0 && Math.Sign(_filtSv) != Math.Sign(targetSv)) { _filtSv = 0; _accSv = 0; }
            if (_filtSh != 0 && targetSh != 0 && Math.Sign(_filtSh) != Math.Sign(targetSh)) { _filtSh = 0; _accSh = 0; }

            double alphaS = Math.Clamp(Settings.ScrollAlpha, 0.05, 0.95);
            _filtSv += alphaS * (targetSv - _filtSv);
            _filtSh += alphaS * (targetSh - _filtSh);

            _accSv += _filtSv * dtS;
            _accSh += _filtSh * dtS;

            while (_accSv >= 1.0) { _mouse!.Scroll(+WheelDelta); _accSv -= 1.0; }
            while (_accSv <= -1.0) { _mouse!.Scroll(-WheelDelta); _accSv += 1.0; }
            while (_accSh >= 1.0) { _mouse!.HScroll(+WheelDelta); _accSh -= 1.0; }
            while (_accSh <= -1.0) { _mouse!.HScroll(-WheelDelta); _accSh += 1.0; }

            if (rmag < dzScroll && dV == 0 && dH == 0)
            {
                _filtSv = _filtSh = 0; _accSv = _accSh = 0;
            }

            // Clicks (A/B)
            bool aNow = (buttons & A) != 0;
            if (aNow && !_isLeftDown) { _mouse!.LeftDown(); _isLeftDown = true; }
            if (!aNow && _isLeftDown) { _mouse!.LeftUp(); _isLeftDown = false; }

            bool bNow = (buttons & B) != 0;
            if (bNow && !_isRightDown) { _mouse!.RightDown(); _isRightDown = true; }
            if (!bNow && _isRightDown) { _mouse!.RightUp(); _isRightDown = false; }

            _prevButtons = buttons;
        };

        _poller.Start();

        // ---- Tray ----
        _tray = new TrayIconService(enabled: _enabled);
        _tray.ShowInfo("DriftOS", "RB toggles mouse mode · Right stick scroll");
        _tray.ToggleEnableRequested += () => ToggleEnabledFromAnywhere();
        _tray.OpenSettingsRequested += () =>
        {
            try { new SettingsWindow().Show(); }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open SettingsWindow");
                System.Windows.MessageBox.Show(ex.ToString(), "Settings error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        _tray.ExitRequested += () => Shutdown();

        // ---- Hotkey host window + registration ----
        var parms = new HwndSourceParameters("DriftOS_HotkeyWnd")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            Width = 0,
            Height = 0,
            ParentWindow = IntPtr.Zero
        };
        _hotkeySource = new HwndSource(parms);
        _hotkeySource.AddHook(WndProc);
        _hotkeyHwnd = _hotkeySource.Handle;

        bool ok = RegisterHotKey(_hotkeyHwnd, HOTKEY_ID_TOGGLE, MOD_CONTROL | MOD_SHIFT, VK_F10);
        Log.Information("Register hotkey Ctrl+Shift+F10: {OK}", ok);

        // ---- Timing init & banner ----
        _lastTicks = Stopwatch.GetTimestamp();
        _lastScrollTicks = _lastTicks;

        base.OnStartup(e);
        Log.Information("DriftOS started. PS={PS} SV={SV} SH={SH} DZ={DZ} αp={PA} αs={SA} γ={SG}",
            Settings.PointerSpeed, Settings.ScrollSpeedV, Settings.ScrollSpeedH, Settings.Deadzone,
            Settings.PointerAlpha, Settings.ScrollAlpha, Settings.ScrollGamma);
    }

    // Called by tray toggle & hotkey
    private void ToggleEnabledFromAnywhere()
    {
        bool wasInjecting = _enabled && _latchedMode;
        _enabled = !_enabled;
        _tray?.SetEnabled(_enabled);

        if (wasInjecting && !_enabled)
        {
            if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
            if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
            _accumX = _accumY = 0; _filtVx = _filtVy = 0;
            _filtSv = _filtSh = 0; _accSv = _accSh = 0; _lastScrollTicks = 0;
            _injectPrev = false;
            _tray?.ShowInfo("DriftOS", "Controller as mouse: OFF");
        }
        else
        {
            _tray?.ShowInfo("DriftOS", _enabled ? "Controller as mouse: ON" : "Controller as mouse: OFF");
        }
        Log.Information("Global toggle → {Enabled}", _enabled);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID_TOGGLE)
        {
            ToggleEnabledFromAnywhere();
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            try { SettingsStore.Save(Settings); }
            catch (Exception ex) { Log.Warning(ex, "Save on exit failed"); }

            if (_hotkeyHwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hotkeyHwnd, HOTKEY_ID_TOGGLE); }
                catch (Exception ex) { Log.Warning(ex, "UnregisterHotKey failed"); }
                _hotkeyHwnd = IntPtr.Zero;
            }

            if (_hotkeySource is not null)
            {
                try { _hotkeySource.RemoveHook(WndProc); } catch { /* ignore */ }
                _hotkeySource.Dispose();
                _hotkeySource = null;
            }
        }
        finally
        {
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }

    // ---- Touch keyboard launcher (COM TabTip preferred; no OSK fallback) ----
    private void ShowTouchKeyboard()
    {
        // 1) COM toggle – pass the current foreground window (more reliable than IntPtr.Zero)
        try
        {
            var inv = (ITipInvocation)new TipInvocation();
            var hFg = GetForegroundWindow();
            inv.Toggle(hFg != IntPtr.Zero ? hFg : _hotkeyHwnd);
            Serilog.Log.Information("Touch keyboard: COM Toggle OK (hWnd={Handle})", hFg);
            return;
        }
        catch (Exception ex)
        {
            Serilog.Log.Information("Touch keyboard: COM Toggle failed: {Msg}", ex.Message);
        }

        // 2) Launch TabTip.exe and attempt to surface its window
        try
        {
            var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            var tabtip = Path.Combine(common, "microsoft shared", "ink", "TabTip.exe");
            if (File.Exists(tabtip))
            {
                Process.Start(new ProcessStartInfo(tabtip) { UseShellExecute = true });
                Serilog.Log.Information("Touch keyboard: launched TabTip.exe");

                // Wait briefly, then try to bring it to front if hidden
                System.Threading.Tasks.Task.Run(() =>
                {
                    System.Threading.Thread.Sleep(600);
                    var h = FindWindow("IPTip_Main_Window", null);
                    if (h != IntPtr.Zero)
                    {
                        ShowWindow(h, SW_SHOW);
                        SetForegroundWindow(h);
                        Serilog.Log.Information("Touch keyboard: brought TabTip to foreground");
                    }
                    else
                    {
                        Serilog.Log.Warning("Touch keyboard: TabTip window not found after launch");
                    }
                });
                return;
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

        // No OSK fallback by request; just log.
        Serilog.Log.Warning("Touch keyboard: unable to show TabTip (no OSK fallback)");
    }
}
