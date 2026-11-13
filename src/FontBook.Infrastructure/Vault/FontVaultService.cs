using System.Security.Cryptography;
using FontBook.Core.Models;
using FontBook.Core.Vault;
using FontBook.Infrastructure.Database;
using FontBook.Infrastructure.Fonts.Discovery;
using FontBook.Infrastructure.Fonts.Files;
using Microsoft.Extensions.Logging;

namespace FontBook.Infrastructure.Vault;

/// <summary>
/// Stores imported fonts as copies in %LOCALAPPDATA%\FontBook\Vault.
/// </summary>
/// <remarks>
/// Safety rules:
/// <list type="bullet">
///   <item>Original files are only ever opened for reading (to hash and copy them).</item>
///   <item>Copies are named by their SHA-256 content hash, so identical files are stored once.</item>
///   <item>Deletion is limited to files inside the Vault folder — checked on every delete.</item>
///   <item>Windows may keep a previewed font file open; if a copy can't be deleted right away its
///         database row is still removed, and <see cref="CleanUpAsync"/> deletes the file on the next start.</item>
/// </list>
/// </remarks>
public sealed class FontVaultService(AppPaths paths, SqliteVaultRepository repository, ILogger<FontVaultService> logger) : IFontVault
{
    public async Task<IReadOnlyList<FontFamilyInfo>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        var records = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            var importedAtByPath = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
            var faces = new List<(FontFaceData, FontSource)>();
            foreach (var record in records)
            {
                var path = StoredPath(record);
                foreach (var face in ReadFacesOrEmpty(path))
                {
                    faces.Add((face, FontSource.Imported));
                }

                importedAtByPath[path] = record.ImportedAt;
            }

            return (IReadOnlyList<FontFamilyInfo>)[.. WindowsFontDiscoveryService.Group(faces).Select(family => family with
            {
                AddedAt = family.Faces
                    .Select(f => f.FilePath is { } p && importedAtByPath.TryGetValue(p, out var at) ? at : DateTimeOffset.MinValue)
                    .Max(),
            })];
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<VaultImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
        => Task.Run(() => ImportCoreAsync(paths, cancellationToken), cancellationToken);

    public async Task<int> RemoveFamilyAsync(FontFamilyInfo family, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(family);
        if (family.Source != FontSource.Imported)
        {
            throw new InvalidOperationException("Only fonts imported into the Font Vault can be removed from it.");
        }

        // Find every Vault file containing a face of this family (including duplicate styles
        // that were hidden from the style list), so the family doesn't reappear after removal.
        var removed = 0;
        foreach (var record in await repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            var path = StoredPath(record);
            var belongs = ReadFacesOrEmpty(path).Any(f => string.Equals(f.FamilyName, family.FamilyName, StringComparison.OrdinalIgnoreCase));
            if (!belongs)
            {
                continue;
            }

            await repository.RemoveAsync(record.Id, cancellationToken).ConfigureAwait(false);
            TryDeleteVaultFile(path);
            TryDeleteEmptyFolder(Path.GetDirectoryName(path)!);
            removed++;
        }

        return removed;
    }

