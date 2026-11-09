namespace FontBook.Core.Models;

/// <summary>Slant of a font face.</summary>
public enum FontSlant
{
    Normal,
    Italic,
    Oblique,
}

/// <summary>
/// One style (face) within a family, e.g. "Bold Italic".
/// Uses plain numbers instead of WPF types so Core stays UI-independent.
/// </summary>
/// <param name="StyleName">Human readable style name, e.g. "Semibold Italic".</param>
/// <param name="Weight">OpenType weight, 1–1000 (400 = Regular, 700 = Bold).</param>
/// <param name="Slant">Upright, italic or oblique.</param>
/// <param name="Stretch">OpenType width class, 1–9 (5 = Normal).</param>
/// <param name="FilePath">Font file on disk, when known.</param>
/// <param name="FileFamilyName">
/// The family name a text renderer matches on for this face (e.g. "Segoe UI" for Segoe UI Semibold).
/// Together with weight/slant/stretch it picks this exact face out of <paramref name="FilePath"/>.
/// </param>
public sealed record FontFaceInfo(
    string StyleName,
    int Weight = 400,
    FontSlant Slant = FontSlant.Normal,
    int Stretch = 5,
    string? FilePath = null,
    string? FileFamilyName = null);
