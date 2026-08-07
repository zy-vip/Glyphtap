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