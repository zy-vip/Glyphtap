# 贴图功能实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 全局热键 Ctrl+V（可重绑）读取系统剪贴板，贴出图像贴图或文本卡片；贴图窗口可拖动/滚轮缩放/边缘拉伸/Ctrl+滚轮透明度/双击销毁/右键菜单（复制、保存、置顶、销毁）；托盘提供手动贴出与热键设置入口。

**Architecture:** 新 `Pin/` 模块：`PinGeometry`（纯逻辑缩放/命中）、`PinParser`（纯逻辑内容分类）、`PinWindow`（无边框 Topmost 窗口，图像/文本分支渲染，LayoutTransform 缩放）、`PinManager`（剪贴板读取 + 贴图登记 + 鼠标所在屏定位）。`HotKeyService` 扩展多 id 注册；`AppSettings` 提供 JSON 热键配置；设置窗口捕获式热键输入。不触碰截图管线与 CaptureComposer。

**Tech Stack:** C# / .NET 8 / WPF、P/Invoke（RegisterHotKey、GetCursorPos）、System.Text.Json、xUnit

## Global Constraints

- 注释、字符串、测试方法名、提交信息全用中文；提交用 conventional 风格（`feat:`/`fix:`）
- 贴图窗口坐标为 WPF DIP；图像物理像素按 `PixelWidth / (DpiX/96)` 换算显示尺寸
- 缩放限 0.2~8x（步进 0.1），透明度限 0.2~1.0（步进 0.1），最小窗口 24 DIP
- 剪贴板图像优先于文本（有图像贴图像，仅纯文本贴文本）；都无 → 静默忽略
- 截图会话中 Ctrl+V 忽略（App 层查 `CaptureController.IsCapturing`）
- 热键字符串格式：修饰符 `C`(Ctrl)/`A`(Alt)/`S`(Shift) + `+` + 键（字母/F1~F24），如 `F1`、`C+V`
- 配置路径 `%APPDATA%\Glyphtap\config.json`；保存图像到 `%USERPROFILE%\Pictures\Glyphtap\`
- 禁止破坏现有 68 测试；纯逻辑 `[Fact]`，涉及剪贴板/窗口的仅手动验证

---

### Task 1: PinGeometry + PinParser 纯逻辑与测试

**Files:**
- Create: `src/Glyphtap/Pin/PinGeometry.cs`
- Create: `src/Glyphtap/Pin/PinParser.cs`
- Test: `tests/Glyphtap.Tests/PinGeometryTests.cs`（新建）

**Interfaces:**
- Consumes: `System.Windows.Rect/Point/Size`
- Produces: `enum PinResizeZone`、`PinGeometry.HitTestZone(Rect,Point)→PinResizeZone`、`PinGeometry.ResizeRect(Rect,PinResizeZone,Point)→Rect`、`PinGeometry.StepScale(double,int)→double`、`PinGeometry.StepOpacity(double,int)→double`、`enum PinSourceKind`、`PinParser.Classify(bool,bool)→PinSourceKind`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/Glyphtap.Tests/PinGeometryTests.cs
using System.Windows;
using Glyphtap.Pin;
using Xunit;

namespace Glyphtap.Tests;

public class PinGeometryTests
{
    private static readonly Rect R = new(100, 100, 200, 150); // 100,100 ~ 300,250

    [Theory]
    [InlineData(102, 102, PinResizeZone.TopLeft)]
    [InlineData(298, 102, PinResizeZone.TopRight)]
    [InlineData(102, 248, PinResizeZone.BottomLeft)]
    [InlineData(298, 248, PinResizeZone.BottomRight)]
    [InlineData(102, 175, PinResizeZone.Left)]
    [InlineData(298, 175, PinResizeZone.Right)]
    [InlineData(200, 102, PinResizeZone.Top)]
    [InlineData(200, 248, PinResizeZone.Bottom)]
    [InlineData(200, 175, PinResizeZone.None)]
    public void 命中测试_八区与内部(double x, double y, PinResizeZone expected)
        => Assert.Equal(expected, PinGeometry.HitTestZone(R, new Point(x, y)));

    [Fact]
    public void 拉伸_右边界随指针_且不小于最小尺寸()
    {
        var r = PinGeometry.ResizeRect(R, PinResizeZone.Right, new Point(350, 175));
        Assert.Equal(250, r.Right);

        var r2 = PinGeometry.ResizeRect(R, PinResizeZone.Right, new Point(110, 175)); // 小于最小
        Assert.Equal(PinGeometry.MinSize, r2.Width);
        Assert.Equal(124, r2.Right - r2.Left); // R.Left + MinSize
    }

    [Fact]
    public void 拉伸_上边不能越过下边减最小()
    {
        var r = PinGeometry.ResizeRect(R, PinResizeZone.Top, new Point(200, 240));
        Assert.Equal(126, r.Top); // R.Bottom - MinSize
        Assert.Equal(PinGeometry.MinSize, r.Height);
    }

    [Fact]
    public void 缩放_按步进_钳制上下限()
    {
        Assert.Equal(1.2, PinGeometry.StepScale(1.0, 120));
        Assert.Equal(0.8, PinGeometry.StepScale(1.0, -120));
        Assert.Equal(8.0, PinGeometry.StepScale(7.95, 120));
        Assert.Equal(0.2, PinGeometry.StepScale(0.25, -120));
    }

    [Fact]
    public void 透明度_按步进_钳制上下限()
    {
        Assert.Equal(0.9, PinGeometry.StepOpacity(1.0, -120));
        Assert.Equal(0.2, PinGeometry.StepOpacity(0.25, -120));
        Assert.Equal(1.0, PinGeometry.StepOpacity(0.95, 120));
    }

    [Fact]
    public void 内容分类_图像优先于文本()
    {
        Assert.Equal(PinSourceKind.Image, PinParser.Classify(true, true));
        Assert.Equal(PinSourceKind.Image, PinParser.Classify(true, false));
        Assert.Equal(PinSourceKind.Text, PinParser.Classify(false, true));
        Assert.Equal(PinSourceKind.None, PinParser.Classify(false, false));
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests --filter "FullyQualifiedName~PinGeometryTests"`（编译失败属预期，类型未创建）

