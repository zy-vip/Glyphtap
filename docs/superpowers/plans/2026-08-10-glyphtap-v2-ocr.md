# Glyphtap V2 — OCR 文字识别 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **实施状态（2026-08-11）：** 已全部完成并合并至 master（提交 d4a8998 / 6e6bbb8 / aac838b / 1ab107b / ceb3a61，含多显示器负偏移坐标修复）；自动测试 68/68 通过。除带「待用户执行」注记的手动验证清单外，其余步骤均已勾选。

**Goal:** 在截图窗口内提供「识别」按钮：对当前选区做本地 OCR（Windows.Media.Ocr 离线引擎），结果浮窗展示识别文本行，支持一键复制全文到剪贴板；识别器接入链式组合（`CompositeTextRecognizer`），为未来云端识别器预留扩展点。

**Architecture:** 实现 `ITextRecognizer` 的 `WindowsOcrRecognizer`：`BitmapSource` → PNG 内存流 → `BitmapDecoder` → `SoftwareBitmap` → `OcrEngine.RecognizeAsync`；引擎不可用时抛 `NotSupportedException`（中文提示）。`CompositeTextRecognizer` 按注入的顺序逐个尝试，前面识别器抛异常则换下一个，全部失败抛出最后一个异常——V2 注册链为 `[WindowsOcrRecognizer]`，未来云端实现加入链尾即可。UI 在 `CaptureWindow`：工具栏「识别」按钮点击 → 用 `_backgroundSource`（高亮/马赛克计划已添加的字段）+ 选区物理像素裁剪 → 异步识别 → 结果浮窗显示；「复制文本」复用已有 `ClipboardService.SetText`。

**Tech Stack:** .NET 8 / WPF / Windows.Media.Ocr（WinRT，net8.0-windows 内置投影，无需第三方包）/ xUnit

## Global Constraints

- 目标框架：`net8.0-windows`，沿用现有 csproj；`Windows.Media.Ocr` 属于 WinRT API，net8.0-windows 默认包含 Windows SDK 投影。若构建报「类型或命名空间 Windows.Media.Ocr 不存在」，改用内联目标框架 `net8.0-windows10.0.19041.0`（SDK 会覆盖显式的 `<TargetPlatformVersion>` 属性，内联 TFM 是实际生效的方式）
- OCR 输入：选区原图（背景位图裁剪，**不含标注**）；`TextLine.BoundsDips` 以物理像素填充（截图位图无 DPI 元数据，1 物理像素 = 1 DIP）
- **OcrEngine 的 2600 像素限制**：`OcrEngine.MaxImageDimension` 为 2600，超过会被引擎内部缩放且结果坐标空间不确定——本实现**显式预缩放**：任一边超过 2600 时先等比缩到 2600 内，识别后把 `BoundsDips` 按缩放因子放大回原图坐标（不依赖 `engine.Scale` 的语义）
- 所有代码注释使用中文；测试方法名使用中文；界面文案使用简体中文
- 每个任务结束必须 `dotnet build` 通过 + 相应测试通过 + git 提交
- 提交信息风格：`feat:` / `test:` / `fix:` 前缀 + 中文概要
- 禁止引入规格之外的第三方依赖（OCR 不引入任何 NuGet 包）
- 规格文档：`docs/superpowers/specs/2026-08-07-glyphtap-design.md`；本计划依赖「高亮与马赛克」计划（`docs/superpowers/plans/2026-08-10-glyphtap-v2-highlight-mosaic.md`）引入的 `CaptureWindow._backgroundSource` 字段——**先执行撤销/重做、高亮/马赛克两个计划，再执行本计划**
- OCR 结果浮窗只影响截图窗口本身，不改变 Enter/Esc/完成/取消既有逻辑

---

### Task 1: WindowsOcrRecognizer（WinRT 管线 + 预缩放）

**Files:**
- Create: `src/Glyphtap/OCR/WindowsOcrRecognizer.cs`
- Create: `tests/Glyphtap.Tests/OcrTests.cs`

