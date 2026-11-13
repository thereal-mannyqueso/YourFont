namespace FontBook.App.ViewModels;

/// <summary>
/// Segoe Fluent Icons used by view models, built from their numeric code points.
/// (Icon characters live in the Unicode Private Use Area; writing them as numbers keeps
/// source files plain ASCII so editors and tools can't silently corrupt them.)
/// XAML files use the equivalent &amp;#xE8D2; entity syntax.
/// </summary>
internal static class Glyphs
{
    public static readonly string Font = Icon(0xE8D2);
    public static readonly string Pc = Icon(0xE977);
    public static readonly string Person = Icon(0xE77B);
    public static readonly string Star = Icon(0xE734);
    public static readonly string Import = Icon(0xE8B5);
    public static readonly string Library = Icon(0xE8F1);
    public static readonly string Recent = Icon(0xE823);
    public static readonly string Folder = Icon(0xE8B7);
    public static readonly string Search = Icon(0xE721);
    public static readonly string Pen = Icon(0xEDC6);

    private static string Icon(int codePoint) => char.ConvertFromUtf32(codePoint);
}
