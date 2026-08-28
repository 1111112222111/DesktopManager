namespace DesktopManager.Core;

public readonly record struct LayoutRectangle(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;
    public double Bottom => Top + Height;
}

[Flags]
public enum CollectionWindowResizeEdge
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}

public enum CollectionWindowLayoutChangeKind
{
    MoveCompleted,
    ResizeLive,
    ResizeCompleted
}

public readonly record struct CollectionWindowLayoutChange(
    CollectionWindowLayoutChangeKind Kind,
    CollectionWindowResizeEdge ResizeEdge = CollectionWindowResizeEdge.None);

public static class CollectionWindowLayoutSolver
{
    private const double Epsilon = 0.1;

    public static LayoutRectangle ResolveMoved(
        LayoutRectangle proposed,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        double gap = 6,
        double snapDistance = 18,
        double minimumWidth = 280,
        double minimumHeight = 180)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        var current = Normalize(proposed, area, minimumWidth, minimumHeight);
        current = FindNearestFreePosition(current, obstacles, area, gap);
        current = SnapPosition(current, obstacles, area, gap, snapDistance);

        var left = FindHorizontalNeighbor(current, obstacles, gap, snapDistance, leftSide: true);
        var right = FindHorizontalNeighbor(current, obstacles, gap, snapDistance, leftSide: false);
        var horizontalNeighbor = left ?? right;
        if (horizontalNeighbor is { } horizontal)
        {
            current = current with
            {
                Top = horizontal.Top,
                Height = Math.Clamp(horizontal.Height, minimumHeight, area.Height)
            };
        }

        var top = FindVerticalNeighbor(current, obstacles, gap, snapDistance, topSide: true);
        var bottom = FindVerticalNeighbor(current, obstacles, gap, snapDistance, topSide: false);
        var boundingTop = top ?? FindBoundingVerticalNeighbor(current, obstacles, topSide: true);
        var boundingBottom = bottom ?? FindBoundingVerticalNeighbor(current, obstacles, topSide: false);
        if ((top is not null || bottom is not null)
            && boundingTop is { } upperBound
            && boundingBottom is { } lowerBound)
        {
            top = upperBound;
            bottom = lowerBound;
        }
        if (top is { } upper && bottom is { } lower)
        {
            var fillTop = upper.Bottom + gap;
            var fillHeight = lower.Top - gap - fillTop;
            if (fillHeight >= minimumHeight)
            {
                current = current with
                {
                    Top = fillTop,
                    Height = fillHeight
                };
            }
        }
        var verticalNeighbor = top ?? bottom;
        if (verticalNeighbor is { } vertical)
        {
            current = current with
            {
                Left = vertical.Left,
                Width = Math.Clamp(vertical.Width, minimumWidth, area.Width)
            };
        }