**Interfaces:**
- Consumes: `OCR/ITextRecognizer.cs` 既有 `ITextRecognizer` / `TextLine`（`TextLine(string Text, Rect BoundsDips)`）；既有 `Solid` 测试辅助（在 `ComposerAndClipboardTests`，本测试文件内自建一份）
- Produces:
  - `public sealed class WindowsOcrRecognizer : ITextRecognizer`
    - `public async Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct)`
    - 行为：引擎不可用（`TryCreateFromUserProfileLanguages` 返回 null）→ 抛 `NotSupportedException("系统不支持 OCR（需 Windows 10 1607 及以上，且系统语言包含可识别语言）")`；识别结果按行输出 `TextLine`
    - 内部：`BitmapSource → SoftwareBitmap` 转换（PNG 编码内存流 → `BitmapDecoder.CreateAsync` → `GetSoftwareBitmapAsync`）；超过 `MaxImageDimension` 时预缩放，识别后坐标乘以还原系数还原到原图尺寸

- [x] **Step 1: 写失败测试（含引擎可用性分支）**

`tests/Glyphtap.Tests/OcrTests.cs`：

```csharp
using System;
using System.Threading;
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
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj --filter WindowsOcr`
Expected: FAIL（编译失败：`WindowsOcrRecognizer` 不存在）

- [x] **Step 3: 实现 WindowsOcrRecognizer**

`src/Glyphtap/OCR/WindowsOcrRecognizer.cs`（新建）：

```csharp
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

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
            // OcrLine 无整行矩形，用行内所有词的 BoundingRect 合并出行级矩形（以工作图坐标，乘回还原系数得到原图坐标）
            var lineRect = Rect.Empty;
            foreach (var word in line.Words)
                lineRect = Rect.Union(lineRect, word.BoundingRect);
            var r = lineRect;
            lines.Add(new TextLine(
                line.Text,
                new Rect(
                    r.X * restoreFactor,
                    r.Y * restoreFactor,
                    r.Width * restoreFactor,
                    r.Height * restoreFactor)));
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
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(ms);
        ms.Position = 0;

        var randomAccess = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(randomAccess).AsTask(ct);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied).AsTask(ct);
        return softwareBitmap;
    }
}
```

> 说明：
> - `ms.AsRandomAccessStream()` 扩展方法在 `System.IO.WindowsRuntimeStreamExtensions`（`System.Runtime.InteropServices.WindowsRuntime` 命名空间），net8.0-windows 的 WinRT 互操作自带
> - `float`/`uint` 处理：`OcrEngine.MaxImageDimension` 是静态 `uint` 属性，`word.BoundingRect` 是 `Windows.Foundation.Rect`（`float` 字段，隐式转 double 参与运算即可）；`OcrLine` 本身无 `BoundingRect` 属性，行级矩形需用 `line.Words` 的 `BoundingRect` 做 `Rect.Union` 合并
> - `BitmapAlphaMode.Premultiplied` 匹配 WPF `Pbgra32` 的已知转换路径，避免颜色异常

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj --filter WindowsOcr`
Expected: PASS（1 个 StaFact）

- [x] **Step 5: 提交**

```bash
git add src/Glyphtap/OCR/WindowsOcrRecognizer.cs tests/Glyphtap.Tests/OcrTests.cs
git commit -m "feat: WindowsOcrRecognizer（WinRT 离线引擎 + 超限预缩放）"
```

---

### Task 2: CompositeTextRecognizer（识别器链式组合，云端预留）

**Files:**
- Create: `src/Glyphtap/OCR/CompositeTextRecognizer.cs`
- Modify: `tests/Glyphtap.Tests/OcrTests.cs`（追加组合逻辑测试）

**Interfaces:**
- Consumes: `ITextRecognizer` / `TextLine`；Task 1 的 `WindowsOcrRecognizer`
- Produces:
  - `public sealed class CompositeTextRecognizer : ITextRecognizer`
    - `public CompositeTextRecognizer(IEnumerable<ITextRecognizer> chain)`
    - `public async Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct)`
    - 行为：按顺序尝试链中识别器；某识别器抛异常则记录并尝试下一个；**识别返回空列表视为成功**（不再继续链）；全部失败抛最后一个异常；链为空时立即抛 `InvalidOperationException`
    - 语义：V2 注册 `new CompositeTextRecognizer(new ITextRecognizer[] { new WindowsOcrRecognizer() })`（UI 任务中给出）

- [x] **Step 1: 追加失败测试**

`tests/Glyphtap.Tests/OcrTests.cs` 追加：

```csharp
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
        return new List<TextLine> { new("甲", new Rect(0, 0, 10, 10)) };
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
        new FakeRecognizer(async (_, _) => { calls++; return new List<TextLine>(); }),
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
```

- [x] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj --filter Composite`
Expected: FAIL（编译失败：`CompositeTextRecognizer` 不存在）

