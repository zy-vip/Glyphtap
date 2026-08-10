# Glyphtap V2 — 撤销/重做 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为标注层实现撤销/重做：标注的添加、删除、清除、移动、整体平移均可撤销与重做，快捷键 Ctrl+Z / Ctrl+Y（及 Ctrl+Shift+Z），并提供工具栏按钮。

**Architecture:** 在纯逻辑 `AnnotationManager` 内实现快照式历史栈：每次修改动作前自动推送「当前标注列表深拷贝」到撤销栈，新动作清空重做栈。`Annotation` 基类新增 `Clone()` 抽象方法，各子类实现深拷贝（画笔需复制点列表）。选区本身的创建/移动/缩放不做撤销（超出 MVP 预期，只覆盖标注层）。UI 侧：鼠标拖拽标注移动是连续调用 `MoveSelectedBy`，由 UI 在 MouseMove 首次实际移动时（布尔守卫：纯点击选中不产生撤销点）调用一次 `PushUndoPoint()` 记录起始状态。

**Tech Stack:** .NET 8 / WPF / xUnit（TDD 沿用仓库现有约定）

## Global Constraints

- 目标框架：`net8.0-windows`，`UseWPF=true`，`Nullable=enable`，`ImplicitUsings=enable`（沿用现有 csproj，不改动）
- 坐标约定：`AnnotationManager` 与 `Annotation` 全部运行在**物理像素**空间，坐标相对选区（现有约定，不引入新单位）
- 所有代码注释使用中文；测试方法名使用中文；界面文案使用简体中文
- 每个任务结束必须 `dotnet build` 通过 + 相应测试通过 + git 提交
- 提交信息风格：`feat:` / `test:` / `fix:` 前缀 + 中文概要（与仓库现有历史一致，如 `feat: 撤销/重做（标注层快照历史栈）`）
- 禁止引入规格之外的第三方依赖
- 规格文档：`docs/superpowers/specs/2026-08-07-glyphtap-design.md`（实现以该文档为准）；本文档为 V2 子计划「撤销/重做」，不覆盖高亮/马赛克（另见 `2026-08-10-glyphtap-v2-highlight-mosaic.md`）与 OCR（另见 `2026-08-10-glyphtap-v2-ocr.md`）
- 快照上限：撤销栈最多保留 100 个快照（超出丢弃最旧），防止长时间会话内存膨胀

---

### Task 1: 撤销/重做核心（Clone + AnnotationManager 历史栈）

**Files:**
- Modify: `src/Glyphtap/Capture/AnnotationModel.cs`（`Annotation` 加 `Clone()` 抽象，4 个子类实现）
- Modify: `src/Glyphtap/Capture/AnnotationManager.cs`（快照栈 + `PushUndoPoint` / `Undo` / `Redo` / `CanUndo` / `CanRedo`；`Add` / `DeleteSelected` / `Clear` / `MoveAllBy` 自动记录）
- Create: `tests/Glyphtap.Tests/UndoRedoTests.cs`

