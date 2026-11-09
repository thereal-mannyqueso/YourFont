namespace FontBook.Core.Library;

/// <summary>Which part of the library the user is looking at (one per sidebar entry).</summary>
public enum LibraryScopeKind
{
    AllFonts,
    SystemFonts,
    UserFonts,
    Favorites,
    ImportedFonts,

    /// <summary>"Custom Collections": every font that is in at least one collection.</summary>
    AnyCollection,
    RecentlyAdded,

    /// <summary>A single collection, identified by <see cref="LibraryScope.CollectionId"/>.</summary>
    Collection,
}

public readonly record struct LibraryScope(LibraryScopeKind Kind, long? CollectionId = null)
{
    public static LibraryScope ForCollection(long collectionId) => new(LibraryScopeKind.Collection, collectionId);
}
