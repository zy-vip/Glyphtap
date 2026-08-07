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