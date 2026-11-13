using FontBook.Core.Vault;

namespace FontBook.Core.FontMaker;

/// <summary>Everything needed to write a font file from traced glyphs.</summary>
/// <param name="FamilyName">Name shown in font menus.</param>
/// <param name="Glyphs">One outline per character.</param>
/// <param name="Ascent">Height above the baseline, in font units (1000 per em).</param>
/// <param name="Descent">Depth below the baseline, as a positive number.</param>
public sealed record FontDefinition(string FamilyName, IReadOnlyList<GlyphOutline> Glyphs, int Ascent, int Descent);

/// <summary>Writes a font file (TrueType) from a <see cref="FontDefinition"/>.</summary>
public interface IFontFileBuilder
{
    byte[] Build(FontDefinition font);
}

/// <summary>
/// Font Maker (beta): turns a filled-in, scanned template into a font in the Font Vault.
/// </summary>
public interface IFontMakerService
{
    /// <summary>Finds the template in a scan and cuts out every character box.</summary>
    Task<TemplateScanResult> ReadScanAsync(GrayImage scan, CancellationToken cancellationToken = default);

    /// <summary>Traces the glyphs, writes a TrueType font and imports it into the Font Vault.</summary>
    /// <exception cref="ArgumentException">The name is empty, too long, or uses unsupported characters.</exception>
    /// <exception cref="InvalidOperationException">No characters were found, or the name is already used in the Vault.</exception>
    Task<VaultImportResult> CreateFontAsync(string familyName, IReadOnlyList<ScannedGlyph> glyphs, CancellationToken cancellationToken = default);
}
