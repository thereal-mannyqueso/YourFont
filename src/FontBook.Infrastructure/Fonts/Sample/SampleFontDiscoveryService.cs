using FontBook.Core.Models;
using FontBook.Core.Services;

namespace FontBook.Infrastructure.Fonts.Sample;

/// <summary>
/// TEMPORARY placeholder data for Milestone 1. This does NOT scan the PC.
/// </summary>
/// <remarks>
/// The list names families that ship with Windows 11, so the previews render with
/// real typefaces. The System/User split is made up purely to
/// exercise the UI (for example, Bahnschrift is really a system font).
/// Every family is flagged <see cref="FontFamilyInfo.IsSample"/> and the window shows a
/// "Sample data" badge. Milestone 2 replaces this class with real discovery.
/// </remarks>
public sealed class SampleFontDiscoveryService : IFontDiscoveryService
{
    public bool IsSampleData => true;

    public Task<IReadOnlyList<FontFamilyInfo>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        var families = new List<FontFamilyInfo>
        {
            Sys("Segoe UI",
                F("Light", 300), F("Light Italic", 300, true),
                F("Semilight", 350), F("Semilight Italic", 350, true),
                F("Regular"), F("Italic", 400, true),
                F("Semibold", 600), F("Semibold Italic", 600, true),
                F("Bold", 700), F("Bold Italic", 700, true),
                F("Black", 900), F("Black Italic", 900, true)),
            Sys("Segoe UI Variable Display", F("Light", 300), F("Regular"), F("Semibold", 600), F("Bold", 700)),
            Sys("Segoe UI Variable Text", F("Light", 300), F("Regular"), F("Semibold", 600), F("Bold", 700)),
            Sys("Arial", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true), F("Black", 900)),
            Sys("Calibri", F("Light", 300), F("Light Italic", 300, true), F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Cambria", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Candara", F("Light", 300), F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Cascadia Code", F("Light", 300), F("Regular"), F("Semibold", 600), F("Bold", 700)),
            Sys("Consolas", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Constantia", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Corbel", F("Light", 300), F("Regular"), F("Italic", 400, true), F("Bold", 700)),
            Sys("Courier New", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Franklin Gothic Medium", F("Regular"), F("Italic", 400, true)),
            Sys("Gabriola", F("Regular")),
            Sys("Georgia", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Impact", F("Regular")),
            Sys("Lucida Console", F("Regular")),
            Sys("Palatino Linotype", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Segoe Print", F("Regular"), F("Bold", 700)),
            Sys("Segoe Script", F("Regular"), F("Bold", 700)),
            Sys("Sitka Text", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Sylfaen", F("Regular")),
            Sys("Tahoma", F("Regular"), F("Bold", 700)),
            Sys("Times New Roman", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Trebuchet MS", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),
            Sys("Verdana", F("Regular"), F("Italic", 400, true), F("Bold", 700), F("Bold Italic", 700, true)),

            // Marked as "User" only so the User Fonts section has something to show.
            Usr("Bahnschrift",
                F("Light", 300), F("SemiLight", 350), F("Regular"), F("SemiBold", 600), F("Bold", 700),
                F("Condensed", 400, stretch: 3), F("SemiBold Condensed", 600, stretch: 3)),
            Usr("Ink Free", F("Regular")),
        };

        return Task.FromResult<IReadOnlyList<FontFamilyInfo>>(families);
    }

    private static FontFaceInfo F(string style, int weight = 400, bool italic = false, int stretch = 5)
        => new(style, weight, italic ? FontSlant.Italic : FontSlant.Normal, stretch);

    private static FontFamilyInfo Sys(string name, params FontFaceInfo[] faces)
        => new(name, FontSource.System, faces) { IsSample = true };

    private static FontFamilyInfo Usr(string name, params FontFaceInfo[] faces)
        => new(name, FontSource.User, faces) { IsSample = true };
}
