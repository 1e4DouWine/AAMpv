using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaAppMPV.UI.Converters;

public sealed class MuteTooltipConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMuted = value is bool muted && muted;
        return isMuted ? "取消静音" : "静音";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
