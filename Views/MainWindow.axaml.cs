using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaAppMPV.Services;
using AvaloniaAppMPV.ViewModels;

namespace AvaloniaAppMPV.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private MpvPlayerService? _playerService;

    private DispatcherTimer? _hideTimer;
    private bool _isPointerOverControlBar;
    private Point _lastMousePosition;
    private static readonly Cursor NoneCursor = new(StandardCursorType.None);

    public MainWindow()
    {
        InitializeComponent();

        SeekSlider.AddHandler(PointerPressedEvent, OnSeekSliderPressed, RoutingStrategies.Tunnel);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekSliderReleased, RoutingStrategies.Tunnel);

        InitializeAutoHide();
    }

    public void AttachPlayerService(MpvPlayerService playerService)
    {
        _playerService = playerService;
        VideoView.AttachPlayerService(playerService);
    }

    // --- Auto-hide control bar ---

    private void InitializeAutoHide()
    {
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            HideControlBar();
        };
        _hideTimer.Start();
    }

    private void ShowControlBar()
    {
        ViewModel.IsControlBarVisible = true;
        ViewModel.ControlBarOpacity = 1.0;
        Cursor = Cursor.Default;
        ResetHideTimer();
    }

    private void HideControlBar()
    {
        if (_isPointerOverControlBar || ViewModel.IsPaused || !ViewModel.HasFile)
            return;

        ViewModel.ControlBarOpacity = 0.0;
        ViewModel.IsControlBarVisible = false;
        Cursor = NoneCursor;
    }

    private void ResetHideTimer()
    {
        _hideTimer?.Stop();
        _hideTimer?.Start();
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (Math.Abs(pos.X - _lastMousePosition.X) < 2 &&
            Math.Abs(pos.Y - _lastMousePosition.Y) < 2)
            return;

        _lastMousePosition = pos;
        ShowControlBar();
    }

    private void OnControlBarPointerEntered(object? sender, PointerEventArgs e)
    {
        _isPointerOverControlBar = true;
        ShowControlBar();
    }

    private void OnControlBarPointerExited(object? sender, PointerEventArgs e)
    {
        _isPointerOverControlBar = false;
        ResetHideTimer();
    }

    // --- Seek slider pointer events → ViewModel ---

    private void OnSeekSliderPressed(object? sender, PointerPressedEventArgs e)
    {
        ViewModel.BeginSeek();
    }

    private void OnSeekSliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        ViewModel.EndSeek();
    }

    private void OnSeekSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SeekDrag(e.NewValue);
    }

    // --- Volume slider → ViewModel ---

    private void OnVolumeSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        ViewModel.SetVolume(e.NewValue);
    }

    // --- Video info ---

    private async void OnShowVideoInfoClick(object? sender, RoutedEventArgs e)
    {
        if (_playerService == null) return;
        var info = _playerService.GetVideoInfo();
        if (info == null) return;

        var window = new VideoInfoWindow();
        window.SetVideoInfo(info);
        await window.ShowDialog(this);
    }

    // --- Keyboard → ViewModel ---

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Any key press shows the control bar briefly
        ShowControlBar();
        e.Handled = ViewModel.HandleKeyDown(e.Key.ToString());
    }

    // --- Cleanup ---

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _hideTimer?.Stop();
        _hideTimer = null;

        // 1. Free the render context FIRST — this stops the update callback
        //    and releases the OpenGL render context before the mpv core is destroyed.
        VideoView.CleanupRenderContext();

        // 2. Now safe to destroy the mpv core
        if (_playerService is IDisposable disposable)
            disposable.Dispose();

        base.OnClosing(e);
    }
}
