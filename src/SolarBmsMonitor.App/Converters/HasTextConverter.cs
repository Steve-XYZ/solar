using System.Globalization;

namespace SolarBmsMonitor.App.Converters;

/// <summary>
/// Maps a string to whether it carries content, so a row bound to an optional
/// value collapses instead of reserving an empty line.
/// </summary>
public sealed class HasTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("HasTextConverter is one-way.");
}
