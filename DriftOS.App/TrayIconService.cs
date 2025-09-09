using System;
using System.Drawing;                 // SystemIcons.Application
using WF = System.Windows.Forms;       // alias to avoid WPF name clashes

namespace DriftOS.App;

public sealed class TrayIconService : IDisposable
{
	private readonly WF.NotifyIcon _notifyIcon;
	private readonly WF.ToolStripMenuItem _toggleItem;

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
			Icon = SystemIcons.Application,               // replace with your .ico later
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

	public void Dispose()
	{
		_notifyIcon.Visible = false;
		_notifyIcon.Dispose();
	}
}
