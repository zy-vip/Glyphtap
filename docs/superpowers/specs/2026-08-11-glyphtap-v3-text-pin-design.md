# Glyphtap 截图工具 — V3 设计文档（文本标注 + 贴图）

- 日期：2026-08-11
- 状态：已批准（V3 设计）
- 前置：V2（撤销/重做、高亮/马赛克、OCR）已完成并合并至 master
- 目标：补齐 V2 遗留的「文本标注」，并实现 Snipaste 核心能力「贴图」（图像/文本钉在屏幕上常驻）

## 1. 背景与范围

### 本阶段（V3）范围
1. **文本标注**：截图窗口工具栏新增「文本」工具（数字键 7），选区内点按弹出内联输入框，Enter 提交为文本标注；复用现有选中/移动/Delete/撤销重做。
2. **贴图功能**：全局热键 `Ctrl+V`（Snipaste 同款，可重绑）读取系统剪贴板——有图像贴出图像贴图，有纯文本贴出文字卡片；贴图可拖动、滚轮缩放、边缘 8 区拉伸、Ctrl+滚轮调透明度、双击销毁、右键菜单（复制图像/复制文本/保存图像/置顶开关/销毁）；复制粘贴链（贴图复制后再贴出）；不持久化（重启消失）。

### 不在本阶段范围（明确排除）
- 截图历史 / 自动保存文件（V3 路线图另一项，后续再做）
- 贴图持久化、鼠标穿透、多贴图排列、贴图内再标注
- 文本标注的自定义字体选择（用系统默认字体）、已有文本的二次编辑（删掉重画）
- Ctrl+V 拦截的其他应用粘贴兼容方案（接受 Snipaste 同款副作用，靠可重绑热键缓解）

## 2. 技术方案选型

在三个候选方案中选择：

1. **贴图窗口按来源分支渲染（采纳）**：图像贴图 = `BitmapSource` 直接显示，文本贴图 = 固定样式 TextBlock 卡片；新模块 `Pin/`（`PinManager` + `PinWindow` + 纯逻辑 `PinGeometry`）。不触碰截图管线与 `CaptureComposer`，回归风险最小，符合「可测试纯逻辑 + 薄 UI」风格。
2. 贴图复用截图合成管线（`RenderTargetBitmap` 统一合成后显示）：路径统一，但对文本贴图是浪费，且改动波及 `CaptureComposer` 与多屏坐标逻辑，回归风险高。
3. 截图与贴图抽象为「内容会话」统一基类：架构最干净，但 V3 过度设计。

采纳方案 1。文本标注不涉及选型：`TextAnnotation` 实现现有 `Annotation` 抽象即可（渲染走 `AnnotationRenderer` 与合成共用路径，零合成改动）。

## 3. 体系结构

```
src/Glyphtap/
├── Capture/
│   ├── AnnotationModel.cs        +TextAnnotation（Text/Position/Thickness→字号映射）
│   ├── AnnotationTools.cs        +TextMetrics 静态类（FormattedText 测量，STA）
│   ├── AnnotationRenderer.cs     +Text 分支（DrawText，无描边）
│   └── CaptureWindow.xaml.cs     +文本工具态：点按→内联 TextBox→Enter/Esc 提交或取消
├── Pin/                          新模块（本阶段核心）
│   ├── PinManager                读剪贴板分发；活动贴图登记；TryPinFromClipboard
│   ├── PinWindow                 无边框 Topmost 贴图窗口（拖动/缩放/拉伸/透明度/右键菜单）
│   ├── PinGeometry               （纯逻辑）8 区命中、拉伸矩形计算、最小尺寸钳制
│   └── TextPinCard               （可并入 PinWindow）文本卡片 + AutoWrap
├── Services/
│   ├── HotKeyService.cs          +多 id 注册（截图为 id1 / 贴图为 id2）；重绑 = 注销旧+注册新
│   ├── AppSettings.cs            （新）JSON 配置：热键两项；%APPDATA%\Glyphtap\config.json
│   └── TrayIconService.cs        +「粘贴贴图 (Ctrl+V)」菜单项 +「设置…」菜单项
└── App.xaml.cs                   组合两热键注册；Ctrl+V → 截图会话中忽略否则 PinManager.TryPinFromClipboard
```