- [ ] **Step 3: 实现纯逻辑**

```csharp
// src/Glyphtap/Pin/PinGeometry.cs
using System.Windows;

namespace Glyphtap.Pin;

/// <summary>贴图窗口几何：边缘 8 区命中、拉伸计算、缩放/透明度步进与钳制。单位 DIP。</summary>
public enum PinResizeZone { None, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

public static class PinGeometry
{
    public const double MinSize = 24;
    private const double Edge = 8;
    public const double MinScale = 0.2;
    public const double MaxScale = 8.0;
    public const double MinOpacity = 0.2;
    private const double ScaleStep = 0.1;
    private const double OpacityStep = 0.1;

    /// <summary>命中测试：角优先于边，边优先于内部。</summary>
    public static PinResizeZone HitTestZone(Rect r, Point p)
    {
        var left = Math.Abs(p.X - r.X) <= Edge;
        var right = Math.Abs(p.X - r.Right) <= Edge;
        var top = Math.Abs(p.Y - r.Y) <= Edge;
        var bottom = Math.Abs(p.Y - r.Bottom) <= Edge;
        if (top && left) return PinResizeZone.TopLeft;
        if (top && right) return PinResizeZone.TopRight;
        if (bottom && left) return PinResizeZone.BottomLeft;
        if (bottom && right) return PinResizeZone.BottomRight;
        if (left) return PinResizeZone.Left;
        if (right) return PinResizeZone.Right;
        if (top) return PinResizeZone.Top;
        if (bottom) return PinResizeZone.Bottom;
        return PinResizeZone.None;
    }

    /// <summary>按拉伸方向计算新矩形，宽高不小于 MinSize。</summary>
    public static Rect ResizeRect(Rect original, PinResizeZone zone, Point p)
    {
        var left = original.Left;
        var top = original.Top;
        var right = original.Right;
        var bottom = original.Bottom;
        if (zone is PinResizeZone.Left or PinResizeZone.TopLeft or PinResizeZone.BottomLeft)
            left = Math.Min(p.X, right - MinSize);
        if (zone is PinResizeZone.Right or PinResizeZone.TopRight or PinResizeZone.BottomRight)
            right = Math.Max(p.X, left + MinSize);
        if (zone is PinResizeZone.Top or PinResizeZone.TopLeft or PinResizeZone.TopRight)
            top = Math.Min(p.Y, bottom - MinSize);
        if (zone is PinResizeZone.Bottom or PinResizeZone.BottomLeft or PinResizeZone.BottomRight)
            bottom = Math.Max(p.Y, top + MinSize);
        return new Rect(left, top, right - left, bottom - top);
    }

    public static double StepScale(double scale, int wheelDelta)
        => Math.Clamp(Math.Round(scale + Math.Sign(wheelDelta) * ScaleStep, 2), MinScale, MaxScale);

    public static double StepOpacity(double opacity, int wheelDelta)
        => Math.Clamp(Math.Round(opacity + Math.Sign(wheelDelta) * OpacityStep, 2), MinOpacity, 1.0);
}
```

```csharp
// src/Glyphtap/Pin/PinParser.cs
namespace Glyphtap.Pin;

/// <summary>剪贴板内容分类：图像优先于文本。</summary>
public enum PinSourceKind { None, Image, Text }

public static class PinParser
{
    public static PinSourceKind Classify(bool hasImage, bool hasText)
        => hasImage ? PinSourceKind.Image : hasText ? PinSourceKind.Text : PinSourceKind.None;
}
```

