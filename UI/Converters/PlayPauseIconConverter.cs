using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaAppMPV.UI.Converters;

public sealed class PlayPauseIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isPaused = value is bool paused && paused;
        return isPaused ? PlayerIcons.Play : PlayerIcons.Pause;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
