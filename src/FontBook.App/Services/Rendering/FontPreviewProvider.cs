using System.Windows;
using System.Windows.Media;
using FontBook.Core.Models;

namespace FontBook.App.Services.Rendering;

/// <summary>
/// Translates FontBook's UI-independent font models into the WPF objects used to draw text.
/// </summary>
/// <remarks>
/// Each face is loaded straight from its own font file — <c>new FontFamily(fileUri, "#Family")</c> —
/// which works for system fonts, per-user fonts and (later) Font Vault copies alike, and
/// never requires installing anything. WPF only reads the file.
/// All of this is kept behind this interface so it can later be swapped for a lower-level
/// DirectWrite renderer (e.g. for variable-font axes) without touching views.
/// </remarks>
public interface IFontPreviewProvider
{
    /// <summary>The WPF family used to draw <paramref name="face"/> (or the family by name if the face has no file).</summary>
    FontFamily GetFontFamily(FontFamilyInfo family, FontFaceInfo? face);

    FontWeight GetWeight(FontFaceInfo face);

    FontStyle GetStyle(FontFaceInfo face);

    FontStretch GetStretch(FontFaceInfo face);
}

public sealed class WpfFontPreviewProvider : IFontPreviewProvider
{
    private readonly Dictionary<string, FontFamily> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FontFamily GetFontFamily(FontFamilyInfo family, FontFaceInfo? face)
    {
        ArgumentNullException.ThrowIfNull(family);

        if (face?.FilePath is not { } path)
        {
            return Cached(family.FamilyName, () => new FontFamily(family.FamilyName));
        }

        // The name inside the file identifies the face (important for .ttc collections holding several families).
        var nameInFile = face.FileFamilyName ?? family.FamilyName;

        if (family.Source == FontSource.Imported && Path.GetDirectoryName(path) is { } vaultFolder)
        {
            // Font Vault copies are resolved through their folder ("./#Name"). WPF's single-file lookup
            // rejects some valid fonts (including ones made by Font Maker) and silently falls back to
            // Segoe UI, while the folder lookup reads them correctly. The Vault folder only holds Vault copies.
            var folderUri = new Uri(vaultFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            return Cached($"{vaultFolder}|{nameInFile}", () => new FontFamily(folderUri, "./#" + nameInFile));
        }

        return Cached($"{path}|{nameInFile}", () => new FontFamily(new Uri(path), "#" + nameInFile));
    }

    public FontWeight GetWeight(FontFaceInfo face) => FontWeight.FromOpenTypeWeight(Math.Clamp(face.Weight, 1, 999));

    public FontStyle GetStyle(FontFaceInfo face) => face.Slant switch
    {
        FontSlant.Italic => FontStyles.Italic,
        FontSlant.Oblique => FontStyles.Oblique,
        _ => FontStyles.Normal,
    };

    public FontStretch GetStretch(FontFaceInfo face) => FontStretch.FromOpenTypeStretch(Math.Clamp(face.Stretch, 1, 9));

    private FontFamily Cached(string key, Func<FontFamily> create)
    {
        if (!_cache.TryGetValue(key, out var fontFamily))
        {
            fontFamily = create();
            _cache[key] = fontFamily;
        }

        return fontFamily;
    }
}
