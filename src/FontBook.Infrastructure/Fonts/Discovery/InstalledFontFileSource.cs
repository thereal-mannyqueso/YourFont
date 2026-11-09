using FontBook.Core.Models;
using Microsoft.Win32;

namespace FontBook.Infrastructure.Fonts.Discovery;

/// <summary>A font file that Windows lists as installed, and whether it is system-wide or per-user.</summary>
public sealed record InstalledFontFile(string Path, FontSource Source);

/// <summary>Lists the font files installed in Windows. Separated out so discovery can be tested without the registry.</summary>
public interface IInstalledFontFileSource
{
    IReadOnlyList<InstalledFontFile> GetInstalledFontFiles();
}

/// <summary>
/// Reads Windows' own list of installed fonts from the registry — the same list Windows
/// Settings uses. Read-only: keys are opened with <c>writable: false</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts</c> — installed for all users.
///         Values are usually bare file names inside C:\Windows\Fonts.</item>
///   <item><c>HKCU\...\Fonts</c> — installed only for the current user (Windows 10 1809+).
///         Values are full paths, normally in %LOCALAPPDATA%\Microsoft\Windows\Fonts.</item>
/// </list>
/// </remarks>
public sealed class WindowsRegistryFontFileSource : IInstalledFontFileSource
{
    private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    public IReadOnlyList<InstalledFontFile> GetInstalledFontFiles()
    {
        var systemFontsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<InstalledFontFile>();

        // System first, so a file listed in both places counts as a system font.
        Collect(Registry.LocalMachine, FontSource.System);
        Collect(Registry.CurrentUser, FontSource.User);
        return files;

        void Collect(RegistryKey hive, FontSource source)
        {
            using var key = hive.OpenSubKey(FontsKey, writable: false);
            if (key is null)
            {
                return;
            }

            foreach (var valueName in key.GetValueNames())
            {
                if (key.GetValue(valueName) is not string raw || string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
                var fullPath = Path.IsPathRooted(expanded) ? expanded : Path.Combine(systemFontsFolder, expanded);
                if (seen.Add(fullPath))
                {
                    files.Add(new InstalledFontFile(fullPath, source));
                }
            }
        }
    }
}
