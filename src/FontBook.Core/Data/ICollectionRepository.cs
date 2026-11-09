using FontBook.Core.Models;

namespace FontBook.Core.Data;

/// <summary>Raw storage for custom collections. Business rules live in <see cref="Collections.CollectionService"/>.</summary>
public interface ICollectionRepository
{
    Task<IReadOnlyList<FontCollection>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<FontCollection> CreateAsync(string name, CancellationToken cancellationToken = default);

    Task RenameAsync(long collectionId, string newName, CancellationToken cancellationToken = default);

    /// <summary>Deletes the collection and its membership rows. Never touches font files.</summary>
    Task DeleteAsync(long collectionId, CancellationToken cancellationToken = default);

    Task<IReadOnlySet<string>> GetFontKeysAsync(long collectionId, CancellationToken cancellationToken = default);

    /// <summary>Every (collection, font) pair, used to build the in-memory library state in one query.</summary>
    Task<IReadOnlyList<CollectionMembership>> GetAllMembershipsAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a font to a collection. Adding a font that is already present is a no-op.</summary>
    Task AddFontAsync(long collectionId, string fontKey, CancellationToken cancellationToken = default);

    Task RemoveFontAsync(long collectionId, string fontKey, CancellationToken cancellationToken = default);
}
