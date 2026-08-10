using System.Windows;
using System.Windows.Media;

namespace Glyphtap.Capture;

public enum AnnotationKind { Rectangle, Ellipse, Arrow, Pen, Highlight, Mosaic }

/// <summary>标注基类。坐标相对选区（物理像素）。</summary>
public abstract class Annotation
{
    public AnnotationKind Kind { get; init; }
    public Color Color { get; set; } = Colors.Red;
    public double Thickness { get; set; } = 3;

    public abstract Rect Bounds { get; }
    public abstract void Offset(Vector delta);
    public abstract void Resize(Rect newBounds);

    /// <summary>深拷贝（撤销快照用；画笔需复制点列表，其余复制值字段）。</summary>
    public abstract Annotation Clone();
}

public sealed class RectangleAnnotation : Annotation
{
    public Rect Rect;
    public RectangleAnnotation() { Kind = AnnotationKind.Rectangle; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new RectangleAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
}

public sealed class EllipseAnnotation : Annotation
{
    public Rect Rect;
    public EllipseAnnotation() { Kind = AnnotationKind.Ellipse; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new EllipseAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
}

public sealed class ArrowAnnotation : Annotation
{
    public Point Start;
    public Point End;
    public ArrowAnnotation() { Kind = AnnotationKind.Arrow; }
    public override Rect Bounds => new Rect(new Point(Math.Min(Start.X, End.X), Math.Min(Start.Y, End.Y)),
                                            new Point(Math.Max(Start.X, End.X), Math.Max(Start.Y, End.Y)));
    public override void Offset(Vector delta) { Start += delta; End += delta; }
    public override void Resize(Rect newBounds) { /* 箭头 MVP 不缩放，仅移动时随 Offset */ }
    public override Annotation Clone() =>
        new ArrowAnnotation { Start = Start, End = End, Color = Color, Thickness = Thickness };
}

public sealed class PenAnnotation : Annotation
{
    public List<Point> Points = new();
    public PenAnnotation() { Kind = AnnotationKind.Pen; }
    public void AddPoint(Point p) => Points.Add(p);
    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0)
                return Rect.Empty;
            var minX = Points.Min(p => p.X);
            var minY = Points.Min(p => p.Y);
            var maxX = Points.Max(p => p.X);
            var maxY = Points.Max(p => p.Y);
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }
    }
    public override void Offset(Vector delta) { for (var i = 0; i < Points.Count; i++) Points[i] += delta; }
    public override void Resize(Rect newBounds) { /* 画笔 MVP 不缩放 */ }
    public override Annotation Clone()
    {
        var copy = new PenAnnotation { Color = Color, Thickness = Thickness };
        copy.Points.AddRange(Points);
        return copy;
    }
}

/// <summary>高亮标注：半透明色块（无描边，粗细不参与渲染）。</summary>
public sealed class HighlightAnnotation : Annotation
{
    public Rect Rect;
    public HighlightAnnotation() { Kind = AnnotationKind.Highlight; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new HighlightAnnotation { Rect = Rect, Color = Color, Thickness = Thickness };
}

/// <summary>马赛克标注：矩形区域像素块化（无描边，粗细/颜色不参与渲染）。</summary>
public sealed class MosaicAnnotation : Annotation
{
    public Rect Rect;

    /// <summary>马赛克块大小（物理像素）。</summary>
    public double BlockSize = 8;

    public MosaicAnnotation() { Kind = AnnotationKind.Mosaic; }
    public override Rect Bounds => Rect;
    public override void Offset(Vector delta) => Rect.Offset(delta);
    public override void Resize(Rect newBounds) => Rect = newBounds;
    public override Annotation Clone() =>
        new MosaicAnnotation { Rect = Rect, BlockSize = BlockSize, Color = Color, Thickness = Thickness };
}