namespace FontBook.Core.FontMaker;

/// <summary>A point with double-precision coordinates.</summary>
public readonly record struct PointD(double X, double Y);

/// <summary>An axis-aligned rectangle.</summary>
public readonly record struct RectD(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public RectD Inflate(double amount) => new(X - amount, Y - amount, Width + (2 * amount), Height + (2 * amount));
}

/// <summary>
/// A perspective (projective) transform defined by four point pairs. Used to map positions on the
/// printed template to positions in a scanned or photographed image, which may be shifted, scaled,
/// rotated or viewed at a slight angle.
/// </summary>
public sealed class PerspectiveTransform
{
    private readonly double[] _h;

    private PerspectiveTransform(double[] h) => _h = h;

    /// <summary>Builds the transform that maps each <paramref name="from"/> point to the matching <paramref name="to"/> point.</summary>
    /// <exception cref="InvalidOperationException">The points are degenerate (e.g. three in a line).</exception>
    public static PerspectiveTransform FromPoints(IReadOnlyList<PointD> from, IReadOnlyList<PointD> to)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        if (from.Count != 4 || to.Count != 4)
        {
            throw new ArgumentException("Exactly four point pairs are required.");
        }

        // Solve the 8 unknowns of the 3x3 homography (h33 = 1) with Gaussian elimination.
        var m = new double[8, 9];
        for (var i = 0; i < 4; i++)
        {
            var (x, y) = (from[i].X, from[i].Y);
            var (u, v) = (to[i].X, to[i].Y);
            double[] r1 = [x, y, 1, 0, 0, 0, -u * x, -u * y, u];
            double[] r2 = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
            for (var c = 0; c < 9; c++)
            {
                m[2 * i, c] = r1[c];
                m[(2 * i) + 1, c] = r2[c];
            }
        }

        for (var col = 0; col < 8; col++)
        {
            var pivot = col;
            for (var r = col + 1; r < 8; r++)
            {
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = r;
                }
            }

            if (Math.Abs(m[pivot, col]) < 1e-12)
            {
                throw new InvalidOperationException("The reference points don't define a valid transform.");
            }

            for (var c = 0; c < 9; c++)
            {
                (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);
            }

            for (var r = 0; r < 8; r++)
            {
                if (r == col)
                {
                    continue;
                }

                var factor = m[r, col] / m[col, col];
                for (var c = col; c < 9; c++)
                {
                    m[r, c] -= factor * m[col, c];
                }
            }
        }

        var h = new double[8];
        for (var i = 0; i < 8; i++)
        {
            h[i] = m[i, 8] / m[i, i];
        }

        return new PerspectiveTransform(h);
    }

    public PointD Map(PointD p)
    {
        var w = (_h[6] * p.X) + (_h[7] * p.Y) + 1;
        return new PointD(
            ((_h[0] * p.X) + (_h[1] * p.Y) + _h[2]) / w,
            ((_h[3] * p.X) + (_h[4] * p.Y) + _h[5]) / w);
    }
}

/// <summary>An 8-bit grayscale image (0 = black, 255 = white), independent of any UI framework.</summary>
public sealed class GrayImage
{
    public GrayImage(int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            throw new ArgumentException("Pixel buffer doesn't match the image size.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Pixels { get; }

    public byte this[int x, int y] => Pixels[(y * Width) + x];

    /// <summary>Bilinear sample at a fractional position; outside the image counts as white paper.</summary>
    public double Sample(double x, double y)
    {
        x -= 0.5;
        y -= 0.5;
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        if (x0 < 0 || y0 < 0 || x0 + 1 >= Width || y0 + 1 >= Height)
        {
            return x0 >= 0 && y0 >= 0 && x0 < Width && y0 < Height ? this[x0, y0] : 255;
        }

        var fx = x - x0;
        var fy = y - y0;
        var top = (this[x0, y0] * (1 - fx)) + (this[x0 + 1, y0] * fx);
        var bottom = (this[x0, y0 + 1] * (1 - fx)) + (this[x0 + 1, y0 + 1] * fx);
        return (top * (1 - fy)) + (bottom * fy);
    }
}
