using FontBook.Core.Classification;
using FontBook.Core.Models;
using FontBook.Core.Services;
using FontBook.Infrastructure.Fonts.Files;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.Fonts.Discovery;

/// <summary>
/// Finds the fonts installed on this PC: reads Windows' installed-font list, opens each
/// file read-only to get its names and style, and groups faces into families.
/// System-wide fonts become <see cref="FontSource.System"/>; fonts installed only for the
/// current user become <see cref="FontSource.User"/>.
/// </summary>
public sealed class WindowsFontDiscoveryService(IInstalledFontFileSource fileSource, ILogger<WindowsFontDiscoveryService> logger)
    : IFontDiscoveryService
{
    public bool IsSampleData => false;

    public Task<IReadOnlyList<FontFamilyInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
        // Reading a few hundred files takes a moment, so keep it off the UI thread.
        => Task.Run(() => Discover(cancellationToken), cancellationToken);

    private IReadOnlyList<FontFamilyInfo> Discover(CancellationToken cancellationToken)
    {
        var faces = new List<(FontFaceData Data, FontSource Source)>();
        int skippedUnsupported = 0, skippedMissing = 0, skippedUnreadable = 0;

        foreach (var file in fileSource.GetInstalledFontFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!FontFileReader.IsSupported(file.Path))
            {
                skippedUnsupported++; // e.g. legacy .fon bitmap fonts, which modern renderers can't draw
                continue;
            }

            if (!File.Exists(file.Path))
            {
                skippedMissing++;
                continue;
            }

            try
            {
                faces.AddRange(FontFileReader.ReadFaces(file.Path).Select(face => (face, file.Source)));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or EndOfStreamException)
            {
                skippedUnreadable++;
                logger.LogWarning(ex, "Skipping unreadable font file {Path}", file.Path);
            }
        }

        var families = Group(faces);
        logger.LogInformation(
            "Discovered {Families} families ({Faces} faces). Skipped: {Unsupported} unsupported, {Missing} missing, {Unreadable} unreadable files.",
            families.Count, faces.Count, skippedUnsupported, skippedMissing, skippedUnreadable);
        return families;
    }

    /// <summary>Groups faces by source + family name, removing duplicate styles and ordering them sensibly.</summary>
    internal static IReadOnlyList<FontFamilyInfo> Group(IEnumerable<(FontFaceData Data, FontSource Source)> faces)
        => [.. faces
            .GroupBy(f => (f.Source, Family: f.Data.FamilyName.ToUpperInvariant()))
            .Select(group =>
            {
                var styles = group
                    .Select(f => f.Data)
                    .DistinctBy(d => d.StyleName, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(d => d.Width == 5 ? 0 : 1) // normal width first, then condensed/expanded
                    .ThenBy(d => d.Width)
                    .ThenBy(d => d.Weight)
                    .ThenBy(d => d.IsItalic)
                    .Select(d => new FontFaceInfo(
                        d.StyleName,
                        d.Weight,
                        d.IsItalic ? FontSlant.Italic : FontSlant.Normal,
                        d.Width,
                        d.FilePath,
                        d.RenderFamilyName))
                    .ToList();
                // Classify the family by its regular style (falling back to any style).
                var reference = group.Select(f => f.Data).FirstOrDefault(d => d is { Weight: 400, Width: 5, IsItalic: false })
                                ?? group.First().Data;
                var category = FontClassifier.Classify(new FontClassificationHints(
                    reference.FamilyName, reference.IbmFamilyClass, reference.Panose ?? [], reference.IsFixedPitch));

                return new FontFamilyInfo(group.First().Data.FamilyName, group.Key.Source, styles) { DetectedCategory = category };
            })];
}
