using CommunityToolkit.Mvvm.ComponentModel;
using FontBook.Core.Models;

namespace FontBook.App.ViewModels;

/// <summary>
/// One selectable category option: a filter chip above the font list, or an entry in the
/// preview's category picker. <see cref="Category"/> is null for "All" / "Automatic".
/// Like the sidebar, selection is driven by <see cref="IsSelected"/> so mouse, keyboard and
/// UI Automation all work.
/// </summary>
public sealed partial class CategoryChoiceViewModel : ObservableObject
{
    private readonly Action<CategoryChoiceViewModel> _onSelected;
    private bool _suppress;

    public CategoryChoiceViewModel(FontCategory? category, string label, int count, bool isSelected, Action<CategoryChoiceViewModel> onSelected)
    {
        Category = category;
        Label = label;
        Count = count;
        _onSelected = onSelected;
        _suppress = true;
        IsSelected = isSelected;
        _suppress = false;
    }

    public FontCategory? Category { get; }

    public string Label { get; }

    public int Count { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && !_suppress)
        {
            _onSelected(this);
        }
    }
}
