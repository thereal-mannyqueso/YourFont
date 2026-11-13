using FontBook.Core.Data;
using FontBook.Core.Models;
using FontBook.Core.Search;
using FontBook.Core.Services;
using FontBook.Core.Vault;

namespace FontBook.Core.Library;

/// <summary>
/// The in-memory state of the user's library: discovered fonts, favorites and collection
/// membership. Reads are instant (no database round-trip while typing in search);
/// every change is written to the database first, then applied in memory.
/// </summary>
public interface IFontLibraryService
{
    bool IsSampleData { get; }

    IReadOnlyList<FontFamilyInfo> Families { get; }

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Re-reads only the Font Vault (after an import or removal); installed fonts are kept as they are.</summary>
    Task RefreshVaultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Families in <paramref name="scope"/> that match <paramref name="searchText"/> and, if given,
    /// <paramref name="category"/>, in display order.
    /// </summary>
    /// <remarks>Without <paramref name="sort"/>: by name, except Recently Added (newest first).</remarks>
    IReadOnlyList<FontFamilyInfo> Query(LibraryScope scope, string? searchText = null, FontCategory? category = null, FontSortOrder? sort = null);

    int Count(LibraryScope scope);

    /// <summary>How many families of each category are in the scope (after search), for the filter chips.</summary>
    IReadOnlyDictionary<FontCategory, int> CountByCategory(LibraryScope scope, string? searchText = null);

    /// <summary>The category a family is shown under: the user's choice if any, otherwise the detected one.</summary>
    FontCategory GetCategory(FontFamilyInfo family);

    /// <summary>True when the user picked the category by hand.</summary>
    bool IsCategoryManual(string fontKey);

    /// <summary>Sets a manual category, or with <c>null</c> goes back to automatic detection.</summary>
    Task SetCategoryAsync(string fontKey, FontCategory? category, CancellationToken cancellationToken = default);

    bool IsFavorite(string fontKey);

    Task SetFavoriteAsync(string fontKey, bool isFavorite, CancellationToken cancellationToken = default);

    IReadOnlySet<long> GetCollectionsContaining(string fontKey);

    Task SetCollectionMembershipAsync(long collectionId, string fontKey, bool isMember, CancellationToken cancellationToken = default);

    /// <summary>Drops in-memory membership for a collection that was deleted from the database.</summary>
    void ForgetCollection(long collectionId);
}

