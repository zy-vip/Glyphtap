using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using Glyphtap.Capture;

namespace Glyphtap.Services;

public sealed record ScreenCaptureResult(Bitmap Bitmap, ScreenLayout Layout);

/// <summary>
/// 屏幕捕获：对每台显示器以各自 DPI 捕获后拼接为虚拟屏幕整图（物理像素）。
/// 捕获失败抛 InvalidOperationException，由调用方提示并退出截图模式。
/// </summary>
public static class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    private static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    public static ScreenCaptureResult Capture()
    {
        var layout = ScreenLayout.Create(MonitorEnumerator.Enumerate());
        var parts = new List<(Rect Dest, Bitmap Src)>();

        try
        {
            foreach (var monitor in layout.Monitors)
            {
                var prev = SetThreadDpiAwarenessContext(PerMonitorV2);
                try
                {
                    var b = monitor.Bounds;
                    var bmp = new Bitmap((int)b.Width, (int)b.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen((int)b.X, (int)b.Y, 0, 0, new System.Drawing.Size((int)b.Width, (int)b.Height));
                    }
                    parts.Add((new Rect(b.X, b.Y, b.Width, b.Height), bmp));
                }
                finally
                {
                    SetThreadDpiAwarenessContext(prev);
                }
            }

            return new ScreenCaptureResult(Stitch(parts, layout.VirtualBounds), layout);
        }
        finally
        {
            // 拼接已完成像素拷贝，源位图即可释放（成功与失败路径均回收）
            foreach (var (_, bmp) in parts)
                bmp.Dispose();
        }
    }

    /// <summary>按虚拟屏幕坐标把各屏位图拼接为整图（纯逻辑，可测试）。</summary>
    public static Bitmap Stitch(IReadOnlyList<(Rect Dest, Bitmap Src)> parts, Rect virtualBounds)
    {
        var w = (int)virtualBounds.Width;
        var h = (int)virtualBounds.Height;
        var canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            foreach (var (dest, src) in parts)
            {
                var x = (int)(dest.X - virtualBounds.X);
                var y = (int)(dest.Y - virtualBounds.Y);
                g.DrawImageUnscaled(src, x, y);
            }
        }
        return canvas;
    }
}