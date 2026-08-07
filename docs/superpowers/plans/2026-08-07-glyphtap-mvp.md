# Glyphtap MVP 截图工具实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现 Windows 截图工具 MVP：托盘常驻 + F1 全局热键 + 区域截图 + 矩形/椭圆/箭头/画笔标注 + 复制到剪贴板，架构为 V2 OCR 预留扩展点。

**Architecture:** WPF .NET 8 单全屏窗口覆盖虚拟屏幕（PerMonitorV2 DPI 感知）。所有几何逻辑（选区、标注、屏幕布局）运行在**物理像素空间**，纯逻辑类可单元测试；窗口只做 DIP→物理像素的入口换算（基准 = 窗口实际 DPI，混合 DPI 下 WPF 窗口 DPI 由重叠面积最大的显示器决定，不一定是主屏）。标注/合成用 DrawingContext + RenderTargetBitmap 以物理像素输出，保证多屏/高 DPI 下无偏移。

**Tech Stack:** .NET 8 / WPF / xUnit / H.NotifyIcon（托盘）/ System.Drawing.Common（分屏捕获拼接）

## Global Constraints

- 目标框架：`net8.0-windows`（主项目与测试项目），`UseWPF=true`（两个项目都要，测试项目需引用 WPF 类型）
- DPI 感知：`PerMonitorV2`，通过 `Properties/app.manifest` + csproj `ApplicationManifest` 启用
- 几何坐标约定：选区、标注、屏幕布局全部使用**物理像素**；仅鼠标事件入口做 DIP 换算（基准 = 主屏 scale，`primaryScale = primaryDpi / 96`）
- 所有代码注释使用中文；界面文案使用简体中文
- 每个任务结束必须 `dotnet build` 通过 + 相应测试通过 + git 提交
- 提交信息风格：`feat:` / `test:` / `chore:` 前缀，简短英文或中文均可，与仓库现有风格一致
- 禁止引入规格之外的第三方依赖（除下述指定包）
- 依赖包：`H.NotifyIcon.Wpf 2.3.0`（WPF 版 TaskbarIcon 所在包，Core 由传递依赖；原计划 `H.NotifyIcon` 单包只有 Core 无控件）、`System.Drawing.Common 9.0.1`（主项目）；`Xunit.StaFact 1.1.11`（测试项目，模板自带的 xunit 系版本保留）。> 注：原计划写死 2.2.1/8.0.0/1.1.0，但 NuGet 无这些版本，且 WPF 控件在 `H.NotifyIcon.Wpf` 包，经确认改用生态实际版本。
- 规格文档：`docs/superpowers/specs/2026-08-07-glyphtap-design.md`（实现以该文档为准）

---

### Task 1: 项目脚手架（解决方案、WPF 项目、测试项目、DPI manifest）

**Files:**
- Create: `Glyphtap.sln`
- Create: `src/Glyphtap/Glyphtap.csproj`（由模板生成后修改）
- Create: `src/Glyphtap/App.xaml`、`App.xaml.cs`、`MainWindow.xaml`（模板生成，MainWindow 后续删除）
- Create: `src/Glyphtap/Properties/app.manifest`
- Create: `tests/Glyphtap.Tests/Glyphtap.Tests.csproj`（由模板生成后修改）
- Create: `.gitignore`
- Create: `tests/Glyphtap.Tests/SmokeTests.cs`

**Interfaces:**
- Produces: 可构建的解决方案，`dotnet test` 能跑通 1 个冒烟测试；主项目已启用 PerMonitorV2

- [ ] **Step 1: 生成项目骨架**

```bash
dotnet new sln -n Glyphtap
dotnet new wpf -n Glyphtap -o src/Glyphtap -f net8.0
dotnet new xunit -n Glyphtap.Tests -o tests/Glyphtap.Tests -f net8.0
dotnet sln add src/Glyphtap/Glyphtap.csproj tests/Glyphtap.Tests/Glyphtap.Tests.csproj
```

- [ ] **Step 2: 修改两个 csproj**

`src/Glyphtap/Glyphtap.csproj` 改为：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
    <ApplicationManifest>Properties\app.manifest</ApplicationManifest>
    <RootNamespace>Glyphtap</RootNamespace>
    <AssemblyName>Glyphtap</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="H.NotifyIcon" Version="2.3.0" />
    <PackageReference Include="System.Drawing.Common" Version="9.0.1" />
  </ItemGroup>
</Project>
```

`tests/Glyphtap.Tests/Glyphtap.Tests.csproj` 改为（保留模板生成的 xunit 包版本）：

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
    <PackageReference Include="Xunit.StaFact" Version="1.1.11" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Glyphtap\Glyphtap.csproj" />
  </ItemGroup>
</Project>
```

若 `dotnet new xunit` 生成的模板版本不同，以模板生成的版本号为准，仅保留上述固定版本中已有的 `xunit.stafact` 必须添加。

- [ ] **Step 3: 创建 DPI manifest**

`src/Glyphtap/Properties/app.manifest`：

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Glyphtap.app" />
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 4: 创建 .gitignore**

```gitignore
bin/
obj/
*.user
.vs/
```

- [ ] **Step 5: 写冒烟测试（验证 WPF 类型引用链路）**

`tests/Glyphtap.Tests/SmokeTests.cs`：

```csharp
using Xunit;

namespace Glyphtap.Tests;

public class SmokeTests
{
    [Fact]
    public void Wpf_Types_Are_Referenceable()
    {
        var color = System.Windows.Media.Colors.Red;
        Assert.Equal(255, color.R);
    }
}
```

- [ ] **Step 6: 构建并运行测试**

Run: `dotnet build Glyphtap.sln`
Expected: 生成成功（0 error）
Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 1 passed

- [ ] **Step 7: 提交**

```bash
git add -A
git commit -m "chore: 项目脚手架（WPF + xUnit + PerMonitorV2）"
```

---

### Task 2: 屏幕布局模型 ScreenLayout / MonitorInfo（纯逻辑）

**Files:**
- Create: `src/Glyphtap/Capture/ScreenLayout.cs`
- Test: `tests/Glyphtap.Tests/ScreenLayoutTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class MonitorInfo { public Rect Bounds; public int DpiX; public int DpiY; public bool IsPrimary; public double ScaleX => DpiX / 96.0; public double ScaleY => DpiY / 96.0; }`（`Rect` 为 `System.Windows.Rect`，物理像素）
  - `public sealed class ScreenLayout`：
    - `public static ScreenLayout Create(IReadOnlyList<MonitorInfo> monitors)`
    - `public IReadOnlyList<MonitorInfo> Monitors { get; }`
    - `public Rect VirtualBounds { get; }`（所有显示器合并矩形，物理像素，可为负坐标）
    - `public MonitorInfo Primary { get; }`
    - `public double PrimaryScale { get; }`（= Primary.DpiX / 96.0）
    - `public MonitorInfo MonitorAtPhysical(Point p)`（命中测试，未命中返回 Primary）
    - `public Point ToPhysical(Point windowDips)`（窗口 DIP 坐标 → 虚拟屏幕物理像素）
    - `public Point ToWindowDips(Point physical)`（反向）
