namespace FontBook.Core.FontMaker;

/// <summary>A point of a glyph outline in font units. Off-curve points are quadratic curve controls.</summary>
public readonly record struct OutlinePoint(int X, int Y, bool OnCurve);

/// <summary>A finished glyph: its character, horizontal advance and closed contours (font units, y up).</summary>
public sealed record GlyphOutline(char Character, int AdvanceWidth, IReadOnlyList<IReadOnlyList<OutlinePoint>> Contours);

/// <summary>
/// Turns an ink mask into font outlines.
/// </summary>
/// <remarks>
/// <list type="number">
///   <item><b>Trace:</b> walk the pixel edges between ink and paper. Edges are directed so ink is always on
///         the same side, which automatically gives outer shapes and holes (like the inside of "o")
///         opposite directions — exactly what fonts need to fill shapes correctly.</item>
///   <item><b>Simplify:</b> the pixel "staircase" is reduced to a few points (Ramer–Douglas–Peucker).</item>
///   <item><b>Smooth:</b> all points become quadratic control points. TrueType then draws a smooth curve
///         through the midpoints between them, which suits handwriting.</item>
/// </list>
/// </remarks>
public static class GlyphTracer
{
    /// <summary>Space kept on each side of a glyph, in font units.</summary>
    public const int SideBearing = 35;

    private const double SimplifyTolerance = 1.2;

    /// <summary>Builds the outline for a scanned character, or null if the box was empty.</summary>
    public static GlyphOutline? BuildOutline(ScannedGlyph glyph)
    {
        ArgumentNullException.ThrowIfNull(glyph);
        if (glyph.Ink is null)
        {
            return null;
        }

        var loops = Trace(glyph.Ink)
            .Select(loop => Simplify(loop, SimplifyTolerance))
            .Where(loop => loop.Count >= 3 && Math.Abs(SignedArea(loop)) >= 6)
            .ToList();
        if (loops.Count == 0)
        {
            return null;
        }

        var inkLeft = loops.SelectMany(l => l).Min(p => p.X);
        var inkRight = loops.SelectMany(l => l).Max(p => p.X);
        var scale = glyph.UnitsPerSample;

        var contours = new List<IReadOnlyList<OutlinePoint>>();
        foreach (var loop in loops)
        {
            // Image rows grow downward; font Y grows upward. Mirroring Y *and* viewing in a y-up frame
            // keeps the visual direction, so outer shapes stay clockwise (TrueType's convention) as traced.
            var points = new List<OutlinePoint>();
            for (var i = 0; i < loop.Count; i++)
            {
                var p = new OutlinePoint(
                    (int)Math.Round(((loop[i].X - inkLeft) * scale) + SideBearing),
                    (int)Math.Round((glyph.BaselineY - loop[i].Y) * scale),
                    OnCurve: false);
                if (points.Count == 0 || points[^1] != p)
                {
                    points.Add(p);
                }
            }

            if (points.Count > 1 && points[0] == points[^1])
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count >= 3)
            {
                contours.Add(points);
            }
        }

