using System.Security.Cryptography;
using FontBook.Core.Library;
using FontBook.Core.Models;
using FontBook.Infrastructure.Database;
using FontBook.Infrastructure.Fonts.Discovery;
using FontBook.Infrastructure.Vault;
using Microsoft.Extensions.Logging.Abstractions;

namespace FontBook.Tests;

/// <summary>
/// Font Vault tests. Source fonts are first copied from C:\Windows\Fonts into a temp
/// "Downloads" folder so the tests import the copies, exactly like a user would.
/// </summary>
public sealed class FontVaultTests : IDisposable
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    private readonly TestDatabase _db;
    private readonly string _downloads;
    private readonly FontVaultService _vault;

    public FontVaultTests()
    {
        _db = TestDatabase.CreateAsync().GetAwaiter().GetResult();
        _downloads = Path.Combine(_db.Paths.RootDirectory, "Downloads");
        Directory.CreateDirectory(_downloads);
        _vault = new FontVaultService(_db.Paths, new SqliteVaultRepository(_db.Connections), NullLogger<FontVaultService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Import_CopiesIntoVault_AndLeavesOriginalUntouched()
    {
        var original = Download("georgia.ttf");
        var hashBefore = SHA256.HashData(File.ReadAllBytes(original));

        var result = await _vault.ImportAsync([original]);

        Assert.Equal(["georgia.ttf"], result.Imported);
        Assert.Equal(["Georgia"], result.ImportedFamilyNames);
        Assert.True(File.Exists(original));
        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(original)));
        var copy = Assert.Single(VaultFiles());
        Assert.Equal(hashBefore, SHA256.HashData(File.ReadAllBytes(copy)));
    }

    [Fact]
    public async Task Import_SameContentTwice_IsReportedAsDuplicate()
    {
        var first = Download("arial.ttf");
        var renamed = Download("arial.ttf", "my-arial-copy.ttf");

        await _vault.ImportAsync([first]);
        var second = await _vault.ImportAsync([renamed]);

        Assert.Empty(second.Imported);
        Assert.Equal(["my-arial-copy.ttf"], second.Duplicates);
        Assert.Single(VaultFiles());
    }

    [Fact]
    public async Task Import_RejectsBadFiles_WithoutStoringThem()
    {
        var fake = Path.Combine(_downloads, "fake.ttf");
        File.WriteAllText(fake, "not really a font");
        var text = Path.Combine(_downloads, "notes.txt");
        File.WriteAllText(text, "hello");

        var result = await _vault.ImportAsync([fake, text, Path.Combine(_downloads, "missing.otf")]);

        Assert.Empty(result.Imported);
        Assert.Equal(3, result.Failed.Count);
        Assert.Empty(VaultFiles());
    }

    [Fact]
    public async Task Import_Folder_FindsFontsInSubfolders()
    {
        var sub = Path.Combine(_downloads, "Fonts", "Serif");
        Directory.CreateDirectory(sub);
        File.Copy(Path.Combine(Fonts, "georgia.ttf"), Path.Combine(sub, "georgia.ttf"));
        File.Copy(Path.Combine(Fonts, "georgiab.ttf"), Path.Combine(sub, "georgiab.ttf"));
        File.WriteAllText(Path.Combine(sub, "readme.txt"), "ignored");

        var result = await _vault.ImportAsync([_downloads]);
        var families = await _vault.GetFamiliesAsync();

        Assert.Equal(2, result.Imported.Count);
        var georgia = Assert.Single(families);
        Assert.Equal(FontSource.Imported, georgia.Source);
        Assert.Equal(["Regular", "Bold"], georgia.Faces.Select(f => f.StyleName));
        Assert.All(georgia.Faces, f => Assert.StartsWith(_db.Paths.VaultDirectory, f.FilePath));
        Assert.NotNull(georgia.AddedAt);
    }

    [Fact]
    public async Task Remove_DeletesOnlyVaultCopies()
    {
        var original = Download("georgia.ttf");
        await _vault.ImportAsync([original, Download("arial.ttf")]);
        var georgia = (await _vault.GetFamiliesAsync()).Single(f => f.FamilyName == "Georgia");

        var removed = await _vault.RemoveFamilyAsync(georgia);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(original));
        Assert.True(File.Exists(Path.Combine(Fonts, "georgia.ttf")));
        Assert.Equal(["Arial"], (await _vault.GetFamiliesAsync()).Select(f => f.FamilyName));
        Assert.Single(VaultFiles());
    }

    [Fact]
    public async Task Remove_RefusesInstalledFonts()
    {
        var installed = new FontFamilyInfo("Georgia", FontSource.System,
            [new FontFaceInfo("Regular", FilePath: Path.Combine(Fonts, "georgia.ttf"))]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _vault.RemoveFamilyAsync(installed));
        Assert.True(File.Exists(Path.Combine(Fonts, "georgia.ttf")));
    }

    [Fact]
    public async Task CleanUp_DeletesUnreferencedFiles_AndKeepsReferencedOnes()
    {
        await _vault.ImportAsync([Download("arial.ttf")]);
        var orphan = Path.Combine(_db.Paths.VaultDirectory, "leftover.ttf.tmp");
        File.WriteAllText(orphan, "partial copy");

        await _vault.CleanUpAsync();

        Assert.False(File.Exists(orphan));
        Assert.Single(VaultFiles());
    }

    [Fact]
    public async Task Library_ShowsImportedFonts_InVaultScopesOnly()
    {
        await _vault.ImportAsync([Download("georgia.ttf")]);
        var library = new FontLibraryService(
            new WindowsFontDiscoveryService(new NoInstalledFonts(), NullLogger<WindowsFontDiscoveryService>.Instance),
            _vault,
            new SqliteFavoritesRepository(_db.Connections),
            new SqliteCollectionRepository(_db.Connections),
            new SqliteCategoryRepository(_db.Connections),
            TimeProvider.System);

        await library.LoadAsync();

        Assert.Single(library.Query(new LibraryScope(LibraryScopeKind.ImportedFonts)));
        Assert.Single(library.Query(new LibraryScope(LibraryScopeKind.RecentlyAdded)));
        Assert.Empty(library.Query(new LibraryScope(LibraryScopeKind.AllFonts)));

        var georgia = library.Query(new LibraryScope(LibraryScopeKind.ImportedFonts))[0];
        await _vault.RemoveFamilyAsync(georgia);
        await library.RefreshVaultAsync();
        Assert.Empty(library.Query(new LibraryScope(LibraryScopeKind.ImportedFonts)));
    }

    [Fact]
    public async Task EachCopyLivesInItsOwnFolder_AndRemovalLeavesNoEmptyFolders()
    {
        await _vault.ImportAsync([Download("georgia.ttf"), Download("arial.ttf")]);

        var folders = Directory.GetDirectories(_db.Paths.VaultDirectory);
        Assert.Equal(2, folders.Length);
        Assert.All(folders, f => Assert.Single(Directory.GetFiles(f)));

        var georgia = (await _vault.GetFamiliesAsync()).Single(f => f.FamilyName == "Georgia");
        await _vault.RemoveFamilyAsync(georgia);

        Assert.Single(Directory.GetDirectories(_db.Paths.VaultDirectory));
    }

    [Fact]
    public async Task CleanUp_MovesFilesFromTheOldFlatLayout()
    {
        await _vault.ImportAsync([Download("georgia.ttf")]);
        var current = Assert.Single(VaultFiles());
        var legacy = Path.Combine(_db.Paths.VaultDirectory, Path.GetFileName(current));
        File.Move(current, legacy); // simulate a Vault written by an older FontBook
        Directory.Delete(Path.GetDirectoryName(current)!);

        await _vault.CleanUpAsync();

        Assert.False(File.Exists(legacy));
        Assert.True(File.Exists(current));
        Assert.Equal(["Georgia"], (await _vault.GetFamiliesAsync()).Select(f => f.FamilyName));
    }

    private string[] VaultFiles() => Directory.GetFiles(_db.Paths.VaultDirectory, "*", SearchOption.AllDirectories);

    private string Download(string systemFont, string? asName = null)
    {
        var target = Path.Combine(_downloads, asName ?? systemFont);
        File.Copy(Path.Combine(Fonts, systemFont), target, overwrite: true);
        File.SetAttributes(target, FileAttributes.Normal);
        return target;
    }

    private sealed class NoInstalledFonts : IInstalledFontFileSource
    {
        public IReadOnlyList<InstalledFontFile> GetInstalledFontFiles() => [];
    }
}