- Consumes: 无（Task 3 提供 MonitorInfo 数据源，此处从构造参数输入）

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/ScreenLayoutTests.cs`：

```csharp
using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class ScreenLayoutTests
{
    private static readonly MonitorInfo Primary =
        new() { Bounds = new Rect(0, 0, 1920, 1080), DpiX = 96, DpiY = 96, IsPrimary = true };

    private static readonly MonitorInfo Secondary =
        new() { Bounds = new Rect(-1280, 0, 1280, 1024), DpiX = 144, DpiY = 144, IsPrimary = false };

    [Fact]
    public void Create_合并虚拟屏幕矩形_含负坐标()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Equal(new Rect(-1280, 0, 3200, 1080), layout.VirtualBounds);
    }

    [Fact]
    public void Create_主屏scale为基准()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Equal(1.0, layout.PrimaryScale);
        Assert.Equal(1.5, layout.Monitors[1].ScaleX);
    }

    [Fact]
    public void MonitorAtPhysical_按坐标命中屏幕()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        Assert.Same(Secondary, layout.MonitorAtPhysical(new Point(-100, 500)));
        Assert.Same(Primary, layout.MonitorAtPhysical(new Point(100, 500)));
    }

    [Fact]
    public void ToPhysical_与_ToWindowDips_往返一致()
    {
        var layout = ScreenLayout.Create(new[] { Primary, Secondary });
        var physical = layout.ToPhysical(new Point(100, 50));
        var dips = layout.ToWindowDips(physical);
        Assert.Equal(100, dips.X, 2);
        Assert.Equal(50, dips.Y, 2);
    }

    [Fact]
    public void Create_无显示器时抛出()
    {
        Assert.Throws<ArgumentException>(() => ScreenLayout.Create(Array.Empty<MonitorInfo>()));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败，ScreenLayout 不存在）

- [ ] **Step 3: 实现 ScreenLayout**

`src/Glyphtap/Capture/ScreenLayout.cs`：

```csharp
using System.Windows;

namespace Glyphtap.Capture;

/// <summary>单台显示器信息（物理像素坐标）。</summary>
public sealed class MonitorInfo
{
    /// <summary>物理像素，虚拟屏幕坐标。</summary>
    public Rect Bounds { get; set; }

    /// <summary>该显示器实际 DPI（PerMonitorV2）。</summary>
    public int DpiX { get; set; }
    public int DpiY { get; set; }

    public bool IsPrimary { get; set; }

    /// <summary>DPI 缩放因子。</summary>
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
}

/// <summary>
/// 虚拟屏幕布局：合并所有显示器，提供物理像素与窗口 DIP 坐标的双向换算。
/// 换算基准 = 主屏 scale（WPF 窗口坐标系以主屏 DPI 为准）。
/// </summary>
public sealed class ScreenLayout
{
    private readonly IReadOnlyList<MonitorInfo> _monitors;
    private readonly MonitorInfo _primary;

    private ScreenLayout(IReadOnlyList<MonitorInfo> monitors, Rect virtualBounds, MonitorInfo primary)
    {
        _monitors = monitors;
        VirtualBounds = virtualBounds;
        _primary = primary;
    }

    public static ScreenLayout Create(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0)
            throw new ArgumentException("至少需要一个显示器", nameof(monitors));

        var union = monitors[0].Bounds;
        foreach (var m in monitors)
            union.Union(m.Bounds);

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        return new ScreenLayout(monitors, union, primary);
    }

    public IReadOnlyList<MonitorInfo> Monitors => _monitors;

    /// <summary>所有显示器合并矩形（物理像素，可为负坐标）。</summary>
    public Rect VirtualBounds { get; }

    public MonitorInfo Primary => _primary;

    /// <summary>主屏 DPI 缩放因子，窗口 DIP ↔ 物理像素的换算基准。</summary>
    public double PrimaryScale => _primary.ScaleX;

    /// <summary>命中测试：返回包含该物理坐标的显示器，未命中返回主屏。</summary>
    public MonitorInfo MonitorAtPhysical(Point p)
    {
        foreach (var m in _monitors)
        {
            if (m.Bounds.Contains(p))
                return m;
        }
        return _primary;
    }

    /// <summary>窗口内 DIP 坐标（相对窗口左上角）→ 虚拟屏幕物理像素。</summary>
    public Point ToPhysical(Point windowDips)
    {
        var px = windowDips.X * PrimaryScale;
        var py = windowDips.Y * PrimaryScale;
        return new Point(VirtualBounds.X + px, VirtualBounds.Y + py);
    }

    /// <summary>虚拟屏幕物理像素 → 窗口内 DIP 坐标。</summary>
    public Point ToWindowDips(Point physical)
    {
        var dx = physical.X - VirtualBounds.X;
        var dy = physical.Y - VirtualBounds.Y;
        return new Point(dx / PrimaryScale, dy / PrimaryScale);
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 5 passed

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 屏幕布局模型与 DPI 换算（ScreenLayout/MonitorInfo）"
```

---

### Task 3: 显示器枚举 MonitorEnumerator

**Files:**
- Create: `src/Glyphtap/Capture/MonitorEnumerator.cs`
- Test: `tests/Glyphtap.Tests/MonitorEnumeratorTests.cs`

**Interfaces:**
- Consumes: `MonitorInfo`（Task 2）
- Produces:
  - `public sealed record MonitorSpec(Rect Bounds, int DpiX, int DpiY, bool IsPrimary)` — 测试与 P/Invoke 的中间载体
  - `public static class MonitorEnumerator`：
    - `public static IReadOnlyList<MonitorInfo> Enumerate()` — Win32 枚举真实显示器
    - `public static IReadOnlyList<MonitorInfo> FromSpecs(IReadOnlyList<MonitorSpec> specs)` — 从规格构建（测试入口）

- [ ] **Step 1: 写失败测试（FromSpecs 为纯逻辑）**

`tests/Glyphtap.Tests/MonitorEnumeratorTests.cs`：

```csharp
using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class MonitorEnumeratorTests
{
    [Fact]
    public void FromSpecs_保留DPI与主屏标记()
    {
        var monitors = MonitorEnumerator.FromSpecs(new[]
        {
            new MonitorSpec(new Rect(0, 0, 1920, 1080), 96, 96, true),
            new MonitorSpec(new Rect(1920, 0, 1280, 1024), 144, 144, false),
        });

        Assert.Equal(2, monitors.Count);
        Assert.Equal(1920, monitors[0].Bounds.Width);
        Assert.Equal(144, monitors[1].DpiX);
        Assert.True(monitors[0].IsPrimary);
        Assert.False(monitors[1].IsPrimary);
    }

    [Fact]
    public void FromSpecs_空列表返回空()
    {
        Assert.Empty(MonitorEnumerator.FromSpecs(Array.Empty<MonitorSpec>()));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现 MonitorEnumerator（Win32 枚举 + FromSpecs）**

`src/Glyphtap/Capture/MonitorEnumerator.cs`：

```csharp
using System.Runtime.InteropServices;
using System.Windows;

namespace Glyphtap.Capture;

/// <summary>显示器规格中间载体（便于测试与 Win32 数据归一）。</summary>
public sealed record MonitorSpec(Rect Bounds, int DpiX, int DpiY, bool IsPrimary);

/// <summary>枚举 Windows 显示器，产出物理像素坐标与 DPI 信息。</summary>
public static class MonitorEnumerator
{
    // ---- Win32 定义 ----
    private const int MonitorInfoFPrimary = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    private const int MdtEffectiveDpi = 0;

    /// <summary>枚举当前全部显示器。</summary>
    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var specs = new List<MonitorSpec>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, hdc, lprcMonitor, dwData) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (GetMonitorInfo(hMonitor, ref info))
                {
                    GetDpiForMonitor(hMonitor, MdtEffectiveDpi, out var dpiX, out var dpiY);
                    specs.Add(new MonitorSpec(
                        new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                                 info.rcMonitor.Right - info.rcMonitor.Left,
                                 info.rcMonitor.Bottom - info.rcMonitor.Top),
                        (int)dpiX, (int)dpiY,
                        (info.dwFlags & MonitorInfoFPrimary) != 0));
                }
                return true;
            }, IntPtr.Zero);

        return FromSpecs(specs);
    }

    /// <summary>从规格列表构建（纯逻辑，供测试与 Enumerate 复用）。</summary>
    public static IReadOnlyList<MonitorInfo> FromSpecs(IReadOnlyList<MonitorSpec> specs)
    {
        return specs.Select(s => new MonitorInfo
        {
            Bounds = s.Bounds,
            DpiX = s.DpiX,
            DpiY = s.DpiY,
            IsPrimary = s.IsPrimary,
        }).ToList();
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 2 passed（+ 之前 5 passed）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 显示器枚举（Win32 EnumDisplayMonitors + DPI）"
```

---

### Task 4: 选区几何状态机 SelectionLogic（纯逻辑）

**Files:**
- Create: `src/Glyphtap/Capture/SelectionLogic.cs`
- Test: `tests/Glyphtap.Tests/SelectionLogicTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public enum SelectionMode { None, Creating, Moving, Resizing }`
  - `public enum ResizeHandle { None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left }`
  - `public sealed class SelectionLogic`：
    - `public const double MinSize = 8;`（物理像素最小选区边长）
    - `public const double GrabTolerance = 8;`（手柄命中容差）
    - `public Rect Selection { get; }`（物理像素，可为空矩形）
    - `public bool HasSelection => !Selection.IsEmpty;`
    - `public SelectionMode Mode { get; }`
    - `public void OnMouseDown(Point p)` / `public void OnMouseMove(Point p)` / `public void OnMouseUp()`
    - `public void Clear()`
    - `public static Rect Normalize(Rect r)`（反向拖拽归一化）
    - `public static Rect ApplyMinSize(Rect r)`（扩到最小边长）
    - `public static ResizeHandle HitTestHandle(Point p, Rect rect)`（命中 8 手柄，中心区域命中返回 None）
    - `public static Cursor? CursorForHandle(ResizeHandle h)`（可选，MVP 不实现光标）
  - 交互规则：无选区时按下 = 开始创建；有选区时按下 → 先命中手柄（Resizing）→ 再命中选区内（Moving）→ 否则重新创建；移动/缩放时保持对手柄的反向钳制（对角手柄反向拖拽合法，即归一化）

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/SelectionLogicTests.cs`：

```csharp
using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class SelectionLogicTests
{
    [Fact]
    public void 拖拽创建_正向与反向都归一化()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(200, 160));
        Assert.Equal(new Rect(100, 100, 100, 60), logic.Selection);
        Assert.Equal(SelectionMode.Creating, logic.Mode);

        logic.Clear();
        logic.OnMouseDown(new Point(200, 160));
        logic.OnMouseMove(new Point(100, 100));
        Assert.Equal(new Rect(100, 100, 100, 60), logic.Selection);
    }

    [Fact]
    public void 有选区时_按下选区内_进入移动模式()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(200, 200));
        Assert.Equal(SelectionMode.Moving, logic.Mode);

        logic.OnMouseMove(new Point(250, 220));
        Assert.Equal(new Rect(150, 120, 200, 200), logic.Selection);
    }

    [Fact]
    public void 按下手柄_进入缩放模式()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(300, 300)); // 右下角手柄
        Assert.Equal(SelectionMode.Resizing, logic.Mode);

        logic.OnMouseMove(new Point(350, 380));
        Assert.Equal(new Rect(100, 100, 250, 280), logic.Selection);
    }

    [Fact]
    public void 反向拖动手柄_选区归一化()
    {
        // 初始选区 (100,100,200,200)（Right=300, Bottom=300）。按 TopRight 手柄(300,100) 拖到 p=(80,160)：
        // 公式：保持 start.X 与 start.Bottom 固定，top=p.Y, right=p.X → Rect(100,160,-20,140)
        // Normalize（负宽交换起终点）→ (80,160,20,140)，ApplyMinSize 不变
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(300, 100)); // TopRight 手柄
        logic.OnMouseMove(new Point(80, 160));  // 拖过左/上边界，触发归一化
        Assert.Equal(new Rect(80, 160, 20, 140), logic.Selection);
    }

    [Fact]
    public void 选区不小于最小尺寸()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(102, 101));
        var s = logic.Selection;
        Assert.True(s.Width >= SelectionLogic.MinSize && s.Height >= SelectionLogic.MinSize);
    }

    [Fact]
    public void 选区外按下_重新创建选区()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(500, 500));
        Assert.Equal(SelectionMode.Creating, logic.Mode);
        logic.OnMouseMove(new Point(600, 550));
        Assert.Equal(new Rect(500, 500, 100, 50), logic.Selection);
    }

    [Fact]
    public void HitTestHandle_命中各手柄与内部()
    {
        var rect = new Rect(100, 100, 200, 150);
        Assert.Equal(ResizeHandle.TopLeft, SelectionLogic.HitTestHandle(new Point(102, 102), rect));
        Assert.Equal(ResizeHandle.BottomRight, SelectionLogic.HitTestHandle(new Point(298, 248), rect));
        Assert.Equal(ResizeHandle.Top, SelectionLogic.HitTestHandle(new Point(200, 102), rect));
        Assert.Equal(ResizeHandle.Left, SelectionLogic.HitTestHandle(new Point(102, 175), rect));
        Assert.Equal(ResizeHandle.None, SelectionLogic.HitTestHandle(new Point(200, 175), rect));
        Assert.Equal(ResizeHandle.None, SelectionLogic.HitTestHandle(new Point(400, 400), rect));
    }

    [Fact]
    public void Clear_清空选区()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.Clear();
        Assert.False(logic.HasSelection);
        Assert.Equal(SelectionMode.None, logic.Mode);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现 SelectionLogic**

`src/Glyphtap/Capture/SelectionLogic.cs`：

```csharp
using System.Windows;

namespace Glyphtap.Capture;

public enum SelectionMode { None, Creating, Moving, Resizing }

public enum ResizeHandle
{
    None, TopLeft, Top, TopRight, Right,
    BottomRight, Bottom, BottomLeft, Left,
}

/// <summary>
/// 选区几何状态机（物理像素空间，纯逻辑）。
/// 规则：无选区按下=创建；有选区按下=命中手柄→缩放 →命中选区内→移动 →否则重新创建。
/// </summary>
public sealed class SelectionLogic
{
    public const double MinSize = 8;
    public const double GrabTolerance = 8;

    private Point _down;
    private Rect _startRect;
    private ResizeHandle _activeHandle = ResizeHandle.None;
    private bool _dragging;

    public Rect Selection { get; private set; } = Rect.Empty;
    public SelectionMode Mode { get; private set; } = SelectionMode.None;
    public bool HasSelection => !Selection.IsEmpty;

    public void OnMouseDown(Point p)
    {
        _down = p;
        _dragging = true;

        if (!HasSelection)
        {
            Mode = SelectionMode.Creating;
            Selection = new Rect(p, new Size(0, 0));
            return;
        }

        var handle = HitTestHandle(p, Selection);
        if (handle != ResizeHandle.None)
        {
            Mode = SelectionMode.Resizing;
            _activeHandle = handle;
            _startRect = Selection;
            return;
        }

        if (Selection.Contains(p))
        {
            Mode = SelectionMode.Moving;
            _startRect = Selection;
            return;
        }

        // 选区外按下：重新创建
        Mode = SelectionMode.Creating;
        Selection = new Rect(p, new Size(0, 0));
    }

    public void OnMouseMove(Point p)
    {
        if (!_dragging)
            return;

        switch (Mode)
        {
            case SelectionMode.Creating:
                Selection = ApplyMinSize(Normalize(new Rect(_down, p)));
                break;

            case SelectionMode.Moving:
                var delta = p - _down;
                var moved = _startRect;
                moved.Offset(delta);
                Selection = moved;
                break;

            case SelectionMode.Resizing:
                Selection = ApplyMinSize(Normalize(ResizeTo(_activeHandle, _startRect, p)));
                break;
        }
    }

    public void OnMouseUp()
    {
        _dragging = false;
        if (Mode == SelectionMode.Creating && Selection.Width < 1 && Selection.Height < 1)
            Selection = Rect.Empty;
        Mode = SelectionMode.None;
        _activeHandle = ResizeHandle.None;
    }

    public void Clear()
    {
        Selection = Rect.Empty;
        Mode = SelectionMode.None;
        _activeHandle = ResizeHandle.None;
        _dragging = false;
    }

    /// <summary>反向拖拽归一化：交换起终点。</summary>
    public static Rect Normalize(Rect r)
    {
        var x = Math.Min(r.X, r.X + r.Width);
        var y = Math.Min(r.Y, r.Y + r.Height);
        return new Rect(x, y, Math.Abs(r.Width), Math.Abs(r.Height));
    }

    /// <summary>最小边长钳制：以左上角为锚向外扩展。</summary>
    public static Rect ApplyMinSize(Rect r)
    {
        if (r.Width >= MinSize && r.Height >= MinSize)
            return r;
        var w = Math.Max(r.Width, MinSize);
        var h = Math.Max(r.Height, MinSize);
        return new Rect(r.X, r.Y, w, h);
    }

    /// <summary>8 手柄命中测试（含 GrabTolerance 容差）。</summary>
    public static ResizeHandle HitTestHandle(Point p, Rect rect)
    {
        var tl = new Rect(rect.X - GrabTolerance, rect.Y - GrabTolerance, GrabTolerance * 2, GrabTolerance * 2);
        var tr = new Rect(rect.Right - GrabTolerance, rect.Y - GrabTolerance, GrabTolerance * 2, GrabTolerance * 2);
        var bl = new Rect(rect.X - GrabTolerance, rect.Bottom - GrabTolerance, GrabTolerance * 2, GrabTolerance * 2);
        var br = new Rect(rect.Right - GrabTolerance, rect.Bottom - GrabTolerance, GrabTolerance * 2, GrabTolerance * 2);

        if (tl.Contains(p)) return ResizeHandle.TopLeft;
        if (tr.Contains(p)) return ResizeHandle.TopRight;
        if (bl.Contains(p)) return ResizeHandle.BottomLeft;
        if (br.Contains(p)) return ResizeHandle.BottomRight;

        var top = new Rect(rect.X + GrabTolerance, rect.Y - GrabTolerance, rect.Width - GrabTolerance * 2, GrabTolerance * 2);
        var bottom = new Rect(rect.X + GrabTolerance, rect.Bottom - GrabTolerance, rect.Width - GrabTolerance * 2, GrabTolerance * 2);
        var left = new Rect(rect.X - GrabTolerance, rect.Y + GrabTolerance, GrabTolerance * 2, rect.Height - GrabTolerance * 2);
        var right = new Rect(rect.Right - GrabTolerance, rect.Y + GrabTolerance, GrabTolerance * 2, rect.Height - GrabTolerance * 2);

        if (top.Contains(p)) return ResizeHandle.Top;
        if (bottom.Contains(p)) return ResizeHandle.Bottom;
        if (left.Contains(p)) return ResizeHandle.Left;
        if (right.Contains(p)) return ResizeHandle.Right;

        return ResizeHandle.None;
    }

    /// <summary>
    /// 按手柄缩放：固定对边（start 内边坐标），动边跟随指针，允许反向（自动归一化）。
    /// 注：WPF Rect 不允许负宽高，故改用动边/定边取 min/max 构造（等价于归一化后构造）。
    /// </summary>
    private static Rect ResizeTo(ResizeHandle handle, Rect start, Point p)
    {
        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        switch (handle)
        {
            case ResizeHandle.Top: top = p.Y; break;
            case ResizeHandle.Bottom: bottom = p.Y; break;
            case ResizeHandle.Left: left = p.X; break;
            case ResizeHandle.Right: right = p.X; break;
            case ResizeHandle.TopLeft: left = p.X; top = p.Y; break;
            case ResizeHandle.TopRight: right = p.X; top = p.Y; break;
            case ResizeHandle.BottomLeft: left = p.X; bottom = p.Y; break;
            case ResizeHandle.BottomRight: right = p.X; bottom = p.Y; break;
            default: return start;
        }

        return new Rect(
            new Point(Math.Min(left, right), Math.Min(top, bottom)),
            new Point(Math.Max(left, right), Math.Max(top, bottom)));
    }
}
```

> 说明：`Normalize` 会将反向拖动修正方向；`ResizeTo` 内部已按 min/max 归一化构造（规避 WPF 不允许负宽高的限制），`OnMouseMove` 中 `Normalize(ResizeTo(...))` 幂等无害。移动模式下需用局部变量 `moved.Offset(delta)` 后赋值回 `Selection`（结构体属性原地 Offset 无效）。测试中的预期值已验证此行为。

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过（8 个新测试）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 选区几何状态机（创建/移动/8手柄缩放）"
```

---

### Task 5: 标注模型 + AnnotationManager（纯逻辑）

**Files:**
- Create: `src/Glyphtap/Capture/AnnotationModel.cs`
- Create: `src/Glyphtap/Capture/AnnotationManager.cs`
- Test: `tests/Glyphtap.Tests/AnnotationManagerTests.cs`

**Interfaces:**
- Consumes: 无
- Produces:
  - `public enum AnnotationKind { Rectangle, Ellipse, Arrow, Pen }`
  - `public abstract class Annotation`：`public AnnotationKind Kind { get; }`、`public Color Color { get; set; }`（`System.Windows.Media.Color`）、`public double Thickness { get; set; }`、`public abstract void Offset(Vector delta)`、`public abstract void Resize(Rect newBounds)`、`public abstract Rect Bounds { get; }`
  - `public sealed class RectangleAnnotation : Annotation { public Rect Rect; }`
  - `public sealed class EllipseAnnotation : Annotation { public Rect Rect; }`
  - `public sealed class ArrowAnnotation : Annotation { public Point Start; public Point End; }`
  - `public sealed class PenAnnotation : Annotation { public List<Point> Points; }`（可增点：`public void AddPoint(Point p)`）
  - `public sealed class AnnotationManager`：
    - `public IReadOnlyList<Annotation> Items { get; }`
    - `public Annotation? Selected { get; }`
    - `public void Add(Annotation a)`
    - `public bool TrySelectAt(Point p, double tolerance)`（命中测试，从后往前）
    - `public void DeleteSelected()`
    - `public void Clear()`
    - `public void MoveSelectedBy(Vector delta)`
    - `public void MoveAllBy(Vector delta)`（选区整体移动时标注随动）
    - `public static bool HitTest(Annotation a, Point p, double tolerance)`（静态命中：矩形/椭圆边界+填充、箭头线段距离、画笔折线距离）

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/AnnotationManagerTests.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class AnnotationManagerTests
{
    private static RectangleAnnotation Rect(double x, double y, double w, double h) =>
        new() { Rect = new Rect(x, y, w, h), Color = Colors.Red, Thickness = 3 };

    [Fact]
    public void Add_与_Clear_管理条目()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(20, 20, 10, 10));
        Assert.Equal(2, mgr.Items.Count);
        mgr.Clear();
        Assert.Empty(mgr.Items);
        Assert.Null(mgr.Selected);
    }

    [Fact]
    public void TrySelectAt_命中内部_与_未命中()
    {
        var mgr = new AnnotationManager();
        var a = Rect(0, 0, 50, 50);
        mgr.Add(a);
        Assert.True(mgr.TrySelectAt(new Point(25, 25), 5));
        Assert.Same(a, mgr.Selected);
        Assert.False(mgr.TrySelectAt(new Point(100, 100), 5));
        Assert.Null(mgr.Selected);
    }

    [Fact]
    public void TrySelectAt_重叠时选最上层()
    {
        var mgr = new AnnotationManager();
        var lower = Rect(0, 0, 100, 100);
        var upper = Rect(10, 10, 20, 20);
        mgr.Add(lower);
        mgr.Add(upper);
        mgr.TrySelectAt(new Point(15, 15), 5);
        Assert.Same(upper, mgr.Selected);
    }

    [Fact]
    public void DeleteSelected_与_MoveSelectedBy()
    {
        var mgr = new AnnotationManager();
        var a = Rect(10, 10, 40, 40);
        mgr.Add(a);
        mgr.TrySelectAt(new Point(30, 30), 5);
        mgr.MoveSelectedBy(new Vector(5, -5));
        Assert.Equal(new Rect(15, 5, 40, 40), ((RectangleAnnotation)mgr.Selected!).Rect);

        mgr.DeleteSelected();
        Assert.Empty(mgr.Items);
    }

    [Fact]
    public void MoveAllBy_整体平移_标注相对选区保持()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(50, 50, 10, 10));
        mgr.MoveAllBy(new Vector(-10, 20));
        Assert.Equal(new Rect(-10, 20, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);
        Assert.Equal(new Rect(40, 70, 10, 10), ((RectangleAnnotation)mgr.Items[1]).Rect);
    }

    [Fact]
    public void HitTest_箭头与画笔按距离命中()
    {
        var arrow = new ArrowAnnotation { Start = new Point(0, 0), End = new Point(100, 0), Color = Colors.Red, Thickness = 3 };
        Assert.True(AnnotationManager.HitTest(arrow, new Point(50, 2), 5));
        Assert.False(AnnotationManager.HitTest(arrow, new Point(50, 30), 5));

        var pen = new PenAnnotation { Color = Colors.Red, Thickness = 3 };
        pen.AddPoint(new Point(0, 0));
        pen.AddPoint(new Point(0, 100));
        Assert.True(AnnotationManager.HitTest(pen, new Point(2, 50), 5));
        Assert.False(AnnotationManager.HitTest(pen, new Point(30, 50), 5));
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现标注模型与管理器**

`src/Glyphtap/Capture/AnnotationModel.cs`：

```csharp
using System.Windows;
using System.Windows.Media;

namespace Glyphtap.Capture;

public enum AnnotationKind { Rectangle, Ellipse, Arrow, Pen }

/// <summary>标注基类。坐标相对选区（物理像素）。</summary>
public abstract class Annotation
{
    public AnnotationKind Kind { get; init; }
    public Color Color { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 3;

    public abstract Rect Bounds { get; }
    public abstract void Offset(Vector delta);
    public abstract void Resize(Rect newBounds);
}

public sealed class RectangleAnnotation : Annotation
{
    public Rect Rect;
    public RectangleAnnotation() { Kind = AnnotationKind.Rectangle; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
}

public sealed class EllipseAnnotation : Annotation
{
    public Rect Rect;
    public EllipseAnnotation() { Kind = AnnotationKind.Ellipse; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
}

public sealed class ArrowAnnotation : Annotation
{
    public Point Start;
    public Point End;
    public ArrowAnnotation() { Kind = AnnotationKind.Arrow; }
    public override Rect Bounds => new Rect(new Point(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y)),
                                            new Point(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y)));
    public override void Offset(Vector delta) { Start += delta; End += delta; }
    public override void Resize(Rect newBounds) { /* 箭头 MVP 不缩放，仅移动时随 Offset */ }
}

public sealed class PenAnnotation : Annotation
{
    public List<Point> Points = new();
    public PenAnnotation() { Kind = AnnotationKind.Pen; }
    public void AddPoint(Point p) => Points.Add(p);
    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0)
                return Rect.Empty;
            var minX = Points.Min(p => p.X);
            var minY = Points.Min(p => p.Y);
            var maxX = Points.Max(p => p.X);
            var maxY = Points.Max(p => p.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
    public override void Offset(Vector delta) { for (var i = 0; i < Points.Count; i++) Points[i] += delta; }
    public override void Resize(Rect newBounds) { /* 画笔 MVP 不缩放 */ }
}
```

> 注意：`ArrowAnnotation.Resize` 与 `PenAnnotation.Resize` 不实现缩放（MVP 中标注存选区相对坐标，选区移动时天然跟随；选区缩放时标注不缩放，合成裁剪由 `PushClip` 完成）。`PenAnnotation.Bounds` 实现为点集包围盒。

`src/Glyphtap/Capture/AnnotationManager.cs`：

```csharp
using System.Windows;

namespace Glyphtap.Capture;

/// <summary>标注集合管理：增删、选中、移动、整体平移。</summary>
public sealed class AnnotationManager
{
    private readonly List<Annotation> _items = new();
    private Annotation? _selected;

    public IReadOnlyList<Annotation> Items => _items;
    public Annotation? Selected => _selected;

    public void Add(Annotation a) => _items.Add(a);

    /// <summary>命中测试（后加入者优先，即最上层）。</summary>
    public bool TrySelectAt(Point p, double tolerance)
    {
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (HitTest(_items[i], p, tolerance))
            {
                _selected = _items[i];
                return true;
            }
        }
        _selected = null;
        return false;
    }

    public void DeleteSelected()
    {
        if (_selected != null)
            _items.Remove(_selected);
        _selected = null;
    }

    public void Clear()
    {
        _items.Clear();
        _selected = null;
    }

    public void MoveSelectedBy(Vector delta)
    {
        if (_selected != null)
            _selected.Offset(delta);
    }

    /// <summary>选区整体移动时所有标注随动，保持相对位置。</summary>
    public void MoveAllBy(Vector delta)
    {
        foreach (var a in _items)
            a.Offset(delta);
    }

    /// <summary>静态命中测试：矩形/椭圆边界或内部、箭头与画笔按线段距离。</summary>
    public static bool HitTest(Annotation a, Point p, double tolerance)
    {
        switch (a)
        {
            case RectangleAnnotation r:
                return r.Rect.Contains(p) || DistanceToRectEdges(p, r.Rect) <= tolerance;
            case EllipseAnnotation e:
                return DistanceToEllipse(p, e.Rect) <= tolerance;
            case ArrowAnnotation ar:
                return DistanceToSegment(p, ar.Start, ar.End) <= tolerance;
            case PenAnnotation pen:
                for (var i = 1; i < pen.Points.Count; i++)
                {
                    if (DistanceToSegment(p, pen.Points[i - 1], pen.Points[i]) <= tolerance)
                        return true;
                }
                return false;
            default:
                return false;
        }
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var lenSq = ab.LengthSquared;
        if (lenSq < 1e-9)
            return (p - a).Length;
        var t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lenSq, 0, 1);
        var proj = new Point(a.X + t * ab.X, a.Y + t * ab.Y);
        return (p - proj).Length;
    }

    private static double DistanceToRectEdges(Point p, Rect r)
    {
        var dx = Math.Max(r.X - p.X, Math.Max(p.X - r.Right, 0));
        var dy = Math.Max(r.Y - p.Y, Math.Max(p.Y - r.Bottom, 0));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double DistanceToEllipse(Point p, Rect r)
    {
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;
        var rx = Math.Max(r.Width / 2, 0.5);
        var ry = Math.Max(r.Height / 2, 0.5);
        var dx = (p.X - cx) / rx;
        var dy = (p.Y - cy) / ry;
        var d = Math.Sqrt(dx * dx + dy * dy);
        var ring = Math.Abs(d - 1) * Math.Min(rx, ry);
        return d <= 1 ? Math.Min(ring, 10) : ring;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过（6 个新测试）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 标注模型与 AnnotationManager（增删/选中/移动）"
```

---

### Task 6: 标注工具四件套 + 箭头几何（纯逻辑）

**Files:**
- Create: `src/Glyphtap/Capture/AnnotationTools.cs`
- Test: `tests/Glyphtap.Tests/AnnotationToolsTests.cs`

**Interfaces:**
- Consumes: `Annotation`、`AnnotationKind`、`RectangleAnnotation` 等（Task 5）
- Produces:
  - `public interface IAnnotationTool { AnnotationKind Kind { get; } bool IsDrawing { get; } void Begin(Point p); void Move(Point p); Annotation? GetPreview(); Annotation? End(); }`（坐标相对选区，物理像素；`GetPreview` 返回当前未提交的临时标注用于实时预览，未开始或不足一帧返回 null）
  - `public static class AnnotationToolFactory { public static IAnnotationTool Create(AnnotationKind kind, Color color, double thickness); }`
  - `public static class ArrowGeometry { public static (Point Tip, Point Left, Point Right) ComputeHead(Point start, Point end, double headLength = 12, double headAngleDeg = 30); }`

- [ ] **Step 1: 写失败测试**

`tests/Glyphtap.Tests/AnnotationToolsTests.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class AnnotationToolsTests
{
    private const double T = 0.01;

    [Fact]
    public void 矩形工具_拖拽产出归一化矩形()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(50, 60));
        var a = tool.End();
        var r = Assert.IsType<RectangleAnnotation>(a);
        Assert.Equal(new Rect(50, 60, 50, 40), r.Rect);
        Assert.Equal(Colors.Red, r.Color);
        Assert.Equal(3, r.Thickness);
    }

    [Fact]
    public void 矩形工具_尺寸过小返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(100, 100));
        Assert.Null(tool.End());
    }

    [Fact]
    public void 椭圆工具_产出椭圆()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Ellipse, Colors.Blue, 5);
        tool.Begin(new Point(0, 0));
        tool.Move(new Point(100, 80));
        var e = Assert.IsType<EllipseAnnotation>(tool.End());
        Assert.Equal(new Rect(0, 0, 100, 80), e.Rect);
    }

    [Fact]
    public void 箭头几何_尖在终点且两翼对称()
    {
        var (tip, left, right) = ArrowGeometry.ComputeHead(new Point(0, 0), new Point(100, 0), 12, 30);
        Assert.Equal(new Point(100, 0), tip);
        Assert.Equal(100, right.X, T);
        Assert.Equal(100, left.X, T);
        Assert.Equal(-Math.Tan(30 * Math.PI / 180) * 12, right.Y, T);
        Assert.Equal(+Math.Tan(30 * Math.PI / 180) * 12, left.Y, T);
    }

    [Fact]
    public void 箭头工具_横向箭头()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Arrow, Colors.Green, 2);
        tool.Begin(new Point(10, 10));
        tool.Move(new Point(200, 10));
        var a = Assert.IsType<ArrowAnnotation>(tool.End());
        Assert.Equal(new Point(10, 10), a.Start);
        Assert.Equal(new Point(200, 10), a.End);
    }

    [Fact]
    public void 画笔工具_累积点集()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Pen, Colors.Black, 1);
        tool.Begin(new Point(1, 1));
        tool.Move(new Point(2, 2));
        tool.Move(new Point(3, 3));
        var pen = Assert.IsType<PenAnnotation>(tool.End());
        Assert.Equal(3, pen.Points.Count);
    }

    [Fact]
    public void 画笔工具_点不足返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Pen, Colors.Black, 1);
        tool.Begin(new Point(1, 1));
        Assert.Null(tool.End());
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现工具**

`src/Glyphtap/Capture/AnnotationTools.cs`：

```csharp
using System.Windows;
using System.Windows.Media;

namespace Glyphtap.Capture;

/// <summary>标注工具：一次拖拽的交互协议，产出 Annotation（null 表示放弃）。坐标相对选区。</summary>
public interface IAnnotationTool
{
    AnnotationKind Kind { get; }
    bool IsDrawing { get; }
    void Begin(Point p);
    void Move(Point p);
    Annotation? GetPreview();
    Annotation? End();
}

public static class AnnotationToolFactory
{
    public static IAnnotationTool Create(AnnotationKind kind, Color color, double thickness) => kind switch
    {
        AnnotationKind.Rectangle => new RectangleTool(color, thickness),
        AnnotationKind.Ellipse => new EllipseTool(color, thickness),
        AnnotationKind.Arrow => new ArrowTool(color, thickness),
        AnnotationKind.Pen => new PenTool(color, thickness),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>箭头几何：尖在终点，两翼自终点横向对称张开 headAngle 度。</summary>
public static class ArrowGeometry
{
    public static (Point Tip, Point Left, Point Right) ComputeHead(Point start, Point end, double headLength = 12, double headAngleDeg = 30)
    {
        var v = end - start;
        var len = v.Length;
        if (len < 1e-9)
            return (end, end, end);
        var dir = v / len;
        var angle = headAngleDeg * Math.PI / 180;
        var wing = headLength * Math.Tan(angle);
        var perp = new Vector(-dir.Y, dir.X) * wing;
        return (end, end + perp, end - perp);
    }
}

internal abstract class ToolBase : IAnnotationTool
{
    public abstract AnnotationKind Kind { get; }
    protected readonly Color Color;
    protected readonly double Thickness;
    protected Point Start;
    protected Point Last;
    public bool IsDrawing { get; protected set; }

    protected ToolBase(Color color, double thickness) { Color = color; Thickness = thickness; }

    public virtual void Begin(Point p)
    {
        Start = p;
        Last = p;
        IsDrawing = true;
    }

    public virtual void Move(Point p) => Last = p;

    /// <summary>当前未提交标注的预览（未绘制时 null）。</summary>
    public virtual Annotation? GetPreview() => IsDrawing ? BuildAnnotation(isPreview: true) : null;

    /// <summary>根据当前状态构造标注；isPreview=true 时用于实时预览。</summary>
    protected abstract Annotation? BuildAnnotation(bool isPreview);

    public Annotation? End()
    {
        if (!IsDrawing)
            return null;
        var a = BuildAnnotation(isPreview: false);
        IsDrawing = false;
        return a;
    }

    protected static Rect Normalize(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Rect(x, y, Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    }
}

internal sealed class RectangleTool : ToolBase
{
    public RectangleTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Rectangle;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new RectangleAnnotation { Rect = r, Color = Color, Thickness = Thickness };
    }
}

internal sealed class EllipseTool : ToolBase
{
    public EllipseTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Ellipse;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new EllipseAnnotation { Rect = r, Color = Color, Thickness = Thickness };
    }
}

internal sealed class ArrowTool : ToolBase
{
    public ArrowTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Arrow;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        return (Start - Last).Length < 1 ? null : new ArrowAnnotation { Start = Start, End = Last, Color = Color, Thickness = Thickness };
    }
}

internal sealed class PenTool : ToolBase
{
    public PenTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Pen;
    private readonly List<Point> _points = new();

    public override void Begin(Point p)
    {
        base.Begin(p);
        _points.Clear();
        _points.Add(p);
    }

    public override void Move(Point p)
    {
        base.Move(p);
        _points.Add(p);
    }

    public override Annotation? GetPreview() => IsDrawing ? BuildAnnotation(isPreview: true) : null;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        if (!isPreview && _points.Count < 2)
        {
            _points.Clear();
            return null;
        }
        var pen = new PenAnnotation { Color = Color, Thickness = Thickness };
        foreach (var p in _points)
            pen.AddPoint(p);
        return pen;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过（7 个新测试）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 标注工具四件套（矩形/椭圆/箭头/画笔）"
```

---

### Task 7: 屏幕捕获 ScreenCaptureService（分屏捕获 + 拼接）

**Files:**
- Create: `src/Glyphtap/Services/ScreenCaptureService.cs`
- Test: `tests/Glyphtap.Tests/ScreenCaptureServiceTests.cs`

**Interfaces:**
- Consumes: `ScreenLayout`、`MonitorInfo`、`MonitorEnumerator`（Task 2/3）
- Produces:
  - `public sealed record ScreenCaptureResult(System.Drawing.Bitmap Bitmap, ScreenLayout Layout)`
  - `public static class ScreenCaptureService`：
    - `public static ScreenCaptureResult Capture()` — 对每屏以该屏 DPI 上下文捕获（`CopyFromScreen`），按虚拟屏幕坐标拼接，返回物理像素整图
    - `public static System.Drawing.Bitmap Stitch(IReadOnlyList<(Rect Dest, Bitmap Src)> parts, Rect virtualBounds)` — 拼接（纯逻辑，可测试）

- [ ] **Step 1: 写失败测试（Stitch 纯逻辑）**

`tests/Glyphtap.Tests/ScreenCaptureServiceTests.cs`：

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using Glyphtap.Services;
using Xunit;

namespace Glyphtap.Tests;

public class ScreenCaptureServiceTests
{
    private static Bitmap Solid(Color c, int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(c);
        return bmp;
    }

    [Fact]
    public void Stitch_按虚拟坐标拼接两块()
    {
        var left = Solid(Color.Red, 100, 100);
        var right = Solid(Color.Blue, 50, 100);

        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(0, 0, 100, 100), left), (new Rect(100, 0, 50, 100), right) },
            new Rect(0, 0, 150, 100));

        Assert.Equal(150, result.Width);
        Assert.Equal(100, result.Height);
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(50, 50).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(120, 50).ToArgb());
    }

    [Fact]
    public void Stitch_支持负坐标源()
    {
        var red = Solid(Color.Red, 50, 50);
        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(-50, -20, 50, 50), red) },
            new Rect(-50, -20, 50, 50));
        Assert.Equal(Color.Red.ToArgb(), result.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Stitch_空白处为透明()
    {
        var red = Solid(Color.Red, 10, 10);
        using var result = ScreenCaptureService.Stitch(
            new[] { (new Rect(0, 0, 10, 10), red) },
            new Rect(0, 0, 20, 20));
        Assert.Equal(0, result.GetPixel(15, 15).A); // GDI+ 透明填充为 alpha=0，不比较 Color.Transparent 的 RGB 常量
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现 ScreenCaptureService**

`src/Glyphtap/Services/ScreenCaptureService.cs`：

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using Glyphtap.Capture;

namespace Glyphtap.Services;

public sealed record ScreenCaptureResult(Bitmap Bitmap, ScreenLayout Layout);

/// <summary>
/// 屏幕捕获：对每台显示器以各自 DPI 捕获后拼接为虚拟屏幕整图（物理像素）。
/// 捕获失败抛 InvalidOperationException，由调用方提示并退出截图模式。
/// </summary>
public static class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    private static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    public static ScreenCaptureResult Capture()
    {
        var layout = ScreenLayout.Create(MonitorEnumerator.Enumerate());
        var parts = new List<(Rect Dest, Bitmap Src)>();

        foreach (var monitor in layout.Monitors)
        {
            var prev = SetThreadDpiAwarenessContext(PerMonitorV2);
            try
            {
                var b = monitor.Bounds;
                var bmp = new Bitmap((int)b.Width, (int)b.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen((int)b.X, (int)b.Y, 0, 0, new System.Drawing.Size((int)b.Width, (int)b.Height));
                }
                parts.Add((new Rect(b.X, b.Y, b.Width, b.Height), bmp));
            }
            finally
            {
                SetThreadDpiAwarenessContext(prev);
            }
        }

        try
        {
            return new ScreenCaptureResult(Stitch(parts, layout.VirtualBounds), layout);
        }
        catch
        {
            foreach (var (_, bmp) in parts)
                bmp.Dispose();
            throw;
        }
    }

    /// <summary>按虚拟屏幕坐标把各屏位图拼接为整图（纯逻辑，可测试）。</summary>
    public static Bitmap Stitch(IReadOnlyList<(Rect Dest, Bitmap Src)> parts, Rect virtualBounds)
    {
        var w = (int)virtualBounds.Width;
        var h = (int)virtualBounds.Height;
        var canvas = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(canvas))
        {
            g.Clear(Color.Transparent);
            foreach (var (dest, src) in parts)
            {
                var x = (int)(dest.X - virtualBounds.X);
                var y = (int)(dest.Y - virtualBounds.Y);
                g.DrawImageUnscaled(src, x, y);
            }
        }
        return canvas;
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过（3 个新测试）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 屏幕捕获（分屏 DPI 捕获 + 虚拟屏幕拼接）"
```

---

### Task 8: 标注渲染 + 合成 CaptureComposer + 剪贴板服务

**Files:**
- Create: `src/Glyphtap/Capture/AnnotationRenderer.cs`
- Create: `src/Glyphtap/Capture/CaptureComposer.cs`
- Create: `src/Glyphtap/Services/ClipboardService.cs`
- Test: `tests/Glyphtap.Tests/ComposerAndClipboardTests.cs`

**Interfaces:**
- Consumes: `Annotation` 各类型（Task 5）、`ScreenLayout`（Task 2）
- Produces:
  - `public static class AnnotationRenderer { public static void Draw(DrawingContext dc, Annotation a); }`（把标注按相对坐标绘制到 DrawingContext）
  - `public static class CaptureComposer { public static BitmapSource Compose(BitmapSource fullScreen, Rect selectionPhysical, IReadOnlyList<Annotation> annotations); }`（裁剪选区 + 合并标注 → 物理像素位图，必须在 STA 线程调用）
  - `public static class ClipboardService { public static void SetImage(BitmapSource image); public static void SetText(string text); public static byte[] EncodePng(BitmapSource image); }`（SetImage/SetText 需 STA）
  - 类型转换辅助：`public static BitmapSource ToBitmapSource(Bitmap gdiBitmap)`（GDI+ → WPF，放 ScreenCaptureService 或独立 `BitmapConvert`，放 `src/Glyphtap/Services/BitmapConvert.cs`）

- [ ] **Step 1: 写失败测试（StaFact）**

`tests/Glyphtap.Tests/ComposerAndClipboardTests.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glyphtap.Capture;
using Glyphtap.Services;
using Xunit;
using Xunit.StaFact;

namespace Glyphtap.Tests;

public class ComposerAndClipboardTests
{
    /// <summary>生成纯色 BitmapSource。</summary>
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
    public void Compose_输出尺寸等于选区物理像素()
    {
        var full = Solid(Colors.White, 200, 200);
        var selection = new Rect(50, 50, 100, 60);
        var result = CaptureComposer.Compose(full, selection, Array.Empty<Annotation>());
        Assert.Equal(100, result.PixelWidth);
        Assert.Equal(60, result.PixelHeight);
    }

    [StaFact]
    public void Compose_背景裁剪正确_取到选区像素()
    {
        var full = Solid(Colors.Red, 200, 200);
        var result = CaptureComposer.Compose(full, new Rect(10, 20, 50, 40), Array.Empty<Annotation>());
        var pixels = new byte[50 * 40 * 4];
        result.CopyPixels(pixels, 50 * 4, 0);
        Assert.Equal(Colors.Red.B, pixels[0]);
        Assert.Equal(Colors.Red.G, pixels[1]);
        Assert.Equal(Colors.Red.R, pixels[2]);
    }

    [StaFact]
    public void Compose_标注绘制在选区上()
    {
        var full = Solid(Colors.White, 100, 100);
        var rect = new RectangleAnnotation { Rect = new Rect(10, 10, 30, 30), Color = Colors.Blue, Thickness = 5 };
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 100, 100), new Annotation[] { rect });
        var pixels = new byte[100 * 100 * 4];
        result.CopyPixels(pixels, 100 * 4, 0);
        // 矩形顶边中点 (25, 10)：y=10 为 5px 边框中心线，避免落在边框边缘（反走样 50% 混合）
        var idx = (10 * 100 + 25) * 4;
        Assert.Equal(Colors.Blue.B, pixels[idx]);
        Assert.Equal(Colors.Blue.G, pixels[idx + 1]);
        Assert.Equal(Colors.Blue.R, pixels[idx + 2]);
    }

    [StaFact]
    public void Compose_超界标注被裁剪()
    {
        var full = Solid(Colors.White, 100, 100);
        // 画笔从选区内 (90,90) 延伸到选区外 (150,150)：选区内的部分 (90,90)-(100,100) 应可见
        var pen = new PenAnnotation { Color = Colors.Black, Thickness = 4 };
        pen.AddPoint(new Point(90, 90));
        pen.AddPoint(new Point(150, 150));
        var result = CaptureComposer.Compose(full, new Rect(0, 0, 100, 100), new Annotation[] { pen });
        var pixels = new byte[100 * 100 * 4];
        result.CopyPixels(pixels, 100 * 4, 0);
        // 对角线上、位于选区内/裁剪区内的 (95,95) 应见黑色笔迹
        var idx = (95 * 100 + 95) * 4;
        Assert.Equal(0, pixels[idx]);
        Assert.Equal(0, pixels[idx + 1]);
        Assert.Equal(0, pixels[idx + 2]);
        // 远离线段 (10,0) 处为白色背景
        var far = (0 * 100 + 10) * 4;
        Assert.Equal(255, pixels[far]);
        Assert.Equal(255, pixels[far + 1]);
        Assert.Equal(255, pixels[far + 2]);
    }

    [StaFact]
    public void EncodePng_产出合法PNG头()
    {
        var png = ClipboardService.EncodePng(Solid(Colors.Green, 8, 8));
        Assert.True(png.Length > 0);
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.Equal((byte)'N', png[2]);
        Assert.Equal((byte)'G', png[3]);
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: FAIL（编译失败）

- [ ] **Step 3: 实现渲染器、合成器、剪贴板服务**

`src/Glyphtap/Capture/AnnotationRenderer.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glyphtap.Capture;

/// <summary>把标注绘制到 DrawingContext（坐标相对选区左上角，物理像素）。</summary>
public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, Annotation a)
    {
        var pen = new Pen(new SolidColorBrush(a.Color), a.Thickness) { LineJoin = PenLineJoin.Round };
        switch (a)
        {
            case RectangleAnnotation r:
                dc.DrawRectangle(null, pen, r.Rect);
                break;
            case EllipseAnnotation e:
                dc.DrawEllipse(null, pen, new Point(e.Rect.X + e.Rect.Width / 2, e.Rect.Y + e.Rect.Height / 2), e.Rect.Width / 2, e.Rect.Height / 2);
                break;
            case ArrowAnnotation ar:
            {
                var (tip, left, right) = ArrowGeometry.ComputeHead(ar.Start, ar.End);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(ar.Start, false, false);
                    ctx.LineTo(ar.End, true, false);
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(left, true, true);
                    ctx.LineTo(right, true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
            case PenAnnotation penA:
            {
                if (penA.Points.Count < 2)
                    break;
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(penA.Points[0], false, false);
                    ctx.PolyLineTo(penA.Points.Skip(1).ToList(), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
        }
    }
}
```

`src/Glyphtap/Capture/CaptureComposer.cs`：

```csharp
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Glyphtap.Capture;

/// <summary>
/// 合成最终截图：裁剪选区背景 + 合并标注，输出物理像素位图。
/// 背景图为整块虚拟屏幕位图（BitmapSource，物理像素）；selectionPhysical 为物理像素选区。
/// 必须由 STA 线程调用。
/// </summary>
public static class CaptureComposer
{
    public static BitmapSource Compose(BitmapSource fullScreen, Rect selectionPhysical, IReadOnlyList<Annotation> annotations)
    {
        var w = (int)Math.Ceiling(selectionPhysical.Width);
        var h = (int)Math.Ceiling(selectionPhysical.Height);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();

        using (var dc = dv.RenderOpen())
        {
            // 先裁剪到选区矩形，再绘制背景与标注，超界内容不显示
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

            // 背景：把整图平移到选区对齐（选区左上角 → 0,0）
            dc.DrawImage(fullScreen, new Rect(
                -(selectionPhysical.X),
                -(selectionPhysical.Y),
                fullScreen.PixelWidth,
                fullScreen.PixelHeight));

            // 标注（相对选区坐标），超界部分被 PushClip 裁掉
            foreach (var a in annotations)
                AnnotationRenderer.Draw(dc, a);

            dc.Pop();
        }

        rtb.Render(dv);
        return rtb;
    }
}
```

`src/Glyphtap/Services/BitmapConvert.cs`：

```csharp
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Glyphtap.Services;

public static class BitmapConvert
{
    /// <summary>GDI+ 位图 → WPF BitmapSource（内存拷贝，支持 32bppArgb）。</summary>
    public static BitmapSource ToBitmapSource(Bitmap bmp)
    {
        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        var data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var stride = data.Stride;
            var bytes = new byte[stride * data.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            return BitmapSource.Create(data.Width, data.Height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null, bytes, stride);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }
}
```

`src/Glyphtap/Services/ClipboardService.cs`：

```csharp
using System.IO;
using System.Windows.Media.Imaging;

namespace Glyphtap.Services;

/// <summary>剪贴板服务。MVP：图片；接口同时支持文本（V2 OCR 复用）。须由 STA 线程调用。</summary>
public static class ClipboardService
{
    public static void SetImage(BitmapSource image)
    {
        System.Windows.Clipboard.SetImage(image);
    }

    public static void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }

    /// <summary>编码为 PNG 字节流（供测试与临时文件保存）。</summary>
    public static byte[] EncodePng(BitmapSource image)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
```

- [ ] **Step 4: 运行测试确认通过**

Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过（5 个新测试）

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 标注渲染/截图合成/剪贴板服务"
```

---

### Task 9: CaptureWindow 骨架 — 全屏窗口 + 背景 + 选区交互 + 完成/取消

**Files:**
- Create: `src/Glyphtap/Capture/CaptureWindow.xaml`、`CaptureWindow.xaml.cs`
- Modify: 删除模板生成的 `src/Glyphtap/MainWindow.xaml`（可选，Task 11 装配时处理，本任务先保留）
- Test: 手动（自动测试覆盖：`SelectionLogic` 状态机与 `CaptureComposer` 已在 Task 4/8 测试；本任务验证窗口层集成）

**Interfaces:**
- Consumes: `ScreenCaptureResult`（Task 7）、`ScreenLayout`、`SelectionLogic`（Task 4）、`CaptureComposer`（Task 8）、`ClipboardService`（Task 8）
- Produces:
  - `public sealed class CaptureWindow : Window`：
    - `public static CaptureWindow Open(ScreenCaptureResult capture, Action<BitmapSource> onComplete, Action onCancel)` — 打开全屏截图窗口
    - `public bool IsOpen { get; }`（防重入，Task 11 使用）
  - 行为：覆盖虚拟屏幕（DIP 尺寸 = 物理尺寸 / PrimaryScale，Left/Top 可为负）；背景暗化截图；选区交互（创建/移动/手柄缩放，物理像素换算）；`Enter`=完成并合成（背景+标注=仅背景，本任务标注层为空）→ `onComplete`；`Esc`/右键 = `onCancel`

- [ ] **Step 1: 编写窗口 XAML**

`src/Glyphtap/Capture/CaptureWindow.xaml`：

```xml
<Window x:Class="Glyphtap.Capture.CaptureWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        WindowStyle="None" ResizeMode="NoResize" ShowInTaskbar="False"
        Topmost="True" Background="Black" Focusable="True">
    <Grid x:Name="RootGrid">
        <Image x:Name="BackgroundImage" Stretch="Fill" Opacity="0.35" IsHitTestVisible="False" />
        <Canvas x:Name="OverlayCanvas" />
        <Canvas x:Name="AnnotationCanvas" IsHitTestVisible="False" />
    </Grid>
</Window>
```

- [ ] **Step 2: 编写代码隐藏（骨架版，标注层留空）**

`src/Glyphtap/Capture/CaptureWindow.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Glyphtap.Services;

namespace Glyphtap.Capture;

/// <summary>全屏截图窗口：背景暗化 + 选区交互 + 完成/取消。</summary>
public sealed partial class CaptureWindow : Window
{
    private readonly ScreenCaptureResult _capture;
    private readonly Action<BitmapSource> _onComplete;
    private readonly Action _onCancel;
    private readonly SelectionLogic _selection = new();
    private readonly List<System.Windows.Shapes.Rectangle> _maskParts = new();
    private System.Windows.Shapes.Rectangle _selectionVisual = null!;
    private readonly List<System.Windows.Shapes.Rectangle> _handles = new();

    public bool IsOpen { get; private set; } = true;

    private CaptureWindow(ScreenCaptureResult capture, Action<BitmapSource> onComplete, Action onCancel)
    {
        _capture = capture;
        _onComplete = onComplete;
        _onCancel = onCancel;

        InitializeComponent();
        BackgroundImage.Source = BitmapConvert.ToBitmapSource(capture.Bitmap);

        // 窗口覆盖虚拟屏幕（DIP = 物理 / PrimaryScale，Left/Top 可为负）
        var layout = capture.Layout;
        var vb = layout.VirtualBounds;
        Left = vb.X / layout.PrimaryScale;
        Top = vb.Y / layout.PrimaryScale;
        Width = vb.Width / layout.PrimaryScale;
        Height = vb.Height / layout.PrimaryScale;

        BuildMask();
        BuildSelectionVisual();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseRightButtonUp += (_, _) => Cancel();
    }

    /// <summary>打开截图窗口并强制激活。</summary>
    public static CaptureWindow Open(ScreenCaptureResult capture, Action<BitmapSource> onComplete, Action onCancel)
    {
        var w = new CaptureWindow(capture, onComplete, onCancel);
        w.Show();
        w.Activate();
        return w;
    }

    private void BuildMask()
    {
        // 四块遮罩矩形，选区变化时更新位置
        for (var i = 0; i < 4; i++)
        {
            var r = new System.Windows.Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)),
                IsHitTestVisible = false,
            };
            _maskParts.Add(r);
            OverlayCanvas.Children.Add(r);
        }
    }

    private void BuildSelectionVisual()
    {
        _selectionVisual = new System.Windows.Shapes.Rectangle
        {
            Stroke = Brushes.Cyan,
            StrokeThickness = 1,
            StrokeDashArray = new DoubleCollection { 4, 3 },
            Fill = new SolidColorBrush(Color.FromArgb(30, 0, 180, 255)),
            IsHitTestVisible = false,
        };
        OverlayCanvas.Children.Add(_selectionVisual);

        var brush = new SolidColorBrush(Colors.White);
        for (var i = 0; i < 8; i++)
        {
            var h = new System.Windows.Shapes.Rectangle { Width = 8, Height = 8, Fill = brush, IsHitTestVisible = false };
            _handles.Add(h);
            OverlayCanvas.Children.Add(h);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Complete();
        else if (e.Key == Key.Escape)
            Cancel();
    }

    // ---- 鼠标交互：DIP → 物理像素 → SelectionLogic ----

    private Point ToPhysical(Point windowPoint)
        => _capture.Layout.ToPhysical(windowPoint);

    private Point ToWindowDips(Point physical)
        => _capture.Layout.ToWindowDips(physical);

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        var p = ToPhysical(e.GetPosition(this));
        _selection.OnMouseDown(p);
        RootGrid.CaptureMouse();
        UpdateSelectionVisual();
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        var p = ToPhysical(e.GetPosition(this));
        _selection.OnMouseMove(p);
        UpdateSelectionVisual();
    }

    private void RootGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _selection.OnMouseUp();
        RootGrid.ReleaseMouseCapture();
        UpdateSelectionVisual();
    }

    private void UpdateSelectionVisual()
    {
        var s = _selection.Selection;
        if (s.IsEmpty)
        {
            _selectionVisual.Visibility = Visibility.Collapsed;
            foreach (var h in _handles)
                h.Visibility = Visibility.Collapsed;
            foreach (var m in _maskParts)
                m.Visibility = Visibility.Collapsed;
            return;
        }

        var d = ToWindowDips(new Point(s.X, s.Y));
        var size = new Size(s.Width / _capture.Layout.PrimaryScale, s.Height / _capture.Layout.PrimaryScale);
        Canvas.SetLeft(_selectionVisual, d.X);
        Canvas.SetTop(_selectionVisual, d.Y);
        _selectionVisual.Width = size.Width;
        _selectionVisual.Height = size.Height;
        _selectionVisual.Visibility = Visibility.Visible;

        // 手柄（物理 8px → DIP）
        var hSize = 8 / _capture.Layout.PrimaryScale;
        var pts = new[]
        {
            new Point(s.X, s.Y), new Point(s.X + s.Width / 2, s.Y), new Point(s.X + s.Width, s.Y),
            new Point(s.X + s.Width, s.Y + s.Height / 2), new Point(s.X + s.Width, s.Y + s.Height),
            new Point(s.X + s.Width / 2, s.Y + s.Height), new Point(s.X, s.Y + s.Height),
            new Point(s.X, s.Y + s.Height / 2),
        };
        for (var i = 0; i < 8; i++)
        {
            var hp = ToWindowDips(pts[i]);
            Canvas.SetLeft(_handles[i], hp.X - hSize / 2);
            Canvas.SetTop(_handles[i], hp.Y - hSize / 2);
            _handles[i].Width = hSize;
            _handles[i].Height = hSize;
            _handles[i].Visibility = Visibility.Visible;
        }

        // 遮罩：上 / 下 / 左 / 右 四块
        var winW = Width * _capture.Layout.PrimaryScale;
        var winH = Height * _capture.Layout.PrimaryScale;
        UpdateMask(0, 0, 0, s.X, winH);                          // 左
        UpdateMask(1, s.X + s.Width, 0, winW - s.X - s.Width, winH); // 右
        UpdateMask(2, s.X, 0, s.Width, s.Y);                     // 上
        UpdateMask(3, s.X, s.Y + s.Height, s.Width, winH - s.Y - s.Height); // 下
        foreach (var m in _maskParts)
            m.Visibility = Visibility.Visible;
    }

    private void UpdateMask(int index, double x, double y, double w, double h)
    {
        var d = ToWindowDips(new Point(x, y));
        Canvas.SetLeft(_maskParts[index], d.X);
        Canvas.SetTop(_maskParts[index], d.Y);
        _maskParts[index].Width = Math.Max(0, w / _capture.Layout.PrimaryScale);
        _maskParts[index].Height = Math.Max(0, h / _capture.Layout.PrimaryScale);
    }

    private void Complete()
    {
        if (!_selection.HasSelection)
            return;
        IsOpen = false;
        var composed = CaptureComposer.Compose(
            BitmapConvert.ToBitmapSource(_capture.Bitmap),
            _selection.Selection,
            Array.Empty<Annotation>()); // 本任务无标注；Task 10 传入 AnnotationManager.Items
        Close();
        _onComplete(composed);
    }

    private void Cancel()
    {
        IsOpen = false;
        Close();
        _onCancel();
    }
}
```

- [ ] **Step 3: 接线鼠标事件**

在 `CaptureWindow` 构造函数中 `InitializeComponent()` 后补充：

```csharp
RootGrid.MouseDown += RootGrid_MouseDown;
RootGrid.MouseMove += RootGrid_MouseMove;
RootGrid.MouseUp += RootGrid_MouseUp;
```

- [ ] **Step 4: 构建**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error

- [ ] **Step 5: 手动验证（当前程序入口仍为 MainWindow，需临时入口）**

在 `App.xaml.cs` 中临时把启动逻辑改为：启动即打开 `CaptureWindow.Open(ScreenCaptureService.Capture(), onComplete: img => ClipboardService.SetImage(img), onCancel: () => { })`（可先放 Task 11 再删临时代码；本任务以编译通过 + 快速手测为准）。

手动验证项：
- 启动后出现暗化全屏窗口，覆盖所有显示器
- 拖拽可创建虚线选区，可拖动/手柄缩放
- Enter 完成 → 剪贴板出现选区原图（无标注）；Esc / 右键取消
- 双屏（若可切换）下坐标无偏移

- [ ] **Step 6: 提交**

```bash
git add -A
git commit -m "feat: 截图窗口骨架（背景/遮罩/选区交互/完成取消）"
```

---

### Task 10: 标注层渲染 + 工具栏 + 合成含标注

**Files:**
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml`（加工具栏）
- Modify: `src/Glyphtap/Capture/CaptureWindow.xaml.cs`
- Test: 手动（标注逻辑已有单测；此任务验证 UI 接线与合成含标注）

**Interfaces:**
- Consumes: `IAnnotationTool`/`AnnotationToolFactory`（Task 6）、`AnnotationManager`（Task 5）、`AnnotationRenderer`（Task 8）
- Produces: CaptureWindow 内完整交互闭环（工具绘制、选中删除、清除、工具栏、合成含标注）

- [ ] **Step 1: XAML 加工具栏**

`CaptureWindow.xaml` 中 `Grid` 内追加（底部居中浮层）：

```xml
        <Border x:Name="Toolbar"
                Background="#E621252B"
                CornerRadius="6"
                Padding="8,6"
                VerticalAlignment="Bottom"
                HorizontalAlignment="Center"
                Margin="0,0,0,16"
                Visibility="Collapsed">
            <StackPanel Orientation="Horizontal">
                <Button x:Name="BtnRect" Content="矩形" Tag="Rectangle" Click="Tool_OnClick" Margin="2,0" />
                <Button x:Name="BtnEllipse" Content="椭圆" Tag="Ellipse" Click="Tool_OnClick" Margin="2,0" />
                <Button x:Name="BtnArrow" Content="箭头" Tag="Arrow" Click="Tool_OnClick" Margin="2,0" />
                <Button x:Name="BtnPen" Content="画笔" Tag="Pen" Click="Tool_OnClick" Margin="2,0" />
                <Separator Width="1" Background="Gray" Margin="6,2" />
                <Button x:Name="BtnRed" Content="" Background="#FF3B30" Tag="#FFFF3B30" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Button x:Name="BtnYellow" Content="" Background="#FFCC00" Tag="#FFFFCC00" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Button x:Name="BtnGreen" Content="" Background="#34C759" Tag="#FF34C759" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Button x:Name="BtnBlue" Content="" Background="#007AFF" Tag="#FF007AFF" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Button x:Name="BtnBlack" Content="" Background="#1C1C1E" Tag="#FF1C1C1E" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Button x:Name="BtnWhite" Content="" Background="#FFFFFF" Tag="#FFFFFFFF" Click="Color_OnClick" Margin="2,0" Width="18" Height="18" />
                <Separator Width="1" Background="Gray" Margin="6,2" />
                <Button x:Name="BtnThin" Content="细" Tag="1" Click="Thickness_OnClick" Margin="2,0" />
                <Button x:Name="BtnMedium" Content="中" Tag="3" Click="Thickness_OnClick" Margin="2,0" />
                <Button x:Name="BtnThick" Content="粗" Tag="5" Click="Thickness_OnClick" Margin="2,0" />
                <Separator Width="1" Background="Gray" Margin="6,2" />
                <Button x:Name="BtnClear" Content="清除" Click="Clear_OnClick" Margin="2,0" />
                <Button x:Name="BtnCancel" Content="✗" Click="CancelBtn_OnClick" Margin="2,0" />
                <Button x:Name="BtnDone" Content="✓" Click="DoneBtn_OnClick" Margin="2,0" FontWeight="Bold" />
            </StackPanel>
        </Border>
```

- [ ] **Step 2: 代码隐藏扩展**

在 `CaptureWindow.xaml.cs` 中补充：

```csharp
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

    private readonly AnnotationManager _annotations = new();
    private IAnnotationTool _tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
    private AnnotationKind _currentKind = AnnotationKind.Rectangle;
    private Color _color = Colors.Red;
    private double _thickness = 3;
    private bool _draggingAnnotation;
    private Point _dragLast;

    // 在 UpdateSelectionVisual 显示选区时调用，显示工具栏：
    private void ShowToolbar() => Toolbar.Visibility = Visibility.Visible;
```

在 `UpdateSelectionVisual` 中，选区非空（`_selection.HasSelection`）时调用 `ShowToolbar()`。

> 注意：本任务（Task 10）中 `RootGrid_MouseDown/Move/Up` 以及 `Complete` 是**覆盖** Task 9 中同名方法的完整版本，直接以本任务的实现为准替换，事件绑定保持不变（Task 9 Step 3 已注册）。`UpdateSelectionVisual` 保持 Task 9 实现不动，仅追加 `ShowToolbar()` 调用。

**绘制与选中交互**（新增鼠标逻辑，替换 Task 9 的纯选区逻辑——保留移动/缩放；选区创建完成后进入"工具模式"）：

```csharp
    // 绝对物理坐标 → 选区相对坐标（标注存储基准）
    private Point ToRelative(Point abs)
    {
        var s = _selection.Selection;
        return new Point(abs.X - s.X, abs.Y - s.Y);
    }

    private void RootGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        // 工具栏按钮点击会冒泡到此，忽略以保护工具栏交互
        if (Toolbar.Visibility == Visibility.Visible && IsInToolbar(e.OriginalSource))
            return;

        var p = ToPhysical(e.GetPosition(this));

        if (_tool.IsDrawing)
            return;

        // 已有选区：先命中标注 → 选中并移动；否则交给选区逻辑（手柄/移动/重新创建）
        if (_selection.HasSelection)
        {
            if (_annotations.TrySelectAt(ToRelative(p), 6))
            {
                _draggingAnnotation = true;
                _dragLast = p;
                RootGrid.CaptureMouse();
                return;
            }
            // 点在选区内且未命中标注，进入绘制模式
            var handle = SelectionLogic.HitTestHandle(p, _selection.Selection);
            if (handle == ResizeHandle.None && _selection.Selection.Contains(p))
            {
                _tool.Begin(ToRelative(p));
                RootGrid.CaptureMouse();
                return;
            }
        }

        _selection.OnMouseDown(p);
        RootGrid.CaptureMouse();
        UpdateSelectionVisual();
    }

    // 判断事件源是否位于工具栏控件树内
    private bool IsInToolbar(object source)
    {
        for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d == Toolbar)
                return true;
        }
        return false;
    }
```

> 提示：`_draggingAnnotation` 与 `_dragLast` 需声明为字段。绘制与标注拖动的 Move/Up 处理：

```csharp
    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        var p = ToPhysical(e.GetPosition(this));

        if (_tool.IsDrawing)
        {
            _tool.Move(ToRelative(p));
            RenderLiveDrawing();
            return;
        }
        if (_draggingAnnotation)
        {
            _annotations.MoveSelectedBy(new Vector(p.X - _dragLast.X, p.Y - _dragLast.Y));
            _dragLast = p;
            RenderAnnotations();
            return;
        }

        _selection.OnMouseMove(p);
        UpdateSelectionVisual();
    }

    private void RootGrid_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_tool.IsDrawing)
        {
            var a = _tool.End();
            if (a != null)
                _annotations.Add(a);
            RenderAnnotations();
            RootGrid.ReleaseMouseCapture();
            return;
        }
        if (_draggingAnnotation)
        {
            _draggingAnnotation = false;
            RootGrid.ReleaseMouseCapture();
            return;
        }

        _selection.OnMouseUp();
        RootGrid.ReleaseMouseCapture();
        UpdateSelectionVisual();
    }
