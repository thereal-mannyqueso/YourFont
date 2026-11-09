using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using FontBook.App.Services.Rendering;
using FontBook.Core.Models;

namespace FontBook.App.ViewModels;

/// <summary>One style of a family, ready to be drawn (WPF family/weight/style/stretch resolved).</summary>
public sealed partial class FontFaceViewModel : ObservableObject
{
    public FontFaceViewModel(FontFaceInfo model, FontFamilyInfo family, IFontPreviewProvider fonts)
    {
        Model = model;
        FontFamily = fonts.GetFontFamily(family, model);
        FontWeight = fonts.GetWeight(model);
        FontStyle = fonts.GetStyle(model);
        FontStretch = fonts.GetStretch(model);
    }

    public FontFaceInfo Model { get; }

    public string StyleName => Model.StyleName;

    public FontFamily FontFamily { get; }

    public FontWeight FontWeight { get; }

    public FontStyle FontStyle { get; }

    public FontStretch FontStretch { get; }

    /// <summary>True when this face is the one shown in the large preview.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
