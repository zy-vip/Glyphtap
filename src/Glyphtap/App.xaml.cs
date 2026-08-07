using System.Windows;
using System.Windows.Interop;
using Glyphtap.Capture;
using Glyphtap.Infrastructure;
using Glyphtap.Services;

namespace Glyphtap;

public partial class App : Application
{
    private IDisposable? _singleInstanceGuard;
    private HotKeyService? _hotKey;
    private TrayIconService? _tray;
    private CaptureController? _controller;
    private Window? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire("Global\\Glyphtap.SingleInstance", out _singleInstanceGuard))
        {
            MessageBox.Show("Glyphtap 已在运行", "Glyphtap", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _controller = new CaptureController(Notify);

        _host = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        _host.Show();
        _host.Hide();

        var hwnd = new WindowInteropHelper(_host).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        _hotKey = HotKeyService.Register(hwnd, 0, 0x70); // F1
        if (_hotKey.IsRegistered)
        {
            source.AddHook((IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled) => _hotKey.OnWndProc(h, m, w, l, ref handled));
            _hotKey.HotKeyPressed += () => _controller.StartCapture();
        }
        else
        {
            Notify("热键注册失败", "F1 全局热键被占用，仍可通过托盘菜单截图");
        }

        _tray = new TrayIconService(() => _controller.StartCapture(), ExitApp);
    }

    private void Notify(string title, string message) => _tray?.ShowNotification(title, message);

    private void ExitApp()
    {
        _hotKey?.Dispose();
        _tray?.Dispose();
        _singleInstanceGuard?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _tray?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}