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
    // ---- Macro 1 ----
    private string _triggerKey1 = "F1";
    private string _clickKey1 = "Mouse1";
    private bool _isToggleMode1 = true;
    private bool _isHoldMode1 = false;
    private int _cpsMin1 = 8;
    private int _cpsMax1 = 12;

    // ---- Macro 2 ----
    private string _triggerKey2 = "F3";
    private string _clickKey2 = "Mouse2";
    private bool _isToggleMode2 = true;
    private bool _isHoldMode2 = false;
    private int _cpsMin2 = 8;
    private int _cpsMax2 = 12;

    // ---- Shared ----
    private string _overlayToggleKey = "F2";
    private ApplicationStatus _status = ApplicationStatus.Ready;
    private bool _isCapturingField = false;
    private string? _capturingFieldName;
    private bool _isArmed = false;
    private string _licenseStatusText = "Active";
    private string _licenseTypeText = string.Empty;
    private string _licenseExpiryText = "Never";

    // ---- License display ----
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

    // =========================================================================
    // Macro 1 properties
    // =========================================================================
    public string TriggerKey1
    {
        get => _triggerKey1;
        set
        {
            if (SetProperty(ref _triggerKey1, value))
                ReRegisterAllHotkeys();
        }
    }

    public string ClickKey1
    {
        get => _clickKey1;
        set
        {
            if (SetProperty(ref _clickKey1, value))
                UpdateMacro1Configuration();
        }
    }

    public bool IsToggleMode1
    {
        get => _isToggleMode1;
        set
        {
            if (SetProperty(ref _isToggleMode1, value))
            {
                if (value) IsHoldMode1 = false;
                UpdateMacro1Configuration();
            }
        }
    }

    public bool IsHoldMode1
    {
        get => _isHoldMode1;
        set
        {
            if (SetProperty(ref _isHoldMode1, value))
            {
                if (value) IsToggleMode1 = false;
                UpdateMacro1Configuration();
            }
        }
    }

    public int CpsMin1
    {
        get => _cpsMin1;
        set
        {
            int clamped = Math.Clamp(value, 1, 500);
            if (clamped > _cpsMax1) clamped = _cpsMax1;
            if (SetProperty(ref _cpsMin1, clamped))
                UpdateMacro1Configuration();
        }
    }

    public int CpsMax1
    {
        get => _cpsMax1;
        set
        {
            int clamped = Math.Clamp(value, 1, 500);
            if (clamped < _cpsMin1) clamped = _cpsMin1;
            if (SetProperty(ref _cpsMax1, clamped))
                UpdateMacro1Configuration();
        }
    }

    // =========================================================================
    // Macro 2 properties
    // =========================================================================
    public string TriggerKey2
    {
        get => _triggerKey2;
        set
        {
            if (SetProperty(ref _triggerKey2, value))
                ReRegisterAllHotkeys();
        }
    }

    public string ClickKey2
    {
        get => _clickKey2;
        set
        {
            if (SetProperty(ref _clickKey2, value))
                UpdateMacro2Configuration();
        }
    }

    public bool IsToggleMode2
    {
        get => _isToggleMode2;
        set
        {
            if (SetProperty(ref _isToggleMode2, value))
            {
                if (value) IsHoldMode2 = false;
                UpdateMacro2Configuration();
            }
        }
    }

    public bool IsHoldMode2
    {
        get => _isHoldMode2;
        set
        {
            if (SetProperty(ref _isHoldMode2, value))
            {
                if (value) IsToggleMode2 = false;
                UpdateMacro2Configuration();
            }
        }
    }

    public int CpsMin2
    {
        get => _cpsMin2;
        set
        {
            int clamped = Math.Clamp(value, 1, 500);
            if (clamped > _cpsMax2) clamped = _cpsMax2;
            if (SetProperty(ref _cpsMin2, clamped))
                UpdateMacro2Configuration();
        }
    }

    public int CpsMax2
    {
        get => _cpsMax2;
        set
        {
            int clamped = Math.Clamp(value, 1, 500);
            if (clamped < _cpsMin2) clamped = _cpsMin2;
            if (SetProperty(ref _cpsMax2, clamped))
                UpdateMacro2Configuration();
        }
    }

    // =========================================================================
    // Shared properties
    // =========================================================================
    public string OverlayToggleKey
    {
        get => _overlayToggleKey;
        set
        {
            if (SetProperty(ref _overlayToggleKey, value))
                ReRegisterAllHotkeys();
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

    /// <summary>
    /// True when the UI is waiting for the user to press a key to assign.
    /// </summary>
    public bool IsCapturingField
    {
        get => _isCapturingField;
        set => SetProperty(ref _isCapturingField, value);
    }

    /// <summary>
    /// Which field is being captured — displayed in the UI so the user knows
    /// what they're assigning. E.g. "Macro 1 Trigger Key".
    /// </summary>
    public string? CapturingFieldName
    {
        get => _capturingFieldName;
        set => SetProperty(ref _capturingFieldName, value);
    }

    // ---- Commands ----
    public ICommand CaptureTriggerKey1Command { get; }
    public ICommand CaptureClickKey1Command { get; }
    public ICommand CaptureTriggerKey2Command { get; }
    public ICommand CaptureClickKey2Command { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    // ---- Services ----
    private readonly GlobalHotkeyService _globalHotkeyService;
    private readonly MacroService _macroService1;
    private readonly MacroService _macroService2;
    private readonly OverlayViewModel _overlayViewModel;
    private readonly LicenseService _licenseService;

    // Tracks which field is being captured: "TriggerKey1", "ClickKey1", etc.
    private string? _capturingTarget;

    public MainWindowViewModel(
        GlobalHotkeyService globalHotkeyService,
        MacroService macroService1,
        MacroService macroService2,
        OverlayViewModel overlayViewModel,
        LicenseService licenseService,
        LicenseValidationResult licenseValidation)
    {
        _globalHotkeyService = globalHotkeyService;
        _macroService1 = macroService1;
        _macroService2 = macroService2;
        _overlayViewModel = overlayViewModel;
        _licenseService = licenseService;
        ApplyLicenseValidation(licenseValidation);

        CaptureTriggerKey1Command = new RelayCommand(() => BeginCapture("TriggerKey1", "Macro 1 Trigger Key"));
        CaptureClickKey1Command = new RelayCommand(() => BeginCapture("ClickKey1", "Macro 1 Click Key"));
        CaptureTriggerKey2Command = new RelayCommand(() => BeginCapture("TriggerKey2", "Macro 2 Trigger Key"));
        CaptureClickKey2Command = new RelayCommand(() => BeginCapture("ClickKey2", "Macro 2 Click Key"));

        StartCommand = new RelayCommand(Start, CanStart);
        StopCommand = new RelayCommand(Stop, CanStop);

        ReRegisterAllHotkeys();
        _globalHotkeyService.HotkeyPressed += OnGlobalHotkeyPressed;
        _globalHotkeyService.HotkeyReleased += OnGlobalHotkeyReleased;

        _macroService1.MacroStatusChanged += OnMacroStatusChanged;
        _macroService1.MacroError += OnMacroError;
        _macroService2.MacroStatusChanged += OnMacroStatusChanged;
        _macroService2.MacroError += OnMacroError;

        UpdateMacro1Configuration();
        UpdateMacro2Configuration();
    }

    // =========================================================================
    // Hotkey registration — always rebuild from scratch so we don't leak stale
    // keys when the user re-assigns triggers.
    // =========================================================================
    private void ReRegisterAllHotkeys()
    {
        _globalHotkeyService.UnregisterHotkey();
        _globalHotkeyService.RegisterHotkey(_triggerKey1);
        _globalHotkeyService.RegisterAdditionalHotkey(_triggerKey2);
        _globalHotkeyService.RegisterAdditionalHotkey(_overlayToggleKey);
    }

    // =========================================================================
    // CanExecute
    // =========================================================================
    private bool CanStart()
    {
        return _macroService1.IsInputSimulationSupported
               && !_isArmed
               && _licenseService.ValidateOnStartup().State == LicenseState.Active;
    }

    private bool CanStop()
    {
        return _macroService1.IsInputSimulationSupported && _isArmed;
    }

    // =========================================================================
    // Hotkey handlers — dispatch to the correct macro
    // =========================================================================
    private void OnGlobalHotkeyPressed(object? sender, string key)
    {
        if (!_isArmed) return;

        if (key == _overlayToggleKey)
        {
            _overlayViewModel.ToggleVisibility();
            return;
        }

        if (key == TriggerKey1)
        {
            if (IsToggleMode1)
                _macroService1.StartToggleMode();
            else
                _macroService1.StartHoldMode();
        }

        if (key == TriggerKey2)
        {
            if (IsToggleMode2)
                _macroService2.StartToggleMode();
            else
                _macroService2.StartHoldMode();
        }
    }

    private void OnGlobalHotkeyReleased(object? sender, string key)
    {
        if (!_isArmed) return;

        if (key == TriggerKey1 && IsHoldMode1)
            _macroService1.StopHoldMode();

        if (key == TriggerKey2 && IsHoldMode2)
            _macroService2.StopHoldMode();
    }

    // =========================================================================
    // Status / Error
    // =========================================================================
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

    // =========================================================================
    // Configuration push
    // =========================================================================
    private void UpdateMacro1Configuration()
    {
        _macroService1.Configure(IsToggleMode1, CpsMin1, CpsMax1, ClickKey1);
    }

    private void UpdateMacro2Configuration()
    {
        _macroService2.Configure(IsToggleMode2, CpsMin2, CpsMax2, ClickKey2);
    }

    // =========================================================================
    // Key capture — generic for all 4 fields
    // =========================================================================
    private void BeginCapture(string target, string displayName)
    {
        _capturingTarget = target;
        CapturingFieldName = displayName;
        IsCapturingField = true;
    }

    public void OnKeyPressed(string key)
    {
        if (!IsCapturingField || _capturingTarget == null) return;

        switch (_capturingTarget)
        {
            case "TriggerKey1": TriggerKey1 = key; break;
            case "ClickKey1":   ClickKey1 = key;   break;
            case "TriggerKey2": TriggerKey2 = key; break;
            case "ClickKey2":   ClickKey2 = key;   break;
        }

        IsCapturingField = false;
        _capturingTarget = null;
        CapturingFieldName = null;
    }

    public void OnKeyReleased(string key)
    {
    }

    // =========================================================================
    // Start / Stop
    // =========================================================================
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
        _macroService1.Stop();
        _macroService2.Stop();
        Status = ApplicationStatus.Ready;
        ((RelayCommand)StartCommand).NotifyCanExecuteChanged();
        ((RelayCommand)StopCommand).NotifyCanExecuteChanged();
    }

    private bool ShouldSuppressTriggerKey(string key) =>
        _isArmed && (key == TriggerKey1 || key == TriggerKey2);

    private void ApplyLicenseValidation(LicenseValidationResult validation)
    {
        LicenseStatusText = validation.State.ToString();
        LicenseTypeText = validation.License?.Type.ToString() ?? "None";
        LicenseExpiryText = validation.License?.ExpiresAtUtc.HasValue == true
            ? validation.License.ExpiresAtUtc!.Value.ToLocalTime().ToString("g")
            : "Never";
    }
}
