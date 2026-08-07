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
    public static BitmapSource Compose(BitmapSource fullScreen, Rect selectionPhysical, IReadOnlyList<Annotation> annotations)
    {
        var w = (int)Math.Ceiling(selectionPhysical.Width);
        var h = (int)Math.Ceiling(selectionPhysical.Height);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();

        using (var dc = dv.RenderOpen())
        {
            // 先裁剪到选区矩形，再绘制背景与标注，超界内容不显示
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

            // 背景：把整图平移到选区对齐（选区左上角 → 0,0）
            dc.DrawImage(fullScreen, new Rect(
                -(selectionPhysical.X),
                -(selectionPhysical.Y),
                fullScreen.PixelWidth,
                fullScreen.PixelHeight));

            // 标注（相对选区坐标），超界部分被 PushClip 裁掉
            foreach (var a in annotations)
                AnnotationRenderer.Draw(dc, a);

            dc.Pop();
        }

        rtb.Render(dv);
        return rtb;
    }
}