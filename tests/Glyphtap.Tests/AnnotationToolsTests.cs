using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class AnnotationToolsTests
{
    private const double T = 0.01;

    [Fact]
    public void 矩形工具_拖拽产出归一化矩形()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(50, 60));
        var a = tool.End();
        var r = Assert.IsType<RectangleAnnotation>(a);
        Assert.Equal(new Rect(50, 60, 50, 40), r.Rect);
        Assert.Equal(Colors.Red, r.Color);
        Assert.Equal(3, r.Thickness);
    }

    [Fact]
    public void 矩形工具_尺寸过小返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Rectangle, Colors.Red, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(100, 100));
        Assert.Null(tool.End());
    }

    [Fact]
    public void 椭圆工具_产出椭圆()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Ellipse, Colors.Blue, 5);
        tool.Begin(new Point(0, 0));
        tool.Move(new Point(100, 80));
        var e = Assert.IsType<EllipseAnnotation>(tool.End());
        Assert.Equal(new Rect(0, 0, 100, 80), e.Rect);
    }

    [Fact]
    public void 箭头几何_尖在终点且两翼对称()
    {
        var (tip, left, right) = ArrowGeometry.ComputeHead(new Point(0, 0), new Point(100, 0), 12, 30);
        Assert.Equal(new Point(100, 0), tip);
        Assert.Equal(100, right.X, T);
        Assert.Equal(100, left.X, T);
        Assert.Equal(-Math.Tan(30 * Math.PI / 180) * 12, right.Y, T);
        Assert.Equal(+Math.Tan(30 * Math.PI / 180) * 12, left.Y, T);
    }

    [Fact]
    public void 箭头工具_横向箭头()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Arrow, Colors.Green, 2);
        tool.Begin(new Point(10, 10));
        tool.Move(new Point(200, 10));
        var a = Assert.IsType<ArrowAnnotation>(tool.End());
        Assert.Equal(new Point(10, 10), a.Start);
        Assert.Equal(new Point(200, 10), a.End);
    }

    [Fact]
    public void 画笔工具_累积点集()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Pen, Colors.Black, 1);
        tool.Begin(new Point(1, 1));
        tool.Move(new Point(2, 2));
        tool.Move(new Point(3, 3));
        var pen = Assert.IsType<PenAnnotation>(tool.End());
        Assert.Equal(3, pen.Points.Count);
    }

    [Fact]
    public void 画笔工具_点不足返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Pen, Colors.Black, 1);
        tool.Begin(new Point(1, 1));
        Assert.Null(tool.End());
    }
}