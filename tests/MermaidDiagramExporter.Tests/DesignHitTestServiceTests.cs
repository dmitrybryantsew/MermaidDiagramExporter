using System;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class DesignHitTestServiceTests
{
    private ClassRectangle CreateTestRect()
    {
        return new ClassRectangle(new DesignClass { Name = "Test" }, new DesignGraph());
    }

    [Fact]
    public void DesignHitResult_None_ReturnsCorrectValues()
    {
        var result = DesignHitResult.None();
        Assert.Null(result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.None, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_Body_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.Body(rect);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.Body, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_Header_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.Header(rect);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.Header, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_Member_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.Member(rect, 42);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.Member, result.Kind);
        Assert.Equal(42, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_ResizeHandle_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.ResizeHandle(rect);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.ResizeHandle, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_LeftPort_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.LeftPort(rect);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.LeftPort, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }

    [Fact]
    public void DesignHitResult_RightPort_ReturnsCorrectValues()
    {
        var rect = CreateTestRect();
        var result = DesignHitResult.RightPort(rect);
        Assert.Same(rect, result.Rectangle);
        Assert.Equal(ClassRectangleHitTest.RightPort, result.Kind);
        Assert.Equal(-1, result.MemberIndex);
    }
}
