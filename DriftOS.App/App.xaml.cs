using System;
using System.Diagnostics;   // Stopwatch
using System.IO;
using System.Threading;     // Mutex
using System.Windows;       // WPF
using DriftOS.Core.IO;
using DriftOS.Core.Settings;
using DriftOS.Input.XInput;
using Serilog;

namespace DriftOS.App;

public partial class App : System.Windows.Application
{
    public static ISettingsStore SettingsStore { get; private set; } = null!;
    public static SettingsModel Settings { get; private set; } = null!;

    private XInputPoller? _poller;
    private IMouseOutput? _mouse;
    private TrayIconService? _tray;

    // Master enable from tray
    private bool _enabled = true;

    // RB toggle (latched mouse mode)
    private bool _latchedMode = false;      // toggled on RB release
    private bool _rbDown = false;           // RB currently held
    private bool _injectPrev = false;      // previous "injecting?" state

    // Track what we pressed (to avoid stuck clicks)
    private bool _isLeftDown = false;
    private bool _isRightDown = false;

    // Pointer movement (left stick): time-based + smoothing
    private long _lastTicks = 0;
    private double _filtVx = 0, _filtVy = 0;    // filtered px/sec
    private double _accumX = 0, _accumY = 0;    // sub-pixel accumulators
    private const double MaxStepPx = 18;        // per-frame clamp

    // Right-stick smooth scrolling: filtered notches/sec + accumulators
    private long _lastScrollTicks = 0;
    private double _filtSv = 0, _filtSh = 0;    // filtered notches/sec (vertical/horizontal)
    private double _accSv = 0, _accSh = 0;    // accumulated notches to emit
    private const int WheelDelta = 120;

    private ushort _prevButtons = 0;

