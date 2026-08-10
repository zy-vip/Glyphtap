using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glyphtap.Capture;

/// <summary>
/// 马赛克像素化：把源位图指定矩形区域做成像素块（须由 STA 线程调用）。
/// 算法：裁剪 → 缩到块粒度（BlocksPer 尺寸）→ NearestNeighbor 放大回原尺寸，产生硬边像素块。
/// </summary>
public static class MosaicPixelator
{
    public static BitmapSource Pixelate(BitmapSource source, Rect physicalRect, double blockSize)
    {
        var w = (int)Math.Ceiling(physicalRect.Width);
        var h = (int)Math.Ceiling(physicalRect.Height);
        var blocksW = Math.Max(1, (int)Math.Ceiling(w / blockSize));
        var blocksH = Math.Max(1, (int)Math.Ceiling(h / blockSize));

        var cropped = new CroppedBitmap(source, new Int32Rect(
            (int)physicalRect.X, (int)physicalRect.Y, w, h));
        var small = RenderScaled(cropped, blocksW, blocksH, BitmapScalingMode.Linear);
        return RenderScaled(small, w, h, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>把 src 渲染到目标尺寸的位图（ImageBrush + RenderTargetBitmap，插值模式可指定）。</summary>
    private static BitmapSource RenderScaled(BitmapSource src, int w, int h, BitmapScalingMode mode)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new ImageBrush(src) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, mode);
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }
}