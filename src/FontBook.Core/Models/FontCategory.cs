namespace FontBook.Core.Models;

/// <summary>Broad style categories used to organize fonts.</summary>
public enum FontCategory
{
    Serif,
    SansSerif,
    Monospace,

    /// <summary>Script and handwritten fonts.</summary>
    Script,

    /// <summary>Display, decorative and "fun" fonts.</summary>
    Display,

    /// <summary>Symbol, icon and dingbat fonts.</summary>
    Symbol,

    /// <summary>Not enough information to decide.</summary>
    Other,
}

public static class FontCategoryNames
{
    /// <summary>Display order in the UI.</summary>
    public static IReadOnlyList<FontCategory> All { get; } =
    [
        FontCategory.Serif, FontCategory.SansSerif, FontCategory.Monospace,
        FontCategory.Script, FontCategory.Display, FontCategory.Symbol, FontCategory.Other,
    ];

    public static string DisplayName(this FontCategory category) => category switch
    {
        FontCategory.Serif => "Serif",
        FontCategory.SansSerif => "Sans Serif",
        FontCategory.Monospace => "Monospace",
        FontCategory.Script => "Script & Handwritten",
        FontCategory.Display => "Display & Fun",
        FontCategory.Symbol => "Symbol",
        _ => "Other",
    };

    /// <summary>Short label for filter chips.</summary>
    public static string ShortName(this FontCategory category) => category switch
    {
        FontCategory.SansSerif => "Sans Serif",
        FontCategory.Script => "Script",
        FontCategory.Display => "Display",
        _ => category.DisplayName(),
    };
}
