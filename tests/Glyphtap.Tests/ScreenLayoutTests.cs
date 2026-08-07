using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class ScreenLayoutTests
{
    private static readonly MonitorInfo Primary =
        new() { Bounds = new Rect(0, 0, 1920, 1080), DpiX = 96, DpiY = 96, IsPrimary = true };

    private static readonly MonitorInfo Secondary =
        new() { Bounds = new Rect(-1280, 0, 1280, 1024), DpiX = 144, DpiY = 144, IsPrimary = false };

    [Fact]
    public void Create_合并虚拟屏幕矩形_含负坐标()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Equal(new Rect(-1280, 0, 3200, 1080), layout.VirtualBounds);
    }

    [Fact]
    public void Create_主屏scale为基准()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Equal(1.0, layout.PrimaryScale);
        Assert.Equal(1.5, layout.Monitors[1].ScaleX);
    }

    [Fact]
    public void MonitorAtPhysical_按坐标命中屏幕()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Same(Secondary, layout.MonitorAtPhysical(new Point(-100, 500)));
        Assert.Same(Primary, layout.MonitorAtPhysical(new Point(100, 500)));
    }

    [Fact]
    public void ToPhysical_与_ToWindowDips_往返一致()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        var physical = layout.ToPhysical(new Point(100, 50));
        var dips = layout.ToWindowDips(physical);
        Assert.Equal(100, dips.X, 2);
        Assert.Equal(50, dips.Y, 2);
    }

    [Fact]
    public void Create_无显示器时抛出()
    {
        Assert.Throws<ArgumentException>(() => ScreenLayout.Create(Array.Empty<MonitorInfo>()));
    }
}