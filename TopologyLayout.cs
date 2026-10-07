namespace PingWatchdog;

internal static class TopologyLayout
{
    internal const int MaxCaptionDisplacement = 96;
    private const int PlacementStep = 16;

    internal static bool TryPlaceCaption(
        Rectangle preferred,
        Rectangle bounds,
        IReadOnlyList<Rectangle> occupied,
        out Rectangle placed)
    {
        int width = preferred.Width;
        int height = preferred.Height;

        if (width > bounds.Width || height > bounds.Height)
        {
            placed = Rectangle.Empty;
            return false;
        }

        var origin = new Rectangle(
            Math.Clamp(preferred.X, bounds.Left, bounds.Right - width),
            Math.Clamp(preferred.Y, bounds.Top, bounds.Bottom - height),
            width,
            height);

        bool Free(Rectangle candidate)
        {
            if (!bounds.Contains(candidate))
                return false;

            var padded = Rectangle.Inflate(candidate, 3, 3);
            return !occupied.Any(obstacle => padded.IntersectsWith(obstacle));
        }

        if (Free(origin))
        {
            placed = origin;
            return true;
        }

        // Captions are annotations for a specific host node. Search only nearby;
        // a detached label on the other side of the topology is worse than hiding it.
        var candidates = new List<Rectangle>();
        var seen = new HashSet<(int X, int Y)>();

        void AddCandidate(int dx, int dy)
        {
            int x = Math.Clamp(origin.X + dx, bounds.Left, bounds.Right - width);
            int y = Math.Clamp(origin.Y + dy, bounds.Top, bounds.Bottom - height);

            if (Math.Abs(x - origin.X) > MaxCaptionDisplacement ||
                Math.Abs(y - origin.Y) > MaxCaptionDisplacement ||
                !seen.Add((x, y)))
            {
                return;
            }

            candidates.Add(new Rectangle(x, y, width, height));
        }

        for (int radius = PlacementStep; radius <= MaxCaptionDisplacement; radius += PlacementStep)
        {
            for (int offset = -radius; offset <= radius; offset += PlacementStep)
            {
                AddCandidate(offset, -radius);
                AddCandidate(offset, radius);
            }

            for (int offset = -radius + PlacementStep; offset <= radius - PlacementStep; offset += PlacementStep)
            {
                AddCandidate(-radius, offset);
                AddCandidate(radius, offset);
            }
        }

        foreach (var candidate in candidates.OrderBy(candidate =>
            Math.Abs((long)candidate.X - origin.X) +
            Math.Abs((long)candidate.Y - origin.Y)))
        {
            if (!Free(candidate))
                continue;

            placed = candidate;
            return true;
        }

        placed = Rectangle.Empty;
        return false;
    }

    internal static void RunTests()
    {
        var bounds = new Rectangle(0, 0, 1000, 700);

        var preferred = new Rectangle(400, 300, 140, 36);
        if (!TryPlaceCaption(preferred, bounds, Array.Empty<Rectangle>(), out var exact) || exact != preferred)
            throw new InvalidOperationException("Free topology caption moved unexpectedly.");

        var oneObstacle = new List<Rectangle> { new(395, 295, 150, 46) };
        if (!TryPlaceCaption(preferred, bounds, oneObstacle, out var nearby) ||
            Math.Abs(nearby.X - preferred.X) > MaxCaptionDisplacement ||
            Math.Abs(nearby.Y - preferred.Y) > MaxCaptionDisplacement)
        {
            throw new InvalidOperationException("Topology caption escaped its local node area.");
        }

        var blockedLocalArea = new List<Rectangle>
        {
            new(290, 190, 360, 270)
        };
        if (TryPlaceCaption(preferred, bounds, blockedLocalArea, out _))
            throw new InvalidOperationException("Crowded topology caption should hide instead of detaching from its node.");

        if (TryPlaceCaption(new Rectangle(0, 0, 200, 40), new Rectangle(0, 0, 100, 30),
            Array.Empty<Rectangle>(), out _))
        {
            throw new InvalidOperationException("Oversized topology caption must not overlap.");
        }
    }
}