- [ ] **Step 4: 跑测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests`（含新测试全绿）

- [ ] **Step 5: 提交**

```bash
git add tests/Glyphtap.Tests/PinGeometryTests.cs src/Glyphtap/Pin/PinGeometry.cs src/Glyphtap/Pin/PinParser.cs
git commit -m "feat: 贴图窗口几何与内容分类纯逻辑（PinGeometry/PinParser）"
```

---

### Task 2: HotKeyService 多 id + AppSettings 热键配置

**Files:**
- Modify: `src/Glyphtap/Services/HotKeyService.cs`（id 参数化）
- Create: `src/Glyphtap/Services/AppSettings.cs`
- Test: `tests/Glyphtap.Tests/AppSettingsTests.cs`（新建）

**Interfaces:**
- Consumes: 现有 `HotKeyService` 单 id 注册
- Produces: `HotKeyService.Register(IntPtr hwnd, int id, uint modifier, uint key)`、`AppSettings.Load(string)→AppSettings`、`AppSettings.Save(string)`、`AppSettings.ParseHotKey(string)→(uint Modifier,uint Key)?`、`AppSettings.FormatHotKey(uint,uint)→string`

- [ ] **Step 1: 写失败测试**

```csharp
// tests/Glyphtap.Tests/AppSettingsTests.cs
using System.IO;
using Glyphtap.Services;
using Xunit;

namespace Glyphtap.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData("F1", 0u, 0x70u)]
    [InlineData("C+V", 0x0002u, 0x56u)]
    [InlineData("A+F1", 0x0001u, 0x70u)]
    [InlineData("S+A", 0x0005u, 0x41u)]
    [InlineData("CA+Z", 0x0003u, 0x5Au)]
    public void 热键解析_合法字符串(string s, uint mod, uint key)
    {
        var r = AppSettings.ParseHotKey(s);
        Assert.NotNull(r);
        Assert.Equal(mod, r!.Value.Modifier);
        Assert.Equal(key, r.Value.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("C")]
    [InlineData("X+Y")]
    [InlineData("F0")]
    [InlineData("C+F25")]
    [InlineData("1")]
    public void 热键解析_非法回退null(string s) => Assert.Null(AppSettings.ParseHotKey(s));

    [Fact]
    public void 格式化_往返一致()
    {
        Assert.Equal("C+V", AppSettings.FormatHotKey(0x0002, 0x56));
        Assert.Equal("F1", AppSettings.FormatHotKey(0, 0x70));
        Assert.Equal("S+A+F1", AppSettings.FormatHotKey(0x0004 | 0x0001, 0x70));
    }

    [Fact]
    public void 配置_保存加载往返()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glyphtap_test_{Guid.NewGuid():N}.json");
        try
        {
            var s = new AppSettings { CaptureHotKey = "F2", PinHotKey = "CA+X" };
            s.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal("F2", loaded.CaptureHotKey);
            Assert.Equal("CA+X", loaded.PinHotKey);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void 配置_损坏json回退默认()
    {
        var path = Path.Combine(Path.GetTempPath(), $"glyphtap_test_{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "{ 不是合法json ");
            var s = AppSettings.Load(path);
            Assert.Equal("F1", s.CaptureHotKey);
            Assert.Equal("C+V", s.PinHotKey);
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 2: 跑测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests --filter "FullyQualifiedName~AppSettingsTests"`（编译失败属预期）

- [ ] **Step 3: 实现**

`HotKeyService.cs` 改动（仅签名与 id 来源）：

```csharp
public static HotKeyService Register(IntPtr hwnd, int id, uint modifier, uint key)
    => new(hwnd, id, modifier, key);
```

（`OnWndProc` 已按 `_id` 过滤 wParam，多个实例各自注册即互不干扰；`App.xaml.cs` 现有调用点改为显式 `id: 1`，Task 4 再做双注册组合。）

```csharp
// src/Glyphtap/Services/AppSettings.cs
using System.Text;
using System.Text.Json;

namespace Glyphtap.Services;

/// <summary>应用配置：全局热键两项，JSON 持久化。字符串格式 修饰符* + "+" + 键。</summary>
public sealed class AppSettings
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    public string CaptureHotKey { get; set; } = "F1";
    public string PinHotKey { get; set; } = "C+V";

    /// <summary>加载配置；文件缺失或损坏时回退默认。</summary>
    public static AppSettings Load(string path)
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(path))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 损坏的 JSON：回退默认配置
        }
        return settings;
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>解析热键字符串 → RegisterHotKey 参数；非法返回 null。</summary>
    public static (uint Modifier, uint Key)? ParseHotKey(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var parts = s.ToUpperInvariant().Split('+');
        var keyPart = parts[^1];
        uint mods = 0;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "C": mods |= ModControl; break;
                case "A": mods |= ModAlt; break;
                case "S": mods |= ModShift; break;
                default: return null;
            }
        }
        uint vk;
        if (keyPart.Length == 1 && keyPart[0] is >= 'A' and <= 'Z')
            vk = keyPart[0];
        else if (keyPart.StartsWith("F") && int.TryParse(keyPart[1..], out var n) && n is >= 1 and <= 24)
            vk = 0x70u + (uint)(n - 1);
        else
            return null;
        return (mods, vk);
    }

    /// <summary>反向格式化（设置窗口显示与菜单标题共用）。</summary>
    public static string FormatHotKey(uint modifier, uint key)
    {
        var sb = new StringBuilder();
        if ((modifier & ModControl) != 0) sb.Append("C+");
        if ((modifier & ModAlt) != 0) sb.Append("A+");
        if ((modifier & ModShift) != 0) sb.Append("S+");
        if (key is >= 0x41 and <= 0x5A) sb.Append((char)key);
        else if (key is >= 0x70 and <= 0x87) sb.Append($"F{key - 0x70 + 1}");
        else sb.Append("无效键");
        return sb.ToString();
    }
}
```

- [ ] **Step 4: 跑测试确认通过 + 存量回归**

Run: `dotnet test tests/Glyphtap.Tests`（存量 68 + 新增全绿）

- [ ] **Step 5: 提交**

```bash
git add tests/Glyphtap.Tests/AppSettingsTests.cs src/Glyphtap/Services/HotKeyService.cs src/Glyphtap/Services/AppSettings.cs
git commit -m "feat: 热键多 id 注册与热键配置持久化（AppSettings）"
```

---

### Task 3: CursorService + PinWindow 贴图窗口

**Files:**
- Create: `src/Glyphtap/Infrastructure/CursorService.cs`
- Create: `src/Glyphtap/Pin/PinWindow.cs`

**Interfaces:**
- Consumes: Task 1 的 `PinGeometry`/`PinResizeZone`；现有 `ClipboardService.EncodePng/SetImage/SetText`
- Produces: `CursorService.GetPosition()→Point`（屏幕物理像素，虚拟屏幕坐标）、`PinWindow.CreateImage(BitmapSource, Point centerDips, Action<PinWindow> onClosed)→PinWindow`、`PinWindow.CreateText(string, Point centerDips, Action<PinWindow> onClosed)→PinWindow`、`PinWindow.SaveImage()`

- [ ] **Step 1: CursorService**

```csharp
// src/Glyphtap/Infrastructure/CursorService.cs
using System.Runtime.InteropServices;
using System.Windows;

