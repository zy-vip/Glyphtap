using System.Windows;

namespace Glyphtap.Capture;

/// <summary>标注集合管理：增删、选中、移动、整体平移。</summary>
public sealed class AnnotationManager
{
    private readonly List<Annotation> _items = new();
    private Annotation? _selected;
    private const int MaxUndoDepth = 100;
    private readonly Stack<List<Annotation>> _undoStack = new();
    private readonly Stack<List<Annotation>> _redoStack = new();

    public IReadOnlyList<Annotation> Items => _items;
    public Annotation? Selected => _selected;

    /// <summary>是否有可撤销的历史。</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>是否有可重做的历史。</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>当前列表的深拷贝快照。</summary>
    private List<Annotation> Snapshot() => _items.Select(a => a.Clone()).ToList();

    /// <summary>
    /// 推送撤销点：记录当前状态，清空重做栈。
    /// 修改动作（Add/DeleteSelected/Clear/MoveAllBy）自动调用；MoveSelectedBy 由 UI 在手势开始时调用一次。
    /// </summary>
    public void PushUndoPoint()
    {
        _undoStack.Push(Snapshot());
        if (_undoStack.Count > MaxUndoDepth)
        {
            // 丢弃最旧快照（栈底；Stack 只提供 Pop 栈顶，故重建列表后移除首元素）
            var all = _undoStack.ToList();
            all.RemoveAt(0);
            _undoStack.Clear();
            foreach (var s in all)
                _undoStack.Push(s);
        }
        _redoStack.Clear();
    }

    /// <summary>新增标注（自动记录撤销点）。</summary>
    public void Add(Annotation a)
    {
        PushUndoPoint();
        _items.Add(a);
    }

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

    /// <summary>删除选中（无选中时不记录）。</summary>
    public void DeleteSelected()
    {
        if (_selected == null)
            return;
        PushUndoPoint();
        _items.Remove(_selected);
        _selected = null;
    }

    /// <summary>清空全部（空时不记录）。</summary>
    public void Clear()
    {
        if (_items.Count == 0)
            return;
        PushUndoPoint();
        _items.Clear();
        _selected = null;
    }

    public void MoveSelectedBy(Vector delta)
    {
        if (_selected != null)
            _selected.Offset(delta);
    }

    /// <summary>选区整体移动时所有标注随动，保持相对位置（自动记录撤销点）。</summary>
    public void MoveAllBy(Vector delta)
    {
        if (_items.Count == 0)
            return;
        PushUndoPoint();
        foreach (var a in _items)
            a.Offset(delta);
    }

    /// <summary>撤销：当前状态入重做栈，还原到上一次快照。</summary>
    public void Undo()
    {
        if (!CanUndo)
            return;
        _redoStack.Push(Snapshot());
        ReplaceItems(_undoStack.Pop());
    }

    /// <summary>重做：撤销的逆操作。</summary>
    public void Redo()
    {
        if (!CanRedo)
            return;
        _undoStack.Push(Snapshot());
        ReplaceItems(_redoStack.Pop());
    }

    /// <summary>用快照替换当前条目，选中清空（历史快照不保留选中）。</summary>
    private void ReplaceItems(List<Annotation> snapshot)
    {
        _items.Clear();
        _items.AddRange(snapshot);
        _selected = null;
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
            case HighlightAnnotation h:
                return h.Rect.Contains(p) || DistanceToRectEdges(p, h.Rect) <= tolerance;
            case MosaicAnnotation m:
                return m.Rect.Contains(p) || DistanceToRectEdges(p, m.Rect) <= tolerance;
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