- [x] **Step 3: 实现 CompositeTextRecognizer**

`src/Glyphtap/OCR/CompositeTextRecognizer.cs`（新建）：

```csharp
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
```

- [x] **Step 4: 运行测试确认通过**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（新增 6 个测试，其中 1 个 StaFact）

- [x] **Step 5: 提交**

```bash
git add src/Glyphtap/OCR/CompositeTextRecognizer.cs tests/Glyphtap.Tests/OcrTests.cs
git commit -m "feat: CompositeTextRecognizer 识别器链（云端预留扩展点）"
```

---

### Task 3: 截图窗口内 OCR 交互（识别按钮 + 结果浮窗 + 一键复制）

**Files:**
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml`（工具栏加「识别」按钮；RootGrid 加 OcrPanel 浮窗 Border）
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml.cs`（识别流程、复制、关闭）

**Interfaces:**
- Consumes: Task 1/2 产出的 `WindowsOcrRecognizer` / `CompositeTextRecognizer`；高亮/马赛克计划产出的 `_backgroundSource` 字段；既有 `ClipboardService.SetText`（`Services/ClipboardService.cs`）
- Produces:
  - XAML：`BtnOcr`（Content="识别" Click="Ocr_OnClick"，置于「清除」按钮左侧）；`OcrPanel`（Border：标题 + `OcrResultText` 多行文本 + 「复制文本」「关闭」按钮，默认 Collapsed，贴在工具栏上方）
  - 代码：`private readonly ITextRecognizer _recognizer;`（构造于字段初始化：`new CompositeTextRecognizer(new ITextRecognizer[] { new WindowsOcrRecognizer() })`）；`Ocr_OnClick`、`OcrCopy_OnClick`、`OcrClose_OnClick`
  - 行为：识别按钮点击 → 用 `_backgroundSource` + `_selection.Selection` 做 `CroppedBitmap`（物理像素）→ 识别 → `OcrResultText` 显示逐行文本（0 行显示「未识别到文字」）；识别期间按钮禁用防重入；异常显示「识别失败：<消息>」；复制 = `ClipboardService.SetText(全文)` 后隐藏浮窗；关闭仅隐藏浮窗
  - `CaptureWindow` 需新增 using：`using Glyphtap.OCR;`（`System.Windows.Media.Imaging` 已有）

- [x] **Step 1: XAML 加识别按钮与结果浮窗**

`src/Glyphtap/Capture/CaptureWindow.xaml`：

在 `<Button x:Name="BtnClear" Content="清除" .../>` 之前插入：

```xml
<Button x:Name="BtnOcr" Content="识别" Click="Ocr_OnClick" Margin="2,0" ToolTip="识别选区文字 (OCR)" />
```

在 `</Border>`（Toolbar 结束标签）之后、`</Grid>` 之前插入浮窗：

```xml
<Border x:Name="OcrPanel"
        Background="#F2FFFFFF"
        BorderBrush="#B0000000"
        BorderThickness="1"
        CornerRadius="6"
        Padding="10"
        MaxWidth="480"
        MaxHeight="260"
        HorizontalAlignment="Center"
        VerticalAlignment="Bottom"
        Margin="0,0,0,64"
        Visibility="Collapsed">
    <StackPanel>
        <TextBlock Text="识别结果" FontWeight="Bold" Margin="0,0,0,6" />
        <ScrollViewer MaxHeight="170" VerticalScrollBarVisibility="Auto">
            <TextBlock x:Name="OcrResultText" TextWrapping="Wrap" FontSize="14" />
        </ScrollViewer>
        <StackPanel Orientation="Horizontal" Margin="0,8,0,0" HorizontalAlignment="Right">
            <Button x:Name="BtnOcrCopy" Content="复制文本" Click="OcrCopy_OnClick" Margin="0,0,6,0" />
            <Button x:Name="BtnOcrClose" Content="关闭" Click="OcrClose_OnClick" />
        </StackPanel>
    </StackPanel>
</Border>
```

> 浮窗 `VerticalAlignment=Bottom` + `Margin=0,0,0,64` 使其悬浮在工具栏上方（工具栏底边距 16 + 工具栏高度约 32 + 余量）。`IsHitTestVisible` 继承 Border 默认值 true，浮窗上的按钮可点击。

- [x] **Step 2: 实现识别流程**

`src/Glyphtap/Capture/CaptureWindow.xaml.cs`：

文件顶部 using 区追加：

```csharp
using Glyphtap.OCR;
```