namespace Glyphtap.Infrastructure;

/// <summary>鼠标位置（屏幕物理像素，虚拟屏幕坐标）。</summary>
public static class CursorService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    public static Point GetPosition()
    {
        GetCursorPos(out var pt);
        return new Point(pt.X, pt.Y);
    }
}
```

- [ ] **Step 2: PinWindow 完整实现**

```csharp
// src/Glyphtap/Pin/PinWindow.cs
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glyphtap.Services;

namespace Glyphtap.Pin;

/// <summary>贴图窗口：无边框 Topmost。拖动/滚轮缩放/Ctrl+滚轮透明度/边缘8区拉伸/双击销毁/右键菜单。</summary>
public sealed class PinWindow : Window
{
    private readonly Action<PinWindow> _onClosed;
    private readonly ScaleTransform _zoom = new();
    private readonly Grid _scaleHost;
    private readonly bool _isImage;
    private BitmapSource _image = null!;
    private string? _text;

    private PinResizeZone _resizeZone;
    private bool _dragging;
    private Point _dragStart;
    private Rect _dragOriginal;
    private DateTime _lastClick = DateTime.MinValue;

    /// <summary>以 centerDips 为中心创建图像贴图（物理像素位图 → DIP 尺寸换算）。</summary>
    public static PinWindow CreateImage(BitmapSource image, Point centerDips, Action<PinWindow> onClosed)
    {
        var w = new PinWindow(onClosed)
        {
            _isImage = true,
            _image = image,
            Width = image.PixelWidth / (image.DpiX / 96.0),
            Height = image.PixelHeight / (image.DpiY / 96.0),
        };
        w._scaleHost.Children.Add(new Image { Source = image, Stretch = Stretch.Fill, Focusable = false });
        w.BuildMenu(hasImage: true);
        w.CenterAt(centerDips);
        return w;
    }

