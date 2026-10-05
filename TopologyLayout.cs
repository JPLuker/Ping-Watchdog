namespace PingWatchdog;

internal static class TopologyLayout
{
    internal static bool TryPlaceCaption(Rectangle preferred, Rectangle bounds,
        IReadOnlyList<Rectangle> occupied, out Rectangle placed)
    {
        var candidates = new List<Rectangle>();
        int width = preferred.Width, height = preferred.Height;
        if (width > bounds.Width || height > bounds.Height) { placed = Rectangle.Empty; return false; }
        int x = Math.Clamp(preferred.X, bounds.Left, bounds.Right - width);
        int y = Math.Clamp(preferred.Y, bounds.Top, bounds.Bottom - height);
        candidates.Add(new Rectangle(x, y, width, height));
        foreach (var obstacle in occupied)
        {
            candidates.Add(new Rectangle(x, obstacle.Bottom + 6, width, height));
            candidates.Add(new Rectangle(x, obstacle.Top - height - 6, width, height));
            candidates.Add(new Rectangle(obstacle.Right + 6, y, width, height));
            candidates.Add(new Rectangle(obstacle.Left - width - 6, y, width, height));
        }
        bool Free(Rectangle candidate)
        {
            if (!bounds.Contains(candidate)) return false;
            var padded = Rectangle.Inflate(candidate, 3, 3);
            return !occupied.Any(obstacle => padded.IntersectsWith(obstacle));
        }
        foreach (var candidate in candidates.OrderBy(r =>
            Math.Abs((long)r.X - preferred.X) + Math.Abs((long)r.Y - preferred.Y)))
        {
            if (!Free(candidate)) continue;
            placed = candidate; return true;
        }
        // Finite packing fallback for crowded views. Never knowingly overlap a
        // caption or shrink an address to make it fit.
        for (int row = bounds.Top; row + height <= bounds.Bottom; row += height + 8)
        for (int column = bounds.Left; column + width <= bounds.Right; column += width + 8)
        {
            var candidate = new Rectangle(column, row, width, height);
            if (!Free(candidate)) continue;
            placed = candidate; return true;
        }
        placed = Rectangle.Empty; return false;
    }
    internal static void RunTests()
    {
        var bounds = new Rectangle(0, 0, 1000, 700);
        var reserved = new List<Rectangle> { new(450, 290, 100, 100), new(460, 100, 80, 80), new(460, 500, 80, 80) };
        for (int i = 0; i < 24; i++)
        {
            var preferred = new Rectangle(300 + i % 2 * 260, 90 + i % 6 * 45, i % 3 == 0 ? 230 : 145, 38);
            if (!TryPlaceCaption(preferred, bounds, reserved, out var placed) || !bounds.Contains(placed) ||
                reserved.Any(r => r.IntersectsWith(Rectangle.Inflate(placed, 3, 3))))
                throw new InvalidOperationException("Topology caption collision regression.");
            reserved.Add(placed);
        }
        if (TryPlaceCaption(new Rectangle(0, 0, 200, 40), new Rectangle(0, 0, 100, 30), reserved, out _))
            throw new InvalidOperationException("Oversized topology caption must not overlap.");
    }
}
