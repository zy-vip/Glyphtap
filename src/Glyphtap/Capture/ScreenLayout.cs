using System.Windows;

namespace Glyphtap.Capture;

/// <summary>单台显示器信息（物理像素坐标）。</summary>
public sealed class MonitorInfo
{
    /// <summary>物理像素，虚拟屏幕坐标。</summary>
    public Rect Bounds { get; set; }

    /// <summary>该显示器实际 DPI（PerMonitorV2）。</summary>
    public int DpiX { get; set; }
    public int DpiY { get; set; }

    public bool IsPrimary { get; set; }

    /// <summary>DPI 缩放因子。</summary>
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
}

/// <summary>
/// 虚拟屏幕布局：合并所有显示器，提供物理像素与窗口 DIP 坐标的双向换算。
/// 换算基准 = 主屏 scale（WPF 窗口坐标系以主屏 DPI 为准）。
/// </summary>
public sealed class ScreenLayout
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly MonitorInfo _primary;

    private ScreenLayout(IReadOnlyList<MonitorInfo> monitors, Rect virtualBounds, MonitorInfo primary)
    {
        _monitors = monitors;
        VirtualBounds = virtualBounds;
        _primary = primary;
    }

    public static ScreenLayout Create(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
            throw new ArgumentException("至少需要一个显示器", nameof(monitors));

        var union = monitors[0].Bounds;
        foreach (var m in monitors)
            union.Union(m.Bounds);

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        return new ScreenLayout(monitors, union, primary);
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>所有显示器合并矩形（物理像素，可为负坐标）。</summary>
    public Rect VirtualBounds { get; }

    public MonitorInfo Primary => _primary;

    /// <summary>主屏 DPI 缩放因子，窗口 DIP ↔ 物理像素的换算基准。</summary>
    public double PrimaryScale => _primary.ScaleX;

    /// <summary>命中测试：返回包含该物理坐标的显示器，未命中返回主屏。</summary>
    public MonitorInfo MonitorAtPhysical(Point p)
    {
        foreach (var m in _monitors)
        {
            if (m.Bounds.Contains(p))
                return m;
        }
        return _primary;
    }

    /// <summary>窗口内 DIP 坐标（相对窗口左上角）→ 虚拟屏幕物理像素。</summary>
    public Point ToPhysical(Point windowDips)
    {
        var px = windowDips.X * PrimaryScale;
        var py = windowDips.Y * PrimaryScale;
        return new Point(VirtualBounds.X + px, VirtualBounds.Y + py);
    }

    /// <summary>虚拟屏幕物理像素 → 窗口内 DIP 坐标。</summary>
    public Point ToWindowDips(Point physical)
    {
        var dx = physical.X - VirtualBounds.X;
        var dy = physical.Y - VirtualBounds.Y;
        return new Point(dx / PrimaryScale, dy / PrimaryScale);
    }
}