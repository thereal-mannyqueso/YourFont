using System.Globalization;

namespace FontBook.Core.Models;

/// <summary>
/// Builds the stable string key that identifies a font family in the database.
/// Format: "&lt;source&gt;:&lt;family name, lower-case&gt;", e.g. "system:segoe ui".
/// Keys include the source, so an imported copy of a font and the installed
/// version of the same font are never confused with each other.
/// </summary>
public static class FontKey
{
    public static string Create(FontSource source, string familyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        var prefix = source switch
        {
            FontSource.System => "system",
            FontSource.User => "user",
            FontSource.Imported => "vault",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null),
        };
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}:{familyName.Trim().ToLowerInvariant()}");
    }
}
