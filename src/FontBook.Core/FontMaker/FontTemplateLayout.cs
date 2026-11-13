namespace FontBook.Core.FontMaker;

/// <summary>
/// Geometry of the printable Font Maker template, in "template pixels": a US Letter page at
/// 300 dots per inch (2550 × 3300). The same numbers are used to draw the template and to read
/// the scan back, so they must never drift apart.
/// </summary>
/// <remarks>
/// Content stays at least 0.5 inch from every edge, so the page also prints on A4 and survives
/// printer margins and "fit to page" scaling. Scaling doesn't matter: the four corner markers
/// are found in the scan and everything else is located relative to them.
/// </remarks>
public static class FontTemplateLayout
{
    public const double Dpi = 300;
    public const double PageWidth = 2550;
    public const double PageHeight = 3300;

    /// <summary>Side of each solid black corner marker.</summary>
    public const double MarkerSize = 90;

    public const int Columns = 8;
    public const int Rows = 10;
    public const double GridLeft = 150;
    public const double GridTop = 360;
    public const double GridWidth = 2250;
    public const double GridHeight = 2700;
    public const double CellWidth = GridWidth / Columns;
    public const double CellHeight = GridHeight / Rows;

    /// <summary>Space above each box for the small printed label showing which character goes there.</summary>
    public const double LabelHeight = 44;
    public const double BoxSideInset = 14;
    public const double BoxBottomInset = 12;

    /// <summary>The baseline guide sits at this fraction of the box height (from the top).</summary>
    public const double BaselineRatio = 0.70;

    /// <summary>Font units that the full box height corresponds to (with 1000 units per em).</summary>
    public const int UnitsPerBoxHeight = 1250;

    /// <summary>Margin (template pixels) inside each box that is ignored, so the printed border is never traced.</summary>
    public const double SampleInset = 10;

    /// <summary>The characters on the template, in box order (8 per row).</summary>
    public const string Characters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "abcdefghijklmnopqrstuvwxyz" +
        "0123456789" +
        ".,!?'\"-:;()&@#$%+/";

    /// <summary>Marker centers in order: top-left, top-right, bottom-right, bottom-left.</summary>
    public static IReadOnlyList<PointD> MarkerCenters { get; } =
    [
        new(150 + (MarkerSize / 2), 150 + (MarkerSize / 2)),
        new(PageWidth - 150 - (MarkerSize / 2), 150 + (MarkerSize / 2)),
        new(PageWidth - 150 - (MarkerSize / 2), PageHeight - 150 - (MarkerSize / 2)),
        new(150 + (MarkerSize / 2), PageHeight - 150 - (MarkerSize / 2)),
    ];

    /// <summary>Font ascent derived from the baseline position (875 with the defaults).</summary>
    public static int Ascent => (int)Math.Round(BaselineRatio * UnitsPerBoxHeight);

    /// <summary>Font descent (positive number, 375 with the defaults).</summary>
    public static int Descent => UnitsPerBoxHeight - Ascent;

    /// <summary>The writing box for the character at <paramref name="index"/> in <see cref="Characters"/>.</summary>
    public static RectD GetBox(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Characters.Length);
        var column = index % Columns;
        var row = index / Columns;
        var x = GridLeft + (column * CellWidth) + BoxSideInset;
        var y = GridTop + (row * CellHeight) + LabelHeight;
        return new RectD(x, y, CellWidth - (2 * BoxSideInset), CellHeight - LabelHeight - BoxBottomInset);
    }

    /// <summary>Where the small label for a box is drawn (above the box).</summary>
    public static RectD GetLabel(int index)
    {
        var box = GetBox(index);
        return new RectD(box.X, box.Y - LabelHeight, box.Width, LabelHeight);
    }
}
