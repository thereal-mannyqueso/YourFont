namespace FontBook.Core.Services;

/// <summary>
/// Installs and uninstalls fonts through documented Windows mechanisms.
/// </summary>
/// <remarks>
/// Contract only — the implementation is planned for a later milestone. The intended
/// approach is per-user installation (supported since Windows 10 1809, no admin rights):
/// <list type="number">
///   <item>Copy the font file into <c>%LOCALAPPDATA%\Microsoft\Windows\Fonts</c>.</item>
///   <item>Register it under <c>HKCU\Software\Microsoft\Windows NT\CurrentVersion\Fonts</c>.</item>
///   <item>Call <c>AddFontResourceEx</c> and broadcast <c>WM_FONTCHANGE</c> so running apps notice.</item>
/// </list>
/// Uninstall reverses those steps and must only ever touch per-user fonts that the
/// user explicitly confirmed. System fonts in C:\Windows\Fonts are never modified.
/// </remarks>
public interface IFontInstallationService
{
    Task InstallForCurrentUserAsync(string fontFilePath, CancellationToken cancellationToken = default);

    Task UninstallForCurrentUserAsync(string installedFontFilePath, CancellationToken cancellationToken = default);
}
