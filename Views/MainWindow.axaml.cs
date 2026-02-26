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

    // --- Keyboard → ViewModel ---

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = ViewModel.HandleKeyDown(e.Key.ToString());
    }

    // --- Cleanup ---

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (_playerService is IDisposable disposable)
            disposable.Dispose();
        base.OnClosing(e);
    }
}
