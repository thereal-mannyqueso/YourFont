using FontBook.Core.Models;

namespace FontBook.Core.Library;

/// <summary>How the font list is ordered.</summary>
public enum FontSortOrder
{
    NameAscending,
    NameDescending,
    DateAddedNewest,
    DateAddedOldest,
    MostStyles,
    Category,
}

public static class FontSorting
{
    /// <summary>Menu order.</summary>
    public static IReadOnlyList<FontSortOrder> All { get; } =
    [
        FontSortOrder.NameAscending, FontSortOrder.NameDescending,
        FontSortOrder.DateAddedNewest, FontSortOrder.DateAddedOldest,
        FontSortOrder.MostStyles, FontSortOrder.Category,
    ];

    public static string DisplayName(this FontSortOrder order) => order switch
    {
        FontSortOrder.NameAscending => "Name (A–Z)",
        FontSortOrder.NameDescending => "Name (Z–A)",
        FontSortOrder.DateAddedNewest => "Date Added (Newest First)",
        FontSortOrder.DateAddedOldest => "Date Added (Oldest First)",
        FontSortOrder.MostStyles => "Most Styles",
        FontSortOrder.Category => "Category",
        _ => order.ToString(),
    };

    /// <summary>Short label for the Sort button.</summary>
    public static string ShortName(this FontSortOrder order) => order switch
    {
        FontSortOrder.NameAscending => "Name",
        FontSortOrder.NameDescending => "Name (Z–A)",
        FontSortOrder.DateAddedNewest => "Newest",
        FontSortOrder.DateAddedOldest => "Oldest",
        FontSortOrder.MostStyles => "Most Styles",
        FontSortOrder.Category => "Category",
        _ => order.ToString(),
    };

    /// <summary>True for the date-based orders (the list then shows each font's date).</summary>
    public static bool IsByDate(this FontSortOrder order) => order is FontSortOrder.DateAddedNewest or FontSortOrder.DateAddedOldest;

    /// <summary>
    /// Orders families. Name is always the tie-breaker, so the result is stable and predictable.
    /// Fonts with an unknown date go last in both date orders.
    /// </summary>
    public static IReadOnlyList<FontFamilyInfo> Sort(IEnumerable<FontFamilyInfo> families, FontSortOrder order, Func<FontFamilyInfo, FontCategory> categoryOf)
    {
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(categoryOf);
        var byName = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<FontFamilyInfo> sorted = order switch
        {
            FontSortOrder.NameDescending => families.OrderByDescending(f => f.FamilyName, byName),
            FontSortOrder.DateAddedNewest => families.OrderBy(f => f.AddedAt is null).ThenByDescending(f => f.AddedAt).ThenBy(f => f.FamilyName, byName),
            FontSortOrder.DateAddedOldest => families.OrderBy(f => f.AddedAt is null).ThenBy(f => f.AddedAt).ThenBy(f => f.FamilyName, byName),
            FontSortOrder.MostStyles => families.OrderByDescending(f => f.Faces.Count).ThenBy(f => f.FamilyName, byName),
            FontSortOrder.Category => families.OrderBy(f => categoryOf(f)).ThenBy(f => f.FamilyName, byName),
            _ => families.OrderBy(f => f.FamilyName, byName),
        };
        return [.. sorted];
    }
}
