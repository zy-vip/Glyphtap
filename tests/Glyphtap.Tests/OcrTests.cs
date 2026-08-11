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

    private sealed class FakeRecognizer : ITextRecognizer
    {
        private readonly Func<BitmapSource, CancellationToken, Task<IReadOnlyList<TextLine>>> _impl;
        public FakeRecognizer(Func<BitmapSource, CancellationToken, Task<IReadOnlyList<TextLine>>> impl) => _impl = impl;
        public Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct) => _impl(image, ct);
    }

    [Fact]
    public async Task Composite_首个成功_不再尝试后续()
    {
        var calls = 0;
        var fake = new FakeRecognizer(async (_, _) =>
        {
            calls++;
            return await Task.FromResult<IReadOnlyList<TextLine>>(new List<TextLine> { new("甲", new Rect(0, 0, 10, 10)) });
        });
        var chain = new CompositeTextRecognizer(new ITextRecognizer[]
        {
            fake,
            new FakeRecognizer((_, _) => throw new Exception("不应被调用")),
        });

        var lines = await chain.RecognizeAsync(null!, CancellationToken.None);
        Assert.Single(lines);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Composite_首个抛异常_回退到下一个()
    {
        var chain = new CompositeTextRecognizer(new ITextRecognizer[]
        {
            new FakeRecognizer((_, _) => throw new NotSupportedException("本地引擎不可用")),
            new FakeRecognizer((_, _) => Task.FromResult<IReadOnlyList<TextLine>>(new List<TextLine> { new("乙", new Rect(0, 0, 5, 5)) })),
        });

        var lines = await chain.RecognizeAsync(null!, CancellationToken.None);
        var text = Assert.Single(lines);
        Assert.Equal("乙", text.Text);
    }

    [Fact]
    public async Task Composite_返回空列表_视为成功不继续链()
    {
        var calls = 0;
        var chain = new CompositeTextRecognizer(new ITextRecognizer[]
        {
            new FakeRecognizer(async (_, _) => { calls++; return await Task.FromResult<IReadOnlyList<TextLine>>(new List<TextLine>()); }),
            new FakeRecognizer((_, _) => throw new Exception("不应被调用")),
        });

        var lines = await chain.RecognizeAsync(null!, CancellationToken.None);
        Assert.Empty(lines);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Composite_全部失败_抛出链中异常()
    {
        var chain = new CompositeTextRecognizer(new ITextRecognizer[]
        {
            new FakeRecognizer((_, _) => throw new NotSupportedException("引擎A")),
            new FakeRecognizer((_, _) => throw new InvalidOperationException("引擎B")),
        });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => chain.RecognizeAsync(null!, CancellationToken.None));
        Assert.Equal("引擎B", ex.Message);
    }

    [Fact]
    public async Task Composite_空链_抛InvalidOperationException()
    {
        var chain = new CompositeTextRecognizer(Array.Empty<ITextRecognizer>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => chain.RecognizeAsync(null!, CancellationToken.None));
    }
}