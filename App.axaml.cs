using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaAppMPV.Core.Playback;
using AvaloniaAppMPV.Infrastructure.Avalonia;
using AvaloniaAppMPV.Infrastructure.Mpv;
using AvaloniaAppMPV.Infrastructure.Settings;
using AvaloniaAppMPV.UI.Main;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaAppMPV;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();

            // 基础设施服务
            services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
            services.AddSingleton<IDialogService, AvaloniaDialogService>();
            services.AddSingleton<PlayerSettingsStore>();

            // 播放核心
            services.AddSingleton<MpvPlayerService>();
            services.AddSingleton<IMpvPlayer>(sp => sp.GetRequiredService<MpvPlayerService>());

            // 主界面
            services.AddSingleton<MainWindowViewModel>();

            Services = services.BuildServiceProvider();

            var playerService = Services.GetRequiredService<MpvPlayerService>();
            var settingsStore = Services.GetRequiredService<PlayerSettingsStore>();
            playerService.Configure(settingsStore.Document.Player);
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow { DataContext = viewModel };
            mainWindow.AttachPlayerService(playerService);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