    /// <summary>以 centerDips 为中心创建文本贴图（白底黑字卡片，限宽 400 自动换行）。</summary>
    public static PinWindow CreateText(string text, Point centerDips, Action<PinWindow> onClosed)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 14,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            MaxWidth = 400,
            TextWrapping = TextWrapping.Wrap,
            Padding = new Thickness(10),
        };
        tb.Measure(new Size(400, double.PositiveInfinity));
        tb.Arrange(new Rect(tb.DesiredSize));
        var w = new PinWindow(onClosed)
        {
            _isImage = false,
            _text = text,
            Width = tb.DesiredSize.Width + 2,
            Height = tb.DesiredSize.Height + 2,
        };
        w._scaleHost.Children.Add(tb);
        w.BuildMenu(hasImage: false);
        w.CenterAt(centerDips);
        return w;
    }

    private PinWindow(Action<PinWindow> onClosed)
    {
        _onClosed = onClosed;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        _scaleHost = new Grid { LayoutTransform = _zoom };
        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 0, 180, 255)),
            BorderThickness = new Thickness(1),
            Child = _scaleHost,
        };
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        MouseWheel += OnMouseWheel;
    }

    private void CenterAt(Point centerDips)
    {
        Left = centerDips.X - Width / 2;
        Top = centerDips.Y - Height / 2;
    }

    private void BuildMenu(bool hasImage)
    {
        var menu = new ContextMenu();
        if (hasImage)
        {
            var copy = new MenuItem { Header = "复制图像" };
            copy.Click += (_, _) => Safe(() => ClipboardService.SetImage(_image));
            var save = new MenuItem { Header = "保存图像…" };
            save.Click += (_, _) => Safe(SaveImage);
            menu.Items.Add(copy);
            menu.Items.Add(save);
        }
        else
        {
            var copy = new MenuItem { Header = "复制文本" };
            copy.Click += (_, _) => Safe(() => ClipboardService.SetText(_text ?? ""));
            menu.Items.Add(copy);
        }
        var top = new MenuItem { Header = "置顶" };
        top.Click += (_, _) => Topmost = !Topmost;
        var close = new MenuItem { Header = "销毁贴图" };
        close.Click += (_, _) => Close();
        menu.Items.Add(top);
        menu.Items.Add(close);
        ContextMenu = menu;

        // 剪贴板/保存异常静默（保存失败提示由 Task 4 接 notify 回调用气泡提示）
        void Safe(Action act) { try { act(); } catch (Exception) { } }
    }

    /// <summary>保存图像为 PNG 到用户图片目录；失败抛给调用方提示。</summary>
    public void SaveImage()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Glyphtap");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"Glyphtap_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        File.WriteAllBytes(path, ClipboardService.EncodePng(_image));
    }

    // ---- 鼠标交互 ----

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DateTime.Now - _lastClick < TimeSpan.FromMilliseconds(500))
        {
            Close(); // 双击销毁
            return;
        }
        _lastClick = DateTime.Now;

        _resizeZone = PinGeometry.HitTestZone(new Rect(0, 0, ActualWidth, ActualHeight), e.GetPosition(this));
        _dragOriginal = _resizeZone == PinResizeZone.None
            ? new Rect(Left, Top, 0, 0)
            : new Rect(Left, Top, Width, Height);
        _dragStart = e.GetPosition(this);
        _dragging = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && _dragging)
        {
            if (_resizeZone != PinResizeZone.None)
            {
                var r = PinGeometry.ResizeRect(_dragOriginal, _resizeZone,
                    new Point(_dragOriginal.X + e.GetPosition(this).X, _dragOriginal.Y + e.GetPosition(this).Y));
                Left = r.X;
                Top = r.Y;
                Width = r.Width;
                Height = r.Height;
            }
            else
            {
                var offset = e.GetPosition(this) - _dragStart; // 按下点位移 == 窗口位移
                Left = _dragOriginal.X + offset.X;
                Top = _dragOriginal.Y + offset.Y;
            }
        }
        else if (e.LeftButton != MouseButtonState.Pressed)
        {
            var zone = PinGeometry.HitTestZone(new Rect(0, 0, ActualWidth, ActualHeight), e.GetPosition(this));
            Cursor = zone switch
            {
                PinResizeZone.TopLeft or PinResizeZone.BottomRight => Cursors.SizeNWSE,
                PinResizeZone.TopRight or PinResizeZone.BottomLeft => Cursors.SizeNESW,
                PinResizeZone.Left or PinResizeZone.Right => Cursors.SizeWE,
                PinResizeZone.Top or PinResizeZone.Bottom => Cursors.SizeNS,
                _ => Cursors.SizeAll,
            };
        }
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            Opacity = PinGeometry.StepOpacity(Opacity, e.Delta);
        else
        {
            var s = PinGeometry.StepScale(_zoom.ScaleX, e.Delta);
            _zoom.ScaleX = s;
            _zoom.ScaleY = s;
        }
        e.Handled = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _onClosed(this);
    }
}
```

- [ ] **Step 3: 构建验证**

Run: `dotnet build Glyphtap.sln`（无编译错误；交互属手动验证范围）

- [ ] **Step 4: 提交**

```bash
git add src/Glyphtap/Infrastructure/CursorService.cs src/Glyphtap/Pin/PinWindow.cs
git commit -m "feat: 贴图窗口（图像/文本、拖动/缩放/拉伸/透明度/双击/右键菜单/保存）"
```

---

### Task 4: PinManager + 设置窗口 + 托盘菜单 + App 集成

**Files:**
- Create: `src/Glyphtap/Pin/PinManager.cs`
- Create: `src/Glyphtap/Settings/SettingsWindow.xaml` + `SettingsWindow.xaml.cs`
- Modify: `src/Glyphtap/Services/TrayIconService.cs`（菜单扩展 + 动态标题）
- Modify: `src/Glyphtap/App.xaml.cs`（双热键注册、Ctrl+V 分发、设置入口、退出清理）

**Interfaces:**
- Consumes: Task 1~3 全部产物；`CaptureController.IsCapturing`、`MonitorEnumerator.Enumerate`、`ScreenLayout.Create/MonitorAtPhysical`、`CursorService`
- Produces: `PinManager.TryPinFromClipboard()→bool`、`PinManager.CloseAll()`；托盘「粘贴贴图 (热键)」「设置…」入口

- [ ] **Step 1: PinManager**

```csharp
// src/Glyphtap/Pin/PinManager.cs
using System.Windows;
using System.Windows.Media.Imaging;
using Glyphtap.Capture;
using Glyphtap.Infrastructure;