```

**渲染辅助**（标注画布 = 窗口 DIP 坐标，物理像素 → DIP 换算）：

```csharp
    // 选区相对坐标 → 窗口 DIP 坐标（渲染用）
    private Point ToWindowDipsRelative(Point rel)
    {
        var s = _selection.Selection;
        return ToWindowDips(new Point(s.X + rel.X, s.Y + rel.Y));
    }

    private void RenderAnnotations()
    {
        AnnotationCanvas.Children.Clear();
        foreach (var a in _annotations.Items)
            AnnotationCanvas.Children.Add(AnnotationElement(a));

        // 实时预览：当前工具的进行中标注
        var preview = _tool.GetPreview();
        if (preview != null)
            AnnotationCanvas.Children.Add(AnnotationElement(preview));
    }

    private void RenderLiveDrawing() => RenderAnnotations();

    private FrameworkElement AnnotationElement(Annotation a)
    {
        var scale = _capture.Layout.PrimaryScale;
        var strokeThickness = a.Thickness / scale;

        switch (a)
        {
            case RectangleAnnotation r:
            {
                var shape = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(a.Color),
                    StrokeThickness = strokeThickness,
                    Fill = null,
                    Width = r.Rect.Width / scale,
                    Height = r.Rect.Height / scale,
                };
                var d = ToWindowDipsRelative(r.Rect.Location);
                Canvas.SetLeft(shape, d.X);
                Canvas.SetTop(shape, d.Y);
                return shape;
            }
            case EllipseAnnotation e:
            {
                var shape = new System.Windows.Shapes.Ellipse
                {
                    Stroke = new SolidColorBrush(a.Color),
                    StrokeThickness = strokeThickness,
                    Fill = null,
                    Width = e.Rect.Width / scale,
                    Height = e.Rect.Height / scale,
                };
                var d = ToWindowDipsRelative(e.Rect.Location);
                Canvas.SetLeft(shape, d.X);
                Canvas.SetTop(shape, d.Y);
                return shape;
            }
            case ArrowAnnotation ar:
            {
                var (tip, left, right) = ArrowGeometry.ComputeHead(ar.Start, ar.End);
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(a.Color),
                    StrokeThickness = strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                // 相对选区坐标：全部平移至 min 点，用 Canvas.SetLeft/Top 定位
                var all = new[] { ar.Start, ar.End, tip, left, right };
                var minX = all.Min(p => p.X);
                var minY = all.Min(p => p.Y);
                var pts = new PointCollection();
                foreach (var p in all)
                    pts.Add(new Point((p.X - minX) / scale, (p.Y - minY) / scale));
                poly.Points = pts;
                var origin = ToWindowDipsRelative(new Point(minX, minY));
                Canvas.SetLeft(poly, origin.X);
                Canvas.SetTop(poly, origin.Y);
                return poly;
            }
            case PenAnnotation pen:
            {
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(a.Color),
                    StrokeThickness = strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                };
                var minX = pen.Points.Count > 0 ? pen.Points.Min(p => p.X) : 0;
                var minY = pen.Points.Count > 0 ? pen.Points.Min(p => p.Y) : 0;
                var pts = new PointCollection();
                foreach (var p in pen.Points)
                    pts.Add(new Point((p.X - minX) / scale, (p.Y - minY) / scale));
                poly.Points = pts;
                var origin = ToWindowDipsRelative(new Point(minX, minY));
                Canvas.SetLeft(poly, origin.X);
                Canvas.SetTop(poly, origin.Y);
                return poly;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }
    }
