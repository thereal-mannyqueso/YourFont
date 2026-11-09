using FontBook.Core.Models;

namespace FontBook.Core.Vault;

/// <summary>
/// The Font Vault: FontBook's own collection of imported font files, kept separate from
/// fonts installed in Windows. Importing copies files into the Vault (originals are only
/// read); removing deletes only FontBook's copies.
/// </summary>
public interface IFontVault
{
    /// <summary>The imported fonts, grouped into families (<see cref="FontSource.Imported"/>).</summary>
    Task<IReadOnlyList<FontFamilyInfo>> GetFamiliesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports font files. Folders are searched (including sub-folders) for supported font files.
    /// Never throws for a bad file: each problem is reported in the result instead.
    /// </summary>
    Task<VaultImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);

    /// <summary>Deletes FontBook's copies of every file belonging to an imported family.</summary>
    /// <exception cref="InvalidOperationException">The family is not an imported (Vault) family.</exception>
    Task<int> RemoveFamilyAsync(FontFamilyInfo family, CancellationToken cancellationToken = default);

    /// <summary>Deletes leftover files in the Vault folder that the database no longer references.</summary>
    Task CleanUpAsync(CancellationToken cancellationToken = default);
}

/// <summary>What happened during an import.</summary>
/// <param name="Imported">File names that were copied into the Vault.</param>
/// <param name="Duplicates">File names skipped because identical content is already in the Vault.</param>
/// <param name="Failed">Files that couldn't be imported, with a short, user-readable reason.</param>
/// <param name="ImportedFamilyNames">Families the imported files belong to.</param>
public sealed record VaultImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Duplicates,
    IReadOnlyList<VaultImportFailure> Failed,
    IReadOnlyList<string> ImportedFamilyNames)
{
    public static VaultImportResult Empty { get; } = new([], [], [], []);
}

public sealed record VaultImportFailure(string FileName, string Reason);