namespace Glyphtap.Pin;

/// <summary>贴图会话协调：读剪贴板分发、活动贴图登记、鼠标所在屏定位。</summary>
public sealed class PinManager
{
    private readonly Action<string, string> _notify;
    private readonly List<PinWindow> _pins = new();

    public PinManager(Action<string, string> notify) => _notify = notify;

    /// <summary>读系统剪贴板并贴出；无图像无文本返回 false。须 STA 线程调用。</summary>
    public bool TryPinFromClipboard()
    {
        BitmapSource? image = null;
        string? text = null;
        try
        {
            if (Clipboard.ContainsImage())
                image = Clipboard.GetImage();
            else if (Clipboard.ContainsText())
                text = Clipboard.GetText();
        }
        catch (Exception)
        {
            return false; // 剪贴板被占用等：静默忽略
        }
        return CreatePin(image, text);
    }

    private bool CreatePin(BitmapSource? image, string? text)
    {
        if (image == null && string.IsNullOrEmpty(text))
            return false;
        var center = CenterOfMouseScreen();
        PinWindow w;
        if (image != null)
            w = PinWindow.CreateImage(image, center, OnPinClosed);
        else
            w = PinWindow.CreateText(text!, center, OnPinClosed);
        w.Show();
        _pins.Add(w);
        return true;
    }

    /// <summary>鼠标所在屏幕中心（DIP，虚拟屏幕坐标系）。</summary>
    private static Point CenterOfMouseScreen()
    {
        var layout = ScreenLayout.Create(MonitorEnumerator.Enumerate());
        var m = layout.MonitorAtPhysical(CursorService.GetPosition());
        var s = m.ScaleX;
        return new Point(m.Bounds.X / s + m.Bounds.Width / s / 2,
                         m.Bounds.Y / s + m.Bounds.Height / s / 2);
    }

    private void OnPinClosed(PinWindow w) => _pins.Remove(w);

    public void CloseAll()
    {
        foreach (var p in _pins.ToList())
            p.Close();
        _pins.Clear();
    }
}
```

- [ ] **Step 2: SettingsWindow（捕获式热键输入）**

`src/Glyphtap/Settings/SettingsWindow.xaml`：

```xml
<Window x:Class="Glyphtap.Settings.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Glyphtap 设置" Width="340" SizeToContent="Height"
        ResizeMode="NoResize" WindowStartupLocation="CenterScreen" ShowInTaskbar="False">
    <StackPanel Margin="16">
        <TextBlock Text="截图热键（点击输入框后按下新组合键）" Margin="0,0,0,4" />
        <TextBox x:Name="CaptureBox" IsReadOnly="True" PreviewKeyDown="HotKeyBox_PreviewKeyDown" Margin="0,0,0,10" />
        <TextBlock Text="贴图热键（点击输入框后按下新组合键）" Margin="0,0,0,4" />
        <TextBox x:Name="PinBox" IsReadOnly="True" PreviewKeyDown="HotKeyBox_PreviewKeyDown" Margin="0,0,0,14" />
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
            <Button Content="保存" Click="Save_OnClick" Margin="0,0,8,0" />
            <Button Content="取消" Click="Cancel_OnClick" />
        </StackPanel>
    </StackPanel>
</Window>
```

`src/Glyphtap/Settings/SettingsWindow.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Glyphtap.Services;

namespace Glyphtap.Settings;

