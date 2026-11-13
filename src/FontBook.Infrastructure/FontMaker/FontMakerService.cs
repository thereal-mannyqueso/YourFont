using FontBook.Core.FontMaker;
using FontBook.Core.Vault;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.FontMaker;

/// <summary>Font Maker (beta): scan → glyph outlines → TrueType file → Font Vault.</summary>
public sealed class FontMakerService(IFontFileBuilder builder, IFontVault vault, ILogger<FontMakerService> logger) : IFontMakerService
{
    public const int MaxNameLength = 40;

    public Task<TemplateScanResult> ReadScanAsync(GrayImage scan, CancellationToken cancellationToken = default)
        => Task.Run(() => TemplateScanReader.Read(scan), cancellationToken);

    public async Task<VaultImportResult> CreateFontAsync(string familyName, IReadOnlyList<ScannedGlyph> glyphs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(glyphs);
        var name = NormalizeName(familyName);

        var existing = await vault.GetFamiliesAsync(cancellationToken).ConfigureAwait(false);
        if (existing.Any(f => string.Equals(f.FamilyName, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Your Font Vault already has a font called \"{name}\". Choose another name, or remove the old one first.");
        }

        var outlines = await Task.Run(() => glyphs.Select(GlyphTracer.BuildOutline).OfType<GlyphOutline>().ToList(), cancellationToken)
            .ConfigureAwait(false);
        if (outlines.Count == 0)
        {
            throw new InvalidOperationException("No characters were found on the template, so there's nothing to build a font from.");
        }

        var bytes = builder.Build(new FontDefinition(name, outlines, FontTemplateLayout.Ascent, FontTemplateLayout.Descent));

        // Write to a private temp folder, import (which copies into the Vault), then clean up.
        var folder = Path.Combine(Path.GetTempPath(), "FontBook", "FontMaker", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, SafeFileName(name) + ".ttf");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Font Maker built \"{Name}\" with {Count} glyphs", name, outlines.Count);
            return await vault.ImportAsync([path], cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "Couldn't delete temporary folder {Folder}", folder);
            }
        }
    }

    /// <summary>Trims the name and checks it's usable as a font family name.</summary>
    public static string NormalizeName(string? name)
    {
        var trimmed = string.Join(' ', (name ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Give your font a name.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"Font names can be at most {MaxNameLength} characters.");
        }

        if (!trimmed.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '\''))
        {
            throw new ArgumentException("Use only letters, numbers, spaces, hyphens and apostrophes in the font name.");
        }

        return trimmed;
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]);
    }
}
