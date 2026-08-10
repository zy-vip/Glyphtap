# Glyphtap V2 — 高亮与马赛克标注 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增两种标注工具：高亮（半透明色块，用户选择的色板色）与马赛克（矩形区域像素块化，块大小固定 8px），支持绘制、选中删除、随选区整体拖动，并正确合成进最终截图。

**Architecture:** 与既有工具相同的扩展路径：`AnnotationKind` 增加 `Highlight`/`Mosaic` → `AnnotationModel.cs` 新增两个标注类（实现 `Clone()`，依赖撤销/重做计划）→ `AnnotationTools.cs` 新增两个 `ToolBase` 子类并扩展工厂 → `AnnotationManager.HitTest` 加两个分支 → 渲染两路：合成路径 `CaptureComposer` 与预览路径 `CaptureWindow.AnnotationElement`。马赛克的本质是「对背景位图的矩形区域做像素块化」，因此新建纯逻辑静态类 `MosaicPixelator`（裁剪 → 缩到块粒度 → NearestNeighbor 放大回原尺寸），合成与预览共用同一实现，保证所见即所得。高亮在 `AnnotationRenderer.Draw` 中作为半透明填充矩形渲染。

**Tech Stack:** .NET 8 / WPF / xUnit（渲染相关测试用 `[StaFact]`，纯几何用 `[Fact]`）

## Global Constraints

- 目标框架：`net8.0-windows`，`UseWPF=true`，沿用现有 csproj，不改动
- 坐标约定：标注坐标相对选区（物理像素）；马赛克处理时换算为虚拟屏幕绝对物理像素再操作背景图
- 马赛克块大小：固定 `BlockSize = 8`（物理像素），不随粗细/色板变化；高亮使用当前色板颜色，绘制时 alpha 固定为 90（不透明度 ≈ 35%），粗细属性不参与渲染（与箭头/画笔不同，高亮与马赛克无描边）
- 所有代码注释使用中文；测试方法名使用中文；界面文案使用简体中文
- 每个任务结束必须 `dotnet build` 通过 + 相应测试通过 + git 提交
- 提交信息风格：`feat:` / `test:` / `fix:` 前缀 + 中文概要
- 禁止引入规格之外的第三方依赖
- 规格文档：`docs/superpowers/specs/2026-08-07-glyphtap-design.md`；本计划依赖「撤销/重做」计划（`docs/superpowers/plans/2026-08-10-glyphtap-v2-undo-redo.md`）已加入的 `Annotation.Clone()` 抽象——**先执行撤销/重做计划再执行本计划**
- OCR 另见 `docs/superpowers/plans/2026-08-10-glyphtap-v2-ocr.md`（本计划引入的 `_backgroundSource` 字段被 OCR 计划复用）

---

### Task 1: 高亮与马赛克的模型、工具与命中测试（纯逻辑）

**Files:**
- Modify: `src/Glyphtap/Capture/AnnotationModel.cs`（`AnnotationKind` 加两项；新增两个标注类 + `Clone()`）
- Modify: `src/Glyphtap/Capture/AnnotationTools.cs`（`HighlightTool`/`MosaicTool` 两个类；工厂扩展两个分支）
- Modify: `src/Glyphtap/Capture/AnnotationManager.cs`（`HitTest` 加两个分支）
- Create: `tests/Glyphtap.Tests/HighlightMosaicTests.cs`

