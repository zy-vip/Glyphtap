using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Glyphtap.Services;

namespace Glyphtap.Capture;

/// <summary>全屏截图窗口：背景暗化 + 选区交互 + 标注层 + 工具栏。</summary>
public sealed partial class CaptureWindow : Window
{
    private readonly ScreenCaptureResult _capture;
    private readonly Action<BitmapSource> _onComplete;
    private readonly Action _onCancel;
    private readonly SelectionLogic _selection = new();
    private readonly List<System.Windows.Shapes.Rectangle> _maskParts = new();
    private System.Windows.Shapes.Rectangle _selectionVisual = null!;
    private readonly List<System.Windows.Shapes.Rectangle> _handles = new();

    private readonly AnnotationManager _annotations = new();
    private IAnnotationTool _tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
    private AnnotationKind _currentKind = AnnotationKind.Rectangle;
    private Color _color = Colors.Red;
    private double _thickness = 3;
    private bool _draggingAnnotation;
    private Point _dragLast;
    private bool _dragUndoPointRecorded;

    /// <summary>窗口实际 DPI 缩放（WPF DIP ↔ 物理像素换算基准）。</summary>
    private double _scale;

    /// <summary>缓存背景位图源：马赛克预览与 OCR 识别都要基于它裁剪/像素化。</summary>
    private readonly BitmapSource _backgroundSource;

    public bool IsOpen { get; private set; } = true;

