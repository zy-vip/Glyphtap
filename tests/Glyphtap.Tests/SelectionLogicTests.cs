using System.Windows;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class SelectionLogicTests
{
    [Fact]
    public void 拖拽创建_正向与反向都归一化()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(200, 160));
        Assert.Equal(new Rect(100, 100, 100, 60), logic.Selection);
        Assert.Equal(SelectionMode.Creating, logic.Mode);

        logic.Clear();
        logic.OnMouseDown(new Point(200, 160));
        logic.OnMouseMove(new Point(100, 100));
        Assert.Equal(new Rect(100, 100, 100, 60), logic.Selection);
    }

    [Fact]
    public void 有选区时_按下选区内_进入移动模式()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(200, 200));
        Assert.Equal(SelectionMode.Moving, logic.Mode);

        logic.OnMouseMove(new Point(250, 220));
        Assert.Equal(new Rect(150, 120, 200, 200), logic.Selection);
    }

    [Fact]
    public void 按下手柄_进入缩放模式()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(300, 300)); // 右下角手柄
        Assert.Equal(SelectionMode.Resizing, logic.Mode);

        logic.OnMouseMove(new Point(350, 380));
        Assert.Equal(new Rect(100, 100, 250, 280), logic.Selection);
    }

    [Fact]
    public void 反向拖动手柄_选区归一化()
    {
        // 初始选区 (100,100,200,200)（Right=300, Bottom=300）。按 TopRight 手柄(300,100) 拖到 p=(80,160)：
        // 公式：保持 start.X 与 start.Bottom 固定，top=p.Y, right=p.X → Rect(100,160,-20,140)
        // Normalize（负宽交换起终点）→ (80,160,20,140)，ApplyMinSize 不变
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(300, 100)); // TopRight 手柄
        logic.OnMouseMove(new Point(80, 160));  // right 越过左边界，触发归一化
        Assert.Equal(new Rect(80, 160, 20, 140), logic.Selection);
    }

    [Fact]
    public void 选区不小于最小尺寸()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(102, 101));
        var s = logic.Selection;
        Assert.True(s.Width >= SelectionLogic.MinSize && s.Height >= SelectionLogic.MinSize);
    }

    [Fact]
    public void 选区外按下_重新创建选区()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.OnMouseUp();

        logic.OnMouseDown(new Point(500, 500));
        Assert.Equal(SelectionMode.Creating, logic.Mode);
        logic.OnMouseMove(new Point(600, 550));
        Assert.Equal(new Rect(500, 500, 100, 50), logic.Selection);
    }

    [Fact]
    public void HitTestHandle_命中各手柄与内部()
    {
        var rect = new Rect(100, 100, 200, 150);
        Assert.Equal(ResizeHandle.TopLeft, SelectionLogic.HitTestHandle(new Point(102, 102), rect));
        Assert.Equal(ResizeHandle.BottomRight, SelectionLogic.HitTestHandle(new Point(298, 248), rect));
        Assert.Equal(ResizeHandle.Top, SelectionLogic.HitTestHandle(new Point(200, 102), rect));
        Assert.Equal(ResizeHandle.Left, SelectionLogic.HitTestHandle(new Point(102, 175), rect));
        Assert.Equal(ResizeHandle.None, SelectionLogic.HitTestHandle(new Point(200, 175), rect));
        Assert.Equal(ResizeHandle.None, SelectionLogic.HitTestHandle(new Point(400, 400), rect));
    }

    [Fact]
    public void Clear_清空选区()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseMove(new Point(300, 300));
        logic.Clear();
        Assert.False(logic.HasSelection);
        Assert.Equal(SelectionMode.None, logic.Mode);
    }

    [Fact]
    public void OnMouseUp_纯点击未拖动不建立选区()
    {
        var logic = new SelectionLogic();
        logic.OnMouseDown(new Point(100, 100));
        logic.OnMouseUp();
        Assert.False(logic.HasSelection);
        Assert.Equal(SelectionMode.None, logic.Mode);
    }
}