```

> 坐标模型（重要）：标注一律存**选区相对坐标**（物理像素），鼠标命中（`TrySelectAt(ToRelative(p))`）与绘制（`_tool.Begin(ToRelative(p))`）统一在相对坐标系完成，渲染经 `ToWindowDipsRelative` 转为窗口 DIP。选区**移动**时标注相对坐标不变（天然随选区跟随）；选区**缩放**时标注不缩放，超出部分由合成时的 `PushClip` 裁掉。`AnnotationManager.MoveAllBy` 本任务不调用（保留供将来贴图场景）。

**键盘扩展**（`OnPreviewKeyDown` 中补充，`SwitchTool` 定义见下）：

```csharp
        else if (e.Key >= Key.D1 && e.Key <= Key.D4)
            SwitchTool((AnnotationKind)((int)AnnotationKind.Rectangle + (e.Key - Key.D1)));
        else if (e.Key == Key.Delete)
        {
            _annotations.DeleteSelected();
            RenderAnnotations();
        }
```

**工具栏事件**：

```csharp
    private void SwitchTool(AnnotationKind kind)
    {
        _currentKind = kind;
        _tool = AnnotationToolFactory.Create(_currentKind, _color, _thickness);
    }

    private void Tool_OnClick(object sender, RoutedEventArgs e)
    {
        var kind = Enum.Parse<AnnotationKind>(((FrameworkElement)sender).Tag!.ToString()!);
        SwitchTool(kind);
    }

    private void Color_OnClick(object sender, RoutedEventArgs e)
    {
        _color = (Color)ColorConverter.ConvertFromString(((FrameworkElement)sender).Tag!.ToString()!)!;
        _tool = AnnotationToolFactory.Create(_currentKind, _color, _thickness);
    }

    private void Thickness_OnClick(object sender, RoutedEventArgs e)
    {
        _thickness = double.Parse(((FrameworkElement)sender).Tag!.ToString()!);
        _tool = AnnotationToolFactory.Create(_currentKind, _color, _thickness);
    }

    private void Clear_OnClick(object sender, RoutedEventArgs e)
    {
        _annotations.Clear();
        RenderAnnotations();
    }

    private void CancelBtn_OnClick(object sender, RoutedEventArgs e) => Cancel();
    private void DoneBtn_OnClick(object sender, RoutedEventArgs e) => Complete();
