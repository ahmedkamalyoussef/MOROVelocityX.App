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
    private IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            
            DisableAvaloniaDataAnnotationValidation();
            
            _globalHotkeyService = new GlobalHotkeyService();
            _macroService = new MacroService();
            
            var mainWindow = new MainWindow();
            var viewModel = new MainWindowViewModel(_globalHotkeyService, _macroService);
            mainWindow.DataContext = viewModel;
            desktop.MainWindow = mainWindow;
            
            mainWindow.Opened += (sender, e) =>
            {
                var handle = mainWindow.TryGetPlatformHandle()?.Handle;
                if (handle != null)
                {
                    _globalHotkeyService?.Initialize(handle.Value);
                }
            };
            
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        try
        {
            if (_desktop?.MainWindow?.DataContext is MainWindowViewModel vm)
            {
                vm.Stop();
            }
        }
        catch { }
        
        _macroService?.Dispose();
        _globalHotkeyService?.Dispose();
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