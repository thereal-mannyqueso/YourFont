using CommunityToolkit.Mvvm.ComponentModel;

namespace FontBook.App.ViewModels;

/// <summary>A checkbox in the preview's "Collections" menu: is the selected font in this collection?</summary>
public sealed partial class CollectionMembershipViewModel : ObservableObject
{
    private readonly Func<CollectionMembershipViewModel, bool, Task> _onToggled;

    public CollectionMembershipViewModel(long collectionId, string name, bool isMember, Func<CollectionMembershipViewModel, bool, Task> onToggled)
    {
        CollectionId = collectionId;
        Name = name;
        _isMember = isMember;
        _onToggled = onToggled;
    }

    public long CollectionId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isMember;

    partial void OnIsMemberChanged(bool value) => _ = _onToggled(this, value);
}
