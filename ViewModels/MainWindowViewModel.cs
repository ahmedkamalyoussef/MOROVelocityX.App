using System;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MOROVelocityX.Models;
using MOROVelocityX.Services;

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
    private string _overlayToggleKey = "F2";
    private bool _isToggleMode = true;
    private bool _isHoldMode = false;
    private int _cps = 10;
    private ApplicationStatus _status = ApplicationStatus.Ready;
    private bool _isCapturingTriggerKey = false;
    private bool _isCapturingClickKey = false;
    private bool _isArmed = false;
    private string _licenseStatusText = "Active";
    private string _licenseTypeText = string.Empty;
    private string _licenseExpiryText = "Never";

    public string LicenseStatusText
    {
        get => _licenseStatusText;
        private set => SetProperty(ref _licenseStatusText, value);
    }

    public string LicenseTypeText
    {
        get => _licenseTypeText;
        private set => SetProperty(ref _licenseTypeText, value);
    }

    public string LicenseExpiryText
    {
        get => _licenseExpiryText;
        private set => SetProperty(ref _licenseExpiryText, value);
    }

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

    public string OverlayToggleKey
    {
        get => _overlayToggleKey;
        set
        {
            if (SetProperty(ref _overlayToggleKey, value))
            {
                _globalHotkeyService.RegisterAdditionalHotkey(value);
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
    private readonly OverlayViewModel _overlayViewModel;
    private readonly LicenseService _licenseService;

    public MainWindowViewModel(
        GlobalHotkeyService globalHotkeyService,
        MacroService macroService,
        OverlayViewModel overlayViewModel,
        LicenseService licenseService,
        LicenseValidationResult licenseValidation)
    {
        _globalHotkeyService = globalHotkeyService;
        _macroService = macroService;
        _overlayViewModel = overlayViewModel;
        _licenseService = licenseService;
        ApplyLicenseValidation(licenseValidation);
        CaptureTriggerKeyCommand = new RelayCommand(CaptureTriggerKey);
        CaptureClickKeyCommand = new RelayCommand(CaptureClickKey);
        StartCommand = new RelayCommand(Start, CanStart);
        StopCommand = new RelayCommand(Stop, CanStop);

        _globalHotkeyService.RegisterHotkey(_triggerKey);
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        _globalHotkeyService.HotkeyReleased += OnGlobalHotkeyReleased;

        _macroService.MacroStatusChanged += OnMacroStatusChanged;
        _macroService.MacroError += OnMacroError;

        UpdateMacroConfiguration();
    }

    private bool CanStart()
    {
        return _macroService.IsInputSimulationSupported
               && !_isArmed
               && _licenseService.ValidateOnStartup().State == LicenseState.Active;
    }

    private bool CanStop()
    {
        return _macroService.IsInputSimulationSupported && _isArmed;
    }

    private void OnGlobalHotkeyPressed(object? sender, string key)
    {
        if (key == TriggerKey && _isArmed)
        {
            if (IsToggleMode)
                _macroService.StartToggleMode();
            else
                _macroService.StartHoldMode();
        }
    }

    private void OnGlobalHotkeyReleased(object? sender, string key)
    {
        if (key == TriggerKey && _isArmed && IsHoldMode)
        {
            _macroService.StopHoldMode();
        }
    }

    private void OnMacroStatusChanged(object? sender, bool isRunning)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ((RelayCommand)StartCommand).NotifyCanExecuteChanged();
            ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
        });
    }

    private void OnMacroError(object? sender, string error)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Status = ApplicationStatus.Stopped;
        });
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

    public void OnKeyReleased(string key)
    {
    }

    private void Start()
    {
        var validation = _licenseService.ValidateOnStartup();
        ApplyLicenseValidation(validation);
        if (validation.State != LicenseState.Active)
        {
            Status = ApplicationStatus.Stopped;
            return;
        }

        _isArmed = true;
        _globalHotkeyService.ShouldSuppressKey = ShouldSuppressTriggerKey;
        _globalHotkeyService.SetKeyboardGrab(true);
        Status = ApplicationStatus.Running;
        ((RelayCommand)StartCommand).NotifyCanExecuteChanged();
        ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
    }

    public void Stop()
    {
        _isArmed = false;
        _globalHotkeyService.ShouldSuppressKey = null;
        _globalHotkeyService.SetKeyboardGrab(false);
        _macroService.Stop();
        Status = ApplicationStatus.Ready;
        ((RelayCommand)StartCommand).NotifyCanExecuteChanged();
        ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
    }

    private bool ShouldSuppressTriggerKey(string key) => _isArmed && key == TriggerKey;

    private void ApplyLicenseValidation(LicenseValidationResult validation)
    {
        LicenseStatusText = validation.State.ToString();
        LicenseTypeText = validation.License?.Type.ToString() ?? "None";
        LicenseExpiryText = validation.License?.ExpiresAtUtc.HasValue == true
            ? validation.License.ExpiresAtUtc!.Value.ToLocalTime().ToString("g")
            : "Never";
    }
}
