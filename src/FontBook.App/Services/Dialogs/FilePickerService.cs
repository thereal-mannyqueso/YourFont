using System.Windows;
using Microsoft.Win32;

namespace FontBook.App.Services.Dialogs;

/// <summary>Asks the user to choose files, without view models touching WPF windows.</summary>
public interface IFilePickerService
{
    /// <summary>Shows an "Import Fonts" dialog. Returns the chosen paths, or an empty list if cancelled.</summary>
    IReadOnlyList<string> PickFontFiles();

    /// <summary>Asks for a scanned/photographed template image. Null if cancelled.</summary>
    string? PickScanImage();

    /// <summary>Asks where to save the printable template. Null if cancelled.</summary>
    string? PickTemplateSaveLocation();

    /// <summary>Asks where to save a shared font ZIP. Null if cancelled.</summary>
    string? PickShareZipLocation(string suggestedName);
}

/// <summary>Small Windows shell actions (kept out of view models so they stay testable).</summary>
public interface IShellService
{
    /// <summary>Opens File Explorer with the file selected.</summary>
    void ShowInFolder(string path);
}

public sealed class ShellService : IShellService
{
    public void ShowInFolder(string path)
    {
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        });
    }
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

        return Show(dialog) ? dialog.FileNames : [];
    }

    public string? PickScanImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose your scanned template",
            Filter = "Images (*.png, *.jpg, *.jpeg, *.tif, *.tiff, *.bmp)|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? PickTemplateSaveLocation()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save Font Maker template",
            Filter = "PNG image (*.png)|*.png",
            FileName = "FontBook Font Maker Template.png",
            AddExtension = true,
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    public string? PickShareZipLocation(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Share font as ZIP (Beta)",
            Filter = "ZIP file (*.zip)|*.zip",
            FileName = suggestedName,
            AddExtension = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        return Show(dialog) ? dialog.FileName : null;
    }

    private static bool Show(FileDialog dialog)
    {
        var owner = Application.Current?.MainWindow;
        return (owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner)) == true;
    }
}
