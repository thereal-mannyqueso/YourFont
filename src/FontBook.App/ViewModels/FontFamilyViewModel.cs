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
        Faces = [.. model.Faces.Select(face => new FontFaceViewModel(face, model, fonts))];
        DefaultFace = Faces.FirstOrDefault(f => f.Model is { Weight: 400, Slant: FontSlant.Normal, Stretch: 5 })
                      ?? (Faces.Count > 0 ? Faces[0] : null);

        // The list shows the family name drawn in its default style.
        PreviewFamily = DefaultFace?.FontFamily ?? fonts.GetFontFamily(model, null);
        PreviewWeight = DefaultFace?.FontWeight ?? System.Windows.FontWeights.Normal;
        PreviewStyle = DefaultFace?.FontStyle ?? System.Windows.FontStyles.Normal;
        PreviewStretch = DefaultFace?.FontStretch ?? System.Windows.FontStretches.Normal;
        _isFavorite = isFavorite;
    }

    public FontFamilyInfo Model { get; }

    public string Key => Model.Key;

    public string FamilyName => Model.FamilyName;

    public FontFamily PreviewFamily { get; }

    public System.Windows.FontWeight PreviewWeight { get; }

    public System.Windows.FontStyle PreviewStyle { get; }

    public System.Windows.FontStretch PreviewStretch { get; }

    public IReadOnlyList<FontFaceViewModel> Faces { get; }

    /// <summary>The face shown when the family is first selected: Regular if it exists.</summary>
    public FontFaceViewModel? DefaultFace { get; }

    /// <summary>True for fonts imported into the Font Vault (they can be removed from it).</summary>
    public bool IsImported => Model.Source == FontSource.Imported;

    /// <summary>True for fonts installed for the current user only (they can be uninstalled).</summary>
    public bool IsUserFont => Model.Source == FontSource.User;

    /// <summary>For Vault fonts: a font with the same family name is already installed in Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isAlsoInstalled;

    /// <summary>Vault fonts that aren't installed yet can be installed.</summary>
    public bool CanInstall => IsImported && !IsAlsoInstalled;

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

    /// <summary>Category shown for this family (manual choice or automatic detection).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CategoryName), nameof(ListCaption))]
    private FontCategory _category = FontCategory.Other;

    /// <summary>True when the user chose the category by hand.</summary>
    [ObservableProperty]
    private bool _isCategoryManual;

    public string CategoryName => Category.DisplayName();

    /// <summary>Second line in the font list, e.g. "4 styles · Serif".</summary>
    public string ListCaption => $"{StyleCountText}  ·  {Category.ShortName()}";
}
