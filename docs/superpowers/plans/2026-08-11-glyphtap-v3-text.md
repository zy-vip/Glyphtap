# 文本标注实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在截图窗口新增「文本」标注工具（工具栏按钮 + 数字键 7），选区内点按内联输入，Enter 提交为可选中/移动/Delete/撤销的 TextAnnotation。

**Architecture:** `TextAnnotation : Annotation` 复用现有模型/管理器/撤销链；点按语义不进入拖拽式 IAnnotationTool 工厂，由 CaptureWindow 直接管理内联 TextBox；渲染走 `AnnotationRenderer`（合成）与 `AnnotationElement`（预览）双路径。

**Tech Stack:** C# / .NET 8 / WPF（FormattedText 测量）、xUnit（StaFact 包）

## Global Constraints

- 注释、字符串、测试方法名、提交信息全用中文；提交用 conventional 风格（`feat:`/`fix:`）
- 坐标单位：标注坐标为相对选区的物理像素；字号随粗细档映射 细12/中16/粗20（物理像素）
- 涉及 WPF 字体测量/渲染的测试必须用 `[StaFact]`，纯几何用 `[Fact]`
- 项目内无 lint/格式任务；每次 Task 结束跑 `dotnet test tests/Glyphtap.Tests`（须全绿）
- D1~D6 已被矩形/椭圆/箭头/画笔/高亮/马赛克占用，文本用 D7

---

### Task 1: TextAnnotation 模型 + TextMetrics + 渲染 + 命中测试

**Files:**
- Modify: `src/Glyphtap/Capture/AnnotationModel.cs`（枚举 + TextAnnotation 类）
- Modify: `src/Glyphtap/Capture/AnnotationTools.cs`（TextMetrics 静态类）
- Modify: `src/Glyphtap/Capture/AnnotationRenderer.cs`（Draw 加 Text 分支）
- Modify: `src/Glyphtap/Capture/AnnotationManager.cs`（HitTest 加 Text 分支）
- Test: `tests/Glyphtap.Tests/TextAnnotationTests.cs`（新建）

**Interfaces:**
- Consumes: 现有 `Annotation` 抽象（`Kind/Color/Thickness/Bounds/Offset/Resize/Clone`）
- Produces: `TextAnnotation`（`Text/Position/TextSize`）、`TextMetrics.FontSizeForThickness(double)→double`、`TextMetrics.Measure(string,double)→Size`、Text 分支渲染与命中

- [ ] **Step 1: 写失败测试**

```csharp
// tests/Glyphtap.Tests/TextAnnotationTests.cs
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class TextAnnotationTests
{
    [Fact]
    public void 字号映射_细中粗对应12_16_20()
    {
        Assert.Equal(12, TextMetrics.FontSizeForThickness(1));
        Assert.Equal(16, TextMetrics.FontSizeForThickness(3));
        Assert.Equal(20, TextMetrics.FontSizeForThickness(5));
    }

    [StaFact]
    public void 文本测量_宽度随文字增长_空文本为零宽()
    {
        Assert.True(TextMetrics.Measure("WW", 16).Width > TextMetrics.Measure("W", 16).Width);
        Assert.Equal(0, TextMetrics.Measure("", 16).Width);
    }

    [StaFact]
    public void 文本测量_字号越大宽度越大()
    {
        Assert.True(TextMetrics.Measure("文本", 20).Width > TextMetrics.Measure("文本", 12).Width);
    }

    [Fact]
    public void 文本标注_静态属性与文本框尺寸()
    {
        var t = new TextAnnotation { Text = "你好", Position = new Point(10, 20), TextSize = new Size(50, 20), Color = Colors.Red, Thickness = 3 };
        Assert.Equal(AnnotationKind.Text, t.Kind);
        Assert.Equal(new Rect(10, 20, 50, 20), t.Bounds);
    }

    [Fact]
    public void 文本标注_Offset平移_Resize无操作()
    {
        var t = new TextAnnotation { Text = "x", Position = new Point(1, 2), TextSize = new Size(10, 10) };
        t.Offset(new Vector(5, 7));
        Assert.Equal(new Point(6, 9), t.Position);
        t.Resize(new Rect(0, 0, 99, 99));
        Assert.Equal("x", t.Text);
    }

    [Fact]
    public void 文本标注_Clone深拷贝_改原文本不影响克隆()
    {
        var t = new TextAnnotation { Text = "原", Position = new Point(1, 2), TextSize = new Size(10, 10) };
        var c = (TextAnnotation)t.Clone();
        c.Text = "改";
        Assert.Equal("原", t.Text);
        Assert.Equal(new Point(1, 2), c.Position);
        Assert.Equal(new Size(10, 10), c.TextSize);
    }

    [Fact]
    public void 文本命中_文本框内命中_框外不命中()
    {
        var t = new TextAnnotation { Text = "x", Position = new Point(0, 0), TextSize = new Size(50, 20) };
        Assert.True(AnnotationManager.HitTest(t, new Point(10, 10), 5));
        Assert.False(AnnotationManager.HitTest(t, new Point(100, 100), 5));
    }

    [Fact]
    public void 管理器_添加文本标注后可撤销()
    {
        var m = new AnnotationManager();
        m.Add(new TextAnnotation { Text = "a", Position = new Point(0, 0), TextSize = new Size(10, 10) });
        m.Add(new TextAnnotation { Text = "b", Position = new Point(0, 0), TextSize = new Size(10, 10) });
        Assert.Equal(2, m.Items.Count);
        m.Undo();
        Assert.Single(m.Items);
        m.Redo();
        Assert.Equal(2, m.Items.Count);
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests --filter "FullyQualifiedName~TextAnnotationTests"`（现有测试须保持 68/68 全绿；新测试因缺类型编译失败属预期）

