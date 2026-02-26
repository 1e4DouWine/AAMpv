using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using AvaloniaAppMPV.Services;
using AvaloniaAppMPV.ViewModels;

namespace AvaloniaAppMPV.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext!;
    private MpvPlayerService? _playerService;

    public MainWindow()
    {
        InitializeComponent();

        SeekSlider.AddHandler(PointerPressedEvent, OnSeekSliderPressed, RoutingStrategies.Tunnel);
        SeekSlider.AddHandler(PointerReleasedEvent, OnSeekSliderReleased, RoutingStrategies.Tunnel);
    }

    public void AttachPlayerService(MpvPlayerService playerService)
    {
        _playerService = playerService;
        VideoView.AttachPlayerService(playerService);
    }

    // --- Open file: now a command on ViewModel, bound in XAML ---

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
        e.Handled = ViewModel.HandleKeyDown(e.Key.ToString());
    }

    // --- Cleanup ---

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 1. Free the render context FIRST — this stops the update callback
        //    and releases the OpenGL render context before the mpv core is destroyed.
        VideoView.CleanupRenderContext();

        // 2. Now safe to destroy the mpv core
        if (_playerService is IDisposable disposable)
            disposable.Dispose();

        base.OnClosing(e);
    }
}
