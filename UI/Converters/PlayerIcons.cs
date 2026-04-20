using Avalonia.Media;

namespace AvaloniaAppMPV.UI.Converters;

internal static class PlayerIcons
{
    public static Geometry Play { get; } =
        Geometry.Parse("M8 5V19L19 12L8 5Z");

    public static Geometry Pause { get; } =
        Geometry.Parse("M6 19H10V5H6V19ZM14 5V19H18V5H14Z");

    public static Geometry VolumeHigh { get; } =
        Geometry.Parse("M3 9V15H7L12 20V4L7 9H3ZM16.5 12C16.5 10.23 15.48 8.71 14 7.97V16.02C15.48 15.29 16.5 13.77 16.5 12ZM14 3.23V5.29C16.89 6.15 19 8.83 19 12C19 15.17 16.89 17.85 14 18.71V20.77C18.01 19.86 21 16.28 21 12C21 7.72 18.01 4.14 14 3.23Z");

    public static Geometry VolumeLow { get; } =
        Geometry.Parse("M3 9V15H7L12 20V4L7 9H3ZM16.5 12C16.5 10.23 15.48 8.71 14 7.97V16.02C15.48 15.29 16.5 13.77 16.5 12Z");

    public static Geometry VolumeMute { get; } =
        Geometry.Parse("M16.5 12C16.5 10.23 15.48 8.71 14 7.97V10.18L16.45 12.63C16.48 12.43 16.5 12.22 16.5 12ZM19 12C19 12.94 18.8 13.82 18.46 14.64L19.97 16.15C20.63 14.91 21 13.5 21 12C21 7.72 18.01 4.14 14 3.23V5.29C16.89 6.15 19 8.83 19 12ZM4.27 3L3 4.27L7.73 9H3V15H7L12 20V14.27L16.25 18.52C15.58 19.04 14.83 19.45 14 19.71V21.77C15.38 21.45 16.63 20.82 17.68 19.96L19.73 22.01L21 20.74L12 11.73L4.27 3ZM12 4L9.91 6.09L12 8.18V4Z");
}
