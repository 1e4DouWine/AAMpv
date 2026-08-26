using AvaloniaAppMPV.Infrastructure.Avalonia;
using AvaloniaAppMPV.Infrastructure.Mpv;
using AvaloniaAppMPV.Infrastructure.Settings;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.UI.Main;
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

    [Fact]
    public void OpeningAnotherFile_ResetsPlayPauseStateImmediately()
    {
        var player = new FakePlayer();
        var viewModel = new MainWindowViewModel(
            player,
            new EmptyDialogService(),
            new InlineDispatcher(),
            new PlayerSettingsStore());

        player.EmitSnapshot(PlaybackSnapshot.Empty with
        {
            State = PlaybackState.Playing,
            FilePath = "old-video.mp4",
            IsPaused = false,
        });

        viewModel.OpenDroppedFile("new-video.mp4");

        // A queued snapshot from the previous video must not restore its
        // playing state while the new file is loading.
        player.EmitSnapshot(PlaybackSnapshot.Empty with
        {
            Revision = 1,
            State = PlaybackState.Playing,
            FilePath = "old-video.mp4",
            IsPaused = false,
        });

        Assert.False(viewModel.HasFile);
        Assert.True(viewModel.IsPaused);
        Assert.Equal(PlaybackState.Loading, viewModel.PlaybackState);
        Assert.Equal("new-video.mp4", player.LastLoadedPath);
    }

    private sealed class InlineDispatcher : IDispatcherService
    {
        public void Post(Action action) => action();

        public void RunOnce(Action action, TimeSpan delay) => action();
    }

    private sealed class EmptyDialogService : IDialogService
    {
        public Task<string?> OpenVideoFileAsync() => Task.FromResult<string?>(null);
    }

#pragma warning disable CS0067
    private sealed class FakePlayer : IMpvPlayer
    {
        public event Action<string?>? FileLoaded;
        public event Action<PlaybackSnapshot>? SnapshotChanged;
        public event Action<string>? ErrorOccurred;
        public event Action<string>? LogMessage;
        public event Action<string>? WarningOccurred;

        public PlaybackSnapshot Snapshot { get; private set; } = PlaybackSnapshot.Empty;
        public PlaybackState PlaybackState => Snapshot.State;
        public string? CurrentFilePath => Snapshot.FilePath;
        public string? CurrentHardwareDecode => Snapshot.HardwareDecode;
        public RenderBackendKind RenderBackend => Snapshot.RenderBackend;
        public string? LastLoadedPath { get; private set; }

        public void Configure(MpvPlayerSettings settings) { }
        public void LoadFile(string path) => LastLoadedPath = path;
        public void Play() { }
        public void Pause() { }
        public void TogglePause() { }
        public void Seek(double positionSeconds) { }
        public void SeekFast(double positionSeconds) { }
        public void SeekRelative(double offsetSeconds) { }
        public void SetVolume(double volume) { }
        public void SetMute(bool mute) { }
        public void ToggleMute() { }
        public void SetSpeed(double speed) { }
        public void ResetSpeed() { }
        public void Screenshot(string? path = null) { }
        public VideoInfo? GetVideoInfo() => null;

        public void EmitSnapshot(PlaybackSnapshot snapshot)
        {
            Snapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
    }
#pragma warning restore CS0067
}
