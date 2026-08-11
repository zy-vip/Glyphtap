using System;
using System.Windows;
using System.Windows.Media;
using Glyphtap.Capture;
using Xunit;

namespace Glyphtap.Tests;

public class UndoRedoTests
{
    private static RectangleAnnotation Rect(double x, double y, double w, double h) =>
        new() { Rect = new Rect(x, y, w, h), Color = Colors.Red, Thickness = 3 };

    [Fact]
    public void Add_两次_Undo逐步回退_Redo逐步前进()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(20, 20, 10, 10));
        Assert.Equal(2, mgr.Items.Count);

        mgr.Undo();
        Assert.Single(mgr.Items);
        Assert.Equal(0, ((RectangleAnnotation)mgr.Items[0]).Rect.X);

        mgr.Undo();
        Assert.Empty(mgr.Items);

        mgr.Redo();
        Assert.Single(mgr.Items);

        mgr.Redo();
        Assert.Equal(2, mgr.Items.Count);
    }

    [Fact]
    public void DeleteSelected_可撤销恢复()
    {
        var mgr = new AnnotationManager();
        var a = Rect(10, 10, 40, 40);
        mgr.Add(a);
        mgr.TrySelectAt(new Point(30, 30), 5);
        mgr.DeleteSelected();
        Assert.Empty(mgr.Items);

        mgr.Undo();
        Assert.Single(mgr.Items);
        Assert.Equal(new Rect(10, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void Clear_可撤销恢复全部()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Add(Rect(50, 50, 10, 10));
        mgr.Clear();
        Assert.Empty(mgr.Items);

        mgr.Undo();
        Assert.Equal(2, mgr.Items.Count);
    }

    [Fact]
    public void PushUndoPoint_后移动_可撤销位置恢复()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(10, 10, 40, 40));
        mgr.TrySelectAt(new Point(30, 30), 5);

        mgr.PushUndoPoint(); // UI 拖拽手势开始时调用一次
        mgr.MoveSelectedBy(new Vector(5, -5));
        mgr.MoveSelectedBy(new Vector(5, 0));
        Assert.Equal(new Rect(20, 5, 40, 40), ((RectangleAnnotation)mgr.Selected!).Rect);

        mgr.Undo();
        Assert.Equal(new Rect(10, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void MoveAllBy_自动记录_可撤销()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.MoveAllBy(new Vector(-10, 20));
        Assert.Equal(new Rect(-10, 20, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);

        mgr.Undo();
        Assert.Equal(new Rect(0, 0, 10, 10), ((RectangleAnnotation)mgr.Items[0]).Rect);
    }

    [Fact]
    public void 新动作_清空重做栈()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Undo();
        Assert.True(mgr.CanRedo);

        mgr.Add(Rect(50, 50, 10, 10)); // 新动作后重做不可用
        Assert.False(mgr.CanRedo);
        Assert.True(mgr.CanUndo);
    }

    [Fact]
    public void 无历史时_UndoRedo为空操作()
    {
        var mgr = new AnnotationManager();
        mgr.Undo();
        mgr.Redo();
        Assert.False(mgr.CanUndo);
        Assert.False(mgr.CanRedo);
    }

    [Fact]
    public void Undo后_清空选中()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.TrySelectAt(new Point(5, 5), 5);
        Assert.NotNull(mgr.Selected);

        mgr.Undo();
        Assert.Null(mgr.Selected);
    }

    [StaFact]
    public void PushUndoPoint_超过上限_最新快照保留且旧快照被裁剪()
    {
        var mgr = new AnnotationManager();
        mgr.PushUndoPoint(); // 第 1 次推点：空状态（对应拖拽手势开始的推点）
        for (var i = 1; i <= 100; i++)
            mgr.Add(Rect(i * 10, 0, 10, 10)); // 第 2..101 次推点（Add 自动推）
        Assert.Equal(100, mgr.Items.Count);

        mgr.Undo(); // 第一步只回退一个动作：撤销后 = 第 99 次 Add 之后的状态（99 个标注），而非被清空
        Assert.Equal(99, mgr.Items.Count);
        Assert.Equal(990, ((RectangleAnnotation)mgr.Items[^1]).Rect.X); // 最后一个 = R99
        Assert.True(mgr.CanUndo);

        for (var i = 0; i < 98; i++)
            mgr.Undo();
        Assert.Single(mgr.Items); // 栈内恰好 100 个撤销点：98 次后仅剩最旧的 1 个
        Assert.True(mgr.CanUndo);

        mgr.Undo(); // 第 100 次 Undo 恰好回到空列表
        Assert.Empty(mgr.Items);
        Assert.False(mgr.CanUndo);

        mgr.Undo(); // 无可撤销项：空操作
        Assert.Empty(mgr.Items);
    }

    [StaFact]
    public void 空列表时_Clear不推撤销点()
    {
        var mgr = new AnnotationManager();
        mgr.Clear();
        Assert.False(mgr.CanUndo);
    }

    [StaFact]
    public void 无选中时_DeleteSelected不推撤销点()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(0, 0, 10, 10));
        mgr.Undo(); // 撤销 Add，回到空且无历史
        Assert.False(mgr.CanUndo);

        mgr.DeleteSelected(); // 无选中：不推点、不改变列表
        Assert.False(mgr.CanUndo);
        Assert.Empty(mgr.Items);
    }

    [StaFact]
    public void 拖拽多次移动_只记录一个撤销点()
    {
        var mgr = new AnnotationManager();
        mgr.Add(Rect(10, 10, 40, 40)); // 撤销点 1
        mgr.TrySelectAt(new Point(30, 30), 5);

        mgr.PushUndoPoint(); // 拖拽手势开始：撤销点 2
        mgr.MoveSelectedBy(new Vector(5, 0));
        mgr.MoveSelectedBy(new Vector(5, 0));
        Assert.Equal(new Rect(20, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);

        mgr.Undo(); // 一次撤销回到拖拽前位置
        Assert.Equal(new Rect(10, 10, 40, 40), ((RectangleAnnotation)mgr.Items[0]).Rect);
        Assert.True(mgr.CanUndo); // 多次移动只记了一个点：Add 的撤销点仍在

        mgr.Undo(); // 撤销 Add
        Assert.Empty(mgr.Items);
        Assert.False(mgr.CanUndo);
    }

    [Fact]
    public void Clone_画笔深拷贝_互不影响()
    {
        var pen = new PenAnnotation { Color = Colors.Red, Thickness = 3 };
        pen.AddPoint(new Point(0, 0));
        pen.AddPoint(new Point(0, 100));
        var copy = (PenAnnotation)pen.Clone();

        copy.AddPoint(new Point(100, 100));
        Assert.Equal(2, pen.Points.Count);
        Assert.Equal(3, copy.Points.Count);
        Assert.Equal(Colors.Red, copy.Color);
        Assert.Equal(3, copy.Thickness);
    }

    [Fact]
    public void Clone_各类型字段一致()
    {
        var arrow = new ArrowAnnotation { Start = new Point(1, 2), End = new Point(9, 8), Color = Colors.Green, Thickness = 2 };
        var a = (ArrowAnnotation)arrow.Clone();
        Assert.Equal(new Point(1, 2), a.Start);
        Assert.Equal(new Point(9, 8), a.End);
        Assert.Equal(Colors.Green, a.Color);
        Assert.Equal(2, a.Thickness);
        Assert.Equal(AnnotationKind.Arrow, a.Kind);
    }
}
