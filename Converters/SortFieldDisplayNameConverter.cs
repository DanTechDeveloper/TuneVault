using System;
using System.Globalization;
using Avalonia.Data.Converters;
using TuneVault.Services;

namespace TuneVault.Converters;

public sealed class SortFieldDisplayNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TrackSortField field)
        {
            return value?.ToString() ?? string.Empty;
        }

        if (parameter is string context && context == "AlbumsList" && field == TrackSortField.Title)
        {
            return "Album Name";
        }

        return field.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}