    public async Task CleanUpAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(paths.VaultDirectory))
        {
            return;
        }

        var records = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        // Older versions stored copies directly in the Vault folder; move them into their own sub-folder.
        foreach (var record in records)
        {
            var legacy = Path.Combine(paths.VaultDirectory, record.StoredName);
            var current = StoredPath(record);
            if (File.Exists(legacy) && !File.Exists(current))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(current)!);
                    File.Move(legacy, current);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogWarning(ex, "Couldn't move Vault file {Path} into its folder", legacy);
                }
            }
        }

        // Delete anything the database no longer refers to (removed fonts whose files were in use, partial copies).
        var referencedFolders = records.Select(FolderName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referencedLegacy = records.Select(r => r.StoredName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(paths.VaultDirectory))
        {
            if (!referencedLegacy.Contains(Path.GetFileName(file)) || File.Exists(Path.Combine(paths.VaultDirectory, Path.GetFileNameWithoutExtension(file), Path.GetFileName(file))))
            {
                TryDeleteVaultFile(file);
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(paths.VaultDirectory))
        {
            if (!referencedFolders.Contains(Path.GetFileName(folder)))
            {
                foreach (var file in Directory.EnumerateFiles(folder))
                {
                    TryDeleteVaultFile(file);
                }

                TryDeleteEmptyFolder(folder);
            }
        }
    }

    private async Task<VaultImportResult> ImportCoreAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.VaultDirectory);
        var imported = new List<string>();
        var duplicates = new List<string>();
        var failed = new List<VaultImportFailure>();
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in ExpandInputs(inputs, failed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileName(source);
            string? copy = null;
            try
            {
                if (!FontFileReader.IsSupported(source))
                {
                    failed.Add(new(name, "Not a supported font type (TTF, OTF, TTC or OTC)."));
                    continue;
                }

                var hash = await HashAsync(source, cancellationToken).ConfigureAwait(false);
                if (await repository.ContainsHashAsync(hash, cancellationToken).ConfigureAwait(false))
                {
                    duplicates.Add(name);
                    continue;
                }

                var storedName = hash + Path.GetExtension(source).ToLowerInvariant();
                var finalPath = Path.Combine(paths.VaultDirectory, hash, storedName);
                var tempPath = finalPath + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

                // Copy (the original is only read), then check the copy really is a font.
                File.Copy(source, tempPath, overwrite: true);
                File.SetAttributes(tempPath, FileAttributes.Normal); // don't inherit read-only, so removal works
                copy = tempPath;
                var faces = FontFileReader.ReadFaces(tempPath);
                File.Move(tempPath, finalPath, overwrite: true);
                copy = finalPath;

                await repository.AddAsync(hash, storedName, name, Path.GetFullPath(source), new FileInfo(finalPath).Length, cancellationToken)
                    .ConfigureAwait(false);
                copy = null; // committed
                imported.Add(name);
                families.UnionWith(faces.Select(f => f.FamilyName));
            }
            catch (InvalidDataException)
            {
                failed.Add(new(name, "The file isn't a valid font or is damaged."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
            {
                logger.LogWarning(ex, "Importing {Path} failed", source);
                failed.Add(new(name, ex is UnauthorizedAccessException ? "FontBook doesn't have permission to read this file." : ex.Message));
            }
            finally
            {
                if (copy is not null)
                {
                    TryDeleteVaultFile(copy); // roll back a partial import
                }
            }
        }

        return new VaultImportResult(imported, duplicates, failed, [.. families.Order(StringComparer.CurrentCultureIgnoreCase)]);
    }

    /// <summary>Turns the user's selection into a list of files, searching any folders.</summary>
    private static List<string> ExpandInputs(IReadOnlyList<string> inputs, List<VaultImportFailure> failed)
    {
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (Directory.Exists(input))
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories).Where(FontFileReader.IsSupported));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add(new(Path.GetFileName(input), "This folder couldn't be read."));
                }
            }
            else if (File.Exists(input))
            {
                files.Add(input);
            }
            else
            {
                failed.Add(new(Path.GetFileName(input), "The file no longer exists."));
            }
        }

        return [.. files.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Each copy lives in its own folder named after its content hash: Vault\&lt;hash&gt;\&lt;hash&gt;.ttf.
    /// WPF caches what it finds in a font folder for the whole session; giving every file its own
    /// folder means a removed font and a new font with the same name never share a cached folder
    /// (that combination crashed DirectWrite with a stack overflow).
    /// </summary>
    private string StoredPath(VaultFileRecord record) => Path.Combine(paths.VaultDirectory, FolderName(record), record.StoredName);

    private static string FolderName(VaultFileRecord record) => record.Sha256;

    private void TryDeleteEmptyFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogInformation(ex, "Vault folder {Folder} couldn't be removed yet", folder);
        }
    }

    private IReadOnlyList<FontFaceData> ReadFacesOrEmpty(string path)
    {
        try
        {
            return File.Exists(path) ? FontFileReader.ReadFaces(path) : [];
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Vault file {Path} is unreadable", path);
            return [];
        }
    }

    /// <summary>Deletes a file, but only if it is inside the Vault folder.</summary>
    private void TryDeleteVaultFile(string path)
    {
        var vault = Path.GetFullPath(paths.VaultDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (!full.StartsWith(vault, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Refusing to delete {Path}: it is outside the Font Vault", full);
            return;
        }

        try
        {
            File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Usually Windows still has the font open for preview. CleanUpAsync retries on next start.
            logger.LogInformation(ex, "Vault file {Path} is in use; it will be deleted on next start", full);
        }
    }
}
