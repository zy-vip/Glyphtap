using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class HighlightMosaicTests
{
    [Fact]
    public void 高亮工具_拖拽产出归一化高亮标注()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Highlight, Colors.Yellow, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(50, 60));
        var a = tool.End();
        var h = Assert.IsType<HighlightAnnotation>(a);
        Assert.Equal(new Rect(50, 60, 50, 40), h.Rect);
        Assert.Equal(Colors.Yellow, h.Color);
    }

    [Fact]
    public void 高亮工具_尺寸过小返回null()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Highlight, Colors.Yellow, 3);
        tool.Begin(new Point(100, 100));
        tool.Move(new Point(100, 100));
        Assert.Null(tool.End());
    }

    [Fact]
    public void 马赛克工具_拖拽产出马赛克标注_块大小默认8()
    {
        var tool = AnnotationToolFactory.Create(AnnotationKind.Mosaic, Colors.Red, 3);
        tool.Begin(new Point(0, 0));
        tool.Move(new Point(100, 80));
        var m = Assert.IsType<MosaicAnnotation>(tool.End());
        Assert.Equal(new Rect(0, 0, 100, 80), m.Rect);
        Assert.Equal(8, m.BlockSize);
    }

    [Fact]
    public void HitTest_高亮按矩形内部命中()
    {
        var h = new HighlightAnnotation { Rect = new Rect(0, 0, 50, 50), Color = Colors.Yellow };
        Assert.True(AnnotationManager.HitTest(h, new Point(25, 25), 5));
        Assert.False(AnnotationManager.HitTest(h, new Point(100, 100), 5));
    }

    [Fact]
    public void HitTest_马赛克按矩形内部命中()
    {
        var m = new MosaicAnnotation { Rect = new Rect(10, 10, 40, 40) };
        Assert.True(AnnotationManager.HitTest(m, new Point(20, 20), 5));
        Assert.False(AnnotationManager.HitTest(m, new Point(100, 100), 5));
    }

    [Fact]
    public void Clone_高亮与马赛克拷贝字段()
    {
        var h = new HighlightAnnotation { Rect = new Rect(1, 2, 3, 4), Color = Colors.Green };
        var hc = (HighlightAnnotation)h.Clone();
        Assert.Equal(new Rect(1, 2, 3, 4), hc.Rect);
        Assert.Equal(Colors.Green, hc.Color);

        var m = new MosaicAnnotation { Rect = new Rect(0, 0, 10, 20), Color = Colors.Blue, Thickness = 5 };
        var mc = (MosaicAnnotation)m.Clone();
        Assert.Equal(new Rect(0, 0, 10, 20), mc.Rect);
        Assert.Equal(8, mc.BlockSize);
        Assert.Equal(Colors.Blue, mc.Color);
        Assert.Equal(5, mc.Thickness);
    }
}