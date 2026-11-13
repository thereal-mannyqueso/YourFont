using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontBook.Core.Classification;
using FontBook.Core.FontMaker;
using FontBook.Core.Models;
using FontBook.Infrastructure.Fonts.Files;
using FontBook.Infrastructure.FontMaker;

namespace FontBook.Tests;

public sealed class FontMakerGeometryTests
{
    [Fact]
    public void PerspectiveTransform_MapsTheFourPointsExactly()
    {
        PointD[] from = [new(0, 0), new(100, 0), new(100, 100), new(0, 100)];
        PointD[] to = [new(10, 20), new(210, 30), new(190, 240), new(5, 220)];

        var t = PerspectiveTransform.FromPoints(from, to);

        for (var i = 0; i < 4; i++)
        {
            var p = t.Map(from[i]);
            Assert.Equal(to[i].X, p.X, 6);
            Assert.Equal(to[i].Y, p.Y, 6);
        }
    }

    [Fact]
    public void Layout_HasOneBoxPerCharacter_AndBoxesStayInsideTheMarkers()
    {
        Assert.Equal(FontTemplateLayout.Columns * FontTemplateLayout.Rows, FontTemplateLayout.Characters.Length);
        var markers = FontTemplateLayout.MarkerCenters;
        for (var i = 0; i < FontTemplateLayout.Characters.Length; i++)
        {
            var box = FontTemplateLayout.GetBox(i);
            Assert.True(box.X > markers[0].X - FontTemplateLayout.MarkerSize && box.Right < markers[1].X + FontTemplateLayout.MarkerSize);
            Assert.True(box.Y > markers[0].Y && box.Bottom < markers[3].Y - (FontTemplateLayout.MarkerSize / 2));
        }
    }
}

public sealed class GlyphTracerTests
{
    [Fact]
    public void Square_GivesOneClockwiseContour()
    {
        var ink = Mask(10, 10, (x, y) => x is >= 2 and < 8 && y is >= 2 and < 8);

        var loops = GlyphTracer.Trace(ink);

        var loop = Assert.Single(loops);
        Assert.Equal(4, loop.Count);
        Assert.Equal(36, GlyphTracer.SignedArea(loop)); // positive = clockwise on screen
    }

    [Fact]
    public void Ring_GivesOuterAndHoleWithOppositeDirections()
    {
        var ink = Mask(12, 12, (x, y) => x is >= 1 and < 11 && y is >= 1 and < 11 && !(x is >= 4 and < 8 && y is >= 4 and < 8));

        var areas = GlyphTracer.Trace(ink).Select(GlyphTracer.SignedArea).OrderBy(a => a).ToList();

        Assert.Equal([-16, 100], areas);
    }

    [Fact]
    public void DiagonallyTouchingPixels_StaySeparateShapes()
    {
        var ink = Mask(4, 4, (x, y) => (x, y) is (1, 1) or (2, 2));

        Assert.Equal(2, GlyphTracer.Trace(ink).Count);
    }

    [Fact]
    public void Outline_IsInFontUnits_SitsOnBaseline_AndOuterIsClockwiseYUp()
    {
        // A 20x40 bar whose bottom is exactly on the baseline (row 50).
        var ink = Mask(60, 70, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 50);
        var glyph = new ScannedGlyph('I', ink, BaselineY: 50, UnitsPerSample: 5);

        var outline = GlyphTracer.BuildOutline(glyph)!;

        var contour = Assert.Single(outline.Contours);
        Assert.Equal(GlyphTracer.SideBearing, contour.Min(p => p.X));
        Assert.Equal(0, contour.Min(p => p.Y));
        Assert.Equal(200, contour.Max(p => p.Y));
        Assert.Equal((20 * 5) + (2 * GlyphTracer.SideBearing), outline.AdvanceWidth);
        // Shoelace with y up: negative = clockwise (TrueType's outer direction).
        var area = 0.0;
        for (var i = 0; i < contour.Count; i++)
        {
            var (a, b) = (contour[i], contour[(i + 1) % contour.Count]);
            area += ((double)a.X * b.Y) - ((double)b.X * a.Y);
        }

        Assert.True(area < 0);
    }

    [Fact]
    public void EmptyBox_GivesNoOutline()
        => Assert.Null(GlyphTracer.BuildOutline(new ScannedGlyph('x', null, 50, 5)));

    private static bool[,] Mask(int w, int h, Func<int, int, bool> ink)
    {
        var mask = new bool[w, h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                mask[x, y] = ink(x, y);
            }
        }

        return mask;
    }
}

