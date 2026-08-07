using System.Windows;
using System.Windows.Media.Imaging;

namespace Glyphtap.OCR;

/// <summary>识别结果行。</summary>
public sealed record TextLine(string Text, Rect BoundsDips);

/// <summary>OCR 识别器接口（V2 接入本地/云端实现；本版仅定义，不实现）。</summary>
public interface ITextRecognizer
{
    Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct);
}