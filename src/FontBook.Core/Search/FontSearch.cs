using FontBook.Core.Models;

namespace FontBook.Core.Search;

/// <summary>
/// Search rules for the font list. Every word in the query must appear in either
/// the family name or one of its style names, ignoring case.
/// Examples: "segoe" → Segoe UI, Segoe Print; "georgia bold" → Georgia (has Bold).
/// </summary>
public static class FontSearch
{
    private static readonly char[] Separators = [' ', '\t'];

    public static bool Matches(FontFamilyInfo family, string? query)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var terms = query.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var term in terms)
        {
            var inFamily = family.FamilyName.Contains(term, StringComparison.OrdinalIgnoreCase);
            var inStyle = family.Faces.Any(f => f.StyleName.Contains(term, StringComparison.OrdinalIgnoreCase));
            if (!inFamily && !inStyle)
            {
                return false;
            }
        }

        return true;
    }
}
