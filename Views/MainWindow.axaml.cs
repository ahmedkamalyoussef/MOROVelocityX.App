using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using MOROVelocityX.ViewModels;

namespace MOROVelocityX.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        PointerPressed += OnPointerPressed;
        PointerReleased += OnPointerReleased;

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

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var key = e.Key.ToString();
            viewModel.OnKeyPressed(key);
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var key = e.Key.ToString();
            viewModel.OnKeyReleased(key);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var mouseButton = e.GetCurrentPoint(this).Properties.PointerUpdateKind.ToString();
            var keyName = mouseButton switch
            {
                "LeftButtonPressed" => "Mouse1",
                "RightButtonPressed" => "Mouse2",
                "MiddleButtonPressed" => "Mouse3",
                _ => mouseButton
            };
            viewModel.OnKeyPressed(keyName);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            var mouseButton = e.GetCurrentPoint(this).Properties.PointerUpdateKind.ToString();
            var keyName = mouseButton switch
            {
                "LeftButtonPressed" => "Mouse1",
                "RightButtonPressed" => "Mouse2",
                "MiddleButtonPressed" => "Mouse3",
                _ => mouseButton
            };
            viewModel.OnKeyReleased(keyName);
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Stop();
        }
    }
}