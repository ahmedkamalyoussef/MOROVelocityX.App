using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using MOROVelocityX.Services;
using System;

namespace MOROVelocityX.ViewModels;

public enum ApplicationStatus
{
    Ready,
    Running,
    Stopped
}

public partial class MainWindowViewModel : ViewModelBase
{
    private string _triggerKey = "F1";
    private string _clickKey = "Mouse1";
    private bool _isToggleMode = true;
    private bool _isHoldMode = false;
    private int _cps = 10;
    private ApplicationStatus _status = ApplicationStatus.Ready;
    private bool _isCapturingTriggerKey = false;
    private bool _isCapturingClickKey = false;

    public string TriggerKey
    {
        get => _triggerKey;
        set
        {
            if (SetProperty(ref _triggerKey, value))
            {
                _globalHotkeyService.RegisterHotkey(value);
            }
        }
    }

    public string ClickKey
    {
        get => _clickKey;
        set
        {
            if (SetProperty(ref _clickKey, value))
            {
                UpdateMacroConfiguration();
            }
        }
    }

    public bool IsToggleMode
    {
        get => _isToggleMode;
        set
        {
            if (SetProperty(ref _isToggleMode, value))
            {
                if (value) IsHoldMode = false;
                UpdateMacroConfiguration();
            }
        }
    }

    public bool IsHoldMode
    {
        get => _isHoldMode;
        set
        {
            if (SetProperty(ref _isHoldMode, value))
            {
                if (value) IsToggleMode = false;
                UpdateMacroConfiguration();
            }
        }
    }

    public int CPS
    {
        get => _cps;
        set
        {
            if (SetProperty(ref _cps, value))
            {
                UpdateMacroConfiguration();
            }
        }
    }

    public ApplicationStatus Status
    {
        get => _status;
        set
        {
            SetProperty(ref _status, value);
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public string StatusText => Status.ToString();

    public bool IsCapturingTriggerKey
    {
        get => _isCapturingTriggerKey;
        set => SetProperty(ref _isCapturingTriggerKey, value);
    }

    public bool IsCapturingClickKey
    {
        get => _isCapturingClickKey;
        set => SetProperty(ref _isCapturingClickKey, value);
    }

    public ICommand CaptureTriggerKeyCommand { get; }
    public ICommand CaptureClickKeyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly MacroService _macroService;

    public MainWindowViewModel(GlobalHotkeyService globalHotkeyService, MacroService macroService)
    {
        _globalHotkeyService = globalHotkeyService;
        _macroService = macroService;
        CaptureTriggerKeyCommand = new RelayCommand(CaptureTriggerKey);
        CaptureClickKeyCommand = new RelayCommand(CaptureClickKey);
        StartCommand = new RelayCommand(Start, CanStart);
        StopCommand = new RelayCommand(Stop, CanStop);

        // Subscribe to global hotkey events
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        
        // Subscribe to macro service events
        _macroService.MacroStatusChanged += OnMacroStatusChanged;
        _macroService.MacroError += OnMacroError;
        
        // Configure macro service with initial settings
        UpdateMacroConfiguration();
    }

    private bool CanStart()
    {
        return _macroService.IsInputSimulationSupported && !_macroService.IsRunning;
    }

    private bool CanStop()
    {
        return _macroService.IsInputSimulationSupported && _macroService.IsRunning;
    }

    private void OnGlobalHotkeyPressed(object? sender, string key)
    {
        // Handle global hotkey press (trigger key)
        if (key == TriggerKey)
        {
            if (IsToggleMode)
            {
                _macroService.StartToggleMode();
            }
            else
            {
                _macroService.StartHoldMode();
            }
        }
    }

    private void OnMacroStatusChanged(object? sender, bool isRunning)
    {
        if (isRunning)
        {
            Status = ApplicationStatus.Running;
        }
        else
        {
            Status = ApplicationStatus.Stopped;
        }
        
        // Notify command execution state changes
        ((RelayCommand)StartCommand).NotifyCanExecuteChanged();
        ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
    }

    private void OnMacroError(object? sender, string error)
    {
        // Handle macro errors - could show in UI
        try
        {
            Status = ApplicationStatus.Stopped;
        }
        catch
        {
            // Ignore errors in status update
        }
    }

    private void UpdateMacroConfiguration()
    {
        _macroService.Configure(IsToggleMode, CPS, ClickKey);
    }

    private void CaptureTriggerKey()
    {
        IsCapturingTriggerKey = true;
    }

    private void CaptureClickKey()
    {
        IsCapturingClickKey = true;
    }

    public void OnKeyPressed(string key)
    {
        if (IsCapturingTriggerKey)
        {
            TriggerKey = key;
            IsCapturingTriggerKey = false;
        }
        else if (IsCapturingClickKey)
        {
            ClickKey = key;
            IsCapturingClickKey = false;
        }
    }

    private void Start()
    {
        try
        {
            if (IsToggleMode)
            {
                _macroService.StartToggleMode();
            }
            else
            {
                _macroService.StartHoldMode();
            }
        }
        catch
        {
            // Handle errors silently
            Status = ApplicationStatus.Stopped;
        }
    }

    private void Stop()
    {
        _macroService.Stop();
    }
}
