using System.Windows;

namespace FontBook.App.Services.Dialogs;

/// <summary>
/// Lets view models ask the user questions without referencing WPF windows directly,
/// which keeps them testable (a test can supply a fake that always answers "yes").
/// </summary>
public interface IDialogService
{
    bool Confirm(string title, string message, string confirmLabel = "OK");

    /// <summary>Yes / No / Cancel question. The message must explain what Yes and No mean.</summary>
    DialogChoice Choose(string title, string message);

    void ShowError(string title, string message);
}

public enum DialogChoice
{
    Yes,
    No,
    Cancel,
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

    public DialogChoice Choose(string title, string message)
    {
        var owner = Application.Current?.MainWindow;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Cancel);
        return result switch
        {
            MessageBoxResult.Yes => DialogChoice.Yes,
            MessageBoxResult.No => DialogChoice.No,
            _ => DialogChoice.Cancel,
        };
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
