using System.Globalization;
using System.IO.Compression;
using System.Text;
using FontBook.Core.Models;
using FontBook.Core.Sharing;
using FontBook.Infrastructure.Fonts.Files;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.Sharing;

/// <summary>
/// Creates a shareable ZIP of a font family. The ZIP is written to a temporary file first and moved
/// into place at the end, so a failure never leaves a half-written ZIP behind.
/// </summary>
public sealed class FontSharingService(ILogger<FontSharingService> logger) : IFontSharingService
{
    public Task<FontShareResult> CreateZipAsync(FontFamilyInfo family, string zipPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        if (family.Source == FontSource.System)
        {
            throw new InvalidOperationException(
                "Windows system fonts are licensed with Windows and can't be shared. You can share fonts you installed yourself or imported into the Font Vault.");
        }

        var files = family.Faces.Select(f => f.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists).ToList();
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"No font files were found for \"{family.FamilyName}\".");
        }

        return Task.Run(() => Write(family, files, zipPath, cancellationToken), cancellationToken);
    }

    private FontShareResult Write(FontFamilyInfo family, List<string> files, string zipPath, CancellationToken cancellationToken)
    {
        var folderName = SafeName(family.FamilyName);
        var temp = zipPath + ".tmp";
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var details = new List<(string FileName, IReadOnlyList<FontFaceData> Faces)>();

        try
        {
            using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    IReadOnlyList<FontFaceData> faces;
                    try
                    {
                        faces = FontFileReader.ReadFaces(file);
                    }
                    catch (InvalidDataException)
                    {
                        faces = [];
                    }

                    var entryName = UniqueName(PreferredFileName(family, file, faces), usedNames);
                    var entry = zip.CreateEntry($"{folderName}/{entryName}", CompressionLevel.Optimal);
                    using (var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var target = entry.Open())
                    {
                        source.CopyTo(target);
                    }

                    details.Add((entryName, faces));
                }

                var about = zip.CreateEntry($"{folderName}/About these fonts.txt", CompressionLevel.Optimal);
                using var writer = new StreamWriter(about.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(BuildAbout(family, details));
            }

            File.Move(temp, zipPath, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        logger.LogInformation("Shared \"{Family}\" as {Zip} ({Count} files)", family.FamilyName, zipPath, files.Count);
        var hasLicense = details.SelectMany(d => d.Faces).Any(f => f.Copyright is not null || f.License is not null);
        return new FontShareResult(zipPath, files.Count, hasLicense);
    }

    /// <summary>Installed fonts keep their file name; Vault copies (named by hash) become "Family Style.ext".</summary>
    private static string PreferredFileName(FontFamilyInfo family, string file, IReadOnlyList<FontFaceData> faces)
    {
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (family.Source != FontSource.Imported)
        {
            return Path.GetFileName(file);
        }

        var style = faces.Count > 0 ? faces[0].StyleName : "Regular";
        return SafeName($"{family.FamilyName} {style}") + extension;
    }

    private static string BuildAbout(FontFamilyInfo family, List<(string FileName, IReadOnlyList<FontFaceData> Faces)> details)
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"{family.FamilyName}");
        text.AppendLine(new string('=', Math.Max(4, family.FamilyName.Length)));
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Shared from FontBook for Windows on {DateTime.Now.ToString("D", CultureInfo.CurrentCulture)}.");
        text.AppendLine();
        text.AppendLine("Files:");
        foreach (var (fileName, faces) in details)
        {
            var styles = faces.Count > 0 ? string.Join(", ", faces.Select(f => f.StyleName).Distinct()) : "unknown style";
            text.AppendLine(CultureInfo.InvariantCulture, $"  - {fileName}  ({styles})");
        }

        var faceData = details.SelectMany(d => d.Faces).ToList();
        AppendDistinct(text, "Copyright", faceData.Select(f => f.Copyright));
        AppendDistinct(text, "License", faceData.Select(f => f.License));
        AppendDistinct(text, "License URL", faceData.Select(f => f.LicenseUrl));

        text.AppendLine();
        text.AppendLine("How to install: open the folder, right-click a font file and choose \"Install\" (or open it with FontBook).");
        text.AppendLine();
        text.AppendLine("Please respect the font's license. Many fonts can't be redistributed; only share fonts you have the right to share.");
        return text.ToString();
    }

    private static void AppendDistinct(StringBuilder text, string title, IEnumerable<string?> values)
    {
        var distinct = values.OfType<string>().Select(v => v.Trim()).Where(v => v.Length > 0).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{title}:");
        foreach (var value in distinct)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {value}");
        }
    }

    private static string UniqueName(string name, HashSet<string> used)
    {
        var candidate = name;
        for (var n = 2; !used.Add(candidate); n++)
        {
            candidate = $"{Path.GetFileNameWithoutExtension(name)} ({n}){Path.GetExtension(name)}";
        }

        return candidate;
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim();
        return cleaned.Length == 0 ? "Font" : cleaned;
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't delete temporary file {Path}", path);
        }
    }
}
