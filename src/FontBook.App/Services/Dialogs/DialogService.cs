using System.Windows;

namespace FontBook.App.Services.Dialogs;

/// <summary>
/// Lets view models ask the user questions without referencing WPF windows directly,
/// which keeps them testable (a test can supply a fake that always answers "yes").
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string confirmLabel = "OK");

    void ShowError(string title, string message);
}

/// <summary>
/// Milestone 1 implementation using the standard Windows message box.
/// A themed in-app dialog can replace it later without touching any view model.
/// </summary>
public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string confirmLabel = "OK")
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel)
            : MessageBox.Show(owner, message, title, MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        return result == MessageBoxResult.OK;
    }

    public void ShowError(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