**Interfaces:**
- Consumes: 现有 `Annotation` / `RectangleAnnotation` / `EllipseAnnotation` / `ArrowAnnotation` / `PenAnnotation` / `AnnotationManager`（`AnnotationModel.cs`、`AnnotationManager.cs` 现状见仓库）
- Produces:
  - `public abstract Annotation Clone();`（Annotation 基类新增抽象方法）
  - `AnnotationManager` 新增：
    - `public bool CanUndo { get; }` / `public bool CanRedo { get; }`
    - `public void PushUndoPoint()`（显式推送撤销点：深拷贝当前列表入撤栈、清空重做栈；供 UI 手势开始处调用）
    - `public void Undo()` / `public void Redo()`（无历史时为空操作；撤销/重做后 `Selected` 置 null）
    - 自动记录：`Add` / `DeleteSelected` / `Clear` / `MoveAllBy` 在修改列表前自动调用 `PushUndoPoint()`（无修改时跳过：`DeleteSelected` 无选中、`Clear` 空列表、`MoveAllBy` 空列表时不推点）
  - `MoveSelectedBy` **不**自动记录（连续调用），由 UI 在 MouseMove 首次实际移动时调用一次 `PushUndoPoint()`（见 Task 2）

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/UndoRedoTests.cs`：

```csharp
using System;
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class UndoRedoTests
{
    private static RectangleAnnotation Rect(double x, double y, double w, double h) =>
        new() { Rect = new Rect(x, y, w, h), Color = Colors.Red, Thickness = 3 };

    [Fact]
    public void Add_两次_Undo逐步回退_Redo逐步前进()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(20, 20, 10, 10));
        Assert.Equal(2, mgr.Items.Count);

        mgr.Undo();
        Assert.Single(mgr.Items);
        Assert.Equal(20, ((RectangleAnnotation)mgr.Items[0]).Rect.X);

        mgr.Undo();
        Assert.Empty(mgr.Items);

        mgr.Redo();
        Assert.Single(mgr.Items);

        mgr.Redo();
        Assert.Equal(2, mgr.Items.Count);
    }

    [Fact]
    public void DeleteSelected_可撤销恢复()
    {
        var mgr = new AnnotationManager();
        var a = Rect(10, 10, 40, 40);
        mgr.Add(a);
        mgr.TrySelectAt(new Point(30, 30), 5);
        mgr.DeleteSelected();
        Assert.Empty(mgr.Items);

        mgr.Undo();
        Assert.Single(mgr.Items);
        Assert.Equal(new Rect(10, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void Clear_可撤销恢复全部()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(50, 50, 10, 10));
        mgr.Clear();
        Assert.Empty(mgr.Items);

        mgr.Undo();
        Assert.Equal(2, mgr.Items.Count);
    }

    [Fact]
    public void PushUndoPoint_后移动_可撤销位置恢复()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(10, 10, 40, 40));
        mgr.TrySelectAt(new Point(30, 30), 5);

        mgr.PushUndoPoint(); // UI 拖拽手势开始时调用一次
        mgr.MoveSelectedBy(new Vector(5, -5));
        mgr.MoveSelectedBy(new Vector(5, 0));
        Assert.Equal(new Rect(20, 5, 40, 40), ((RectangleAnnotation)mgr.Selected!).Rect);

        mgr.Undo();
        Assert.Equal(new Rect(10, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void MoveAllBy_自动记录_可撤销()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.MoveAllBy(new Vector(-10, 20));
        Assert.Equal(new Rect(-10, 20, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);

        mgr.Undo();
        Assert.Equal(new Rect(0, 0, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void 新动作_清空重做栈()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Undo();
        Assert.True(mgr.CanRedo);

        mgr.Add(Rect(50, 50, 10, 10)); // 新动作后重做不可用
        Assert.False(mgr.CanRedo);
        Assert.True(mgr.CanUndo);
    }

    [Fact]
    public void 无历史时_UndoRedo为空操作()
    {
        var mgr = new AnnotationManager();
        mgr.Undo();
        mgr.Redo();
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
    }

    [Fact]
    public void Undo后_清空选中()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.TrySelectAt(new Point(5, 5), 5);
        Assert.NotNull(mgr.Selected);

        mgr.Undo();
        Assert.Null(mgr.Selected);
    }

    [Fact]
    public void Clone_画笔深拷贝_互不影响()
    {
        var pen = new PenAnnotation { Color = Colors.Red, Thickness = 3 };
        pen.AddPoint(new Point(0, 0));
        pen.AddPoint(new Point(0, 100));
        var copy = (PenAnnotation)pen.Clone();

        copy.AddPoint(new Point(100, 100));
        Assert.Equal(2, pen.Points.Count);
        Assert.Equal(3, copy.Points.Count);
        Assert.Equal(Colors.Red, copy.Color);
        Assert.Equal(3, copy.Thickness);
    }

    [Fact]
    public void Clone_各类型字段一致()
    {
        var arrow = new ArrowAnnotation { Start = new Point(1, 2), End = new Point(9, 8), Color = Colors.Green, Thickness = 2 };
        var a = (ArrowAnnotation)arrow.Clone();
        Assert.Equal(new Point(1, 2), a.Start);
        Assert.Equal(new Point(9, 8), a.End);
        Assert.Equal(Colors.Green, a.Color);
        Assert.Equal(2, a.Thickness);
        Assert.Equal(AnnotationKind.Arrow, a.Kind);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: FAIL（编译错误：`Clone` 未定义）

- [ ] **Step 3: 实现 Clone**

在 `src/Glyphtap/Capture/AnnotationModel.cs` 的 `Annotation` 基类中新增抽象方法：

```csharp
/// <summary>深拷贝（撤销快照用；画笔需复制点列表，其余复制值字段）。</summary>
public abstract Annotation Clone();
```

各子类实现（追加到对应类体中）——`RectangleAnnotation`：

```csharp
public override Annotation Clone() =>
    new RectangleAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
```

`EllipseAnnotation`：

```csharp
public override Annotation Clone() =>
    new EllipseAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
```

`ArrowAnnotation`：

```csharp
public override Annotation Clone() =>
    new ArrowAnnotation { Start = Start, End = End, Color = Color, Thickness = Thickness };
```

`PenAnnotation`（深度复制点列表）：

```csharp
public override Annotation Clone()
{
    var copy = new PenAnnotation { Color = Color, Thickness = Thickness };
    copy.Points.AddRange(Points);
    return copy;
}
```

- [ ] **Step 4: 实现 AnnotationManager 历史栈**

修改 `src/Glyphtap/Capture/AnnotationManager.cs`：

字段与属性、快照辅助（追加到类体）：

```csharp
private const int MaxUndoDepth = 100;
private readonly Stack<List<Annotation>> _undoStack = new();
private readonly Stack<List<Annotation>> _redoStack = new();

/// <summary>是否有可撤销的历史。</summary>
public bool CanUndo => _undoStack.Count > 0;

/// <summary>是否有可重做的历史。</summary>
public bool CanRedo => _redoStack.Count > 0;

/// <summary>当前列表的深拷贝快照。</summary>
private List<Annotation> Snapshot() => _items.Select(a => a.Clone()).ToList();

/// <summary>
/// 推送撤销点：记录当前状态，清空重做栈。
/// 修改动作（Add/DeleteSelected/Clear/MoveAllBy）自动调用；MoveSelectedBy 由 UI 在手势开始时调用一次。
/// </summary>
public void PushUndoPoint()
{
    _undoStack.Push(Snapshot());
    if (_undoStack.Count > MaxUndoDepth)
    {
        // 丢弃最旧快照（栈底；Stack 只提供 Pop 栈顶，故重建列表后移除首元素）
        var all = _undoStack.ToList();
        all.RemoveAt(0);
        _undoStack.Clear();
        foreach (var s in all)
            _undoStack.Push(s);
    }
    _redoStack.Clear();
}
```

> 说明：`Stack.Pop()` 移除的是栈顶（最新），因此丢弃最旧（栈底）快照需重建列表。快照本身是持久化列表（深拷贝），重建无风险。

撤销与重做：

```csharp
/// <summary>撤销：当前状态入重做栈，还原到上一次快照。</summary>
public void Undo()
{
    if (!CanUndo)
        return;
    _redoStack.Push(Snapshot());
    ReplaceItems(_undoStack.Pop());
}

/// <summary>重做：撤销的逆操作。</summary>
public void Redo()
{
    if (!CanRedo)
        return;
    _undoStack.Push(Snapshot());
    ReplaceItems(_redoStack.Pop());
}

/// <summary>用快照替换当前条目，选中清空（历史快照不保留选中）。</summary>
private void ReplaceItems(List<Annotation> snapshot)
{
    _items.Clear();
    _items.AddRange(snapshot);
    _selected = null;
}
```

修改既有动作，在修改前自动记录：

```csharp
/// <summary>新增标注（自动记录撤销点）。</summary>
public void Add(Annotation a)
{
    PushUndoPoint();
    _items.Add(a);
}

/// <summary>删除选中（无选中时不记录）。</summary>
public void DeleteSelected()
{
    if (_selected == null)
        return;
    PushUndoPoint();
    _items.Remove(_selected);
    _selected = null;
}

/// <summary>清空全部（空时不记录）。</summary>
public void Clear()
{
    if (_items.Count == 0)
        return;
    PushUndoPoint();
    _items.Clear();
    _selected = null;
}

/// <summary>选区整体移动时所有标注随动（自动记录撤销点）。</summary>
public void MoveAllBy(Vector delta)
{
    if (_items.Count == 0)
        return;
    PushUndoPoint();
    foreach (var a in _items)
        a.Offset(delta);
}
```

> `MoveSelectedBy` 保持原样（不自动记录）：UI 拖拽期间每帧调用，若自动记录会产生大量无意义快照；由 UI 在 MouseDown 命中标注时调用一次 `PushUndoPoint()`。

- [ ] **Step 5: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（既有 38 个 + 新增 10 个）

- [ ] **Step 6: 提交**

```bash
git add src/Glyphtap/Capture/AnnotationModel.cs src/Glyphtap/Capture/AnnotationManager.cs tests/Glyphtap.Tests/UndoRedoTests.cs
git commit -m "feat: 标注撤销/重做（快照历史栈 + Clone 深拷贝）"
```

---

### Task 2: 撤销/重做 UI（工具栏按钮 + 快捷键 + 拖拽手势记录点）

**Files:**
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml`（工具栏加撤销/重做按钮，置于「清除」按钮左侧）
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml.cs`（Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z、拖拽标注首次实际移动时 PushUndoPoint、按钮事件、撤销后刷新按钮状态）

**Interfaces:**
- Consumes: Task 1 产出的 `AnnotationManager.CanUndo` / `CanRedo` / `Undo` / `Redo` / `PushUndoPoint`
- Produces: `CaptureWindow` 内新增
  - XAML：`BtnUndo`（Content="↶"）、`BtnRedo`（Content="↷"），Click 分别为 `Undo_OnClick` / `Redo_OnClick`
  - 代码：`private void Undo_OnClick(object sender, RoutedEventArgs e)`、`private void Redo_OnClick(object sender, RoutedEventArgs e)`、`private void UpdateUndoButtons()`（刷新两按钮 IsEnabled）

- [ ] **Step 1: 修改工具栏 XAML**

`src/Glyphtap/Capture/CaptureWindow.xaml`，在 `<Button x:Name="BtnClear" .../>` 之前插入：

```xml
<Separator Width="1" Background="Gray" Margin="6,2" />
<Button x:Name="BtnUndo" Content="↶" Click="Undo_OnClick" Margin="2,0" IsEnabled="False" ToolTip="撤销 (Ctrl+Z)" />
<Button x:Name="BtnRedo" Content="↷" Click="Redo_OnClick" Margin="2,0" IsEnabled="False" ToolTip="重做 (Ctrl+Y)" />
```

- [ ] **Step 2: 修改快捷键处理**

`src/Glyphtap/Capture/CaptureWindow.xaml.cs` 的 `OnPreviewKeyDown`（现第 113 行起），在 `PreviewKeyDown` 开头加入撤销/重做分支（在 Enter/Esc 分支之前）：

```csharp
private void OnPreviewKeyDown(object sender, KeyEventArgs e)
{
    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
    {
        if (e.Key == Key.Z)
            UndoAnnotations();
        else if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
            RedoAnnotations();
        return;
    }
    // ... 原有 Enter/Esc/数字键/Delete 逻辑保持不变
}
```

> 注：`Key.Z` + Shift 的组合在第一个 `if` 内处理（Ctrl+Shift+Z = 重做）。

- [ ] **Step 3: 实现按钮事件与状态刷新**

在 `CaptureWindow.xaml.cs` 的工具栏区域（`Tool_OnClick` 附近）追加：

```csharp
private void Undo_OnClick(object sender, RoutedEventArgs e) => UndoAnnotations();

private void Redo_OnClick(object sender, RoutedEventArgs e) => RedoAnnotations();

/// <summary>执行撤销并刷新标注与按钮状态。</summary>
private void UndoAnnotations()
{
    _annotations.Undo();
    RenderAnnotations();
    UpdateUndoButtons();
}

/// <summary>执行重做并刷新标注与按钮状态。</summary>
private void RedoAnnotations()
{
    _annotations.Redo();
    RenderAnnotations();
    UpdateUndoButtons();
}

/// <summary>按历史栈状态刷新撤销/重做按钮可用性。</summary>
private void UpdateUndoButtons()
{
    BtnUndo.IsEnabled = _annotations.CanUndo;
    BtnRedo.IsEnabled = _annotations.CanRedo;
}
```

- [ ] **Step 4: 拖拽标注实际移动时记录撤销点**

修改 `CaptureWindow.xaml.cs`：`MoveSelectedBy` 不自动记录，纯点击选中（无移动）不应产生撤销点，改为 MouseMove 首次实际移动时用布尔守卫记录一次。

字段区追加：

```csharp
private bool _dragUndoPointRecorded;
```

`RootGrid_MouseDown`（现第 183-191 行，命中标注分支）不再直接 push，仅重置守卫：

```csharp
if (_annotations.TrySelectAt(ToRelative(p), 6))
{
    _dragUndoPointRecorded = false; // 点击选中：移动发生时（MouseMove）才记录撤销点
    _draggingAnnotation = true;
    _dragLast = p;
    RootGrid.CaptureMouse();
    return;
}
```

`RootGrid_MouseMove`（现第 224-230 行，拖拽标注分支）在首次移动记录一次：

```csharp
if (_draggingAnnotation)
{
    if (!_dragUndoPointRecorded)
    {
        _annotations.PushUndoPoint(); // 每次拖拽手势只记录一次撤销点
        _dragUndoPointRecorded = true;
    }
    _annotations.MoveSelectedBy(new Vector(p.X - _dragLast.X, p.Y - _dragLast.Y));
    _dragLast = p;
    RenderAnnotations();
    return;
}
```

- [ ] **Step 5: 操作后刷新按钮状态**

在 `RenderAnnotations()` 调用处保持一致：在 `RootGrid_MouseUp` 的标注提交分支（`_annotations.Add(a)` 后）与 `_annotations.DeleteSelected()` / `Clear_OnClick` 之后追加 `UpdateUndoButtons();`。

涉及的 3 处：
1. `OnPreviewKeyDown` 的 Delete 分支（`_annotations.DeleteSelected(); RenderAnnotations();` 后）
2. `RootGrid_MouseUp` 绘制提交分支（`_annotations.Add(a); RenderAnnotations();` 后）
3. `Clear_OnClick`（`_annotations.Clear(); RenderAnnotations();` 后）

修改 `RenderAnnotations` 直接在末尾调用一次 `UpdateUndoButtons()` 更简单：

```csharp
private void RenderAnnotations()
{
    AnnotationCanvas.Children.Clear();
    foreach (var a in _annotations.Items)
        AnnotationCanvas.Children.Add(AnnotationElement(a));

    // 实时预览：当前工具的进行中标注
    var preview = _tool.GetPreview();
    if (preview != null)
        AnnotationCanvas.Children.Add(AnnotationElement(preview));

    UpdateUndoButtons(); // 渲染同时刷新撤销/重做按钮状态
}
```

> 说明：撤销/重做按钮为纯 UI 行为，无单元测试可写（托盘/工具栏 UI 只能手动验证，与仓库现有约定一致）。Task 2 的验收 = `dotnet build` 通过 + 手动验证清单（见 Step 7）。

- [ ] **Step 6: 构建并全量跑测试**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error 0 warning

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj`
Expected: 全部通过（48 个）

- [ ] **Step 7: 手动验证清单（运行 `dotnet run --project src/Glyphtap`）**

1. 画一个矩形标注 → Ctrl+Z 消失 → Ctrl+Y 恢复
2. 画椭圆 → 追加 Delete 删除 → Ctrl+Z 恢复 → 工具栏「↶」按钮同样可撤销
3. 拖拽移动标注 → Ctrl+Z 回到原位（只回退一步，不是逐帧）
4. 画 3 个标注 → 清除 → Ctrl+Z 全部恢复
5. 撤销两步后画新标注 → Ctrl+Y 不可用（重做栈被清空）
6. 无历史时两个按钮灰置

- [ ] **Step 8: 提交**

```bash
git add src/Glyphtap/Capture/CaptureWindow.xaml src/Glyphtap/Capture/CaptureWindow.xaml.cs
git commit -m "feat: 撤销/重做快捷键与工具栏按钮"
```