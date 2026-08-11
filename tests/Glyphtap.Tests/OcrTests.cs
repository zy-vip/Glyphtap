using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glyphtap.OCR;
using Xunit;

namespace Glyphtap.Tests;

public class OcrTests
{
    /// <summary>生成纯色 BitmapSource（与 ComposerAndClipboardTests.Solid 相同实现）。</summary>
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
    public async Task WindowsOcr_纯色图_管线不崩溃_结果可为空()
    {
        // 无引擎环境走 NotSupportedException 降级分支；有引擎环境识别纯色图通常返回 0 行
        var rec = new WindowsOcrRecognizer();
        try
        {
            var lines = await rec.RecognizeAsync(Solid(Colors.White, 64, 64), CancellationToken.None);
            Assert.NotNull(lines);
        }
        catch (NotSupportedException)
        {
            // 系统无 OCR 引擎：断言是合法的降级路径
        }
    }
}