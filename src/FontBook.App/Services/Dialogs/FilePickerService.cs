using System.Windows;
using Microsoft.Win32;

namespace FontBook.App.Services.Dialogs;

/// <summary>Asks the user to choose files, without view models touching WPF windows.</summary>
public interface IFilePickerService
{
    /// <summary>Shows an "Import Fonts" dialog. Returns the chosen paths, or an empty list if cancelled.</summary>
    IReadOnlyList<string> PickFontFiles();
}

public sealed class FilePickerService : IFilePickerService
{
    public IReadOnlyList<string> PickFontFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Fonts into the Font Vault",
            Filter = "Font files (*.ttf, *.otf, *.ttc, *.otc)|*.ttf;*.otf;*.ttc;*.otc|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        var owner = Application.Current?.MainWindow;
        var accepted = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return accepted == true ? dialog.FileNames : [];
    }
}
