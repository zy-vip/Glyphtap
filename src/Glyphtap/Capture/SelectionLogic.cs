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
    /// 按手柄缩放：固定对边（start 对应内边），动边跟随指针。
    /// 允许反向拖拽：直接以动边/定边求 min/max 构造矩形（等价于先行归一化），
    /// 避免构造 WPF 不允许的负宽高 Rect。
    /// </summary>
    private static Rect ResizeTo(ResizeHandle handle, Rect start, Point p)
    {
        var left = start.Left;
        var top = start.Top;
        var right = start.Right;
        var bottom = start.Bottom;

        switch (handle)
        {
            case ResizeHandle.Top:
                top = p.Y;
                break;
            case ResizeHandle.Bottom:
                bottom = p.Y;
                break;
            case ResizeHandle.Left:
                left = p.X;
                break;
            case ResizeHandle.Right:
                right = p.X;
                break;
            case ResizeHandle.TopLeft:
                left = p.X;
                top = p.Y;
                break;
            case ResizeHandle.TopRight:
                right = p.X;
                top = p.Y;
                break;
            case ResizeHandle.BottomLeft:
                left = p.X;
                bottom = p.Y;
                break;
            case ResizeHandle.BottomRight:
                right = p.X;
                bottom = p.Y;
                break;
            default:
                return start;
        }

        return new Rect(
            new Point(Math.Min(left, right), Math.Min(top, bottom)),
            new Point(Math.Max(left, right), Math.Max(top, bottom)));
    }
}