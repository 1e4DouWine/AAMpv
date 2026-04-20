using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaAppMPV.UI.Converters;

public sealed class VolumeIconConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isMuted = values.Count > 0 && values[0] is bool muted && muted;
        var volume = values.Count > 1 && values[1] is double currentVolume ? currentVolume : 0;

        if (isMuted || volume <= 0)
            return PlayerIcons.VolumeMute;

        return volume < 50 ? PlayerIcons.VolumeLow : PlayerIcons.VolumeHigh;
    }
}
