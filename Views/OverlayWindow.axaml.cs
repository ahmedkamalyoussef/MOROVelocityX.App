using System;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MOROVelocityX.ViewModels;

namespace MOROVelocityX.Views;

public partial class OverlayWindow : Window
{
    private readonly Timer _neonTimer;
    private double _hue;
    private Border? _neonBorder;
    private bool _isResizing;
    private PixelPoint _resizeStartPoint;
    private Size _resizeStartSize;

    public OverlayWindow()
    {
        InitializeComponent();
        _neonTimer = new Timer(30);
        _neonTimer.Elapsed += OnNeonTick;
        _neonTimer.AutoReset = true;

        PointerPressed += OnWindowPointerPressed;
        PointerMoved += OnWindowPointerMoved;
        PointerReleased += OnWindowPointerReleased;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _neonBorder = this.FindControl<Border>("NeonBorder");
        _neonTimer.Start();

        if (DataContext is OverlayViewModel vm)
        {
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(vm.Position))
                    UpdatePosition(vm.Position);
            };
            UpdatePosition(vm.Position);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _neonTimer.Stop();
        _neonTimer.Dispose();
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not OverlayViewModel vm) return;
        if (!vm.IsInteractive) return;

        var point = e.GetCurrentPoint(this);
        var pos = point.Position;

        double w = Width;
        double h = Height;

        bool nearBottomRight = pos.X >= w - 20 && pos.Y >= h - 20;
        bool nearBottomLeft = pos.X <= 20 && pos.Y >= h - 20;
        bool nearTopRight = pos.X >= w - 20 && pos.Y <= 20;
        bool nearTopLeft = pos.X <= 20 && pos.Y <= 20;

        if (nearBottomRight || nearBottomLeft || nearTopRight || nearTopLeft)
        {
            _isResizing = true;
            _resizeStartPoint = new PixelPoint((int)point.Position.X, (int)point.Position.Y);
            _resizeStartSize = new Size(Width, Height);
            e.Handled = true;
        }
        else
        {
            BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing || DataContext is not OverlayViewModel vm) return;

        var point = e.GetCurrentPoint(this);
        int dx = (int)point.Position.X - _resizeStartPoint.X;
        int dy = (int)point.Position.Y - _resizeStartPoint.Y;

        double newWidth = Math.Max(MinWidth, Math.Min(MaxWidth, _resizeStartSize.Width + dx));
        double newHeight = Math.Max(MinHeight, Math.Min(MaxHeight, _resizeStartSize.Height + dy));

        vm.WindowWidth = newWidth;
        vm.WindowHeight = newHeight;
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isResizing = false;
    }

    private void OnNeonTick(object? sender, ElapsedEventArgs e)
    {
        _hue += 1.5;
        if (_hue >= 360) _hue -= 360;

        var color = FromHsl(_hue, 1.0, 0.55);
        var glowColor = Color.FromArgb(64, color.R, color.G, color.B);

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_neonBorder != null)
            {
                _neonBorder.BorderBrush = new SolidColorBrush(color);
                _neonBorder.BoxShadow = new BoxShadows(new BoxShadow
                {
                    Color = glowColor,
                    Blur = 15,
                    OffsetX = 0,
                    OffsetY = 0,
                    IsInset = false
                });
            }
        });
    }

    private void UpdatePosition(OverlayPosition position)
    {
        var screen = Screens.ScreenFromWindow(this);
        if (screen == null) return;

        double w = Width;
        double h = Height;
        double sw = screen.WorkingArea.Width;
        double sh = screen.WorkingArea.Height;
        double margin = 10;

        (double left, double top) = position switch
        {
            OverlayPosition.TopLeft => (margin, margin),
            OverlayPosition.TopRight => (sw - w - margin, margin),
            OverlayPosition.BottomLeft => (margin, sh - h - margin),
            OverlayPosition.BottomRight => (sw - w - margin, sh - h - margin),
            OverlayPosition.Center => ((sw - w) / 2, (sh - h) / 2),
            _ => (sw - w - margin, margin)
        };

        Position = new PixelPoint((int)left, (int)top);
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1.0 - Math.Abs(2.0 * lightness - 1.0)) * saturation;
        double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
        double m = lightness - c / 2.0;

        double r, g, b;
        if (hue < 60) { r = c; g = x; b = 0; }
        else if (hue < 120) { r = x; g = c; b = 0; }
        else if (hue < 180) { r = 0; g = c; b = x; }
        else if (hue < 240) { r = 0; g = x; b = c; }
        else if (hue < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromArgb(255,
            (byte)((r + m) * 255),
            (byte)((g + m) * 255),
            (byte)((b + m) * 255));
    }
}
