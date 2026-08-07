using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Glyphtap.Services;

public static class BitmapConvert
{
    /// <summary>GDI+ 位图 → WPF BitmapSource（内存拷贝，支持 32bppArgb）。</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return BitmapSource.Create(data.Width, data.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, bytes, stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}