/// <summary>设置窗口：捕获式热键输入，保存回调 (captureHotKey, pinHotKey) 字符串。</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly Action<string, string> _onSave;

    public SettingsWindow(string captureHotKey, string pinHotKey, Action<string, string> onSave)
    {
        _onSave = onSave;
        InitializeComponent();
        CaptureBox.Text = captureHotKey;
        PinBox.Text = pinHotKey;
    }

    /// <summary>捕获组合键：修饰符 + 主键 → 热键字符串；纯修饰键忽略。</summary>
    private void HotKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        var main = e.Key == Key.System ? e.SystemKey : e.Key;
        if (main is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift)
        {
            e.Handled = true;
            return;
        }
        var mods = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Shift);
        var vk = (uint)KeyInterop.VirtualKeyFromKey(main);
        var s = AppSettings.FormatHotKey(0
            | ((mods & ModifierKeys.Control) != 0 ? 0x0002u : 0u)
            | ((mods & ModifierKeys.Alt) != 0 ? 0x0001u : 0u)
            | ((mods & ModifierKeys.Shift) != 0 ? 0x0004u : 0u), vk);
        if (s == "无效键")
            return; // 非字母/F1~F24 不作为热键（如 Esc、方向键）
        box.Text = s;
        e.Handled = true;
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        if (AppSettings.ParseHotKey(CaptureBox.Text) == null || AppSettings.ParseHotKey(PinBox.Text) == null)
        {
            MessageBox.Show(this, "热键格式无效：需为 字母 或 F1~F24，可加 C/A/S 修饰（如 C+V）", "Glyphtap 设置");
            return;
        }
        _onSave(CaptureBox.Text.Trim(), PinBox.Text.Trim());
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();
}
```

- [ ] **Step 3: TrayIconService 菜单扩展（动态标题）**

`src/Glyphtap/Services/TrayIconService.cs` 构造函数签名改为：

```csharp
public TrayIconService(Action onCapture, Action onPin, Action onSettings, Action onExit,
    string captureHotKeyLabel, string pinHotKeyLabel)
```

菜单构建（替换现有 capture/exit 两行）：

```csharp
var captureItem = new System.Windows.Controls.MenuItem { Header = $"截图 ({captureHotKeyLabel})" };
captureItem.Click += (_, _) => onCapture();
var pinItem = new System.Windows.Controls.MenuItem { Header = $"粘贴贴图 ({pinHotKeyLabel})" };
pinItem.Click += (_, _) => onPin();
var settingsItem = new System.Windows.Controls.MenuItem { Header = "设置…" };
settingsItem.Click += (_, _) => onSettings();
var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
exitItem.Click += (_, _) => onExit();
menu.Items.Add(captureItem);
menu.Items.Add(pinItem);
menu.Items.Add(settingsItem);
menu.Items.Add(exitItem);
```

- [ ] **Step 4: App.xaml.cs 集成（双热键 + 设置 + 退出清理）**

替换 `OnStartup` 中热键段与字段：

```csharp
private HotKeyService? _hotKeyCapture;
private HotKeyService? _hotKeyPin;
private PinManager? _pinManager;
private AppSettings _settings = new();
private static string ConfigPath => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Glyphtap", "config.json");
```

`OnStartup` 中（`_host` 创建后）：

```csharp
_settings = AppSettings.Load(ConfigPath);
_pinManager = new PinManager(Notify);
_tray = new TrayIconService(() => _controller.StartCapture(),
    () => _pinManager.TryPinFromClipboard(), OpenSettings, ExitApp,
    _settings.CaptureHotKey, _settings.PinHotKey);
RegisterHotKeys();
```

新增方法：

```csharp
/// <summary>按配置注册截图/贴图两个全局热键；失败气泡提示并返回 false。</summary>
private bool RegisterHotKeys()
{
    _hotKeyCapture?.Dispose();
    _hotKeyPin?.Dispose();
    var hwnd = new WindowInteropHelper(_host!).Handle;
    var source = HwndSource.FromHwnd(hwnd);
    if (source == null)
        return false;

    var cap = AppSettings.ParseHotKey(_settings.CaptureHotKey);
    _hotKeyCapture = cap is { } c ? HotKeyService.Register(hwnd, 1, c.Modifier, c.Key) : null;
    var pin = AppSettings.ParseHotKey(_settings.PinHotKey);
    _hotKeyPin = pin is { } p ? HotKeyService.Register(hwnd, 2, p.Modifier, p.Key) : null;

    source.RemoveHook(OnHotKeyWndProc);
    source.AddHook(OnHotKeyWndProc);

    if (_hotKeyCapture != null && _hotKeyCapture.IsRegistered)
        _hotKeyCapture.HotKeyPressed += () => _controller.StartCapture();
    else
        Notify("热键注册失败", "截图全局热键被占用，仍可通过托盘菜单截图");

    if (_hotKeyPin != null && _hotKeyPin.IsRegistered)
        _hotKeyPin.HotKeyPressed += () =>
        {
            if (!_controller.IsCapturing) // 截图会话中忽略
                _pinManager!.TryPinFromClipboard();
        };
    else
        Notify("热键注册失败", "贴图全局热键被占用，可通过托盘「粘贴贴图」菜单触发");

    return (_hotKeyCapture?.IsRegistered ?? false) && (_hotKeyPin?.IsRegistered ?? false);
}

