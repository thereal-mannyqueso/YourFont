using FontBook.Core.Data;
using FontBook.Core.Models;

namespace FontBook.Core.Collections;

/// <summary>
/// Collection management: creating, renaming and deleting collections, with validation
/// and naming rules on top of the repository. Which fonts are *in* a collection is
/// handled by <see cref="Library.IFontLibraryService"/>, which keeps that state in memory.
/// </summary>
public interface ICollectionService
{
    Task<IReadOnlyList<FontCollection>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a collection called "New Collection" (or "New Collection 2", 3, …).</summary>
    Task<FontCollection> CreateUntitledAsync(CancellationToken cancellationToken = default);

    Task<FontCollection> CreateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Renames a collection and returns the normalized (trimmed) name that was stored.</summary>
    Task<string> RenameAsync(long collectionId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes a collection. Fonts themselves are never touched.</summary>
    Task DeleteAsync(long collectionId, CancellationToken cancellationToken = default);
}

public sealed class CollectionService(ICollectionRepository repository) : ICollectionService
{
    public const int MaxNameLength = 100;
    public const string DefaultName = "New Collection";

    public Task<IReadOnlyList<FontCollection>> GetAllAsync(CancellationToken cancellationToken = default)
        => repository.GetAllAsync(cancellationToken);

    public async Task<FontCollection> CreateUntitledAsync(CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return await repository.CreateAsync(NextUntitledName(existing.Select(c => c.Name)), cancellationToken).ConfigureAwait(false);
    }

    public Task<FontCollection> CreateAsync(string name, CancellationToken cancellationToken = default)
        => repository.CreateAsync(NormalizeName(name), cancellationToken);

    public async Task<string> RenameAsync(long collectionId, string newName, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeName(newName);
        await repository.RenameAsync(collectionId, normalized, cancellationToken).ConfigureAwait(false);
        return normalized;
    }

    public Task DeleteAsync(long collectionId, CancellationToken cancellationToken = default)
        => repository.DeleteAsync(collectionId, cancellationToken);

    /// <summary>Trims the name and checks it is usable. Throws <see cref="ArgumentException"/> otherwise.</summary>
    public static string NormalizeName(string? name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("A collection name cannot be empty.", nameof(name));
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"A collection name can be at most {MaxNameLength} characters.", nameof(name));
        }

        return trimmed;
    }

    /// <summary>Returns "New Collection", or "New Collection N" with the lowest free N.</summary>
    public static string NextUntitledName(IEnumerable<string> existingNames)
    {
        var taken = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(DefaultName))
        {
            return DefaultName;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{DefaultName} {n}";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }
}