### 设计决策
- `HotKeyService` 从「单热键固定 id=1」扩展为「按 id 注册多个」：`Register(hwnd, id, modifier, key)`；重复注册同 id 先注销。App 持有两个注册对象（截图、贴图），重绑时重建。
- `AppSettings` 轻量 JSON（`System.Text.Json`，无第三方依赖）：`{ CaptureHotKey, PinHotKey }`，格式 `C+V` / `F1` 字符串，默认 F1 / Ctrl+V；提供解析/序列化为纯逻辑（可单测）。
- 文本标注字号映射：细 12 / 中 16 / 粗 20（物理像素），复用粗细三档按钮；颜色复用色板。`TextAnnotation.Position` 为文本区域左上角（相对选区物理像素），`Bounds` 由 `TextMetrics.Measure(text, fontSize)` 给出。
- `TextTool` 不进拖拽式 `IAnnotationTool` 工厂（点按语义不同）；内联输入由 `CaptureWindow` 直接管理（Canvas 中定位 TextBox），提交回调创建 `TextAnnotation` 并入 `AnnotationManager`（自动获得撤销/重做）。`TextAnnotation.Resize` 为无操作（沿用箭头/画笔不缩放惯例，仅随选区平移）。
- 贴图窗口独立小窗口：`PinWindow` 内容 = `Image`（贴图时 `BitmapSource` 直显，缩放用 `LayoutTransform`）或文本卡片（白底黑字、`TextWrapping` 限宽 400px、字号 14）；`Window.Opacity` 调透明度。
- 复制粘贴链：贴图右键「复制」= 写系统剪贴板（`ClipboardService.SetImage/SetText`）→ Ctrl+V 再贴出第二张；自贴递归属预期（Snipaste 同款）。

## 4. 交互流程与数据流

```
托盘常驻 + F1 截图（现有）+ Ctrl+V 贴图（新增）
  → Ctrl+V 全局热键 → PinManager.TryPinFromClipboard()
      读取系统剪贴板（STA）：
        有 BitmapSource → 新建图像 PinWindow（物理像素直显，按 DPI 换算窗口尺寸）
        有纯文本且无图像 → 新建文本 PinWindow（文字卡片）
        都无（或文件列表等）→ 静默忽略，不提示
  → 贴图定位：显示在当前鼠标光标所在屏幕的中央（多显示器下贴在与鼠标同屏的位置，剪贴板来源无关）；窗口 Topmost 置顶
  → 贴图窗口交互：
      左键拖动移动；滚轮缩放（LayoutTransform，1x 附近步进 0.1，限 0.2~8x）
      Ctrl+滚轮调透明度（0.1 步进，限 0.2~1.0）；边缘 8 区拉伸改窗口宽高（最小 24 DIP）
      双击销毁；右键菜单 → 复制图像/复制文本（文本贴图）/保存图像（仅图像贴图，文本贴图菜单不显示）/置顶开关/销毁
  → 右键「保存图像」：%USERPROFILE%\Pictures\Glyphtap\Glyphtap_yyyyMMdd_HHmmss.png（PNG）
  → 程序退出：注销全部热键，贴图窗口随进程消失（不持久化）
```

### 文本标注交互（截图窗口内）

```
选择「文本」工具（工具栏按钮 / 数字键 7）
  → 在选区内点按（左键按下）→ 该位置浮出内联 TextBox（半透明底、当前色文本、字号档）
  → Enter 或失焦：提交 → 创建 TextAnnotation（坐标换算为相对选区物理像素）→ 撤消栈记录
  → Esc：取消输入，不创建
  → 提交后为普通标注：可点击选中、拖动微调、Delete 删除、撤销/重做
```

### 快捷键（新增）
| 键 | 行为 |
|----|------|
| Ctrl+V（全局，可重绑） | 贴出剪贴板图像/文本；截图会话中忽略 |
| 7（截图窗口内） | 切换文本工具（D1~D6 已被矩形/椭圆/箭头/画笔/高亮/马赛克占用） |

### 托盘菜单（新增两项）
- 「粘贴贴图 (Ctrl+V)」：手动触发贴出（热键注册失败时的降级入口）
- 「设置…」：捕获式热键输入框（点击后按下新组合键即捕获，支持 Ctrl/Alt/Shift+任意键 或 功能键），可分别重绑截图/贴图热键；「保存」后重新注册全局热键，冲突则气泡提示并保留原热键
- 菜单标题动态显示当前热键（如「截图 (F1)」「粘贴贴图 (Ctrl+V)」）

