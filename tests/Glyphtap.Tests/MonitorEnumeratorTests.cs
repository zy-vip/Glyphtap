using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class MonitorEnumeratorTests
{
    [Fact]
    public void FromSpecs_保留DPI与主屏标记()
    {
        var monitors = MonitorEnumerator.FromSpecs(new[]
        {
            new MonitorSpec(new Rect(0, 0, 1920, 1080), 96, 96, true),
            new MonitorSpec(new Rect(1920, 0, 1280, 1024), 144, 144, false),
        });

        Assert.Equal(2, monitors.Count);
        Assert.Equal(1920, monitors[0].Bounds.Width);
        Assert.Equal(144, monitors[1].DpiX);
        Assert.True(monitors[0].IsPrimary);
        Assert.False(monitors[1].IsPrimary);
    }

    [Fact]
    public void FromSpecs_空列表返回空()
    {
        Assert.Empty(MonitorEnumerator.FromSpecs(Array.Empty<MonitorSpec>()));
    }
}