**Interfaces:**
- Consumes: 撤销/重做计划产出的 `Annotation.Clone()` 抽象；既有 `ToolBase`、`AnnotationToolFactory`、`AnnotationManager.HitTest`
- Produces:
  - `public sealed class HighlightAnnotation : Annotation`：公开字段 `public Rect Rect;`，`Bounds`/`Offset`/`Resize` 行为与 `RectangleAnnotation` 一致
  - `public sealed class MosaicAnnotation : Annotation`：公开字段 `public Rect Rect;` 与 `public double BlockSize = 8;`，其余同上
  - `AnnotationKind` 枚举顺序：`Rectangle, Ellipse, Arrow, Pen, Highlight, Mosaic`（顺序决定数字键 1~6 映射，见 Task 3）
  - `HighlightTool` / `MosaicTool`：均为矩形拖拽协议，尺寸小于 1px 时 `End()` 返回 null（与 `RectangleTool` 一致）

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/HighlightMosaicTests.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class HighlightMosaicTests
{
    [Fact]
    public void 高亮工具_拖拽产出归一化高亮标注()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Highlight, Colors.Yellow, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(50, 60));
        var a = tool.End();
        var h = Assert.IsType<HighlightAnnotation>(a);
        Assert.Equal(new Rect(50, 60, 50, 40), h.Rect);
        Assert.Equal(Colors.Yellow, h.Color);
    }

    [Fact]
    public void 高亮工具_尺寸过小返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Highlight, Colors.Yellow, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(100, 100));
        Assert.Null(tool.End());
    }

    [Fact]
    public void 马赛克工具_拖拽产出马赛克标注_块大小默认8()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Mosaic, Colors.Red, 3);
        tool.Begin(new Point(0, 0));
        tool.Move(new Point(100, 80));
        var m = Assert.IsType<MosaicAnnotation>(tool.End());
        Assert.Equal(new Rect(0, 0, 100, 80), m.Rect);
        Assert.Equal(8, m.BlockSize);
    }

    [Fact]
    public void HitTest_高亮按矩形内部命中()
    {
        var h = new HighlightAnnotation { Rect = new Rect(0, 0, 50, 50), Color = Colors.Yellow };
        Assert.True(AnnotationManager.HitTest(h, new Point(25, 25), 5));
        Assert.False(AnnotationManager.HitTest(h, new Point(100, 100), 5));
    }

    [Fact]
    public void HitTest_马赛克按矩形内部命中()
    {
        var m = new MosaicAnnotation { Rect = new Rect(10, 10, 40, 40) };
        Assert.True(AnnotationManager.HitTest(m, new Point(20, 20), 5));
        Assert.False(AnnotationManager.HitTest(m, new Point(100, 100), 5));
    }

    [Fact]
    public void Clone_高亮与马赛克拷贝字段()
    {
        var h = new HighlightAnnotation { Rect = new Rect(1, 2, 3, 4), Color = Colors.Green };
        var hc = (HighlightAnnotation)h.Clone();
        Assert.Equal(new Rect(1, 2, 3, 4), hc.Rect);
        Assert.Equal(Colors.Green, hc.Color);

        var m = new MosaicAnnotation { Rect = new Rect(0, 0, 10, 20), Color = Colors.Blue, Thickness = 5 };
        var mc = (MosaicAnnotation)m.Clone();
        Assert.Equal(new Rect(0, 0, 10, 20), mc.Rect);
        Assert.Equal(8, mc.BlockSize);
        Assert.Equal(Colors.Blue, mc.Color);
        Assert.Equal(5, mc.Thickness);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: FAIL（编译失败：`HighlightAnnotation` / `MosaicAnnotation` / `AnnotationKind.Highlight` 不存在；`Clone` 若撤销计划未执行也会编译失败）

- [ ] **Step 3: 扩展模型**

`src/Glyphtap/Capture/AnnotationModel.cs`：

枚举改为：

```csharp
public enum AnnotationKind { Rectangle, Ellipse, Arrow, Pen, Highlight, Mosaic }
```

文件末尾追加两个类：

```csharp
/// <summary>高亮标注：半透明色块（无描边，粗细不参与渲染）。</summary>
public sealed class HighlightAnnotation : Annotation
{
    public Rect Rect;
    public HighlightAnnotation() { Kind = AnnotationKind.Highlight; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new HighlightAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
}

/// <summary>马赛克标注：矩形区域像素块化（无描边，粗细/颜色不参与渲染）。</summary>
public sealed class MosaicAnnotation : Annotation
{
    public Rect Rect;

    /// <summary>马赛克块大小（物理像素）。</summary>
    public double BlockSize = 8;

    public MosaicAnnotation() { Kind = AnnotationKind.Mosaic; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new MosaicAnnotation { Rect = Rect, BlockSize = BlockSize, Color = Color, Thickness = Thickness };
}
```

- [ ] **Step 4: 扩展工具工厂与工具类**

`src/Glyphtap/Capture/AnnotationTools.cs`：

工厂 switch 增加两个分支（现有 `_ => throw` 保持）：

```csharp
AnnotationKind.Highlight => new HighlightTool(color, thickness),
AnnotationKind.Mosaic => new MosaicTool(color, thickness),
```

文件末尾追加两个工具类：

```csharp
internal sealed class HighlightTool : ToolBase
{
    public HighlightTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Highlight;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new HighlightAnnotation { Rect = r, Color = Color, Thickness = Thickness };
    }
}

internal sealed class MosaicTool : ToolBase
{
    public MosaicTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Mosaic;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new MosaicAnnotation { Rect = r, BlockSize = 8 };
    }
}
```

- [ ] **Step 5: 扩展命中测试**

`src/Glyphtap/Capture/AnnotationManager.cs` 的 `HitTest` switch，在 `case EllipseAnnotation e:` 分支之后追加（矩形类命中逻辑与 `RectangleAnnotation` 相同）：

```csharp
case HighlightAnnotation h:
    return h.Rect.Contains(p) || DistanceToRectEdges(p, h.Rect) <= tolerance;
case MosaicAnnotation m:
    return m.Rect.Contains(p) || DistanceToRectEdges(p, m.Rect) <= tolerance;
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（既有 48 个 + 新增 6 个）

- [ ] **Step 7: 提交**

```bash
git add src/Glyphtap/Capture/AnnotationModel.cs src/Glyphtap/Capture/AnnotationTools.cs src/Glyphtap/Capture/AnnotationManager.cs tests/Glyphtap.Tests/HighlightMosaicTests.cs
git commit -m "feat: 高亮与马赛克标注的模型/工具/命中测试"
```

---

### Task 2: 马赛克像素化与合成（MosaicPixelator + CaptureComposer）

**Files:**
- Create: `src/Glyphtap/Capture/MosaicPixelator.cs`
- Modify: `src/Glyphtap/Capture/CaptureComposer.cs`（马赛克标注特殊合成）
- Test: `tests/Glyphtap.Tests/ComposerAndClipboardTests.cs`（追加马赛克/高亮合成测试）

**Interfaces:**
- Consumes: `MosaicAnnotation`（Task 1）、既有 `CaptureComposer.Compose(BitmapSource fullScreen, Rect selectionPhysical, IReadOnlyList<Annotation> annotations)`
- Produces:
  - `public static class MosaicPixelator`：
    - `public static BitmapSource Pixelate(BitmapSource source, Rect physicalRect, double blockSize)`——把 `source` 上 `physicalRect`（虚拟屏幕绝对物理像素，必须在源图边界内）区域的像素块化，输出与 `physicalRect` 同尺寸（width×height，ceil 到整数）的位图；STA 线程调用
    - 算法：`CroppedBitmap` 裁剪 → `RenderTargetBitmap` 缩放到约 (w/block × h/block) → 同样手法以 `NearestNeighbor` 放大回原尺寸（硬像素块，无模糊）
- `CaptureComposer.Compose` 新行为：`MosaicAnnotation` 不再进入 `AnnotationRenderer.Draw`，而是在背景绘制完成后立即覆盖（把马赛克区域换算为虚拟屏幕绝对物理像素，与源图边界求交后调用 `MosaicPixelator.Pixelate`，再以相对选区坐标 `DrawImage` 回画）

- [ ] **Step 1: 结构设计说明（确保马赛克区域不越界）**

马赛克区域 = `selectionPhysical.X + m.Rect.X, selectionPhysical.Y + m.Rect.Y`（宽高 = `m.Rect.Width/Height`）。该区域可能超出虚拟屏幕（选区贴着屏幕边缘时标注可部分越界）。`CroppedBitmap` 要求裁剪矩形完全落在源图内，因此须先用 `Rect.Intersect` 与源图边界求交：

```csharp
var clip = Rect.Intersect(absRect, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
```

交集为空时跳过该马赛克（屏幕外无内容可块化）。交集非空时按交集像素化，回画位置同样用交集（相对选区 = `clip.X - selectionPhysical.X`）。

- [ ] **Step 2: 写失败测试**

`tests/Glyphtap.Tests/ComposerAndClipboardTests.cs` 追加：

```csharp
[StaFact]
public void Compose_马赛克标注_选区像素被块化()
{
    // 背景：左半红右半蓝（4x4，每 2 列一色）
    var full = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
    var bytes = new byte[4 * 4 * 4];
    for (var y = 0; y < 4; y++)
    {
        for (var x = 0; x < 4; x++)
        {
            var c = x < 2 ? Colors.Red : Colors.Blue;
            var i = (y * 4 + x) * 4;
            bytes[i] = c.B; bytes[i + 1] = c.G; bytes[i + 2] = c.R; bytes[i + 3] = 255;
        }
    }
    full.WritePixels(new Int32Rect(0, 0, 4, 4), bytes, 4 * 4, 0);

    // 马赛克覆盖中间 2x2 区域：块大小 2 → 块化后整块取像素平均，红蓝混合成紫
    var mosaic = new MosaicAnnotation { Rect = new Rect(1, 1, 2, 2), BlockSize = 2 };
    var result = CaptureComposer.Compose(full, new Rect(0, 0, 4, 4), new Annotation[] { mosaic });
    var outPx = new byte[4 * 4 * 4];
    result.CopyPixels(outPx, 4 * 4, 0);

    // 马赛克块中心 (2,2) 应为红蓝混合（128, 0, 128 附近）
    var idx = (2 * 4 + 2) * 4;
    Assert.True(outPx[idx + 2] > 64 && outPx[idx + 2] < 192, $"R={outPx[idx + 2]}");
    Assert.Equal(0, outPx[idx + 1]);
    Assert.True(outPx[idx] > 64 && outPx[idx] < 192, $"B={outPx[idx]}");
    // 块外 (0,0) 保持纯红
    var outer = 0;
    Assert.Equal(255, outPx[outer + 2]);
    Assert.Equal(0, outPx[outer]);
}

[StaFact]
public void Compose_高亮标注_半透明色块叠在背景上()
{
    var full = Solid(Colors.White, 50, 50);
    var highlight = new HighlightAnnotation
    {
        Rect = new Rect(10, 10, 30, 30),
        Color = Color.FromArgb(255, 0, 120, 255), // 蓝色系
    };
    var result = CaptureComposer.Compose(full, new Rect(0, 0, 50, 50), new Annotation[] { highlight });
    var pixels = new byte[50 * 50 * 4];
    result.CopyPixels(pixels, 50 * 4, 0);
    // 高亮中心 (25,25)：白色背景混 35% 蓝色 → 蓝通道显著上升、红通道下降
    var idx = (25 * 50 + 25) * 4;
    Assert.True(pixels[idx + 2] < 255, $"R={pixels[idx + 2]}");   // 红被蓝压暗
    Assert.True(pixels[idx] > pixels[idx + 2], $"B={pixels[idx]}"); // 蓝高于红
    Assert.True(pixels[idx] > 64, $"B={pixels[idx]}");
}
```

> 说明：高亮渲染 alpha 固定 90/255 ≈ 35%（渲染实现见 Step 3 的 `AnnotationRenderer` 分支），因此断言「蓝通道 > 红通道且红被压暗」验证叠加效果；精确混合值与反走样（边缘）无关，取区域中心像素避免边缘。

- [ ] **Step 3: 实现 MosaicPixelator**

`src/Glyphtap/Capture/MosaicPixelator.cs`（新建）：

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glyphtap.Capture;

/// <summary>
/// 马赛克像素化：把源位图指定矩形区域做成像素块（须由 STA 线程调用）。
/// 算法：裁剪 → 缩到块粒度（BlocksPer 尺寸）→ NearestNeighbor 放大回原尺寸，产生硬边像素块。
/// </summary>
public static class MosaicPixelator
{
    public static BitmapSource Pixelate(BitmapSource source, Rect physicalRect, double blockSize)
    {
        var w = (int)Math.Ceiling(physicalRect.Width);
        var h = (int)Math.Ceiling(physicalRect.Height);
        var blocksW = Math.Max(1, (int)Math.Ceiling(w / blockSize));
        var blocksH = Math.Max(1, (int)Math.Ceiling(h / blockSize));

        var cropped = new CroppedBitmap(source, new Int32Rect(
            (int)physicalRect.X, (int)physicalRect.Y, w, h));
        var small = RenderScaled(cropped, blocksW, blocksH, BitmapScalingMode.Linear);
        return RenderScaled(small, w, h, BitmapScalingMode.NearestNeighbor);
    }

    /// <summary>把 src 渲染到目标尺寸的位图（VisualBrush + RenderTargetBitmap，插值模式可指定）。</summary>
    private static BitmapSource RenderScaled(BitmapSource src, int w, int h, BitmapScalingMode mode)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(src) { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(brush, mode);
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        return rtb;
    }
}
```

- [ ] **Step 4: 修改 CaptureComposer**

`src/Glyphtap/Capture/CaptureComposer.cs`，把标注循环改为（当前第 33-35 行）：

```csharp
// 标注（相对选区坐标），超界部分被 PushClip 裁掉；马赛克需先覆盖背景再画其他标注
foreach (var a in annotations)
{
    if (a is MosaicAnnotation m)
    {
        DrawMosaic(dc, fullScreen, selectionPhysical, m);
        continue;
    }
    AnnotationRenderer.Draw(dc, a);
}
```

类内追加私有方法：

```csharp
/// <summary>把马赛克区域块化后覆盖到背景上（区域换算为虚拟屏幕绝对物理像素）。</summary>
private static void DrawMosaic(DrawingContext dc, BitmapSource fullScreen, Rect selectionPhysical, MosaicAnnotation m)
{
    var abs = new Rect(
        selectionPhysical.X + m.Rect.X,
        selectionPhysical.Y + m.Rect.Y,
        m.Rect.Width,
        m.Rect.Height);
    // 与源图边界求交：防止越界区域导致 CroppedBitmap 抛异常
    var clip = Rect.Intersect(abs, new Rect(0, 0, fullScreen.PixelWidth, fullScreen.PixelHeight));
    if (clip.IsEmpty)
        return;
    var blocky = MosaicPixelator.Pixelate(fullScreen, clip, m.BlockSize);
    dc.DrawImage(blocky, new Rect(
        clip.X - selectionPhysical.X,
        clip.Y - selectionPhysical.Y,
        clip.Width,
        clip.Height));
}
```

- [ ] **Step 5: 高亮渲染分支（AnnotationRenderer）**

`src/Glyphtap/Capture/AnnotationRenderer.cs` 的 `Draw` switch，在 `case RectangleAnnotation r:` 之前追加：

```csharp
case HighlightAnnotation h:
    // 高亮：固定 35% 不透明度色块，无描边
    dc.DrawRectangle(
        new SolidColorBrush(Color.FromArgb(90, h.Color.R, h.Color.G, h.Color.B)),
        null,
        h.Rect);
    break;
```

- [ ] **Step 6: 运行测试确认通过**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（新增 2 个 StaFact 测试）

- [ ] **Step 7: 提交**

```bash
git add src/Glyphtap/Capture/MosaicPixelator.cs src/Glyphtap/Capture/CaptureComposer.cs src/Glyphtap/Capture/AnnotationRenderer.cs tests/Glyphtap.Tests/ComposerAndClipboardTests.cs
git commit -m "feat: 马赛克像素化渲染与高亮合成（MosaicPixelator/Composer/Renderer）"
```

---

### Task 3: 截图窗口 UI（工具栏按钮 + 预览渲染 + 数字键 5/6）

**Files:**
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml`（工具栏加「高亮」「马赛克」按钮）
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml.cs`（数字键范围扩到 1~6；`AnnotationElement` 增加高亮/马赛克分支；缓存背景位图源字段）

**Interfaces:**
- Consumes: Task 1/2 产出的全部内容；既有 `CaptureWindow.AnnotationElement`、`ToWindowDipsRelative`、`_scale`
- Produces:
  - `CaptureWindow` 字段：`private readonly BitmapSource _backgroundSource;`（构造时保存 `BitmapConvert.ToBitmapSource(capture.Bitmap)`，`BackgroundImage.Source` 仍赋同一实例；供马赛克预览与 OCR 计划复用）
  - XAML：`BtnHighlight`（Content="高亮" Tag="Highlight" Click="Tool_OnClick"）、`BtnMosaic`（Content="马赛克" Tag="Mosaic" Click="Tool_OnClick"），置于「画笔」按钮之后、色板分隔线之前
  - 数字键：`Key.D1`~`Key.D6` 映射 `AnnotationKind.Rectangle`~`Mosaic`（枚举顺序已在 Task 1 固定）

- [ ] **Step 1: XAML 加按钮**

`src/Glyphtap/Capture/CaptureWindow.xaml`，在 `<Button x:Name="BtnPen" .../>` 之后插入：

```xml
<Button x:Name="BtnHighlight" Content="高亮" Tag="Highlight" Click="Tool_OnClick" Margin="2,0" />
<Button x:Name="BtnMosaic" Content="马赛克" Tag="Mosaic" Click="Tool_OnClick" Margin="2,0" />
```

- [ ] **Step 2: 缓存背景位图源**

`src/Glyphtap/Capture/CaptureWindow.xaml.cs`，构造中把现有两行：

```csharp
BackgroundImage.Source = BitmapConvert.ToBitmapSource(capture.Bitmap);
```

改为：

```csharp
// 缓存背景源：马赛克预览与 OCR 识别都要基于它裁剪/像素化
_backgroundSource = BitmapConvert.ToBitmapSource(capture.Bitmap);
BackgroundImage.Source = _backgroundSource;
```

并在字段区（`_scale` 附近）新增：

```csharp
private readonly BitmapSource _backgroundSource;
```

- [ ] **Step 3: 数字键扩到 6**

`OnPreviewKeyDown`（Task A2 后该函数顶部已有 Ctrl 分支，保留）：

```csharp
else if (e.Key >= Key.D1 && e.Key <= Key.D6)
    SwitchTool((AnnotationKind)((int)AnnotationKind.Rectangle + (e.Key - Key.D1)));
```

> 数字键与枚举顺序对齐：1=矩形 2=椭圆 3=箭头 4=画笔 5=高亮 6=马赛克。

- [ ] **Step 4: AnnotationElement 增加两个分支**

`src/Glyphtap/Capture/CaptureWindow.xaml.cs` 的 `AnnotationElement` switch，在 `case EllipseAnnotation e:` 分支后追加（需新建文件顶部 `using System.Windows.Media.Imaging;`，若已有则跳过）：

```csharp
case HighlightAnnotation h:
{
    var shape = new System.Windows.Shapes.Rectangle
    {
        Fill = new SolidColorBrush(Color.FromArgb(90, a.Color.R, a.Color.G, a.Color.B)),
        Width = h.Rect.Width / scale,
        Height = h.Rect.Height / scale,
    };
    var d = ToWindowDipsRelative(h.Rect.Location);
    Canvas.SetLeft(shape, d.X);
    Canvas.SetTop(shape, d.Y);
    return shape;
}
case MosaicAnnotation m:
{
    // 把选区相对矩形换算为虚拟屏幕绝对物理像素，与背景源边界求交后像素化，回贴预览
    var s = _selection.Selection;
    var abs = new Rect(s.X + m.Rect.X, s.Y + m.Rect.Y, m.Rect.Width, m.Rect.Height);
    var clip = Rect.Intersect(abs, new Rect(0, 0, _backgroundSource.PixelWidth, _backgroundSource.PixelHeight));
    var img = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };
    if (!clip.IsEmpty)
    {
        img.Source = MosaicPixelator.Pixelate(_backgroundSource, clip, m.BlockSize);
        var origin = ToWindowDipsRelative(new Point(clip.X - s.X, clip.Y - s.Y));
        Canvas.SetLeft(img, origin.X);
        Canvas.SetTop(img, origin.Y);
        img.Width = clip.Width / _scale;
        img.Height = clip.Height / _scale;
    }
    img.Visibility = clip.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    return img;
}
```

> 说明：`Image` 控件来自 `System.Windows.Controls`（文件已有该 using）。马赛克预览直接复用 `MosaicPixelator`，与合成输出一致（所见即所得）；像素化成本与选区尺寸相关，块化中间位图很小（约 w/8 × h/8），仅在标注集合/预览变化时重建。

- [ ] **Step 5: 构建并全量跑测试**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（56 个）

- [ ] **Step 6: 手动验证清单（运行 `dotnet run --project src/Glyphtap`）**

1. 按 5 或点「高亮」→ 拖拽 → 半透明色块悬于背景上；换色板颜色后高亮跟随
2. 按 6 或点「马赛克」→ 拖拽 → 实时预览出现硬边像素块；松开后块保持
3. 高亮/马赛克可点击选中、Delete 删除、按住拖动微调（随选区整体移动保持相对位置）
4. 马赛克区域贴着屏幕边缘画（部分越界）→ 无异常，可见部分正常块化
5. Enter 合成后粘贴：马赛克/高亮与预览一致、无模糊无跑偏
6. 撤销（Ctrl+Z）/重做（Ctrl+Y）对高亮与马赛克生效
7. 1~6 快捷键全部可切换，工具栏当前工具高亮跟随

- [ ] **Step 7: 提交**

```bash
git add src/Glyphtap/Capture/CaptureWindow.xaml src/Glyphtap/Capture/CaptureWindow.xaml.cs
git commit -m "feat: 高亮与马赛克工具栏按钮与预览渲染"
```