    // single-instance
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // ---------- Single-instance guard ----------
        bool createdNew;
        _singleInstanceMutex = new Mutex(initiallyOwned: true, name: @"Global\DriftOS_SingleInstance", out createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "DriftOS is already running.", "DriftOS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // ---------- Logging ----------
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriftOS", "logs", "driftos-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(a => a.File(logsPath, rollingInterval: RollingInterval.Day))
            .CreateLogger();

        // ---------- Settings ----------
        SettingsStore = new JsonSettingsStore();
        Settings = SettingsStore.Load();

        // ---------- IO + poller ----------
        _mouse = new SendInputMouseOutput();
        _poller = new XInputPoller(hz: 120);

        // Extended poller event: (lx, ly, rx, ry, buttons)
        _poller.OnStateEx += (lx, ly, rx, ry, buttons) =>
        {
            // Button masks (define once)
            const ushort RB = 0x0200; // Right bumper -> toggle
            const ushort A = 0x1000; // A -> left click
            const ushort B = 0x2000; // B -> right click
            const ushort DPAD_UP = 0x0001;
            const ushort DPAD_DOWN = 0x0002;
            const ushort DPAD_LEFT = 0x0004;
            const ushort DPAD_RIGHT = 0x0008;

            // ---- RB toggle on RELEASE ----
            bool rbNow = (buttons & RB) != 0;
            bool rbWas = (_prevButtons & RB) != 0;

            if (rbNow && !rbWas) _rbDown = true;
            else if (!rbNow && rbWas)
            {
                if (_rbDown)
                {
                    _latchedMode = !_latchedMode;
                    _tray?.ShowInfo("DriftOS",
                        _latchedMode ? "Mouse mode ON (RB toggle)" : "Mouse mode OFF");
                }
                _rbDown = false;
            }

            // Decide injection based on BOTH master enable and latched mode
            bool injectNow = _enabled && _latchedMode;

            // Transitions: OFF → release & clear; ON → reset timebases/integrators
            if (!injectNow && _injectPrev)
            {
                if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
                if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
                _accumX = _accumY = 0;
                _filtVx = _filtVy = 0;

                _filtSv = _filtSh = 0;
                _accSv = _accSh = 0;
                _lastScrollTicks = 0;
            }
            else if (injectNow && !_injectPrev)
            {
                _lastTicks = Stopwatch.GetTimestamp();
                _accumX = _accumY = 0;
                _filtVx = _filtVy = 0;

                _filtSv = _filtSh = 0;
                _accSv = _accSh = 0;
                _lastScrollTicks = _lastTicks;
            }
            _injectPrev = injectNow;

            if (!injectNow)
            {
                _prevButtons = buttons;
                return;
            }

            // ===================== Pointer: left stick =====================
            double dz = Math.Clamp(Settings.Deadzone, 0.0, 0.30);
            double mag = Math.Sqrt(lx * lx + ly * ly);

            if (mag >= dz)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                double dt = Math.Max(1e-3, (nowTicks - _lastTicks) / (double)Stopwatch.Frequency);
                _lastTicks = nowTicks;

                double scaled = (mag - dz) / (1.0 - dz);
                double curved = scaled * scaled * scaled;  // cubic easing
                double basePps = 900.0;                    // tweak 700–1100
                double pps = basePps * Settings.Sensitivity;

                double ux = (mag == 0 ? 0.0 : lx / mag);
                double uy = (mag == 0 ? 0.0 : ly / mag);

                double targetVx = ux * curved * pps;
                double targetVy = -uy * curved * pps;

                // Zero-cross snap: kill inertia on instant reversals
                if (_filtVx != 0 && targetVx != 0 && Math.Sign(_filtVx) != Math.Sign(targetVx))
                { _filtVx = 0; _accumX = 0; }
                if (_filtVy != 0 && targetVy != 0 && Math.Sign(_filtVy) != Math.Sign(targetVy))
                { _filtVy = 0; _accumY = 0; }

                const double alpha = 0.35; // smoothing (0..1)
                _filtVx += alpha * (targetVx - _filtVx);
                _filtVy += alpha * (targetVy - _filtVy);

                _accumX += _filtVx * dt;
                _accumY += _filtVy * dt;

                double stepX = Math.Truncate(Math.Clamp(_accumX, -MaxStepPx, MaxStepPx));
                double stepY = Math.Truncate(Math.Clamp(_accumY, -MaxStepPx, MaxStepPx));

                int dx = (int)stepX;
                int dy = (int)stepY;

                if (dx != 0 || dy != 0)
                {
                    _accumX -= dx;
                    _accumY -= dy;
                    _mouse!.Move(dx, dy);
                }
            }
            else
            {
                // Inside deadzone: drop velocity & remainder so no carry-through
                _filtVx = _filtVy = 0;
                _accumX = _accumY = 0;
            }

            // ===================== Scrolling: right stick + D-pad =====================
            long nowS = Stopwatch.GetTimestamp();
            if (_lastScrollTicks == 0) _lastScrollTicks = nowS;
            double dtS = Math.Max(1e-3, (nowS - _lastScrollTicks) / (double)Stopwatch.Frequency);
            _lastScrollTicks = nowS;

            double dzScroll = Math.Clamp(Settings.Deadzone, 0.0, 0.30);

            // Right-stick analog contribution
            double rmag = Math.Sqrt(rx * rx + ry * ry);
            double sx = 0, sy = 0;
            if (rmag >= dzScroll)
            {
                double scaled = (rmag - dzScroll) / (1.0 - dzScroll);   // 0..1
                double ux = rx / rmag;
                double uy = ry / rmag;
                sx = ux * scaled;     // [-1..1]
                sy = uy * scaled;
            }

            // D-pad digital contribution
            int dV = ((buttons & DPAD_UP) != 0 ? +1 : 0) + ((buttons & DPAD_DOWN) != 0 ? -1 : 0);
            int dH = ((buttons & DPAD_RIGHT) != 0 ? +1 : 0) + ((buttons & DPAD_LEFT) != 0 ? -1 : 0);

            // Combine (up = positive vertical scroll, right = positive horizontal)
            double vDrive = (-sy) + dV;  // invert Y: stick up → scroll up
            double hDrive = (sx) + dH;

            // Clamp to avoid spikes when mixing inputs
            vDrive = Math.Max(-1, Math.Min(1, vDrive));
            hDrive = Math.Max(-1, Math.Min(1, hDrive));

            // Shape: shallower power → stronger mid-stick (less laggy)
            const double gammaS = 1.6;
            double driveV = Math.Sign(vDrive) * Math.Pow(Math.Abs(vDrive), gammaS);
            double driveH = Math.Sign(hDrive) * Math.Pow(Math.Abs(hDrive), gammaS);

            // Target notch rates
            double baseV = 18.0 * Settings.Sensitivity; // vertical notches/sec
            double baseH = 16.0 * Settings.Sensitivity; // horizontal
            double targetSv = driveV * baseV;
            double targetSh = driveH * baseH;

            // Zero-cross snap (scroll)
            if (_filtSv != 0 && targetSv != 0 && Math.Sign(_filtSv) != Math.Sign(targetSv)) { _filtSv = 0; _accSv = 0; }
            if (_filtSh != 0 && targetSh != 0 && Math.Sign(_filtSh) != Math.Sign(targetSh)) { _filtSh = 0; _accSh = 0; }

            // Snappier EMA
            const double alphaS = 0.50;
            _filtSv += alphaS * (targetSv - _filtSv);
            _filtSh += alphaS * (targetSh - _filtSh);

            // Integrate filtered notches/sec → notches
            _accSv += _filtSv * dtS;
            _accSh += _filtSh * dtS;

            // Emit whole notches (sign-aware)
            while (_accSv >= 1.0) { _mouse!.Scroll(+WheelDelta); _accSv -= 1.0; }
            while (_accSv <= -1.0) { _mouse!.Scroll(-WheelDelta); _accSv += 1.0; }
            while (_accSh >= 1.0) { _mouse!.HScroll(+WheelDelta); _accSh -= 1.0; }
            while (_accSh <= -1.0) { _mouse!.HScroll(-WheelDelta); _accSh += 1.0; }

            // If both analog + digital are idle and inside dz: stop residual motion
            if (rmag < dzScroll && dV == 0 && dH == 0)
            {
                _filtSv = _filtSh = 0;
                _accSv = _accSh = 0;
            }

            // ===================== Clicks (A/B) =====================
            bool aNow = (buttons & A) != 0;
            if (aNow && !_isLeftDown) { _mouse!.LeftDown(); _isLeftDown = true; }
            if (!aNow && _isLeftDown) { _mouse!.LeftUp(); _isLeftDown = false; }

            bool bNow = (buttons & B) != 0;
            if (bNow && !_isRightDown) { _mouse!.RightDown(); _isRightDown = true; }
            if (!bNow && _isRightDown) { _mouse!.RightUp(); _isRightDown = false; }

            _prevButtons = buttons;
        };

