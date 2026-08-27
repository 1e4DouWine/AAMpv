using AvaloniaAppMPV.Infrastructure.Settings;
using Xunit;

namespace AvaloniaAppMPV.Tests;

public sealed class PlayerSettingsStoreTests
{
    [Fact]
    public void RecentFile_IsDeduplicatedAndPositionIsPreserved()
    {
        var store = new PlayerSettingsStore();
        var path = Path.Combine(Path.GetTempPath(), "avalonia-mpv-test-video.mp4");

        store.AddRecentFile(path);
        store.UpdatePosition(path, 42.5);
        store.AddRecentFile(path);

        Assert.Single(store.Document.RecentFiles, x =>
            string.Equals(x.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        Assert.Equal(42.5, store.GetPosition(path));
    }

    [Fact]
    public void SettingsStore_ExposesAppScopedPaths()
    {
        var store = new PlayerSettingsStore();

        Assert.EndsWith("AvaloniaAppMPV", store.AppDataDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("settings.json", store.SettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("mpv.conf", store.MpvConfigPath, StringComparison.OrdinalIgnoreCase);
    }
}