        var advance = (int)Math.Round((inkRight - inkLeft) * scale) + (2 * SideBearing);
        return contours.Count == 0 ? null : new GlyphOutline(glyph.Character, advance, contours);
    }

    /// <summary>
    /// Traces the boundaries of the ink. Coordinates are pixel corners (y down). Outer boundaries run
    /// clockwise on screen and holes counter-clockwise.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<PointD>> Trace(bool[,] ink)
    {
        ArgumentNullException.ThrowIfNull(ink);
        int width = ink.GetLength(0), height = ink.GetLength(1);
        bool Ink(int x, int y) => x >= 0 && y >= 0 && x < width && y < height && ink[x, y];

        // Directed edges with ink on the right-hand side (screen coordinates).
        var edges = new List<(int Sx, int Sy, int Dx, int Dy)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!ink[x, y])
                {
                    continue;
                }

                if (!Ink(x, y - 1))
                {
                    edges.Add((x, y, 1, 0));
                }

                if (!Ink(x + 1, y))
                {
                    edges.Add((x + 1, y, 0, 1));
                }

                if (!Ink(x, y + 1))
                {
                    edges.Add((x + 1, y + 1, -1, 0));
                }

                if (!Ink(x - 1, y))
                {
                    edges.Add((x, y + 1, 0, -1));
                }
            }
        }

        var outgoing = new Dictionary<(int, int), List<int>>();
        for (var i = 0; i < edges.Count; i++)
        {
            var key = (edges[i].Sx, edges[i].Sy);
            if (!outgoing.TryGetValue(key, out var list))
            {
                list = [];
                outgoing[key] = list;
            }

            list.Add(i);
        }

        var used = new bool[edges.Count];
        var loops = new List<IReadOnlyList<PointD>>();
        for (var first = 0; first < edges.Count; first++)
        {
            if (used[first])
            {
                continue;
            }

            var loop = new List<PointD>();
            var current = first;
            while (true)
            {
                used[current] = true;
                var e = edges[current];
                loop.Add(new PointD(e.Sx, e.Sy));
                var end = (e.Sx + e.Dx, e.Sy + e.Dy);
                if (end == (edges[first].Sx, edges[first].Sy))
                {
                    break;
                }

                var next = PickNext(outgoing[end], edges, used, e.Dx, e.Dy);
                if (next < 0)
                {
                    break; // shouldn't happen for a valid mask
                }

                current = next;
            }

            loops.Add(RemoveCollinear(loop));
        }

        return loops;
    }

    /// <summary>At a corner where two shapes touch diagonally, prefer turning right so the shapes stay separate.</summary>
    private static int PickNext(List<int> candidates, List<(int Sx, int Sy, int Dx, int Dy)> edges, bool[] used, int dx, int dy)
    {
        (int, int)[] preference = [(-dy, dx), (dx, dy), (dy, -dx)]; // right, straight, left (y down)
        foreach (var direction in preference)
        {
            foreach (var candidate in candidates)
            {
                if (!used[candidate] && (edges[candidate].Dx, edges[candidate].Dy) == direction)
                {
                    return candidate;
                }
            }
        }

        return -1;
    }

    private static List<PointD> RemoveCollinear(List<PointD> loop)
    {
        var result = new List<PointD>();
        for (var i = 0; i < loop.Count; i++)
        {
            var prev = loop[(i - 1 + loop.Count) % loop.Count];
            var next = loop[(i + 1) % loop.Count];
            var cross = ((loop[i].X - prev.X) * (next.Y - loop[i].Y)) - ((loop[i].Y - prev.Y) * (next.X - loop[i].X));
            if (Math.Abs(cross) > 1e-9)
            {
                result.Add(loop[i]);
            }
        }

        return result;
    }

    /// <summary>Ramer–Douglas–Peucker simplification of a closed polygon.</summary>
    public static List<PointD> Simplify(IReadOnlyList<PointD> loop, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(loop);
        if (loop.Count < 4)
        {
            return [.. loop];
        }

        // Split the closed loop at the point farthest from the first point, then simplify both halves.
        var far = 0;
        var best = -1.0;
        for (var i = 1; i < loop.Count; i++)
        {
            var d = Distance(loop[0], loop[i]);
            if (d > best)
            {
                best = d;
                far = i;
            }
        }

        var keep = new bool[loop.Count];
        keep[0] = keep[far] = true;
        var closed = loop.Append(loop[0]).ToList();
        Mark(closed, 0, far, tolerance, keep);
        Mark(closed, far, loop.Count, tolerance, keep);
        return [.. loop.Where((_, i) => keep[i])];
    }

    private static void Mark(List<PointD> points, int start, int end, double tolerance, bool[] keep)
    {
        if (end <= start + 1)
        {
            return;
        }

        var index = -1;
        var max = 0.0;
        for (var i = start + 1; i < end; i++)
        {
            var d = DistanceToSegment(points[i], points[start], points[end]);
            if (d > max)
            {
                max = d;
                index = i;
            }
        }

        if (index >= 0 && max > tolerance)
        {
            keep[index % keep.Length] = true;
            Mark(points, start, index, tolerance, keep);
            Mark(points, index, end, tolerance, keep);
        }
    }

    /// <summary>Signed area; positive means clockwise on screen (y down).</summary>
    public static double SignedArea(IReadOnlyList<PointD> loop)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var sum = 0.0;
        for (var i = 0; i < loop.Count; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % loop.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2;
    }

    private static double Distance(PointD a, PointD b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var lengthSquared = ((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y));
        if (lengthSquared < 1e-12)
        {
            return Distance(p, a);
        }

        var t = Math.Clamp((((p.X - a.X) * (b.X - a.X)) + ((p.Y - a.Y) * (b.Y - a.Y))) / lengthSquared, 0, 1);
        return Distance(p, new PointD(a.X + (t * (b.X - a.X)), a.Y + (t * (b.Y - a.Y))));
    }
}
