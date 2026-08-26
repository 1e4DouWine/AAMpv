using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Avalonia;
using AvaloniaAppMPV.Infrastructure.Settings;
using AvaloniaAppMPV.UI.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AvaloniaAppMPV.UI.Main;

/// <summary>
/// 主窗口的状态与交互逻辑。
/// 这里主要处理播放器状态、按钮命令、进度拖动和快捷键语义。
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private const string DefaultTitle = "MPV Player";

    private readonly IMpvPlayer _player;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcher;
    private readonly PlayerSettingsStore _settingsStore;

    private bool _isSeeking;
    private bool _isDragging;
    private bool _playRequestedAfterLoad;
    private double _resumePosition;
    private string? _currentPath;
    private DateTime _lastSeekTime = DateTime.MinValue;
    private int _lastDisplayedPositionSecond = -1;
    private int _errorVersion;
    private const double SeekThrottleMs = 100;

    // --- 可绑定状态 ---

    [ObservableProperty]
    private string _title = DefaultTitle;

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

    [ObservableProperty]
    private PlaybackState _playbackState = PlaybackState.Unloaded;

    [ObservableProperty]
    private double _speed = 1.0;

    [ObservableProperty]
    private string? _hardwareDecode;

    // --- 派生显示属性 ---

    public string PositionText => FormatTime(Position);
    public string DurationText => FormatTime(Duration);
    public string TimeText => $"{PositionText} / {DurationText}";

    public MainWindowViewModel(
        IMpvPlayer player,
        IDialogService dialogService,
        IDispatcherService dispatcher,
        PlayerSettingsStore settingsStore)
    {
        _player = player;
        _dialogService = dialogService;
        _dispatcher = dispatcher;
        _settingsStore = settingsStore;
        Volume = settingsStore.Document.Player.Volume;
        IsMuted = settingsStore.Document.Player.IsMuted;
        Speed = settingsStore.Document.Player.DefaultSpeed;

        _player.FileLoaded += OnPlayerFileLoaded;
        _player.PositionChanged += OnPlayerPositionChanged;
        _player.DurationChanged += dur => Duration = dur;
        _player.PauseChanged += paused => IsPaused = paused;
        _player.VolumeChanged += vol => Volume = vol;
        _player.MuteChanged += muted => IsMuted = muted;
        _player.EofReached += eof =>
        {
            if (!eof)
                return;
            IsPaused = true;
            Position = 0;
            SaveCurrentPosition();
        };
        _player.ErrorOccurred += OnPlayerError;
        _player.PlaybackStateChanged += state => PlaybackState = state;
        _player.SpeedChanged += speed => Speed = speed;
        _player.HardwareDecodeChanged += value => HardwareDecode = value;
        _player.WarningOccurred += OnPlayerWarning;
    }

    private void OnPlayerPositionChanged(double pos)
    {
        if (!_isSeeking)
            Position = pos;
        if (_currentPath != null && _settingsStore.Document.Player.RememberPlaybackPosition)
            _settingsStore.UpdatePosition(_currentPath, pos);
    }

    private void OnPlayerFileLoaded(string? fileName)
    {
        HasFile = true;
        Title = string.IsNullOrWhiteSpace(fileName)
            ? DefaultTitle
            : $"{Path.GetFileName(fileName)} - {DefaultTitle}";

        _currentPath = fileName;
        if (!string.IsNullOrWhiteSpace(fileName))
            _settingsStore.AddRecentFile(fileName);
        _resumePosition = _settingsStore.Document.Player.RememberPlaybackPosition && fileName != null
            ? _settingsStore.GetPosition(fileName)
            : 0;

        if (_resumePosition > 0)
            _player.Seek(_resumePosition);

        if (!_playRequestedAfterLoad)
            return;

        _playRequestedAfterLoad = false;
        _player.Play();
    }

    private void OnPlayerError(string message)
    {
        var errorVersion = Interlocked.Increment(ref _errorVersion);
        ErrorMessage = message;
        HasError = true;

        _dispatcher.RunOnce(() =>
        {
            if (errorVersion != Volatile.Read(ref _errorVersion))
                return;

            HasError = false;
            ErrorMessage = null;
        }, TimeSpan.FromSeconds(5));
    }

    private void OnPlayerWarning(string message) => OnPlayerError(message);

    [RelayCommand]
    private async Task OpenFile()
    {
        var path = await _dialogService.OpenVideoFileAsync();
        if (path == null)
            return;

        SaveCurrentPosition();
        ClearError();
        _playRequestedAfterLoad = true;
        HasFile = false;
        _player.LoadFile(path);
        Position = 0;
        Duration = 0;
        Title = DefaultTitle;
    }

    [RelayCommand]
    private void PlayPause() => _player.TogglePause();

    [RelayCommand]
    private void Screenshot()
    {
        try
        {
            _player.Screenshot(BuildScreenshotPath());
        }
        catch (Exception ex)
        {
            OnPlayerError($"截图失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void IncreaseSpeed() => SetSpeed(Speed + 0.25);

    [RelayCommand]
    private void DecreaseSpeed() => SetSpeed(Speed - 0.25);

    [RelayCommand]
    private void ResetSpeed() => SetSpeed(1.0);

    [RelayCommand]
    private void Mute() => _player.ToggleMute();

    [RelayCommand]
    private void DismissError() => ClearError();

    // --- 拖动进度条 ---

    public void BeginSeek()
    {
        if (!HasFile)
            return;

        _isSeeking = true;
        _isDragging = true;
    }

    public void EndSeek()
    {
        if (!_isSeeking)
            return;

        _isDragging = false;
        if (HasFile)
        {
            var clamped = ClampPosition(Position);
            Position = clamped;
            _player.Seek(clamped);
        }

        // 拖动结束后稍微延迟再恢复自动同步，避免和 mpv 回推的位置抖动。
        _dispatcher.RunOnce(() => _isSeeking = false, TimeSpan.FromMilliseconds(300));
    }

    public void SeekDrag(double newValue)
    {
        if (!_isDragging || !_isSeeking || !HasFile)
            return;

        var now = DateTime.UtcNow;
        if ((now - _lastSeekTime).TotalMilliseconds < SeekThrottleMs)
            return;

        _lastSeekTime = now;
        _player.SeekFast(ClampPosition(newValue));
    }

    public void SeekTo(double positionSeconds)
    {
        if (!HasFile)
            return;

        var clamped = ClampPosition(positionSeconds);
        Position = clamped;
        _player.Seek(clamped);
    }

    public void SetVolume(double vol)
    {
        var clamped = Math.Clamp(vol, 0, 100);
        Volume = clamped;
        _player.SetVolume(clamped);
        _settingsStore.Document.Player.Volume = clamped;
    }

    // --- 键盘快捷键 ---

    public bool HandleKeyDown(string key)
    {
        if (!HasFile && key != "O")
            return false;

        switch (key)
        {
            case "O":
                OpenFileCommand.Execute(null);
                return true;
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
                SetVolume(Volume + 5);
                return true;
            case "Down":
                SetVolume(Volume - 5);
                return true;
            case "M":
                _player.ToggleMute();
                return true;
            case "S":
                ScreenshotCommand.Execute(null);
                return true;
            case "R":
                ResetSpeedCommand.Execute(null);
                return true;
            case "OemOpenBrackets":
            case "[":
                DecreaseSpeedCommand.Execute(null);
                return true;
            case "OemCloseBrackets":
            case "]":
                IncreaseSpeedCommand.Execute(null);
                return true;
            default:
                return false;
        }
    }

    // --- 派生属性变更通知 ---

    partial void OnPositionChanged(double value)
    {
        var displaySecond = GetDisplaySecond(value);
        if (displaySecond == _lastDisplayedPositionSecond)
            return;

        _lastDisplayedPositionSecond = displaySecond;
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(TimeText));
    }

    partial void OnDurationChanged(double value)
    {
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(TimeText));
    }

    private void ClearError()
    {
        Interlocked.Increment(ref _errorVersion);
        HasError = false;
        ErrorMessage = null;
    }

    public void SaveState()
    {
        SaveCurrentPosition();
        _settingsStore.Document.Player.IsMuted = IsMuted;
        _settingsStore.Document.Player.DefaultSpeed = Speed;
        _settingsStore.Document.Player.Volume = Volume;
        _settingsStore.Save();
    }

    public void OpenDroppedFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        SaveCurrentPosition();
        ClearError();
        _playRequestedAfterLoad = true;
        HasFile = false;
        Position = 0;
        Duration = 0;
        Title = DefaultTitle;
        _player.LoadFile(path);
    }

    private void SetSpeed(double speed)
    {
        var normalized = Math.Clamp(speed, 0.5, 2.0);
        _player.SetSpeed(normalized);
        Speed = normalized;
        _settingsStore.Document.Player.DefaultSpeed = normalized;
    }

    private void SaveCurrentPosition()
    {
        if (_currentPath != null && _settingsStore.Document.Player.RememberPlaybackPosition)
            _settingsStore.UpdatePosition(_currentPath, Position);
    }

    private string BuildScreenshotPath()
    {
        var directory = string.IsNullOrWhiteSpace(_settingsStore.Document.Player.ScreenshotDirectory)
            ? _settingsStore.ScreenshotDirectory
            : _settingsStore.Document.Player.ScreenshotDirectory;
        Directory.CreateDirectory(directory);
        var name = $"mpv-shot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
        return Path.Combine(directory, name);
    }

    private double ClampPosition(double positionSeconds)
    {
        if (double.IsNaN(positionSeconds) || positionSeconds < 0)
            return 0;

        if (Duration <= 0 || double.IsNaN(Duration))
            return positionSeconds;

        return Math.Min(positionSeconds, Duration);
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

    private static int GetDisplaySecond(double totalSeconds)
    {
        if (double.IsNaN(totalSeconds) || totalSeconds <= 0)
            return 0;

        return (int)Math.Floor(totalSeconds);
    }
}
