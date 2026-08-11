using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class TextAnnotationTests
{
    [Fact]
    public void 字号映射_细中粗对应12_16_20()
    {
        Assert.Equal(12, TextMetrics.FontSizeForThickness(1));
        Assert.Equal(16, TextMetrics.FontSizeForThickness(3));
        Assert.Equal(20, TextMetrics.FontSizeForThickness(5));
    }

    [StaFact]
    public void 文本测量_宽度随文字增长_空文本为零宽()
    {
        Assert.True(TextMetrics.Measure("WW", 16).Width > TextMetrics.Measure("W", 16).Width);
        Assert.Equal(0, TextMetrics.Measure("", 16).Width);
    }

    [StaFact]
    public void 文本测量_字号越大宽度越大()
    {
        Assert.True(TextMetrics.Measure("文本", 20).Width > TextMetrics.Measure("文本", 12).Width);
    }

    [Fact]
    public void 文本标注_静态属性与文本框尺寸()
    {
        var t = new TextAnnotation { Text = "你好", Position = new Point(10, 20), TextSize = new Size(50, 20), Color = Colors.Red, Thickness = 3 };
        Assert.Equal(AnnotationKind.Text, t.Kind);
        Assert.Equal(new Rect(10, 20, 50, 20), t.Bounds);
    }

    [Fact]
    public void 文本标注_Offset平移_Resize无操作()
    {
        var t = new TextAnnotation { Text = "x", Position = new Point(1, 2), TextSize = new Size(10, 10) };
        t.Offset(new Vector(5, 7));
        Assert.Equal(new Point(6, 9), t.Position);
        t.Resize(new Rect(0, 0, 99, 99));
        Assert.Equal("x", t.Text);
    }

    [Fact]
    public void 文本标注_Clone深拷贝_改原文本不影响克隆()
    {
        var t = new TextAnnotation { Text = "原", Position = new Point(1, 2), TextSize = new Size(10, 10) };
        var c = (TextAnnotation)t.Clone();
        c.Text = "改";
        Assert.Equal("原", t.Text);
        Assert.Equal(new Point(1, 2), c.Position);
        Assert.Equal(new Size(10, 10), c.TextSize);
    }

    [Fact]
    public void 文本命中_文本框内命中_框外不命中()
    {
        var t = new TextAnnotation { Text = "x", Position = new Point(0, 0), TextSize = new Size(50, 20) };
        Assert.True(AnnotationManager.HitTest(t, new Point(10, 10), 5));
        Assert.False(AnnotationManager.HitTest(t, new Point(100, 100), 5));
    }

    [Fact]
    public void 管理器_添加文本标注后可撤销()
    {
        var m = new AnnotationManager();
        m.Add(new TextAnnotation { Text = "a", Position = new Point(0, 0), TextSize = new Size(10, 10) });
        m.Add(new TextAnnotation { Text = "b", Position = new Point(0, 0), TextSize = new Size(10, 10) });
        Assert.Equal(2, m.Items.Count);
        m.Undo();
        Assert.Single(m.Items);
        m.Redo();
        Assert.Equal(2, m.Items.Count);
    }
}
