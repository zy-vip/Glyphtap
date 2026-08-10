using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Glyphtap.Capture;

/// <summary>把标注绘制到 DrawingContext（坐标相对选区左上角，物理像素）。</summary>
public static class AnnotationRenderer
{
    public static void Draw(DrawingContext dc, Annotation a)
    {
        var pen = new Pen(new SolidColorBrush(a.Color), a.Thickness) { LineJoin = PenLineJoin.Round };
        switch (a)
        {
            case HighlightAnnotation h:
                // 高亮：固定 35% 不透明度色块，无描边
                dc.DrawRectangle(
                    new SolidColorBrush(Color.FromArgb(90, h.Color.R, h.Color.G, h.Color.B)),
                    null,
                    h.Rect);
                break;
            case RectangleAnnotation r:
                dc.DrawRectangle(null, pen, r.Rect);
                break;
            case EllipseAnnotation e:
                dc.DrawEllipse(null, pen, new Point(e.Rect.X + e.Rect.Width / 2, e.Rect.Y + e.Rect.Height / 2), e.Rect.Width / 2, e.Rect.Height / 2);
                break;
            case ArrowAnnotation ar:
            {
                var (tip, left, right) = ArrowGeometry.ComputeHead(ar.Start, ar.End);
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(ar.Start, false, false);
                    ctx.LineTo(ar.End, true, false);
                    ctx.BeginFigure(tip, true, true);
                    ctx.LineTo(left, true, true);
                    ctx.LineTo(right, true, true);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
            case PenAnnotation penA:
            {
                if (penA.Points.Count < 2)
                    break;
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(penA.Points[0], false, false);
                    ctx.PolyLineTo(penA.Points.Skip(1).ToList(), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(null, pen, geo);
                break;
            }
        }
    }
}