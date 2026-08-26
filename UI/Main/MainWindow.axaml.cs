using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaAppMPV.Infrastructure.Mpv;
using AvaloniaAppMPV.Infrastructure.Settings;
using AvaloniaAppMPV.UI.Dialogs;

namespace AvaloniaAppMPV.UI.Main;

/// <summary>
/// 主窗口的视图层行为。
/// 这里保留窗口级交互：控制条自动隐藏、滑块指针事件、窗口关闭时的资源释放等。
/// </summary>
public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private MpvPlayerService? _playerService;

    private DispatcherTimer? _hideTimer;
    private bool _cleanupCompleted;
    private bool _isClosingAfterCleanup;
    private bool _isPointerOverControlBar;
    private bool _isAdjustingSeek;
    private bool _isAdjustingVolume;
    private Point _lastMousePosition;
    private static readonly Cursor NoneCursor = new(StandardCursorType.None);

    public MainWindow()
    {
        InitializeComponent();

        SeekSlider.AddHandler(PointerPressedEvent, OnSeekSliderPressed, RoutingStrategies.Tunnel);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekSliderReleased, RoutingStrategies.Tunnel);
        VolumeSlider.AddHandler(PointerPressedEvent, OnVolumeSliderPressed, RoutingStrategies.Tunnel);
        VolumeSlider.AddHandler(PointerReleasedEvent, OnVolumeSliderReleased, RoutingStrategies.Tunnel);

        InitializeAutoHide();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public void AttachPlayerService(MpvPlayerService playerService)
    {
        _playerService = playerService;
        VideoView.AttachRenderHost(playerService);
    }

    // --- 控制条自动隐藏 ---

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
        ControlBarRoot.IsHitTestVisible = true;
        ControlBarRoot.Opacity = 1.0;
        Cursor = Cursor.Default;
        ResetHideTimer();
    }

    private void HideControlBar()
    {
        if (_isPointerOverControlBar || ViewModel.IsPaused || !ViewModel.HasFile)
            return;

        ControlBarRoot.Opacity = 0.0;
        ControlBarRoot.IsHitTestVisible = false;
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
        {
            return;
        }

        _lastMousePosition = pos;
        ShowControlBar();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && e.Source is UI.Controls.MpvVideoView)
            ToggleFullScreen();
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var store = App.Services.GetService(typeof(PlayerSettingsStore)) as PlayerSettingsStore;
        if (store != null)
            await new SettingsWindow(store).ShowDialog(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var file = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        var path = file?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            ViewModel.OpenDroppedFile(path);
        e.Handled = true;
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

    // --- 进度条指针事件转交给 ViewModel ---

    private void OnSeekSliderPressed(object? sender, PointerPressedEventArgs e)
    {
        _isAdjustingSeek = true;
        ViewModel.BeginSeek();
    }

    private void OnSeekSliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishSeekAdjustment();
    }

    private void OnSeekSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_isAdjustingSeek)
            ViewModel.SeekDrag(e.NewValue);
    }

    private void OnSeekSliderCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishSeekAdjustment();
    }

    private void OnSeekSliderLostFocus(object? sender, RoutedEventArgs e)
    {
        FinishSeekAdjustment();
    }

    private void OnSeekSliderKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
                ViewModel.SeekTo(ViewModel.Position - 5);
                e.Handled = true;
                break;
            case Key.Right:
                ViewModel.SeekTo(ViewModel.Position + 5);
                e.Handled = true;
                break;
            case Key.Home:
                ViewModel.SeekTo(0);
                e.Handled = true;
                break;
            case Key.End:
                ViewModel.SeekTo(ViewModel.Duration);
                e.Handled = true;
                break;
        }

        if (e.Handled)
            ShowControlBar();
    }

    // --- 音量变化转交给 ViewModel ---

    private void OnVolumeSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_isAdjustingVolume)
            return;

        ViewModel.SetVolume(e.NewValue);
    }

    private void OnVolumeSliderPressed(object? sender, PointerPressedEventArgs e)
    {
        _isAdjustingVolume = true;
    }

    private void OnVolumeSliderReleased(object? sender, PointerReleasedEventArgs e)
    {
        FinishVolumeAdjustment();
    }

    private void OnVolumeSliderCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        FinishVolumeAdjustment();
    }

    private void OnVolumeSliderLostFocus(object? sender, RoutedEventArgs e)
    {
        FinishVolumeAdjustment();
    }

    private void OnVolumeSliderKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left:
            case Key.Down:
                ViewModel.SetVolume(ViewModel.Volume - 5);
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Up:
                ViewModel.SetVolume(ViewModel.Volume + 5);
                e.Handled = true;
                break;
            case Key.Home:
                ViewModel.SetVolume(0);
                e.Handled = true;
                break;
            case Key.End:
                ViewModel.SetVolume(100);
                e.Handled = true;
                break;
        }

        if (e.Handled)
            ShowControlBar();
    }

    private void FinishSeekAdjustment()
    {
        if (!_isAdjustingSeek)
            return;

        _isAdjustingSeek = false;
        ViewModel.EndSeek();
    }

    private void FinishVolumeAdjustment()
    {
        if (!_isAdjustingVolume)
            return;

        _isAdjustingVolume = false;
        ViewModel.SetVolume(VolumeSlider.Value);
    }

    // --- 视频信息弹窗 ---

    private async void OnShowVideoInfoClick(object? sender, RoutedEventArgs e)
    {
        if (_playerService == null)
            return;

        var info = _playerService.GetVideoInfo();
        if (info == null)
            return;

        var window = new VideoInfoWindow();
        window.SetVideoInfo(info);
        await window.ShowDialog(this);
    }

    // --- 键盘事件转交给 ViewModel ---

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && WindowState == WindowState.FullScreen)
        {
            ToggleFullScreen();
            e.Handled = true;
            return;
        }

        // 键盘有交互时也让控制条重新出现，避免“盲操”。
        ShowControlBar();
        e.Handled = ViewModel.HandleKeyDown(e.Key.ToString());
    }

    private void ToggleFullScreen()
    {
        WindowState = WindowState == WindowState.FullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_cleanupCompleted)
        {
            base.OnClosing(e);
            return;
        }

        if (_isClosingAfterCleanup)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        _isClosingAfterCleanup = true;

        _hideTimer?.Stop();
        _hideTimer = null;
        ViewModel.SaveState();

        // 先释放 render context，再销毁 mpv core，避免原生层访问失效资源。
        VideoView.CleanupRenderContext();

        try
        {
            if (_playerService is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (_playerService is IDisposable disposable)
                disposable.Dispose();
        }
        finally
        {
            _cleanupCompleted = true;
            _isClosingAfterCleanup = false;
            Dispatcher.UIThread.Post(Close);
        }
    }
}
