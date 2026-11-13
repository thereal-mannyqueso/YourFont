using System.IO.Compression;
using System.Security.Cryptography;
using FontBook.Core.Library;
using FontBook.Core.Models;
using FontBook.Infrastructure.Fonts.Discovery;
using FontBook.Infrastructure.Sharing;
using Microsoft.Extensions.Logging.Abstractions;

namespace FontBook.Tests;

public sealed class FontSortingTests
{
    private static readonly DateTimeOffset Day = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<FontFamilyInfo> Families =
    [
        new("Bravo", FontSource.System, [new FontFaceInfo("Regular")]) { AddedAt = Day.AddDays(2), DetectedCategory = FontCategory.SansSerif },
        new("alpha", FontSource.System, [new FontFaceInfo("Regular"), new FontFaceInfo("Bold", 700), new FontFaceInfo("Italic")]) { AddedAt = Day, DetectedCategory = FontCategory.Script },
        new("Charlie", FontSource.User, [new FontFaceInfo("Regular"), new FontFaceInfo("Bold", 700)]) { AddedAt = Day.AddDays(5), DetectedCategory = FontCategory.Serif },
        new("Delta", FontSource.User, [new FontFaceInfo("Regular")]) { AddedAt = null, DetectedCategory = FontCategory.Serif },
    ];

    [Theory]
    [InlineData(FontSortOrder.NameAscending, "alpha,Bravo,Charlie,Delta")]
    [InlineData(FontSortOrder.NameDescending, "Delta,Charlie,Bravo,alpha")]
    [InlineData(FontSortOrder.DateAddedNewest, "Charlie,Bravo,alpha,Delta")]   // unknown date last
    [InlineData(FontSortOrder.DateAddedOldest, "alpha,Bravo,Charlie,Delta")]   // unknown date last
    [InlineData(FontSortOrder.MostStyles, "alpha,Charlie,Bravo,Delta")]        // ties broken by name
    [InlineData(FontSortOrder.Category, "Charlie,Delta,Bravo,alpha")]          // Serif, Sans Serif, Script
    public void SortsAsExpected(FontSortOrder order, string expected)
        => Assert.Equal(expected, string.Join(",", FontSorting.Sort(Families, order, f => f.DetectedCategory).Select(f => f.FamilyName)));

    [Fact]
    public async Task Discovery_UsesTheFileCreationDateAsDateAdded()
    {
        var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var source = new FakeSource(new InstalledFontFile(Path.Combine(fonts, "georgia.ttf"), FontSource.System));

        var family = Assert.Single(await new WindowsFontDiscoveryService(source, NullLogger<WindowsFontDiscoveryService>.Instance).DiscoverAsync());

        Assert.Equal(File.GetCreationTimeUtc(Path.Combine(fonts, "georgia.ttf")), family.AddedAt!.Value.UtcDateTime);
    }

    private sealed class FakeSource(params InstalledFontFile[] files) : IInstalledFontFileSource
    {
        public IReadOnlyList<InstalledFontFile> GetInstalledFontFiles() => files;
    }
}

public sealed class FontSharingTests : IDisposable
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FontBookShareTests", Guid.NewGuid().ToString("N"));
    private readonly FontSharingService _sharing = new(NullLogger<FontSharingService>.Instance);

    public FontSharingTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Zip_ContainsEveryFontFileAndAnAboutFile_AndOriginalsAreUntouched()
    {
        var regular = CopyFont("georgia.ttf", "user-georgia.ttf");
        var bold = CopyFont("georgiab.ttf", "user-georgiab.ttf");
        var before = SHA256.HashData(File.ReadAllBytes(regular));
        var family = new FontFamilyInfo("Georgia", FontSource.User,
            [new FontFaceInfo("Regular", FilePath: regular), new FontFaceInfo("Bold", 700, FilePath: bold)]);
        var zipPath = Path.Combine(_root, "Georgia.zip");

        var result = await _sharing.CreateZipAsync(family, zipPath);

        Assert.Equal(2, result.FileCount);
        Assert.True(result.HasLicenseInfo); // Georgia carries a copyright notice
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Equal(["Georgia/About these fonts.txt", "Georgia/user-georgia.ttf", "Georgia/user-georgiab.ttf"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        using var about = new StreamReader(zip.GetEntry("Georgia/About these fonts.txt")!.Open());
        var text = await about.ReadToEndAsync();
        Assert.Contains("Copyright", text, StringComparison.Ordinal);
        Assert.Contains("user-georgiab.ttf  (Bold)", text, StringComparison.Ordinal);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(regular)));
        Assert.False(File.Exists(zipPath + ".tmp"));
    }

    [Fact]
    public async Task VaultCopies_GetReadableNamesInsideTheZip()
    {
        var hashed = CopyFont("georgia.ttf", "3f9e512f82eaf6f1b7869b82013fb79f.ttf");
        var family = new FontFamilyInfo("Georgia", FontSource.Imported, [new FontFaceInfo("Regular", FilePath: hashed)]);
        var zipPath = Path.Combine(_root, "Vault.zip");

        await _sharing.CreateZipAsync(family, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.FullName == "Georgia/Georgia Regular.ttf");
    }

    [Fact]
    public async Task SystemFonts_CantBeShared()
    {
        var family = new FontFamilyInfo("Georgia", FontSource.System, [new FontFaceInfo("Regular", FilePath: Path.Combine(Fonts, "georgia.ttf"))]);
        var zipPath = Path.Combine(_root, "System.zip");

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sharing.CreateZipAsync(family, zipPath));
        Assert.False(File.Exists(zipPath));
    }

    [Fact]
    public async Task FailedZip_LeavesNothingBehind()
    {
        var file = CopyFont("georgia.ttf", "g.ttf");
        var family = new FontFamilyInfo("Georgia", FontSource.User, [new FontFaceInfo("Regular", FilePath: file)]);
        var missingFolder = Path.Combine(_root, "does-not-exist", "out.zip");

        await Assert.ThrowsAnyAsync<IOException>(() => _sharing.CreateZipAsync(family, missingFolder));
        Assert.False(File.Exists(missingFolder + ".tmp"));
    }

    private string CopyFont(string systemFile, string asName)
    {
        var target = Path.Combine(_root, asName);
        File.Copy(Path.Combine(Fonts, systemFile), target);
        File.SetAttributes(target, FileAttributes.Normal);
        return target;
    }
}
