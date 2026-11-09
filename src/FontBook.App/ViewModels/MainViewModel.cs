using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontBook.App.Services.Dialogs;
using FontBook.App.Services.Rendering;
using FontBook.App.Services.Settings;
using FontBook.App.Services.Theming;
using FontBook.Core.Data;
using FontBook.Core.Library;
using FontBook.Core.Models;
using Microsoft.Extensions.Logging;

namespace FontBook.App.ViewModels;

/// <summary>
/// The main window's view model. It connects the sidebar, the font list and the preview,
/// and owns app-wide preferences (theme, pane widths).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const double DefaultSidebarWidth = 240;
    public const double DefaultFontListWidth = 300;

    private readonly IFontLibraryService _library;
    private readonly ISettingsStore _settings;
    private readonly DeferredSettingsWriter _settingsWriter;
    private readonly IThemeService _themes;
    private readonly IFontPreviewProvider _fonts;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dictionary<string, FontFamilyViewModel> _familyViewModels = new(StringComparer.Ordinal);

    private bool _isRefreshingList;
    private bool _isInitialized;

    public MainViewModel(
        IFontLibraryService library,
        SidebarViewModel sidebar,
        PreviewViewModel preview,
        ISettingsStore settings,
        DeferredSettingsWriter settingsWriter,
        IThemeService themes,
        IFontPreviewProvider fonts,
        IDialogService dialogs,
        ILogger<MainViewModel> logger)
    {
        _library = library;
        _settings = settings;
        _settingsWriter = settingsWriter;
        _themes = themes;
        _fonts = fonts;
        _dialogs = dialogs;
        _logger = logger;
        Sidebar = sidebar;
        Preview = preview;

        Sidebar.SelectionChanged += (_, _) => RefreshList();
        Sidebar.CollectionsChanged += (_, _) => OnCollectionsChanged();
    }

    public SidebarViewModel Sidebar { get; }

    public PreviewViewModel Preview { get; }

    /// <summary>True while the font list shows temporary sample data instead of real fonts.</summary>
    public bool IsSampleData => _library.IsSampleData;

    /// <summary>Checkboxes for the preview's "Collections" menu, for the selected family.</summary>
    public ObservableCollection<CollectionMembershipViewModel> Memberships { get; } = [];

    public bool HasCollections => Sidebar.Collections.Count > 0;

    public bool IsCollectionSelected => Sidebar.SelectedItem.IsCollection;

    [ObservableProperty]
    private IReadOnlyList<FontFamilyViewModel> _visibleFamilies = [];

    [ObservableProperty]
    private FontFamilyViewModel? _selectedFamily;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _sectionTitle = string.Empty;

    [ObservableProperty]
    private string _sectionSubtitle = string.Empty;

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private string _emptyGlyph = string.Empty;

    [ObservableProperty]
    private string _emptyTitle = string.Empty;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    private AppTheme _theme = AppTheme.System;

    [ObservableProperty]
    private double _sidebarWidth = DefaultSidebarWidth;

    [ObservableProperty]
    private double _fontListWidth = DefaultFontListWidth;

    /// <summary>Loads settings, fonts, favorites and collections. Called once when the window opens.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await LoadSettingsAsync();
            await Preview.LoadSettingsAsync();
            await _library.LoadAsync();
            await Sidebar.LoadAsync();

            _familyViewModels.Clear();
            foreach (var family in _library.Families)
            {
                _familyViewModels[family.Key] = new FontFamilyViewModel(family, _fonts, _library.IsFavorite(family.Key));
            }

            _isInitialized = true;
            OnPropertyChanged(nameof(HasCollections));
            Sidebar.UpdateCounts();
            RefreshList();
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or IOException or InvalidOperationException)
        {
            _logger.LogError(ex, "Loading the library failed");
            _dialogs.ShowError("FontBook couldn't load your library", ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Writes any setting changes that are still waiting in the debounce buffer.</summary>
    public Task FlushPendingSavesAsync() => _settingsWriter.FlushAsync();

    partial void OnSearchTextChanged(string value) => RefreshList();

    partial void OnSelectedFamilyChanged(FontFamilyViewModel? value)
    {
        // While the list is being rebuilt the ListBox briefly reports "no selection";
        // ignore that so the preview doesn't flicker on every keystroke.
        if (!_isRefreshingList)
        {
            ShowInPreview(value);
        }
    }

    partial void OnThemeChanged(AppTheme value)
    {
        _themes.Apply(value);
        if (_isInitialized)
        {
            _settingsWriter.Schedule(SettingKeys.Theme, value.ToString());
        }
    }

    partial void OnSidebarWidthChanged(double value)
    {
        if (_isInitialized)
        {
            _settingsWriter.Schedule(SettingKeys.SidebarWidth, Math.Round(value).ToString(CultureInfo.InvariantCulture));
        }
    }

    partial void OnFontListWidthChanged(double value)
    {
        if (_isInitialized)
        {
            _settingsWriter.Schedule(SettingKeys.FontListWidth, Math.Round(value).ToString(CultureInfo.InvariantCulture));
        }
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    [RelayCommand]
    private async Task ToggleFavoriteAsync(FontFamilyViewModel? family)
    {
        if (family is null)
        {
            return;
        }

        var makeFavorite = !family.IsFavorite;
        try
        {
            await _library.SetFavoriteAsync(family.Key, makeFavorite);
            family.IsFavorite = makeFavorite;
            Sidebar.UpdateCounts();
            if (Sidebar.SelectedItem.Scope.Kind == LibraryScopeKind.Favorites)
            {
                RefreshList();
            }
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or IOException)
        {
            _logger.LogError(ex, "Updating favorite {Key} failed", family.Key);
            _dialogs.ShowError("Couldn't update favorites", ex.Message);
        }
    }

    [RelayCommand]
    private async Task RemoveFromCurrentCollectionAsync(FontFamilyViewModel? family)
    {
        if (family is null || Sidebar.SelectedItem.CollectionId is not { } collectionId)
        {
            return;
        }

        await SetMembershipAsync(collectionId, family, isMember: false);
    }

    private async Task OnMembershipToggledAsync(CollectionMembershipViewModel membership, bool isMember)
    {
        if (SelectedFamily is { } family)
        {
            await SetMembershipAsync(membership.CollectionId, family, isMember);
        }
    }

    private async Task SetMembershipAsync(long collectionId, FontFamilyViewModel family, bool isMember)
    {
        try
        {
            await _library.SetCollectionMembershipAsync(collectionId, family.Key, isMember);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or IOException)
        {
            _logger.LogError(ex, "Updating collection {Id} failed", collectionId);
            _dialogs.ShowError("Couldn't update collection", ex.Message);
        }

        Sidebar.UpdateCounts();
        var scope = Sidebar.SelectedItem.Scope;
        if (scope.Kind is LibraryScopeKind.Collection or LibraryScopeKind.AnyCollection)
        {
            RefreshList();
        }
        else
        {
            RebuildMemberships();
        }
    }

    private void OnCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasCollections));
        Sidebar.UpdateCounts();
        RefreshList();
    }

    private void RefreshList()
    {
        if (!_isInitialized)
        {
            return;
        }

        var navigation = Sidebar.SelectedItem;
        var scope = navigation.Scope;
        var results = _library.Query(scope, SearchText)
            .Select(f => _familyViewModels[f.Key])
            .ToList();

        var previous = SelectedFamily;
        _isRefreshingList = true;
        try
        {
            VisibleFamilies = results;
            SelectedFamily = previous is not null && results.Contains(previous) ? previous : results.FirstOrDefault();
        }
        finally
        {
            _isRefreshingList = false;
        }

        ShowInPreview(SelectedFamily);

        SectionTitle = navigation.Title;
        var total = _library.Count(scope);
        SectionSubtitle = string.IsNullOrWhiteSpace(SearchText) || total == 0
            ? FamilyCount(total)
            : $"{results.Count} of {FamilyCount(total)}";
        OnPropertyChanged(nameof(IsCollectionSelected));
        UpdateEmptyState(scope.Kind, results.Count, total);
    }

    private void ShowInPreview(FontFamilyViewModel? family)
    {
        if (!ReferenceEquals(Preview.Family, family))
        {
            Preview.Family = family;
        }

        RebuildMemberships();
    }

    private void RebuildMemberships()
    {
        Memberships.Clear();
        if (SelectedFamily is not { } family)
        {
            return;
        }

        var containing = _library.GetCollectionsContaining(family.Key);
        foreach (var item in Sidebar.Collections)
        {
            if (item.CollectionId is { } id)
            {
                Memberships.Add(new CollectionMembershipViewModel(id, item.Title, containing.Contains(id), OnMembershipToggledAsync));
            }
        }
    }

    private void UpdateEmptyState(LibraryScopeKind kind, int visibleCount, int totalCount)
    {
        IsEmpty = visibleCount == 0;
        if (!IsEmpty)
        {
            return;
        }

        if (totalCount > 0)
        {
            (EmptyGlyph, EmptyTitle, EmptyMessage) = (Glyphs.Search, "No matching fonts",
                $"Nothing here matches “{SearchText.Trim()}”. Try a family name or a style such as “Bold”.");
            return;
        }

        (EmptyGlyph, EmptyTitle, EmptyMessage) = kind switch
        {
            LibraryScopeKind.Favorites => (Glyphs.Star, "No favorites yet",
                "Select the star next to a font to keep it here."),
            LibraryScopeKind.UserFonts => (Glyphs.Person, "No user fonts",
                "Fonts installed only for your Windows account will appear here."),
            LibraryScopeKind.ImportedFonts => (Glyphs.Import, "Your Font Vault is empty",
                "Importing font files into the Vault (to preview them without installing) arrives in the next milestone."),
            LibraryScopeKind.RecentlyAdded => (Glyphs.Recent, "Nothing added recently",
                "Fonts you import into the Font Vault in the last 30 days will appear here."),
            LibraryScopeKind.AnyCollection => (Glyphs.Library, "No fonts in collections",
                "Create a collection with the + button, then add fonts to it from the preview."),
            LibraryScopeKind.Collection => (Glyphs.Folder, "This collection is empty",
                "Select a font, then use the Collections button in the preview to add it here."),
            _ => (Glyphs.Font, "No fonts found", "FontBook didn't find any fonts for this section."),
        };
    }

    private async Task LoadSettingsAsync()
    {
        var theme = await _settings.GetAsync(SettingKeys.Theme);
        Theme = Enum.TryParse<AppTheme>(theme, out var parsed) ? parsed : AppTheme.System;
        _themes.Apply(Theme);

        SidebarWidth = await ReadDoubleAsync(SettingKeys.SidebarWidth, DefaultSidebarWidth, 180, 360);
        FontListWidth = await ReadDoubleAsync(SettingKeys.FontListWidth, DefaultFontListWidth, 220, 520);
    }

    private async Task<double> ReadDoubleAsync(string key, double fallback, double min, double max)
    {
        var raw = await _settings.GetAsync(key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Math.Clamp(value, min, max)
            : fallback;
    }

    private static string FamilyCount(int count) => count == 1 ? "1 family" : $"{count} families";
}
