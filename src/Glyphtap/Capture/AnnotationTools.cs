using System.Globalization;
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
        AnnotationKind.Highlight => new HighlightTool(color, thickness),
        AnnotationKind.Mosaic => new MosaicTool(color, thickness),
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

internal sealed class HighlightTool : ToolBase
{
    public HighlightTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Highlight;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new HighlightAnnotation { Rect = r, Color = Color, Thickness = Thickness };
    }
}

internal sealed class MosaicTool : ToolBase
{
    public MosaicTool(Color color, double thickness) : base(color, thickness) { }
    public override AnnotationKind Kind => AnnotationKind.Mosaic;

    protected override Annotation? BuildAnnotation(bool isPreview)
    {
        var r = Normalize(Start, Last);
        return r.Width < 1 || r.Height < 1 ? null : new MosaicAnnotation { Rect = r, BlockSize = 8 };
    }
}

/// <summary>文本测量与字号映射。测量单位物理像素（pixelsPerDip=1.0）。</summary>
public static class TextMetrics
{
    public const string FontFamilyName = "Microsoft YaHei";

    /// <summary>粗细档 → 字号（物理像素）：细12 / 中16 / 粗20。</summary>
    public static double FontSizeForThickness(double thickness) => thickness switch
    {
        <= 1.5 => 12,
        <= 4 => 16,
        _ => 20,
    };

    /// <summary>按物理像素字号测量文本宽度高度。需 STA。</summary>
    public static Size Measure(string text, double fontSizePx)
    {
        if (text.Length == 0)
            return new Size(0, 0);
        var ft = new FormattedText(text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamilyName),
            fontSizePx,
            Brushes.Black,
            pixelsPerDip: 1.0);
        return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
    }
}

/// <summary>文本工具占位：点按语义不进工厂，令其无绘制行为。</summary>
internal sealed class NoOpTool : IAnnotationTool
{
    public static readonly NoOpTool Instance = new();
    public AnnotationKind Kind => AnnotationKind.Text;
    public bool IsDrawing => false;
    public void Begin(Point p) { }
    public void Move(Point p) { }
    public Annotation? GetPreview() => null;
    public Annotation? End() => null;
}