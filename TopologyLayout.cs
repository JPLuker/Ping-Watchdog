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

        // Check exact obstacle edges as well as the coarse search grid. Narrow
        // free gaps near the canvas edge otherwise go undetected.
        foreach (var obstacle in occupied)
        {
            AddCandidate(0, obstacle.Top - height - 4 - origin.Y);
            AddCandidate(0, obstacle.Bottom + 4 - origin.Y);
            AddCandidate(obstacle.Left - width - 4 - origin.X, 0);
            AddCandidate(obstacle.Right + 4 - origin.X, 0);
            foreach (int edgeX in new[] { obstacle.Left - width - 4, obstacle.Right + 4 })
            foreach (int edgeY in new[] { obstacle.Top - height - 4, obstacle.Bottom + 4 })
                AddCandidate(edgeX - origin.X, edgeY - origin.Y);
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

    internal static bool TryPlaceHostCaption(Point node, Size size, Rectangle bounds,
        IReadOnlyList<Rectangle> occupied, bool preferRight, out Rectangle placed)
    {
        var anchors = new[]
        {
            new Rectangle(preferRight ? node.X + 12 : node.X - size.Width - 12, node.Y - size.Height / 2, size.Width, size.Height),
            new Rectangle(preferRight ? node.X - size.Width - 12 : node.X + 12, node.Y - size.Height / 2, size.Width, size.Height),
            new Rectangle(node.X - size.Width / 2, node.Y + 12, size.Width, size.Height),
            new Rectangle(node.X - size.Width / 2, node.Y - size.Height - 12, size.Width, size.Height)
        };
        long Distance(Rectangle r)
        {
            long dx = node.X - Math.Clamp(node.X, r.Left, r.Right);
            long dy = node.Y - Math.Clamp(node.Y, r.Top, r.Bottom);
            return dx * dx + dy * dy;
        }
        var candidates = new List<Rectangle>();
        // Evaluate both sides before allowing a displaced caption. A clear
        // alternate side is preferable to a long leader beside another host.
        foreach (var anchor in anchors)
        {
            var exact = new Rectangle(Math.Clamp(anchor.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - size.Width)),
                Math.Clamp(anchor.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - size.Height)), size.Width, size.Height);
            if (bounds.Contains(exact) && !occupied.Any(o => Rectangle.Inflate(exact, 3, 3).IntersectsWith(o)))
                candidates.Add(exact);
        }
        if (candidates.Count == 0)
            foreach (var anchor in anchors)
                if (TryPlaceCaption(anchor, bounds, occupied, out var nearby)) candidates.Add(nearby);
        placed = candidates.OrderBy(Distance).FirstOrDefault();
        if (placed.IsEmpty || Distance(placed) > 32L * 32L)
        { placed = Rectangle.Empty; return false; }
        return true;
    }

    internal static void RunTests()
    {
        var bounds = new Rectangle(0, 0, 1000, 700);
        var node = new Point(500, 670);
        var crowdedRight = new[] { new Rectangle(505, 620, 160, 60), new Rectangle(492, 662, 16, 16) };
        if (!TryPlaceHostCaption(node, new Size(140, 36), bounds, crowdedRight, true, out var attached) ||
            attached.Right >= node.X || crowdedRight.Any(o => Rectangle.Inflate(attached, 3, 3).IntersectsWith(o)))
            throw new InvalidOperationException("Bottom host caption must use the free nearby side instead of a distant gap.");

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

        // A 40px caption fits a 46px gap with 3px clearance on each side;
        // the 16px search grid alone cannot discover the only valid position.
        var narrowGap = new List<Rectangle> { new(0, 0, 1000, 301), new(0, 348, 1000, 352) };
        if (!TryPlaceCaption(new Rectangle(400, 300, 140, 40), bounds, narrowGap, out var gapCaption) ||
            narrowGap.Any(r => r.IntersectsWith(Rectangle.Inflate(gapCaption, 3, 3))))
            throw new InvalidOperationException("Topology caption missed an exact nearby gap.");

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