        _poller.Start();

        // ---------- Tray ----------
        _tray = new TrayIconService(enabled: _enabled);
        _tray.ShowInfo("DriftOS", "Press RB to toggle mouse mode (right stick scroll)");

        _tray.ToggleEnableRequested += () =>
        {
            bool wasInjecting = _enabled && _latchedMode;

            _enabled = !_enabled;
            _tray!.SetEnabled(_enabled);

            if (wasInjecting && !_enabled)
            {
                if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
                if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
                _accumX = _accumY = 0;
                _filtVx = _filtVy = 0;
                _filtSv = _filtSh = 0;
                _accSv = _accSh = 0;
                _lastScrollTicks = 0;
                _injectPrev = false;
                _tray.ShowInfo("DriftOS", "Controller as mouse: OFF");
            }
            else
            {
                _tray.ShowInfo("DriftOS", _enabled ? "Controller as mouse: ON" : "Controller as mouse: OFF");
            }

            Log.Information("Master enable toggled → {Enabled}", _enabled);
        };

        _tray.OpenSettingsRequested += () =>
        {
            var win = new SettingsWindow();
            win.Show();
        };

        _tray.ExitRequested += () => Shutdown();

        // init timebases
        _lastTicks = Stopwatch.GetTimestamp();
        _lastScrollTicks = _lastTicks;

        base.OnStartup(e);
        Log.Information("DriftOS started. Sensitivity={Sens} Deadzone={Dz}",
            Settings.Sensitivity, Settings.Deadzone);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _poller?.Dispose();
            SettingsStore.Save(Settings);
            _tray?.Dispose();

            try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
            _singleInstanceMutex?.Dispose();
        }
        finally
        {
            Log.CloseAndFlush();
        }

        base.OnExit(e);
    }
}