    private CaptureWindow(ScreenCaptureResult capture, Action<BitmapSource> onComplete, Action onCancel)
    {
        _capture = capture;
        _onComplete = onComplete;
        _onCancel = onCancel;

        InitializeComponent();
        // 缓存背景源：马赛克预览与 OCR 识别都要基于它裁剪/像素化
        _backgroundSource = BitmapConvert.ToBitmapSource(capture.Bitmap);
        BackgroundImage.Source = _backgroundSource;

        var layout = capture.Layout;
        var vb = layout.VirtualBounds;

        // 窗口显示前以主屏 scale 估算，Show 后按窗口实际 DPI 校正（混合 DPI 下窗口 DPI 不一定是主屏）
        _scale = layout.PrimaryScale;
        Left = vb.X / _scale;
        Top = vb.Y / _scale;
        Width = vb.Width / _scale;
        Height = vb.Height / _scale;

        BuildMask();
        BuildSelectionVisual();

        RootGrid.MouseDown += RootGrid_MouseDown;
        RootGrid.MouseMove += RootGrid_MouseMove;
        RootGrid.MouseUp += RootGrid_MouseUp;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseRightButtonUp += (_, _) => Cancel();

        SourceInitialized += (_, _) => RelayoutToWindowDpi();
        DpiChanged += (_, _) => RelayoutToWindowDpi();
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
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            if (e.Key == Key.Z)
                UndoAnnotations();
            else if (e.Key == Key.Y || (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Shift) != 0))
                RedoAnnotations();
            return;
        }
        if (e.Key == Key.Enter)
            Complete();
        else if (e.Key == Key.Escape)
            Cancel();
        else if (e.Key >= Key.D1 && e.Key <= Key.D6)
            SwitchTool((AnnotationKind)((int)AnnotationKind.Rectangle + (e.Key - Key.D1)));
        else if (e.Key == Key.Delete)
        {
            _annotations.DeleteSelected();
            RenderAnnotations();
        }
    }

    // ---- 坐标换算：DIP 与物理像素（基准 = 窗口实际 DPI） ----

    /// <summary>按窗口实际 DPI 重新覆盖虚拟屏幕（窗口创建/DpiChanged 时调用）。</summary>
    private void RelayoutToWindowDpi()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        if (dpi.DpiScaleX <= 0)
            return;
        _scale = dpi.DpiScaleX;

        var vb = _capture.Layout.VirtualBounds;
        Left = vb.X / _scale;
        Top = vb.Y / _scale;
        Width = vb.Width / _scale;
        Height = vb.Height / _scale;

        UpdateSelectionVisual();
    }

    private Point ToPhysical(Point windowPoint)
    {
        var vb = _capture.Layout.VirtualBounds;
        return new Point(vb.X + windowPoint.X * _scale, vb.Y + windowPoint.Y * _scale);
    }

    private Point ToWindowDips(Point physical)
    {
        var vb = _capture.Layout.VirtualBounds;
        return new Point((physical.X - vb.X) / _scale, (physical.Y - vb.Y) / _scale);
    }

    // ---- 鼠标交互（分区创建后进入工具模式） ----

    /// <summary>绝对物理坐标 → 选区相对坐标（标注存储基准）。</summary>
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
                _dragUndoPointRecorded = false; // 点击选中：移动发生时（MouseMove）才记录撤销点
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

        var hadSelection = _selection.HasSelection;
        _selection.OnMouseDown(p);
        if (hadSelection && _selection.Mode == SelectionMode.Creating)
        {
            // 重新创建选区：旧标注以旧选区为基准，坐标已失效，整体清除
            _annotations.Clear();
            RenderAnnotations();
        }
        RootGrid.CaptureMouse();
        UpdateSelectionVisual();
    }

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

    /// <summary>判断事件源是否位于工具栏控件树内。</summary>
    private bool IsInToolbar(object source)
    {
        for (var d = source as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d == Toolbar)
                return true;
        }
        return false;
    }

    // ---- 选区与遮罩渲染 ----

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
        var size = new Size(s.Width / _scale, s.Height / _scale);
        Canvas.SetLeft(_selectionVisual, d.X);
        Canvas.SetTop(_selectionVisual, d.Y);
        _selectionVisual.Width = size.Width;
        _selectionVisual.Height = size.Height;
        _selectionVisual.Visibility = Visibility.Visible;

        // 手柄（物理 8px → DIP）
        var hSize = 8 / _scale;
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

        // 遮罩：左 / 右 / 上 / 下 四块（物理绝对坐标，ToWindowDips 负责换算）
        var winW = _capture.Layout.VirtualBounds.Width;
        var winH = _capture.Layout.VirtualBounds.Height;
        var vbX = _capture.Layout.VirtualBounds.X;
        var vbY = _capture.Layout.VirtualBounds.Y;
        UpdateMask(0, vbX, vbY, s.X - vbX, winH);                    // 左
        UpdateMask(1, s.X + s.Width, vbY, winW - s.X - s.Width, winH); // 右
        UpdateMask(2, s.X, vbY, s.Width, s.Y - vbY);                 // 上
        UpdateMask(3, s.X, s.Y + s.Height, s.Width, winH - s.Y - s.Height); // 下
        foreach (var m in _maskParts)
            m.Visibility = Visibility.Visible;

        ShowToolbar();
    }

    /// <summary>选区建立后显示工具栏。</summary>
    private void ShowToolbar() => Toolbar.Visibility = Visibility.Visible;

    private void UpdateMask(int index, double x, double y, double w, double h)
    {
        var d = ToWindowDips(new Point(x, y));
        Canvas.SetLeft(_maskParts[index], d.X);
        Canvas.SetTop(_maskParts[index], d.Y);
        _maskParts[index].Width = Math.Max(0, w / _scale);
        _maskParts[index].Height = Math.Max(0, h / _scale);
    }

    // ---- 标注渲染 ----

    /// <summary>选区相对坐标 → 窗口 DIP 坐标（渲染用）。</summary>
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

        UpdateUndoButtons(); // 渲染同时刷新撤销/重做按钮状态
    }

    private void RenderLiveDrawing() => RenderAnnotations();

    private FrameworkElement AnnotationElement(Annotation a)
    {
        var scale = _scale;
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
            case HighlightAnnotation h:
            {
                var shape = new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(90, h.Color.R, h.Color.G, h.Color.B)),
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
            case ArrowAnnotation ar:
            {
                var (tip, left, right) = ArrowGeometry.ComputeHead(ar.Start, ar.End);
                var poly = new System.Windows.Shapes.Polyline
                {
                    Stroke = new SolidColorBrush(a.Color),
                    StrokeThickness = strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
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
                    StrokeLineJoin = PenLineJoin.Round,
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

    // ---- 工具栏 ----

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

    private void CancelBtn_OnClick(object sender, RoutedEventArgs e) => Cancel();
    private void DoneBtn_OnClick(object sender, RoutedEventArgs e) => Complete();

    // ---- 完成 / 取消 ----

    private void Complete()
    {
        if (!IsOpen)
            return;
        if (!_selection.HasSelection)
            return;
        IsOpen = false;
        var composed = CaptureComposer.Compose(
            BitmapConvert.ToBitmapSource(_capture.Bitmap),
            _selection.Selection,
            _annotations.Items);
        Close();
        _capture.Bitmap.Dispose();
        _onComplete(composed);
    }

    private void Cancel()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        Close();
        _capture.Bitmap.Dispose();
        _onCancel();
    }
}