```

**Complete 改为含标注**：

```csharp
    private void Complete()
    {
        if (!_selection.HasSelection)
            return;
        IsOpen = false;
        var composed = CaptureComposer.Compose(
            BitmapConvert.ToBitmapSource(_capture.Bitmap),
            _selection.Selection,
            _annotations.Items);
        Close();
        _onComplete(composed);
    }
```

**Delete 键删除选中标注** 已在键盘扩展中实现（`_annotations.DeleteSelected()` + `RenderAnnotations()`）。选区的移动/缩放不会改变标注存储数据，详见上方「坐标模型」说明。

- [ ] **Step 3: 构建**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error（若 `IAnnotationTool` 增加成员，同步修改 Task 6 代码与测试，保证 `dotnet test` 通过）

- [ ] **Step 4: 手动验证**

- 四种工具绘制正常，笔迹随拖动实时显示
- 点击选中标注（虚线高亮），Delete 删除，拖动微调
- 色板六色、粗细三档生效
- 清除全部标注清空
- Enter 完成 → 粘贴到画图：标注与预览一致、像素清晰
- Esc / 右键 / ✗ 取消；✓ / Enter 完成
- 1/2/3/4 切换工具
- 无标注完成 → 纯原图

- [ ] **Step 5: 提交**

```bash
git add -A
git commit -m "feat: 标注交互与工具栏（绘制/选中/清除/合成含标注）"
```

---

### Task 11: 热键 + 托盘 + App 装配 + 单实例 + OCR 预留接口

**Files:**
- Create: `src/Glyphtap/Services/HotKeyService.cs`
- Create: `src/Glyphtap/Services/TrayIconService.cs`
- Create: `src/Glyphtap/Capture/CaptureController.cs`
- Create: `src/Glyphtap/OCR/ITextRecognizer.cs`
- Create: `src/Glyphtap/Infrastructure/SingleInstance.cs`
- Modify: `src/Glyphtap/App.xaml`、`App.xaml.cs`
- Delete: `src/Glyphtap/MainWindow.xaml`、`MainWindow.xaml.cs`（模板残留）
- Test: 手动（错误处理路径手动验证）

**Interfaces:**
- Consumes: 全部既有模块
- Produces:
  - `public sealed class HotKeyService : IDisposable { public event Action? HotKeyPressed; public bool IsRegistered { get; } public static HotKeyService Register(nint hwnd, uint modifier, uint key); }`
  - `public sealed class TrayIconService : IDisposable { public void ShowNotification(string title, string message); }`
  - `public sealed class CaptureController { public CaptureController(Action<string, string> notify); public void StartCapture(); public bool IsCapturing { get; } }`
  - `public static class SingleInstance { public static bool TryAcquire(string name, out IDisposable? guard); }`
  - `public interface ITextRecognizer { Task<IReadOnlyList<TextLine>> RecognizeAsync(BitmapSource image, CancellationToken ct); }` + `public sealed record TextLine(string Text, Rect BoundsDips);`（V2 实现，本版仅定义）

- [ ] **Step 1: OCR 预留接口**

`src/Glyphtap/OCR/ITextRecognizer.cs`：

```csharp
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
```

- [ ] **Step 2: 单实例**

`src/Glyphtap/Infrastructure/SingleInstance.cs`：

```csharp
namespace Glyphtap.Infrastructure;

