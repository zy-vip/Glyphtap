using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glyphtap.Capture;

/// <summary>
/// 合成最终截图：裁剪选区背景 + 合并标注，输出物理像素位图。
/// 背景图为整块虚拟屏幕位图（BitmapSource，物理像素）；selectionPhysical 为物理像素选区。
/// 必须由 STA 线程调用。
/// </summary>
public static class CaptureComposer
{
    public static BitmapSource Compose(BitmapSource fullScreen, Rect selectionPhysical, IReadOnlyList<Annotation> annotations, Point bitmapOrigin = default)
    {
        var w = (int)Math.Ceiling(selectionPhysical.Width);
        var h = (int)Math.Ceiling(selectionPhysical.Height);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();

        using (var dc = dv.RenderOpen())
        {
            // 先裁剪到选区矩形，再绘制背景与标注，超界内容不显示
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

            // 背景：把整图平移到选区对齐（选区左上角 → 0,0；位图原点在 bitmapOrigin，减掉后才是位图像素位置）
            dc.DrawImage(fullScreen, new Rect(
                -(selectionPhysical.X - bitmapOrigin.X),
                -(selectionPhysical.Y - bitmapOrigin.Y),
                fullScreen.PixelWidth,
                fullScreen.PixelHeight));

            // 标注（相对选区坐标），超界部分被 PushClip 裁掉；马赛克需先覆盖背景再画其他标注
            foreach (var a in annotations)
            {
                if (a is MosaicAnnotation m)
                {
                    DrawMosaic(dc, fullScreen, selectionPhysical, m, bitmapOrigin);
                    continue;
                }
                AnnotationRenderer.Draw(dc, a);
            }

            dc.Pop();
        }

        rtb.Render(dv);
        return rtb;
    }

    /// <summary>把马赛克区域块化后覆盖到背景上（区域换算为虚拟屏幕绝对物理像素）。</summary>
    private static void DrawMosaic(DrawingContext dc, BitmapSource fullScreen, Rect selectionPhysical, MosaicAnnotation m, Point bitmapOrigin)
    {
        var abs = new Rect(
            selectionPhysical.X + m.Rect.X,
            selectionPhysical.Y + m.Rect.Y,
            m.Rect.Width,
            m.Rect.Height);
        // 与位图边界求交（位图边界用物理坐标空间表示，原点 = bitmapOrigin）：防止越界区域导致 CroppedBitmap 抛异常
        var clip = Rect.Intersect(abs, new Rect(bitmapOrigin.X, bitmapOrigin.Y, fullScreen.PixelWidth, fullScreen.PixelHeight));
        if (clip.IsEmpty)
            return;
        // Pixelate 的矩形按位图像素坐标理解：物理坐标减位图原点转换；回画位置保持物理坐标相对选区（clip 与 selectionPhysical 同空间）
        var blocky = MosaicPixelator.Pixelate(fullScreen, new Rect(
            clip.X - bitmapOrigin.X, clip.Y - bitmapOrigin.Y, clip.Width, clip.Height), m.BlockSize);
        dc.DrawImage(blocky, new Rect(
            clip.X - selectionPhysical.X,
            clip.Y - selectionPhysical.Y,
            clip.Width,
            clip.Height));
    }
}