using System.Runtime.InteropServices;
using System.Windows;

namespace Glyphtap.Capture;

/// <summary>显示器规格中间载体（便于测试与 Win32 数据归一）。</summary>
public sealed record MonitorSpec(Rect Bounds, int DpiX, int DpiY, bool IsPrimary);

/// <summary>枚举 Windows 显示器，产出物理像素坐标与 DPI 信息。</summary>
public static class MonitorEnumerator
{
    // ---- Win32 定义 ----
    private const int MonitorInfoFPrimary = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const int MdtEffectiveDpi = 0;

    /// <summary>枚举当前全部显示器。</summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var specs = new List<MonitorSpec>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, hdc, lprcMonitor, dwData) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out var dpiX, out var dpiY);
                    specs.Add(new MonitorSpec(
                        new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                                 info.rcMonitor.Right - info.rcMonitor.Left,
                                 info.rcMonitor.Bottom - info.rcMonitor.Top),
                        (int)dpiX, (int)dpiY,
                        (info.dwFlags & MonitorInfoFPrimary) != 0));
                }
                return true;
            }, IntPtr.Zero);

        return FromSpecs(specs);
    }

    /// <summary>从规格列表构建（纯逻辑，供测试与 Enumerate 复用）。</summary>
    public static IReadOnlyList<MonitorInfo> FromSpecs(IReadOnlyList<MonitorSpec> specs)
    {
        return specs.Select(s => new MonitorInfo
        {
            Bounds = s.Bounds,
            DpiX = s.DpiX,
            DpiY = s.DpiY,
            IsPrimary = s.IsPrimary,
        }).ToList();
    }
}