using System.Windows;
using System.Windows.Media;
using FontBook.Core.Models;

namespace FontBook.App.Services.Rendering;

/// <summary>
/// Translates FontBook's UI-independent font models into the WPF objects used to draw text.
/// </summary>
/// <remarks>
/// WPF's text stack is built on DirectWrite-era font technology (OpenType, TrueType, TTC).
/// It can also load a font straight from a file — <c>new FontFamily(folderUri, "./#Family")</c> —
/// without installing it, which is how the Font Vault will preview imported fonts.
/// All of that knowledge is kept behind this interface so it can later be swapped for a
/// lower-level DirectWrite renderer (e.g. for variable-font axes) without touching views.
/// </remarks>
public interface IFontPreviewProvider
{
    FontFamily GetFontFamily(FontFamilyInfo family);

    FontWeight GetWeight(FontFaceInfo face);

    FontStyle GetStyle(FontFaceInfo face);

    FontStretch GetStretch(FontFaceInfo face);
}

public sealed class WpfFontPreviewProvider : IFontPreviewProvider
{
    private readonly Dictionary<string, FontFamily> _cache = new(StringComparer.Ordinal);

    public FontFamily GetFontFamily(FontFamilyInfo family)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (!_cache.TryGetValue(family.Key, out var wpfFamily))
        {
            // Installed fonts are resolved by family name. File-based loading for imported
            // fonts is added together with the Font Vault in Milestone 2.
            wpfFamily = new FontFamily(family.FamilyName);
            _cache[family.Key] = wpfFamily;
        }

        return wpfFamily;
    }

    public FontWeight GetWeight(FontFaceInfo face) => FontWeight.FromOpenTypeWeight(Math.Clamp(face.Weight, 1, 999));

    public FontStyle GetStyle(FontFaceInfo face) => face.Slant switch
    {
        FontSlant.Italic => FontStyles.Italic,
        FontSlant.Oblique => FontStyles.Oblique,
        _ => FontStyles.Normal,
    };

    public FontStretch GetStretch(FontFaceInfo face) => FontStretch.FromOpenTypeStretch(Math.Clamp(face.Stretch, 1, 9));
}