public sealed class FontLibraryService(
    IFontDiscoveryService discovery,
    IFontVault vault,
    IFavoritesRepository favoritesRepository,
    ICollectionRepository collectionRepository,
    ICategoryRepository categoryRepository,
    TimeProvider timeProvider) : IFontLibraryService
{
    private readonly Dictionary<string, FontCategory> _manualCategories = new(StringComparer.Ordinal);
    /// <summary>How far back "Recently Added" looks.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(30);

    private readonly HashSet<string> _favorites = new(StringComparer.Ordinal);
    private readonly Dictionary<long, HashSet<string>> _memberships = [];
    private IReadOnlyList<FontFamilyInfo> _families = [];

    public bool IsSampleData => discovery.IsSampleData;

    public IReadOnlyList<FontFamilyInfo> Families => _families;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var installed = await discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);
        var imported = await vault.GetFamiliesAsync(cancellationToken).ConfigureAwait(false);
        var favorites = await favoritesRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var memberships = await collectionRepository.GetAllMembershipsAsync(cancellationToken).ConfigureAwait(false);
        var categories = await categoryRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        SetFamilies(installed.Concat(imported));

        _manualCategories.Clear();
        foreach (var (key, category) in categories)
        {
            _manualCategories[key] = category;
        }

        _favorites.Clear();
        _favorites.UnionWith(favorites);

        _memberships.Clear();
        foreach (var membership in memberships)
        {
            GetOrCreateMembers(membership.CollectionId).Add(membership.FontKey);
        }
    }

    public async Task RefreshVaultAsync(CancellationToken cancellationToken = default)
    {
        var imported = await vault.GetFamiliesAsync(cancellationToken).ConfigureAwait(false);
        SetFamilies(_families.Where(f => f.Source != FontSource.Imported).Concat(imported));
    }

    private void SetFamilies(IEnumerable<FontFamilyInfo> families)
        => _families = [.. families.OrderBy(f => f.FamilyName, StringComparer.CurrentCultureIgnoreCase)];

    public IReadOnlyList<FontFamilyInfo> Query(LibraryScope scope, string? searchText = null, FontCategory? category = null, FontSortOrder? sort = null)
    {
        var matches = _families.Where(f => IsInScope(f, scope)
                                           && FontSearch.Matches(f, searchText)
                                           && (category is null || GetCategory(f) == category));
        var order = sort ?? (scope.Kind == LibraryScopeKind.RecentlyAdded ? FontSortOrder.DateAddedNewest : FontSortOrder.NameAscending);
        return FontSorting.Sort(matches, order, GetCategory);
    }

    public int Count(LibraryScope scope) => _families.Count(f => IsInScope(f, scope));

    public IReadOnlyDictionary<FontCategory, int> CountByCategory(LibraryScope scope, string? searchText = null)
        => _families
            .Where(f => IsInScope(f, scope) && FontSearch.Matches(f, searchText))
            .GroupBy(GetCategory)
            .ToDictionary(g => g.Key, g => g.Count());

    public FontCategory GetCategory(FontFamilyInfo family)
        => _manualCategories.TryGetValue(family.Key, out var manual) ? manual : family.DetectedCategory;

    public bool IsCategoryManual(string fontKey) => _manualCategories.ContainsKey(fontKey);

    public async Task SetCategoryAsync(string fontKey, FontCategory? category, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        if (category is { } chosen)
        {
            await categoryRepository.SetAsync(fontKey, chosen, cancellationToken).ConfigureAwait(false);
            _manualCategories[fontKey] = chosen;
        }
        else
        {
            await categoryRepository.RemoveAsync(fontKey, cancellationToken).ConfigureAwait(false);
            _manualCategories.Remove(fontKey);
        }
    }

    public bool IsFavorite(string fontKey) => _favorites.Contains(fontKey);

    public async Task SetFavoriteAsync(string fontKey, bool isFavorite, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        if (isFavorite)
        {
            await favoritesRepository.AddAsync(fontKey, cancellationToken).ConfigureAwait(false);
            _favorites.Add(fontKey);
        }
        else
        {
            await favoritesRepository.RemoveAsync(fontKey, cancellationToken).ConfigureAwait(false);
            _favorites.Remove(fontKey);
        }
    }

    public IReadOnlySet<long> GetCollectionsContaining(string fontKey)
        => _memberships.Where(pair => pair.Value.Contains(fontKey)).Select(pair => pair.Key).ToHashSet();

    public async Task SetCollectionMembershipAsync(long collectionId, string fontKey, bool isMember, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontKey);
        if (isMember)
        {
            await collectionRepository.AddFontAsync(collectionId, fontKey, cancellationToken).ConfigureAwait(false);
            GetOrCreateMembers(collectionId).Add(fontKey);
        }
        else
        {
            await collectionRepository.RemoveFontAsync(collectionId, fontKey, cancellationToken).ConfigureAwait(false);
            if (_memberships.TryGetValue(collectionId, out var members))
            {
                members.Remove(fontKey);
            }
        }
    }

    public void ForgetCollection(long collectionId) => _memberships.Remove(collectionId);

    private bool IsInScope(FontFamilyInfo family, LibraryScope scope) => scope.Kind switch
    {
        // The Library shows fonts installed in Windows; imported Vault copies live under Font Vault.
        LibraryScopeKind.AllFonts => family.Source is FontSource.System or FontSource.User,
        LibraryScopeKind.SystemFonts => family.Source == FontSource.System,
        LibraryScopeKind.UserFonts => family.Source == FontSource.User,
        LibraryScopeKind.Favorites => _favorites.Contains(family.Key),
        LibraryScopeKind.ImportedFonts => family.Source == FontSource.Imported,
        LibraryScopeKind.AnyCollection => _memberships.Values.Any(m => m.Contains(family.Key)),
        LibraryScopeKind.RecentlyAdded => family.Source == FontSource.Imported
                                          && family.AddedAt is { } added
                                          && added >= timeProvider.GetUtcNow() - RecentWindow,
        LibraryScopeKind.Collection => scope.CollectionId is { } id
                                       && _memberships.TryGetValue(id, out var members)
                                       && members.Contains(family.Key),
        _ => false,
    };

    private HashSet<string> GetOrCreateMembers(long collectionId)
    {
        if (!_memberships.TryGetValue(collectionId, out var members))
        {
            members = new HashSet<string>(StringComparer.Ordinal);
            _memberships[collectionId] = members;
        }

        return members;
    }
}