- [ ] **Step 3: 实现模型与测量**

`AnnotationModel.cs`：枚举后追加 `Text` 并新增类：

```csharp
public enum AnnotationKind { Rectangle, Ellipse, Arrow, Pen, Highlight, Mosaic, Text }

/// <summary>文本标注：TextSize 在提交时由 TextMetrics 测量一次并缓存（避免重复 STA 测量）。坐标相对选区物理像素。</summary>
public sealed class TextAnnotation : Annotation
{
    public string Text = "";
    public Point Position;
    public Size TextSize;

    public TextAnnotation() { Kind = AnnotationKind.Text; }

    public override Rect Bounds => new(Position, TextSize);
    public override void Offset(Vector delta) => Position += delta;
    public override void Resize(Rect newBounds) { /* 文本不缩放（同箭头/画笔惯例） */ }
    public override Annotation Clone() =>
        new TextAnnotation { Text = Text, Position = Position, TextSize = TextSize, Color = Color, Thickness = Thickness };
}
```

`AnnotationTools.cs` 追加：

```csharp
/// <summary>文本测量与字号映射。测量单位物理像素（pixelsPerDip=1.0）。</summary>
public static class TextMetrics
{
    public const string FontFamilyName = "Microsoft YaHei";

    /// <summary>粗细档 → 字号（物理像素）：细12 / 中16 / 粗20。</summary>
    public static double FontSizeForThickness(double thickness) => thickness switch
    {
        <= 1.5 => 12,
        <= 4 => 16,
        _ => 20,
    };

    /// <summary>按物理像素字号测量文本宽度高度。需 STA。</summary>
    public static Size Measure(string text, double fontSizePx)
    {
        if (text.Length == 0)
            return Size.Empty;
        var ft = new FormattedText(text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamilyName),
            fontSizePx,
            Brushes.Black,
            pixelsPerDip: 1.0);
        return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }
}

/// <summary>文本工具占位：点按语义不进工厂，令其无绘制行为。</summary>
internal sealed class NoOpTool : IAnnotationTool
{
    public static readonly NoOpTool Instance = new();
    public AnnotationKind Kind => AnnotationKind.Text;
    public bool IsDrawing => false;
    public void Begin(Point p) { }
    public void Move(Point p) { }
    public Annotation? GetPreview() => null;
    public Annotation? End() => null;
}
```

`AnnotationRenderer.cs` 的 `Draw` switch 追加（放在 PenAnnotation 分支后）：

```csharp
case TextAnnotation t:
    dc.DrawText(new FormattedText(t.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(TextMetrics.FontFamilyName),
            TextMetrics.FontSizeForThickness(t.Thickness),
            new SolidColorBrush(t.Color),
            pixelsPerDip: 1.0), t.Position);
    break;
```

`AnnotationManager.cs` 的 `HitTest` switch 追加：

```csharp
case TextAnnotation t:
{
    var r = new Rect(t.Position, t.TextSize);
    return r.Contains(p) || DistanceToRectEdges(p, r) <= tolerance;
}
```