        current = ClampPosition(current, area);
        return FindNearestFreePosition(current, obstacles, area, gap);
    }

    public static LayoutRectangle ResolveResized(
        LayoutRectangle proposed,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        CollectionWindowResizeEdge edge,
        bool preventOverlap,
        double gap = 6,
        double sizeMatchDistance = 18,
        double minimumWidth = 280,
        double minimumHeight = 180)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        var current = ClampResized(proposed, area, edge, minimumWidth, minimumHeight);
        if ((edge & (CollectionWindowResizeEdge.Left | CollectionWindowResizeEdge.Right)) != 0)
        {
            var top = FindVerticalNeighbor(current, obstacles, gap, sizeMatchDistance, topSide: true);
            var bottom = FindVerticalNeighbor(current, obstacles, gap, sizeMatchDistance, topSide: false);
            var neighbor = top ?? bottom;
            if (neighbor is { } vertical
                && Math.Abs(vertical.Width - current.Width) <= sizeMatchDistance)
            {
                current = ApplyWidth(current, vertical.Width, edge);
            }
        }
        if ((edge & (CollectionWindowResizeEdge.Top | CollectionWindowResizeEdge.Bottom)) != 0)
        {
            var left = FindHorizontalNeighbor(current, obstacles, gap, sizeMatchDistance, leftSide: true);
            var right = FindHorizontalNeighbor(current, obstacles, gap, sizeMatchDistance, leftSide: false);
            var neighbor = left ?? right;
            if (neighbor is { } horizontal
                && Math.Abs(horizontal.Height - current.Height) <= sizeMatchDistance)
            {
                current = ApplyHeight(current, horizontal.Height, edge);
            }
        }

        current = ClampResized(current, area, edge, minimumWidth, minimumHeight);
        return preventOverlap
            ? ConstrainResize(current, obstacles, area, edge, gap, minimumWidth, minimumHeight)
            : current;
    }

    public static bool Overlaps(LayoutRectangle first, LayoutRectangle second) =>
        first.Left < second.Right - Epsilon
        && first.Right > second.Left + Epsilon
        && first.Top < second.Bottom - Epsilon
        && first.Bottom > second.Top + Epsilon;

    private static LayoutRectangle FindNearestFreePosition(
        LayoutRectangle origin,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        double gap)
    {
        if (!obstacles.Any(obstacle => Overlaps(origin, obstacle)))
        {
            return origin;
        }
        var lefts = obstacles.Select(obstacle => obstacle.Right + gap)
            .Concat(obstacles.Select(obstacle => obstacle.Left - gap - origin.Width))
            .Append(area.Left)
            .Append(area.Right - origin.Width)
            .Distinct();
        var tops = obstacles.Select(obstacle => obstacle.Bottom + gap)
            .Concat(obstacles.Select(obstacle => obstacle.Top - gap - origin.Height))
            .Append(area.Top)
            .Append(area.Bottom - origin.Height)
            .Distinct();
        LayoutRectangle? best = null;
        var bestDistance = double.MaxValue;
        foreach (var left in lefts)
        {
            foreach (var top in tops)
            {
                var candidate = new LayoutRectangle(left, top, origin.Width, origin.Height);
                if (!IsInside(candidate, area)
                    || obstacles.Any(obstacle => Overlaps(candidate, obstacle)))
                {
                    continue;
                }
                var distance = Math.Abs(left - origin.Left) + Math.Abs(top - origin.Top);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }
        return best ?? FindAdaptiveFreeRectangle(origin, obstacles, area, gap) ?? origin;
    }

    private static LayoutRectangle? FindAdaptiveFreeRectangle(
        LayoutRectangle origin,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        double gap)
    {
        const double minimumWidth = 280;
        const double minimumHeight = 180;
        var lefts = obstacles.Select(obstacle => obstacle.Right + gap).Append(area.Left).Distinct();
        var tops = obstacles.Select(obstacle => obstacle.Bottom + gap).Append(area.Top).Distinct();
        LayoutRectangle? best = null;
        var bestScore = double.MaxValue;
        foreach (var left in lefts)
        {
            foreach (var top in tops)
            {
                var widths = obstacles.Select(obstacle => obstacle.Left - gap - left)
                    .Append(Math.Min(origin.Width, area.Right - left));
                var heights = obstacles.Select(obstacle => obstacle.Top - gap - top)
                    .Append(Math.Min(origin.Height, area.Bottom - top));
                foreach (var width in widths.Where(width => width >= minimumWidth && width <= origin.Width))
                {
                    foreach (var height in heights.Where(height => height >= minimumHeight && height <= origin.Height))
                    {
                        var candidate = new LayoutRectangle(left, top, width, height);
                        if (!IsInside(candidate, area)
                            || obstacles.Any(obstacle => Overlaps(candidate, obstacle)))
                        {
                            continue;
                        }
                        var score = Math.Abs(left - origin.Left) + Math.Abs(top - origin.Top)
                            + (origin.Width - width + origin.Height - height) * 1.5;
                        if (score < bestScore)
                        {
                            best = candidate;
                            bestScore = score;
                        }
                    }
                }
            }
        }
        return best;
    }

    private static LayoutRectangle SnapPosition(
        LayoutRectangle current,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        double gap,
        double threshold)
    {
        current = current with
        {
            Left = Snap(current.Left, [area.Left, area.Right - current.Width], threshold),
            Top = Snap(current.Top, [area.Top, area.Bottom - current.Height], threshold)
        };
        foreach (var obstacle in obstacles)
        {
            if (RangesOverlap(current.Top, current.Bottom, obstacle.Top, obstacle.Bottom))
            {
                var targets = new[] { obstacle.Right + gap, obstacle.Left - gap - current.Width };
                if (targets.Any(target => Math.Abs(target - current.Left) <= threshold))
                {
                    current = current with
                    {
                        Left = Snap(current.Left, targets, threshold),
                        Top = Snap(current.Top, [obstacle.Top, obstacle.Bottom - current.Height], threshold)
                    };
                }
            }
            if (RangesOverlap(current.Left, current.Right, obstacle.Left, obstacle.Right))
            {
                var targets = new[] { obstacle.Bottom + gap, obstacle.Top - gap - current.Height };
                if (targets.Any(target => Math.Abs(target - current.Top) <= threshold))
                {
                    current = current with
                    {
                        Top = Snap(current.Top, targets, threshold),
                        Left = Snap(current.Left, [obstacle.Left, obstacle.Right - current.Width], threshold)
                    };
                }
            }
        }
        return ClampPosition(current, area);
    }

    private static LayoutRectangle ConstrainResize(
        LayoutRectangle current,
        IReadOnlyList<LayoutRectangle> obstacles,
        LayoutRectangle area,
        CollectionWindowResizeEdge edge,
        double gap,
        double minimumWidth,
        double minimumHeight)
    {
        for (var pass = 0; pass <= obstacles.Count; pass++)
        {
            var obstacle = obstacles.FirstOrDefault(candidate => Overlaps(current, candidate));
            if (!Overlaps(current, obstacle))
            {
                break;
            }
            var candidates = new List<LayoutRectangle>();
            if ((edge & CollectionWindowResizeEdge.Right) != 0
                && obstacle.Left - gap - current.Left >= minimumWidth)
            {
                candidates.Add(current with { Width = obstacle.Left - gap - current.Left });
            }
            if ((edge & CollectionWindowResizeEdge.Left) != 0
                && current.Right - obstacle.Right - gap >= minimumWidth)
            {
                candidates.Add(current with
                {
                    Left = obstacle.Right + gap,
                    Width = current.Right - obstacle.Right - gap
                });
            }
            if ((edge & CollectionWindowResizeEdge.Bottom) != 0
                && obstacle.Top - gap - current.Top >= minimumHeight)
            {
                candidates.Add(current with { Height = obstacle.Top - gap - current.Top });
            }
            if ((edge & CollectionWindowResizeEdge.Top) != 0
                && current.Bottom - obstacle.Bottom - gap >= minimumHeight)
            {
                candidates.Add(current with
                {
                    Top = obstacle.Bottom + gap,
                    Height = current.Bottom - obstacle.Bottom - gap
                });
            }
            var next = candidates
                .OrderBy(candidate => Math.Abs(candidate.Width - current.Width)
                    + Math.Abs(candidate.Height - current.Height))
                .FirstOrDefault();
            if (next.Width <= 0)
            {
                break;
            }
            current = next;
        }
        return ClampResized(current, area, edge, minimumWidth, minimumHeight);
    }

    private static LayoutRectangle? FindHorizontalNeighbor(
        LayoutRectangle current,
        IReadOnlyList<LayoutRectangle> obstacles,
        double gap,
        double threshold,
        bool leftSide) => obstacles
        .Where(obstacle => Math.Abs((leftSide ? obstacle.Right + gap - current.Left : current.Right + gap - obstacle.Left)) <= threshold
            && RangesOverlap(current.Top, current.Bottom, obstacle.Top, obstacle.Bottom))
        .OrderBy(obstacle => Math.Abs(obstacle.Top - current.Top))
        .Cast<LayoutRectangle?>()
        .FirstOrDefault();

    private static LayoutRectangle? FindVerticalNeighbor(
        LayoutRectangle current,
        IReadOnlyList<LayoutRectangle> obstacles,
        double gap,
        double threshold,
        bool topSide) => obstacles
        .Where(obstacle => Math.Abs((topSide ? obstacle.Bottom + gap - current.Top : current.Bottom + gap - obstacle.Top)) <= threshold
            && RangesOverlap(current.Left, current.Right, obstacle.Left, obstacle.Right))
        .OrderBy(obstacle => Math.Abs(obstacle.Left - current.Left))
        .Cast<LayoutRectangle?>()
        .FirstOrDefault();

    private static LayoutRectangle? FindBoundingVerticalNeighbor(
        LayoutRectangle current,
        IReadOnlyList<LayoutRectangle> obstacles,
        bool topSide) => obstacles
        .Where(obstacle => RangesOverlap(current.Left, current.Right, obstacle.Left, obstacle.Right)
            && (topSide ? obstacle.Bottom <= current.Top + Epsilon : obstacle.Top >= current.Bottom - Epsilon))
        .OrderBy(obstacle => topSide ? current.Top - obstacle.Bottom : obstacle.Top - current.Bottom)
        .Cast<LayoutRectangle?>()
        .FirstOrDefault();

    private static LayoutRectangle Normalize(
        LayoutRectangle rectangle,
        LayoutRectangle area,
        double minimumWidth,
        double minimumHeight) => ClampPosition(rectangle with
        {
            Width = Math.Clamp(rectangle.Width, Math.Min(minimumWidth, area.Width), area.Width),
            Height = Math.Clamp(rectangle.Height, Math.Min(minimumHeight, area.Height), area.Height)
        }, area);

    private static LayoutRectangle ClampResized(
        LayoutRectangle rectangle,
        LayoutRectangle area,
        CollectionWindowResizeEdge edge,
        double minimumWidth,
        double minimumHeight)
    {
        rectangle = ApplyWidth(rectangle, Math.Clamp(rectangle.Width, Math.Min(minimumWidth, area.Width), area.Width), edge);
        rectangle = ApplyHeight(rectangle, Math.Clamp(rectangle.Height, Math.Min(minimumHeight, area.Height), area.Height), edge);
        if ((edge & CollectionWindowResizeEdge.Left) != 0 && rectangle.Left < area.Left)
        {
            rectangle = ApplyWidth(rectangle, rectangle.Right - area.Left, edge);
        }
        if ((edge & CollectionWindowResizeEdge.Right) != 0 && rectangle.Right > area.Right)
        {
            rectangle = ApplyWidth(rectangle, area.Right - rectangle.Left, edge);
        }
        if ((edge & CollectionWindowResizeEdge.Top) != 0 && rectangle.Top < area.Top)
        {
            rectangle = ApplyHeight(rectangle, rectangle.Bottom - area.Top, edge);
        }
        if ((edge & CollectionWindowResizeEdge.Bottom) != 0 && rectangle.Bottom > area.Bottom)
        {
            rectangle = ApplyHeight(rectangle, area.Bottom - rectangle.Top, edge);
        }
        return rectangle;
    }

    private static LayoutRectangle ApplyWidth(LayoutRectangle rectangle, double width, CollectionWindowResizeEdge edge) =>
        (edge & CollectionWindowResizeEdge.Left) != 0
            ? rectangle with { Left = rectangle.Right - width, Width = width }
            : rectangle with { Width = width };

    private static LayoutRectangle ApplyHeight(LayoutRectangle rectangle, double height, CollectionWindowResizeEdge edge) =>
        (edge & CollectionWindowResizeEdge.Top) != 0
            ? rectangle with { Top = rectangle.Bottom - height, Height = height }
            : rectangle with { Height = height };

    private static LayoutRectangle ClampPosition(LayoutRectangle rectangle, LayoutRectangle area) => rectangle with
    {
        Left = Math.Clamp(rectangle.Left, area.Left, Math.Max(area.Left, area.Right - rectangle.Width)),
        Top = Math.Clamp(rectangle.Top, area.Top, Math.Max(area.Top, area.Bottom - rectangle.Height))
    };

    private static bool IsInside(LayoutRectangle rectangle, LayoutRectangle area) =>
        rectangle.Left >= area.Left - Epsilon && rectangle.Top >= area.Top - Epsilon
        && rectangle.Right <= area.Right + Epsilon && rectangle.Bottom <= area.Bottom + Epsilon;

    private static bool RangesOverlap(double firstStart, double firstEnd, double secondStart, double secondEnd) =>
        firstStart < secondEnd - Epsilon && firstEnd > secondStart + Epsilon;

    private static double Snap(double value, IReadOnlyList<double> targets, double threshold)
    {
        var nearest = targets.OrderBy(target => Math.Abs(target - value)).FirstOrDefault(value);
        return Math.Abs(nearest - value) <= threshold ? nearest : value;
    }
}
