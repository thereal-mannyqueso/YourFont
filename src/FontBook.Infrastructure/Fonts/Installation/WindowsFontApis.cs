using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace FontBook.Infrastructure.Fonts.Installation;

/// <summary>The current user's font registrations (HKCU\...\Fonts): value name → font file path.</summary>
public interface IUserFontRegistry
{
    IReadOnlyDictionary<string, string> GetEntries();

    void SetEntry(string valueName, string fontFilePath);

    void RemoveEntry(string valueName);
}

/// <summary>Tells Windows about font files being added or removed (GDI font table + broadcast).</summary>
public interface IFontResourceApi
{
    /// <summary>Makes the font available to apps in this session. Returns false if Windows rejected the file.</summary>
    bool AddFontResource(string fontFilePath);

    bool RemoveFontResource(string fontFilePath);

    /// <summary>Notifies running apps that the set of fonts changed (WM_FONTCHANGE).</summary>
    void BroadcastFontChange();
}

public sealed class WindowsUserFontRegistry : IUserFontRegistry
{
    private const string FontsKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    public IReadOnlyDictionary<string, string> GetEntries()
    {
        using var key = Registry.CurrentUser.OpenSubKey(FontsKey, writable: false);
        var entries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (key is null)
        {
            return entries;
        }

        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string path && path.Length > 0)
            {
                entries[name] = Environment.ExpandEnvironmentVariables(path);
            }
        }

        return entries;
    }

    public void SetEntry(string valueName, string fontFilePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(FontsKey, writable: true);
        key.SetValue(valueName, fontFilePath, RegistryValueKind.String);
    }

    public void RemoveEntry(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(FontsKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public sealed partial class WindowsFontResourceApi : IFontResourceApi
{
    private static readonly nint HwndBroadcast = 0xFFFF;
    private const uint WmFontChange = 0x001D;
    private const uint SmtoAbortIfHung = 0x0002;

    public bool AddFontResource(string fontFilePath) => AddFontResourceW(fontFilePath) > 0;

    public bool RemoveFontResource(string fontFilePath) => RemoveFontResourceW(fontFilePath);

    public void BroadcastFontChange()
        // A timeout, so one frozen app can't hang FontBook.
        => _ = SendMessageTimeoutW(HwndBroadcast, WmFontChange, 0, 0, SmtoAbortIfHung, 1000, out _);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int AddFontResourceW(string fileName);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveFontResourceW(string fileName);

    [LibraryImport("user32.dll")]
    private static partial nint SendMessageTimeoutW(nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
}
