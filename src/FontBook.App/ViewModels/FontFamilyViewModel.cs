using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FontBook.App.Services.Rendering;
using FontBook.Core.Models;

namespace FontBook.App.ViewModels;

/// <summary>A row in the font list and the subject of the preview pane.</summary>
public sealed partial class FontFamilyViewModel : ObservableObject
{
    public FontFamilyViewModel(FontFamilyInfo model, IFontPreviewProvider fonts, bool isFavorite)
    {
        Model = model;
        PreviewFamily = fonts.GetFontFamily(model);
        Faces = [.. model.Faces.Select(face => new FontFaceViewModel(face, PreviewFamily, fonts))];
        DefaultFace = Faces.FirstOrDefault(f => f.Model is { Weight: 400, Slant: FontSlant.Normal, Stretch: 5 })
                      ?? (Faces.Count > 0 ? Faces[0] : null);
        _isFavorite = isFavorite;
    }

    public FontFamilyInfo Model { get; }

    public string Key => Model.Key;

    public string FamilyName => Model.FamilyName;

    public FontFamily PreviewFamily { get; }

    public IReadOnlyList<FontFaceViewModel> Faces { get; }

    /// <summary>The face shown when the family is first selected: Regular if it exists.</summary>
    public FontFaceViewModel? DefaultFace { get; }

    public string StyleCountText => Faces.Count == 1 ? "1 style" : $"{Faces.Count} styles";

    public string SourceText => Model.Source switch
    {
        FontSource.System => "System font",
        FontSource.User => "User font",
        FontSource.Imported => "Imported to Font Vault",
        _ => string.Empty,
    };

    [ObservableProperty]
    private bool _isFavorite;
}
