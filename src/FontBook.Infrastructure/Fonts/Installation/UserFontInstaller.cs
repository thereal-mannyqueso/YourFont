using System.Security.Cryptography;
using FontBook.Core.Models;
using FontBook.Core.Services;
using FontBook.Infrastructure.Fonts.Files;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.Fonts.Installation;

/// <summary>
/// Per-user font installation — see <see cref="IFontInstallationService"/> for the mechanism.
/// </summary>
/// <remarks>
/// Safety rules enforced here:
/// <list type="bullet">
///   <item>Install only Font Vault families; uninstall only per-user (User) families.</item>
///   <item>Only files inside the per-user fonts folder are ever deleted. A User font registered elsewhere
///         is unregistered but its file is left in place.</item>
///   <item>If a file is in use, it is re-registered and left installed rather than half-removed.</item>
///   <item>The source file of an install (the Vault copy) is only read.</item>
/// </list>
/// </remarks>
public sealed class UserFontInstaller(
    string userFontsDirectory,
    IUserFontRegistry registry,
    IFontResourceApi fontApi,
    ILogger<UserFontInstaller> logger) : IFontInstallationService
{
    /// <summary>Windows' per-user fonts folder.</summary>
    public static string DefaultUserFontsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");

    public Task<FontInstallResult> InstallForCurrentUserAsync(FontFamilyInfo family, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Source != FontSource.Imported)
        {
            throw new InvalidOperationException("Only fonts from the Font Vault can be installed.");
        }

        return Task.Run(() => Install(family, cancellationToken), cancellationToken);
    }

    public Task<FontUninstallResult> UninstallForCurrentUserAsync(FontFamilyInfo family, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Source != FontSource.User)
        {
            throw new InvalidOperationException(family.Source == FontSource.System
                ? "System fonts can't be uninstalled with FontBook."
                : "Only fonts installed for your account can be uninstalled.");
        }

        return Task.Run(() => Uninstall(family, cancellationToken), cancellationToken);
    }

    private FontInstallResult Install(FontFamilyInfo family, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(userFontsDirectory);
        var installed = 0;
        var problems = new List<string>();
        var entries = registry.GetEntries();

        foreach (var source in DistinctFiles(family))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? copied = null;
            string? valueName = null;
            try
            {
                var faces = FontFileReader.ReadFaces(source); // validates the file
                var target = ChooseTargetPath(source, faces[0]);
                if (!File.Exists(target))
                {
                    var temp = target + ".tmp";
                    File.Copy(source, temp, overwrite: true);
                    File.SetAttributes(temp, FileAttributes.Normal);
                    File.Move(temp, target);
                    copied = target;
                }

                valueName = UniqueValueName(DescribeFaces(faces), target, entries);
                registry.SetEntry(valueName, target);
                if (!fontApi.AddFontResource(target))
                {
                    throw new InvalidDataException("Windows couldn't load this font file.");
                }

                installed++;
                copied = null; // committed
                valueName = null;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                logger.LogWarning(ex, "Installing {Path} failed", source);
                problems.Add($"{Path.GetFileName(source)}: {ex.Message}");
            }
            finally
            {
                // Roll back a partial install of this file.
                if (valueName is not null)
                {
                    registry.RemoveEntry(valueName);
                }

                if (copied is not null)
                {
                    TryDelete(copied);
                }
            }
        }

        if (installed > 0)
        {
            fontApi.BroadcastFontChange();
        }

        return new FontInstallResult(installed, problems);
    }

    private FontUninstallResult Uninstall(FontFamilyInfo family, CancellationToken cancellationToken)
    {
        var removed = 0;
        var inUse = new List<string>();
        var entries = registry.GetEntries();

        foreach (var file in DistinctFiles(family))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fontApi.RemoveFontResource(file);

            if (IsInsideUserFontsFolder(file) && File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // An app has the font open: keep it fully installed instead of half-removing it.
                    logger.LogInformation(ex, "Font file {Path} is in use", file);
                    fontApi.AddFontResource(file);
                    inUse.Add(Path.GetFileName(file));
                    continue;
                }
            }
            else if (File.Exists(file))
            {
                logger.LogInformation("Unregistering {Path} without deleting it (outside the per-user fonts folder)", file);
            }

            foreach (var entry in entries.Where(e => SamePath(e.Value, file)))
            {
                registry.RemoveEntry(entry.Key);
            }

            removed++;
        }

        if (removed > 0)
        {
            fontApi.BroadcastFontChange();
        }

        return new FontUninstallResult(removed, inUse);
    }

    private static IEnumerable<string> DistinctFiles(FontFamilyInfo family)
        => family.Faces.Select(f => f.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Family Style.ext" in the per-user fonts folder; reuses an identical existing file, otherwise picks a free name.</summary>
    private string ChooseTargetPath(string source, FontFaceData face)
    {
        var extension = Path.GetExtension(source).ToLowerInvariant();
        var baseName = Sanitize($"{face.FamilyName} {face.StyleName}");
        var sourceHash = Hash(source);
        for (var n = 1; ; n++)
        {
            var candidate = Path.Combine(userFontsDirectory, n == 1 ? baseName + extension : $"{baseName} ({n}){extension}");
            if (!File.Exists(candidate) || Hash(candidate).AsSpan().SequenceEqual(sourceHash))
            {
                return candidate;
            }
        }
    }

    /// <summary>Registry value name in Windows' own style, e.g. "Georgia Bold (TrueType)".</summary>
    private static string DescribeFaces(IReadOnlyList<FontFaceData> faces)
        => string.Join(" & ", faces.Select(f => string.Equals(f.StyleName, "Regular", StringComparison.OrdinalIgnoreCase)
            ? f.FamilyName
            : $"{f.FamilyName} {f.StyleName}").Distinct()) + " (TrueType)";

    private static string UniqueValueName(string name, string target, IReadOnlyDictionary<string, string> entries)
    {
        var candidate = name;
        for (var n = 2; entries.TryGetValue(candidate, out var existing) && !SamePath(existing, target); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }

    private bool IsInsideUserFontsFolder(string path)
    {
        var folder = Path.GetFullPath(userFontsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(folder, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim();
        return cleaned.Length == 0 ? "Font" : cleaned;
    }

    private static byte[] Hash(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return SHA256.HashData(stream);
    }

    private void TryDelete(string path)
    {
        try
        {
            if (IsInsideUserFontsFolder(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't roll back {Path}", path);
        }
    }
}
