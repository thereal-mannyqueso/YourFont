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
using FontBook.Core.Services;
using FontBook.Core.Vault;
using Microsoft.Extensions.Logging;

namespace FontBook.App.ViewModels;

/// <summary>
/// The main window's view model. It connects the sidebar, the font list and the preview,
/// and owns app-wide preferences (theme, pane widths).
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const double DefaultSidebarWidth = 240;
    public const double DefaultFontListWidth = 300;

    private readonly IFontLibraryService _library;
    private readonly ISettingsStore _settings;
    private readonly DeferredSettingsWriter _settingsWriter;
    private readonly IThemeService _themes;
    private readonly IFontPreviewProvider _fonts;
    private readonly IDialogService _dialogs;
    private readonly IFontVault _vault;
    private readonly IFilePickerService _filePicker;
    private readonly IFontInstallationService _installer;
    private readonly ILogger<MainViewModel> _logger;
    private CancellationTokenSource? _notificationTimer;
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
        IFontVault vault,
        IFilePickerService filePicker,
        IFontInstallationService installer,
        ILogger<MainViewModel> logger)
    {
        _vault = vault;
        _filePicker = filePicker;
        _installer = installer;
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

    /// <summary>Category filter chips above the font list.</summary>
    public ObservableCollection<CategoryChoiceViewModel> CategoryChips { get; } = [];

    /// <summary>Choices in the preview's category picker for the selected family.</summary>
    public ObservableCollection<CategoryChoiceViewModel> CategoryOptions { get; } = [];

    /// <summary>The category the font list is filtered to; null shows all.</summary>
    private FontCategory? _categoryFilter;

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

    /// <summary>True when the empty state should offer an "Import Fonts" button.</summary>
    [ObservableProperty]
    private bool _showImportAction;

    [ObservableProperty]
    private bool _isImporting;

    /// <summary>Text of the "working…" indicator (importing, installing, uninstalling). Null hides it.</summary>
    [ObservableProperty]
    private string? _busyText;

    /// <summary>Short message shown in the notification bar (e.g. import results). Null hides the bar.</summary>
    [ObservableProperty]
    private string? _notification;

    [ObservableProperty]
    private bool _notificationIsWarning;

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
            await _vault.CleanUpAsync(); // finish deleting Vault copies that were in use last time
            await _library.LoadAsync();
            await Sidebar.LoadAsync();
            SyncFamilyViewModels();

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

    public void Dispose()
    {
        _notificationTimer?.Cancel();
        _notificationTimer?.Dispose();
        _notificationTimer = null;
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

    /// <summary>Opens the file picker and imports the chosen fonts into the Font Vault.</summary>
    [RelayCommand]
    private async Task ImportFontsAsync()
    {
        var files = _filePicker.PickFontFiles();
        if (files.Count > 0)
        {
            await ImportPathsAsync(files);
        }
    }

    /// <summary>Imports files and/or folders (from the picker or drag and drop) into the Font Vault.</summary>
    [RelayCommand]
    public async Task ImportPathsAsync(IReadOnlyList<string>? paths)
    {
        if (paths is not { Count: > 0 } || IsImporting)
        {
            return;
        }

        IsImporting = true;
        BusyText = "Importing fonts into the Font Vault…";
        try
        {
            var result = await _vault.ImportAsync(paths);
            if (result.Imported.Count > 0)
            {
                await _library.RefreshVaultAsync();
                SyncFamilyViewModels();
                SearchText = string.Empty;
                Sidebar.Select(Sidebar.ImportedItem);
                Sidebar.UpdateCounts();
                RefreshList();

                // Show the first newly imported family.
                var first = VisibleFamilies.FirstOrDefault(f => result.ImportedFamilyNames.Contains(f.FamilyName, StringComparer.OrdinalIgnoreCase));
                if (first is not null)
                {
                    SelectedFamily = first;
                }
            }

            ShowNotification(DescribeImport(result), isWarning: result.Failed.Count > 0 && result.Imported.Count == 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            _logger.LogError(ex, "Import failed");
            _dialogs.ShowError("Couldn't import fonts", ex.Message);
        }
        finally
        {
            IsImporting = false;
            BusyText = null;
        }
    }

    /// <summary>Installs a Font Vault family for the current Windows user.</summary>
    [RelayCommand]
    private async Task InstallAsync(FontFamilyViewModel? family)
    {
        if (family is not { CanInstall: true } || BusyText is not null)
        {
            return;
        }

        BusyText = $"Installing \"{family.FamilyName}\"…";
        try
        {
            var result = await _installer.InstallForCurrentUserAsync(family.Model);
            await ReloadLibraryAsync();
            if (result.InstalledFiles > 0)
            {
                ShowNotification(
                    $"Installed \"{family.FamilyName}\" for your account. It's now available in your apps (some apps need a restart)."
                    + (result.Problems.Count > 0 ? $" {Plural(result.Problems.Count, "file")} couldn't be installed: {result.Problems[0]}" : string.Empty),
                    isWarning: result.Problems.Count > 0);
            }
            else
            {
                ShowNotification($"\"{family.FamilyName}\" couldn't be installed. {string.Join(" ", result.Problems)}", isWarning: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException)
        {
            _logger.LogError(ex, "Installing {Family} failed", family.FamilyName);
            _dialogs.ShowError("Couldn't install font", ex.Message);
        }
        finally
        {
            BusyText = null;
        }
    }

    /// <summary>Uninstalls a per-user font, after confirmation, optionally keeping a copy in the Font Vault.</summary>
    [RelayCommand]
    private async Task UninstallAsync(FontFamilyViewModel? family)
    {
        if (family is not { IsUserFont: true } || BusyText is not null)
        {
            return; // system fonts can never be uninstalled from FontBook
        }

        var choice = _dialogs.Choose(
            "Uninstall font",
            $"Uninstall \"{family.FamilyName}\" from Windows?\n\n" +
            "It will no longer be available in your apps. Apps that are open may need a restart.\n\n" +
            "Yes: uninstall and keep a copy in the Font Vault (recommended)\n" +
            "No: uninstall without keeping a copy\n" +
            "Cancel: keep the font installed");
        if (choice == DialogChoice.Cancel)
        {
            return;
        }

        BusyText = $"Uninstalling \"{family.FamilyName}\"…";
        try
        {
            if (choice == DialogChoice.Yes)
            {
                var files = family.Model.Faces.Select(f => f.FilePath).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var saved = await _vault.ImportAsync(files);
                if (saved.Imported.Count + saved.Duplicates.Count < files.Count)
                {
                    _dialogs.ShowError("Font not uninstalled",
                        "FontBook couldn't save a copy in the Font Vault, so nothing was uninstalled."
                        + (saved.Failed.Count > 0 ? $"\n\n{saved.Failed[0].FileName}: {saved.Failed[0].Reason}" : string.Empty));
                    return;
                }
            }

            var result = await _installer.UninstallForCurrentUserAsync(family.Model);
            await ReloadLibraryAsync();

            var message = result.InUse.Count == 0
                ? $"Uninstalled \"{family.FamilyName}\"." + (choice == DialogChoice.Yes ? " A copy is kept in the Font Vault." : string.Empty)
                : $"\"{family.FamilyName}\" is in use by an app, so {Plural(result.InUse.Count, "file")} stayed installed. Close apps using it and try again.";
            ShowNotification(message, isWarning: result.InUse.Count > 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.SecurityException or System.Data.Common.DbException)
        {
            _logger.LogError(ex, "Uninstalling {Family} failed", family.FamilyName);
            _dialogs.ShowError("Couldn't uninstall font", ex.Message);
        }
        finally
        {
            BusyText = null;
        }
    }

    /// <summary>Re-discovers installed fonts and the Vault after Windows' font list changed.</summary>
    private async Task ReloadLibraryAsync()
    {
        await _library.LoadAsync();
        SyncFamilyViewModels();
        Sidebar.UpdateCounts();
        RefreshList();
    }

    /// <summary>Deletes FontBook's Vault copy of an imported family, after confirmation.</summary>
    [RelayCommand]
    private async Task RemoveFromVaultAsync(FontFamilyViewModel? family)
    {
        if (family is not { IsImported: true })
        {
            return; // installed fonts can never be removed through the Vault
        }

        var confirmed = _dialogs.Confirm(
            "Remove from Font Vault",
            $"Remove \"{family.FamilyName}\" from the Font Vault?\n\n" +
            "FontBook deletes only its own copy. Your original font files and fonts installed in Windows are not affected.",
            "Remove");
        if (!confirmed)
        {
            return;
        }

        try
        {
            var removed = await _vault.RemoveFamilyAsync(family.Model);
            await _library.RefreshVaultAsync();
            SyncFamilyViewModels();
            Sidebar.UpdateCounts();
            RefreshList();
            ShowNotification($"Removed \"{family.FamilyName}\" from the Font Vault ({Plural(removed, "file")}).", isWarning: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
        {
            _logger.LogError(ex, "Removing {Family} from the vault failed", family.FamilyName);
            _dialogs.ShowError("Couldn't remove font", ex.Message);
        }
    }

    [RelayCommand]
    private void DismissNotification()
    {
        _notificationTimer?.Cancel();
        Notification = null;
    }

    private void ShowNotification(string message, bool isWarning)
    {
        _notificationTimer?.Cancel();
        _notificationTimer?.Dispose();
        _notificationTimer = new CancellationTokenSource();
        var token = _notificationTimer.Token;

        NotificationIsWarning = isWarning;
        Notification = message;
        _ = HideNotificationLaterAsync(token);
    }

    private async Task HideNotificationLaterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            Notification = null;
        }
        catch (OperationCanceledException)
        {
            // A newer notification replaced this one.
        }
    }

    private static string DescribeImport(VaultImportResult result)
    {
        var parts = new List<string>();
        if (result.Imported.Count > 0)
        {
            parts.Add($"Imported {Plural(result.Imported.Count, "file")}");
        }

        if (result.Duplicates.Count > 0)
        {
            parts.Add($"{Plural(result.Duplicates.Count, "file")} already in the Vault");
        }

        if (result.Failed.Count > 0)
        {
            var first = result.Failed[0];
            parts.Add(result.Failed.Count == 1
                ? $"\"{first.FileName}\" couldn't be imported: {first.Reason}"
                : $"{result.Failed.Count} files couldn't be imported (e.g. \"{first.FileName}\": {first.Reason})");
        }

        return parts.Count == 0 ? "No font files were found to import." : string.Join(" · ", parts);
    }

    private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>Keeps one view model per family, reusing existing ones so favorites/selection stay intact.</summary>
    private void SyncFamilyViewModels()
    {
        var previous = new Dictionary<string, FontFamilyViewModel>(_familyViewModels, StringComparer.Ordinal);
        _familyViewModels.Clear();
        foreach (var family in _library.Families)
        {
            _familyViewModels[family.Key] = previous.TryGetValue(family.Key, out var existing) && ReferenceEquals(existing.Model, family)
                ? existing
                : new FontFamilyViewModel(family, _fonts, _library.IsFavorite(family.Key));
        }

        // Mark Vault fonts whose family is already installed in Windows.
        var installedNames = _library.Families
            .Where(f => f.Source != FontSource.Imported)
            .Select(f => f.FamilyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var vm in _familyViewModels.Values)
        {
            vm.IsAlsoInstalled = vm.IsImported && installedNames.Contains(vm.FamilyName);
            vm.Category = _library.GetCategory(vm.Model);
            vm.IsCategoryManual = _library.IsCategoryManual(vm.Key);
        }

        if (SelectedFamily is not null && !_familyViewModels.ContainsKey(SelectedFamily.Key))
        {
            SelectedFamily = null;
        }
    }

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
        var results = _library.Query(scope, SearchText, _categoryFilter)
            .Select(f => _familyViewModels[f.Key])
            .ToList();
        RebuildCategoryChips(scope);

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
        var filtered = !string.IsNullOrWhiteSpace(SearchText) || _categoryFilter is not null;
        SectionSubtitle = !filtered || total == 0
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
        RebuildCategoryOptions();
    }

    /// <summary>Filter chips: "All" plus every category present in the current section (and search).</summary>
    private void RebuildCategoryChips(LibraryScope scope)
    {
        var counts = _library.CountByCategory(scope, SearchText);
        CategoryChips.Clear();
        CategoryChips.Add(new CategoryChoiceViewModel(null, "All", counts.Values.Sum(), _categoryFilter is null, OnCategoryChipSelected));
        foreach (var category in FontCategoryNames.All)
        {
            var count = counts.GetValueOrDefault(category);
            if (count > 0 || category == _categoryFilter)
            {
                CategoryChips.Add(new CategoryChoiceViewModel(category, category.ShortName(), count, category == _categoryFilter, OnCategoryChipSelected));
            }
        }
    }

    private void OnCategoryChipSelected(CategoryChoiceViewModel chip)
    {
        if (_categoryFilter == chip.Category)
        {
            return;
        }

        _categoryFilter = chip.Category;
        // Rebuilding the chips inside their own selection event would recreate the button being clicked; defer it.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(RefreshList);
    }

    /// <summary>Options in the preview's category picker for the selected family.</summary>
    private void RebuildCategoryOptions()
    {
        CategoryOptions.Clear();
        if (SelectedFamily is not { } family)
        {
            return;
        }

        var manual = family.IsCategoryManual;
        CategoryOptions.Add(new CategoryChoiceViewModel(null, $"Automatic ({family.Model.DetectedCategory.DisplayName()})", 0, !manual, OnCategoryOptionSelected));
        foreach (var category in FontCategoryNames.All)
        {
            CategoryOptions.Add(new CategoryChoiceViewModel(category, category.DisplayName(), 0, manual && family.Category == category, OnCategoryOptionSelected));
        }
    }

    private async void OnCategoryOptionSelected(CategoryChoiceViewModel option)
    {
        if (SelectedFamily is not { } family)
        {
            return;
        }

        try
        {
            await _library.SetCategoryAsync(family.Key, option.Category);
            family.Category = _library.GetCategory(family.Model);
            family.IsCategoryManual = _library.IsCategoryManual(family.Key);

            // If the change moves the font out of the active filter, show All so it doesn't vanish.
            if (_categoryFilter is { } filter && filter != family.Category)
            {
                _categoryFilter = null;
            }

            RefreshList();
            ShowNotification(option.Category is null
                ? $"\"{family.FamilyName}\" uses its detected category again ({family.CategoryName})."
                : $"Moved \"{family.FamilyName}\" to {family.CategoryName}.", isWarning: false);
        }
        catch (Exception ex) when (ex is System.Data.Common.DbException or IOException)
        {
            _logger.LogError(ex, "Saving the category of {Family} failed", family.FamilyName);
            _dialogs.ShowError("Couldn't change category", ex.Message);
        }
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
        ShowImportAction = IsEmpty && totalCount == 0 && kind is LibraryScopeKind.ImportedFonts or LibraryScopeKind.RecentlyAdded;
        if (!IsEmpty)
        {
            return;
        }

        if (totalCount > 0 && _categoryFilter is { } category && string.IsNullOrWhiteSpace(SearchText))
        {
            (EmptyGlyph, EmptyTitle, EmptyMessage) = (Glyphs.Font, $"No {category.DisplayName()} fonts here",
                "Choose \"All\" above the list to see every font in this section.");
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
                "Import font files to preview them without installing. FontBook keeps its own copy; your originals stay untouched. You can also drag files or folders onto the window."),
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
