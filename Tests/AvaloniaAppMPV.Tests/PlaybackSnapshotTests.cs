using AvaloniaAppMPV.Infrastructure.Avalonia;
using AvaloniaAppMPV.Infrastructure.Mpv;
using AvaloniaAppMPV.Core.Playback;
using Xunit;

namespace AvaloniaAppMPV.Tests;

public sealed class PlaybackSnapshotTests
{
    [Fact]
    public void Configure_UpdatesInitialSnapshotWithoutCreatingNativeCore()
    {
        var dispatcher = new InlineDispatcher();
        using var player = new MpvPlayerService(dispatcher);

        player.Configure(new MpvPlayerSettings
        {
            DefaultSpeed = 1.5,
            Volume = 72,
            IsMuted = true,
        });

        Assert.Equal(PlaybackState.Unloaded, player.Snapshot.State);
        Assert.Equal(1.5, player.Snapshot.Speed);
        Assert.Equal(72, player.Snapshot.Volume);
        Assert.True(player.Snapshot.IsMuted);
        Assert.Equal(RenderBackendKind.OpenGL, player.Snapshot.RenderBackend);
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Post(Action action) => action();

        public void RunOnce(Action action, TimeSpan delay) => action();
    }
}
