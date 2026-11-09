using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FontBook.App.Services.Settings;
using FontBook.Core.Data;

namespace FontBook.App.ViewModels;

/// <summary>State of the preview pane: which family/face is shown, the specimen text and size.</summary>
public sealed partial class PreviewViewModel(ISettingsStore settings, DeferredSettingsWriter settingsWriter) : ObservableObject
{
    public const string DefaultSpecimenText = "The quick brown fox jumps over the lazy dog";
    public const double MinPointSize = 8;
    public const double MaxPointSize = 200;
    public const double DefaultPointSize = 64;

    /// <summary>Size used for the smaller specimens in the "Styles" list.</summary>
    public const double StyleRowPointSize = 22;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFamily))]
    private FontFamilyViewModel? _family;

    [ObservableProperty]
    private FontFaceViewModel? _selectedFace;

    [ObservableProperty]
    private string _specimenText = DefaultSpecimenText;

    [ObservableProperty]
    private double _pointSize = DefaultPointSize;

    public bool HasFamily => Family is not null;

    public async Task LoadSettingsAsync()
    {
        var text = await settings.GetAsync(SettingKeys.SpecimenText);
        if (text is not null)
        {
            SpecimenText = text;
        }

        var size = await settings.GetAsync(SettingKeys.PreviewSize);
        if (double.TryParse(size, NumberStyles.Float, CultureInfo.InvariantCulture, out var points))
        {
            PointSize = Math.Clamp(points, MinPointSize, MaxPointSize);
        }
    }

    partial void OnFamilyChanged(FontFamilyViewModel? oldValue, FontFamilyViewModel? newValue)
    {
        // A face can be selected by mouse, keyboard or UI Automation; all of them end up
        // setting FontFaceViewModel.IsSelected, so that is what we listen to.
        foreach (var face in oldValue?.Faces ?? [])
        {
            face.PropertyChanged -= OnFacePropertyChanged;
        }

        foreach (var face in newValue?.Faces ?? [])
        {
            face.PropertyChanged += OnFacePropertyChanged;
        }

        SelectedFace = newValue?.DefaultFace;
    }

    private void OnFacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontFaceViewModel.IsSelected) && sender is FontFaceViewModel { IsSelected: true } face)
        {
            SelectedFace = face;
        }
    }

    partial void OnSelectedFaceChanged(FontFaceViewModel? oldValue, FontFaceViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }
    }

    partial void OnSpecimenTextChanged(string value) => settingsWriter.Schedule(SettingKeys.SpecimenText, value);

    partial void OnPointSizeChanged(double value)
        => settingsWriter.Schedule(SettingKeys.PreviewSize, Math.Round(value).ToString(CultureInfo.InvariantCulture));

    [RelayCommand]
    private void ResetSpecimen() => SpecimenText = DefaultSpecimenText;
}