（`using System.Globalization;` 与 `using System.Windows.Media;` 已存在于各自文件，确认顶部 using 完整。）

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests`，预期：68 存量 + 新增全部通过（含 `[StaFact]` 测量用例）

- [ ] **Step 5: 提交**

```bash
git add tests/Glyphtap.Tests/TextAnnotationTests.cs src/Glyphtap/Capture/AnnotationModel.cs src/Glyphtap/Capture/AnnotationTools.cs src/Glyphtap/Capture/AnnotationRenderer.cs src/Glyphtap/Capture/AnnotationManager.cs
git commit -m "feat: 文本标注模型/测量/渲染/命中（TextAnnotation + TextMetrics）"
```

---

### Task 2: 工具栏按钮 + 数字键 7 + 内联输入交互

**Files:**
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml`（文本按钮）
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml.cs`（SwitchTool 文本分支、内联 TextBox 编辑态、数字键 D7）

**Interfaces:**
- Consumes: Task 1 的 `TextAnnotation`/`TextMetrics`/`NoOpTool`
- Produces: 无新公开接口（纯 UI 交互）

- [ ] **Step 1: 先改 XAML 加按钮**

`CaptureWindow.xaml` 在 `BtnMosaic` 按钮后追加：

```xml
<Button x:Name="BtnText" Content="文本" Tag="Text" Click="Tool_OnClick" Margin="2,0" />
```

（`Tool_OnClick` 现有实现已按 Tag 解析枚举，无需改动。）

- [ ] **Step 2: 改快捷键范围 D1~D7**

`CaptureWindow.xaml.cs` 的 `OnPreviewKeyDown` 中：

```csharp
// 原: else if (e.Key >= Key.D1 && e.Key <= Key.D6)
else if (e.Key >= Key.D1 && e.Key <= Key.D7)
    SwitchTool((AnnotationKind)((int)AnnotationKind.Rectangle + (e.Key - Key.D1)));
```

- [ ] **Step 3: SwitchTool/颜色/粗细处理器支持 Text（绕过工厂）**

```csharp
private void SwitchTool(AnnotationKind kind)
{
    _currentKind = kind;
    _tool = kind == AnnotationKind.Text
        ? NoOpTool.Instance
        : AnnotationToolFactory.Create(kind, _color, _thickness);
}

private void Color_OnClick(object sender, RoutedEventArgs e)
{
    _color = (Color)ColorConverter.ConvertFromString(((FrameworkElement)sender).Tag!.ToString()!)!;
    SwitchTool(_currentKind); // 原为直接调工厂，Text 时保持 NoOp
}

private void Thickness_OnClick(object sender, RoutedEventArgs e)
{
    _thickness = double.Parse(((FrameworkElement)sender).Tag!.ToString()!);
    SwitchTool(_currentKind);
}
```

- [ ] **Step 4: 内联输入编辑态字段与方法**

字段区追加：

```csharp
private bool _textEditing;
private TextBox? _editBox;
private Point _editPosRel; // 文本起点，选区相对物理像素
```

`RootGrid_MouseDown` 中「点在选区内且未命中标注」分支前加入文本路径：

```csharp
if (_selection.HasSelection)
{
    if (_textEditing)
        return; // 编辑中：点击空白交给失焦提交，不启动新手势
    if (_annotations.TrySelectAt(ToRelative(p), 6))
    {
        // ... 现有逻辑不变
    }
    var handle = SelectionLogic.HitTestHandle(p, _selection.Selection);
    if (handle == ResizeHandle.None && _selection.Selection.Contains(p) && _currentKind == AnnotationKind.Text)
    {
        BeginTextEdit(ToRelative(p));
        return;
    }
    if (handle == ResizeHandle.None && _selection.Selection.Contains(p))
    {
        _tool.Begin(ToRelative(p));
        RootGrid.CaptureMouse();
        return;
    }
}
```

新增方法（放在「工具栏」区前）：

```csharp
// ---- 文本标注：内联输入 ----

private void BeginTextEdit(Point rel)
{
    _textEditing = true;
    _editPosRel = rel;
    var d = ToWindowDipsRelative(rel);
    _editBox = new TextBox
    {
        Width = 240,
        FontFamily = new FontFamily(TextMetrics.FontFamilyName),
        FontSize = TextMetrics.FontSizeForThickness(_thickness) / _scale,
        Foreground = new SolidColorBrush(_color),
        Background = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
        AcceptsReturn = false,
    };
    Canvas.SetLeft(_editBox, d.X);
    Canvas.SetTop(_editBox, d.Y);
    // AnnotationCanvas 命中关闭（IsHitTestVisible=False），TextBox 须挂到 OverlayCanvas
    OverlayCanvas.Children.Add(_editBox);
    _editBox.Focus();
}