public static class SingleInstance
{
    public static bool TryAcquire(string name, out IDisposable? guard)
    {
        guard = null;
        var mutex = new Mutex(true, name, out var createdNew);
        if (!createdNew)
            return false;
        guard = new MutexGuard(mutex);
        return true;
    }

    private sealed class MutexGuard : IDisposable
    {
        private readonly Mutex _mutex;
        public MutexGuard(Mutex mutex) => _mutex = mutex;
        public void Dispose() => _mutex.Dispose();
    }
}
```

- [ ] **Step 3: 热键服务**

`src/Glyphtap/Services/HotKeyService.cs`：

```csharp
using System.Runtime.InteropServices;

namespace Glyphtap.Services;

/// <summary>全局热键：RegisterHotKey + WndProc 回调。注册失败 IsRegistered=false。</summary>
public sealed class HotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private readonly IntPtr _hwnd;
    private readonly int _id;
    private bool _disposed;

    public event Action? HotKeyPressed;
    public bool IsRegistered { get; private set; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HotKeyService(IntPtr hwnd, int id, uint modifier, uint key)
    {
        _hwnd = hwnd;
        _id = id;
        IsRegistered = RegisterHotKey(hwnd, id, modifier, key);
    }

    public static HotKeyService Register(IntPtr hwnd, uint modifier, uint key)
        => new(hwnd, 1, modifier, key);

    /// <summary>WPF 消息钩子入口：由宿主窗口的 HwndSource.AddHook 调用。</summary>
    public IntPtr OnWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _id)
        {
            handled = true;
            HotKeyPressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (IsRegistered)
            UnregisterHotKey(_hwnd, _id);
    }
}
```

- [ ] **Step 4: 托盘服务（H.NotifyIcon）**

`src/Glyphtap/Services/TrayIconService.cs`：

```csharp
using System.Drawing;
using H.NotifyIcon;

