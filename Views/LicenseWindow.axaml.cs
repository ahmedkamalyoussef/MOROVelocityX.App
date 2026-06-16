using Avalonia.Controls;
using Avalonia.Interactivity;
using MOROVelocityX.ViewModels;

namespace MOROVelocityX.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();
    }

    public LicenseWindow(LicenseViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.ActivationSucceeded += OnActivationSucceeded;
        viewModel.ExitRequested += OnExitRequested;
    }

    private void OnActivationSucceeded(object? sender, System.EventArgs e)
    {
        Close(true);
    }

    private void OnExitRequested(object? sender, System.EventArgs e)
    {
        Close(false);
    }
}
