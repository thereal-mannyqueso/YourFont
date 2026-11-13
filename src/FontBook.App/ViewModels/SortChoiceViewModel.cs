using CommunityToolkit.Mvvm.ComponentModel;
using FontBook.Core.Library;

namespace FontBook.App.ViewModels;

/// <summary>One option in the Sort menu. Selection-driven, like the sidebar and category chips.</summary>
public sealed partial class SortChoiceViewModel : ObservableObject
{
    private readonly Action<SortChoiceViewModel> _onSelected;
    private readonly bool _ready;

    public SortChoiceViewModel(FontSortOrder order, bool isSelected, Action<SortChoiceViewModel> onSelected)
    {
        Order = order;
        Label = order.DisplayName();
        _onSelected = onSelected;
        IsSelected = isSelected;
        _ready = true;
    }

    public FontSortOrder Order { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value && _ready)
        {
            _onSelected(this);
        }
    }
}