private IntPtr OnHotKeyWndProc(IntPtr h, int m, IntPtr w, IntPtr l, ref bool handled)
{
    _hotKeyCapture?.OnWndProc(h, m, w, l, ref handled);
    _hotKeyPin?.OnWndProc(h, m, w, l, ref handled);
    return IntPtr.Zero;
}

private void OpenSettings()
{
    // 冲突回退：新热键注册失败 → 恢复旧值并重注册，不写入配置
    var win = new SettingsWindow(_settings.CaptureHotKey, _settings.PinHotKey, (cap, pin) =>
    {
        var (oldCap, oldPin) = (_settings.CaptureHotKey, _settings.PinHotKey);
        _settings.CaptureHotKey = cap;
        _settings.PinHotKey = pin;
        _tray!.UpdateHotKeyLabels(cap, pin);
        if (!RegisterHotKeys())
        {
            (_settings.CaptureHotKey, _settings.PinHotKey) = (oldCap, oldPin);
            _tray.UpdateHotKeyLabels(oldCap, oldPin);
            RegisterHotKeys();
            Notify("热键冲突", "新热键被占用，已保留原热键");
            return;
        }
        _settings.Save(ConfigPath);
    });
    win.Show();
}
```

补充：`TrayIconService` 加菜单标题更新方法以支持重绑后刷新菜单：

```csharp
private readonly System.Windows.Controls.ContextMenu _menu;
private readonly System.Windows.Controls.MenuItem _captureItem;
private readonly System.Windows.Controls.MenuItem _pinItem;

public void UpdateHotKeyLabels(string captureHotKeyLabel, string pinHotKeyLabel)
{
    _captureItem.Header = $"截图 ({captureHotKeyLabel})";
    _pinItem.Header = $"粘贴贴图 ({pinHotKeyLabel})";
}
```

（实现时把现有局部 `menu/captureItem/pinItem` 提升为字段即可。）

`ExitApp` 与 `OnExit` 中追加清理：

```csharp
_pinManager?.CloseAll();
_hotKeyCapture?.Dispose();
_hotKeyPin?.Dispose();
```

- [ ] **Step 5: 构建 + 全量测试**

Run: `dotnet build Glyphtap.sln`; `dotnet test tests/Glyphtap.Tests`（存量 68 + 新增全绿）

- [ ] **Step 6: 提交**

```bash
git add src/Glyphtap/Pin/PinManager.cs src/Glyphtap/Settings/ src/Glyphtap/Services/TrayIconService.cs src/Glyphtap/App.xaml.cs
git commit -m "feat: 贴图管理器/设置窗口/托盘菜单与双热键集成"
```

---

### Task 5: 手动验证清单

- [ ] **Step 1: 运行与验证**

Run: `dotnet run --project src/Glyphtap`

逐条验证（GUI 环境）：
1. 任意应用 Ctrl+C 复制图像 → Ctrl+V 贴出图像贴图（出现在鼠标所在屏中央，Topmost）
2. 复制文字 → Ctrl+V 贴出白底黑字卡片（限宽 400 自动换行）
3. 拖动画布移动；滚轮缩放（0.2~8x）；Ctrl+滚轮透明度；边缘 8 区拉伸（最小 24 DIP）各方向生效
4. 双击销毁；右键菜单：图像贴图（复制图像/保存图像…/置顶/销毁贴图）、文本贴图（复制文本/置顶/销毁贴图）
5. 复制粘贴链：贴图右键「复制图像」→ Ctrl+V 贴出第二张
6. 右键「保存图像…」→ `%USERPROFILE%\Pictures\Glyphtap\` 生成 PNG，内容一致
7. 置顶开关：取消置顶后窗口被其他窗口遮挡，再开恢复
8. 托盘菜单：截图热键/贴图热键标签显示当前配置；「粘贴贴图 (Ctrl+V)」手动触发有效
9. 设置：重绑截图/贴图热键 → 立即生效、菜单标题更新、重启后仍生效（config.json 持久化）
10. 冲突回退：把热键绑到被占用的组合 → 气泡提示「热键冲突」，原热键保留
11. 截图中按 Ctrl+V → 不贴图；Glyphtap 运行中其他应用 Ctrl+V 被吞 → 改绑热键后恢复（Snipaste 同款副作用已接受）
12. 退出程序 → 所有贴图消失，无残留进程；剪贴板无图像/文本时 Ctrl+V 静默无反应

- [ ] **Step 2: 回归确认**

确认 F1 截图、标注、撤销重做、OCR、多屏负偏移均不受影响；完成后更新 VP 与规格文档状态。

> 待用户执行：GUI 环境人工过 12 条。其余步骤自动测试全覆盖。