### 热键重绑规则
- `AppSettings` 加载于启动；注册失败降级路径与 F1 现状一致（气泡提示 + 托盘菜单可用）
- 重绑后立即生效；写入 config.json

## 5. 错误处理

| 场景 | 处理 |
|------|------|
| Ctrl+V 注册失败 | 托盘气泡「贴图热键被占用」；托盘「粘贴贴图 (Ctrl+V)」菜单仍可手动贴出 |
| 重绑热键冲突 | 气泡提示，保留原热键，不写入配置 |
| 剪贴板无图像无文本 | 静默忽略（不吞按键提示），不打扰 |
| 剪贴板读取失败（其他进程占用） | 静默忽略（与 Windows 常态一致） |
| 保存图像失败（目录/权限） | 托盘气泡提示路径与原因；贴图不销毁 |
| 截图会话中按 Ctrl+V | 忽略（沿用防重入模式，避免贴图盖住截图窗口） |

## 6. 测试与验收标准

### 单元测试（新增，xUnit）
- `PinGeometry`：8 区边缘命中、拖拉伸缩矩形计算、最小尺寸钳制（24 DIP）——普通 `[Fact]`
- `TextMetrics`：文本测量宽度单调递增、字号档位映射（12/16/20）、空文本零宽 —— `[StaFact]`
- `TextAnnotation`：Clone 深拷贝（字符串独立）、Offset 位移、Bounds 一致 —— `[Fact]`（测量部分 `[StaFact]`）
- `AppSettings`：JSON 序列化/反序列化往返、默认值、非法热键字符串回退默认 —— `[Fact]`
- 现有 68 测试全量回归（涉及 `AnnotationModel`/`AnnotationRenderer` 的测试需保持通过）

### 手动验证清单（GUI 环境，3 份 V2 清单验收后执行）
1. 文本标注：点按输入中文/英文、Enter 提交、Esc 取消、三种字号、六色、选中拖动、Delete、撤销/重做；合成图文字清晰与预览一致
2. 贴图图像：任意应用 Ctrl+C 复制图像 → Ctrl+V 贴出；拖动/滚轮缩放/Ctrl+滚轮透明度/8 区拉伸/双击销毁
3. 贴图文本：复制文字 → Ctrl+V 贴出文字卡片；右键「复制文本」可用
4. 复制粘贴链：贴图 → 右键复制图像/文本 → Ctrl+V 贴出第二张
5. 保存图像：右键保存 → 目标目录生成 PNG，内容与贴图一致
6. 置顶开关：贴图置顶/不置顶切换生效
7. 热键：设置中重绑 F1/Ctrl+V → 新热键生效、配置持久化（重启后仍生效）；重绑冲突 → 气泡提示且原热键保留
8. 降级：Ctrl+V 被占用场景 → 托盘「粘贴贴图」菜单手动触发可用
9. 干扰：截图中按 Ctrl+V 不贴图；Glyphtap 运行期间其他应用 Ctrl+V 被吞 → 改绑热键后恢复（Snipaste 同款副作用已接受）
10. 退出/重启：贴图随进程消失；托盘两次「粘贴贴图」贴出两张贴图互不干扰
11. 多显示器：鼠标移到副屏后 Ctrl+V → 贴图出现在副屏中央；DPI 不同的屏幕间贴图尺寸显示正确（物理像素/DPI 换算）

### 成功标准
- Ctrl+V 贴出响应 <0.5s；截图、标注、撤销等既有功能不受影响（68 测试通过 + 手动回归）
- 文本标注所见即所得，合成图与预览一致
- 贴图行为与 Snipaste 核心体验一致（拖动/缩放/透明度/右键菜单/粘贴链）

## 7. 与其他文档的关系
- 本规格为 V3 设计；实施计划与验证清单将写入 `docs/superpowers/plans/2026-08-11-glyphtap-v3-*.md`
- 未变更项：多屏 DPI 换算、合成管线、OCR、撤销栈上限等沿用现有设计（`2026-08-07-glyphtap-design.md`）