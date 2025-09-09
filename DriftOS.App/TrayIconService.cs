using System;
using System.Diagnostics;
using System.Drawing;
// NOTE: we DO NOT import System.Windows here to avoid "Application" ambiguity
using WF = System.Windows.Forms;

namespace DriftOS.App;

public sealed class TrayIconService : IDisposable
{
    private readonly WF.NotifyIcon _notifyIcon;
    private readonly WF.ToolStripMenuItem _toggleItem;
    private Icon? _icon; // keep a ref so GC doesn't collect it

    public event Action? ToggleEnableRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;

    public TrayIconService(bool enabled)
    {
        _toggleItem = new WF.ToolStripMenuItem(enabled ? "Disable" : "Enable");
        _toggleItem.Click += (_, __) => ToggleEnableRequested?.Invoke();

        var settingsItem = new WF.ToolStripMenuItem("Settings…");
        settingsItem.Click += (_, __) => OpenSettingsRequested?.Invoke();

        var exitItem = new WF.ToolStripMenuItem("Exit");
        exitItem.Click += (_, __) => ExitRequested?.Invoke();

        _notifyIcon = new WF.NotifyIcon
        {
            Visible = true,
            Text = "DriftOS",
            Icon = LoadAppIcon() ?? SystemIcons.Application,
            ContextMenuStrip = new WF.ContextMenuStrip()
        };
        _notifyIcon.ContextMenuStrip.Items.AddRange(new WF.ToolStripItem[]
        {
            _toggleItem, new WF.ToolStripSeparator(), settingsItem, exitItem
        });
    }

    public void SetEnabled(bool enabled) => _toggleItem.Text = enabled ? "Disable" : "Enable";

    public void ShowInfo(string title, string text)
        => _notifyIcon.ShowBalloonTip(2000, title, text, WF.ToolTipIcon.Info);

    private Icon? LoadAppIcon()
    {
        // Try WPF pack resource first (requires <Resource Include="Assets\app.ico" /> in csproj)
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute);
            var sri = System.Windows.Application.GetResourceStream(uri); // fully-qualified WPF Application
            if (sri?.Stream != null)
            {
                _icon = new Icon(sri.Stream);
                return _icon;
            }
        }
        catch { /* fall through to EXE icon */ }

        // Fallback: use the EXE's associated icon (requires <ApplicationIcon>Assets\app.ico</ApplicationIcon>)
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                _icon = Icon.ExtractAssociatedIcon(exePath);
                if (_icon != null) return _icon;
            }
        }
        catch { /* ignore */ }

        return null;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }
}
