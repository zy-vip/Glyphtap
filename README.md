# Glyphtap

Windows 截图工具（Snipaste 类比）。托盘常驻，按 `F1` 随时触发区域截图，选区内可做矩形 / 椭圆 / 箭头 / 画笔 / 高亮 / 马赛克 / 文本标注，支持撤销重做与 OCR 文字识别，`Enter` 一键复制到剪贴板。

## 功能

- **托盘常驻**：启动后常驻系统托盘，`F1` 全局热键随时触发截图；热键被占用时可通过托盘菜单「截图」兜底
- **区域截图**：拖拽创建选区，选区整体拖动、8 个手柄缩放；多显示器（含不同 DPI）下坐标精确无偏移
- **7 种标注工具**：矩形、椭圆、箭头、画笔、高亮、马赛克、文本（工具栏按钮或数字键 1~7 切换）
- **标注编辑**：点击选中、拖动微调、`Delete` 删除、`清除` 一键清空；`Ctrl+Z` / `Ctrl+Y` 撤销重做（上限 100 步）
- **OCR 文字识别**：工具栏「识别」对选区内文字做本地离线识别（Windows.Media.Ocr，无需联网），结果浮窗一键复制
- **所见即所得**：标注预览与最终合成图一致，像素清晰；完成即写入系统剪贴板，可直接粘贴到任意应用
- **健壮性**：单实例互斥、截图中重复热键防重入、剪贴板写入失败自动降级保存到 `%TEMP%\Glyphtap\` 并气泡提示

## 快捷键

| 键 | 行为 |
|----|------|
| `F1`（全局） | 触发区域截图 |
| `1` ~ `7` | 切换 矩形 / 椭圆 / 箭头 / 画笔 / 高亮 / 马赛克 / 文本 |
| `Enter` | 完成截图并复制到剪贴板 |
| `Esc` / 鼠标右键 | 取消截图 |
| `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做标注 |
| `Delete` | 删除选中的标注 |

## 构建与运行

要求：Windows 10+，[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
# 构建
dotnet build Glyphtap.sln

# 运行（托盘常驻，按 F1 截图）
dotnet run --project src/Glyphtap
```

## 测试

```powershell
dotnet test tests/Glyphtap.Tests
```

68 个单元测试覆盖选区状态机、标注管理与撤销栈、多屏 DPI 换算、马赛克像素化、OCR 坐标还原等纯逻辑，耗时 <1s。

## 技术栈与架构

- C# / .NET 8 / WPF，PerMonitorV2 DPI 感知，仅依赖 H.NotifyIcon.Wpf 与 System.Drawing.Common
- `Capture/` 为可单测的纯逻辑核心：`SelectionLogic`（选区状态机）、`AnnotationManager`（标注与撤销栈）、`CaptureComposer`（合成）、`MosaicPixelator`（马赛克）、`ScreenLayout`（物理像素↔DIP 换算）
- `OCR/` 为识别器链：`WindowsOcrRecognizer` 本地离线引擎，接口预留云端扩展点
- 全局热键（RegisterHotKey）、单实例（Mutex）、显示器枚举均为 P/Invoke 或托管封装，无第三方库替换

## 路线图

- 贴图功能（Ctrl+V 贴出剪贴板图像 / 文本卡片、热键重绑设置）—— 已设计，待实现
- 截图历史与自动保存文件

## 许可证

[MIT](LICENSE) © 2026 zhou
