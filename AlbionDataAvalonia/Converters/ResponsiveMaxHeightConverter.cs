using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AlbionDataAvalonia.Converters;

public sealed class ResponsiveMaxHeightConverter : IValueConverter
{
    public double Ratio { get; set; } = 0.3;

    public double Min { get; set; } = 180;

    public double Max { get; set; } = 360;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double height || double.IsNaN(height) || double.IsInfinity(height) || height <= 0)
        {
            return Min;
        }

        return Math.Clamp(height * Ratio, Min, Max);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
