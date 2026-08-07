using System.IO;
using System.Windows.Media.Imaging;

namespace Glyphtap.Services;

/// <summary>剪贴板服务。MVP：图片；接口同时支持文本（V2 OCR 复用）。须由 STA 线程调用。</summary>
public static class ClipboardService
{
    public static void SetImage(BitmapSource image)
    {
        System.Windows.Clipboard.SetImage(image);
    }

    public static void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }

    /// <summary>编码为 PNG 字节流（供测试与临时文件保存）。</summary>
    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}