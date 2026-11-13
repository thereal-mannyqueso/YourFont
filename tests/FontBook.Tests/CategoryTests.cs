using FontBook.Core.Classification;
using FontBook.Core.Library;
using FontBook.Core.Models;
using FontBook.Core.Services;
using FontBook.Infrastructure.Database;
using FontBook.Infrastructure.Fonts.Files;

namespace FontBook.Tests;

public sealed class FontClassifierTests
{
    private static readonly byte[] NoPanose = new byte[10];

    private static FontCategory Classify(string name, int ibmClass = 0, byte[]? panose = null, bool fixedPitch = false)
        => FontClassifier.Classify(new FontClassificationHints(name, ibmClass, panose ?? NoPanose, fixedPitch));

    [Theory]
    [InlineData(1, FontCategory.Serif)]       // oldstyle serif
    [InlineData(5, FontCategory.Serif)]       // slab serif
    [InlineData(8, FontCategory.SansSerif)]
    [InlineData(9, FontCategory.Display)]     // ornamental
    [InlineData(10, FontCategory.Script)]
    [InlineData(12, FontCategory.Symbol)]
    public void UsesIbmFontClass(int ibmClass, FontCategory expected) => Assert.Equal(expected, Classify("Untitled", ibmClass));

    [Fact]
    public void UsesPanose()
    {
        Assert.Equal(FontCategory.SansSerif, Classify("X", panose: [2, 11, 5, 2, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(FontCategory.Serif, Classify("X", panose: [2, 2, 5, 2, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(FontCategory.Script, Classify("X", panose: [3, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(FontCategory.Display, Classify("X", panose: [4, 0, 0, 0, 0, 0, 0, 0, 0, 0]));
        Assert.Equal(FontCategory.Monospace, Classify("X", panose: [2, 11, 5, 9, 0, 0, 0, 0, 0, 0]));
    }

    [Fact]
    public void FixedPitchWinsOverSerifs() => Assert.Equal(FontCategory.Monospace, Classify("Courier New", ibmClass: 2, fixedPitch: true));

    [Theory]
    [InlineData("Lucida Sans Unicode", FontCategory.SansSerif)] // "Unicode" is not "code"
    [InlineData("Monotype Corsiva", FontCategory.Script)]       // "Monotype" is not "mono"
    [InlineData("JetBrains Mono", FontCategory.Monospace)]
    [InlineData("Fira Code", FontCategory.Monospace)]
    [InlineData("Helvetica", FontCategory.SansSerif)]
    [InlineData("SF Pro Display", FontCategory.SansSerif)] // "Display" here is a size optimization, not a style
    [InlineData("Didot", FontCategory.Serif)]
    [InlineData("Minecraft", FontCategory.Display)]
    [InlineData("Dancing Script", FontCategory.Script)]
    [InlineData("Webdings", FontCategory.Symbol)]
    [InlineData("Totally Unknown", FontCategory.Other)]
    public void FallsBackToNameClues(string name, FontCategory expected)
        => Assert.Equal(expected, Classify(name, ibmClass: name == "Lucida Sans Unicode" ? 8 : name == "Monotype Corsiva" ? 10 : 0));

    [Theory]
    [InlineData("georgia.ttf", FontCategory.Serif)]
    [InlineData("arial.ttf", FontCategory.SansSerif)]
    [InlineData("consola.ttf", FontCategory.Monospace)]
    [InlineData("webdings.ttf", FontCategory.Symbol)]
    public void ClassifiesRealWindowsFonts(string file, FontCategory expected)
    {
        var face = FontFileReader.ReadFaces(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), file))[0];

        var category = FontClassifier.Classify(new FontClassificationHints(face.FamilyName, face.IbmFamilyClass, face.Panose ?? [], face.IsFixedPitch));

        Assert.Equal(expected, category);
    }
}

public sealed class CategoryLibraryTests
{
    private static readonly IReadOnlyList<FontFamilyInfo> Families =
    [
        new("Georgia", FontSource.System, [new FontFaceInfo("Regular")]) { DetectedCategory = FontCategory.Serif },
        new("Arial", FontSource.System, [new FontFaceInfo("Regular")]) { DetectedCategory = FontCategory.SansSerif },
        new("Cooper Black", FontSource.User, [new FontFaceInfo("Regular")]) { DetectedCategory = FontCategory.Serif },
    ];

    [Fact]
    public async Task FiltersAndCountsByCategory()
    {
        using var db = await TestDatabase.CreateAsync();
        var library = await CreateAsync(db);
        var all = new LibraryScope(LibraryScopeKind.AllFonts);

        Assert.Equal(["Cooper Black", "Georgia"], library.Query(all, category: FontCategory.Serif).Select(f => f.FamilyName));
        Assert.Equal(2, library.CountByCategory(all)[FontCategory.Serif]);
        Assert.Equal(1, library.CountByCategory(all, "arial")[FontCategory.SansSerif]);
    }

    [Fact]
    public async Task ManualCategory_OverridesDetection_PersistsAndCanBeReset()
    {
        using var db = await TestDatabase.CreateAsync();
        var library = await CreateAsync(db);
        var cooper = Families[2];

        await library.SetCategoryAsync(cooper.Key, FontCategory.Display);
        var reloaded = await CreateAsync(db);

        Assert.Equal(FontCategory.Display, reloaded.GetCategory(cooper));
        Assert.True(reloaded.IsCategoryManual(cooper.Key));
        Assert.Equal(["Cooper Black"], reloaded.Query(new LibraryScope(LibraryScopeKind.AllFonts), category: FontCategory.Display).Select(f => f.FamilyName));

        await reloaded.SetCategoryAsync(cooper.Key, null); // back to automatic
        Assert.Equal(FontCategory.Serif, reloaded.GetCategory(cooper));
        Assert.False(reloaded.IsCategoryManual(cooper.Key));
    }

    private static async Task<FontLibraryService> CreateAsync(TestDatabase db)
    {
        var library = new FontLibraryService(
            new FixedDiscovery(Families),
            new NoVault(),
            new SqliteFavoritesRepository(db.Connections),
            new SqliteCollectionRepository(db.Connections),
            new SqliteCategoryRepository(db.Connections),
            TimeProvider.System);
        await library.LoadAsync();
        return library;
    }

    private sealed class FixedDiscovery(IReadOnlyList<FontFamilyInfo> families) : IFontDiscoveryService
    {
        public bool IsSampleData => false;

        public Task<IReadOnlyList<FontFamilyInfo>> DiscoverAsync(CancellationToken cancellationToken = default) => Task.FromResult(families);
    }

    private sealed class NoVault : FontBook.Core.Vault.IFontVault
    {
        public Task<IReadOnlyList<FontFamilyInfo>> GetFamiliesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FontFamilyInfo>>([]);

        public Task<FontBook.Core.Vault.VaultImportResult> ImportAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
            => Task.FromResult(FontBook.Core.Vault.VaultImportResult.Empty);

        public Task<int> RemoveFamilyAsync(FontFamilyInfo family, CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task CleanUpAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
