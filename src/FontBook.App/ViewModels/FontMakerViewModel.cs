using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontBook.App.Services.Dialogs;
using FontBook.App.Services.FontMaker;
using FontBook.Core.FontMaker;
using FontBook.Core.Vault;
using Microsoft.Extensions.Logging;

namespace FontBook.App.ViewModels;

/// <summary>One character box from the scan, shown in the results grid.</summary>
public sealed record GlyphTileViewModel(string Label, ImageSource? Image, bool HasInk);

/// <summary>
/// Font Maker (beta): print a template, fill it in, scan it, and turn it into a font in the Font Vault.
/// </summary>
public sealed partial class FontMakerViewModel(
    IFontMakerService fontMaker,
    ITemplateRenderer templates,
    IScanImageLoader scanLoader,
    IFilePickerService filePicker,
    IDialogService dialogs,
    ILogger<FontMakerViewModel> logger) : ObservableObject
{
    private IReadOnlyList<ScannedGlyph> _glyphs = [];

    /// <summary>Raised after a font was created and imported, so the main window can show it.</summary>
    public event EventHandler<VaultImportResult>? FontCreated;

    public ObservableCollection<GlyphTileViewModel> Tiles { get; } = [];

    public int TotalCount { get; } = FontTemplateLayout.Characters.Length;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateFontCommand))]
    private bool _hasResult;

    [ObservableProperty]
    private int _foundCount;

    [ObservableProperty]
    private string? _scanFileName;

    [ObservableProperty]
    private ImageSource? _scanPreview;

    [ObservableProperty]
    private string? _errorText;

    /// <summary>"Reading your scan…" etc. Null when idle.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateFontCommand))]
    private string? _busyText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateFontCommand))]
    private string _fontName = "My Handwriting";

    [RelayCommand]
    private void PrintTemplate()
    {
        try
        {
            templates.Print();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Printing.PrintSystemException)
        {
            logger.LogError(ex, "Printing the template failed");
            dialogs.ShowError("Couldn't print", ex.Message);
        }
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        if (filePicker.PickTemplateSaveLocation() is not { } path)
        {
            return;
        }

        try
        {
            templates.SavePng(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialogs.ShowError("Couldn't save the template", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ChooseScanAsync()
    {
        if (filePicker.PickScanImage() is not { } path)
        {
            return;
        }

        await LoadScanAsync(path);
    }

    /// <summary>Reads a scan from disk (also used by tests and future drag-and-drop).</summary>
    public async Task LoadScanAsync(string path)
    {
        ErrorText = null;
        HasResult = false;
        Tiles.Clear();
        BusyText = "Reading your scan…";
        try
        {
            var (image, preview) = scanLoader.Load(path);
            ScanFileName = Path.GetFileName(path);
            ScanPreview = preview;

            var result = await fontMaker.ReadScanAsync(image);
            if (!result.Success)
            {
                ErrorText = result.Error;
                return;
            }

            _glyphs = result.Glyphs;
            foreach (var glyph in result.Glyphs)
            {
                Tiles.Add(new GlyphTileViewModel(glyph.Character.ToString(), glyph.Ink is { } ink ? ToImage(ink) : null, glyph.HasInk));
            }

            FoundCount = result.FoundCount;
            HasResult = true;
            if (FoundCount == 0)
            {
                ErrorText = "The template was found, but every box looks empty. Try a darker pen or a brighter scan.";
            }
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UnauthorizedAccessException or FileFormatException or ArgumentException)
        {
            logger.LogWarning(ex, "Reading scan {Path} failed", path);
            ErrorText = "FontBook couldn't open that image. Use a PNG or JPG scan or photo of the template.";
        }
        finally
        {
            BusyText = null;
        }
    }

    private bool CanCreateFont() => HasResult && FoundCount > 0 && BusyText is null && !string.IsNullOrWhiteSpace(FontName);

    [RelayCommand(CanExecute = nameof(CanCreateFont))]
    private async Task CreateFontAsync()
    {
        ErrorText = null;
        BusyText = "Building your font…";
        try
        {
            var result = await fontMaker.CreateFontAsync(FontName, _glyphs);
            if (result.Imported.Count == 0)
            {
                ErrorText = result.Failed.Count > 0
                    ? $"The font couldn't be added to the Font Vault: {result.Failed[0].Reason}"
                    : "The font couldn't be added to the Font Vault.";
                return;
            }

            FontCreated?.Invoke(this, result);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            ErrorText = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException)
        {
            logger.LogError(ex, "Creating font {Name} failed", FontName);
            dialogs.ShowError("Couldn't create font", ex.Message);
        }
        finally
        {
            BusyText = null;
        }
    }

    [RelayCommand]
    private void StartOver()
    {
        _glyphs = [];
        Tiles.Clear();
        HasResult = false;
        FoundCount = 0;
        ScanFileName = null;
        ScanPreview = null;
        ErrorText = null;
    }

    /// <summary>Renders an ink mask as dark ink on white "paper".</summary>
    private static BitmapSource ToImage(bool[,] ink)
    {
        int width = ink.GetLength(0), height = ink.GetLength(1);
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = ink[x, y] ? (byte)30 : (byte)255;
            }
        }

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, width);
        bitmap.Freeze();
        return bitmap;
    }
}