/// <summary>
/// End-to-end: draws a filled-in template (letters in Segoe Print, standing in for handwriting),
/// shrinks and tilts it like a real scan, then reads it back and builds a font that WPF can load.
/// </summary>
public sealed class FontMakerEndToEndTests
{
    [Fact]
    public void ScanOfFilledTemplate_BecomesALoadableFont()
    {
        var scan = Sta.Run(() => SimulatedScan.Render(scale: 0.8, angleDegrees: 1.5, leaveEmpty: "%"));

        var result = TemplateScanReader.Read(scan);

        Assert.True(result.Success, result.Error);
        Assert.Equal(FontTemplateLayout.Characters.Length - 1, result.FoundCount);
        Assert.False(result.Glyphs.Single(g => g.Character == '%').HasInk);

        var outlines = result.Glyphs.Select(GlyphTracer.BuildOutline).OfType<GlyphOutline>().ToList();
        Assert.Equal(79, outlines.Count);
        // The "o" must have a hole: an outer contour and an inner one.
        Assert.True(outlines.Single(o => o.Character == 'o').Contours.Count >= 2);

        var bytes = new TrueTypeFontBuilder().Build(new FontDefinition("Test Hand", outlines, FontTemplateLayout.Ascent, FontTemplateLayout.Descent));
        var path = Path.Combine(Path.GetTempPath(), $"fontbook-test-{Guid.NewGuid():N}.ttf");
        File.WriteAllBytes(path, bytes);
        try
        {
            // Our own reader understands it, and it classifies as handwriting.
            var face = Assert.Single(FontFileReader.ReadFaces(path));
            Assert.Equal("Test Hand", face.FamilyName);
            Assert.Equal("Regular", face.StyleName);
            Assert.Equal(FontCategory.Script, FontClassifier.Classify(new FontClassificationHints(face.FamilyName, face.IbmFamilyClass, face.Panose ?? [], face.IsFixedPitch)));

            // WPF (DirectWrite-based text stack) loads it and finds every drawn character plus space.
            Sta.Run(() =>
            {
                var typeface = new GlyphTypeface(new Uri(path));
                foreach (var c in FontTemplateLayout.Characters.Where(c => c != '%').Append(' '))
                {
                    Assert.True(typeface.CharacterToGlyphMap.ContainsKey(c), $"Missing '{c}'");
                }

                Assert.False(typeface.CharacterToGlyphMap.ContainsKey('%'));
                var a = typeface.CharacterToGlyphMap['A'];
                Assert.True(typeface.AdvanceWidths[a] > 0);
                Assert.NotNull(typeface.GetGlyphOutline(a, 72, 72));

                // The preview resolves Vault fonts through their folder; that route must find this font
                // (a miss silently falls back to the default font, which has 14 typefaces).
                var folder = new Uri(Path.GetDirectoryName(path)! + Path.DirectorySeparatorChar);
                var family = new FontFamily(folder, "./#Test Hand");
                var typefaces = family.GetTypefaces().ToList();
                Assert.NotEmpty(typefaces);
                Assert.True(typefaces.First().TryGetGlyphTypeface(out var resolved));
                Assert.Equal("Test Hand", resolved.FamilyNames.Values.First());
                return 0;
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BlankPage_ReportsMissingMarkers()
    {
        var blank = new GrayImage(800, 1000, Enumerable.Repeat((byte)250, 800 * 1000).ToArray());

        var result = TemplateScanReader.Read(blank);

        Assert.False(result.Success);
        Assert.Contains("corners", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("  My   Hand  ", "My Hand")]
    [InlineData("Emma's Print", "Emma's Print")]
    public void NormalizeName_TidiesWhitespace(string input, string expected) => Assert.Equal(expected, FontMakerService.NormalizeName(input));

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("A name that is far too long for a font family name")]
    public void NormalizeName_RejectsBadNames(string input) => Assert.Throws<ArgumentException>(() => FontMakerService.NormalizeName(input));
}

/// <summary>Draws a filled-in Font Maker template the way a scanner would see it.</summary>
internal static class SimulatedScan
{
    public static GrayImage Render(double scale, double angleDegrees, string leaveEmpty = "")
    {
        var width = (int)(FontTemplateLayout.PageWidth * scale) + 120;
        var height = (int)(FontTemplateLayout.PageHeight * scale) + 120;
        var visual = new DrawingVisual();
        var pen = new Typeface(new FontFamily("Segoe Print"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            dc.PushTransform(new TranslateTransform(60, 60));
            dc.PushTransform(new RotateTransform(angleDegrees));
            dc.PushTransform(new ScaleTransform(scale, scale));

            foreach (var m in FontTemplateLayout.MarkerCenters)
            {
                var half = FontTemplateLayout.MarkerSize / 2;
                dc.DrawRectangle(Brushes.Black, null, new Rect(m.X - half, m.Y - half, FontTemplateLayout.MarkerSize, FontTemplateLayout.MarkerSize));
            }

            var border = new Pen(new SolidColorBrush(Color.FromRgb(150, 150, 150)), 3);
            var guide = new Pen(new SolidColorBrush(Color.FromRgb(205, 205, 205)), 2);
            for (var i = 0; i < FontTemplateLayout.Characters.Length; i++)
            {
                var box = FontTemplateLayout.GetBox(i);
                dc.DrawRectangle(null, border, new Rect(box.X, box.Y, box.Width, box.Height));
                var baseline = box.Y + (box.Height * FontTemplateLayout.BaselineRatio);
                dc.DrawLine(guide, new Point(box.X + 10, baseline), new Point(box.Right - 10, baseline));

                var character = FontTemplateLayout.Characters[i];
                if (leaveEmpty.Contains(character, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = new FormattedText(character.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, pen, 120, Brushes.Black, 1);
                dc.DrawText(text, new Point(box.X + ((box.Width - text.Width) / 2), baseline - text.Baseline));
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var gray = new FormatConvertedBitmap(bitmap, PixelFormats.Gray8, null, 0);
        var pixels = new byte[width * height];
        gray.CopyPixels(pixels, width, 0);
        return new GrayImage(width, height, pixels);
    }
}

/// <summary>WPF objects must be created on a single-threaded apartment (STA) thread.</summary>
internal static class Sta
{
    public static T Run<T>(Func<T> work)
    {
        T result = default!;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            throw new InvalidOperationException("STA work failed: " + error.Message, error);
        }

        return result;
    }
}
