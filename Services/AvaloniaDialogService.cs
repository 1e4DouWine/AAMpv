using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace AvaloniaAppMPV.Services;

public class AvaloniaDialogService : IDialogService
{
    public async Task<string?> OpenVideoFileAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel == null) return null;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择视频文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("视频文件")
                {
                    Patterns =
                    [
                        "*.mp4", "*.mkv", "*.avi", "*.mov", "*.wmv",
                        "*.flv", "*.webm", "*.m4v", "*.mpg", "*.mpeg",
                        "*.ts", "*.3gp"
                    ]
                },
                FilePickerFileTypes.All
            ]
        });

        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }
}
