using FontBook.Core.Models;

namespace FontBook.Core.Classification;

/// <summary>Classification data stored inside a font file.</summary>
/// <param name="FamilyName">Family name, used for fallback clues ("Mono", "Script", "Sans"…).</param>
/// <param name="IbmFamilyClass">OS/2 sFamilyClass high byte (0 = no classification).</param>
/// <param name="Panose">The 10-byte PANOSE description (all zeros = not set).</param>
/// <param name="IsFixedPitch">post.isFixedPitch: every glyph has the same width.</param>
public sealed record FontClassificationHints(string FamilyName, int IbmFamilyClass, IReadOnlyList<byte> Panose, bool IsFixedPitch);

/// <summary>
/// Decides a font's <see cref="FontCategory"/> automatically.
/// </summary>
/// <remarks>
/// Order of evidence, strongest first:
/// <list type="number">
///   <item>Symbol and monospace signals (they cut across other styles: a monospace font can have serifs).</item>
///   <item>The IBM font class and PANOSE values the designer stored in the file.</item>
///   <item>Clues in the family name, for fonts whose metadata is blank (common in free/demo fonts).</item>
/// </list>
/// Users can override the result per family; this is only the starting point.
/// </remarks>
public static class FontClassifier
{
    // IBM font classes (OS/2 sFamilyClass high byte).
    // Classes 1–5 are serif families (oldstyle, transitional, modern, clarendon, slab); 7 is freeform serif.
    private const int IbmOldstyleSerif = 1, IbmSlabSerif = 5, IbmFreeformSerif = 7,
        IbmSansSerif = 8, IbmOrnamental = 9, IbmScript = 10, IbmSymbolic = 12;

    // PANOSE family kinds (byte 0).
    private const byte PanoseLatinText = 2, PanoseHandwritten = 3, PanoseDecorative = 4, PanosePictorial = 5;

    private static readonly string[] SymbolWords = ["symbol", "icon", "emoji", "dingbat", "wingding", "webding", "mdl2", "assets", "marlett", "glyphs"];
    // Matched against whole words (or words ending in "mono", e.g. "SFMono"), so "Unicode" and "Monotype" don't count.
    private static readonly string[] MonoWords = ["mono", "code", "console", "typewriter", "courier", "terminal", "fixed"];
    private static readonly string[] ScriptWords = ["script", "hand", "brush", "signature", "calligraph", "cursive", "marker", "ink free", "print"];
    // Well-known family names, for fonts whose files carry no classification.
    private static readonly string[] KnownSans = ["helvetica", "gotham", "futura", "avenir", "inter", "roboto", "montserrat", "swiss", "segoe ui variable", "proxima", "sf pro", "sf compact"];
    private static readonly string[] KnownSerif = ["didot", "walbaum", "bodoni", "garamond", "baskerville", "caslon", "sitka", "times", "playfair"];
    private static readonly string[] KnownDisplay = ["minecraft", "mojang", "license plate", "nightmare"];

    private static readonly string[] DisplayWords = ["display", "deco", "poster", "stencil", "grunge", "pixel", "8px", "retro", "comic", "graffiti", "black", "fat"];

    public static FontCategory Classify(FontClassificationHints hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        var name = hints.FamilyName.ToLowerInvariant();
        var panose = hints.Panose.Count >= 4 ? hints.Panose : [0, 0, 0, 0];
        var kind = panose[0];
        var serifStyle = panose[1];
        var proportion = panose[3];

        // 1. Symbols and monospace.
        if (hints.IbmFamilyClass == IbmSymbolic || kind == PanosePictorial || ContainsAny(name, SymbolWords))
        {
            return FontCategory.Symbol;
        }

        if (hints.IsFixedPitch || (kind == PanoseLatinText && proportion == 9) || HasMonoWord(name))
        {
            return FontCategory.Monospace;
        }

        // 2. Designer-provided classification.
        if (hints.IbmFamilyClass == IbmScript || kind == PanoseHandwritten)
        {
            return FontCategory.Script;
        }

        if (hints.IbmFamilyClass == IbmOrnamental || kind == PanoseDecorative)
        {
            return FontCategory.Display;
        }

        if (hints.IbmFamilyClass == IbmSansSerif || (kind == PanoseLatinText && serifStyle is >= 11 and <= 15))
        {
            return FontCategory.SansSerif;
        }

        if (hints.IbmFamilyClass is >= IbmOldstyleSerif and <= IbmSlabSerif or IbmFreeformSerif
            || (kind == PanoseLatinText && serifStyle is >= 2 and <= 10))
        {
            return FontCategory.Serif;
        }

        // 3. Name clues.
        if (ContainsAny(name, KnownSans))
        {
            return FontCategory.SansSerif;
        }

        if (ContainsAny(name, KnownSerif))
        {
            return FontCategory.Serif;
        }

        if (ContainsAny(name, KnownDisplay))
        {
            return FontCategory.Display;
        }

        if (ContainsAny(name, ScriptWords))
        {
            return FontCategory.Script;
        }

        if (name.Contains("sans", StringComparison.Ordinal) || name.Contains("grotesk", StringComparison.Ordinal) || name.Contains("gothic", StringComparison.Ordinal))
        {
            return FontCategory.SansSerif;
        }

        if (name.Contains("serif", StringComparison.Ordinal) || name.Contains("slab", StringComparison.Ordinal) || name.Contains("roman", StringComparison.Ordinal))
        {
            return FontCategory.Serif;
        }

        if (ContainsAny(name, DisplayWords))
        {
            return FontCategory.Display;
        }

        return FontCategory.Other;
    }

    private static bool ContainsAny(string text, string[] words) => words.Any(w => text.Contains(w, StringComparison.Ordinal));

    private static bool HasMonoWord(string name)
        => name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => MonoWords.Contains(word) || word.EndsWith("mono", StringComparison.Ordinal));
}
