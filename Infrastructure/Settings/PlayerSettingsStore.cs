using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AvaloniaAppMPV.Core.Playback;

namespace AvaloniaAppMPV.Infrastructure.Settings;

public sealed class PlayerSettingsStore
{
    private const int MaxRecentFiles = 20;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public string AppDataDirectory { get; }
    public string SettingsPath { get; }
    public string MpvConfigPath { get; }
    public string ScreenshotDirectory { get; }
    public PlayerSettingsDocument Document { get; private set; }

    public PlayerSettingsStore()
    {
        AppDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AvaloniaAppMPV");
        SettingsPath = Path.Combine(AppDataDirectory, "settings.json");
        MpvConfigPath = Path.Combine(AppDataDirectory, "mpv.conf");
        ScreenshotDirectory = Path.Combine(AppDataDirectory, "screenshots");
        Document = LoadDocument();

        if (string.IsNullOrWhiteSpace(Document.Player.MpvConfigPath))
            Document.Player.MpvConfigPath = MpvConfigPath;
        if (string.IsNullOrWhiteSpace(Document.Player.ScreenshotDirectory))
            Document.Player.ScreenshotDirectory = ScreenshotDirectory;
    }

    public void AddRecentFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = Document.RecentFiles.FirstOrDefault(x =>
            string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LastOpenedUtc = DateTime.UtcNow;
            return;
        }

        Document.RecentFiles.Insert(0, new RecentMediaEntry
        {
            Path = fullPath,
            LastOpenedUtc = DateTime.UtcNow,
        });
        TrimRecentFiles();
    }

    public double GetPosition(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Document.RecentFiles.FirstOrDefault(x =>
            string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase))?.Position ?? 0;
    }

    public void UpdatePosition(string path, double position)
    {
        var fullPath = Path.GetFullPath(path);
        var item = Document.RecentFiles.FirstOrDefault(x =>
            string.Equals(x.Path, fullPath, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            AddRecentFile(fullPath);
            item = Document.RecentFiles[0];
        }

        item.Position = double.IsFinite(position) && position >= 0 ? position : 0;
        item.LastOpenedUtc = DateTime.UtcNow;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppDataDirectory);
        TrimRecentFiles();
        var tempPath = SettingsPath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(Document, _jsonOptions));
        File.Move(tempPath, SettingsPath, true);
    }

    public void EnsureDefaultConfigExists()
    {
        Directory.CreateDirectory(AppDataDirectory);
        if (!File.Exists(MpvConfigPath))
        {
            File.WriteAllText(MpvConfigPath,
                "# AvaloniaAppMPV custom MPV options\n" +
                "# Do not set vo, gpu-api, gpu-context, wid or window-id here.\n");
        }
    }

    private PlayerSettingsDocument LoadDocument()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new PlayerSettingsDocument();

            var document = JsonSerializer.Deserialize<PlayerSettingsDocument>(File.ReadAllText(SettingsPath));
            if (document?.Player == null)
                return new PlayerSettingsDocument();
            document.RecentFiles ??= [];
            return document;
        }
        catch
        {
            return new PlayerSettingsDocument();
        }
    }

    private void TrimRecentFiles()
    {
        Document.RecentFiles = Document.RecentFiles
            .Where(x => !string.IsNullOrWhiteSpace(x.Path))
            .OrderByDescending(x => x.LastOpenedUtc)
            .Take(MaxRecentFiles)
            .ToList();
    }
}
