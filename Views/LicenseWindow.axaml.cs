using System.Diagnostics;
using Avalonia.Controls;
using MOROVelocityX.ViewModels;

namespace MOROVelocityX.Views;

public partial class LicenseWindow : Window
{
    public LicenseWindow()
    {
        InitializeComponent();

        var telegram = this.FindControl<TextBlock>("TelegramLink");
        if (telegram != null)
        {
            telegram.PointerPressed += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://t.me/Unro0")
                    {
                        UseShellExecute = true
                    });
                }
                catch { }
            };
        }
    }

    public LicenseWindow(LicenseViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.ExitRequested += OnExitRequested;
    }

    private void OnExitRequested(object? sender, System.EventArgs e)
    {
        Close(false);
    }
}
