using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MOROVelocityX.Services;

namespace MOROVelocityX.ViewModels;

public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Center
}

public class OverlaySettings
{
    public bool IsVisible { get; set; } = true;
    public OverlayPosition Position { get; set; } = OverlayPosition.TopRight;
    public double Width { get; set; } = 220;
    public double Height { get; set; } = 120;
    public double Opacity { get; set; } = 0.85;
}

public class OverlayViewModel : INotifyPropertyChanged
{
    private readonly StatsService _statsService;
    private OverlaySettings _settings;
    private bool _isVisible;
    private bool _isInteractive;
    private OverlayPosition _position;
    private string _cpsText = "0.0";
    private string _fpsText = "0";
    private string _totalClicksText = "0";
    private double _opacity;
    private double _windowWidth;
    private double _windowHeight;
    private static readonly string SettingsPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "overlay_settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                _settings.IsVisible = value;
                SaveSettings();
            }
        }
    }

    public bool IsInteractive
    {
        get => _isInteractive;
        set => SetProperty(ref _isInteractive, value);
    }

    public OverlayPosition Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                _settings.Position = value;
                SaveSettings();
            }
        }
    }

    public string CpsText
    {
        get => _cpsText;
        private set => SetProperty(ref _cpsText, value);
    }

    public string FpsText
    {
        get => _fpsText;
        private set => SetProperty(ref _fpsText, value);
    }

    public string TotalClicksText
    {
        get => _totalClicksText;
        private set => SetProperty(ref _totalClicksText, value);
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            if (SetProperty(ref _opacity, value))
            {
                _settings.Opacity = value;
                SaveSettings();
            }
        }
    }

    public double WindowWidth
    {
        get => _windowWidth;
        set
        {
            if (SetProperty(ref _windowWidth, value))
            {
                _settings.Width = value;
                SaveSettings();
            }
        }
    }

    public double WindowHeight
    {
        get => _windowHeight;
        set
        {
            if (SetProperty(ref _windowHeight, value))
            {
                _settings.Height = value;
                SaveSettings();
            }
        }
    }

    public ICommand SetPositionTopLeftCommand { get; }
    public ICommand SetPositionTopRightCommand { get; }
    public ICommand SetPositionBottomLeftCommand { get; }
    public ICommand SetPositionBottomRightCommand { get; }
    public ICommand SetPositionCenterCommand { get; }

    public OverlayViewModel(StatsService statsService)
    {
        _statsService = statsService;
        _settings = LoadSettings();
        _isVisible = _settings.IsVisible;
        _isInteractive = false;
        _position = _settings.Position;
        _opacity = _settings.Opacity;
        _windowWidth = _settings.Width;
        _windowHeight = _settings.Height;

        SetPositionTopLeftCommand = new RelayCommand(() => Position = OverlayPosition.TopLeft);
        SetPositionTopRightCommand = new RelayCommand(() => Position = OverlayPosition.TopRight);
        SetPositionBottomLeftCommand = new RelayCommand(() => Position = OverlayPosition.BottomLeft);
        SetPositionBottomRightCommand = new RelayCommand(() => Position = OverlayPosition.BottomRight);
        SetPositionCenterCommand = new RelayCommand(() => Position = OverlayPosition.Center);

        _statsService.StatsUpdated += OnStatsUpdated;
    }

    public void ToggleVisibility()
    {
        IsVisible = !IsVisible;
    }

    public void ToggleInteractive()
    {
        IsInteractive = !IsInteractive;
    }

    private void OnStatsUpdated()
    {
        CpsText = _statsService.CurrentCps.ToString("F1");
        FpsText = _statsService.CurrentFps.ToString("F0");
        TotalClicksText = _statsService.TotalClicks.ToString();
    }

    private static OverlaySettings LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<OverlaySettings>(json) ?? new OverlaySettings();
            }
        }
        catch { }
        return new OverlaySettings();
    }

    private void SaveSettings()
    {
        try
        {
            string json = JsonSerializer.Serialize(_settings, JsonOptions);
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
