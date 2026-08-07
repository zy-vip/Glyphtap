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

        RootGrid.MouseDown += RootGrid_MouseDown;
        RootGrid.MouseMove += RootGrid_MouseMove;
        RootGrid.MouseUp += RootGrid_MouseUp;

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
        else if (e.Key >= Key.D1 && e.Key <= Key.D4)
            SwitchTool((AnnotationKind)((int)AnnotationKind.Rectangle + (e.Key - Key.D1)));
        else if (e.Key == Key.Delete)
        {
            _annotations.DeleteSelected();
            RenderAnnotations();
        }
    }

    // ---- 坐标换算：DP 与物理像素 ----

    private Point ToPhysical(Point windowPoint)
        => _capture.Layout.ToPhysical(windowPoint);

    private Point ToWindowDips(Point physical)
        => _capture.Layout.ToWindowDips(physical);

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

        // 遮罩：左 / 右 / 上 / 下 四块
        var winW = Width * _capture.Layout.PrimaryScale;
        var winH = Height * _capture.Layout.PrimaryScale;
        UpdateMask(0, 0, 0, s.X, winH);                          // 左
        UpdateMask(1, s.X + s.Width, 0, winW - s.X - s.Width, winH); // 右
        UpdateMask(2, s.X, 0, s.Width, s.Y);                     // 上
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
        _maskParts[index].Width = Math.Max(0, w / _capture.Layout.PrimaryScale);
        _maskParts[index].Height = Math.Max(0, h / _capture.Layout.PrimaryScale);
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

    private void CancelBtn_OnClick(object sender, RoutedEventArgs e) => Cancel();
    private void DoneBtn_OnClick(object sender, RoutedEventArgs e) => Complete();

    // ---- 完成 / 取消 ----

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

    private void Cancel()
    {
        IsOpen = false;
        Close();
        _onCancel();
    }
}