using FontBook.Core.Collections;
using FontBook.Core.Library;
using FontBook.Core.Models;
using FontBook.Core.Search;
using FontBook.Core.Services;
using FontBook.Infrastructure.Database;

namespace FontBook.Tests;

public sealed class FontSearchTests
{
    private static readonly FontFamilyInfo Georgia = new("Georgia", FontSource.System,
        [new FontFaceInfo("Regular"), new FontFaceInfo("Bold Italic", 700, FontSlant.Italic)]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("georgia")]
    [InlineData("GEOR")]
    [InlineData("georgia bold")]
    [InlineData("italic")]
    public void Matches_FamilyOrStyleTerms(string? query) => Assert.True(FontSearch.Matches(Georgia, query));

    [Theory]
    [InlineData("arial")]
    [InlineData("georgia condensed")]
    public void DoesNotMatch_WhenAnyTermIsMissing(string query) => Assert.False(FontSearch.Matches(Georgia, query));
}

public sealed class CollectionNamingTests
{
    [Fact]
    public void NormalizeName_TrimsWhitespace() => Assert.Equal("Serifs", CollectionService.NormalizeName("  Serifs  "));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void NormalizeName_RejectsEmpty(string? name) => Assert.Throws<ArgumentException>(() => CollectionService.NormalizeName(name));

    [Fact]
    public void NormalizeName_RejectsTooLong()
        => Assert.Throws<ArgumentException>(() => CollectionService.NormalizeName(new string('x', CollectionService.MaxNameLength + 1)));

    [Fact]
    public void NextUntitledName_PicksLowestFreeNumber()
    {
        Assert.Equal("New Collection", CollectionService.NextUntitledName([]));
        Assert.Equal("New Collection 2", CollectionService.NextUntitledName(["new collection"]));
        Assert.Equal("New Collection 3", CollectionService.NextUntitledName(["New Collection", "New Collection 2", "New Collection 4"]));
    }
}

public sealed class FontKeyTests
{
    [Fact]
    public void Key_IncludesSource_SoVaultCopiesNeverCollideWithInstalledFonts()
    {
        Assert.Equal("system:segoe ui", FontKey.Create(FontSource.System, " Segoe UI "));
        Assert.NotEqual(FontKey.Create(FontSource.System, "Inter"), FontKey.Create(FontSource.Imported, "Inter"));
    }
}

public sealed class FontLibraryServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<FontFamilyInfo> Families =
    [
        new("Georgia", FontSource.System, [new FontFaceInfo("Regular")]),
        new("Arial", FontSource.System, [new FontFaceInfo("Regular"), new FontFaceInfo("Bold", 700)]),
        new("Bahnschrift", FontSource.User, [new FontFaceInfo("Regular")]),
        new("Inter", FontSource.Imported, [new FontFaceInfo("Regular")]) { AddedAt = Now.AddDays(-2) },
        new("Old Vault Font", FontSource.Imported, [new FontFaceInfo("Regular")]) { AddedAt = Now.AddDays(-90) },
    ];

    [Fact]
    public async Task Query_ScopesSeparateLibraryFromVault()
    {
        var (library, db) = await CreateAsync();
        using var _ = db;

        Assert.Equal(["Arial", "Bahnschrift", "Georgia"], Names(library.Query(new LibraryScope(LibraryScopeKind.AllFonts))));
        Assert.Equal(["Arial", "Georgia"], Names(library.Query(new LibraryScope(LibraryScopeKind.SystemFonts))));
        Assert.Equal(["Bahnschrift"], Names(library.Query(new LibraryScope(LibraryScopeKind.UserFonts))));
        Assert.Equal(["Inter", "Old Vault Font"], Names(library.Query(new LibraryScope(LibraryScopeKind.ImportedFonts))));
        Assert.Equal(["Inter"], Names(library.Query(new LibraryScope(LibraryScopeKind.RecentlyAdded))));
    }

    [Fact]
    public async Task Query_AppliesSearchWithinScope()
    {
        var (library, db) = await CreateAsync();
        using var _ = db;

        Assert.Equal(["Arial"], Names(library.Query(new LibraryScope(LibraryScopeKind.AllFonts), "bold")));
        Assert.Equal(3, library.Count(new LibraryScope(LibraryScopeKind.AllFonts)));
    }

    [Fact]
    public async Task Favorites_PersistAcrossReload()
    {
        var (library, db) = await CreateAsync();
        using var _ = db;
        var georgia = Families[0].Key;

        await library.SetFavoriteAsync(georgia, true);
        var reloaded = CreateService(db);
        await reloaded.LoadAsync();

        Assert.True(reloaded.IsFavorite(georgia));
        Assert.Equal(["Georgia"], Names(reloaded.Query(new LibraryScope(LibraryScopeKind.Favorites))));

        await reloaded.SetFavoriteAsync(georgia, false);
        Assert.Empty(reloaded.Query(new LibraryScope(LibraryScopeKind.Favorites)));
    }

    [Fact]
    public async Task CollectionMembership_DrivesCollectionScopes()
    {
        var (library, db) = await CreateAsync();
        using var _ = db;
        var collections = new CollectionService(new SqliteCollectionRepository(db.Connections));
        var serifs = await collections.CreateAsync("Serifs");
        var georgia = Families[0].Key;

        await library.SetCollectionMembershipAsync(serifs.Id, georgia, true);

        Assert.Equal(["Georgia"], Names(library.Query(LibraryScope.ForCollection(serifs.Id))));
        Assert.Equal(["Georgia"], Names(library.Query(new LibraryScope(LibraryScopeKind.AnyCollection))));
        Assert.Contains(serifs.Id, library.GetCollectionsContaining(georgia));

        await collections.DeleteAsync(serifs.Id);
        library.ForgetCollection(serifs.Id);
        Assert.Empty(library.Query(new LibraryScope(LibraryScopeKind.AnyCollection)));
    }

    private static string[] Names(IEnumerable<FontFamilyInfo> families) => [.. families.Select(f => f.FamilyName)];

    private static async Task<(FontLibraryService Library, TestDatabase Db)> CreateAsync()
    {
        var db = await TestDatabase.CreateAsync();
        var library = CreateService(db);
        await library.LoadAsync();
        return (library, db);
    }

    private static FontLibraryService CreateService(TestDatabase db) => new(
        new FixedDiscovery(Families),
        new EmptyVault(),
        new SqliteFavoritesRepository(db.Connections),
        new SqliteCollectionRepository(db.Connections),
        new SqliteCategoryRepository(db.Connections),
        new FixedTimeProvider(Now));

    private sealed class FixedDiscovery(IReadOnlyList<FontFamilyInfo> families) : IFontDiscoveryService
    {
        public bool IsSampleData => false;

        public Task<IReadOnlyList<FontFamilyInfo>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(families);
    }

    private sealed class EmptyVault : FontBook.Core.Vault.IFontVault
    {
        public Task<IReadOnlyList<FontFamilyInfo>> GetFamiliesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<FontFamilyInfo>>([]);

        public Task<FontBook.Core.Vault.VaultImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
            => Task.FromResult(FontBook.Core.Vault.VaultImportResult.Empty);

        public Task<int> RemoveFamilyAsync(FontFamilyInfo family, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task CleanUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
