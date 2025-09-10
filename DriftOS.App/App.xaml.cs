using System;
using System.Diagnostics;          // Stopwatch
using System.IO;
using System.Windows;              // WPF
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

    // Time-based motion + smoothing
    private long _lastTicks = 0;
    private double _filtVx = 0, _filtVy = 0;    // filtered px/sec
    private double _accumX = 0, _accumY = 0;    // sub-pixel accumulators
    private const double MaxStepPx = 18;        // per-frame clamp

    private ushort _prevButtons = 0;

    protected override void OnStartup(StartupEventArgs e)
    {
        var logsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriftOS", "logs", "driftos-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Async(a => a.File(logsPath, rollingInterval: RollingInterval.Day))
            .CreateLogger();

        SettingsStore = new JsonSettingsStore();
        Settings = SettingsStore.Load();

        _mouse = new SendInputMouseOutput();
        _poller = new XInputPoller(hz: 120);

        _poller.OnState += (lx, ly, buttons) =>
        {
            // Button masks (define ONCE here)
            const ushort RB = 0x0200; // Right bumper -> toggle
            const ushort A = 0x1000; // A -> left click
            const ushort B = 0x2000; // B -> right click

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

            // Transitions: OFF → release; ON → reset timebase/integrators
            if (!injectNow && _injectPrev)
            {
                if (_isLeftDown) { try { _mouse!.LeftUp(); } catch { } _isLeftDown = false; }
                if (_isRightDown) { try { _mouse!.RightUp(); } catch { } _isRightDown = false; }
                _accumX = _accumY = 0;
                _filtVx = _filtVy = 0;
            }
            else if (injectNow && !_injectPrev)
            {
                _lastTicks = Stopwatch.GetTimestamp();
                _accumX = _accumY = 0;
                _filtVx = _filtVy = 0;
            }
            _injectPrev = injectNow;

            if (!injectNow)
            {
                _prevButtons = buttons;
                return;
            }

            // ---- stick -> mouse move (deadzone + time-based velocity + smoothing) ----
            double dz = Math.Clamp(Settings.Deadzone, 0.0, 0.30);
            double mag = Math.Sqrt(lx * lx + ly * ly);

            if (mag >= dz)
            {
                long nowTicks = Stopwatch.GetTimestamp();
                double dt = Math.Max(1e-3, (nowTicks - _lastTicks) / (double)Stopwatch.Frequency);
                _lastTicks = nowTicks;

                // direction & easing
                double scaled = (mag - dz) / (1.0 - dz);
                double curved = scaled * scaled * scaled;  // cubic easing
                double basePps = 900.0;                    // tweak 700–1100 if needed
                double pps = basePps * Settings.Sensitivity;

                double ux = (mag == 0 ? 0.0 : lx / mag);
                double uy = (mag == 0 ? 0.0 : ly / mag);

                double targetVx = ux * curved * pps;
                double targetVy = -uy * curved * pps;

                // Zero-crossing snap: kill inertia on instant reversals
                if (_filtVx != 0 && targetVx != 0 && Math.Sign(_filtVx) != Math.Sign(targetVx))
                {
                    _filtVx = 0; _accumX = 0;
                }
                if (_filtVy != 0 && targetVy != 0 && Math.Sign(_filtVy) != Math.Sign(targetVy))
                {
                    _filtVy = 0; _accumY = 0;
                }

                // smoothing (EMA)
                const double alpha = 0.35; // 0..1 (higher = snappier)
                _filtVx += alpha * (targetVx - _filtVx);
                _filtVy += alpha * (targetVy - _filtVy);

                // integrate to pixels
                _accumX += _filtVx * dt;
                _accumY += _filtVy * dt;

                // clamp per-frame step
                double stepX = Math.Truncate(Math.Max(-MaxStepPx, Math.Min(MaxStepPx, _accumX)));
                double stepY = Math.Truncate(Math.Max(-MaxStepPx, Math.Min(MaxStepPx, _accumY)));

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

            // ---- Buttons while injecting: A -> left, B -> right (track state) ----
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
        _tray.ShowInfo("DriftOS", "Press RB to toggle mouse mode");

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

        _lastTicks = Stopwatch.GetTimestamp();

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
        }
        finally { Log.CloseAndFlush(); }

        base.OnExit(e);
    }
}
