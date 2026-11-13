using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FontBook.Core.FontMaker;

namespace FontBook.App.Services.FontMaker;

/// <summary>Draws the printable Font Maker template, prints it, or saves it as an image.</summary>
public interface ITemplateRenderer
{
    /// <summary>Shows the Windows print dialog and prints the template. Returns false if the user cancelled.</summary>
    bool Print();

    /// <summary>Saves the template as a 300 dpi PNG (US Letter).</summary>
    void SavePng(string path);
}

public sealed class TemplateRenderer : ITemplateRenderer
{
    /// <summary>Template pixels are 1/300 inch; WPF draws in 1/96 inch units.</summary>
    private const double ToDip = 96.0 / FontTemplateLayout.Dpi;

    private static readonly Typeface UiTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface TitleTypeface = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public bool Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        // Fit the page inside the printer's printable area (works for Letter and A4).
        var pageWidth = FontTemplateLayout.PageWidth * ToDip;
        var pageHeight = FontTemplateLayout.PageHeight * ToDip;
        var scale = Math.Min(1, Math.Min(dialog.PrintableAreaWidth / pageWidth, dialog.PrintableAreaHeight / pageHeight));
        var offsetX = (dialog.PrintableAreaWidth - (pageWidth * scale)) / 2;
        var offsetY = (dialog.PrintableAreaHeight - (pageHeight * scale)) / 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(offsetX, offsetY));
            dc.PushTransform(new ScaleTransform(scale, scale));
            DrawPage(dc);
            dc.Pop();
            dc.Pop();
        }

        dialog.PrintVisual(visual, "FontBook Font Maker template");
        return true;
    }

    public void SavePng(string path)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            DrawPage(dc);
        }

        var bitmap = new RenderTargetBitmap((int)FontTemplateLayout.PageWidth, (int)FontTemplateLayout.PageHeight,
            FontTemplateLayout.Dpi, FontTemplateLayout.Dpi, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Draws one page in WPF units (1/96 inch).</summary>
    private static void DrawPage(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ToDip, ToDip)); // draw in template pixels from here on
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, FontTemplateLayout.PageWidth, FontTemplateLayout.PageHeight));

        // Corner markers: FontBook finds these in the scan to line the page up.
        foreach (var m in FontTemplateLayout.MarkerCenters)
        {
            var half = FontTemplateLayout.MarkerSize / 2;
            dc.DrawRectangle(Brushes.Black, null, new Rect(m.X - half, m.Y - half, FontTemplateLayout.MarkerSize, FontTemplateLayout.MarkerSize));
        }

        var grey = new SolidColorBrush(Color.FromRgb(120, 120, 120));
        DrawText(dc, "FontBook Font Maker", TitleTypeface, 46, Brushes.Black, new Point(300, 162));
        DrawText(dc, "Write one character per box with a dark pen. Sit letters on the dotted line and let tails (g, j, p, q, y) hang below it.",
            UiTypeface, 25, grey, new Point(300, 232));
        DrawText(dc, "Leave a box empty to skip that character. When you scan, keep all four black corner squares visible.",
            UiTypeface, 25, grey, new Point(300, 268));

        var border = new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 3);
        var guide = new Pen(new SolidColorBrush(Color.FromRgb(200, 200, 200)), 3) { DashStyle = new DashStyle([4, 4], 0) };
        var labelBrush = new SolidColorBrush(Color.FromRgb(140, 140, 140));
        for (var i = 0; i < FontTemplateLayout.Characters.Length; i++)
        {
            var box = FontTemplateLayout.GetBox(i);
            var label = FontTemplateLayout.GetLabel(i);
            DrawText(dc, Describe(FontTemplateLayout.Characters[i]), UiTypeface, 30, labelBrush, new Point(label.X + 4, label.Y + 4));
            dc.DrawRoundedRectangle(null, border, new Rect(box.X, box.Y, box.Width, box.Height), 10, 10);
            var baseline = box.Y + (box.Height * FontTemplateLayout.BaselineRatio);
            dc.DrawLine(guide, new Point(box.X + 14, baseline), new Point(box.Right - 14, baseline));
        }

        DrawText(dc, "Template v1  ·  US Letter / A4", UiTypeface, 22, labelBrush, new Point(1100, 3090));
        dc.Pop();
    }

    /// <summary>Label text; spells out characters that are hard to read on their own.</summary>
    private static string Describe(char c) => c switch
    {
        '\'' => "'  apostrophe",
        '"' => "\"  quote",
        '-' => "-  hyphen",
        '.' => ".  period",
        ',' => ",  comma",
        _ => c.ToString(),
    };

    private static void DrawText(DrawingContext dc, string text, Typeface typeface, double size, Brush brush, Point origin)
        => dc.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush, 1), origin);
}
