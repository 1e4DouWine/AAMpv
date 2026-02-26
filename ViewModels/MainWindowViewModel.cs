using System;
using System.Threading.Tasks;
using Avalonia.Media;
using AvaloniaAppMPV.Models;
using AvaloniaAppMPV.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaAppMPV.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IMpvPlayer _player;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcher;

    private bool _isSeeking;
    private bool _isDragging;
    private DateTime _lastSeekTime = DateTime.MinValue;
    private const double SeekThrottleMs = 100;

    // --- Observable state ---

    [ObservableProperty]
    private string _title = "MPV Player";

    [ObservableProperty]
    private string _playPauseText = "▶ 播放";

    [ObservableProperty]
    private bool _hasFile;

    [ObservableProperty]
    private double _position;

    [ObservableProperty]
    private double _duration;

    [ObservableProperty]
    private double _volume = 100;

    [ObservableProperty]
    private bool _isMuted;

    [ObservableProperty]
    private bool _isPaused = true;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    // --- Icon path data ---

    private static readonly Geometry PlayIconData =
        Geometry.Parse("M8 5V19L19 12L8 5Z");
    private static readonly Geometry PauseIconData =
        Geometry.Parse("M6 19H10V5H6V19ZM14 5V19H18V5H14Z");
    private static readonly Geometry VolumeHighData =
        Geometry.Parse("M3 9V15H7L12 20V4L7 9H3ZM16.5 12C16.5 10.23 15.48 8.71 14 7.97V16.02C15.48 15.29 16.5 13.77 16.5 12ZM14 3.23V5.29C16.89 6.15 19 8.83 19 12C19 15.17 16.89 17.85 14 18.71V20.77C18.01 19.86 21 16.28 21 12C21 7.72 18.01 4.14 14 3.23Z");
    private static readonly Geometry VolumeLowData =
        Geometry.Parse("M3 9V15H7L12 20V4L7 9H3ZM16.5 12C16.5 10.23 15.48 8.71 14 7.97V16.02C15.48 15.29 16.5 13.77 16.5 12Z");
    private static readonly Geometry VolumeMuteData =
        Geometry.Parse("M16.5 12C16.5 10.23 15.48 8.71 14 7.97V10.18L16.45 12.63C16.48 12.43 16.5 12.22 16.5 12ZM19 12C19 12.94 18.8 13.82 18.46 14.64L19.97 16.15C20.63 14.91 21 13.5 21 12C21 7.72 18.01 4.14 14 3.23V5.29C16.89 6.15 19 8.83 19 12ZM4.27 3L3 4.27L7.73 9H3V15H7L12 20V14.27L16.25 18.52C15.58 19.04 14.83 19.45 14 19.71V21.77C15.38 21.45 16.63 20.82 17.68 19.96L19.73 22.01L21 20.74L12 11.73L4.27 3ZM12 4L9.91 6.09L12 8.18V4Z");

    // --- Derived properties ---

    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    public string TimeText => $"{PositionText} / {DurationText}";

    public Geometry PlayPauseIcon => IsPaused ? PlayIconData : PauseIconData;
    public Geometry VolumeIconPath => IsMuted || Volume <= 0 ? VolumeMuteData : Volume < 50 ? VolumeLowData : VolumeHighData;
    public string MuteTooltip => IsMuted ? "取消静音" : "静音";

    public MainWindowViewModel(IMpvPlayer player, IDialogService dialogService, IDispatcherService dispatcher)
    {
        _player = player;
        _dialogService = dialogService;
        _dispatcher = dispatcher;

        _player.PositionChanged += OnPlayerPositionChanged;
        _player.DurationChanged += dur => Duration = dur;
        _player.PauseChanged += paused => IsPaused = paused;
        _player.VolumeChanged += vol => Volume = vol;
        _player.MuteChanged += muted => IsMuted = muted;
        _player.EofReached += eof => { if (eof) IsPaused = true; };
        _player.ErrorOccurred += OnPlayerError;
    }

    private void OnPlayerPositionChanged(double pos)
    {
        if (!_isSeeking)
            Position = pos;
    }

    private void OnPlayerError(string message)
    {
        ErrorMessage = message;
        HasError = true;

        _dispatcher.RunOnce(() =>
        {
            HasError = false;
            ErrorMessage = null;
        }, TimeSpan.FromSeconds(5));
    }

    // --- Commands ---

    [RelayCommand]
    private async Task OpenFile()
    {
        var path = await _dialogService.OpenVideoFileAsync();
        if (path == null) return;

        ClearError();
        _player.LoadFile(path);
        _player.Play();
        HasFile = true;
        Position = 0;
        Duration = 0;
        IsPaused = false;
    }

    [RelayCommand]
    private void PlayPause() => _player.TogglePause();

    [RelayCommand]
    private void Mute() => _player.ToggleMute();

    [RelayCommand]
    private void DismissError() => ClearError();

    // --- Seek handling ---

    public void BeginSeek()
    {
        _isSeeking = true;
        _isDragging = true;
    }

    public void EndSeek()
    {
        _isDragging = false;
        if (HasFile)
            _player.Seek(Position);

        _dispatcher.RunOnce(() => _isSeeking = false, TimeSpan.FromMilliseconds(300));
    }

    public void SeekDrag(double newValue)
    {
        if (_isDragging && _isSeeking && HasFile)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastSeekTime).TotalMilliseconds >= SeekThrottleMs)
            {
                _lastSeekTime = now;
                _player.SeekFast(newValue);
            }
        }
    }

    public void SetVolume(double vol) => _player.SetVolume(vol);

    // --- Keyboard handling ---

    public bool HandleKeyDown(string key)
    {
        if (!HasFile && key != "O") return false;

        switch (key)
        {
            case "Space":
                _player.TogglePause();
                return true;
            case "Left":
                _player.SeekRelative(-5);
                return true;
            case "Right":
                _player.SeekRelative(5);
                return true;
            case "Up":
                _player.SetVolume(Math.Min(Volume + 5, 100));
                return true;
            case "Down":
                _player.SetVolume(Math.Max(Volume - 5, 0));
                return true;
            case "M":
                _player.ToggleMute();
                return true;
            default:
                return false;
        }
    }

    // --- Property change notifications for derived props ---

    partial void OnPositionChanged(double value)
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(VolumeIconPath));
        OnPropertyChanged(nameof(MuteTooltip));
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(VolumeIconPath));
        OnPropertyChanged(nameof(MuteTooltip));
    }

    partial void OnIsPausedChanged(bool value)
    {
        PlayPauseText = value ? "▶ 播放" : "⏸ 暂停";
        OnPropertyChanged(nameof(PlayPauseIcon));
    }

    // --- Helpers ---

    private void ClearError()
    {
        HasError = false;
        ErrorMessage = null;
    }

    private static string FormatTime(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || totalSeconds < 0)
            totalSeconds = 0;
        var ts = TimeSpan.FromSeconds(totalSeconds);
        return ts.TotalHours >= 1
            ? ts.ToString(@"h\:mm\:ss")
            : ts.ToString(@"mm\:ss");
    }
}