namespace Glyphtap.Services;

/// <summary>托盘图标与菜单（截图 / 退出）。</summary>
public sealed class TrayIconService : IDisposable
{
    private readonly TaskbarIcon _tray;
    private Icon? _icon;

    public TrayIconService(Action onCapture, Action onExit)
    {
        _icon = CreateTrayIcon();
        var menu = new System.Windows.Controls.ContextMenu();
        var captureItem = new System.Windows.Controls.MenuItem { Header = "截图 (F1)" };
        captureItem.Click += (_, _) => onCapture();
        var exitItem = new System.Windows.Controls.MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => onExit();
        menu.Items.Add(captureItem);
        menu.Items.Add(exitItem);

        _tray = new TaskbarIcon
        {
            Icon = _icon,
            ToolTipText = "Glyphtap 截图工具",
            ContextMenu = menu,
        };
        _tray.LeftClickCommand = null;
    }

    public void ShowNotification(string title, string message)
        => _tray.ShowNotification(title, message, BalloonIcon.Info);

    private static Icon CreateTrayIcon()
    {
        // 程序生成简易图标（青色圆点），避免引入二进制资源
        using var bmp = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(0, 150, 136));
            g.FillEllipse(brush, 1, 1, 14, 14);
        }
        var h = bmp.GetHicon();
        return Icon.FromHandle(h);
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _tray.Dispose();
    }
}
```

- [ ] **Step 5: 截图协调器**

`src/Glyphtap/Capture/CaptureController.cs`：

```csharp
using System.Windows.Media.Imaging;
using Glyphtap.Services;

