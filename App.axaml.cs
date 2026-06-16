using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using MOROVelocityX.ViewModels;
using MOROVelocityX.Views;
using MOROVelocityX.Services;

namespace MOROVelocityX;

public partial class App : Application
{
    private GlobalHotkeyService? _globalHotkeyService;
    private MacroService? _macroService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            _globalHotkeyService = new GlobalHotkeyService();
            _macroService = new MacroService();
            
            var mainWindow = new MainWindow();
            var viewModel = new MainWindowViewModel(_globalHotkeyService, _macroService);
            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;
            
            // Initialize global hotkey service with window handle after window is loaded
            mainWindow.Opened += (sender, e) =>
            {
                var handle = mainWindow.TryGetPlatformHandle()?.Handle;
                if (handle != null)
                {
                    _globalHotkeyService?.Initialize(handle.Value);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}