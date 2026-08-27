using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Settings;

namespace AvaloniaAppMPV.UI.Dialogs;

public partial class SettingsWindow : Window
{
    private static readonly double[] SpeedOptions = [0.5, 1.0, 1.25, 1.5, 2.0];
    private readonly PlayerSettingsStore _store;

    public SettingsWindow() : this(new PlayerSettingsStore())
    {
    }

    public SettingsWindow(PlayerSettingsStore store)
    {
        _store = store;
        InitializeComponent();
        LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        var settings = _store.Document.Player;
        HardwareDecodeComboBox.SelectedIndex = (int)settings.HardwareDecode;
        RenderBackendComboBox.SelectedIndex = (int)settings.RenderBackend;
        DefaultSpeedComboBox.SelectedIndex = FindClosestSpeedIndex(settings.DefaultSpeed);
        RememberPositionCheckBox.IsChecked = settings.RememberPlaybackPosition;
        UseCustomConfigCheckBox.IsChecked = settings.UseCustomMpvConfig;
        ConfigPathTextBox.Text = settings.MpvConfigPath ?? _store.MpvConfigPath;
        ScreenshotDirectoryTextBox.Text = settings.ScreenshotDirectory;
    }

    private void OnOpenConfigClick(object? sender, RoutedEventArgs e)
    {
        _store.EnsureDefaultConfigExists();
        LaunchPath(_store.MpvConfigPath);
    }

    private void OnOpenConfigDirectoryClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_store.AppDataDirectory);
        LaunchPath(_store.AppDataDirectory);
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var settings = _store.Document.Player;
        settings.HardwareDecode = (HardwareDecodeMode)Math.Clamp(HardwareDecodeComboBox.SelectedIndex, 0, 9);
        settings.RenderBackend = (RenderBackendKind)Math.Clamp(RenderBackendComboBox.SelectedIndex, 0, 3);
        settings.DefaultSpeed = SpeedOptions[Math.Clamp(DefaultSpeedComboBox.SelectedIndex, 0, SpeedOptions.Length - 1)];
        settings.RememberPlaybackPosition = RememberPositionCheckBox.IsChecked == true;
        settings.UseCustomMpvConfig = UseCustomConfigCheckBox.IsChecked == true;
        settings.MpvConfigPath = ConfigPathTextBox.Text;
        settings.ScreenshotDirectory = string.IsNullOrWhiteSpace(ScreenshotDirectoryTextBox.Text)
            ? _store.ScreenshotDirectory
            : ScreenshotDirectoryTextBox.Text.Trim();
        _store.Save();
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private static int FindClosestSpeedIndex(double speed)
    {
        if (!double.IsFinite(speed))
            return 1;

        var closestIndex = 0;
        var closestDistance = Math.Abs(speed - SpeedOptions[0]);
        for (var i = 1; i < SpeedOptions.Length; i++)
        {
            var distance = Math.Abs(speed - SpeedOptions[i]);
            if (distance < closestDistance)
            {
                closestIndex = i;
                closestDistance = distance;
            }
        }

        return closestIndex;
    }

    private static void LaunchPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }
}
