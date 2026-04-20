using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaAppMPV.UI.Converters;

public sealed class PlayPauseTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isPaused = value is bool paused && paused;
        return isPaused ? "▶ 播放" : "⏸ 暂停";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
