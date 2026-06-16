using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using MOROVelocityX.Models;
using MOROVelocityX.Services;
using MOROVelocityX.ViewModels;
using MOROVelocityX.Views;

namespace MOROVelocityX;

public partial class App : Application
{
    private GlobalHotkeyService? _globalHotkeyService;
    private MacroService? _macroService;
    private StatsService? _statsService;
    private OverlayViewModel? _overlayViewModel;
    private OverlayWindow? _overlayWindow;
    private LicenseService? _licenseService;
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

            _licenseService = new LicenseService(
                new HardwareFingerprintService(),
                new EncryptionService());

            var validation = _licenseService.ValidateOnStartup();
            if (validation.State != LicenseState.Active)
            {
                ShowLicenseWindow(desktop, validation);
            }
            else
            {
                LaunchMainApplication(desktop, validation);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowLicenseWindow(IClassicDesktopStyleApplicationLifetime desktop, LicenseValidationResult validation)
    {
        var viewModel = new LicenseViewModel(_licenseService!, validation);
        var licenseWindow = new LicenseWindow(viewModel);

        viewModel.ActivationSucceeded += (_, _) =>
        {
            var latest = _licenseService!.ValidateOnStartup();
            LaunchMainApplication(desktop, latest);
        };

        licenseWindow.Closed += (_, _) =>
        {
            if (_licenseService!.ValidateOnStartup().State != LicenseState.Active)
            {
                desktop.Shutdown();
            }
        };

        desktop.MainWindow = licenseWindow;
        licenseWindow.Show();
    }

    private void LaunchMainApplication(IClassicDesktopStyleApplicationLifetime desktop, LicenseValidationResult validation)
    {
        if (validation.State != LicenseState.Active || _licenseService == null)
        {
            desktop.Shutdown();
            return;
        }

        _statsService = new StatsService();
        _globalHotkeyService = new GlobalHotkeyService();
        _macroService = new MacroService
        {
            StatsService = _statsService
        };

        _overlayViewModel = new OverlayViewModel(_statsService);

        var mainWindow = new MainWindow();
        var viewModel = new MainWindowViewModel(
            _globalHotkeyService,
            _macroService,
            _overlayViewModel,
            _licenseService,
            validation);
        mainWindow.DataContext = viewModel;
        desktop.MainWindow = mainWindow;

        _overlayWindow = new OverlayWindow
        {
            DataContext = _overlayViewModel
        };
        _overlayWindow.Show();

        mainWindow.Opened += (_, _) =>
        {
            var handle = mainWindow.TryGetPlatformHandle()?.Handle;
            if (handle != null)
            {
                _globalHotkeyService.Initialize(handle.Value);
            }
        };

        mainWindow.Closed += (_, _) => desktop.Shutdown();
        desktop.ShutdownRequested += OnShutdownRequested;

        mainWindow.Show();
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

        _overlayWindow?.Close();
        _macroService?.Dispose();
        _globalHotkeyService?.Dispose();
        _statsService?.Dispose();
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
