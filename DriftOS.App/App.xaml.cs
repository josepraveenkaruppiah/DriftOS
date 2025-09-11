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

    private bool _enabled = true;
    private bool _latchedMode = false;
    private bool _rbDown = false;
    private bool _injectPrev = false;

    private bool _isLeftDown = false;
    private bool _isRightDown = false;

    // Pointer movement
    private long _lastTicks = 0;
    private double _filtVx = 0, _filtVy = 0;
    private double _accumX = 0, _accumY = 0;
    private const double MaxStepPx = 18;

    // Scrolling
    private long _lastScrollTicks = 0;
    private double _filtSv = 0, _filtSh = 0;
    private double _accSv = 0, _accSh = 0;
    private const int WheelDelta = 120;

    private ushort _prevButtons = 0;

    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single-instance guard
        bool createdNew;
        _singleInstanceMutex = new Mutex(true, @"Global\DriftOS_SingleInstance", out createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("DriftOS is already running.", "DriftOS",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // Logging
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriftOS", "logs", "driftos-.log");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(a => a.File(logsPath, rollingInterval: RollingInterval.Day))
            .CreateLogger();

        // Settings
        SettingsStore = new JsonSettingsStore();
        Settings = SettingsStore.Load();

        // Migration / sane defaults
        if (Settings.PointerSpeed <= 0) Settings.PointerSpeed = Settings.Sensitivity > 0 ? Settings.Sensitivity : 1.0;
        if (Settings.ScrollSpeedV <= 0) Settings.ScrollSpeedV = Settings.PointerSpeed;
        if (Settings.ScrollSpeedH <= 0) Settings.ScrollSpeedH = Settings.PointerSpeed;
        if (Settings.PointerAlpha <= 0) Settings.PointerAlpha = 0.35;
        if (Settings.ScrollAlpha <= 0) Settings.ScrollAlpha = 0.50;
        if (Settings.ScrollGamma <= 0) Settings.ScrollGamma = 1.60;

        _mouse = new SendInputMouseOutput();
        _poller = new XInputPoller(hz: 120);

        _poller.OnStateEx += (lx, ly, rx, ry, buttons) =>
        {
            const ushort RB = 0x0200;
            const ushort A = 0x1000;
            const ushort B = 0x2000;
            const ushort DPAD_UP = 0x0001;
            const ushort DPAD_DOWN = 0x0002;
            const ushort DPAD_LEFT = 0x0004;
            const ushort DPAD_RIGHT = 0x0008;

            // RB toggle on release
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
                double curved = scaled * scaled * scaled;
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
            double hDrive = (sx) + dH; // right = positive
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

            // Clicks
            bool aNow = (buttons & A) != 0;
            if (aNow && !_isLeftDown) { _mouse!.LeftDown(); _isLeftDown = true; }
            if (!aNow && _isLeftDown) { _mouse!.LeftUp(); _isLeftDown = false; }

            bool bNow = (buttons & B) != 0;
            if (bNow && !_isRightDown) { _mouse!.RightDown(); _isRightDown = true; }
            if (!bNow && _isRightDown) { _mouse!.RightUp(); _isRightDown = false; }

            _prevButtons = buttons;
        };

        _poller.Start();

        // Tray
        _tray = new TrayIconService(enabled: _enabled);
        _tray.ShowInfo("DriftOS", "RB toggles mouse mode · Right stick scroll");

        _tray.ToggleEnableRequested += () =>
        {
            bool wasInjecting = _enabled && _latchedMode;
            _enabled = !_enabled;
            _tray!.SetEnabled(_enabled);

            if (wasInjecting && !_enabled)
            {
                if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
                if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
                _accumX = _accumY = 0; _filtVx = _filtVy = 0;
                _filtSv = _filtSh = 0; _accSv = _accSh = 0; _lastScrollTicks = 0;
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
            try
            {
                var win = new SettingsWindow();
                win.Show();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "Failed to open SettingsWindow");
                System.Windows.MessageBox.Show(ex.ToString(), "Settings error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        _tray.ExitRequested += () => Shutdown();

        _lastTicks = Stopwatch.GetTimestamp();
        _lastScrollTicks = _lastTicks;

        base.OnStartup(e);
        Log.Information("DriftOS started. PS={PS} SV={SV} SH={SH} DZ={DZ} αp={PA} αs={SA} γ={SG}",
            Settings.PointerSpeed, Settings.ScrollSpeedV, Settings.ScrollSpeedH, Settings.Deadzone,
            Settings.PointerAlpha, Settings.ScrollAlpha, Settings.ScrollGamma);
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
        finally { Log.CloseAndFlush(); }
        base.OnExit(e);
    }
}
