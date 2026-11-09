namespace FontBook.Core.Models;

/// <summary>
/// A font family (e.g. "Segoe UI") and the styles available for it.
/// </summary>
public sealed record FontFamilyInfo
{
    public FontFamilyInfo(string familyName, FontSource source, IReadOnlyList<FontFaceInfo> faces)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(familyName);
        ArgumentNullException.ThrowIfNull(faces);

        FamilyName = familyName;
        Source = source;
        Faces = faces;
        Key = FontKey.Create(source, familyName);
    }

    /// <summary>Stable identifier used to store favorites and collection membership.</summary>
    public string Key { get; }

    public string FamilyName { get; }

    public FontSource Source { get; }

    public IReadOnlyList<FontFaceInfo> Faces { get; }

    /// <summary>When the family was added to FontBook (used by "Recently Added"). Null if unknown.</summary>
    public DateTimeOffset? AddedAt { get; init; }

    /// <summary>Category detected automatically from the font's metadata (users can override it).</summary>
    public FontCategory DetectedCategory { get; init; } = FontCategory.Other;

    /// <summary>True when this entry comes from the temporary sample data set, not real discovery.</summary>
    public bool IsSample { get; init; }
}
