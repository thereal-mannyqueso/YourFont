namespace FontBook.Core.FontMaker;

/// <summary>One character cut out of a scanned template.</summary>
/// <param name="Character">The character this box is for.</param>
/// <param name="Ink">Ink mask indexed [x, y]; null when the box was left empty.</param>
/// <param name="BaselineY">Baseline position in the mask's rows.</param>
/// <param name="UnitsPerSample">Font units per mask pixel.</param>
public sealed record ScannedGlyph(char Character, bool[,]? Ink, double BaselineY, double UnitsPerSample)
{
    public bool HasInk => Ink is not null;
}

/// <summary>Result of reading a scanned template.</summary>
public sealed record TemplateScanResult(bool Success, string? Error, IReadOnlyList<ScannedGlyph> Glyphs)
{
    public int FoundCount => Glyphs.Count(g => g.HasInk);

    public static TemplateScanResult Failed(string error) => new(false, error, []);
}

/// <summary>
/// Reads a scanned or photographed Font Maker template: finds the four corner markers,
/// works out how the page is positioned, and cuts out the ink inside every box.
/// </summary>
public static class TemplateScanReader
{
    public static TemplateScanResult Read(GrayImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        var threshold = Math.Clamp(OtsuThreshold(image), 60, 200);

        var markers = FindMarkers(image, threshold);
        if (markers is null)
        {
            return TemplateScanResult.Failed(
                "FontBook couldn't find the four black squares in the corners of the template. " +
                "Make sure the whole page is visible, upright, and evenly lit.");
        }

        PerspectiveTransform toImage;
        try
        {
            toImage = PerspectiveTransform.FromPoints(FontTemplateLayout.MarkerCenters, markers);
        }
        catch (InvalidOperationException)
        {
            return TemplateScanResult.Failed("The corner squares are in unexpected positions. Try scanning the page again, upright.");
        }

        var glyphs = new List<ScannedGlyph>(FontTemplateLayout.Characters.Length);
        for (var i = 0; i < FontTemplateLayout.Characters.Length; i++)
        {
            glyphs.Add(ReadBox(image, toImage, threshold, i));
        }

        return new TemplateScanResult(true, null, glyphs);
    }

