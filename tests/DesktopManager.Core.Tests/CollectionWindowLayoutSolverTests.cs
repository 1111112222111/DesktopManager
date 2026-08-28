using DesktopManager.Core;

namespace DesktopManager.Core.Tests;

public sealed class CollectionWindowLayoutSolverTests
{
    private static readonly LayoutRectangle Desktop = new(0, 0, 1600, 900);

    [Fact]
    public void MoveCompleted_WhenLeftAndRightNeighborsExist_PrefersLeftNeighborHeight()
    {
        var left = new LayoutRectangle(0, 100, 300, 240);
        var right = new LayoutRectangle(626, 80, 300, 360);
        var proposed = new LayoutRectangle(306, 105, 314, 300);

        var resolved = CollectionWindowLayoutSolver.ResolveMoved(proposed, [left, right], Desktop);

        Assert.Equal(left.Top, resolved.Top);
        Assert.Equal(left.Height, resolved.Height);
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, left));
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, right));
    }

    [Fact]
    public void MoveCompleted_WhenTopAndBottomNeighborsExist_PrefersTopNeighborWidth()
    {
        var top = new LayoutRectangle(200, 0, 420, 220);
        var bottom = new LayoutRectangle(180, 526, 520, 220);
        var proposed = new LayoutRectangle(205, 226, 360, 294);

        var resolved = CollectionWindowLayoutSolver.ResolveMoved(proposed, [top, bottom], Desktop);

        Assert.Equal(top.Left, resolved.Left);
        Assert.Equal(top.Width, resolved.Width);
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, top));
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, bottom));
    }

    [Fact]
    public void MoveCompleted_WhenTopAndBottomNeighborsExist_FillsVerticalGap()
    {
        var top = new LayoutRectangle(200, 0, 420, 220);
        var bottom = new LayoutRectangle(180, 560, 520, 220);
        var proposed = new LayoutRectangle(205, 230, 360, 260);

        var resolved = CollectionWindowLayoutSolver.ResolveMoved(proposed, [top, bottom], Desktop);

        Assert.Equal(top.Bottom + 6, resolved.Top);
        Assert.Equal(bottom.Top - 6, resolved.Bottom);
        Assert.Equal(top.Left, resolved.Left);
        Assert.Equal(top.Width, resolved.Width);
    }

    [Fact]
    public void ResizeWidth_WhenTopNeighborWidthIsClose_MatchesWidthAndKeepsLeftEdge()
    {
        var top = new LayoutRectangle(100, 0, 400, 220);
        var proposed = new LayoutRectangle(100, 226, 389, 300);

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [top],
            Desktop,
            CollectionWindowResizeEdge.Right,
            preventOverlap: false);

        Assert.Equal(100, resolved.Left);
        Assert.Equal(400, resolved.Width);
    }

    [Fact]
    public void ResizeHeight_WhenLeftNeighborHeightIsClose_MatchesHeightAndKeepsTopEdge()
    {
        var left = new LayoutRectangle(0, 120, 300, 360);
        var proposed = new LayoutRectangle(306, 120, 380, 346);

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [left],
            Desktop,
            CollectionWindowResizeEdge.Bottom,
            preventOverlap: false);

        Assert.Equal(120, resolved.Top);
        Assert.Equal(360, resolved.Height);
    }

    [Fact]
    public void ResizeFromLeft_WhenWidthMatches_KeepsRightEdgeFixed()
    {
        var top = new LayoutRectangle(300, 0, 420, 220);
        var proposed = new LayoutRectangle(311, 226, 409, 300);
        var originalRight = proposed.Right;

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [top],
            Desktop,
            CollectionWindowResizeEdge.Left,
            preventOverlap: false);

        Assert.Equal(420, resolved.Width);
        Assert.Equal(originalRight, resolved.Right);
    }

    [Fact]
    public void ResizeFromTop_WhenHeightMatches_KeepsBottomEdgeFixed()
    {
        var left = new LayoutRectangle(0, 200, 300, 380);
        var proposed = new LayoutRectangle(306, 211, 360, 369);
        var originalBottom = proposed.Bottom;

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [left],
            Desktop,
            CollectionWindowResizeEdge.Top,
            preventOverlap: false);

        Assert.Equal(380, resolved.Height);
        Assert.Equal(originalBottom, resolved.Bottom);
    }

    [Fact]
    public void ResizeCompleted_FromRightStopsBeforeOverlapAndKeepsLeftEdge()
    {
        var obstacle = new LayoutRectangle(500, 100, 300, 300);
        var proposed = new LayoutRectangle(100, 100, 450, 300);

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [obstacle],
            Desktop,
            CollectionWindowResizeEdge.Right,
            preventOverlap: true);

        Assert.Equal(proposed.Left, resolved.Left);
        Assert.Equal(6, obstacle.Left - resolved.Right);
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, obstacle));
    }

    [Fact]
    public void ResizeCompleted_FromBottomStopsBeforeOverlapAndKeepsTopEdge()
    {
        var obstacle = new LayoutRectangle(100, 500, 360, 260);
        var proposed = new LayoutRectangle(100, 100, 360, 450);

        var resolved = CollectionWindowLayoutSolver.ResolveResized(
            proposed,
            [obstacle],
            Desktop,
            CollectionWindowResizeEdge.Bottom,
            preventOverlap: true);

        Assert.Equal(proposed.Top, resolved.Top);
        Assert.Equal(6, obstacle.Top - resolved.Bottom);
        Assert.False(CollectionWindowLayoutSolver.Overlaps(resolved, obstacle));
    }
}