/// <summary>提交文本：Enter/失焦。空文本不创建标注。</summary>
private void CommitTextEdit()
{
    if (!_textEditing)
        return;
    _textEditing = false;
    var text = _editBox!.Text.Trim();
    OverlayCanvas.Children.Remove(_editBox);
    _editBox = null;
    if (text.Length == 0)
        return;
    var fs = TextMetrics.FontSizeForThickness(_thickness);
    var size = TextMetrics.Measure(text, fs);
    var a = new TextAnnotation { Text = text, Position = _editPosRel, TextSize = size, Color = _color, Thickness = _thickness };
    _annotations.Add(a); // Add 自动记录撤销点
    RenderAnnotations();
    Focus(); // 恢复窗口焦点，保证 Enter 继续走完成截图等快捷键
}

private void CancelTextEdit()
{
    if (!_textEditing)
        return;
    _textEditing = false;
    OverlayCanvas.Children.Remove(_editBox);
    _editBox = null;
}
```

`RootGrid_MouseMove` 与 `RootGrid_MouseUp` 开头各加一行防干扰：

```csharp
if (_textEditing) return; // 编辑中不进入选区/标注手势
```

`OnPreviewKeyDown` 顶部（Ctrl 分支之前）拦截编辑态按键：

```csharp
if (_textEditing && e.OriginalSource is TextBox)
{
    if (e.Key == Key.Enter)
    {
        CommitTextEdit();
        e.Handled = true;
    }
    else if (e.Key == Key.Escape)
    {
        CancelTextEdit();
        e.Handled = true;
    }
    else if (e.Key == Key.Delete)
        e.Handled = true; // 输入框内删除不清标注
    return; // Ctrl+Z 等在 TextBox 内原生处理，不触发全局撤销
}
```

TextBox 失焦提交（构造函数 `SourceInitialized` 附近注册，或 BeginTextEdit 内注册）：

```csharp
// BeginTextEdit 末尾
_editBox.LostKeyboardFocus += (_, _) => CommitTextEdit();
```

注意：失焦提交与 Enter 提交共用路径，Enter 后立刻失焦会二次提交——`CommitTextEdit` 开头 `_textEditing` 判空已防重复。

`AnnotationElement` switch 末尾（PenAnnotation 后）追加预览分支：

```csharp
case TextAnnotation t:
{
    var tb = new TextBlock
    {
        Text = t.Text,
        FontFamily = new FontFamily(TextMetrics.FontFamilyName),
        FontSize = TextMetrics.FontSizeForThickness(t.Thickness) / scale,
        Foreground = new SolidColorBrush(a.Color),
        IsHitTestVisible = false,
    };
    var td = ToWindowDipsRelative(t.Position);
    Canvas.SetLeft(tb, td.X);
    Canvas.SetTop(tb, td.Y);
    return tb;
}
```

- [ ] **Step 5: 全量测试 + 构建**

Run: `dotnet build Glyphtap.sln`; `dotnet test tests/Glyphtap.Tests`（存量 68 + 新增测试全绿）

- [ ] **Step 6: 提交**

```bash
git add src/Glyphtap/Capture/CaptureWindow.xaml src/Glyphtap/Capture/CaptureWindow.xaml.cs
git commit -m "feat: 文本标注工具栏按钮与内联输入交互（数字键7）"
```

---

### Task 3: 手动验证清单

- [ ] **Step 1: 运行与验证**

Run: `dotnet run --project src/Glyphtap`

逐条验证（GUI 环境）：
1. F1 截图 → 工具栏出现「文本」按钮；数字键 7 也能切换（按钮高亮）
2. 选区内点按 → 浮出半透明输入框（当前色文字、字号随细/中/粗）
3. 输入中文/英文 → Enter 提交；位置/颜色/字号正确
4. 空文本 Enter → 不创建标注
5. Esc → 取消，无标注
6. 失焦（点击框外）→ 提交
7. 提交后：点击选中、拖动、Delete、Ctrl+Z 撤销、Ctrl+Y 重做 均正常
8. 合成（✓/Enter）：文字渲染与预览一致、清晰；超出选区部分被裁剪
9. 文本框内 Ctrl+Z 只回退输入内容，不撤销标注（回归：既有六工具不受影响）

- [ ] **Step 2: 回归确认**

确认既有工具（矩形/椭圆/箭头/画笔/高亮/马赛克）、撤销重做、OCR、多屏负偏移均不受影响；完成本计划后更新 VP 计划文档勾选。

> 待用户执行：GUI 环境人工过 9 条。其余步骤自动测试全覆盖。