using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Glyphtap.OCR;

/// <summary>
/// 识别器链式组合：按注入顺序逐个尝试，抛异常则换下一个（云端实现加入链尾即可），
/// 返回空列表视为识别成功。全部失败抛出链中最后一个异常。
/// </summary>
public sealed class CompositeTextRecognizer : ITextRecognizer
{
    private readonly IReadOnlyList<ITextRecognizer> _chain;

    public CompositeTextRecognizer(IEnumerable<ITextRecognizer> chain) =>
        _chain = chain.ToList();

    public async Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct)
    {
        if (_chain.Count == 0)
            throw new InvalidOperationException("没有可用的识别器");

        Exception? last = null;
        foreach (var recognizer in _chain)
        {
            try
            {
                return await recognizer.RecognizeAsync(image, ct);
            }
            catch (Exception ex)
            {
                last = ex; // 记录后尝试下一个识别器
            }
        }
        throw last!;
    }
}
