using System.Runtime.InteropServices;

namespace Glyphtap.Services;

/// <summary>全局热键：RegisterHotKey + WndProc 回调。注册失败 IsRegistered=false。</summary>
public sealed class HotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private readonly IntPtr _hwnd;
    private readonly int _id;
    private bool _disposed;

    public event Action? HotKeyPressed;
    public bool IsRegistered { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HotKeyService(IntPtr hwnd, int id, uint modifier, uint key)
    {
        _hwnd = hwnd;
        _id = id;
        IsRegistered = RegisterHotKey(hwnd, id, modifier, key);
    }

    public static HotKeyService Register(IntPtr hwnd, uint modifier, uint key)
        => new(hwnd, 1, modifier, key);

    /// <summary>WPF 消息钩子入口：由宿主窗口的 HwndSource.AddHook 调用。</summary>
    public IntPtr OnWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _id)
        {
            handled = true;
            HotKeyPressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (IsRegistered)
            UnregisterHotKey(_hwnd, _id);
    }
}