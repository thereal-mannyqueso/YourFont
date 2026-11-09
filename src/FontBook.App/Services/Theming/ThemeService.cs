using System.Windows;
using FontBook.Core.Models;
using Microsoft.Win32;

namespace FontBook.App.Services.Theming;

public interface IThemeService
{
    AppTheme Theme { get; }

    /// <summary>True when the dark palette is currently applied (directly or via System).</summary>
    bool IsDark { get; }

    event EventHandler? EffectiveThemeChanged;

    void Apply(AppTheme theme);
}

/// <summary>
/// Swaps the color palette dictionary (Themes/Light.xaml or Themes/Dark.xaml) in the
/// application resources. Because all styles use DynamicResource, every open window
/// repaints immediately. In System mode it follows the Windows "app mode" setting live.
/// </summary>
public sealed class ThemeService : IThemeService, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly Uri LightUri = new("pack://application:,,,/FontBook;component/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/FontBook;component/Themes/Dark.xaml");

    private ResourceDictionary? _activePalette;

    public ThemeService()
    {
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public AppTheme Theme { get; private set; } = AppTheme.System;

    public bool IsDark { get; private set; }

    public event EventHandler? EffectiveThemeChanged;

    public void Apply(AppTheme theme)
    {
        Theme = theme;
        UpdatePalette(force: _activePalette is null);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (Theme == AppTheme.System && e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => UpdatePalette(force: false));
        }
    }

    private void UpdatePalette(bool force)
    {
        var dark = Theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => !SystemUsesLightTheme(),
        };

        if (!force && dark == IsDark)
        {
            return;
        }

        IsDark = dark;
        var palette = new ResourceDictionary { Source = dark ? DarkUri : LightUri };
        var merged = Application.Current.Resources.MergedDictionaries;

        // The palette is always the first merged dictionary (see App.xaml).
        var index = _activePalette is null ? 0 : merged.IndexOf(_activePalette);
        if (index >= 0 && index < merged.Count && IsPalette(merged[index]))
        {
            merged[index] = palette;
        }
        else
        {
            merged.Insert(0, palette);
        }

        _activePalette = palette;
        EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsPalette(ResourceDictionary dictionary)
        => dictionary.Source is { } source && (source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)
                                               || source.OriginalString.EndsWith("Dark.xaml", StringComparison.OrdinalIgnoreCase));

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
        }
        catch (System.Security.SecurityException)
        {
            return true;
        }
    }
}
