using System.Drawing;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace Glyphtap.Services;

/// <summary>托盘图标与菜单（截图 / 退出）。</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _tray;
    private Icon? _icon;

    public TrayIconService(Action onCapture, Action onExit)
    {
        _icon = CreateTrayIcon();
        var menu = new System.Windows.Controls.ContextMenu();
        var captureItem = new System.Windows.Controls.MenuItem { Header = "截图 (F1)" };
        captureItem.Click += (_, _) => onCapture();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => onExit();
        menu.Items.Add(captureItem);
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            Icon = _icon,
            ToolTipText = "Glyphtap 截图工具",
            ContextMenu = menu,
        };
    }

    public void ShowNotification(string title, string message)
        => _tray.ShowNotification(title, message, NotificationIcon.Info);

    private static Icon CreateTrayIcon()
    {
        // 程序生成简易图标（青色圆点），避免引入二进制资源
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0, 150, 136));
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        var h = bmp.GetHicon();
        return Icon.FromHandle(h);
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _tray.Dispose();
    }
}