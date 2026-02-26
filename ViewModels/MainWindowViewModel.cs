using System;
using System.Threading.Tasks;
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

    // --- Derived properties ---

    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    public string TimeText => $"{PositionText} / {DurationText}";
    public string VolumeIcon => IsMuted || Volume <= 0 ? "🔇" : Volume < 50 ? "🔉" : "🔊";

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
        OnPropertyChanged(nameof(VolumeIcon));
    }

    partial void OnIsMutedChanged(bool value)
    {
        OnPropertyChanged(nameof(VolumeIcon));
    }

    partial void OnIsPausedChanged(bool value)
    {
        PlayPauseText = value ? "▶ 播放" : "⏸ 暂停";
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
