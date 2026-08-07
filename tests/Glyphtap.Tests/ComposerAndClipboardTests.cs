using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glyphtap.Capture;
using Glyphtap.Services;
using Xunit;

namespace Glyphtap.Tests;

public class ComposerAndClipboardTests
{
    /// <summary>生成纯色 BitmapSource。</summary>
    private static BitmapSource Solid(Color c, int w, int h)
    {
        var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        var bytes = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            bytes[i * 4] = c.B;
            bytes[i * 4 + 1] = c.G;
            bytes[i * 4 + 2] = c.R;
            bytes[i * 4 + 3] = c.A;
        }
        bmp.WritePixels(new Int32Rect(0, 0, w, h), bytes, w * 4, 0);
        return bmp;
    }

    [StaFact]
    public void Compose_输出尺寸等于选区物理像素()
    {
        var full = Solid(Colors.White, 200, 200);
        var selection = new Rect(50, 50, 100, 60);
        var result = CaptureComposer.Compose(full, selection, Array.Empty<Annotation>());
        Assert.Equal(100, result.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    [StaFact]
    public void Compose_背景裁剪正确_取到选区像素()
    {
        var full = Solid(Colors.Red, 200, 200);
        var result = CaptureComposer.Compose(full, new Rect(10, 20, 50, 40), Array.Empty<Annotation>());
        var pixels = new byte[50 * 40 * 4];
        result.CopyPixels(pixels, 50 * 4, 0);
        Assert.Equal(Colors.Red.B, pixels[0]);
        Assert.Equal(Colors.Red.G, pixels[1]);
        Assert.Equal(Colors.Red.R, pixels[2]);
    }

    [StaFact]
    public void Compose_标注绘制在选区上()
    {
        var full = Solid(Colors.White, 100, 100);
        var rect = new RectangleAnnotation { Rect = new Rect(10, 10, 30, 30), Color = Colors.Blue, Thickness = 5 };
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 100, 100), new Annotation[] { rect });
        var pixels = new byte[100 * 100 * 4];
        result.CopyPixels(pixels, 100 * 4, 0);
        // 矩形顶边中点 (25, 10)：y=10 为 5px 边框中心线，避免落在边框边缘（反走样 50% 混合）
        var idx = (10 * 100 + 25) * 4;
        Assert.Equal(Colors.Blue.B, pixels[idx]);
        Assert.Equal(Colors.Blue.G, pixels[idx + 1]);
        Assert.Equal(Colors.Blue.R, pixels[idx + 2]);
    }

    [StaFact]
    public void Compose_超界标注被裁剪()
    {
        var full = Solid(Colors.White, 100, 100);
        // 画笔从选区内 (90,90) 延伸到选区外 (150,150)：选区内的部分 (90,90)-(100,100) 应可见
        var pen = new PenAnnotation { Color = Colors.Black, Thickness = 4 };
        pen.AddPoint(new Point(90, 90));
        pen.AddPoint(new Point(150, 150));
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 100, 100), new Annotation[] { pen });
        var pixels = new byte[100 * 100 * 4];
        result.CopyPixels(pixels, 100 * 4, 0);
        // 对角线上、位于选区内/裁剪区内的 (95,95) 应见黑色笔迹
        var idx = (95 * 100 + 95) * 4;
        Assert.Equal(0, pixels[idx]);
        Assert.Equal(0, pixels[idx + 1]);
        Assert.Equal(0, pixels[idx + 2]);
        // 远离线段 (10,0) 处为白色背景
        var far = (0 * 100 + 10) * 4;
        Assert.Equal(255, pixels[far]);
        Assert.Equal(255, pixels[far + 1]);
        Assert.Equal(255, pixels[far + 2]);
    }

    [StaFact]
    public void EncodePng_产出合法PNG头()
    {
        var png = ClipboardService.EncodePng(Solid(Colors.Green, 8, 8));
        Assert.True(png.Length > 0);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
    }
}