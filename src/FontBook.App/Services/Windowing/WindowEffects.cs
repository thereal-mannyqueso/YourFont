using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace FontBook.App.Services.Windowing;

/// <summary>
/// Windows 11 window effects through the documented DWM API (DwmSetWindowAttribute):
/// the Mica backdrop material and a dark/light title bar.
/// </summary>
internal static partial class WindowEffects
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtMainWindow = 2; // Mica

    /// <summary>The system backdrop attribute exists from Windows 11 22H2 (build 22621).</summary>
    public static bool IsMicaSupported => Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Turns on Mica. Returns false (and changes nothing) when the OS does not support it,
    /// so the caller can keep a solid background instead.
    /// </summary>
    public static bool TryEnableMica(Window window)
    {
        if (!IsMicaSupported)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var value = DwmsbtMainWindow;
        if (DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref value, sizeof(int)) != 0)
        {
            return false;
        }

        // WPF paints an opaque background by default; make it transparent so Mica shows through.
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } target })
        {
            target.BackgroundColor = Colors.Transparent;
        }

        return true;
    }

    /// <summary>Makes the title bar and caption buttons match the app theme.</summary>
    public static void SetDarkTitleBar(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        var value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