    private static ScannedGlyph ReadBox(GrayImage image, PerspectiveTransform toImage, int threshold, int index)
    {
        var box = FontTemplateLayout.GetBox(index);
        var inset = FontTemplateLayout.SampleInset;
        var width = (int)(box.Width - (2 * inset));
        var height = (int)(box.Height - (2 * inset));
        var ink = new bool[width, height];
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var p = toImage.Map(new PointD(box.X + inset + x + 0.5, box.Y + inset + y + 0.5));
                if (image.Sample(p.X, p.Y) < threshold)
                {
                    ink[x, y] = true;
                    count++;
                }
            }
        }

        count -= RemoveNoise(ink);
        var character = FontTemplateLayout.Characters[index];
        var baseline = (box.Height * FontTemplateLayout.BaselineRatio) - inset;
        var unitsPerSample = FontTemplateLayout.UnitsPerBoxHeight / box.Height;
        return new ScannedGlyph(character, count >= 40 ? ink : null, baseline, unitsPerSample);
    }

    /// <summary>
    /// Removes specks and leftover slivers of the printed box border. Returns how many ink pixels were cleared.
    /// </summary>
    private static int RemoveNoise(bool[,] ink)
    {
        int width = ink.GetLength(0), height = ink.GetLength(1);
        var minimum = Math.Max(12, width * height / 1200);
        var removed = 0;
        foreach (var component in Components(ink))
        {
            var (minX, minY, maxX, maxY) = Bounds(component, width);
            var touchesEdge = minX == 0 || minY == 0 || maxX == width - 1 || maxY == height - 1;
            var isSliver = (maxX - minX) < 8 || (maxY - minY) < 8;
            if (component.Count < minimum || (touchesEdge && isSliver))
            {
                foreach (var cell in component)
                {
                    ink[cell % width, cell / width] = false;
                }

                removed += component.Count;
            }
        }

        return removed;
    }

    /// <summary>Finds the four solid square corner markers, ordered TL, TR, BR, BL, in image coordinates.</summary>
    private static PointD[]? FindMarkers(GrayImage image, int threshold)
    {
        // Work on a smaller copy so large photos stay fast.
        var factor = Math.Max(1, image.Width / 1000);
        int sw = image.Width / factor, sh = image.Height / factor;
        var dark = new bool[sw, sh];
        for (var y = 0; y < sh; y++)
        {
            for (var x = 0; x < sw; x++)
            {
                var sum = 0;
                for (var dy = 0; dy < factor; dy++)
                {
                    for (var dx = 0; dx < factor; dx++)
                    {
                        sum += image[(x * factor) + dx, (y * factor) + dy];
                    }
                }

                dark[x, y] = sum / (factor * factor) < threshold;
            }
        }

        var candidates = new List<PointD>();
        foreach (var component in Components(dark))
        {
            var (minX, minY, maxX, maxY) = Bounds(component, sw);
            var w = maxX - minX + 1;
            var h = maxY - minY + 1;
            var squareness = Math.Min(w, h) / (double)Math.Max(w, h);
            var fill = component.Count / (double)(w * h);
            var relative = Math.Max(w, h) / (double)sw;
            if (component.Count >= 9 && squareness >= 0.7 && fill >= 0.75 && relative is >= 0.008 and <= 0.07)
            {
                double cx = 0, cy = 0;
                foreach (var cell in component)
                {
                    cx += cell % sw;
                    cy += cell / sw;
                }

                candidates.Add(new PointD(((cx / component.Count) + 0.5) * factor, ((cy / component.Count) + 0.5) * factor));
            }
        }

        if (candidates.Count < 4)
        {
            return null;
        }

        // The markers are the most extreme candidates in each diagonal direction.
        PointD[] corners =
        [
            candidates.MinBy(p => p.X + p.Y),
            candidates.MaxBy(p => p.X - p.Y),
            candidates.MaxBy(p => p.X + p.Y),
            candidates.MaxBy(p => p.Y - p.X),
        ];
        if (corners.Distinct().Count() != 4)
        {
            return null;
        }

        // Sanity check: the markers should span a good part of the image.
        var spanX = Math.Abs(corners[1].X - corners[0].X) / image.Width;
        var spanY = Math.Abs(corners[3].Y - corners[0].Y) / image.Height;
        return spanX > 0.3 && spanY > 0.3 ? corners : null;
    }

    /// <summary>Connected groups of true pixels (4-connected). Each item is a list of flattened indices (y * width + x).</summary>
    private static IEnumerable<List<int>> Components(bool[,] mask)
    {
        int width = mask.GetLength(0), height = mask.GetLength(1);
        var seen = new bool[width * height];
        var queue = new Queue<int>();
        for (var start = 0; start < width * height; start++)
        {
            if (seen[start] || !mask[start % width, start / width])
            {
                continue;
            }

            var component = new List<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                component.Add(cell);
                int x = cell % width, y = cell / width;
                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            yield return component;

            void Visit(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height)
                {
                    return;
                }

                var index = (y * width) + x;
                if (!seen[index] && mask[x, y])
                {
                    seen[index] = true;
                    queue.Enqueue(index);
                }
            }
        }
    }

    private static (int MinX, int MinY, int MaxX, int MaxY) Bounds(List<int> component, int width)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        foreach (var cell in component)
        {
            int x = cell % width, y = cell / width;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>Otsu's method: the gray level that best separates ink from paper.</summary>
    internal static int OtsuThreshold(GrayImage image)
    {
        var histogram = new long[256];
        var step = image.Pixels.Length > 4_000_000 ? 3 : 1;
        long total = 0;
        for (var i = 0; i < image.Pixels.Length; i += step)
        {
            histogram[image.Pixels[i]]++;
            total++;
        }

        double sumAll = 0;
        for (var t = 0; t < 256; t++)
        {
            sumAll += t * histogram[t];
        }

        double sumBackground = 0, best = 0;
        long weightBackground = 0;
        var threshold = 128;
        for (var t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0)
            {
                continue;
            }

            var weightForeground = total - weightBackground;
            if (weightForeground == 0)
            {
                break;
            }

            sumBackground += t * histogram[t];
            var meanBackground = sumBackground / weightBackground;
            var meanForeground = (sumAll - sumBackground) / weightForeground;
            var between = (double)weightBackground * weightForeground * (meanBackground - meanForeground) * (meanBackground - meanForeground);
            if (between > best)
            {
                best = between;
                threshold = t;
            }
        }

        return threshold + 1;
    }
}
