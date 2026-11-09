using FontBook.Core.Models;

namespace FontBook.Core.Services;

/// <summary>
/// Installs and uninstalls fonts for the current Windows user, through documented mechanisms.
/// </summary>
/// <remarks>
/// Per-user installation (Windows 10 1809+, no admin rights), the same thing Windows does:
/// <list type="number">
///   <item>Copy the font file into <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>.</item>
///   <item>Register it under <c>HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts</c>.</item>
///   <item>Call <c>AddFontResource</c> and broadcast <c>WM_FONTCHANGE</c> so running apps notice.</item>
/// </list>
/// Uninstall reverses those steps for per-user fonts only. System fonts in C:\Windows\Fonts are
/// never modified, and files outside the per-user fonts folder are never deleted.
/// </remarks>
public interface IFontInstallationService
{
    /// <summary>Installs every file of an imported (Font Vault) family for the current user.</summary>
    /// <exception cref="InvalidOperationException">The family is not a Font Vault family.</exception>
    Task<FontInstallResult> InstallForCurrentUserAsync(FontFamilyInfo family, CancellationToken cancellationToken = default);

    /// <summary>Uninstalls a family that is installed for the current user (a User font).</summary>
    /// <exception cref="InvalidOperationException">The family is not a per-user font.</exception>
    Task<FontUninstallResult> UninstallForCurrentUserAsync(FontFamilyInfo family, CancellationToken cancellationToken = default);
}

/// <param name="InstalledFiles">Files newly installed.</param>
/// <param name="Problems">Files that couldn't be installed, with a reason.</param>
public sealed record FontInstallResult(int InstalledFiles, IReadOnlyList<string> Problems);

/// <param name="RemovedFiles">Files uninstalled.</param>
/// <param name="InUse">Files left installed because an app is using them.</param>
public sealed record FontUninstallResult(int RemovedFiles, IReadOnlyList<string> InUse);
