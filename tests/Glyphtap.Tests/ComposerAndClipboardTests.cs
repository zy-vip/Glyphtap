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
    public void Compose_马赛克标注_选区像素被块化()
    {
        // 背景：左半红右半蓝（4x4，每 2 列一色）
        var full = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
        var bytes = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var c = x < 2 ? Colors.Red : Colors.Blue;
                var i = (y * 4 + x) * 4;
                bytes[i] = c.B; bytes[i + 1] = c.G; bytes[i + 2] = c.R; bytes[i + 3] = 255;
            }
        }
        full.WritePixels(new Int32Rect(0, 0, 4, 4), bytes, 4 * 4, 0);

        // 马赛克覆盖中间 2x2 区域：块大小 2 → 块化后整块取像素平均，红蓝混合成紫
        var mosaic = new MosaicAnnotation { Rect = new Rect(1, 1, 2, 2), BlockSize = 2 };
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 4, 4), new Annotation[] { mosaic });
        var outPx = new byte[4 * 4 * 4];
        result.CopyPixels(outPx, 4 * 4, 0);

        // 马赛克块中心 (2,2) 应为红蓝混合（128, 0, 128 附近）
        var idx = (2 * 4 + 2) * 4;
        Assert.True(outPx[idx + 2] > 64 && outPx[idx + 2] < 192, $"R={outPx[idx + 2]}");
        Assert.Equal(0, outPx[idx + 1]);
        Assert.True(outPx[idx] > 64 && outPx[idx] < 192, $"B={outPx[idx]}");
        // 块外 (0,0) 保持纯红
        var outer = 0;
        Assert.Equal(255, outPx[outer + 2]);
        Assert.Equal(0, outPx[outer]);
    }

    [StaFact]
    public void Compose_负偏移虚拟屏下背景与马赛克定位正确()
    {
        // 背景：左半红右半蓝（4x4，每 2 列一色），位图像素原点位于物理 (-2,0)（模拟副屏在主屏左侧）
        var full = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
        var bytes = new byte[4 * 4 * 4];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                var c = x < 2 ? Colors.Red : Colors.Blue;
                var i = (y * 4 + x) * 4;
                bytes[i] = c.B; bytes[i + 1] = c.G; bytes[i + 2] = c.R; bytes[i + 3] = 255;
            }
        }
        full.WritePixels(new Int32Rect(0, 0, 4, 4), bytes, 4 * 4, 0);

        // 推导：位图像素(px,py) = 物理(px-2, py)；选区恰好覆盖整张位图 → 输出(ox,oy) = 位图像素(ox,oy)
        var origin = new Point(-2, 0);
        var selection = new Rect(-2, 0, 4, 4);

        // 无标注：输出 (0,0)=位图像素(0,0)=红；(3,0)=位图像素(3,0)=蓝
        var plain = CaptureComposer.Compose(full, selection, Array.Empty<Annotation>(), origin);
        var px = new byte[4 * 4 * 4];
        plain.CopyPixels(px, 4 * 4, 0);
        Assert.Equal(Colors.Red.B, px[0]);
        Assert.Equal(Colors.Red.G, px[1]);
        Assert.Equal(Colors.Red.R, px[2]);
        var bl = (0 * 4 + 3) * 4;
        Assert.Equal(Colors.Blue.B, px[bl]);
        Assert.Equal(Colors.Blue.G, px[bl + 1]);
        Assert.Equal(Colors.Blue.R, px[bl + 2]);

        // 马赛克 Rect(1,1,2,2) 是选区相对坐标 → 物理 (-1,1,2,2) = 位图像素 (1,1)-(3,3)（2x2 红蓝块）
        // 块大小 2 → 整块取像素平均成紫 (128,0,128)，回画在输出 (1,1,2,2)
        var mosaic = new MosaicAnnotation { Rect = new Rect(1, 1, 2, 2), BlockSize = 2 };
        var result = CaptureComposer.Compose(full, selection, new Annotation[] { mosaic }, origin);
        var outPx = new byte[4 * 4 * 4];
        result.CopyPixels(outPx, 4 * 4, 0);

        // 马赛克块中心 (2,2) 应为红蓝混合（128, 0, 128 附近）
        var idx = (2 * 4 + 2) * 4;
        Assert.True(outPx[idx + 2] > 64 && outPx[idx + 2] < 192, $"R={outPx[idx + 2]}");
        Assert.Equal(0, outPx[idx + 1]);
        Assert.True(outPx[idx] > 64 && outPx[idx] < 192, $"B={outPx[idx]}");
        // 块外 (0,0) 保持纯红
        var outer = 0;
        Assert.Equal(255, outPx[outer + 2]);
        Assert.Equal(0, outPx[outer]);
    }

    [StaFact]
    public void Compose_高亮标注_半透明色块叠在背景上()
    {
        var full = Solid(Colors.White, 50, 50);
        var highlight = new HighlightAnnotation
        {
            Rect = new Rect(10, 10, 30, 30),
            Color = Color.FromArgb(255, 0, 120, 255), // 蓝色系
        };
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 50, 50), new Annotation[] { highlight });
        var pixels = new byte[50 * 50 * 4];
        result.CopyPixels(pixels, 50 * 4, 0);
        // 高亮中心 (25,25)：白色背景混 35% 蓝色 → 蓝通道显著上升、红通道下降
        var idx = (25 * 50 + 25) * 4;
        Assert.True(pixels[idx + 2] < 255, $"R={pixels[idx + 2]}");   // 红被蓝压暗
        Assert.True(pixels[idx] > pixels[idx + 2], $"B={pixels[idx]}"); // 蓝高于红
        Assert.True(pixels[idx] > 64, $"B={pixels[idx]}");
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