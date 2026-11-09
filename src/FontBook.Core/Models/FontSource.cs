namespace FontBook.Core.Models;

/// <summary>
/// Where a font comes from. This is the key distinction that keeps the
/// Font Vault (app-managed copies) separate from fonts installed in Windows.
/// </summary>
public enum FontSource
{
    /// <summary>Installed for all users in C:\Windows\Fonts.</summary>
    System,

    /// <summary>Installed for the current user only (%LOCALAPPDATA%\Microsoft\Windows\Fonts).</summary>
    User,

    /// <summary>A copy imported into FontBook's Font Vault. Not installed in Windows.</summary>
    Imported,
}
