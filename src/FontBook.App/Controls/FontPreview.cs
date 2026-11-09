using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FontBook.App.Controls;

/// <summary>
/// Reusable specimen renderer: draws text in a given typeface at a size in points.
/// Used for the large preview and for each row of the Styles list, and is designed to
/// work unchanged for Font Vault fonts loaded from files (any WPF <see cref="FontFamily"/>).
/// </summary>
/// <remarks>
/// The visual template lives in Themes/Generic.xaml. Point sizes are converted to WPF's
/// device-independent pixels (1/96 inch): 1 pt = 96/72 px.
/// </remarks>
public class FontPreview : Control
{
    public static readonly DependencyProperty PreviewFontFamilyProperty = DependencyProperty.Register(
        nameof(PreviewFontFamily), typeof(FontFamily), typeof(FontPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty PreviewFontWeightProperty = DependencyProperty.Register(
        nameof(PreviewFontWeight), typeof(FontWeight), typeof(FontPreview), new PropertyMetadata(FontWeights.Normal));

    public static readonly DependencyProperty PreviewFontStyleProperty = DependencyProperty.Register(
        nameof(PreviewFontStyle), typeof(FontStyle), typeof(FontPreview), new PropertyMetadata(FontStyles.Normal));

    public static readonly DependencyProperty PreviewFontStretchProperty = DependencyProperty.Register(
        nameof(PreviewFontStretch), typeof(FontStretch), typeof(FontPreview), new PropertyMetadata(FontStretches.Normal));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(FontPreview), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty FallbackTextProperty = DependencyProperty.Register(
        nameof(FallbackText), typeof(string), typeof(FontPreview),
        new PropertyMetadata("The quick brown fox jumps over the lazy dog", OnTextChanged));

    public static readonly DependencyProperty PointSizeProperty = DependencyProperty.Register(
        nameof(PointSize), typeof(double), typeof(FontPreview),
        new PropertyMetadata(24.0, OnPointSizeChanged), value => value is double d && d > 0 && double.IsFinite(d));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(FontPreview), new PropertyMetadata(null));

    public static readonly DependencyProperty IsSingleLineProperty = DependencyProperty.Register(
        nameof(IsSingleLine), typeof(bool), typeof(FontPreview), new PropertyMetadata(false));

    private static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText), typeof(string), typeof(FontPreview), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey DipFontSizePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DipFontSize), typeof(double), typeof(FontPreview), new PropertyMetadata(32.0));

    public static readonly DependencyProperty DipFontSizeProperty = DipFontSizePropertyKey.DependencyProperty;

    static FontPreview()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FontPreview), new FrameworkPropertyMetadata(typeof(FontPreview)));
    }

    public FontPreview()
    {
        UpdateDisplayText();
        UpdateDipFontSize();
    }

    /// <summary>Typeface to draw with. When null the inherited font is used.</summary>
    public FontFamily? PreviewFontFamily
    {
        get => (FontFamily?)GetValue(PreviewFontFamilyProperty);
        set => SetValue(PreviewFontFamilyProperty, value);
    }

    public FontWeight PreviewFontWeight
    {
        get => (FontWeight)GetValue(PreviewFontWeightProperty);
        set => SetValue(PreviewFontWeightProperty, value);
    }

    public FontStyle PreviewFontStyle
    {
        get => (FontStyle)GetValue(PreviewFontStyleProperty);
        set => SetValue(PreviewFontStyleProperty, value);
    }

    public FontStretch PreviewFontStretch
    {
        get => (FontStretch)GetValue(PreviewFontStretchProperty);
        set => SetValue(PreviewFontStretchProperty, value);
    }

    /// <summary>The specimen text. When blank, <see cref="FallbackText"/> is shown instead.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? FallbackText
    {
        get => (string?)GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    /// <summary>Size in typographic points.</summary>
    public double PointSize
    {
        get => (double)GetValue(PointSizeProperty);
        set => SetValue(PointSizeProperty, value);
    }

    /// <summary>Optional small label above the specimen (e.g. the style name).</summary>
    public string? Caption
    {
        get => (string?)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    /// <summary>When true the specimen stays on one line and is trimmed with an ellipsis.</summary>
    public bool IsSingleLine
    {
        get => (bool)GetValue(IsSingleLineProperty);
        set => SetValue(IsSingleLineProperty, value);
    }

    public string DisplayText => (string)GetValue(DisplayTextProperty);

    public double DipFontSize => (double)GetValue(DipFontSizeProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FontPreview)d).UpdateDisplayText();

    private static void OnPointSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FontPreview)d).UpdateDipFontSize();

    private void UpdateDisplayText()
        => SetValue(DisplayTextPropertyKey, string.IsNullOrWhiteSpace(Text) ? FallbackText ?? string.Empty : Text);

    private void UpdateDipFontSize() => SetValue(DipFontSizePropertyKey, PointSize * 96.0 / 72.0);
}
