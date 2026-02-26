using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaAppMPV.Models;

namespace AvaloniaAppMPV.Views;

public partial class VideoInfoWindow : Window
{
    public VideoInfoWindow()
    {
        InitializeComponent();
    }

    public void SetVideoInfo(VideoInfo info)
    {
        AddSection("视频");
        AddRow("文件名", info.FileName);
        AddRow("容器格式", info.FileFormat);
        AddRow("文件大小", FormatFileSize(info.FileSize));
        AddRow("分辨率", info.VideoWidth != null && info.VideoHeight != null
            ? $"{info.VideoWidth} x {info.VideoHeight}" : null);
        AddRow("视频编解码器", info.VideoCodec);
        AddRow("硬件解码", info.HwDecCurrent ?? "无");
        AddRow("帧率", info.VideoFps.HasValue ? $"{info.VideoFps:F3} fps" : null);
        AddRow("视频码率", FormatBitrate(info.VideoBitrate));
        AddRow("像素格式", info.PixelFormat);

        AddSection("音频");
        AddRow("音频编解码器", info.AudioCodec);
        AddRow("采样率", info.AudioSampleRate.HasValue ? $"{info.AudioSampleRate} Hz" : null);
        AddRow("声道数", info.AudioChannels?.ToString());
        AddRow("音频码率", FormatBitrate(info.AudioBitrate));
    }

    private void AddSection(string title)
    {
        var isFirst = InfoPanel.Children.Count == 0;

        // Add divider before the new section (except the first one)
        if (!isFirst)
        {
            InfoPanel.Children.Add(new Border
            {
                Height = 1,
                Opacity = 0.15,
                Background = new SolidColorBrush(
                    Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark
                        ? Colors.White : Colors.Black),
                Margin = new Avalonia.Thickness(0, 12, 0, 0)
            });
        }

        InfoPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, isFirst ? 0 : 16, 0, 6),
            Opacity = 0.9
        });
    }

    private void AddRow(string label, string? value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("140,*"),
            Margin = new Avalonia.Thickness(0, 3)
        };

        var labelBlock = new TextBlock
        {
            Text = label,
            Opacity = 0.55,
            FontSize = 13,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value ?? "N/A",
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);
        InfoPanel.Children.Add(grid);
    }

    private static string? FormatFileSize(long? bytes)
    {
        if (bytes == null) return null;
        double b = bytes.Value;
        if (b < 1024) return $"{b:F0} B";
        b /= 1024;
        if (b < 1024) return $"{b:F1} KB";
        b /= 1024;
        if (b < 1024) return $"{b:F2} MB";
        b /= 1024;
        return $"{b:F2} GB";
    }

    private static string? FormatBitrate(double? bps)
    {
        if (bps == null) return null;
        if (bps <= 0) return "0 kbps";
        double kbps = bps.Value / 1000.0;
        return kbps >= 1000 ? $"{kbps / 1000.0:F2} Mbps" : $"{kbps:F0} kbps";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
