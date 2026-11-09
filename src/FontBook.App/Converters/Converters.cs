using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FontBook.App.Converters;

/// <summary>true → Visible, false → Collapsed. Pass ConverterParameter="Invert" to reverse.</summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (IsInvert(parameter))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static bool IsInvert(object? parameter)
        => string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
}

/// <summary>non-null → Visible, null → Collapsed. Pass ConverterParameter="Invert" to reverse.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is not null;
        if (BoolToVisibilityConverter.IsInvert(parameter))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Negates a boolean.</summary>
[ValueConversion(typeof(bool), typeof(bool))]
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// For radio buttons bound to an enum: IsChecked is true when the value equals the parameter,
/// and checking the button writes the parameter back.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null && value.Equals(parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is not null ? parameter : Binding.DoNothing;
}

/// <summary>Lets a view model expose a pane width as a plain number while the Grid uses GridLength.</summary>
public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? new GridLength(d) : new GridLength(1, GridUnitType.Star);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is GridLength { IsAbsolute: true } length ? length.Value : Binding.DoNothing;
}

/// <summary>Formats a point size for display, e.g. 64 → "64 pt".</summary>
public sealed class PointSizeTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? string.Create(culture, $"{Math.Round(d):0} pt") : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
