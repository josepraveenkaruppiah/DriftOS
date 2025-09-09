using System;
using System.IO;
using System.Windows;                 // WPF
using DriftOS.Core.Settings;
using DriftOS.Core.IO;
using DriftOS.Input.XInput;
using Serilog;

namespace DriftOS.App;

public partial class App : System.Windows.Application
{
    public static ISettingsStore SettingsStore { get; private set; } = null!;
    public static SettingsModel Settings { get; private set; } = null!;

    private XInputPoller? _poller;
    private IMouseOutput? _mouse;

    private TrayIconService? _tray;   // <-- add this
    private bool _enabled = true;     // <-- and this

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
            if (!_enabled) return;

            var dz = (float)Math.Clamp(Settings.Deadzone, 0.0, 0.30);
            var mag = MathF.Sqrt(lx * lx + ly * ly);
            if (mag < dz) return;

            var scaled = (mag - dz) / (1f - dz);
            var gain = (float)(8.0 * Settings.Sensitivity);

            int dx = (int)(lx / (mag == 0 ? 1 : mag) * scaled * gain);
            int dy = (int)(-ly / (mag == 0 ? 1 : mag) * scaled * gain);

            _mouse!.Move(dx, dy);
        };
        _poller.Start();

        // Tray:
        _tray = new TrayIconService(enabled: _enabled);
        _tray.ShowInfo("DriftOS", "Tray running. Right-click me!");

        _tray.ToggleEnableRequested += () =>
        {
            _enabled = !_enabled;
            _tray!.SetEnabled(_enabled);
            _tray.ShowInfo("DriftOS", _enabled ? "Controller as mouse: ON" : "Controller as mouse: OFF");
        };
        _tray.OpenSettingsRequested += () =>
        {
            var win = new SettingsWindow();
            win.Show();
        };
        _tray.ExitRequested += () => Shutdown();

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
