using CommunityToolkit.Mvvm.ComponentModel;
using FontBook.Core.Library;

namespace FontBook.App.ViewModels;

/// <summary>One entry in the sidebar (a library section or a custom collection).</summary>
public sealed partial class NavigationItemViewModel : ObservableObject
{
    private readonly Action<NavigationItemViewModel> _onSelected;

    /// <param name="onSelected">
    /// Called whenever this entry becomes selected — by mouse, keyboard or assistive
    /// technology (UI Automation). Driving navigation from the selection state, instead of
    /// from a click command, keeps all of those input methods working the same way.
    /// </param>
    public NavigationItemViewModel(LibraryScope scope, string title, string glyph, Action<NavigationItemViewModel> onSelected)
    {
        Scope = scope;
        _title = title;
        Glyph = glyph;
        _onSelected = onSelected;
    }

    public LibraryScope Scope { get; }

    /// <summary>Segoe Fluent Icons code point.</summary>
    public string Glyph { get; }

    public bool IsCollection => Scope.Kind == LibraryScopeKind.Collection;

    public long? CollectionId => Scope.CollectionId;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _count;

    /// <summary>True while the inline rename box is shown (collections only).</summary>
    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = string.Empty;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected(this);
        }
    }
}
