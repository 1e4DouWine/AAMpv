using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using AvaloniaAppMPV.Models;
using AvaloniaAppMPV.Services;
using AvaloniaAppMPV.ViewModels;
using AvaloniaAppMPV.Views;
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
            DisableAvaloniaDataAnnotationValidation();

            var services = new ServiceCollection();

            // Services
            services.AddSingleton<IDispatcherService, AvaloniaDispatcherService>();
            services.AddSingleton<IDialogService, AvaloniaDialogService>();
            services.AddSingleton<MpvPlayerService>();
            services.AddSingleton<IMpvPlayer>(sp => sp.GetRequiredService<MpvPlayerService>());

            // ViewModels
            services.AddSingleton<MainWindowViewModel>();

            Services = services.BuildServiceProvider();

            var playerService = Services.GetRequiredService<MpvPlayerService>();
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();

            var mainWindow = new MainWindow { DataContext = viewModel };
            mainWindow.AttachPlayerService(playerService);

            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
