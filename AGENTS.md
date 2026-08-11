# AGENTS.md

Glyphtap：Windows 截图工具（Snipaste 类似）——托盘常驻 + F1 全局热键触发区域截图，选区内可做矩形/椭圆/箭头/画笔/高亮/马赛克标注，支持撤销/重做与 OCR 文字识别，Enter 复制到剪贴板。

## 命令

- 构建：`dotnet build Glyphtap.sln`（从仓库根目录）
- 测试：`dotnet test tests/Glyphtap.Tests`——68 个测试耗时 <1s，可随时全量跑，无需装服务或环境
- 无 lint/格式任务、无 CI；托盘/截图窗口 UI 只能手动验证

## 架构

- `src/Glyphtap`：WPF 应用（目标框架 `net8.0-windows10.0.19041.0`，PerMonitorV2 DPI 感知见 `Properties/app.manifest`），仅依赖 H.NotifyIcon.Wpf、System.Drawing.Common
- 可测试的纯逻辑集中在 `Capture/`：`SelectionLogic.cs`（选区状态机）、`AnnotationManager.cs`（标注增删/选中/平移/**撤销重做快照栈**）、`AnnotationModel.cs`（`Annotation.Clone()` 深拷贝）、`MosaicPixelator.cs`（马赛克像素化）、`ScreenLayout.cs`（物理像素↔DIP 换算）、`MonitorEnumerator.cs`、`CaptureComposer.cs`（合成，需 STA）。UI 交互只在 `CaptureWindow.xaml.cs`；`CaptureController.cs` 编排会话（防重入、捕获失败/剪贴板失败降级——失败时存 `%TEMP%\Glyphtap\`）
- OCR 在 `OCR/`：`ITextRecognizer.cs` 接口 + `WindowsOcrRecognizer.cs`（WinRT `Windows.Media.Ocr` 离线引擎，>2600px 预缩放并还原坐标）+ `CompositeTextRecognizer.cs`（识别器链，宽泛 catch 内对 `OperationCanceledException` 重新抛出）
- 测试项目以 ProjectReference 引用主 exe 项目；xUnit 规则：**涉及 WPF/剪贴板/渲染的测试必须用 `[StaFact]`**（Xunit.StaFact 包），纯几何逻辑用普通 `[Fact]`
- 撤销/重做已实现：快照式历史栈（上限 100），`Add/DeleteSelected/Clear/MoveAllBy` 自动记录，拖拽移动由 UI 手势开始时推一次点；新增标注工具只需加一个实现 `IAnnotationTool` 的类

## 约定与陷阱

- 项目内注释、字符串、测试方法名、git 提交信息全部用中文；提交用 conventional 风格（`feat:`/`fix:`/`docs:`/`chore:`），如 `fix: 代码审查修正（...概要）`
- 权威设计文档：`docs/superpowers/specs/2026-08-07-glyphtap-design.md`（MVP 规格，已批准；V2 完成状态见其第 1 节注记）。三个 V2 计划文档在 `docs/superpowers/plans/2026-08-10-glyphtap-v2-*.md`（均已实施完成，含手动验证清单，其中 3 份待用户在 GUI 环境执行）。改交互/坐标逻辑前先读设计文档第 4 节
- **DPI 是最大的坑**：窗口坐标单位是 DIP，捕获位图是物理像素，换算基准为主屏 scale（设计文档第 4 节）。动到坐标/合成的改动必须回归 `ScreenLayout`、`CaptureWindow`、`CaptureComposer` 相关测试
- **多显示器负偏移**：捕获位图像素原点 = 虚拟屏幕 `VirtualBounds` 左上角（可为负），选区/标注是虚拟屏幕绝对坐标；接触像素边界（`CaptureComposer` 主绘制/`DrawMosaic`、`AnnotationElement` 马赛克预览、OCR 裁剪）须统一减 `bitmapOrigin`，测试见 `Compose_负偏移虚拟屏下背景与马赛克定位正确`
- 全局热键注册（Win32 RegisterHotKey）、单实例（命名 Mutex）、显示器枚举（EnumDisplayMonitors）均为 P/Invoke 或托管包装，别用第三方库替换