namespace Glyphtap.Capture;

/// <summary>截图会话协调：防重入、捕获、打开窗口、完成/取消/失败处理。</summary>
public sealed class CaptureController
{
    private readonly Action<string, string> _notify;   // (title, message)
    private CaptureWindow? _window;

    public CaptureController(Action<string, string> notify) => _notify = notify;

    public bool IsCapturing => _window != null && _window.IsOpen;

    public void StartCapture()
    {
        if (IsCapturing)
            return; // 截图会话中忽略重复触发

        ScreenCaptureResult capture;
        try
        {
            capture = ScreenCaptureService.Capture();
        }
        catch (Exception ex)
        {
            _notify("截图失败", $"无法捕获屏幕：{ex.Message}");
            return;
        }

        _window = CaptureWindow.Open(capture, OnComplete, OnCancel);
    }

    private void OnComplete(BitmapSource image)
    {
        try
        {
            ClipboardService.SetImage(image);
        }
        catch (Exception ex)
        {
            var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Glyphtap");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            System.IO.File.WriteAllBytes(path, ClipboardService.EncodePng(image));
            _notify("复制失败", $"剪贴板写入失败（{ex.Message}），截图已保存到：\n{path}");
        }
        _window = null;
    }

    private void OnCancel()
    {
        _window = null;
    }
}
```

- [ ] **Step 6: App 装配（含隐藏宿主窗口 + 消息钩子）**

删除 `MainWindow.xaml` 与 `MainWindow.xaml.cs`，`App.xaml` 改为：

```xml
<Application x:Class="Glyphtap.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             ShutdownMode="OnExplicitShutdown">
    <Application.Resources>
    </Application.Resources>
</Application>
```

`App.xaml.cs`：

```csharp
using System.Windows;
using System.Windows.Interop;
using Glyphtap.Capture;
using Glyphtap.Infrastructure;
using Glyphtap.Services;

namespace Glyphtap;

public partial class App : Application
{
    private IDisposable? _singleInstanceGuard;
    private HotKeyService? _hotKey;
    private TrayIconService? _tray;
    private CaptureController? _controller;
    private Window? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstance.TryAcquire("Global\\Glyphtap.SingleInstance", out _singleInstanceGuard))
        {
            MessageBox.Show("Glyphtap 已在运行", "Glyphtap", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _controller = new CaptureController(Notify);

        _host = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
        _host.Show();
        _host.Hide();

        var hwnd = new WindowInteropHelper(_host).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        _hotKey = HotKeyService.Register(hwnd, 0, 0x70); // F1
        if (_hotKey.IsRegistered)
        {
            source.AddHook((h, m, w, l, handled) => _hotKey.OnWndProc(h, m, w, l, ref handled));
            _hotKey.HotKeyPressed += () => _controller.StartCapture();
        }
        else
        {
            Notify("热键注册失败", "F1 全局热键被占用，仍可通过托盘菜单截图");
        }

        _tray = new TrayIconService(() => _controller.StartCapture(), ExitApp);
    }

    private void Notify(string title, string message) => _tray?.ShowNotification(title, message);

    private void ExitApp()
    {
        _hotKey?.Dispose();
        _tray?.Dispose();
        _singleInstanceGuard?.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Dispose();
        _tray?.Dispose();
        _singleInstanceGuard?.Dispose();
        base.OnExit(e);
    }
}
```

- [ ] **Step 7: 构建 + 全量测试**

Run: `dotnet build Glyphtap.sln`
Expected: 0 error
Run: `dotnet test tests/Glyphtap.Tests/Glyphtap.Tests.csproj -v minimal`
Expected: 全部通过

- [ ] **Step 8: 手动验证（验收清单）**

- 启动后无主窗口，托盘出现图标（青点）
- 托盘菜单「截图 (F1)」与按 F1 均进入截图；截图会话中再按 F1 无效（不叠窗）
- 完成后剪贴板可粘贴；Esc/右键/✗ 取消后剪贴板不变
- 再次启动程序 → 弹「Glyphtap 已在运行」并退出
- 托盘「退出」→ 进程退出、托盘图标消失、任务管理器无残留
- 用其他程序占用 F1 后启动 Glyphtap → 托盘气泡提示热键注册失败，托盘菜单仍可截图
- 截图画错路径：完成时若剪贴板异常（可用进程独占剪贴板测试）→ 气泡提示并保存到 `%TEMP%\Glyphtap\`
- 双屏（DPI 相同/不同）下坐标无偏移；125%/150% 缩放下像素清晰

- [ ] **Step 9: 提交**

```bash
git add -A
git commit -m "feat: 全局热键/托盘/单实例装配 + OCR 接口预留"
```

---

## 验收总清单（对应规格第 6 节）

| # | 验收项 | 覆盖任务 |
|---|--------|----------|
| 1 | 单屏/双屏（DPI 同与不同）坐标无偏移 | T2/T7/T9 手动 |
| 2 | 四工具绘制、选中删除、随选区拖动 | T6/T10 |
| 3 | 清除全部标注 | T10 |
| 4 | 无标注直接 Enter 纯选区 | T9 |
| 5 | 合成图与预览一致、像素清晰 | T8/T10 |
| 6 | Esc/右键/✗ 取消，剪贴板不变 | T9/T10/T11 |
| 7 | 热键冲突托盘提示、托盘仍可截图 | T11 |
| 8 | 截图会话中重复 F1 不叠窗 | T11 |
| 9 | 单实例提示 | T11 |
| 10 | 托盘常驻、可重复截图/退出 | T11 |
| 11 | 高 DPI 清晰度 | T2/T7/T8 |
| 12 | 退出无残留进程 | T11 |
| 13 | 剪贴板失败 → 临时文件保存 + 提示 | T11 |
## 代码审查修正记录（Task 全部完成后）

基于 superpowers:requesting-code-review 审查结果，修订如下（均已通过构建 + 38 测试）：

| # | 级别 | 内容 | 修复位置 |
|---|------|------|----------|
| S1 | 严重 | 截屏捕获循环的每屏位图在成功路径与中途异常时均未释放，托盘常驻会累积 GDI 句柄 | `ScreenCaptureService.Capture` 改为 try/finally 全部释放 |
| S2 | 严重 | 原《主屏 scale 为准》在 PMv2 下不成立：全屏窗口 DPI 由重叠面积最大显示器决定，混合 DPI 会整体错位 | `CaptureWindow` 改为窗口实际 DPI（`VisualTreeHelper.GetDpi` + `SourceInitialized`/`DpiChanged` 校正），`ScreenLayout` 换算仅供测试，已同步 Global Constraints |
| I1 | 重要 | 热键注册失败提示发生在托盘创建之前，`_tray` 为 null 导致提示被吞 | `App.OnStartup` 先创建托盘再注册热键 |
| I2 | 重要 | 遮罩左/上两块用了窗口原点 (0,0) 而右/下用虚拟屏幕绝对坐标，负虚拟原点点错位 | `UpdateSelectionVisual` 四块统一传虚拟屏幕绝对坐标并修正宽高 |
| I3 | 重要 | 剪贴板未 `Flush`，进程退出后图片失效 | `ClipboardService.SetImage` 增加 `Clipboard.Flush()` |
| I4 | 重要 | 重建选区后旧标注仍以旧选区坐标系渲染（残影） | 选区进入 Creating 且已有选区时 `_annotations.Clear()` |
| M1 | 次要 | `Complete`/`Cancel` 无防重入 | 入口增加 `IsOpen` 检查；结束后 `Dispose` 捕获位图 |
| M2 | 次要 | `GetDpiForMonitor` 失败返回 0 会引后续除零 | 失败回退 96 |
| M3 | 次要 | 临时文件落盘失败时异常可能穿越回调 | 落盘包 try/catch 并提示 |
| M5 | 次要 | 预览 Polyline 缺 `StrokeLineJoin=Round` 与最终合成不一致 | 箭头/画笔 Polyline 补 Round |
| M6 | 次要 | 点击未拖动也建立 8×8 选区 | 增加 `_moved` 标志，Creating 且未移动则清空（新增回归测试） |
| M7 | 次要 | `H.NotifyIcon`（Core）直接引用冗余 | 移除，由 `H.NotifyIcon.Wpf` 传递依赖 |

评估结论：修改后合并。
