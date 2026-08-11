using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using WpfBitmapFrame = System.Windows.Media.Imaging.BitmapFrame;
using WinBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace Glyphtap.OCR;

/// <summary>
/// 基于 Windows.Media.Ocr 离线引擎的识别器。
/// 引擎不可用（老系统/无语言包）抛 NotSupportedException；超过引擎尺寸限制的图像先等比缩小，
/// 识别后把坐标按缩放因子放大回原图尺寸。
/// </summary>
public sealed class WindowsOcrRecognizer : ITextRecognizer
{
    public async Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null)
            throw new NotSupportedException("系统不支持 OCR（需 Windows 10 1607 及以上，且系统语言包含可识别语言）");

        // 超过引擎限制时预缩放：工作图坐标 × 还原系数 = 原图坐标
        var (workImage, restoreFactor) = EnsureWithinLimit(image, OcrEngine.MaxImageDimension);
        using var softwareBitmap = await ToSoftwareBitmapAsync(workImage, ct);
        var result = await engine.RecognizeAsync(softwareBitmap).AsTask(ct);

        var lines = new List<TextLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            // OcrLine 无整行矩形，用行内所有词的 BoundingRect 合并出行级矩形
            var lineRect = Rect.Empty;
            foreach (var word in line.Words)
            {
                var wr = word.BoundingRect; // Windows.Foundation.Rect（float 字段）
                var rect = new Rect(wr.X, wr.Y, wr.Width, wr.Height);
                lineRect = lineRect.IsEmpty ? rect : Rect.Union(lineRect, rect);
            }
            if (lineRect.IsEmpty)
                continue; // 无词的行没有几何信息，跳过

            // 行矩形以工作图（可能已缩小）为坐标系，乘回还原系数得到原图坐标
            lines.Add(new TextLine(
                line.Text,
                new Rect(
                    lineRect.X * restoreFactor,
                    lineRect.Y * restoreFactor,
                    lineRect.Width * restoreFactor,
                    lineRect.Height * restoreFactor)));
        }
        return lines;
    }

    /// <summary>
    /// 超限时等比缩放到 MaxImageDimension 内；返回 (待识别图, 还原系数)。
    /// 约定：工作图坐标 × 还原系数 = 原图坐标（未缩放时还原系数 = 1.0）。
    /// </summary>
    private static (BitmapSource Image, double RestoreFactor) EnsureWithinLimit(BitmapSource image, uint maxDim)
    {
        var max = Math.Max(image.PixelWidth, image.PixelHeight);
        if (max <= maxDim)
            return (image, 1.0);

        var scale = maxDim / (double)max; // 缩小因子（<1）
        var tb = new TransformedBitmap();
        tb.BeginInit();
        tb.Source = image;
        tb.Transform = new ScaleTransform(scale, scale);
        tb.EndInit();
        tb.Freeze();
        return (tb, 1.0 / scale); // 还原系数 = 原图尺寸 / 工作图尺寸（>1）
    }

    /// <summary>BitmapSource → SoftwareBitmap（走 PNG 编码内存流 + BitmapDecoder）。</summary>
    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(BitmapSource image, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(WpfBitmapFrame.Create(image));
        encoder.Save(ms);
        ms.Position = 0;

        var randomAccess = ms.AsRandomAccessStream();
        var decoder = await WinBitmapDecoder.CreateAsync(randomAccess).AsTask(ct);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct);
        return softwareBitmap;
    }
}