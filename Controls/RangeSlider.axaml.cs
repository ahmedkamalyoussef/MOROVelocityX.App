using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Input;

namespace MOROVelocityX.Controls;

public partial class RangeSlider : UserControl
{
    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Minimum), 1);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(Maximum), 500);

    public static readonly StyledProperty<double> LowerValueProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(LowerValue), 8,
            coerce: CoerceLowerValue);

    public static readonly StyledProperty<double> UpperValueProperty =
        AvaloniaProperty.Register<RangeSlider, double>(nameof(UpperValue), 12,
            coerce: CoerceUpperValue);

    private Slider? _minSlider;
    private Slider? _maxSlider;
    private TextBlock? _minText;
    private TextBlock? _maxText;
    private Border? _rangeHighlight;
    private Canvas? _rangeCanvas;
    private bool _updating;

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double LowerValue
    {
        get => GetValue(LowerValueProperty);
        set => SetValue(LowerValueProperty, value);
    }

    public double UpperValue
    {
        get => GetValue(UpperValueProperty);
        set => SetValue(UpperValueProperty, value);
    }

    public RangeSlider()
    {
        AvaloniaXamlLoader.Load(this);

        _minSlider = this.FindControl<Slider>("MinSlider");
        _maxSlider = this.FindControl<Slider>("MaxSlider");
        _minText = this.FindControl<TextBlock>("MinText");
        _maxText = this.FindControl<TextBlock>("MaxText");
        _rangeHighlight = this.FindControl<Border>("RangeHighlight");
        _rangeCanvas = this.FindControl<Canvas>("RangeCanvas");
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        SyncSlidersFromProperties();
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);

        if (_minSlider != null)
        {
            _minSlider.Minimum = Minimum;
            _minSlider.Maximum = Maximum;
            _minSlider.Value = LowerValue;
            _minSlider.PropertyChanged += OnMinSliderChanged;
        }

        if (_maxSlider != null)
        {
            _maxSlider.Minimum = Minimum;
            _maxSlider.Maximum = Maximum;
            _maxSlider.Value = UpperValue;
            _maxSlider.PropertyChanged += OnMaxSliderChanged;
        }

        UpdateDisplay();
        UpdateRangeHighlight();

        this.PointerMoved += OnPointerMoved;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_minSlider == null || _maxSlider == null) return;
        
        double range = Maximum - Minimum;
        if (range <= 0) return;

        var pos = e.GetPosition(this);
        double width = this.Bounds.Width > 0 ? this.Bounds.Width : 380;
        
        double minPct = (LowerValue - Minimum) / range;
        double maxPct = (UpperValue - Minimum) / range;
        
        double minThumbX = minPct * width;
        double maxThumbX = maxPct * width;
        
        if (Math.Abs(pos.X - minThumbX) < Math.Abs(pos.X - maxThumbX))
        {
            _minSlider.ZIndex = 1;
            _maxSlider.ZIndex = 0;
        }
        else
        {
            _minSlider.ZIndex = 0;
            _maxSlider.ZIndex = 1;
        }
    }

    private static double CoerceLowerValue(AvaloniaObject obj, double value)
    {
        var rs = (RangeSlider)obj;
        value = Math.Clamp(value, rs.Minimum, rs.Maximum);
        if (value > rs.UpperValue) value = rs.UpperValue;
        return value;
    }

    private static double CoerceUpperValue(AvaloniaObject obj, double value)
    {
        var rs = (RangeSlider)obj;
        value = Math.Clamp(value, rs.Minimum, rs.Maximum);
        if (value < rs.LowerValue) value = rs.LowerValue;
        return value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_updating) return;

        if (change.Property == LowerValueProperty || change.Property == UpperValueProperty)
        {
            SyncSlidersFromProperties();
            UpdateDisplay();
            UpdateRangeHighlight();
        }
        else if (change.Property == MinimumProperty || change.Property == MaximumProperty)
        {
            if (_minSlider != null)
            {
                _minSlider.Minimum = Minimum;
                _minSlider.Maximum = Maximum;
            }
            if (_maxSlider != null)
            {
                _maxSlider.Minimum = Minimum;
                _maxSlider.Maximum = Maximum;
            }
            UpdateRangeHighlight();
        }
    }

    private void SyncSlidersFromProperties()
    {
        _updating = true;
        try
        {
            if (_minSlider != null) _minSlider.Value = LowerValue;
            if (_maxSlider != null) _maxSlider.Value = UpperValue;
        }
        finally { _updating = false; }
    }

    private void OnMinSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_updating || e.Property != Slider.ValueProperty || _minSlider == null) return;

        _updating = true;
        try
        {
            double val = _minSlider.Value;
            if (val > UpperValue)
            {
                _minSlider.Value = UpperValue;
                val = UpperValue;
            }
            LowerValue = val;
            UpdateDisplay();
            UpdateRangeHighlight();
        }
        finally { _updating = false; }
    }

    private void OnMaxSliderChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_updating || e.Property != Slider.ValueProperty || _maxSlider == null) return;

        _updating = true;
        try
        {
            double val = _maxSlider.Value;
            if (val < LowerValue)
            {
                _maxSlider.Value = LowerValue;
                val = LowerValue;
            }
            UpperValue = val;
            UpdateDisplay();
            UpdateRangeHighlight();
        }
        finally { _updating = false; }
    }

    private void UpdateDisplay()
    {
        if (_minText != null) _minText.Text = ((int)LowerValue).ToString();
        if (_maxText != null) _maxText.Text = ((int)UpperValue).ToString();
    }

    private void UpdateRangeHighlight()
    {
        if (_rangeHighlight == null || _rangeCanvas == null) return;

        double range = Maximum - Minimum;
        if (range <= 0) return;

        double canvasWidth = _rangeCanvas.Bounds.Width;
        if (canvasWidth <= 0) canvasWidth = 380; // fallback

        double leftPct = (LowerValue - Minimum) / range;
        double rightPct = (UpperValue - Minimum) / range;

        Canvas.SetLeft(_rangeHighlight, leftPct * canvasWidth);
        _rangeHighlight.Width = Math.Max(0, (rightPct - leftPct) * canvasWidth);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRangeHighlight();
    }
}
