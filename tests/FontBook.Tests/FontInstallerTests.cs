using FontBook.Core.Models;
using FontBook.Infrastructure.Fonts.Installation;
using Microsoft.Extensions.Logging.Abstractions;

namespace FontBook.Tests;

/// <summary>
/// Installer tests use a temp folder instead of the real per-user fonts folder and fakes for the
/// registry and Windows font APIs, so running them never changes this PC's installed fonts.
/// </summary>
public sealed class FontInstallerTests : IDisposable
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "FontBookInstallerTests", Guid.NewGuid().ToString("N"));
    private readonly string _userFonts;
    private readonly string _vault;
    private readonly FakeRegistry _registry = new();
    private readonly FakeFontApi _api = new();
    private readonly UserFontInstaller _installer;

    public FontInstallerTests()
    {
        _userFonts = Path.Combine(_root, "UserFonts");
        _vault = Path.Combine(_root, "Vault");
        Directory.CreateDirectory(_vault);
        _installer = new UserFontInstaller(_userFonts, _registry, _api, NullLogger<UserFontInstaller>.Instance);
    }

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
    public async Task Install_CopiesRegistersAndNotifiesWindows()
    {
        var family = VaultFamily("georgia.ttf", "georgiab.ttf");

        var result = await _installer.InstallForCurrentUserAsync(family);

        Assert.Equal(2, result.InstalledFiles);
        Assert.Empty(result.Problems);
        var regular = Path.Combine(_userFonts, "Georgia Regular.ttf");
        var bold = Path.Combine(_userFonts, "Georgia Bold.ttf");
        Assert.True(File.Exists(regular));
        Assert.True(File.Exists(bold));
        Assert.Equal(regular, _registry.Entries["Georgia (TrueType)"]);
        Assert.Equal(bold, _registry.Entries["Georgia Bold (TrueType)"]);
        Assert.Equal([regular, bold], _api.Added);
        Assert.Equal(1, _api.Broadcasts);
        Assert.All(family.Faces, f => Assert.True(File.Exists(f.FilePath))); // Vault copies untouched
    }

    [Fact]
    public async Task Install_RefusesFontsThatAreNotInTheVault()
    {
        var system = new FontFamilyInfo("Georgia", FontSource.System, [new FontFaceInfo("Regular", FilePath: Path.Combine(Fonts, "georgia.ttf"))]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.InstallForCurrentUserAsync(system));
        Assert.Empty(_registry.Entries);
    }

    [Fact]
    public async Task Install_WhenWindowsRejectsTheFile_RollsBack()
    {
        _api.RejectAdds = true;

        var result = await _installer.InstallForCurrentUserAsync(VaultFamily("georgia.ttf"));

        Assert.Equal(0, result.InstalledFiles);
        Assert.Single(result.Problems);
        Assert.Empty(_registry.Entries);
        Assert.False(Directory.EnumerateFiles(_userFonts).Any());
        Assert.Equal(0, _api.Broadcasts);
    }

    [Fact]
    public async Task Uninstall_DeletesFileAndRegistration()
    {
        await _installer.InstallForCurrentUserAsync(VaultFamily("georgia.ttf"));
        var installed = UserFamily(Path.Combine(_userFonts, "Georgia Regular.ttf"));

        var result = await _installer.UninstallForCurrentUserAsync(installed);

        Assert.Equal(1, result.RemovedFiles);
        Assert.Empty(result.InUse);
        Assert.Empty(_registry.Entries);
        Assert.False(File.Exists(installed.Faces[0].FilePath));
        Assert.Contains(installed.Faces[0].FilePath, _api.Removed);
    }

    [Fact]
    public async Task Uninstall_RefusesSystemFonts()
    {
        var system = new FontFamilyInfo("Georgia", FontSource.System, [new FontFaceInfo("Regular", FilePath: Path.Combine(Fonts, "georgia.ttf"))]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _installer.UninstallForCurrentUserAsync(system));
        Assert.True(File.Exists(Path.Combine(Fonts, "georgia.ttf")));
        Assert.Empty(_api.Removed);
    }

    [Fact]
    public async Task Uninstall_FileInUse_StaysFullyInstalled()
    {
        await _installer.InstallForCurrentUserAsync(VaultFamily("georgia.ttf"));
        var path = Path.Combine(_userFonts, "Georgia Regular.ttf");

        FontUninstallResultHolder result;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // simulate an app holding the font
        {
            result = new(await _installer.UninstallForCurrentUserAsync(UserFamily(path)));
        }

        Assert.Equal(0, result.Value.RemovedFiles);
        Assert.Equal(["Georgia Regular.ttf"], result.Value.InUse);
        Assert.True(File.Exists(path));
        Assert.True(_registry.Entries.ContainsKey("Georgia (TrueType)"));
        Assert.Equal(path, _api.Added[^1]); // re-registered with Windows
    }

    [Fact]
    public async Task Uninstall_FileOutsideUserFontsFolder_IsUnregisteredButNotDeleted()
    {
        var elsewhere = Path.Combine(_root, "Elsewhere");
        Directory.CreateDirectory(elsewhere);
        var file = Path.Combine(elsewhere, "georgia.ttf");
        File.Copy(Path.Combine(Fonts, "georgia.ttf"), file);
        _registry.Entries["Georgia (TrueType)"] = file;

        var result = await _installer.UninstallForCurrentUserAsync(UserFamily(file));

        Assert.Equal(1, result.RemovedFiles);
        Assert.True(File.Exists(file));
        Assert.Empty(_registry.Entries);
    }

    private FontFamilyInfo VaultFamily(params string[] systemFiles)
    {
        var faces = systemFiles.Select(name =>
        {
            var copy = Path.Combine(_vault, Guid.NewGuid().ToString("N") + ".ttf");
            File.Copy(Path.Combine(Fonts, name), copy);
            File.SetAttributes(copy, FileAttributes.Normal);
            return new FontFaceInfo(name, FilePath: copy);
        }).ToList();
        return new FontFamilyInfo("Georgia", FontSource.Imported, faces);
    }

    private static FontFamilyInfo UserFamily(string path)
        => new("Georgia", FontSource.User, [new FontFaceInfo("Regular", FilePath: path)]);

    private sealed record FontUninstallResultHolder(FontBook.Core.Services.FontUninstallResult Value);

    private sealed class FakeRegistry : IUserFontRegistry
    {
        public Dictionary<string, string> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> GetEntries() => new Dictionary<string, string>(Entries, StringComparer.OrdinalIgnoreCase);

        public void SetEntry(string valueName, string fontFilePath) => Entries[valueName] = fontFilePath;

        public void RemoveEntry(string valueName) => Entries.Remove(valueName);
    }

    private sealed class FakeFontApi : IFontResourceApi
    {
        public bool RejectAdds { get; set; }

        public List<string> Added { get; } = [];

        public List<string> Removed { get; } = [];

        public int Broadcasts { get; private set; }

        public bool AddFontResource(string fontFilePath)
        {
            if (RejectAdds)
            {
                return false;
            }

            Added.Add(fontFilePath);
            return true;
        }

        public bool RemoveFontResource(string fontFilePath)
        {
            Removed.Add(fontFilePath);
            return true;
        }

        public void BroadcastFontChange() => Broadcasts++;
    }
}
