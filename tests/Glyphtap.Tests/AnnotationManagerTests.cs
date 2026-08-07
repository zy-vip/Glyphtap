using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class AnnotationManagerTests
{
    private static RectangleAnnotation Rect(double x, double y, double w, double h) =>
        new() { Rect = new Rect(x, y, w, h), Color = Colors.Red, Thickness = 3 };

    [Fact]
    public void Add_与_Clear_管理条目()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(20, 20, 10, 10));
        Assert.Equal(2, mgr.Items.Count);
        mgr.Clear();
        Assert.Empty(mgr.Items);
        Assert.Null(mgr.Selected);
    }

    [Fact]
    public void TrySelectAt_命中内部_与_未命中()
    {
        var mgr = new AnnotationManager();
        var a = Rect(0, 0, 50, 50);
        mgr.Add(a);
        Assert.True(mgr.TrySelectAt(new Point(25, 25), 5));
        Assert.Same(a, mgr.Selected);
        Assert.False(mgr.TrySelectAt(new Point(100, 100), 5));
        Assert.Null(mgr.Selected);
    }

    [Fact]
    public void TrySelectAt_重叠时选最上层()
    {
        var mgr = new AnnotationManager();
        var lower = Rect(0, 0, 100, 100);
        var upper = Rect(10, 10, 20, 20);
        mgr.Add(lower);
        mgr.Add(upper);
        mgr.TrySelectAt(new Point(15, 15), 5);
        Assert.Same(upper, mgr.Selected);
    }

    [Fact]
    public void DeleteSelected_与_MoveSelectedBy()
    {
        var mgr = new AnnotationManager();
        var a = Rect(10, 10, 40, 40);
        mgr.Add(a);
        mgr.TrySelectAt(new Point(30, 30), 5);
        mgr.MoveSelectedBy(new Vector(5, -5));
        Assert.Equal(new Rect(15, 5, 40, 40), ((RectangleAnnotation)mgr.Selected!).Rect);

        mgr.DeleteSelected();
        Assert.Empty(mgr.Items);
    }

    [Fact]
    public void MoveAllBy_整体平移_标注相对选区保持()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(50, 50, 10, 10));
        mgr.MoveAllBy(new Vector(-10, 20));
        Assert.Equal(new Rect(-10, 20, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);
        Assert.Equal(new Rect(40, 70, 10, 10), ((RectangleAnnotation)mgr.Items[1]).Rect);
    }

    [Fact]
    public void HitTest_箭头与画笔按距离命中()
    {
        var arrow = new ArrowAnnotation { Start = new Point(0, 0), End = new Point(100, 0), Color = Colors.Red, Thickness = 3 };
        Assert.True(AnnotationManager.HitTest(arrow, new Point(50, 2), 5));
        Assert.False(AnnotationManager.HitTest(arrow, new Point(50, 30), 5));

        var pen = new PenAnnotation { Color = Colors.Red, Thickness = 3 };
        pen.AddPoint(new Point(0, 0));
        pen.AddPoint(new Point(0, 100));
        Assert.True(AnnotationManager.HitTest(pen, new Point(2, 50), 5));
        Assert.False(AnnotationManager.HitTest(pen, new Point(30, 50), 5));
    }
}