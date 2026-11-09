using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontBook.App.Services.Dialogs;
using FontBook.Core.Collections;
using FontBook.Core.Library;
using FontBook.Core.Models;
using Microsoft.Extensions.Logging;

namespace FontBook.App.ViewModels;

/// <summary>
/// The sidebar: fixed Library / Font Vault sections plus the user's custom collections.
/// Owns which entry is selected and the create/rename/delete collection workflow.
/// </summary>
public sealed partial class SidebarViewModel : ObservableObject
{
    private readonly ICollectionService _collections;
    private readonly IFontLibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SidebarViewModel> _logger;
    private bool _isSelecting;

    public SidebarViewModel(ICollectionService collections, IFontLibraryService library, IDialogService dialogs, ILogger<SidebarViewModel> logger)
    {
        _collections = collections;
        _library = library;
        _dialogs = dialogs;
        _logger = logger;

        LibraryItems =
        [
            new(new LibraryScope(LibraryScopeKind.AllFonts), "All Fonts", Glyphs.Font, Select),
            new(new LibraryScope(LibraryScopeKind.SystemFonts), "System Fonts", Glyphs.Pc, Select),
            new(new LibraryScope(LibraryScopeKind.UserFonts), "User Fonts", Glyphs.Person, Select),
            new(new LibraryScope(LibraryScopeKind.Favorites), "Favorites", Glyphs.Star, Select),
        ];
        ImportedItem = new(new LibraryScope(LibraryScopeKind.ImportedFonts), "Imported Fonts", Glyphs.Import, Select);
        CustomCollectionsItem = new(new LibraryScope(LibraryScopeKind.AnyCollection), "Custom Collections", Glyphs.Library, Select);
        RecentlyAddedItem = new(new LibraryScope(LibraryScopeKind.RecentlyAdded), "Recently Added", Glyphs.Recent, Select);

        _selectedItem = LibraryItems[0];
        Select(_selectedItem);
    }

    /// <summary>Raised when the selected sidebar entry changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised after a collection is created, renamed or deleted.</summary>
    public event EventHandler? CollectionsChanged;

    public IReadOnlyList<NavigationItemViewModel> LibraryItems { get; }

    public NavigationItemViewModel ImportedItem { get; }

    public NavigationItemViewModel CustomCollectionsItem { get; }

    public NavigationItemViewModel RecentlyAddedItem { get; }

    public ObservableCollection<NavigationItemViewModel> Collections { get; } = [];

    [ObservableProperty]
    private NavigationItemViewModel _selectedItem;

    private IEnumerable<NavigationItemViewModel> AllItems =>
        LibraryItems.Concat([ImportedItem, CustomCollectionsItem]).Concat(Collections).Append(RecentlyAddedItem);

    public async Task LoadAsync()
    {
        var collections = await _collections.GetAllAsync();
        Collections.Clear();
        foreach (var collection in collections)
        {
            Collections.Add(CreateCollectionItem(collection));
        }
    }

    /// <summary>Refreshes the number shown next to each entry.</summary>
    public void UpdateCounts()
    {
        foreach (var item in AllItems)
        {
            item.Count = _library.Count(item.Scope);
        }
    }

    [RelayCommand]
    private async Task NewCollectionAsync()
    {
        try
        {
            var collection = await _collections.CreateUntitledAsync();
            var item = CreateCollectionItem(collection);
            InsertSorted(item);
            Select(item);
            CollectionsChanged?.Invoke(this, EventArgs.Empty);
            BeginRename(item);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogError(ex, "Creating a collection failed");
            _dialogs.ShowError("Couldn't create collection", ex.Message);
        }
    }

    [RelayCommand]
    private static void BeginRename(NavigationItemViewModel? item)
    {
        if (item is { IsCollection: true })
        {
            item.EditText = item.Title;
            item.IsEditing = true;
        }
    }

    [RelayCommand]
    private static void CancelRename(NavigationItemViewModel? item)
    {
        if (item is not null)
        {
            item.IsEditing = false;
        }
    }

    [RelayCommand]
    private async Task CommitRenameAsync(NavigationItemViewModel? item)
    {
        // Enter and losing focus both commit; the IsEditing check makes the second call a no-op.
        if (item is not { IsEditing: true, CollectionId: { } id })
        {
            return;
        }

        item.IsEditing = false;
        if (string.Equals(item.EditText.Trim(), item.Title, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            item.Title = await _collections.RenameAsync(id, item.EditText);
            Collections.Remove(item);
            InsertSorted(item);
            CollectionsChanged?.Invoke(this, EventArgs.Empty);
            if (item.IsSelected)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty); // refresh the section title
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _dialogs.ShowError("Couldn't rename collection", ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteCollectionAsync(NavigationItemViewModel? item)
    {
        if (item is not { CollectionId: { } id })
        {
            return;
        }

        var confirmed = _dialogs.Confirm(
            "Delete collection",
            $"Delete the collection \"{item.Title}\"?\n\nOnly the collection is removed. The fonts in it are not deleted or uninstalled.",
            "Delete");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _collections.DeleteAsync(id);
            _library.ForgetCollection(id);
            var wasSelected = item.IsSelected;
            Collections.Remove(item);
            if (wasSelected)
            {
                Select(CustomCollectionsItem);
            }

            CollectionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            _logger.LogError(ex, "Deleting collection {Id} failed", id);
            _dialogs.ShowError("Couldn't delete collection", ex.Message);
        }
    }

    /// <summary>
    /// Makes <paramref name="item"/> the only selected entry. Each item also calls this when it
    /// becomes selected (mouse, keyboard or UI Automation), so navigation always follows selection.
    /// </summary>
    private void Select(NavigationItemViewModel item)
    {
        if (_isSelecting)
        {
            return;
        }

        _isSelecting = true;
        try
        {
            foreach (var other in AllItems)
            {
                other.IsSelected = ReferenceEquals(other, item);
            }
        }
        finally
        {
            _isSelecting = false;
        }

        if (!ReferenceEquals(SelectedItem, item))
        {
            SelectedItem = item;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void InsertSorted(NavigationItemViewModel item)
    {
        var index = 0;
        while (index < Collections.Count
               && StringComparer.CurrentCultureIgnoreCase.Compare(Collections[index].Title, item.Title) < 0)
        {
            index++;
        }

        Collections.Insert(index, item);
    }

    private NavigationItemViewModel CreateCollectionItem(FontCollection collection)
        => new(LibraryScope.ForCollection(collection.Id), collection.Name, Glyphs.Folder, Select);

    private static bool IsExpected(Exception ex)
        => ex is ArgumentException or InvalidOperationException or System.Data.Common.DbException or IOException;
}