字段区追加：

```csharp
/// <summary>OCR 识别器链：本地优先，云端实现未来加入链尾。</summary>
private readonly ITextRecognizer _recognizer = new CompositeTextRecognizer(
    new ITextRecognizer[] { new WindowsOcrRecognizer() });

private bool _ocrRunning;
```

鼠标保护扩展：`RootGrid_MouseDown` 的工具栏点击保护（现第 174-175 行）只排除 Toolbar，浮窗按钮点击（「复制文本」「关闭」）会冒泡进来误触发选区/标注交互。把 `IsInToolbar` 改为泛化的浮层判断：

```csharp
// 替换原 IsInToolbar 方法（遍历 Toolbar 与 OcrPanel 两个浮层）
private bool IsInFloatingUi(object source)
{
    for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
    {
        if (d == Toolbar || d == OcrPanel)
            return true;
    }
    return false;
}
```

调用处同步修改（原第 174 行）：

```csharp
// 浮层（工具栏 / OCR 结果浮窗）按钮点击不进入截图交互
if (IsInFloatingUi(e.OriginalSource))
    return;
```

工具栏区域追加事件处理器：

```csharp
private async void Ocr_OnClick(object sender, RoutedEventArgs e)
{
    if (_ocrRunning || !_selection.HasSelection)
        return;
    _ocrRunning = true;
    BtnOcr.IsEnabled = false;
    try
    {
        var s = _selection.Selection;
        // 识别选区原图（不含标注）：背景源 + 选区物理像素裁剪
        var crop = new CroppedBitmap(_backgroundSource, new Int32Rect(
            (int)s.X, (int)s.Y, (int)s.Width, (int)s.Height));
        var lines = await _recognizer.RecognizeAsync(crop, CancellationToken.None);
        OcrResultText.Text = lines.Count == 0
            ? "未识别到文字"
            : string.Join("\n", lines.Select(l => l.Text));
        OcrPanel.Visibility = Visibility.Visible;
    }
    catch (Exception ex)
    {
        OcrResultText.Text = "识别失败：" + ex.Message;
        OcrPanel.Visibility = Visibility.Visible;
    }
    finally
    {
        _ocrRunning = false;
        BtnOcr.IsEnabled = true;
    }
}

private void OcrCopy_OnClick(object sender, RoutedEventArgs e)
{
    // 复制全文后收起浮窗（空结果时 OcrResultText 为提示文案，不复制）
    if (OcrResultText.Text is "未识别到文字" or null)
        return;
    ClipboardService.SetText(OcrResultText.Text);
    OcrPanel.Visibility = Visibility.Collapsed;
}

private void OcrClose_OnClick(object sender, RoutedEventArgs e)
{
    OcrPanel.Visibility = Visibility.Collapsed;
}
```

> 注：`ClipboardService` 来自 `Glyphtap.Services`，文件已有 `using Glyphtap.Services;`。`OcrCopy_OnClick` 对「识别失败：…」文案也允许复制（用户可能想复制错误信息），仅拦截「未识别到文字」与空文案。

- [x] **Step 3: 构建并全量跑测试**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（56 + 6 = 62 个）

- [ ] **Step 4: 手动验证清单（运行 `dotnet run --project src/Glyphtap`）**（待用户执行：实现完成时无 GUI 环境，需带显示环境人工过 7 条）

1. 屏幕上打开一段文字（浏览器/记事本均可）→ F1 → 框选文字 → 点「识别」→ 浮窗出现识别文本，与原文字基本一致
2. 点「复制文本」→ 粘贴到记事本 → 内容正确；浮窗自动关闭
3. 对纯色空白区域点「识别」→ 浮窗显示「未识别到文字」；点「复制文本」不复制（剪贴板不变）
4. 识别进行中再次点「识别」→ 按钮禁用，无重入
5. 浮窗打开时 Enter 完成截图 → 截图与 OCR 流程互不干扰（浮窗随窗口关闭）
6. 浮窗打开时「关闭」→ 浮窗隐藏，可再次点「识别」重新识别
7. 机器无 OCR 引擎时点「识别」→ 浮窗显示「识别失败：系统不支持 OCR（…）」，无异常崩溃

- [x] **Step 5: 提交**

```bash
git add src/Glyphtap/Capture/CaptureWindow.xaml src/Glyphtap/Capture/CaptureWindow.xaml.cs
git commit -m "feat: 截图窗口内 OCR 识别按钮与结果浮窗（一键复制）"
```