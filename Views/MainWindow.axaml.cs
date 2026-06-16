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
}