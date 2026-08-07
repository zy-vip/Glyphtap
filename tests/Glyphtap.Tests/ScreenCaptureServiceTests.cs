using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Glyphtap.Services;
using Xunit;

namespace Glyphtap.Tests;

public class ScreenCaptureServiceTests
{
    private static Bitmap Solid(Color c, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(c);
        return bmp;
    }

    [Fact]
    public void Stitch_按虚拟坐标拼接两块()
    {
        var left = Solid(Color.Red, 100, 100);
        var right = Solid(Color.Blue, 50, 100);

        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(0, 0, 100, 100), left), (new Rect(100, 0, 50, 100), right) },
            new Rect(0, 0, 150, 100));

        Assert.Equal(150, result.Width);
        Assert.Equal(100, result.Height);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(50, 50).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(120, 50).ToArgb());
    }

    [Fact]
    public void Stitch_支持负坐标源()
    {
        var red = Solid(Color.Red, 50, 50);
        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(-50, -20, 50, 50), red) },
            new Rect(-50, -20, 50, 50));
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Stitch_空白处为透明()
    {
        var red = Solid(Color.Red, 10, 10);
        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(0, 0, 10, 10), red) },
            new Rect(0, 0, 20, 20));
        // GDI+ 透明填充落盘为 alpha=0（ARGB 0，透明黑）而非 Color.Transparent 的 0x00FFFFFF，故断言 Alpha
        Assert.Equal(0, result.GetPixel(15, 15).A);
    }
}