using System.Security.Cryptography;
using FontBook.Core.Models;
using FontBook.Infrastructure.Fonts.Discovery;
using FontBook.Infrastructure.Fonts.Files;
using Microsoft.Extensions.Logging.Abstractions;

namespace FontBook.Tests;

/// <summary>
/// These tests read fonts that ship with every Windows 10/11 installation (Arial, Georgia,
/// Cambria). They only read the files; one test proves the files are left untouched.
/// </summary>
public sealed class FontFileReaderTests
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    [Fact]
    public void ReadsRegularFace()
    {
        var face = Assert.Single(FontFileReader.ReadFaces(Path.Combine(Fonts, "arial.ttf")));

        Assert.Equal("Arial", face.FamilyName);
        Assert.Equal("Regular", face.StyleName);
        Assert.Equal(400, face.Weight);
        Assert.Equal(5, face.Width);
        Assert.False(face.IsItalic);
    }

    [Fact]
    public void ReadsBoldItalicFace()
    {
        var face = Assert.Single(FontFileReader.ReadFaces(Path.Combine(Fonts, "georgiaz.ttf")));

        Assert.Equal("Georgia", face.FamilyName);
        Assert.Equal("Bold Italic", face.StyleName);
        Assert.Equal(700, face.Weight);
        Assert.True(face.IsItalic);
    }

    [Fact]
    public void ReadsEveryFaceInACollection()
    {
        var faces = FontFileReader.ReadFaces(Path.Combine(Fonts, "cambria.ttc"));

        Assert.Contains(faces, f => f.FamilyName == "Cambria");
        Assert.Contains(faces, f => f.FamilyName == "Cambria Math");
    }

    [Fact]
    public void RenderName_IsTheFamilyNameWpfMatches()
    {
        // Segoe UI Semibold's file calls itself "Segoe UI Semibold" (legacy name), but WPF matches "Segoe UI".
        var face = Assert.Single(FontFileReader.ReadFaces(Path.Combine(Fonts, "seguisb.ttf")));

        Assert.Equal("Segoe UI", face.RenderFamilyName);
        Assert.Equal(600, face.Weight);
    }

    /// <summary>
    /// Guards against silent fallback: for every installed single-face font file, the name we pass to
    /// WPF must be one WPF recognizes for that file, otherwise WPF would quietly draw Segoe UI instead.
    /// </summary>
    [Fact]
    public void RenderName_MatchesWpf_ForEveryInstalledFont()
    {
        var mismatches = new List<string>();
        foreach (var file in new WindowsRegistryFontFileSource().GetInstalledFontFiles())
        {
            if (!FontFileReader.IsSupported(file.Path) || !File.Exists(file.Path)
                || Path.GetExtension(file.Path).Equals(".ttc", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            FontFaceData ours;
            System.Windows.Media.GlyphTypeface wpf;
            try
            {
                ours = Assert.Single(FontFileReader.ReadFaces(file.Path));
                wpf = new System.Windows.Media.GlyphTypeface(new Uri(file.Path));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or FileFormatException or NotSupportedException)
            {
                continue; // unreadable files are skipped by discovery too
            }

            // WPF trims family names when matching (e.g. a font that stores "Allenoire " with a trailing space).
            if (!wpf.FamilyNames.Values.Select(n => n.Trim()).Contains(ours.RenderFamilyName, StringComparer.OrdinalIgnoreCase))
            {
                mismatches.Add($"{Path.GetFileName(file.Path)}: ours '{ours.RenderFamilyName}', WPF '{string.Join(" | ", wpf.FamilyNames.Values)}'");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void RejectsFilesThatAreNotFonts()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "definitely not a font");
            Assert.Throws<InvalidDataException>(() => FontFileReader.ReadFaces(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadingNeverModifiesTheFile()
    {
        var path = Path.Combine(Fonts, "arial.ttf");
        var before = (Hash: SHA256.HashData(File.ReadAllBytes(path)), Written: File.GetLastWriteTimeUtc(path));

        FontFileReader.ReadFaces(path);

        Assert.Equal(before.Hash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(before.Written, File.GetLastWriteTimeUtc(path));
    }
}

public sealed class WindowsFontDiscoveryTests
{
    private static readonly string Fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);

    [Fact]
    public async Task GroupsFilesIntoFamilies_AndKeepsSystemAndUserApart()
    {
        var source = new FakeSource(
            new InstalledFontFile(Path.Combine(Fonts, "georgia.ttf"), FontSource.System),
            new InstalledFontFile(Path.Combine(Fonts, "georgiab.ttf"), FontSource.System),
            new InstalledFontFile(Path.Combine(Fonts, "georgiaz.ttf"), FontSource.System),
            new InstalledFontFile(Path.Combine(Fonts, "arial.ttf"), FontSource.User));
        var discovery = new WindowsFontDiscoveryService(source, NullLogger<WindowsFontDiscoveryService>.Instance);

        var families = await discovery.DiscoverAsync();

        var georgia = Assert.Single(families, f => f.FamilyName == "Georgia");
        Assert.Equal(FontSource.System, georgia.Source);
        Assert.Equal(["Regular", "Bold", "Bold Italic"], georgia.Faces.Select(f => f.StyleName));
        Assert.All(georgia.Faces, f => Assert.NotNull(f.FilePath));

        var arial = Assert.Single(families, f => f.FamilyName == "Arial");
        Assert.Equal(FontSource.User, arial.Source);
        Assert.False(discovery.IsSampleData);
    }

    [Fact]
    public async Task SkipsUnsupportedMissingAndBrokenFiles_WithoutFailing()
    {
        var broken = Path.GetTempFileName();
        File.WriteAllText(broken, "not a font");
        var brokenTtf = Path.ChangeExtension(broken, ".ttf");
        File.Move(broken, brokenTtf);
        try
        {
            var source = new FakeSource(
                new InstalledFontFile(Path.Combine(Fonts, "arial.ttf"), FontSource.System),
                new InstalledFontFile(Path.Combine(Fonts, "does-not-exist.ttf"), FontSource.System),
                new InstalledFontFile(Path.Combine(Fonts, "legacy.fon"), FontSource.System),
                new InstalledFontFile(brokenTtf, FontSource.User));
            var discovery = new WindowsFontDiscoveryService(source, NullLogger<WindowsFontDiscoveryService>.Instance);

            var family = Assert.Single(await discovery.DiscoverAsync());

            Assert.Equal("Arial", family.FamilyName);
        }
        finally
        {
            File.Delete(brokenTtf);
        }
    }

    [Fact]
    public void RegistrySource_FindsSystemFonts()
    {
        var files = new WindowsRegistryFontFileSource().GetInstalledFontFiles();

        Assert.Contains(files, f => f.Source == FontSource.System
                                    && string.Equals(Path.GetFileName(f.Path), "arial.ttf", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class FakeSource(params InstalledFontFile[] files) : IInstalledFontFileSource
    {
        public IReadOnlyList<InstalledFontFile> GetInstalledFontFiles() => files;
    }
}
