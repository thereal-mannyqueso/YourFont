using FontBook.Core.Models;

namespace FontBook.Core.Sharing;

/// <summary>
/// Share (beta): packs a font family into a ZIP file that can be sent to someone else.
/// </summary>
public interface IFontSharingService
{
    /// <summary>
    /// Writes a ZIP containing every file of <paramref name="family"/> plus an "About these fonts.txt"
    /// with styles and any copyright/license text found in the fonts. Font files are only read.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The family is a Windows system font (licensed with Windows, not for redistribution), or has no files.
    /// </exception>
    Task<FontShareResult> CreateZipAsync(FontFamilyInfo family, string zipPath, CancellationToken cancellationToken = default);
}

/// <param name="ZipPath">Where the ZIP was saved.</param>
/// <param name="FileCount">Number of font files inside.</param>
/// <param name="HasLicenseInfo">True when at least one font carried copyright or license text.</param>
public sealed record FontShareResult(string ZipPath, int FileCount, bool HasLicenseInfo);
