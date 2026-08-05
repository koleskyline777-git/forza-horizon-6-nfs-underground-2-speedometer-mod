using System.IO;
using System.Windows;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;

namespace Nfsu2ForzaHud.Input;

/// <summary>System tray icon — app lives here instead of the taskbar.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notify;
    private readonly Window _window;
    private bool _exitRequested;

    public event Action? ShowRequested;
    public event Action? ExitRequested;
    public event Action? ToggleMoveRequested;

    public bool ExitRequestedFlag => _exitRequested;

    public TrayIcon(Window window, string? iconPath)
    {
        _window = window;
        DrawingIcon icon = !string.IsNullOrEmpty(iconPath) && File.Exists(iconPath)
            ? new DrawingIcon(iconPath)
            : DrawingSystemIcons.Application;

        _notify = new System.Windows.Forms.NotifyIcon
        {
            Icon = icon,
            Text = "NFSU2 Forza HUD",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notify.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    private System.Windows.Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show HUD", null, (_, _) => ShowRequested?.Invoke());
        menu.Items.Add("Hide to tray", null, (_, _) => _window.Dispatcher.Invoke(HideWindow));
        menu.Items.Add("Move / resize (F4)", null, (_, _) => ToggleMoveRequested?.Invoke());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit (F9)", null, (_, _) =>
        {
            _exitRequested = true;
            ExitRequested?.Invoke();
        });
        return menu;
    }

    private void HideWindow() => _window.Hide();

    public void ShowBalloon(string title, string text)
    {
        try
        {
            _notify.BalloonTipTitle = title;
            _notify.BalloonTipText = text;
            _notify.ShowBalloonTip(2500);
        }
        catch { /* ignore */ }
    }

    public void SetTooltip(string text)
    {
        _notify.Text = text.Length <= 63 ? text : text[..63];
    }

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Dispose();
    }
}
